using System.Globalization;
using System.Text.Json;
using Arkko.Core;

namespace Arkko.Hook;

/// <summary>Statusline de Claude Code: pinta la línea configurada en el editor y guarda límites y estado de sesión.</summary>
/// <remarks>
/// Es la única vía por la que Claude Code entrega los límites de uso de la cuenta, así que aquí se
/// guardan en data/limits.json para la ventana. Se leen todas las claves de rate_limits, no una
/// lista fija: si el plan trae un límite propio de un modelo, aparece sin tocar código.
/// </remarks>
internal static class Statusline
{
    /// <summary>Procesa el JSON de la statusline y escribe su salida.</summary>
    public static int Run(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            IO.Write("");
            return 0;
        }

        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        JsonStore.WriteText(AppPaths.StatuslineRawFile, raw);

        var sessionId = Json.Str(root, "session_id");
        if (!string.IsNullOrEmpty(sessionId)) JsonStore.WriteText(SessionRawFile(sessionId), raw);

        var limits = MergedLimits(ReadLimits(root));
        if (limits.Count > 0)
        {
            var before = JsonStore.Read<LimitsSnapshot>(AppPaths.LimitsFile);
            JsonStore.Write(AppPaths.LimitsFile, new LimitsSnapshot { UpdatedUtc = DateTime.UtcNow, Limits = limits });
            // También la primera vez de cada día, para que cada fichero del histórico arranque con su punto de partida.
            var history = AppPaths.LimitsHistoryFile(DateTime.Now);
            if (LimitsSample.Changed(before?.Limits, limits) || !File.Exists(history))
                JsonStore.AppendLine(history, new LimitsSample { WhenUtc = DateTime.UtcNow, Limits = limits });
        }

        var model = Json.Str(root, "model", "display_name") ?? Json.Str(root, "model", "id") ?? "?";
        var cwd = Json.Str(root, "workspace", "current_dir") ?? Json.Str(root, "cwd");
        var ctx = Json.Num(root, "context_window", "used_percentage");
        var cost = Json.Num(root, "cost", "total_cost_usd");
        var folder = cwd is null ? null : Path.GetFileName(cwd.TrimEnd('/', '\\'));
        var repo = Worktree.RepoOf(cwd);

        if (!string.IsNullOrEmpty(sessionId))
        {
            SessionStore.Update(sessionId, s =>
            {
                s.Model = model;
                s.ContextPct = ctx;
                s.ContextSize = (long?)Json.Num(root, "context_window", "context_window_size");
                s.CostUsd = cost;
                s.Effort = Json.Str(root, "effort", "level") ?? s.Effort;
                s.SessionName = Json.Str(root, "session_name") ?? s.SessionName;
                if (cwd is not null)
                {
                    s.Cwd = cwd;
                    s.Project = folder;
                    s.Repo = repo;
                }
                s.TranscriptPath ??= Json.Str(root, "transcript_path");

                // Sesiones abiertas antes de instalar los hooks: sin SessionStart, el proceso se apunta aquí.
                if (s.ClaudePid is null && ClaudeProcess.FindAncestor() is { } found)
                    (s.ClaudePid, s.ClaudeStartUtc) = (found.Pid, found.StartUtc);
            });
        }

        var data = new TerminalData(
            model,
            repo ?? folder,
            Json.Str(root, "session_name"),
            ctx,
            Json.Str(root, "effort", "level"),
            limits,
            cost,
            repo is null ? null : folder);
        var layout = TerminalLayout.Load();
        IO.Write(layout.ShowStatusline ? TerminalRenderer.Ansi(TerminalRenderer.Segments(layout, data)) + "\n" : "");
        return 0;
    }

    private static readonly TimeSpan RecentSession = TimeSpan.FromHours(12);
    private static readonly TimeSpan KeepRaw = TimeSpan.FromDays(7);

    /// <summary>Carpeta con el último JSON de statusline de cada sesión.</summary>
    private static string RawDir => Path.Combine(AppPaths.Data, "statusline");

    /// <summary>Fichero con el último JSON de statusline de una sesión.</summary>
    private static string SessionRawFile(string sessionId)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(sessionId.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return Path.Combine(RawDir, name + ".json");
    }

    /// <summary>Límites de la sesión actual completados con los que solo envían otras sesiones recientes.</summary>
    /// <remarks>
    /// Claude Code no manda los mismos límites a todas las sesiones: un límite propio de un modelo
    /// (el semanal de Fable, por ejemplo) puede llegar solo a las sesiones de ese modelo. Cada clave
    /// se toma de la sesión que la envió más recientemente, entre las activas en las últimas 12 horas;
    /// la fecha de modificación del fichero hace de marca de tiempo. De paso se borran los ficheros de
    /// sesiones de hace más de 7 días.
    /// </remarks>
    internal static List<LimitInfo> MergedLimits(List<LimitInfo> current)
    {
        var merged = current.ToDictionary(l => l.Key, StringComparer.Ordinal);
        if (!Directory.Exists(RawDir)) return current;

        var now = DateTime.UtcNow;
        foreach (var file in new DirectoryInfo(RawDir).EnumerateFiles("*.json").OrderByDescending(f => f.LastWriteTimeUtc))
        {
            var age = now - file.LastWriteTimeUtc;
            if (age > KeepRaw)
            {
                try { file.Delete(); } catch (IOException) { }
                continue;
            }
            if (age > RecentSession) continue;

            try
            {
                using var fs = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var doc = JsonDocument.Parse(fs);
                foreach (var limit in ReadLimits(doc.RootElement))
                    merged.TryAdd(limit.Key, limit);
            }
            catch (Exception ex) when (ex is IOException or JsonException) { }
        }

        return merged.Values.OrderBy(l => LimitLabels.Order(l.Key)).ThenBy(l => l.Key, StringComparer.Ordinal).ToList();
    }

    /// <summary>Extrae todos los límites de rate_limits, en orden de presentación.</summary>
    internal static List<LimitInfo> ReadLimits(JsonElement root)
    {
        var result = new List<LimitInfo>();
        if (!root.TryGetProperty("rate_limits", out var rl) || rl.ValueKind != JsonValueKind.Object) return result;

        foreach (var prop in rl.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Object) continue;
            var pct = Json.Num(prop.Value, "used_percentage") ?? Json.Num(prop.Value, "utilization");
            if (pct is null) continue;
            result.Add(new LimitInfo
            {
                Key = prop.Name,
                Label = LimitLabels.For(prop.Name),
                UsedPct = pct.Value,
                ResetsAtUtc = ResetTime(prop.Value),
            });
        }
        return result.OrderBy(l => LimitLabels.Order(l.Key)).ThenBy(l => l.Key, StringComparer.Ordinal).ToList();
    }

    /// <summary>Hora de reinicio de un límite, en segundos Unix o en ISO 8601.</summary>
    private static DateTime? ResetTime(JsonElement limit)
    {
        if (!limit.TryGetProperty("resets_at", out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var epoch))
            return DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;
        if (v.ValueKind == JsonValueKind.String && DateTime.TryParse(v.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var ts))
            return ts;
        return null;
    }
}
