using System.Globalization;

namespace Arkko.Core;

/// <summary>Lo que Arkko sabe del informe de seguridad de la vez anterior: el estado de cada comprobación y de cada aviso,
/// y de qué caducidad ya ha avisado.</summary>
/// <remarks>Se guarda en <c>data/vulnerabilities/state.json</c>, para que reiniciar la app no repita avisos.</remarks>
public sealed class StackSecurityState
{
    /// <summary>Si ya se ha leído algún informe. El estado puede existir sin eso: lo crea el aviso de que no hay informe.</summary>
    public bool Started { get; set; }

    /// <summary>Estado de cada comprobación, por su id: OK, FALLA o SIN COMPROBAR.</summary>
    public Dictionary<string, string> Checks { get; set; } = [];

    /// <summary>Si cada aviso estaba visto, por su id.</summary>
    public Dictionary<string, bool> Notices { get; set; } = [];

    /// <summary>La fecha de las noticias de la que ya se avisó que estaban viejas; con otra fecha, se vuelve a avisar.</summary>
    public DateTimeOffset? NewsStaleAlerted { get; set; }

    /// <summary>La fecha de la exposición de la que ya se avisó que estaba vieja.</summary>
    public DateTimeOffset? ExposureStaleAlerted { get; set; }
}

/// <summary>Un cambio en el historial: una comprobación que cambia de estado o un aviso que aparece, se ve o se va.</summary>
/// <param name="Kind">«check» o «notice».</param>
/// <param name="From">El estado de antes; null si es la primera vez que sale.</param>
/// <param name="To">El estado de ahora; null si ya no sale en el informe.</param>
/// <param name="Detail">La frase del informe en ese momento («instalada 2.54.0.1; la última es la 2.56.0.4»); solo en las
/// comprobaciones, y null en lo apuntado antes de guardarla.</param>
public sealed record StackHistoryEntry(DateTime Utc, string Kind, string Id, string? Name, string? Object, string? From, string? To, string? Detail = null);

/// <summary>Lo que ha cambiado al leer un informe: lo nuevo de lo que avisar y lo que va al historial.</summary>
/// <param name="First">Si era la primera lectura: entonces no se avisa de nada, solo se apunta cómo está todo.</param>
public sealed record StackChanges(IReadOnlyList<StackCheck> NewFailures, IReadOnlyList<StackNotice> NewNotices, IReadOnlyList<StackHistoryEntry> History, bool First)
{
    public static readonly StackChanges None = new([], [], [], false);
}

/// <summary>Compara cada informe con lo que se sabía, guarda el estado y escribe el historial de vulnerabilidades.</summary>
/// <remarks>
/// El historial va por días (<c>data/vulnerabilities/history/aaaa-mm-dd.jsonl</c>), con el plazo de Configuración →
/// Datos. Guarda los cambios de estado por id, no copias del informe, que sigue siendo de arkko-stack.
/// </remarks>
public static class StackSecurityLog
{
    public const string CheckKind = "check";
    public const string NoticeKind = "notice";

    /// <summary>Cómo se apunta un aviso en el historial.</summary>
    public const string Unseen = "sin ver";
    public const string Seen = "visto";

    /// <summary>La carpeta de lo que guarda Arkko de las vulnerabilidades, dentro de la de datos.</summary>
    public static string Folder => Path.Combine(AppPaths.Data, "vulnerabilities");

    private static string StateFile(string folder) => Path.Combine(folder, "state.json");
    private static string HistoryDir(string folder) => Path.Combine(folder, "history");

    /// <summary>Compara un informe con el estado anterior y devuelve el estado nuevo y lo que ha cambiado, sin tocar disco.</summary>
    /// <remarks>
    /// Una sección que nunca se ha ejecutado no cambia nada: el informe la deja sin fecha. Lo que sale por primera vez
    /// en FALLA, o como aviso sin ver, cuenta como nuevo, salvo en la primera lectura, que solo apunta.
    /// </remarks>
    public static (StackSecurityState State, StackChanges Changes) Compare(StackSecurityState? before, StackReport report, DateTime nowUtc)
    {
        var first = before is not { Started: true };
        var state = new StackSecurityState
        {
            Started = true,
            Checks = new(before?.Checks ?? [], StringComparer.Ordinal),
            Notices = new(before?.Notices ?? [], StringComparer.Ordinal),
            NewsStaleAlerted = before?.NewsStaleAlerted,
            ExposureStaleAlerted = before?.ExposureStaleAlerted,
        };
        var failures = new List<StackCheck>();
        var notices = new List<StackNotice>();
        var history = new List<StackHistoryEntry>();

        if (report.Exposure?.Date is not null)
        {
            foreach (var check in report.Checks)
            {
                var to = check.State ?? StackSecurity.Unchecked;
                var from = state.Checks.GetValueOrDefault(check.Id);
                if (from == to) continue;
                history.Add(new(nowUtc, CheckKind, check.Id, check.Name, check.Object, from, to, check.Detail));
                state.Checks[check.Id] = to;
                if (!first && check.Failing) failures.Add(check);
            }
            var present = report.Checks.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var gone in state.Checks.Keys.Where(id => !present.Contains(id)).ToList())
            {
                history.Add(new(nowUtc, CheckKind, gone, null, null, state.Checks[gone], null));
                state.Checks.Remove(gone);
            }
        }

        if (report.News?.Date is not null)
        {
            foreach (var notice in report.Notices)
            {
                var known = state.Notices.TryGetValue(notice.Id, out var wasSeen);
                if (known && wasSeen == notice.Seen) continue;
                history.Add(new(nowUtc, NoticeKind, notice.Id, notice.Title, notice.Source, known ? Mark(wasSeen) : null, Mark(notice.Seen)));
                state.Notices[notice.Id] = notice.Seen;
                if (!first && !known && !notice.Seen) notices.Add(notice);
            }
            var present = report.Notices.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var gone in state.Notices.Keys.Where(id => !present.Contains(id)).ToList())
            {
                history.Add(new(nowUtc, NoticeKind, gone, null, null, Mark(state.Notices[gone]), null));
                state.Notices.Remove(gone);
            }
        }

        return (state, new StackChanges(failures, notices, history, first));
    }

    private static string Mark(bool seen) => seen ? Seen : Unseen;

    /// <summary>Compara un informe con el estado guardado, escribe el historial y el estado nuevo, y devuelve lo que ha cambiado.</summary>
    /// <param name="folder">La carpeta donde se guarda; por defecto, la de la app.</param>
    public static StackChanges Record(StackReport report, DateTime nowUtc, string? folder = null)
    {
        folder ??= Folder;
        using var _ = JsonStore.Lock("vulnerabilities");
        var (state, changes) = Compare(JsonStore.Read<StackSecurityState>(StateFile(folder)), report, nowUtc);
        foreach (var entry in changes.History) JsonStore.AppendLine(HistoryFile(folder, nowUtc.ToLocalTime()), entry);
        if (changes.History.Count > 0 || changes.First) JsonStore.Write(StateFile(folder), state);
        return changes;
    }

    /// <summary>El fichero del historial de un día.</summary>
    private static string HistoryFile(string folder, DateTime localDay) =>
        Path.Combine(HistoryDir(folder), localDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".jsonl");

    /// <summary>Los cambios de una comprobación o de un aviso, del más reciente al más antiguo, dentro de lo que se conserva.</summary>
    public static List<StackHistoryEntry> History(string kind, string id, string? folder = null) =>
        History(folder).Where(e => e.Kind == kind && e.Id == id).ToList();

    /// <summary>Todo el historial que se conserva, del cambio más reciente al más antiguo.</summary>
    public static List<StackHistoryEntry> History(string? folder = null)
    {
        var dir = HistoryDir(folder ?? Folder);
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateFiles(dir, "*.jsonl")
            .SelectMany(JsonStore.ReadLines<StackHistoryEntry>)
            .OrderByDescending(e => e.Utc)
            .ToList();
    }

    /// <summary>Indica si toca avisar de que una sección está vieja, y lo apunta: una sola vez por cada fecha de la sección.</summary>
    /// <param name="news">Las noticias; si no, la exposición.</param>
    /// <param name="date">La fecha de la sección en el informe; null si nunca se ha ejecutado.</param>
    public static bool TakeStaleAlert(bool news, DateTimeOffset? date, string? folder = null)
    {
        var file = StateFile(folder ?? Folder);
        using var _ = JsonStore.Lock("vulnerabilities");
        var state = JsonStore.Read<StackSecurityState>(file) ?? new StackSecurityState();
        var mark = date ?? DateTimeOffset.MinValue;
        if ((news ? state.NewsStaleAlerted : state.ExposureStaleAlerted) == mark) return false;
        if (news) state.NewsStaleAlerted = mark;
        else state.ExposureStaleAlerted = mark;
        JsonStore.Write(file, state);
        return true;
    }
}
