using Avalonia.Threading;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Una sesión leída de su transcript, compartida por todas las páginas que la enseñan: la sección Sesiones y las
/// ventanas aparte.</summary>
/// <remarks>
/// La lectura se queda con el lector, para que en directo solo haya que leer lo nuevo, y es una por sesión aunque la
/// enseñen varias páginas. Mientras la sesión está abierta, un vigilante de su transcript (<see cref="TranscriptWatcher"/>)
/// la lee en cuanto Claude escribe; cada página, además, le pide ponerse al día con su ritmo, que es lo que trae el
/// estado (trabajando, cerrada), porque no está en el transcript. Cada vez que cambia algo sube <see cref="Version"/> y
/// se avisa con <see cref="Updated"/>, y todas las páginas que la enseñan mandan lo nuevo a la vez, la haya leído quien
/// la haya leído: si cada una esperaba a su propio ritmo, una iba hasta segundo y medio por detrás de otra. Una sesión
/// cerrada se lee como definitiva: un agente cuya llamada no aparece va por su hora en lugar de esperarla. La sesión se
/// olvida cuando la suelta la última página que la enseña.
/// </remarks>
internal sealed class SessionFeed
{
    // Las sesiones que enseña alguna página, con cuántas la enseñan.
    private static readonly Dictionary<string, (SessionFeed Feed, int Users)> Feeds = new(StringComparer.OrdinalIgnoreCase);

    // Lo que vale una lectura recién hecha: la página que llega justo después recibe esa en lugar de leer otra vez.
    private static readonly TimeSpan Fresh = TimeSpan.FromMilliseconds(500);

    private static Func<IReadOnlyList<SessionState>> _open = () => [];

    // El lector no admite dos lecturas a la vez.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SessionTraceBuilder? _builder;
    private DateTime _readUtc;

    // El transcript que se lee y su vigilante, mientras la sesión está abierta; y si llegó un aviso con una lectura en
    // marcha, que hay que repetir al acabar.
    private string? _file;
    private TranscriptWatcher? _watcher;
    private bool _again;

    private SessionFeed(string id) => Id = id;

    /// <summary>Ha cambiado lo leído y ha subido <see cref="Version"/>: cada página que enseña la sesión manda lo nuevo.
    /// Llega en el hilo de la ventana.</summary>
    public event Action? Updated;

    /// <summary>El id de la sesión.</summary>
    public string Id { get; }

    /// <summary>Lo leído de la sesión la última vez; null si aún no se ha leído o no tiene transcript.</summary>
    public SessionTrace? Trace { get; private set; }

    /// <summary>El resumen de la sesión (título, proyecto, worktree), leído al abrirla.</summary>
    public SessionSummary? Summary { get; private set; }

    /// <summary>Si la sesión sigue abierta, y entonces se sigue en directo.</summary>
    public bool Live { get; private set; }

    /// <summary>Si la sesión está abierta y trabajando ahora mismo.</summary>
    public bool Working { get; private set; }

    /// <summary>Cuántas veces ha cambiado lo leído: una página que vio otra versión tiene algo que mandar.</summary>
    public int Version { get; private set; }

    /// <summary>Recibe de dónde salen las sesiones abiertas, para saber cuáles van en directo.</summary>
    public static void Init(Func<IReadOnlyList<SessionState>> openSessions) => _open = openSessions;

    /// <summary>Las sesiones abiertas ahora.</summary>
    public static IReadOnlyList<SessionState> OpenSessions() => _open();

    /// <summary>La sesión de un id, para una página que la va a enseñar; hay que soltarla con <see cref="Release"/>.</summary>
    public static SessionFeed Acquire(string id)
    {
        if (Feeds.TryGetValue(id, out var known))
        {
            Feeds[id] = known with { Users = known.Users + 1 };
            return known.Feed;
        }
        var feed = new SessionFeed(id);
        Feeds[id] = (feed, 1);
        return feed;
    }

    /// <summary>Suelta la sesión que enseñaba una página; si ya no la enseña ninguna, se olvida lo leído.</summary>
    public void Release()
    {
        if (!Feeds.TryGetValue(Id, out var known) || known.Feed != this) return;
        if (known.Users > 1) Feeds[Id] = known with { Users = known.Users - 1 };
        else
        {
            Feeds.Remove(Id);
            Unwatch();
        }
    }

    /// <summary>Lee la sesión entera la primera vez y, después, solo lo nuevo.</summary>
    /// <param name="path">El transcript, si la página ya lo sabe; si no, se busca.</param>
    /// <returns>Lo leído; null si la sesión ya no tiene transcript en disco.</returns>
    public async Task<SessionTrace?> ReadAsync(string? path)
    {
        var open = OpenIds();
        var live = open.ContainsKey(Id);
        await _gate.WaitAsync();
        try
        {
            if (_builder is { } builder)
            {
                Trace = await Task.Run(() =>
                {
                    builder.Update(final: !live);
                    return builder.Snapshot();
                });
            }
            else
            {
                var found = await Task.Run<(SessionTraceBuilder? Builder, SessionTrace? Trace, SessionSummary? Summary, string? File)>(() =>
                {
                    var file = path ?? (open.TryGetValue(Id, out var q) ? q : SessionsView.FindTranscript(Id));
                    if (file is null) return (null, null, null, null);
                    var reader = new SessionTraceBuilder(file);
                    reader.Update(final: !live);
                    return (reader, reader.Snapshot(), SessionCatalog.Summarize(file), file);
                });
                if (found.Trace is null) return null;
                (_builder, Trace, Summary, _file) = (found.Builder, found.Trace, found.Summary, found.File);
            }
            await Complete(Trace);
            (Live, Working) = (live, IsWorking());
            _readUtc = DateTime.UtcNow;
            Version++;
            Updated?.Invoke();
            return Trace;
        }
        finally
        {
            _gate.Release();
            Watch();
        }
    }

    /// <summary>En directo: lee lo nuevo y, si ha cambiado algo, sube la versión y avisa a las páginas.</summary>
    /// <param name="now">Si lo pide el vigilante del transcript, que acaba de ver una escritura: se lee aunque otra página
    /// lo acabe de hacer y, si hay una lectura en marcha, se repite al acabar.</param>
    /// <remarks>Al cerrarse la sesión se hace una última lectura definitiva, deja de ir en directo y se suelta el
    /// vigilante. También cuenta como cambio que solo cambie el estado: «En directo» se quita al terminar aunque no haya
    /// pasos nuevos.</remarks>
    public async Task FollowAsync(bool now = false)
    {
        if (!Live || _builder is not { } builder || (!now && DateTime.UtcNow - _readUtc < Fresh)) return;
        if (!await _gate.WaitAsync(0))
        {
            if (now) _again = true;
            return;
        }
        try
        {
            var stillOpen = OpenIds().ContainsKey(Id);
            var working = IsWorking();
            var read = await Task.Run(() => builder.Update(final: !stillOpen) ? builder.Snapshot() : null);
            _readUtc = DateTime.UtcNow;
            var changed = read is not null || !stillOpen || working != Working;
            if (read is not null) await Complete(Trace = read);
            (Live, Working) = (stillOpen, working);
            if (changed)
            {
                Version++;
                Updated?.Invoke();
            }
        }
        finally
        {
            _gate.Release();
            Watch();
        }
        if (!_again) return;
        _again = false;
        await FollowAsync(now: true);
    }

    /// <summary>Vigila el transcript mientras la sesión esté abierta y alguna página la enseñe; si ya no, deja de vigilarlo.</summary>
    private void Watch()
    {
        if (!Live || _file is null || !Feeds.TryGetValue(Id, out var known) || known.Feed != this)
        {
            Unwatch();
            return;
        }
        if (_watcher is not null) return;
        try
        {
            _watcher = new TranscriptWatcher(_file);
            _watcher.Changed += () => Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    await FollowAsync(now: true);
                }
                catch (Exception ex)
                {
                    JsonStore.LogError("sessions-live", ex);
                }
            });
        }
        catch (Exception ex)
        {
            // Sin vigilante, la sesión sigue al ritmo de las páginas, como antes.
            JsonStore.LogError("transcript-watch", ex);
        }
    }

    /// <summary>Deja de vigilar el transcript.</summary>
    private void Unwatch()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    /// <summary>Si la sesión está abierta y trabajando ahora mismo: ella, sus agentes o un comando que espera en segundo plano.</summary>
    private bool IsWorking() =>
        _open().Any(s => s.SessionId.Equals(Id, StringComparison.OrdinalIgnoreCase) && s.Status is SessionStatus.Working or SessionStatus.Agents or SessionStatus.Command);

    /// <summary>Transcript de cada sesión abierta, por su id.</summary>
    /// <summary>Lo que dicen los hooks de esta sesión mientras está abierta; null si no lo está.</summary>
    public SessionState? Opened() => _open().FirstOrDefault(s => string.Equals(s.SessionId, Id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Completa lo leído con lo que no trae el transcript: su resumen, en cuanto existe, y la carpeta de la
    /// sesión, que dan los hooks, de la que salen su rama y sus cambios.</summary>
    /// <remarks>Claude Code no crea el transcript hasta el primer prompt: sin esto, una sesión recién abierta sale sin
    /// nombre, sin rama y sin cambios aunque los haya.</remarks>
    private async Task Complete(SessionTrace? trace)
    {
        if (Summary is null && _file is { } file && File.Exists(file)) Summary = await Task.Run(() => SessionCatalog.Summarize(file));
        if (trace is { Cwd: null } && Opened()?.Cwd is { } cwd) trace.Cwd = cwd;
    }

    private static Dictionary<string, string> OpenIds() =>
        _open().Where(s => s.TranscriptPath is not null).ToDictionary(s => s.SessionId, s => s.TranscriptPath!, StringComparer.OrdinalIgnoreCase);
}
