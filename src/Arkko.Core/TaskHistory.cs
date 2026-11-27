using System.Globalization;

namespace Arkko.Core;

/// <summary>Un cambio de columna de una tarea, una línea en data/task-history/&lt;día&gt;.jsonl.</summary>
public sealed class TaskStateChange
{
    /// <summary>Cuándo se vio el cambio: la hora de la lectura, no la del proveedor.</summary>
    public DateTime WhenUtc { get; set; }
    public string Key { get; set; } = "";
    public TaskState From { get; set; }
    public TaskState To { get; set; }

    /// <summary>El estado tal como lo llama el proveedor al llegar a la nueva columna («En revisión»).</summary>
    public string? Status { get; set; }
}

/// <summary>Los cambios de columna de las tareas leídas de los tableros.</summary>
/// <remarks>
/// La app solo ve un tablero al leerlo, así que un cambio se apunta con la hora de la primera lectura que lo
/// encuentra: con la app cerrada, la siguiente al abrirla. La última columna conocida de cada tarea se guarda
/// en data/task-states.json, solo con claves y estados, para compararla en la lectura siguiente. Una tarea
/// que se ve por primera vez no cuenta como cambio.
/// </remarks>
public static class TaskHistory
{
    /// <summary>Compara una lectura con las columnas conocidas y devuelve los cambios.</summary>
    public static List<TaskStateChange> Diff(IReadOnlyDictionary<string, TaskState> known, IEnumerable<BoardTask> tasks, DateTime nowUtc) =>
        tasks
            .Where(t => known.TryGetValue(t.Key, out var before) && before != t.State)
            .Select(t => new TaskStateChange { WhenUtc = nowUtc, Key = t.Key, From = known[t.Key], To = t.State, Status = t.Status })
            .ToList();

    /// <summary>Apunta los cambios de una lectura y guarda sus columnas como las conocidas; devuelve los cambios.</summary>
    /// <remarks>Las tareas que no vienen en esta lectura, de otros tableros, conservan su última columna.</remarks>
    public static List<TaskStateChange> Record(IReadOnlyCollection<BoardTask> tasks, DateTime nowUtc, string? dataDir = null)
    {
        var data = dataDir ?? AppPaths.Data;
        var knownFile = Path.Combine(data, "task-states.json");
        using var _ = JsonStore.Lock("task-states");
        var known = JsonStore.Read<Dictionary<string, TaskState>>(knownFile) ?? new Dictionary<string, TaskState>();
        known = new Dictionary<string, TaskState>(known, StringComparer.OrdinalIgnoreCase);
        var changes = Diff(known, tasks, nowUtc);
        foreach (var change in changes) JsonStore.AppendLine(PathOf(nowUtc.ToLocalTime(), data), change);
        foreach (var task in tasks) known[task.Key] = task.State;
        JsonStore.Write(knownFile, known);
        return changes;
    }

    /// <summary>Los cambios de un día local, en el orden en que se vieron.</summary>
    public static List<TaskStateChange> Read(DateTime localDay, string? dataDir = null) =>
        JsonStore.ReadLines<TaskStateChange>(PathOf(localDay, dataDir));

    /// <summary>El fichero de un día local.</summary>
    public static string PathOf(DateTime localDay, string? dataDir = null) =>
        Path.Combine(dataDir ?? AppPaths.Data, "task-history", localDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".jsonl");
}
