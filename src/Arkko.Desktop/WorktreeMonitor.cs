using Avalonia.Threading;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Los worktrees de los repositorios en que han trabajado las sesiones, compartidos por Worktrees y el Tablero.</summary>
/// <remarks>
/// Se leen fuera del hilo de la ventana, porque lanzan git en cada worktree y preguntan al remoto, y en dos tiempos:
/// primero lo local, que tarda un segundo, y después las PR, que esperan al remoto. Lo del remoto lo guarda el Core
/// media hora. Se lee al poco de arrancar y cada 10 minutos, para que al entrar en Worktrees ya esté.
/// De paso lee qué subtablero declara cada repositorio, para sus colores (<see cref="RepoColors"/>); eso, también nada
/// más arrancar, porque no necesita git.
/// </remarks>
public sealed class WorktreeMonitor
{
    // Van antes que Instance: los estáticos se inicializan en el orden en que están escritos.
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan FirstAfter = TimeSpan.FromSeconds(15);

    /// <summary>Edad a partir de la cual una lectura se repite al entrar en Worktrees o en el Tablero.</summary>
    public static readonly TimeSpan Stale = TimeSpan.FromMinutes(5);

    public static WorktreeMonitor Instance { get; } = new();

    private readonly DispatcherTimer _timer = new() { Interval = Every };
    private bool _started;
    private bool _reading;
    private bool _asking;

    private WorktreeMonitor() => _timer.Tick += async (_, _) => await RefreshAsync();

    /// <summary>Se dispara en el hilo de la ventana al empezar una lectura y al acabar cada uno de sus dos tiempos.</summary>
    public event Action? Changed;

    /// <summary>Lo último leído, por repositorio; vacío hasta la primera lectura.</summary>
    public IReadOnlyList<RepoWorktrees> Repos { get; private set; } = [];

    /// <summary>Cuándo acabó la última lectura de lo local; null si aún no se ha leído.</summary>
    public DateTime? ReadUtc { get; private set; }

    /// <summary>Se está leyendo, lo local o las PR.</summary>
    public bool Reading => _reading;

    /// <summary>Ya está lo local y se espera al remoto por las PR.</summary>
    public bool AskingRemote => _asking;

    /// <summary>Empieza a leer en segundo plano: poco después de arrancar y, luego, cada 10 minutos.</summary>
    public void Start()
    {
        if (_started) return;
        _started = true;
        _ = ReadBoardsAsync();
        DispatcherTimer.RunOnce(async () => await RefreshAsync(), FirstAfter);
        _timer.Start();
    }

    /// <summary>Lee al momento qué subtablero declara cada repositorio, para sus colores.</summary>
    /// <remarks>Es leer un fichero por repositorio: no espera a la primera lectura de los worktrees, que va 15 segundos
    /// después de arrancar y lanza git en cada uno. Hasta entonces, un repositorio sin color elegido salía en gris.</remarks>
    private static async Task ReadBoardsAsync()
    {
        try
        {
            RepoColors.SetRepos(await Task.Run(() => RepoBoards.Read(SessionCatalog.Folders())));
        }
        catch (Exception ex)
        {
            JsonStore.LogError("worktrees", ex);
        }
    }

    /// <summary>Vuelve a leer si no se ha leído o lo leído es más viejo que <paramref name="maxAge"/>.</summary>
    /// <param name="fresh">Si se pregunta al remoto aunque su última respuesta tenga menos de media hora.</param>
    public async Task RefreshAsync(TimeSpan? maxAge = null, bool fresh = false)
    {
        if (_reading) return;
        if (!fresh && ReadUtc is { } at && maxAge is { } age && DateTime.UtcNow - at < age) return;
        _reading = true;
        Changed?.Invoke();
        try
        {
            var (repos, boards) = await Task.Run(() =>
            {
                var folders = SessionCatalog.Folders();
                return (Worktrees.Read(folders, remote: false), RepoBoards.Read(folders));
            });
            Repos = repos;
            ReadUtc = DateTime.UtcNow;
            RepoColors.SetRepos(boards);
            if (fresh || Worktrees.NeedsRemote(repos))
            {
                _asking = true;
                Changed?.Invoke();
                // La carpeta de git de cada repositorio lleva a él igual que cualquiera de sus worktrees.
                Repos = await Task.Run(() => Worktrees.Read(repos.Select(r => r.CommonDir), remote: true, fresh));
                ReadUtc = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            JsonStore.LogError("worktrees", ex);
        }
        finally
        {
            _reading = false;
            _asking = false;
            Changed?.Invoke();
        }
    }

    /// <summary>El worktree de cada tarea, por la clave que sale del nombre de su carpeta.</summary>
    /// <remarks>Si la misma clave tiene carpeta en dos repositorios, cuenta la que tiene PR.</remarks>
    public Dictionary<string, (RepoWorktrees Repo, WorktreeInfo Item)> ByKey(BoardSettings settings) =>
        Repos.SelectMany(r => r.Worktrees.Where(w => !w.IsBase).Select(w => (Key: TaskLinks.KeyOf(settings, w.Folder), Repo: r, Item: w)))
            .Where(x => x.Key is not null)
            .GroupBy(x => x.Key!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Item.Pr is not null).Select(x => (x.Repo, x.Item)).First(),
                StringComparer.OrdinalIgnoreCase);
}
