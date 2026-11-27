using System.Globalization;

namespace Arkko.Core;

/// <summary>Planificación local de las tareas: lo traído del backlog, lo propuesto para descartar, lo devuelto y lo quitado de la vista.</summary>
/// <remarks>
/// Es de la app, no del proveedor: nada de esto se escribe en el panel. Solo guarda claves y fechas, sin
/// datos de personas. Las claves van con la conexión delante («conexión/DEVLAB-99»), porque dos paneles
/// pueden tener una tarea con la misma clave.
/// </remarks>
public sealed class TaskPlan
{
    /// <summary>Tareas traídas del backlog a Por hacer.</summary>
    public HashSet<string> Pulled { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Tareas que el proveedor tiene por hacer y se han devuelto al backlog.</summary>
    public HashSet<string> Returned { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Tareas del backlog llevadas a Por descartar.</summary>
    public HashSet<string> Discarding { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Tareas hechas o descartadas que se han quitado de la vista, una a una; se ven en el historial.</summary>
    public HashSet<string> Hidden { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Por tablero, hasta cuándo se limpiaron de golpe las hechas: las cerradas antes no se ven.</summary>
    /// <remarks>Ya no se ofrece, pero se respeta lo que se limpió así antes de poder quitarlas una a una.</remarks>
    public Dictionary<string, DateTime> DoneClearedUtc { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Lo añadido y quitado a mano en las tareas de cada sesión, por su id.</summary>
    public Dictionary<string, SessionLink> SessionLinks { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Formato anterior, de una sola tarea por sesión: al leer el plan se pasa a <see cref="SessionLinks"/>.</summary>
    public Dictionary<string, string> SessionTasks { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Clave de una tarea en el plan, con su conexión delante.</summary>
    public static string Id(string connectionId, string taskKey) => $"{connectionId}/{taskKey}";

    /// <summary>Clave de un tablero elegido: conexión, proyecto y subtablero (o «*» para el proyecto entero).</summary>
    public static string BoardId(string connectionId, BoardSelection board) => $"{connectionId}/{board.ProjectId}/{board.BoardId ?? "*"}";

    /// <summary>Trae una tarea del backlog a Por hacer.</summary>
    public void Pull(string id)
    {
        Returned.Remove(id);
        Discarding.Remove(id);
        Pulled.Add(id);
    }

    /// <summary>Lleva una tarea del backlog a Por descartar.</summary>
    public void Discard(string id)
    {
        Returned.Remove(id);
        Pulled.Remove(id);
        Discarding.Add(id);
    }

    /// <summary>Devuelve una tarea de Por hacer o de Por descartar al backlog, la llevara la persona o la tuviera así el proveedor.</summary>
    public void Return(string id)
    {
        Pulled.Remove(id);
        Discarding.Remove(id);
        Returned.Add(id);
    }

    /// <summary>Quita una tarea hecha o descartada de la vista; pasa al historial.</summary>
    public void Hide(string id) => Hidden.Add(id);

    /// <summary>Devuelve una tarea del historial a su columna.</summary>
    public void Restore(string id) => Hidden.Remove(id);

    /// <summary>Añade a mano una tarea a una sesión.</summary>
    public void AddTask(string sessionId, string key)
    {
        if (!SessionLinks.TryGetValue(sessionId, out var link)) SessionLinks[sessionId] = link = new SessionLink();
        link.Removed.RemoveAll(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
        if (!link.Added.Contains(key, StringComparer.OrdinalIgnoreCase)) link.Added.Add(key.ToUpperInvariant());
    }

    /// <summary>Quita una tarea de una sesión, la añadiera la persona o la encontrara la app; también descarta una sugerencia.</summary>
    public void RemoveTask(string sessionId, string key)
    {
        if (!SessionLinks.TryGetValue(sessionId, out var link)) SessionLinks[sessionId] = link = new SessionLink();
        link.Added.RemoveAll(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
        if (!link.Removed.Contains(key, StringComparer.OrdinalIgnoreCase)) link.Removed.Add(key.ToUpperInvariant());
        if (string.Equals(link.Principal, key, StringComparison.OrdinalIgnoreCase)) link.Principal = null;
    }

    /// <summary>Hace de una tarea la principal de una sesión, la que se lleva su coste; si no era suya, se la añade.</summary>
    public void SetPrincipal(string sessionId, string key)
    {
        AddTask(sessionId, key);
        SessionLinks[sessionId].Principal = key.ToUpperInvariant();
    }
}

/// <summary>Lo añadido y quitado a mano en las tareas de una sesión.</summary>
public sealed class SessionLink
{
    public List<string> Added { get; set; } = [];
    public List<string> Removed { get; set; } = [];

    /// <summary>La tarea principal elegida a mano; null para la de serie, la primera que queda.</summary>
    public string? Principal { get; set; }
}

/// <summary>Qué se enseña del tablero: las tareas de cada columna, las del backlog y las del historial, ya filtradas.</summary>
public sealed record TaskBoardView(IReadOnlyList<BoardTask> Backlog, IReadOnlyDictionary<TaskState, IReadOnlyList<BoardTask>> Columns,
    IReadOnlyList<BoardTask> History)
{
    /// <summary>Columnas del tablero, en su orden; el backlog va aparte, entre Por descartar y Por hacer.</summary>
    public static readonly TaskState[] Order =
        [TaskState.Discarded, TaskState.ToDiscard, TaskState.Todo, TaskState.Doing, TaskState.Blocked, TaskState.Review, TaskState.Done];

    public IReadOnlyList<BoardTask> this[TaskState state] => Columns.TryGetValue(state, out var list) ? list : [];
}

/// <summary>Quién es la persona en el proveedor, para el filtro «Mías».</summary>
public sealed record BoardPerson(string? Id, string? Name);

/// <summary>Filtros de la sección Tareas.</summary>
/// <param name="Me">Con valor, solo las asignadas a la persona.</param>
/// <param name="BoardIds">Con alguno, solo las de esos subtableros; el conjunto decide si distingue mayúsculas.</param>
public sealed record TaskFilter(BoardPerson? Me = null, IReadOnlySet<string>? Labels = null, string? Query = null, IReadOnlySet<string>? BoardIds = null);

/// <summary>Coloca las tareas de un tablero en las columnas de la app, aplicando la planificación local y los filtros.</summary>
public static class TaskBoard
{
    private static readonly CompareInfo Compare = CultureInfo.GetCultureInfo("es-ES").CompareInfo;

    /// <summary>Nombre de una columna de la app, el mismo en el Tablero, los buscadores y el Diario.</summary>
    public static string ColumnName(TaskState state) => state switch
    {
        TaskState.Todo => "Por hacer",
        TaskState.Doing => "En curso",
        TaskState.Blocked => "Bloqueada",
        TaskState.Review => "En revisión",
        TaskState.Done => "Hecha",
        TaskState.ToDiscard => "Por descartar",
        TaskState.Discarded => "Descartada",
        TaskState.Backlog => "Backlog",
        _ => "Sin traducir",
    };

    /// <summary>Reparte las tareas en el backlog y las columnas.</summary>
    /// <remarks>
    /// Al backlog van las que el proveedor tiene en backlog y no se han traído ni llevado a Por descartar, las
    /// devueltas y las de un estado que el perfil no traduce, para que se vean sin ensuciar el tablero. Las hechas
    /// y descartadas quitadas de la vista, o cerradas antes de la última limpieza de ese tablero, van al
    /// historial. Dentro de cada columna, lo último que ha cambiado, arriba.
    /// </remarks>
    public static TaskBoardView Arrange(IEnumerable<BoardTask> tasks, TaskPlan plan, string connectionId, string boardId, TaskFilter filter)
    {
        var cleared = plan.DoneClearedUtc.TryGetValue(boardId, out var at) ? at : (DateTime?)null;
        var backlog = new List<BoardTask>();
        var history = new List<BoardTask>();
        var columns = TaskBoardView.Order.ToDictionary(s => s, _ => new List<BoardTask>());

        foreach (var task in tasks.Where(t => Matches(t, filter)))
        {
            var id = TaskPlan.Id(connectionId, task.Key);
            var state = task.State;
            var closed = state is TaskState.Done or TaskState.Discarded;
            if (closed && (plan.Hidden.Contains(id) || cleared is { } c && (task.UpdatedUtc ?? DateTime.MinValue) <= c))
            {
                history.Add(task);
                continue;
            }

            var waiting = state is TaskState.Backlog or TaskState.Unknown;
            if (plan.Returned.Contains(id) && (waiting || state is TaskState.Todo or TaskState.ToDiscard)) backlog.Add(task);
            else if (waiting && plan.Discarding.Contains(id)) columns[TaskState.ToDiscard].Add(task);
            else if (waiting && plan.Pulled.Contains(id)) columns[TaskState.Todo].Add(task);
            else if (waiting) backlog.Add(task);
            else columns[state].Add(task);
        }

        static List<BoardTask> Newest(IEnumerable<BoardTask> list) => list.OrderByDescending(t => t.UpdatedUtc ?? DateTime.MinValue).ToList();
        return new TaskBoardView(Newest(backlog), columns.ToDictionary(c => c.Key, c => (IReadOnlyList<BoardTask>)Newest(c.Value)), Newest(history));
    }

    /// <summary>Indica si una tarea pasa los filtros.</summary>
    public static bool Matches(BoardTask task, TaskFilter filter)
    {
        if (filter.Me is { } me && !IsMine(task, me)) return false;
        if (filter.BoardIds is { Count: > 0 } boards && (task.BoardId is not { } board || !boards.Contains(board))) return false;
        if (filter.Labels is { Count: > 0 } labels && !task.Labels.Any(labels.Contains)) return false;
        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var q = filter.Query.Trim();
            bool Has(string? text) => text is not null && Compare.IndexOf(text, q, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
            if (!Has(task.Key) && !Has(task.Title)) return false;
        }
        return true;
    }

    /// <summary>Indica si la tarea está asignada a la persona; por identificador si el perfil lo da, si no por nombre.</summary>
    public static bool IsMine(BoardTask task, BoardPerson me)
    {
        if (task.AssigneeId is { } id && me.Id is { } myId) return string.Equals(id, myId, StringComparison.OrdinalIgnoreCase);
        return task.Assignee is { } name && me.Name is { } myName && string.Equals(name, myName, StringComparison.OrdinalIgnoreCase);
    }
}
