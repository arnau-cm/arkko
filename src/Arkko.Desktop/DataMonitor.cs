using System.IO;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Un cambio de estado que merece aviso: una sesión ha terminado o espera respuesta.</summary>
public sealed record SessionAlert(SessionState Session);

/// <summary>Lee el almacén de datos y detecta los cambios de estado de las sesiones.</summary>
public sealed class DataMonitor
{
    private static readonly TimeSpan StaleWithoutPid = TimeSpan.FromHours(12);

    private readonly Dictionary<string, (SessionStatus Status, DateTime Since)> _seen = new();
    private bool _firstPass = true;
    private long _promptsLength = -1;
    private DateTime _promptsDay;
    private Dictionary<string, ModelTokens> _today = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, (long Length, (string Text, DateTime Utc)? Recap)> _recaps = new();
    private readonly Dictionary<string, BackgroundTrace> _background = new(StringComparer.OrdinalIgnoreCase);

    public List<SessionState> Sessions { get; private set; } = [];

    /// <summary>Lo que vigila que los hooks de Arkko funcionen, en cada sesión y en el equipo.</summary>
    public HookMonitor Hooks { get; } = new();
    public LimitsSnapshot? Limits { get; private set; }
    public IReadOnlyDictionary<string, ModelTokens> Today => _today;

    /// <summary>Relee el almacén y devuelve los avisos nuevos desde la lectura anterior.</summary>
    public List<SessionAlert> Refresh()
    {
        Limits = JsonStore.Read<LimitsSnapshot>(AppPaths.LimitsFile);
        RefreshToday();

        var alive = new List<SessionState>();
        foreach (var s in WithUnreadable(SessionStore.LoadAll()))
        {
            if (s.ClaudePid is { } pid)
            {
                if (!ClaudeProcess.IsAlive(pid, s.ClaudeStartUtc))
                {
                    SessionStore.Remove(s.SessionId);
                    continue;
                }
            }
            else if (DateTime.UtcNow - s.UpdatedUtc > StaleWithoutPid)
            {
                continue;
            }
            alive.Add(s);
        }

        // Lo que los hooks no pudieron apuntar se corrige antes de buscar cambios: un turno cerrado así avisa como cualquier otro.
        var fixedUp = Hooks.Check(alive);
        if (fixedUp.Count > 0) alive = alive.Select(s => fixedUp.GetValueOrDefault(s.SessionId) ?? s).ToList();
        Hooks.CheckIfDue();

        // También antes de buscar cambios: una sesión que espera a sus agentes no ha terminado, y no avisa hasta que acaba.
        var now = DateTime.UtcNow;
        foreach (var gone in _background.Keys.Except(alive.Select(s => s.SessionId)).ToList()) _background.Remove(gone);
        foreach (var s in alive) ApplyBackground(s, now);

        var alerts = new List<SessionAlert>();
        foreach (var s in alive)
        {
            var changed = !_seen.TryGetValue(s.SessionId, out var prev) ||
                          prev.Status != s.Status || prev.Since != s.StatusSinceUtc;
            if (changed && !_firstPass && s.Status is SessionStatus.Done or SessionStatus.Waiting)
                alerts.Add(new SessionAlert(s));
            if (changed && !_firstPass && s.Status is SessionStatus.Agents or SessionStatus.Command && prev.Status != s.Status) LogBackground(s);
            _seen[s.SessionId] = (s.Status, s.StatusSinceUtc);
        }
        foreach (var gone in _seen.Keys.Except(alive.Select(s => s.SessionId)).ToList()) _seen.Remove(gone);

        _firstPass = false;
        Sessions = alive
            .OrderBy(s => s.Status switch
            {
                SessionStatus.Waiting => 0,
                SessionStatus.Done => 1,
                SessionStatus.Working or SessionStatus.Agents or SessionStatus.Command => 2,
                _ => 3,
            })
            .ThenByDescending(s => s.StatusSinceUtc)
            .ToList();
        return alerts;
    }

    /// <summary>Lee en el transcript de una sesión su trabajo en segundo plano y le pone el estado que corresponde: esperando a
    /// sus agentes, desde que acabó su turno, si le quedan agentes trabajando.</summary>
    /// <remarks>Solo cambia la copia en memoria: el fichero de la sesión es de los hooks.</remarks>
    private void ApplyBackground(SessionState s, DateTime nowUtc)
    {
        if (string.IsNullOrEmpty(s.TranscriptPath) || !File.Exists(s.TranscriptPath)) return;
        if (!_background.TryGetValue(s.SessionId, out var trace)) _background[s.SessionId] = trace = new BackgroundTrace();
        try
        {
            trace.Read(s.TranscriptPath);
            var folder = SubagentFolder.Of(s.TranscriptPath, s.SessionId);
            trace.ReadAgents(folder);
            var background = trace.Snapshot(nowUtc, folder);
            s.Background = background;
            var status = SessionActivity.Status(s.Status, background);
            if (status == s.Status) return;
            s.Status = status;
            if (status is SessionStatus.Agents or SessionStatus.Command && background.TurnEndUtc is { } end) s.StatusSinceUtc = end;
        }
        catch (Exception ex)
        {
            JsonStore.LogError("background", ex);
        }
    }

    /// <summary>Apunta en el historial de estados que la sesión ha pasado a esperar a sus agentes o a un comando.</summary>
    /// <remarks>El hook no lo ve: lo apunta la app, con la hora en que acabó el turno. Un fallo no para la lectura.</remarks>
    private static void LogBackground(SessionState s)
    {
        try
        {
            StateLog.Append(s.SessionId, s.Status, s.StatusSinceUtc);
        }
        catch (Exception ex)
        {
            JsonStore.LogError("states", ex);
        }
    }

    /// <summary>Las sesiones leídas, más las de la lectura anterior cuyo fichero sigue ahí pero no se ha podido leer ahora.</summary>
    /// <remarks>
    /// Los hooks reescriben el estado de una sesión a cada paso, y una lectura que coincide con esa
    /// sustitución falla. Sin esto, la sesión desaparecía un segundo: el tablero la sacaba y volvía a
    /// meter, y Sesiones la daba por cerrada y dejaba de seguirla en directo.
    /// </remarks>
    private List<SessionState> WithUnreadable(List<SessionState> loaded)
    {
        var ids = loaded.Select(s => s.SessionId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        loaded.AddRange(Sessions.Where(s => !ids.Contains(s.SessionId) && File.Exists(AppPaths.SessionFile(s.SessionId))));
        return loaded;
    }

    /// <summary>Recap vigente de una sesión: el último de Claude Code, si es posterior a su estado actual.</summary>
    /// <remarks>
    /// El transcript solo se vuelve a leer cuando cambia de tamaño. Un recap anterior al estado
    /// actual habla de un trabajo ya superado (la sesión ha vuelto a trabajar), así que no se muestra.
    /// </remarks>
    public (string Text, DateTime Utc)? RecapFor(SessionState s)
    {
        if (string.IsNullOrEmpty(s.TranscriptPath)) return null;
        long length;
        try
        {
            length = new FileInfo(s.TranscriptPath).Length;
        }
        catch (IOException)
        {
            return null;
        }

        if (!_recaps.TryGetValue(s.SessionId, out var cached) || cached.Length != length)
        {
            (string, DateTime)? recap = null;
            try
            {
                recap = TranscriptReader.LatestRecap(s.TranscriptPath);
            }
            catch (Exception ex)
            {
                JsonStore.LogError("recap", ex);
            }
            cached = (length, recap);
            _recaps[s.SessionId] = cached;
        }
        return cached.Recap is { } r && r.Utc >= s.StatusSinceUtc ? r : null;
    }

    /// <summary>Recalcula el consumo del día solo cuando crece su fichero o cambia el día.</summary>
    private void RefreshToday()
    {
        var day = DateTime.Now.Date;
        var path = AppPaths.PromptsFile(day);
        var length = File.Exists(path) ? new FileInfo(path).Length : 0;
        if (day == _promptsDay && length == _promptsLength) return;

        var totals = new Dictionary<string, ModelTokens>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in JsonStore.ReadLines<PromptRecord>(path))
        {
            foreach (var (model, tokens) in record.Usage.ByModel)
            {
                var name = Format.ModelName(model);
                if (!totals.TryGetValue(name, out var acc)) totals[name] = acc = new ModelTokens();
                acc.Add(tokens);
            }
        }

        _today = totals;
        _promptsDay = day;
        _promptsLength = length;
    }
}
