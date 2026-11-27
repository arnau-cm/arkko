using System.Text.Json;
using Arkko.Core;

namespace Arkko.Hook;

/// <summary>Traduce cada evento de hook de Claude Code en un cambio de estado de la sesión.</summary>
internal static class HookHandler
{
    /// <summary>Procesa el JSON de un hook y escribe la respuesta que espera Claude Code.</summary>
    public static int Run(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        var sessionId = Json.Str(root, "session_id");
        var evt = Json.Str(root, "hook_event_name") ?? "";
        if (string.IsNullOrEmpty(sessionId))
        {
            IO.Write("{}");
            return 0;
        }

        var response = evt switch
        {
            "SessionStart" => SessionStart(sessionId, root),
            "UserPromptSubmit" => PromptSubmit(sessionId, root),
            "PreToolUse" => PreToolUse(sessionId, root),
            "PostToolUse" => PostToolUse(sessionId, root),
            "Notification" => Notification(sessionId, root),
            "Stop" => Stop(sessionId, root),
            "SessionEnd" => SessionEnd(sessionId),
            _ => null,
        };

        IO.Write(response ?? "{}");
        return 0;
    }

    /// <summary>Registra la sesión y el proceso de Claude Code que la aloja.</summary>
    private static string? SessionStart(string sessionId, JsonElement root)
    {
        var source = Json.Str(root, "source");
        SessionStore.Update(sessionId, s =>
        {
            Common(s, root);
            var found = ClaudeProcess.FindAncestor();
            if (found is not null) (s.ClaudePid, s.ClaudeStartUtc) = (found.Value.Pid, found.Value.StartUtc);

            // Una compactación ocurre en mitad del trabajo: no cambia el estado.
            if (source != "compact")
            {
                s.Status = SessionStatus.Idle;
                s.Detail = source == "resume" ? "Reanudada" : null;
            }
        });
        return null;
    }

    /// <summary>Marca la sesión en espera cuando Claude pregunta o presenta un plan.</summary>
    private static string? PreToolUse(string sessionId, JsonElement root)
    {
        var detail = Json.Str(root, "tool_name") switch
        {
            "AskUserQuestion" => "Te hace una pregunta",
            "ExitPlanMode" => "Plan pendiente de aprobar",
            _ => null,
        };
        return detail is null ? null : Set(sessionId, root, SessionStatus.Waiting, detail);
    }

    /// <summary>Vuelve a trabajando tras una espera y avisa si la herramienta ha movido un secreto.</summary>
    /// <remarks>Si no había espera ni secreto, no escribe nada: es el evento más frecuente.</remarks>
    private static string? PostToolUse(string sessionId, JsonElement root)
    {
        var fingerprints = new List<string>();
        var alert = Settings().SecretAlerts ? LiveSecrets.ForTool(root, new SettledSecrets(), fingerprints) : null;
        var current = JsonStore.Read<SessionState>(AppPaths.SessionFile(sessionId));
        if (alert is null && current is not null && current.Status == SessionStatus.Working) return null;
        SessionStore.Update(sessionId, s =>
        {
            Common(s, root);
            if (s.Status != SessionStatus.Working)
            {
                s.Status = SessionStatus.Working;
                s.Detail = null;
            }
            if (alert is not null) (s.SecretAlert, s.SecretAlertUtc, s.SecretAlertFingerprints) = (alert, DateTime.UtcNow, fingerprints);
        });
        return alert is null ? null : SystemMessage(LiveSecrets.Message(alert));
    }

    /// <summary>Marca la sesión en espera cuando Claude Code pide permiso o una respuesta.</summary>
    private static string? Notification(string sessionId, JsonElement root)
    {
        var type = Json.Str(root, "notification_type");
        var message = Json.Str(root, "message");
        var waits = type is "permission_prompt" or "elicitation_dialog" ||
                    (type is null && message is not null && message.Contains("permission", StringComparison.OrdinalIgnoreCase));
        if (!waits) return null;

        var detail = type == "elicitation_dialog" ? "Espera tu respuesta" : "Pide permiso";
        if (!string.IsNullOrWhiteSpace(message)) detail += ": " + Truncate(message, 90);
        return Set(sessionId, root, SessionStatus.Waiting, detail);
    }

    /// <summary>Marca la sesión trabajando y guarda el texto del prompt; si lleva un secreto, lo frena o avisa.</summary>
    /// <remarks>
    /// Frenado, el prompt no llega a Claude y la sesión sigue como estaba. Es lo único que se puede parar
    /// antes de enviarlo: la salida de una herramienta ya está en el contexto cuando el hook la ve.
    /// </remarks>
    private static string? PromptSubmit(string sessionId, JsonElement root)
    {
        var prompt = Json.Str(root, "prompt");
        var settings = Settings();
        var fingerprints = new List<string>();
        var kinds = settings.SecretAlerts || settings.BlockSecretPrompts ? LiveSecrets.Kinds(prompt, new SettledSecrets(), fingerprints) : [];
        if (kinds.Count > 0 && settings.BlockSecretPrompts)
        {
            return JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["decision"] = "block",
                ["reason"] = LiveSecrets.BlockReason(kinds),
            });
        }

        var alert = kinds.Count > 0 && settings.SecretAlerts ? LiveSecrets.ForPrompt(kinds) : null;
        SessionStore.Update(sessionId, s =>
        {
            Common(s, root);
            s.Status = SessionStatus.Working;
            s.Detail = null;
            s.LastPrompt = string.IsNullOrWhiteSpace(prompt) ? null : Truncate(prompt.Trim(), PromptMaxLength);
            if (alert is not null) (s.SecretAlert, s.SecretAlertUtc, s.SecretAlertFingerprints) = (alert, DateTime.UtcNow, fingerprints);
        });
        return alert is null ? null : SystemMessage(LiveSecrets.Message(alert));
    }

    /// <summary>Ajustes de la app; los de fábrica si aún no hay fichero o no se puede leer.</summary>
    private static AppSettings Settings() => JsonStore.Read<AppSettings>(AppPaths.AppSettingsFile) ?? new AppSettings();

    /// <summary>Respuesta del hook que solo enseña una línea en la sesión.</summary>
    private static string SystemMessage(string text) =>
        JsonSerializer.Serialize(new Dictionary<string, object> { ["systemMessage"] = text });

    /// <summary>Cierra el prompt: calcula su consumo, lo registra y lo muestra en Claude Code.</summary>
    private static string Stop(string sessionId, JsonElement root)
    {
        var closed = TurnClose.Close(sessionId, Json.Str(root, "transcript_path"), DateTime.UtcNow, StopRetries, common: s => Common(s, root));
        if (closed is not { Usage: { ByModel.Count: > 0 } usage }) return "{}";

        // El registro del prompt se guarda siempre; solo la línea de la terminal depende del editor.
        var layout = TerminalLayout.Load();
        var summary = layout.ShowSummary ? Summary(usage, layout) : "";
        if (summary.Length == 0) return "{}";

        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["systemMessage"] = summary,
            ["suppressOutput"] = true,
        });
    }

    /// <summary>Retira la sesión cerrada.</summary>
    private static string? SessionEnd(string sessionId)
    {
        SessionStore.Remove(sessionId);
        return null;
    }

    /// <summary>Cambia el estado de la sesión y rellena los datos comunes.</summary>
    private static string? Set(string sessionId, JsonElement root, SessionStatus status, string? detail)
    {
        SessionStore.Update(sessionId, s =>
        {
            Common(s, root);
            s.Status = status;
            s.Detail = detail;
        });
        return null;
    }

    /// <summary>Copia carpeta, transcript y proceso, presentes en todos los eventos.</summary>
    private static void Common(SessionState s, JsonElement root)
    {
        var cwd = Json.Str(root, "cwd");
        if (cwd is not null)
        {
            s.Cwd = cwd;
            s.Project = Path.GetFileName(cwd.TrimEnd('/', '\\'));
            s.Repo = Worktree.RepoOf(cwd);
        }
        s.TranscriptPath = Json.Str(root, "transcript_path") ?? s.TranscriptPath;

        // Si el hook ha llegado a ejecutarse, funciona: lo que la app vio fallar ya no vale.
        s.HookError = null;
        s.HookErrorUtc = null;

        if (s.ClaudePid is null)
        {
            var found = ClaudeProcess.FindAncestor();
            if (found is not null) (s.ClaudePid, s.ClaudeStartUtc) = (found.Value.Pid, found.Value.StartUtc);
        }
    }

    /// <summary>Línea de resumen del prompt, con las piezas y el orden elegidos en el editor.</summary>
    internal static string Summary(TurnUsage usage, TerminalLayout layout)
    {
        var limits = JsonStore.Read<LimitsSnapshot>(AppPaths.LimitsFile)?.Limits ?? [];
        return PromptSummary.Render(layout.SummaryItems, usage, limits);
    }

    private const int PromptMaxLength = TurnClose.PromptMaxLength;

    // Cuántas veces se vuelve a leer un turno sin respuestas: como mucho un segundo y medio de hook.
    private const int StopRetries = 6;

    /// <summary>Recorta un texto a una longitud máxima.</summary>
    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
