namespace Arkko.Core;

/// <summary>Avisa cuando cambia en la carpeta de una sesión algo de lo que enseña git: un fichero del repositorio, un
/// commit o un cambio de rama.</summary>
/// <remarks>
/// Lo dice el sistema (<see cref="FileSystemWatcher"/>, que en Windows, Linux y macOS usa los avisos de cada uno), así
/// que sin cambios no se hace nada. Los avisos que llegan seguidos se juntan en uno: se espera a que pase un rato sin
/// ninguno, pero no más de un máximo, para que un cambio largo (compilar, instalar paquetes) se vea igual al poco. De lo
/// interno de git solo cuentan el <c>HEAD</c> y su historial, que cambian con un commit o al cambiar de rama: lo demás
/// lo toca el propio git al mirar lo cambiado, y avisar de eso volvería a mirarlo sin fin. Si el sistema no puede
/// vigilar la carpeta (en Linux, inotify tiene un tope de carpetas vigiladas por usuario), se avisa cada poco, como
/// antes de vigilar, y se anota en errors.log.
/// </remarks>
public sealed class GitWatcher : IDisposable
{
    /// <summary>Lo que se espera sin avisos nuevos antes de avisar.</summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(400);

    /// <summary>Lo que se espera como mucho desde el primer aviso de una tanda, aunque sigan llegando.</summary>
    public static readonly TimeSpan Longest = TimeSpan.FromSeconds(2);

    /// <summary>Cada cuánto se avisa cuando el sistema no deja vigilar la carpeta.</summary>
    public static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(10);

    // Carpetas que cambian mucho y git nunca enseña: los paquetes de Node y lo de Visual Studio.
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase) { "node_modules", ".vs" };

    private readonly object _gate = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly TimeSpan _quiet;
    private readonly TimeSpan _longest;
    private readonly TimeSpan _pollEvery;
    private readonly string? _gitDir;
    private readonly ChangeBatch _batch;
    private Timer? _poll;
    private bool _disposed;

    /// <summary>Empieza a vigilar el repositorio de una carpeta; si no es de ninguno, no vigila nada.</summary>
    /// <remarks>Lanza git para saber dónde está el repositorio: no se debe crear desde el hilo de una ventana.</remarks>
    public GitWatcher(string? cwd) : this(cwd, Quiet, Longest, PollEvery)
    {
    }

    /// <summary>Lo mismo, con otros plazos, para las pruebas.</summary>
    internal GitWatcher(string? cwd, TimeSpan quiet, TimeSpan longest, TimeSpan pollEvery)
    {
        (_quiet, _longest, _pollEvery) = (quiet, longest, pollEvery);
        _batch = new ChangeBatch(_quiet, _longest, () => Changed?.Invoke());
        Root = GitChanges.RootOf(cwd, out _);
        if (Root is null) return;
        var gitDir = GitChanges.Run(Root, "rev-parse", "--absolute-git-dir");
        _gitDir = gitDir.Ok ? Path.GetFullPath(gitDir.Out.Trim()) : null;
        try
        {
            Watch(Root, OnRepo);
            // En un worktree, lo de git está fuera de su carpeta: se vigila aparte.
            if (_gitDir is not null && Path.GetRelativePath(Root, _gitDir).StartsWith("..", StringComparison.Ordinal)) Watch(_gitDir, OnGitDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or PlatformNotSupportedException)
        {
            StartPolling(ex);
        }
    }

    /// <summary>La carpeta raíz del repositorio vigilado; null si la carpeta no es de ninguno.</summary>
    public string? Root { get; }

    /// <summary>Si el sistema no deja vigilar la carpeta y se avisa cada poco.</summary>
    public bool Polling { get; private set; }

    /// <summary>Ha cambiado algo de lo que enseña git. Llega en un hilo de fondo.</summary>
    public event Action? Changed;

    /// <summary>Deja de vigilar.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            StopWatchers();
            _batch.Dispose();
            _poll?.Dispose();
            _poll = null;
        }
    }

    /// <summary>Pasa a avisar cada poco, como si el sistema no dejara vigilar la carpeta; para las pruebas, sin anotarlo
    /// en errors.log.</summary>
    internal void ForcePolling() => StartPolling(null);

    /// <summary>Vigila una carpeta entera, con todo lo que cuelga de ella.</summary>
    private void Watch(string folder, Func<string, bool> counts)
    {
        var watcher = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            // Cabe una tanda grande (un checkout) sin perder avisos; si aun así se pierden, se avisa igual.
            InternalBufferSize = 64 * 1024,
        };
        FileSystemEventHandler on = (_, e) =>
        {
            if (counts(Path.GetRelativePath(folder, e.FullPath).Replace('\\', '/'))) Poke();
        };
        watcher.Changed += on;
        watcher.Created += on;
        watcher.Deleted += on;
        watcher.Renamed += (_, e) =>
        {
            if (counts(Path.GetRelativePath(folder, e.FullPath).Replace('\\', '/')) || counts(Path.GetRelativePath(folder, e.OldFullPath).Replace('\\', '/'))) Poke();
        };
        watcher.Error += (_, e) =>
        {
            if (e.GetException() is InternalBufferOverflowException) Poke();
            else StartPolling(e.GetException());
        };
        lock (_gate) _watchers.Add(watcher);
        watcher.EnableRaisingEvents = true;
    }

    /// <summary>Si un cambio en el repositorio cuenta: lo de dentro de git, solo su <c>HEAD</c> y su historial; lo demás,
    /// salvo lo que cuelga de una carpeta que git nunca enseña.</summary>
    private static bool OnRepo(string relative)
    {
        var parts = relative.Split('/');
        if (parts[0] == ".git") return relative is ".git/HEAD" or ".git/logs/HEAD";
        return !parts.Any(Ignored.Contains);
    }

    /// <summary>Si un cambio en la carpeta de git de un worktree cuenta: solo su <c>HEAD</c> y su historial.</summary>
    private static bool OnGitDir(string relative) => relative is "HEAD" or "logs/HEAD";

    /// <summary>Apunta un aviso, que se junta con los que lleguen seguidos.</summary>
    private void Poke() => _batch.Poke();

    /// <summary>Deja de vigilar con el sistema y pasa a avisar cada poco, una sola vez.</summary>
    private void StartPolling(Exception? why)
    {
        lock (_gate)
        {
            if (_disposed || Polling) return;
            Polling = true;
            StopWatchers();
            _poll = new Timer(_ => Changed?.Invoke(), null, _pollEvery, _pollEvery);
        }
        if (why is not null) JsonStore.LogError("git-watch", why);
    }

    /// <summary>Suelta los vigilantes del sistema.</summary>
    private void StopWatchers()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();
    }
}
