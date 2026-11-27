namespace Arkko.Core;

/// <summary>Lo que ha costado una tarea: sus prompts, sus sesiones, los tokens nuevos y el tiempo de Claude trabajando.</summary>
public sealed record TaskUsage(string Key, int Prompts, int Sessions, long NewTokens, TimeSpan Working, DateTime FirstUtc, DateTime LastUtc)
{
    /// <summary>Parte de los tokens nuevos de un total, de 0 a 100, para repartir un periodo entre tareas.</summary>
    public double Pct { get; init; }
}

/// <summary>Las tareas en que trabaja una sesión.</summary>
/// <param name="Principal">La que se lleva su coste: la elegida a mano o, si no, la de su carpeta o la primera que se le conoció.</param>
/// <param name="Tasks">Todas, con la principal delante.</param>
/// <param name="Manual">Las añadidas a mano.</param>
/// <param name="Suggestions">Claves que sus prompts mencionan a menudo, del mismo proyecto que la principal, y aún no son suyas.</param>
public sealed record SessionTasks(string SessionId, string? Principal, IReadOnlyList<string> Tasks, IReadOnlyList<string> Manual,
    IReadOnlyList<string> Suggestions)
{
    /// <summary>Las de una sesión sin prompts guardados: solo la de su carpeta, si lleva clave.</summary>
    public static SessionTasks Of(string sessionId, string? folderKey) =>
        new(sessionId, folderKey, folderKey is null ? [] : [folderKey], [], []);
}

/// <summary>Une las tareas del tablero con las sesiones de Claude Code: una sesión puede trabajar en varias.</summary>
/// <remarks>
/// Solo cuentan señales explícitas: la clave de su carpeta (la de un worktree se llama como su rama, y la rama como
/// su tarea, DEVLAB-99), las que aparecen en un prompt cuya primera línea es un comando con una clave
/// («/opsx:apply DEVLAB-87») y las añadidas a mano; y se le quitan las quitadas a mano, también la de la carpeta. La
/// principal es la elegida a mano o, si no, la primera que queda, en ese orden. Mencionar una clave no la añade:
/// se habla de tareas sin trabajar en ellas.
/// Como mucho, una clave mencionada en varios prompts de una sesión con tarea principal, y del mismo proyecto,
/// se sugiere. Todo el coste de la sesión cuenta para la principal: repartirlo a partes iguales falsearía una tarea
/// grande junto a dos pequeñas. Las claves se comparan sin distinguir mayúsculas.
/// </remarks>
public static class TaskLinks
{
    private const int MaxSuggestions = 3;

    // Prompts distintos en que tiene que salir una clave para sugerirla.
    private const int MinMentions = 3;

    /// <summary>La clave de tarea de una carpeta de sesión; null si no lleva ninguna.</summary>
    public static string? KeyOf(BoardSettings settings, string? folder) => settings.TaskKeyIn(folder)?.ToUpperInvariant();

    /// <summary>La clave de tarea de un prompt, por la carpeta en que se hizo.</summary>
    public static string? KeyOf(BoardSettings settings, PromptRecord record) => KeyOf(settings, record.Project);

    /// <summary>Las tareas de cada sesión con prompts guardados, por su id.</summary>
    /// <param name="links">Lo añadido y quitado a mano en cada sesión, por su id.</param>
    public static Dictionary<string, SessionTasks> Sessions(IEnumerable<PromptRecord> records, BoardSettings settings,
        IReadOnlyDictionary<string, SessionLink>? links = null)
    {
        var result = new Dictionary<string, SessionTasks>(StringComparer.OrdinalIgnoreCase);
        foreach (var session in records.GroupBy(r => r.SessionId, StringComparer.OrdinalIgnoreCase))
        {
            var link = links is not null && links.TryGetValue(session.Key, out var l) ? l : null;
            var removed = new HashSet<string>(link?.Removed ?? [], StringComparer.OrdinalIgnoreCase);
            var manual = (link?.Added ?? []).Select(k => k.ToUpperInvariant()).Distinct().ToList();
            var ordered = session.OrderBy(r => r.WhenUtc).ToList();

            var folder = ordered.Select(r => KeyOf(settings, r)).FirstOrDefault(k => k is not null);
            var detected = new List<string>();
            var mentions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var record in ordered.Where(r => r.ByPerson))
            {
                if (settings.SwitchKeyIn(record.Prompt) is { } command) detected.Add(command);
                else
                    foreach (var key in settings.TaskKeysIn(record.Prompt))
                        mentions[key] = mentions.GetValueOrDefault(key) + 1;
            }

            var (principal, tasks) = Choose(new[] { folder }.Concat(detected).Concat(manual), removed, link?.Principal);
            var suggestions = principal is null
                ? []
                : mentions.Where(m => m.Value >= MinMentions && SameProject(m.Key, principal)
                                      && !tasks.Contains(m.Key, StringComparer.OrdinalIgnoreCase) && !removed.Contains(m.Key))
                    .OrderByDescending(m => m.Value).ThenBy(m => m.Key)
                    .Take(MaxSuggestions).Select(m => m.Key).ToList();
            result[session.Key] = new SessionTasks(session.Key, principal, tasks, manual.Where(k => !removed.Contains(k)).ToList(), suggestions);
        }
        return result;
    }

    /// <summary>Las tareas de una sesión, sin las quitadas, y su principal: la elegida a mano si sigue entre ellas o, si no, la primera.</summary>
    /// <param name="found">Las que se le conocen, en orden: la de su carpeta, las de sus comandos y las añadidas a mano.</param>
    public static (string? Principal, List<string> Tasks) Choose(IEnumerable<string?> found, IReadOnlySet<string> removed, string? chosen)
    {
        var tasks = found.OfType<string>().Where(k => !removed.Contains(k)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var principal = tasks.FirstOrDefault(k => string.Equals(k, chosen, StringComparison.OrdinalIgnoreCase)) ?? tasks.FirstOrDefault();
        if (principal is not null)
        {
            tasks.Remove(principal);
            tasks.Insert(0, principal);
        }
        return (principal, tasks);
    }

    /// <summary>Lo que ha costado cada tarea en unos prompts, por su clave: cada sesión cuenta entera para su tarea principal.</summary>
    public static Dictionary<string, TaskUsage> UsageByTask(IEnumerable<PromptRecord> records, BoardSettings settings,
        IReadOnlyDictionary<string, SessionLink>? links = null)
    {
        var list = records as IReadOnlyCollection<PromptRecord> ?? records.ToList();
        var sessions = Sessions(list, settings, links);
        return list.Select(r => (Key: sessions.TryGetValue(r.SessionId, out var s) ? s.Principal : KeyOf(settings, r), Record: r))
            .Where(x => x.Key is not null)
            .GroupBy(x => x.Key!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => Usage(g.Key, g.Select(x => x.Record).ToList()), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>El consumo de unos prompts repartido por tarea, de la que más gasta a la que menos, con su parte del total.</summary>
    /// <remarks>Los prompts sin tarea no cuentan ni en el reparto ni en el total.</remarks>
    public static List<TaskUsage> ByTask(IReadOnlyCollection<PromptRecord> records, BoardSettings settings,
        IReadOnlyDictionary<string, SessionLink>? links = null)
    {
        var usage = UsageByTask(records, settings, links).Values.ToList();
        var total = usage.Sum(u => u.NewTokens);
        return usage.Select(u => u with { Pct = total == 0 ? 0 : 100.0 * u.NewTokens / total })
            .OrderByDescending(u => u.NewTokens)
            .ToList();
    }

    /// <summary>Lo que ha costado cada sesión en unos prompts, por su id.</summary>
    public static Dictionary<string, TaskUsage> UsageBySession(IEnumerable<PromptRecord> records) =>
        records.GroupBy(r => r.SessionId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => Usage(g.Key, g.ToList()), StringComparer.OrdinalIgnoreCase);

    /// <summary>Las sesiones de cada tarea, por su clave: todas las que la tienen, sea o no su principal.</summary>
    public static Dictionary<string, HashSet<string>> SessionsByTask(IEnumerable<SessionTasks> sessions)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var session in sessions)
            foreach (var key in session.Tasks)
            {
                if (!result.TryGetValue(key, out var set)) result[key] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                set.Add(session.SessionId);
            }
        return result;
    }

    /// <summary>Si dos claves son del mismo proyecto: lo que va antes del último guion («DEVLAB» en DEVLAB-99).</summary>
    private static bool SameProject(string a, string b)
    {
        static string Prefix(string key) => key.LastIndexOf('-') is > 0 and var i ? key[..i] : key;
        return string.Equals(Prefix(a), Prefix(b), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Suma de unos prompts.</summary>
    private static TaskUsage Usage(string key, List<PromptRecord> records)
    {
        var summary = History.Summarize(records);
        return new TaskUsage(key, summary.Prompts, summary.Sessions, summary.NewTokens, summary.Working,
            records.Min(r => r.WhenUtc), records.Max(r => r.WhenUtc));
    }
}
