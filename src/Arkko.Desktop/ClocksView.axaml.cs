using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Sección «Relojes»: un reloj por sesión abierta, todos con la misma hora, en una página web local.</summary>
/// <remarks>
/// La página solo dibuja: la app lee cada sesión abierta con la lectura que comparten las páginas que la enseñan
/// (<see cref="SessionFeed"/>), saca de ella su última hora (<see cref="SessionClock"/>) y se la manda junto con su
/// estado, el mismo de Inicio. Las sesiones solo se tienen leídas mientras se está en la sección.
/// </remarks>
public partial class ClocksView : UserControl
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // Sin la sección a la vista durante este rato, se descarga el navegador.
    private static readonly TimeSpan IdleUnload = TimeSpan.FromMinutes(2);

    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly DispatcherTimer _unload = new() { Interval = IdleUnload };
    private readonly DispatcherTimer _soon = new() { Interval = TimeSpan.FromMilliseconds(300) };

    // Cada sesión que se está leyendo, por su id.
    private readonly Dictionary<string, SessionFeed> _feeds = new(StringComparer.OrdinalIgnoreCase);

    private MainViewModel? _home;
    private Func<IReadOnlyList<SessionState>> _open = () => [];
    private NativeWebView? _web;
    private bool _ready, _active, _listening;

    /// <summary>Se pulsa el reloj de una sesión: hay que abrirla en Sesiones.</summary>
    public event Action<string>? SessionRequested;

    /// <summary>Crea la sección; la página no se carga hasta que se entra en ella.</summary>
    public ClocksView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) =>
        {
            App.ThemeChanged += SendTheme;
            RepoColors.Changed += Soon;
        };
        DetachedFromVisualTree += (_, _) =>
        {
            App.ThemeChanged -= SendTheme;
            RepoColors.Changed -= Soon;
        };
        _tick.Tick += (_, _) => Follow();
        _soon.Tick += (_, _) =>
        {
            _soon.Stop();
            Send();
        };
        _unload.Tick += (_, _) =>
        {
            _unload.Stop();
            if (!_active) Unload();
        };
    }

    /// <summary>Recibe las filas de Inicio, de donde salen el estado de cada sesión, y las sesiones abiertas.</summary>
    public void Init(MainViewModel home, Func<IReadOnlyList<SessionState>> openSessions)
    {
        _home = home;
        _open = openSessions;
        Listen(true);
    }

    /// <summary>Empieza o deja de escuchar los cambios de las sesiones de Inicio, sin apuntarse dos veces.</summary>
    private void Listen(bool on)
    {
        if (_home is null || on == _listening) return;
        if (on) _home.PropertyChanged += OnHomeChanged;
        else _home.PropertyChanged -= OnHomeChanged;
        _listening = on;
    }

    /// <summary>Entra en la sección: carga la página si hace falta, lee las sesiones abiertas y empieza a seguirlas.</summary>
    public void Activate()
    {
        _active = true;
        Listen(true);
        _unload.Stop();
        if (_web is null) Start();
        Sync();
        _tick.Start();
        Send();
    }

    /// <summary>Sale de la sección: deja de seguir las sesiones y suelta lo leído; el navegador se descarga al rato.</summary>
    public void Deactivate()
    {
        _active = false;
        _tick.Stop();
        _soon.Stop();
        foreach (var feed in _feeds.Values)
        {
            feed.Updated -= Soon;
            feed.Release();
        }
        _feeds.Clear();
        if (_web is not null) _unload.Start();
    }

    /// <summary>Se deja de usar para siempre, como un widget que se quita del corcho: suelta las sesiones, deja de escuchar
    /// a Inicio y descarga el navegador ya.</summary>
    public void Close()
    {
        Deactivate();
        _unload.Stop();
        Listen(false);
        Unload();
    }

    /// <summary>Cambian las sesiones de Inicio: se leen las nuevas, se sueltan las cerradas y se repinta.</summary>
    private void OnHomeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_active || e.PropertyName != nameof(MainViewModel.Sessions)) return;
        Sync();
        Soon();
    }

    /// <summary>Deja leyéndose justo las sesiones abiertas: empieza a leer las nuevas y suelta las que se cerraron.</summary>
    private void Sync()
    {
        var open = _open().Where(s => s.TranscriptPath is not null).ToDictionary(s => s.SessionId, StringComparer.OrdinalIgnoreCase);
        foreach (var id in _feeds.Keys.Where(id => !open.ContainsKey(id)).ToList())
        {
            _feeds[id].Updated -= Soon;
            _feeds[id].Release();
            _feeds.Remove(id);
        }
        foreach (var (id, state) in open)
        {
            if (_feeds.ContainsKey(id)) continue;
            var feed = SessionFeed.Acquire(id);
            feed.Updated += Soon;
            _feeds[id] = feed;
            _ = Read(feed, state.TranscriptPath);
        }
    }

    /// <summary>Lee una sesión por primera vez y repinta al acabar.</summary>
    private async Task Read(SessionFeed feed, string? path)
    {
        try
        {
            await feed.ReadAsync(path);
            Soon();
        }
        catch (Exception ex)
        {
            JsonStore.LogError("clocks-read", ex);
        }
    }

    /// <summary>Pone al día cada sesión con lo nuevo de su transcript y repinta, para que la aguja avance.</summary>
    private async void Follow()
    {
        try
        {
            foreach (var feed in _feeds.Values.ToList()) await feed.FollowAsync();
        }
        catch (Exception ex)
        {
            JsonStore.LogError("clocks-follow", ex);
        }
        Send();
    }

    /// <summary>Repinta dentro de un momento, para juntar en uno los avisos de varias sesiones seguidas.</summary>
    private void Soon()
    {
        if (!_active) return;
        _soon.Stop();
        _soon.Start();
    }

    /// <summary>Manda a la página el reloj de cada sesión abierta, por orden de apertura.</summary>
    private void Send()
    {
        if (!_ready || !_active || _home is null) return;
        var now = DateTime.UtcNow;
        var started = _open().ToDictionary(s => s.SessionId, s => s.StartedUtc, StringComparer.OrdinalIgnoreCase);
        var rows = _home.Sessions
            .OrderBy(r => started.TryGetValue(r.SessionId, out var at) ? at : DateTime.MaxValue)
            .ThenBy(r => r.SessionId, StringComparer.Ordinal)
            .ToList();
        var sessions = rows.Select(row =>
        {
            var trace = _feeds.TryGetValue(row.SessionId, out var feed) ? feed.Trace : null;
            var running = row.Working || row.Agents || row.Command || row.Waiting;
            var face = trace is null ? null : SessionClock.Build(trace, now, running);
            var state = StateOf(row);
            var color = RepoColors.Of(row.Title);
            var prompt = trace?.Interactions.LastOrDefault(p => p.StartUtc is not null);
            return new
            {
                id = row.SessionId,
                title = row.Title,
                worktree = row.Worktree,
                color = Hex(color),
                band = Hex(RepoColors.Band(color)),
                task = row.TaskKey,
                taskTitle = row.TaskTitle,
                state,
                status = row.Status,
                since = row.Since,
                doing = Doing(row, state, face),
                prompt = prompt?.Prompt,
                took = prompt is { StartUtc: { } start } ? Format.Duration((running ? now : prompt.EndUtc ?? now) - start) : null,
                model = string.IsNullOrEmpty(row.EffortText) ? row.Model : $"{row.Model} · {row.EffortText}",
                context = row.ContextPct is { } pct ? (int)Math.Round(pct) : (int?)null,
                agents = face?.Rings.Skip(1).SelectMany(r => r.Tags).OrderBy(t => t.At).Select(t => t.Letter).ToList(),
                face,
            };
        }).ToList();
        Post(new { type = "clocks", now = new DateTimeOffset(now).ToUnixTimeMilliseconds(), sessions });
    }

    /// <summary>Un pincel de color liso como «#rrggbb»; null si es de otro tipo.</summary>
    internal static string? Hex(IBrush brush) =>
        brush is ISolidColorBrush { Color: var c } ? $"#{c.R:x2}{c.G:x2}{c.B:x2}" : null;

    /// <summary>El estado de una sesión para su ficha: pregunta, trabaja, espera a sus agentes o a un comando, sin revisar,
    /// terminada o en reposo.</summary>
    internal static string StateOf(SessionRow row) =>
        row.Waiting ? "asking"
        : row.Agents ? "agents"
        : row.Command ? "command"
        : row.Working ? "working"
        : row.NeedsReview ? "review"
        : row.Status == "Terminado" ? "done"
        : "idle";

    /// <summary>Lo que hace la sesión, en una línea: lo que pregunta, la última herramienta con la letra de quien la usa,
    /// o su último resumen si ya no trabaja.</summary>
    internal static string? Doing(SessionRow row, string state, ClockFace? face)
    {
        if (state == "asking") return row.Detail;
        var tool = face?.Doing is { } doing ? (face.DoingLetter is { } letter && letter != "B" ? $"{letter} · {doing}" : doing) : null;
        return state is "working" or "agents" or "command" ? tool ?? row.Detail : row.Recap ?? tool;
    }

    /// <summary>Crea el navegador, con la misma carpeta de datos que el de Sesiones, y carga la página local.</summary>
    private void Start()
    {
        try
        {
            var page = Path.Combine(AppContext.BaseDirectory, "Web", "relojes.html");
            var pageUri = new Uri(page);
            var folder = Path.Combine(AppPaths.Home, "webview-desktop");
            var web = new NativeWebView { IsVisible = false };
            _web = web;
            web.EnvironmentRequested += (_, e) =>
            {
#if !DEBUG
                e.EnableDevTools = false;
#endif
                switch (e)
                {
                    // Los mismos argumentos que Sesiones: dos navegadores con la misma carpeta tienen que pedir lo mismo.
                    case WindowsWebView2EnvironmentRequestedEventArgs edge:
                        edge.UserDataFolder = folder;
                        edge.AdditionalBrowserArguments = "--disable-features=CalculateNativeWinOcclusion --disable-backgrounding-occluded-windows";
                        break;
                    case GtkWebViewEnvironmentRequestedEventArgs gtk:
                        gtk.BaseDataDirectory = folder;
                        gtk.BaseCacheDirectory = Path.Combine(folder, "cache");
                        break;
                }
            };
            web.Background = new SolidColorBrush(PageTheme.PageColor());
            web.NavigationStarted += (_, e) =>
            {
                if (e.Request is { } uri && !string.Equals(uri.AbsolutePath, pageUri.AbsolutePath, StringComparison.OrdinalIgnoreCase)) e.Cancel = true;
            };
            web.NewWindowRequested += (_, e) => e.Handled = true;
            web.WebMessageReceived += (_, e) =>
            {
                if (_web == web) OnMessage(e.Body);
            };
            // Por si la página arrancó antes de que estuviera el puente con la app: se le pide que vuelva a decir que está lista.
            web.NavigationCompleted += async (_, _) =>
            {
                await Task.Delay(300);
                if (!_ready && _web == web) _ = web.InvokeScript("window.agentPulseHello && window.agentPulseHello()");
            };
            Host.Children.Insert(0, web);
            Loading.IsVisible = true;
            web.IsVisible = true;
            web.Opacity = 0;
            web.Navigate(pageUri);
        }
        catch (Exception ex)
        {
            JsonStore.LogError("clocks-webview", ex);
            if (_web is not null) _web.IsVisible = false;
            Loading.IsVisible = false;
            Failure.Text = "No se ha podido abrir la vista de relojes: hace falta el componente WebView2 de Microsoft, que viene con Windows 11. El detalle está en errors.log.";
            Failure.IsVisible = true;
        }
    }

    /// <summary>Descarga el navegador y su página, para que no ocupe memoria mientras no se mira la sección.</summary>
    private void Unload()
    {
        if (_web is null) return;
        Host.Children.Remove(_web);
        _web = null;
        _ready = false;
        Loading.IsVisible = true;
    }

    /// <summary>Atiende lo que pide la página: que está lista, o abrir la sesión de un reloj.</summary>
    private void OnMessage(string? body)
    {
        if (string.IsNullOrEmpty(body)) return;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var m = doc.RootElement;
            switch (m.TryGetProperty("type", out var t) ? t.GetString() : null)
            {
                case "ready":
                    _ready = true;
                    SendTheme();
                    Send();
                    if (_web is { } web)
                    {
                        web.Opacity = 1;
                        Loading.IsVisible = false;
                    }
                    break;
                case "open" when m.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } session:
                    SessionRequested?.Invoke(session);
                    break;
            }
        }
        catch (JsonException)
        {
            // Un mensaje que no se entiende no rompe la sección.
        }
    }

    /// <summary>Manda a la página los colores del tema activo.</summary>
    private void SendTheme()
    {
        if (!_ready || _web is null) return;
        _web.Background = new SolidColorBrush(PageTheme.PageColor());
        Post(PageTheme.Message());
    }

    /// <summary>Llama a la página con un mensaje en JSON.</summary>
    private void Post(object payload)
    {
        if (_web is not { } web) return;
        var json = JsonSerializer.Serialize(payload, Json);
        _ = web.InvokeScript($"window.agentPulseReceive && window.agentPulseReceive({json})");
    }
}
