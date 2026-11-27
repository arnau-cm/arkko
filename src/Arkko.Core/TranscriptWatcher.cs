namespace Arkko.Core;

/// <summary>Avisa cuando Claude Code escribe en el transcript de una sesión o en el de uno de sus subagentes.</summary>
/// <remarks>
/// Lo dice el sistema (<see cref="FileSystemWatcher"/>), así que sin escrituras no se hace nada. Se vigila la carpeta del
/// transcript con lo que cuelga de ella, porque los subagentes escriben en <c>&lt;id&gt;/subagents</c>, y solo cuenta lo
/// de esa sesión: en la misma carpeta escriben las demás sesiones del proyecto. Los avisos seguidos se juntan con plazos
/// cortos, porque un paso nuevo tiene que verse al momento. Si el sistema no deja vigilar la carpeta, se avisa cada poco
/// y se anota en errors.log.
/// </remarks>
public sealed class TranscriptWatcher : IDisposable
{
    /// <summary>Lo que se espera sin avisos nuevos antes de avisar.</summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(100);

    /// <summary>Lo que se espera como mucho desde el primer aviso de una tanda, aunque sigan llegando.</summary>
    public static readonly TimeSpan Longest = TimeSpan.FromMilliseconds(400);

    /// <summary>Cada cuánto se avisa cuando el sistema no deja vigilar la carpeta.</summary>
    public static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(2);

    private readonly object _gate = new();
    private readonly ChangeBatch _batch;
    private readonly TimeSpan _pollEvery;
    private readonly string _name;
    private readonly string _subagents;
    private FileSystemWatcher? _watcher;
    private Timer? _poll;
    private bool _disposed;

    /// <summary>Empieza a vigilar el transcript de una sesión.</summary>
    public TranscriptWatcher(string transcript) : this(transcript, Quiet, Longest, PollEvery)
    {
    }

    /// <summary>Lo mismo, con otros plazos, para las pruebas.</summary>
    internal TranscriptWatcher(string transcript, TimeSpan quiet, TimeSpan longest, TimeSpan pollEvery)
    {
        _pollEvery = pollEvery;
        _batch = new ChangeBatch(quiet, longest, () => Changed?.Invoke());
        var full = Path.GetFullPath(transcript);
        _name = Path.GetFileName(full);
        _subagents = Path.GetFileNameWithoutExtension(full) + "/";
        try
        {
            var watcher = new FileSystemWatcher(Path.GetDirectoryName(full)!)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
            };
            var folder = watcher.Path;
            FileSystemEventHandler on = (_, e) =>
            {
                if (Counts(Path.GetRelativePath(folder, e.FullPath))) _batch.Poke();
            };
            watcher.Changed += on;
            watcher.Created += on;
            watcher.Renamed += (_, e) =>
            {
                if (Counts(Path.GetRelativePath(folder, e.FullPath))) _batch.Poke();
            };
            watcher.Error += (_, e) =>
            {
                if (e.GetException() is InternalBufferOverflowException) _batch.Poke();
                else StartPolling(e.GetException());
            };
            lock (_gate) _watcher = watcher;
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or PlatformNotSupportedException)
        {
            StartPolling(ex);
        }
    }

    /// <summary>Si el sistema no deja vigilar la carpeta y se avisa cada poco.</summary>
    public bool Polling { get; private set; }

    /// <summary>Se ha escrito algo de la sesión. Llega en un hilo de fondo.</summary>
    public event Action? Changed;

    /// <summary>Deja de vigilar.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            StopWatcher();
            _batch.Dispose();
            _poll?.Dispose();
            _poll = null;
        }
    }

    /// <summary>Pasa a avisar cada poco, como si el sistema no dejara vigilar la carpeta; para las pruebas, sin anotarlo
    /// en errors.log.</summary>
    internal void ForcePolling() => StartPolling(null);

    /// <summary>Si un fichero de la carpeta es de la sesión: su transcript o lo que cuelga de su carpeta de subagentes.</summary>
    private bool Counts(string relative)
    {
        relative = relative.Replace('\\', '/');
        return relative.Equals(_name, StringComparison.OrdinalIgnoreCase) || relative.StartsWith(_subagents, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Deja de vigilar con el sistema y pasa a avisar cada poco, una sola vez.</summary>
    private void StartPolling(Exception? why)
    {
        lock (_gate)
        {
            if (_disposed || Polling) return;
            Polling = true;
            StopWatcher();
            _poll = new Timer(_ => Changed?.Invoke(), null, _pollEvery, _pollEvery);
        }
        if (why is not null) JsonStore.LogError("transcript-watch", why);
    }

    /// <summary>Suelta el vigilante del sistema.</summary>
    private void StopWatcher()
    {
        if (_watcher is null) return;
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _watcher = null;
    }
}
