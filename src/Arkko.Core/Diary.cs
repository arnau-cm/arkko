using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace Arkko.Core;

/// <summary>Un tramo en que una sesión esperaba una respuesta de la persona.</summary>
public sealed record DiarySpan(DateTime StartUtc, DateTime EndUtc);

/// <summary>Un bloque del Diario: una sesión trabajando sin pausas de más de unos minutos entre sus prompts.</summary>
public sealed class DiaryBlock
{
    public string SessionId { get; set; } = "";
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }

    /// <summary>La tarea principal de la sesión; null si no tiene.</summary>
    public string? Task { get; set; }

    /// <summary>Repositorio si la carpeta era un worktree; la carpeta si no.</summary>
    public string Project { get; set; } = "";

    /// <summary>La carpeta del worktree en que trabajó («master», «develop»), si no es la del repositorio; null si no hay.</summary>
    public string? Folder { get; set; }
    public string? SessionName { get; set; }
    public int Prompts { get; set; }
    public long NewTokens { get; set; }

    /// <summary>Lo que Claude estuvo trabajando en el bloque: la suma de lo que duró cada prompt.</summary>
    public long WorkingMs { get; set; }
    public List<DiarySpan> Waits { get; set; } = [];

    /// <summary>Cada prompt del bloque: cuándo empezó y acabó y su principio, para la ficha y su línea de tiempo.</summary>
    public List<DiaryTurn> Turns { get; set; } = [];
}

/// <summary>Un prompt de un bloque: de cuándo a cuándo trabajó Claude en él y el principio de su texto, si se conserva.</summary>
/// <remarks>El texto sigue los plazos del de los prompts: pasado el suyo, o al eliminar la sesión, se queda en null.</remarks>
public sealed class DiaryTurn
{
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }

    /// <summary>El principio del prompt; en un comando «!», el comando, y en un aviso, lo que dice.</summary>
    public string? Text { get; set; }

    /// <summary>Con qué empezó el turno: solo los prompts de la persona cuentan como prompts.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public PromptKind Kind { get; set; }
}

/// <summary>Un commit del día, de la persona, con la tarea de la carpeta en que se hizo.</summary>
public sealed class DiaryCommit
{
    public string Hash { get; set; } = "";
    public DateTime WhenUtc { get; set; }
    public string Subject { get; set; } = "";
    public string? Task { get; set; }
    public string Project { get; set; } = "";

    /// <summary>La carpeta del worktree en que se hizo, si no es la del repositorio.</summary>
    public string? Folder { get; set; }
}

/// <summary>Una PR de una rama trabajada en el día, con su tarea.</summary>
public sealed record DiaryPullRequest(string? Task, string Project, int Number);

/// <summary>Lo hecho en el día en una tarea, o en un proyecto si se trabajó sin tarea: una entrada del parte.</summary>
public sealed class DiaryTask
{
    public string? Key { get; set; }
    public string Project { get; set; } = "";

    /// <summary>Sin tarea, la carpeta del worktree en que se trabajó, si no es la del repositorio: el parte va por cada una.</summary>
    public string? Folder { get; set; }
    public string? Title { get; set; }
    public int Sessions { get; set; }
    public int Prompts { get; set; }
    public long WorkingMs { get; set; }
    public long WaitMs { get; set; }
    public long NewTokens { get; set; }
    public List<DiaryCommit> Commits { get; set; } = [];
    public List<int> PullRequests { get; set; } = [];
    public List<TaskStateChange> Moves { get; set; } = [];
}

/// <summary>El resumen de un día: sus bloques y su parte por tarea. Se guarda en data/diary/&lt;día&gt;.json.</summary>
public sealed class DiaryDay
{
    /// <summary>El día local, a medianoche.</summary>
    public DateTime Day { get; set; }

    /// <summary>Cuándo se calculó: si fue después de acabar el día, el resumen ya no cambia.</summary>
    public DateTime BuiltUtc { get; set; }

    /// <summary>La versión del formato con que se calculó: uno de una versión anterior se vuelve a calcular.</summary>
    public int Version { get; set; }
    public List<DiaryBlock> Blocks { get; set; } = [];
    public List<DiaryTask> Tasks { get; set; } = [];

    /// <summary>El uso más alto que llegó a tener el límite de 5 horas en el día; null si no hay datos.</summary>
    public double? PeakFiveHourPct { get; set; }

    /// <summary>Desde cuándo se apuntaban ese día los estados de las sesiones; null si no se apuntaban.</summary>
    /// <remarks>Lo de antes no sabe cuánto se esperó: el día en que se empezó a apuntar, solo desde esa hora.</remarks>
    public DateTime? StatesSinceUtc { get; set; }

    /// <summary>Si se sabe cuánto esperó un bloque: si empezó cuando ya se apuntaban los estados.</summary>
    public bool WaitsKnown(DiaryBlock block) => StatesSinceUtc is { } since && block.StartUtc >= since - Diary.BlockGap;

    [JsonIgnore] public long WorkingMs => Blocks.Sum(b => b.WorkingMs);
    [JsonIgnore] public long WaitMs => Blocks.Sum(b => b.Waits.Sum(w => (long)(w.EndUtc - w.StartUtc).TotalMilliseconds));
    [JsonIgnore] public long NewTokens => Blocks.Sum(b => b.NewTokens);
    [JsonIgnore] public int Prompts => Blocks.Sum(b => b.Prompts);
    [JsonIgnore] public int Commits => Tasks.Sum(t => t.Commits.Count);
    [JsonIgnore] public bool Empty => Blocks.Count == 0 && Tasks.Count == 0;
}

/// <summary>Lo que hace falta para calcular un día, ya leído: así el cálculo no toca el disco ni git.</summary>
/// <param name="SessionTasks">Las tareas de cada sesión, por su id, con la misma regla de tarea principal que el Tablero.</param>
/// <param name="Title">El título de una tarea por su clave; null si no se conoce.</param>
public sealed record DiaryInputs(
    DateTime Day,
    IReadOnlyList<PromptRecord> Prompts,
    IReadOnlyList<StateChange> States,
    IReadOnlyList<TaskStateChange> Moves,
    IReadOnlyList<DiaryCommit> Commits,
    IReadOnlyList<DiaryPullRequest> PullRequests,
    IReadOnlyDictionary<string, SessionTasks> SessionTasks,
    Func<string, string?> Title,
    double? PeakFiveHourPct);

/// <summary>Calcula el Diario de un día y su parte: sin IA, juntando lo que ya existe y agrupándolo por tarea.</summary>
/// <remarks>
/// Un bloque junta los prompts seguidos de una sesión: cada prompt guarda cuándo terminó y cuánto duró, así que se
/// sabe cuándo empezó. Lo que se hizo lo cuentan los mensajes de commit, que ya escribe quien commitea; la app no
/// redacta nada.
/// </remarks>
public static class Diary
{
    /// <summary>La versión del formato del resumen: se sube cuando cambia lo que guarda o cómo se calcula.</summary>
    /// <remarks>
    /// 2: cada bloque guarda sus prompts con su hora, y el repositorio de los registros antiguos sale de su carpeta.
    /// 3: la carpeta del worktree («master») y desde cuándo se apuntaban ese día los estados.
    /// 4: cada turno dice si lo empezó un prompt, un comando «!» o un aviso, y solo los prompts cuentan como prompts.
    /// </remarks>
    public const int Version = 4;

    /// <summary>La pausa más larga entre dos prompts de una sesión que aún cuenta como el mismo bloque.</summary>
    public static readonly TimeSpan BlockGap = TimeSpan.FromMinutes(10);

    private const int PromptStartLength = 120;
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    /// <summary>Calcula el resumen de un día con lo ya leído.</summary>
    public static DiaryDay Build(DiaryInputs input, DateTime nowUtc)
    {
        var blocks = Blocks(input).OrderBy(b => b.StartUtc).ToList();
        return new DiaryDay
        {
            Day = input.Day.Date,
            BuiltUtc = nowUtc,
            Version = Version,
            Blocks = blocks,
            Tasks = Tasks(blocks, input),
            PeakFiveHourPct = input.PeakFiveHourPct,
            StatesSinceUtc = input.States.Count == 0 ? null : input.States.Min(s => s.WhenUtc),
        };
    }

    /// <summary>Los bloques de cada sesión, con sus esperas.</summary>
    private static IEnumerable<DiaryBlock> Blocks(DiaryInputs input)
    {
        var states = input.States.GroupBy(s => s.SessionId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.WhenUtc).ToList(), StringComparer.OrdinalIgnoreCase);
        foreach (var session in input.Prompts.GroupBy(p => p.SessionId, StringComparer.OrdinalIgnoreCase))
        {
            var task = input.SessionTasks.TryGetValue(session.Key, out var tasks) ? tasks.Principal : null;
            var waits = Waits(states.GetValueOrDefault(session.Key) ?? []);
            DiaryBlock? block = null;
            foreach (var prompt in session.OrderBy(p => p.WhenUtc))
            {
                var start = prompt.WhenUtc - TimeSpan.FromMilliseconds(Math.Max(0, prompt.Usage.DurationMs));
                if (block is null || start - block.EndUtc > BlockGap)
                {
                    if (block is not null) yield return Finish(block, waits);
                    block = new DiaryBlock { SessionId = session.Key, StartUtc = start, Task = task };
                }
                block.EndUtc = prompt.WhenUtc > block.EndUtc ? prompt.WhenUtc : block.EndUtc;
                if (start < block.StartUtc) block.StartUtc = start;
                block.Project = prompt.Repo ?? prompt.Project ?? block.Project;
                block.Folder = FolderOf(prompt.Repo, prompt.Project) ?? block.Folder;
                block.SessionName = prompt.SessionName ?? block.SessionName;
                if (prompt.ByPerson) block.Prompts++;
                block.WorkingMs += Math.Max(0, prompt.Usage.DurationMs);
                block.NewTokens += PromptSummary.NewTokens(prompt.Usage.Totals());
                block.Turns.Add(new DiaryTurn { StartUtc = start, EndUtc = prompt.WhenUtc, Text = TurnText(prompt), Kind = prompt.KindOf });
            }
            if (block is not null) yield return Finish(block, waits);
        }
    }

    /// <summary>La carpeta del worktree si no es la del repositorio («master» en arkko-stack); null si son la misma o no hay repositorio.</summary>
    public static string? FolderOf(string? repo, string? folder) =>
        repo is not null && folder is not null && !string.Equals(repo, folder, StringComparison.OrdinalIgnoreCase) ? folder : null;

    /// <summary>Le pone a un bloque las esperas que caen dentro de él, recortadas a sus bordes.</summary>
    private static DiaryBlock Finish(DiaryBlock block, List<DiarySpan> waits)
    {
        foreach (var w in waits)
        {
            var start = w.StartUtc < block.StartUtc ? block.StartUtc : w.StartUtc;
            var end = w.EndUtc > block.EndUtc ? block.EndUtc : w.EndUtc;
            if (end > start) block.Waits.Add(new DiarySpan(start, end));
        }
        return block;
    }

    /// <summary>Los tramos de espera de una sesión: de cada paso a «esperando» al siguiente cambio de estado.</summary>
    /// <remarks>Una espera sin cambio detrás no se cuenta: no se sabe cuándo acabó.</remarks>
    internal static List<DiarySpan> Waits(List<StateChange> states)
    {
        var result = new List<DiarySpan>();
        for (var i = 0; i < states.Count - 1; i++)
            if (states[i].Status == SessionStatus.Waiting) result.Add(new DiarySpan(states[i].WhenUtc, states[i + 1].WhenUtc));
        return result;
    }

    /// <summary>El parte: una entrada por tarea, o por proyecto si se trabajó sin tarea, de la que más tiempo llevó a la que menos.</summary>
    private static List<DiaryTask> Tasks(List<DiaryBlock> blocks, DiaryInputs input)
    {
        var entries = new Dictionary<string, DiaryTask>(StringComparer.OrdinalIgnoreCase);
        // Sin tarea, una entrada por repositorio y carpeta: «master» y «develop» de un mismo repositorio van aparte.
        static string Id(string? key, string project, string? folder) => key ?? $"proyecto:{project}/{folder}";
        DiaryTask Entry(string? key, string project, string? folder)
        {
            var id = Id(key, project, folder);
            if (!entries.TryGetValue(id, out var entry))
                entries[id] = entry = new DiaryTask
                {
                    Key = key, Project = project, Folder = key is null ? folder : null, Title = key is null ? null : input.Title(key),
                };
            return entry;
        }

        foreach (var group in blocks.GroupBy(b => Id(b.Task, b.Project, b.Folder), StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First();
            var entry = Entry(first.Task, first.Project, first.Folder);
            entry.Sessions = group.Select(b => b.SessionId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            entry.Prompts = group.Sum(b => b.Prompts);
            entry.WorkingMs = group.Sum(b => b.WorkingMs);
            entry.WaitMs = group.Sum(b => b.Waits.Sum(w => (long)(w.EndUtc - w.StartUtc).TotalMilliseconds));
            entry.NewTokens = group.Sum(b => b.NewTokens);
        }
        foreach (var commit in input.Commits.OrderBy(c => c.WhenUtc)) Entry(commit.Task, commit.Project, commit.Folder).Commits.Add(commit);
        foreach (var pr in input.PullRequests)
        {
            var entry = Entry(pr.Task, pr.Project, null);
            if (!entry.PullRequests.Contains(pr.Number)) entry.PullRequests.Add(pr.Number);
        }
        // Un cambio de columna sin trabajo ese día también sale: es lo que se hizo con la tarea.
        foreach (var move in input.Moves.OrderBy(m => m.WhenUtc))
        {
            var known = entries.Values.FirstOrDefault(e => string.Equals(e.Key, move.Key, StringComparison.OrdinalIgnoreCase));
            (known ?? Entry(move.Key, "", null)).Moves.Add(move);
        }
        return entries.Values
            .OrderByDescending(e => e.WorkingMs)
            .ThenByDescending(e => e.Commits.Count)
            .ThenBy(e => e.Key ?? e.Project, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Los bloques de una entrada del parte: los de su tarea o, sin tarea, los de su proyecto.</summary>
    public static IEnumerable<DiaryBlock> BlocksOf(DiaryDay day, DiaryTask task) =>
        day.Blocks.Where(b => task.Key is { } key
            ? string.Equals(b.Task, key, StringComparison.OrdinalIgnoreCase)
            : b.Task is null && b.Project == task.Project && b.Folder == task.Folder);

    /// <summary>El nombre de una entrada del parte: su clave y título, o su repositorio con la carpeta del worktree.</summary>
    public static string NameOf(DiaryTask task) =>
        task.Key is { } key ? task.Title is null ? key : $"{key} · {task.Title}"
        : task.Folder is { } folder ? $"{task.Project} ({folder})" : task.Project;

    /// <summary>Lo que se pidió en una entrada del parte, por orden y sin repetir: lo que cuenta lo hecho si no hubo commits.</summary>
    public static List<string> Asked(DiaryDay day, DiaryTask task, int max = 3) =>
        BlocksOf(day, task).OrderBy(b => b.StartUtc).SelectMany(b => b.Turns).Where(t => t.Kind == PromptKind.Prompt)
            .Select(t => t.Text).OfType<string>().Distinct().Take(max).ToList();

    /// <summary>El texto de un turno para el Diario: el principio del prompt o del comando, o lo que dice el aviso; null si no se guardó.</summary>
    private static string? TurnText(PromptRecord prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt.Prompt)) return null;
        return Start(prompt.KindOf == PromptKind.Notification ? UserMessage.NotificationText(prompt.Prompt) : prompt.Prompt);
    }

    /// <summary>El principio de un prompt en una línea.</summary>
    private static string Start(string prompt)
    {
        var line = string.Join(' ', prompt.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= PromptStartLength ? line : line[..PromptStartLength].TrimEnd() + "…";
    }

    /// <summary>Un tiempo como se escribe en el panel: «4h 10m», «25m», «menos de 1m» o «0m».</summary>
    public static string Hours(long ms)
    {
        var span = TimeSpan.FromMilliseconds(ms);
        if (ms <= 0) return "0m";
        if (span.TotalMinutes < 1) return "menos de 1m";
        return span.TotalHours < 1 ? $"{(int)span.TotalMinutes}m" : $"{(int)span.TotalHours}h {span.Minutes:00}m";
    }

    /// <summary>El parte de unos días como texto para pegar: una cabecera por día y, debajo, cada tarea con lo que se hizo.</summary>
    public static string Report(IEnumerable<DiaryDay> days)
    {
        var text = new StringBuilder();
        foreach (var day in days.Where(d => !d.Empty).OrderBy(d => d.Day))
        {
            if (text.Length > 0) text.AppendLine();
            var title = day.Day.ToString("dddd d 'de' MMMM", Es);
            text.AppendLine(char.ToUpper(title[0], Es) + title[1..]);
            var totals = new List<string> { $"{Hours(day.WorkingMs)} de agente" };
            if (day.WaitMs >= 60_000) totals.Add($"{Hours(day.WaitMs)} esperándote");
            totals.Add($"{Format.Tokens(day.NewTokens)} tokens");
            if (day.PeakFiveHourPct is { } peak) totals.Add($"pico 5 h: {peak:0} %");
            text.AppendLine(string.Join(" · ", totals));
            foreach (var task in day.Tasks)
            {
                text.AppendLine();
                var name = NameOf(task);
                var work = task.Sessions == 0
                    ? ""
                    : $" — {task.Sessions} {(task.Sessions == 1 ? "sesión" : "sesiones")} · {Hours(task.WorkingMs)}";
                text.AppendLine(name + work);
                foreach (var commit in task.Commits) text.AppendLine($"  - {commit.Subject}");
                if (task.Commits.Count == 0)
                    foreach (var asked in Asked(day, task)) text.AppendLine($"  - Pedido: «{asked}»");
                var marks = task.PullRequests.Select(n => $"PR #{n}")
                    .Concat(task.Moves.Select(m => $"{TaskBoard.ColumnName(m.From)} → {TaskBoard.ColumnName(m.To)}"))
                    .ToList();
                if (marks.Count > 0) text.AppendLine("  " + string.Join(" · ", marks));
            }
        }
        return text.ToString().TrimEnd();
    }
}
