using System.IO;
using System.Net.Http;
using Avalonia.Controls;
using Avalonia.Threading;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Las tareas leídas de los tableros elegidos y lo que ha costado cada una, compartido por Tareas, el Inicio y Sesiones.</summary>
/// <remarks>
/// Lee en segundo plano al arrancar y cada 15 minutos, y Tareas lo hace además a su ritmo. Todo queda en
/// memoria: en disco solo van las claves con su columna, para apuntar sus cambios (TaskHistory). Un tablero
/// que no se puede leer conserva lo último bueno.
/// </remarks>
public sealed class TaskDirectory
{
    // Van antes que Instance: los estáticos se inicializan en el orden en que están escritos, y al crear la
    // instancia su temporizador toma Every. Detrás de ella valdría cero, y el temporizador se dispararía sin
    // parar leyendo los tableros una y otra vez.
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(15);

    // Margen en que se da por buena la última comprobación del coste por tarea: lo que tarda un repintado de sobra.
    private static readonly TimeSpan UsageRecheck = TimeSpan.FromMilliseconds(500);

    public static TaskDirectory Instance { get; } = new(new BoardStore());

    private readonly BoardStore _store;
    private readonly Dictionary<string, (List<BoardTask> Tasks, DateTime ReadUtc)> _read = new();
    private readonly Dictionary<string, BoardPerson?> _me = new();
    private readonly Dictionary<string, List<BoardRef>> _subBoards = new();
    private readonly DispatcherTimer _timer = new() { Interval = Every };
    // Un cliente HTTP por conexión y por aquello en que confía, para no repetir el saludo TLS en cada lectura.
    private readonly Dictionary<string, HttpClient> _http = new();
    private bool _started;
    private bool _refreshing;

    // Las tareas de cada sesión y el coste por tarea, rehechos solo si cambia el histórico, la configuración o lo hecho a mano.
    private string _usageKey = "";
    // Cuándo se comprobó por última vez si había que rehacerlas: un repintado las pide una vez por sesión.
    private DateTime _usageCheckedUtc;
    private Dictionary<string, TaskUsage> _byTask = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, TaskUsage> _bySession = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, SessionTasks> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, HashSet<string>> _sessionsByTask = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, SessionLink> _links = new(StringComparer.OrdinalIgnoreCase);

    private TaskDirectory(BoardStore store)
    {
        _store = store;
        _timer.Tick += async (_, _) => await RefreshAllAsync();
    }

    /// <summary>Se dispara cuando hay tareas nuevas leídas, para que quien enseña sus títulos se repinte.</summary>
    public event Action? Changed;

    public BoardStore Store => _store;

    /// <summary>La configuración de los tableros, leída de nuevo en cada llamada para ver los cambios de Configuración.</summary>
    public BoardSettings Settings => _store.Load();

    /// <summary>Empieza a leer en segundo plano: unos segundos después de arrancar y, luego, cada 15 minutos.</summary>
    public void Start()
    {
        if (_started) return;
        _started = true;
        var first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        first.Tick += async (_, _) =>
        {
            first.Stop();
            await RefreshAllAsync();
        };
        first.Start();
        _timer.Start();
    }

    /// <summary>Los tableros que se pueden ver: los elegidos de las conexiones que funcionan y cuyo perfil sigue importado.</summary>
    public List<BoardOption> Boards()
    {
        var settings = _store.Load();
        var profiles = _store.Profiles();
        return settings.Connections
            .Where(c => c.VerifiedUtc is not null)
            .SelectMany(c => profiles.FirstOrDefault(p => string.Equals(p.Id, c.Profile, StringComparison.OrdinalIgnoreCase)) is { } profile
                ? c.Boards.Select(b => new BoardOption(c, profile, b))
                : Enumerable.Empty<BoardOption>())
            .ToList();
    }

    /// <summary>Lo último leído de un tablero; null si aún no se ha leído.</summary>
    public (List<BoardTask> Tasks, DateTime ReadUtc)? ReadOf(string boardId) => _read.TryGetValue(boardId, out var r) ? r : null;

    /// <summary>Los subtableros de un tablero elegido entero, como proyecto; vacío si es un subtablero o aún no se han leído.</summary>
    public List<BoardRef> SubBoardsOf(string boardId) => _subBoards.GetValueOrDefault(boardId) ?? [];

    /// <summary>Quién es la persona en una conexión, si su perfil lo dice y ya se ha leído.</summary>
    public BoardPerson? MeOf(string connectionId) => _me.GetValueOrDefault(connectionId);

    /// <summary>Lee un tablero; si falla, lanza BoardException con el motivo y deja lo último bueno.</summary>
    public async Task ReadAsync(BoardOption board)
    {
        var client = _store.Client(board.Profile, board.Connection, Http(board.Connection));
        var project = new BoardProject(board.Selection.ProjectId, board.Selection.ProjectKey, board.Selection.ProjectName);
        var boardRef = board.Selection.BoardId is { } id ? new BoardRef(id, board.Selection.BoardName ?? "") : null;
        var tasks = await client.TasksAsync(project, boardRef);
        if (!_me.ContainsKey(board.Connection.Id)) _me[board.Connection.Id] = await client.MeAsync();
        if (boardRef is null)
        {
            await ReadSubBoardsAsync(client, project, board.Id);
            tasks = await TagSubBoardsAsync(client, project, tasks, SubBoardsOf(board.Id));
        }
        _read[board.Id] = (tasks, DateTime.UtcNow);
        try
        {
            TaskHistory.Record(tasks, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            JsonStore.LogError("task-history", ex);
        }
        Changed?.Invoke();
    }

    /// <summary>El cliente HTTP de una conexión: el mismo entre lecturas mientras no cambie en qué confía.</summary>
    /// <remarks>Si cambia la huella o la raíz en que confía, se hace otro y se suelta el anterior.</remarks>
    private HttpClient Http(BoardConnection connection)
    {
        var trust = BoardStore.Trust(connection);
        var root = trust.RootCertificate is { Length: > 0 } cert ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(cert)) : "";
        var key = $"{connection.Id}|{trust.PinnedSha256}|{root}";
        if (_http.TryGetValue(key, out var http)) return http;
        foreach (var old in _http.Keys.Where(k => k.StartsWith(connection.Id + "|", StringComparison.Ordinal)).ToList())
        {
            _http[old].Dispose();
            _http.Remove(old);
        }
        return _http[key] = BoardHttp.Create(trust);
    }

    /// <summary>Lee los subtableros de un proyecto, para verlo por subtableros; si falla, se anota y se queda lo último bueno.</summary>
    private async Task ReadSubBoardsAsync(BoardClient client, BoardProject project, string boardId)
    {
        try
        {
            _subBoards[boardId] = await client.BoardsAsync(project);
        }
        catch (Exception ex)
        {
            JsonStore.LogError("tasks-boards", ex);
        }
    }

    /// <summary>Pone a cada tarea del proyecto su subtablero, si el proveedor no lo dice al listarlas.</summary>
    /// <remarks>
    /// Hay paneles, como el de la empresa, que filtran por subtablero pero no lo devuelven en cada tarea: se
    /// leen entonces las de cada subtablero y se marcan por su clave. Un subtablero que falla se salta.
    /// </remarks>
    private static async Task<List<BoardTask>> TagSubBoardsAsync(BoardClient client, BoardProject project, List<BoardTask> tasks, List<BoardRef> subBoards)
    {
        if (subBoards.Count == 0 || tasks.All(t => t.BoardId is not null)) return tasks;
        var boardOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sub in subBoards)
        {
            try
            {
                foreach (var task in await client.TasksAsync(project, sub)) boardOf.TryAdd(task.Key, sub.Id);
            }
            catch (Exception ex)
            {
                JsonStore.LogError("tasks-boards", ex);
            }
        }
        return tasks.Select(t => t.BoardId is null && boardOf.TryGetValue(t.Key, out var id) ? t with { BoardId = id } : t).ToList();
    }

    /// <summary>Lee todos los tableros elegidos; los fallos se anotan y no paran a los demás.</summary>
    public async Task RefreshAllAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            foreach (var board in Boards())
            {
                // Lo que el Tablero ha leído hace poco ya está al día.
                if (ReadOf(board.Id) is { } read && DateTime.UtcNow - read.ReadUtc < Every / 2) continue;
                try
                {
                    await ReadAsync(board);
                }
                catch (Exception ex)
                {
                    JsonStore.LogError("tasks-background", ex);
                }
            }
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>Una tarea por su clave, en cualquiera de los tableros leídos, con el tablero en que está; null si no se ha leído.</summary>
    public (BoardTask Task, BoardOption Board)? Find(string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        foreach (var board in Boards())
            if (ReadOf(board.Id)?.Tasks.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase)) is { } task)
                return (task, board);
        return null;
    }

    /// <summary>Las tareas de una sesión: las que dicen sus prompts y lo hecho a mano o, si aún no tiene prompts guardados, la de su carpeta.</summary>
    public SessionTasks TasksOfSession(string sessionId, string? folder)
    {
        RefreshUsage();
        if (_sessions.TryGetValue(sessionId, out var known)) return known;
        // Sin prompts aún: la de su carpeta, más lo añadido a mano.
        var link = _links.GetValueOrDefault(sessionId);
        var folderKey = TaskLinks.KeyOf(_store.Load(), folder);
        var removed = new HashSet<string>(link?.Removed ?? [], StringComparer.OrdinalIgnoreCase);
        var manual = (link?.Added ?? []).Where(k => !removed.Contains(k)).ToList();
        var (principal, tasks) = TaskLinks.Choose(new[] { folderKey }.Concat(manual), removed, link?.Principal);
        return new SessionTasks(sessionId, principal, tasks, manual, []);
    }

    /// <summary>Las sugerencias de una sesión que se pueden enseñar: tareas leídas del tablero, del mismo proyecto del panel que su principal.</summary>
    public List<string> SuggestionsOf(SessionTasks tasks)
    {
        if (tasks.Suggestions.Count == 0 || tasks.Principal is null) return [];
        var project = Find(tasks.Principal)?.Board.Selection.ProjectId;
        return tasks.Suggestions
            .Where(k => Find(k) is { } found && (project is null || found.Board.Selection.ProjectId == project))
            .ToList();
    }

    /// <summary>La línea de una tarea por su clave: «DEVLAB-99 · Mesa de planificación», o solo la clave si aún no se ha leído; null sin clave.</summary>
    public string? LineOf(string? key) =>
        key is null ? null : Find(key) is { } found ? $"{found.Task.Key} · {found.Task.Title}" : key;

    /// <summary>Las sesiones que trabajan en una tarea, sea o no su principal, y si una sesión tiene prompts guardados.</summary>
    public (HashSet<string> Sessions, Func<string, bool> HasPrompts) SessionsOf(string key)
    {
        RefreshUsage();
        var sessions = _sessionsByTask.GetValueOrDefault(key) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var known = _sessions;
        return (sessions, id => known.ContainsKey(id));
    }

    /// <summary>De cada tarea trabajada en sesiones cuya principal es otra: en cuántas, a qué tarea va su coste y lo que gastaron.</summary>
    /// <remarks>
    /// Su coste cuenta en la principal, así que sin esto, con la sesión ya cerrada, la tarea no tendría nada que enseñar.
    /// Lo gastado es el de esas sesiones enteras: se enseña aparte, sin sumarlo al de la tarea, que lo contaría dos veces.
    /// </remarks>
    public Dictionary<string, SharedWork> SharedByTask()
    {
        RefreshUsage();
        var shared = new Dictionary<string, SharedWork>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, ids) in _sessionsByTask)
        {
            var others = ids.Select(id => _sessions.GetValueOrDefault(id)).OfType<SessionTasks>()
                .Where(s => s.Principal is { } principal && !string.Equals(principal, key, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (others.Count == 0) continue;
            var spent = others.Select(s => _bySession.GetValueOrDefault(s.SessionId)).OfType<TaskUsage>().ToList();
            var usage = spent.Count == 0
                ? null
                : new TaskUsage(key, spent.Sum(u => u.Prompts), others.Count, spent.Sum(u => u.NewTokens), TimeSpan.FromTicks(spent.Sum(u => u.Working.Ticks)),
                    spent.Min(u => u.FirstUtc), spent.Max(u => u.LastUtc));
            shared[key] = new SharedWork(others.Count, others[0].Principal!, usage);
        }
        return shared;
    }

    /// <summary>Las sesiones que tienen añadida a mano alguna tarea, aunque aún no tengan prompts guardados.</summary>
    public IReadOnlyDictionary<string, SessionLink> Links
    {
        get
        {
            RefreshUsage();
            return _links;
        }
    }

    /// <summary>Las tareas leídas de todos los tableros, sin repetir, para elegir una; las descartadas y del historial, no.</summary>
    public List<BoardTask> AllTasks() =>
        Boards().SelectMany(b => ReadOf(b.Id)?.Tasks ?? [])
            .Where(t => t.State is not (TaskState.Discarded or TaskState.ToDiscard))
            .GroupBy(t => t.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

    /// <summary>Añade a mano una tarea a una sesión; se guarda en el plan.</summary>
    public void AddTask(string sessionId, string key) => ChangeLinks(plan => plan.AddTask(sessionId, key));

    /// <summary>Quita una tarea de una sesión, o descarta una sugerencia; se guarda en el plan.</summary>
    public void RemoveTask(string sessionId, string key) => ChangeLinks(plan => plan.RemoveTask(sessionId, key));

    /// <summary>Hace de una tarea la principal de una sesión, la que se lleva su coste; se guarda en el plan.</summary>
    public void SetPrincipal(string sessionId, string key) => ChangeLinks(plan => plan.SetPrincipal(sessionId, key));

    /// <summary>Si se puede quitar una tarea de una sesión: si es su principal, lo pregunta antes, diciendo a dónde va su coste.</summary>
    public Task<bool> ConfirmRemoveAsync(Window? owner, string sessionId, string? folder, string key)
    {
        var tasks = TasksOfSession(sessionId, folder);
        if (!string.Equals(tasks.Principal, key, StringComparison.OrdinalIgnoreCase)) return Task.FromResult(true);
        var next = tasks.Tasks.FirstOrDefault(k => !string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
        return AppDialog.ConfirmAsync(owner, $"¿Quitar {key} de la sesión?",
            $"{key} es la tarea principal: se lleva el coste de la sesión. " +
            (next is null ? "Si la quitas, la sesión se queda sin tarea y su coste no cuenta en ninguna." : $"Si la quitas, la principal y su coste pasan a {next}."),
            "Quitar", DialogTone.Warning, note: "Se puede volver a añadir con el «+».");
    }

    private void ChangeLinks(Action<TaskPlan> change)
    {
        var plan = _store.LoadPlan();
        change(plan);
        _store.SavePlan(plan);
        _usageKey = "";
        Changed?.Invoke();
    }

    /// <summary>Lo que ha costado cada tarea, por su clave, en todo el histórico de prompts.</summary>
    public Dictionary<string, TaskUsage> UsageByTask()
    {
        RefreshUsage();
        return _byTask;
    }

    /// <summary>Lo que ha costado cada sesión, por su id, en todo el histórico de prompts.</summary>
    public Dictionary<string, TaskUsage> UsageBySession()
    {
        RefreshUsage();
        return _bySession;
    }

    /// <summary>Rehace las tareas de cada sesión y el coste por tarea si ha cambiado algo de lo que dependen.</summary>
    /// <remarks>
    /// El histórico solo crece por el fichero de hoy: basta mirar cuántos hay y lo que mide el último. También
    /// cuentan el patrón de la clave, la regla del comando, la del nombre y lo añadido o quitado a mano.
    /// </remarks>
    private void RefreshUsage()
    {
        if (_usageKey.Length > 0 && DateTime.UtcNow - _usageCheckedUtc < UsageRecheck) return;
        _usageCheckedUtc = DateTime.UtcNow;
        try
        {
            var files = Directory.Exists(AppPaths.Prompts) ? Directory.GetFiles(AppPaths.Prompts, "*.jsonl") : [];
            var last = files.Order(StringComparer.Ordinal).LastOrDefault();
            var settings = _store.Load();
            var links = _store.LoadPlan().SessionLinks;
            var key = string.Join("|", files.Length, last is null ? 0 : new FileInfo(last).Length, DateTime.Today.Ticks, settings.TaskPattern,
                settings.TaskSwitch,
                string.Join(",", links.OrderBy(l => l.Key).Select(l => $"{l.Key}+{string.Join("+", l.Value.Added)}-{string.Join("-", l.Value.Removed)}")));
            if (key == _usageKey) return;
            var records = History.Load(null);
            _links = links;
            _sessions = TaskLinks.Sessions(records, settings, links);
            _byTask = TaskLinks.UsageByTask(records, settings, links);
            _bySession = TaskLinks.UsageBySession(records);
            _sessionsByTask = TaskLinks.SessionsByTask(_sessions.Values);
            // Las que tienen tareas añadidas a mano y aún no tienen prompts también son de esas tareas.
            foreach (var (session, link) in links.Where(l => !_sessions.ContainsKey(l.Key)))
                foreach (var task in link.Added.Where(k => !link.Removed.Contains(k, StringComparer.OrdinalIgnoreCase)))
                {
                    if (!_sessionsByTask.TryGetValue(task, out var set)) _sessionsByTask[task] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    set.Add(session);
                }
            _usageKey = key;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            JsonStore.LogError("tasks-usage", ex);
        }
    }
}

/// <summary>Lo trabajado en una tarea desde sesiones cuya principal es otra: cuántas, a qué tarea va su coste y lo que gastaron enteras.</summary>
public sealed record SharedWork(int Sessions, string Principal, TaskUsage? Usage);

/// <summary>Un tablero que se puede elegir: una conexión y uno de sus proyectos o subtableros.</summary>
public sealed record BoardOption(BoardConnection Connection, BoardProfile Profile, BoardSelection Selection)
{
    public string Id => TaskPlan.BoardId(Connection.Id, Selection);
    public string Label => $"{Connection.Name} · {Selection.ProjectKey}" + (Selection.BoardName is { } b ? $" › {b}" : "");
}
