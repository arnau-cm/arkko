using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Qué enseña un panel: la ficha de un fichero cambiado («file») o una parte de una sesión («live», el Flujo;
/// «chat»; «changes», los cambios y la revisión; «step», la ficha de un paso; o una de sus vistas, como el Diagrama); de
/// qué sesión, y cómo se llama.</summary>
/// <param name="Cwd">La carpeta de la sesión, en la ficha de un fichero.</param>
/// <param name="Scope">En la ficha de un fichero: «pending» o «branch», si es lo pendiente o toda la rama.</param>
/// <param name="Path">En la ficha de un fichero, su ruta; en la de un paso, la clave de su nodo en el directo.</param>
/// <param name="Tab">En la ficha de un fichero: «diff» o «file», la pestaña con que se abre; en la de un paso, el número
/// de su prompt.</param>
/// <param name="Round">En la ficha de un fichero de toda la rama: la ronda de la revisión con que se abre, o null para
/// toda la rama. No distingue un panel de otro: el fichero es el mismo en cualquier ronda.</param>
internal sealed record PieceSpec(string Kind, string SessionId, string Title, string? Cwd = null, string? Scope = null, string? Path = null, string? Tab = null,
    int? Round = null)
{
    /// <summary>Lo que distingue a un panel de otro: no hay dos abiertos con la misma.</summary>
    public string Key => Kind switch
    {
        "file" => $"{SessionId}:file:{Scope}:{Path}",
        "step" => $"{SessionId}:step:{Path}",
        // Una vista de la app lleva en Path el número de su copia: la primera no lleva, y «Otro widget igual» abre más.
        _ when Path is { } copy && NativeViews.Is(Kind) => $"{SessionId}:{Kind}:{copy}",
        _ => $"{SessionId}:{Kind}",
    };

    /// <summary>Si es una de las partes de una sesión: «live», el Flujo; «chat»; «changes», los cambios y la revisión;
    /// «review», las notas de la revisión, abiertas aparte de los cambios; «history», la historia del fichero abierto;
    /// «step», la ficha de un paso; o una de las otras vistas de Sesiones (<see cref="IsView"/>).</summary>
    public static bool IsPart(string kind) => kind is "live" or "chat" or "changes" or "review" or "history" or "step" || IsView(kind);

    /// <summary>Si es una de las vistas de Sesiones que se clavan aparte del Flujo: «diagram», el Diagrama; «now», la vista
    /// Ahora; «parte», el Parte; u «obra», la Obra.</summary>
    public static bool IsView(string kind) => kind is "diagram" or "now" or "parte" or "obra";

    /// <summary>Devuelve la clase de panel que toca a una guardada: las vistas retiradas abren la que las sustituye (el
    /// Árbol, las Rondas y los Prompts, el Diagrama; la Secuencia, el Parte, que la lleva dentro).</summary>
    public static string Current(string kind) => kind switch
    {
        "tree" or "rounds" or "prompts" => "diagram",
        "sequence" => "parte",
        _ => kind,
    };
}

/// <summary>Un panel con una parte de Sesiones, viva: la ficha de un fichero con su revisión, o el lienzo, el chat o los
/// cambios de una sesión. Vive en una ventana suelta o en un recuadro de un corcho, y se puede pasar de uno a otro.</summary>
/// <remarks>
/// Por dentro carga la misma página que la sección (Web/sesiones.html) en modo pieza, que solo pinta esa parte, y habla
/// con la app por sus propios canales: el de git y la revisión (<see cref="ChangesChannel"/>), con el que lo que se anota
/// aquí se ve en los demás paneles y al revés, y en los de una sesión, el de la sesión (<see cref="SessionChannel"/>),
/// que la sigue en directo con la misma lectura que los demás. El panel es de su sesión: cambiar de sesión en la sección
/// no lo toca. Al cambiarlo de sitio, su navegador se vuelve a crear y la página se recarga: lo que enseña vive en la
/// app, así que vuelve igual. No hay dos paneles con lo mismo: pedir otra vez uno abierto lo trae a la vista.
/// </remarks>
internal sealed class PiecePane : Grid
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static readonly Dictionary<string, PiecePane> Opened = new();

    private readonly ChangesChannel? _changes;
    private readonly SessionChannel? _session;
    private readonly Func<string> _userName;

    // Una vista de la app (Inicio, el Tablero…), en lugar de una página: se queda siempre en el panel, porque cambiarla
    // de ventana no la recarga.
    private readonly Control? _view;

    // El marco de una vista de la app en un corcho: los lados y el pie, como los que se pinta la página de una sesión.
    private readonly Border? _frame;

    // Las partes de una sesión la siguen en directo cada poco, como la sección.
    private readonly DispatcherTimer _live = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private NativeWebView? _web;
    private bool _ready;
    private bool _paused;

    // Lo último que se mandó de las sesiones abiertas, para mandarlo solo cuando cambia.
    private string? _activeSent;

    // La ronda que enseña la ficha de un fichero, la elegida en ella: la página la vuelve a recibir al recargarse.
    private int? _round;

    // Lo que otra pieza ha pedido para esta página antes de que estuviera lista (ir a una línea, los usos de un nombre):
    // se le manda al decir que lo está.
    private readonly List<object> _waiting = [];

    /// <summary>Crea el panel de una parte y lo apunta entre los abiertos.</summary>
    /// <param name="security">Los secretos de Seguridad, para los escudos de cada paso.</param>
    /// <param name="userName">El nombre de la persona, para sus mensajes.</param>
    public PiecePane(PieceSpec spec, Func<SecurityMonitor?> security, Func<string> userName)
    {
        Spec = spec;
        _round = spec.Round;
        _userName = userName;
        if (NativeViews.Is(spec.Kind)) _view = NativeViews.Make(spec.Kind);
        else if (PieceSpec.IsPart(spec.Kind)) _session = new SessionChannel(Post, security);
        if (_view is not null)
        {
            _frame = new Border { Child = _view, CornerRadius = new Avalonia.CornerRadius(0, 0, 12, 12), ClipToBounds = true };
            _frame.Bind(Border.BorderBrushProperty, this.GetResourceObservable("LensEdge"));
        }
        if (_view is CorkWidget widget) widget.StateChanged += CorkWindow.NotifyStateChanged;
        else _changes = new ChangesChannel(Post, () => (spec.SessionId, spec.Cwd));
        Background = new SolidColorBrush(PageTheme.PageColor());
        Opened[spec.Key] = this;
        _live.Tick += (_, _) =>
        {
            if (!_ready || _paused) return;
            ReviewIfRead();
            SendActive();
            _session?.Follow();
        };
        AttachedToVisualTree += (_, _) => Start();
        DetachedFromVisualTree += (_, _) => Stop();
    }

    /// <summary>Lo que enseña el panel.</summary>
    public PieceSpec Spec { get; }

    /// <summary>El widget del panel, si enseña una vista de la app que lo tiene; null si no.</summary>
    internal CorkWidget? Widget => _view as CorkWidget;

    /// <summary>Lo que guarda de sí mismo el widget del panel, para guardarlo con el corcho; ponerlo lo recupera.</summary>
    internal IReadOnlyDictionary<string, string>? WidgetState
    {
        get => Widget?.State;
        set => Widget?.Restore(value);
    }

    /// <summary>Se ha cerrado del todo: ya no está en ninguna parte.</summary>
    public bool Closed { get; private set; }

    /// <summary>Si no se ve (su ventana minimizada, o en una pestaña de detrás): mientras, no sigue su sesión y los avisos
    /// de su carpeta se guardan; al volver a verse se pone al día.</summary>
    public bool Paused
    {
        get => _paused;
        set
        {
            if (_paused == value) return;
            _paused = value;
            if (_view is not null)
            {
                if (value) NativeViews.Deactivate(_view);
                else if (TopLevel.GetTopLevel(_view) is not null) NativeViews.Activate(_view);
                return;
            }
            Changes!.Paused = value;
            if (_session is not null) _session.Paused = value;
            if (!value && _ready) _session?.Follow();
        }
    }

    /// <summary>El panel abierto con lo mismo, si lo hay.</summary>
    public static PiecePane? Find(PieceSpec spec) => Opened.TryGetValue(spec.Key, out var pane) && !pane.Closed ? pane : null;

    /// <summary>Los paneles abiertos ahora, estén donde estén.</summary>
    public static IEnumerable<PiecePane> All => Opened.Values.Where(p => !p.Closed);

    /// <summary>Lo de git y la revisión del panel: el suyo, o el del canal de su sesión; ninguno en una vista de la app.</summary>
    private ChangesChannel? Changes => _changes ?? _session?.Changes;

    /// <summary>Lo cierra del todo: suelta su sesión y su carpeta, y deja de estar entre los abiertos.</summary>
    /// <remarks>Quien lo tenga tiene que haberlo quitado ya de su sitio.</remarks>
    public void Close()
    {
        if (Closed) return;
        Closed = true;
        Stop();
        // Quitada del panel, la vista deja de escuchar los avisos de la app.
        if (_frame is not null) Children.Remove(_frame);
        _session?.Dispose();
        _changes?.Dispose();
        if (Opened.TryGetValue(Spec.Key, out var known) && known == this) Opened.Remove(Spec.Key);
    }

    /// <summary>Al ponerse en un sitio: crea su navegador y carga la página, y empieza a seguir su sesión.</summary>
    private void Start()
    {
        if (Closed) return;
        if (_view is not null)
        {
            // En un corcho va enmarcada como sus vecinos; en una ventana suelta, el borde ya es el de la ventana.
            _frame!.BorderThickness = TopLevel.GetTopLevel(this) is CorkWindow ? new Avalonia.Thickness(1, 0, 1, 1) : default;
            if (!Children.Contains(_frame)) Children.Add(_frame);
            if (!_paused) NativeViews.Activate(_view);
            return;
        }
        if (_web is not null) return;
        App.ThemeChanged += SendTheme;
        CodeThemes.Changed += SendCode;
        FinalReplyFormat.Changed += SendFinalFormat;
        CorkWindow.ListChanged += SendCorks;
        Pieces.Reviewed += SendActive;
        RepoColors.Changed += SendHost;
        StartWeb();
        if (_session is not null) _live.Start();
    }

    /// <summary>Al quitarse de su sitio: suelta su navegador, que se vuelve a crear donde se ponga.</summary>
    /// <remarks>La página nueva recibirá la sesión entera, no solo lo cambiado desde la que había.</remarks>
    private void Stop()
    {
        if (_view is not null)
        {
            NativeViews.Deactivate(_view);
            return;
        }
        App.ThemeChanged -= SendTheme;
        CodeThemes.Changed -= SendCode;
        FinalReplyFormat.Changed -= SendFinalFormat;
        CorkWindow.ListChanged -= SendCorks;
        Pieces.Reviewed -= SendActive;
        RepoColors.Changed -= SendHost;
        _live.Stop();
        _ready = false;
        _activeSent = null;
        _session?.Forget();
        if (_web is null) return;
        Children.Remove(_web);
        _web = null;
    }

    /// <summary>Crea el navegador con la misma carpeta de datos que Sesiones y carga su página en modo pieza.</summary>
    private void StartWeb()
    {
        try
        {
            var file = System.IO.Path.Combine(AppContext.BaseDirectory, "Web", "sesiones.html");
            var query = Spec.Kind == "step"
                ? $"piece=step&key={Uri.EscapeDataString(Spec.Path ?? "")}&interaction={Uri.EscapeDataString(Spec.Tab ?? "")}"
                : $"piece={Spec.Kind}";
            var page = new UriBuilder(new Uri(file)) { Query = query }.Uri;
            var folder = System.IO.Path.Combine(AppPaths.Home, "webview-desktop");
            var web = new NativeWebView();
            _web = web;
            web.EnvironmentRequested += (_, e) =>
            {
#if !DEBUG
                e.EnableDevTools = false;
#endif
                switch (e)
                {
                    // Los mismos ajustes que Sesiones: dos navegadores con la misma carpeta tienen que pedirla igual.
                    case WindowsWebView2EnvironmentRequestedEventArgs edge:
                        edge.UserDataFolder = folder;
                        edge.AdditionalBrowserArguments = "--disable-features=CalculateNativeWinOcclusion --disable-backgrounding-occluded-windows";
                        break;
                    case GtkWebViewEnvironmentRequestedEventArgs gtk:
                        gtk.BaseDataDirectory = folder;
                        gtk.BaseCacheDirectory = System.IO.Path.Combine(folder, "cache");
                        break;
                }
            };
            web.Background = new SolidColorBrush(PageTheme.PageColor());
            web.NavigationStarted += (_, e) =>
            {
                if (e.Request is { } uri && !string.Equals(uri.AbsolutePath, page.AbsolutePath, StringComparison.OrdinalIgnoreCase)) e.Cancel = true;
            };
            web.NewWindowRequested += (_, e) => e.Handled = true;
            // Lo que llegue de un navegador ya soltado no cuenta.
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
            // Se enseña cuando la página ha aplicado el tema: antes sacaría un primer fotograma con el de serie.
            web.Opacity = 0;
            Children.Add(web);
            web.Navigate(page);
        }
        catch (Exception ex)
        {
            JsonStore.LogError("piece-webview", ex);
        }
    }

    /// <summary>Atiende lo que pide la página: lo de la sesión, git y la revisión va a su canal; lo demás, aquí.</summary>
    /// <remarks>Lo que la página de la sección pide y aquí no tiene sentido (la lista de sesiones, la vista) no se
    /// atiende.</remarks>
    private void OnMessage(string? body)
    {
        if (string.IsNullOrEmpty(body)) return;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var m = doc.RootElement;
            var type = Str(m, "type");
            if (_session?.Handle(type, m) ?? _changes!.Handle(type, m)) return;
            switch (type)
            {
                case "ready":
                    _ready = true;
                    SendHost();
                    SendTheme();
                    SendCode();
                    SendFinalFormat();
                    SendCorks();
                    if (Spec.Kind == "file")
                        Post(new { type = "piece", piece = "file", id = Spec.SessionId, scope = Spec.Scope, path = Spec.Path, tab = Spec.Tab, round = _round });
                    else if (Spec.Kind == "finder")
                        Post(new { type = "piece", piece = "finder", id = Spec.SessionId });
                    else
                    {
                        Post(new { type = "user", name = _userName() });
                        SendActive();
                        // El chat va en el Parte, que no lo minimiza como el Flujo; una vista, en la suya; lo demás, en el
                        // Flujo.
                        Post(new { type = "tab", tab = Spec.Kind == "chat" ? "parte" : PieceSpec.IsView(Spec.Kind) ? Spec.Kind : "live" });
                        Post(new { type = "select", id = Spec.SessionId });
                    }
                    Post(new { type = "reveal" });
                    foreach (var waiting in _waiting) Post(waiting);
                    _waiting.Clear();
                    break;
                case "revealed":
                    if (_web is not null) _web.Opacity = 1;
                    break;
                case "pieceRound" when Spec.Kind == "file":
                    _round = Pieces.RoundOf(m);
                    break;
                case "finalFormat":
                    FinalReplyFormat.Set(m.TryGetProperty("on", out var formatOn) && formatOn.ValueKind == JsonValueKind.True);
                    break;
                case "open" when _session is not null && Str(m, "id") is { } open && string.Equals(open, Spec.SessionId, StringComparison.OrdinalIgnoreCase):
                    _session.Open(open, null);
                    break;
                case "reviewed" when _session is not null:
                    // Leída aquí, leída en todas: la app avisa a las demás páginas.
                    Pieces.MarkReviewed(Spec.SessionId);
                    break;
                case "pieceFile" or "pieceOpen" when Pieces.SpecFrom(type, m, Spec.SessionId, _session?.Trace?.Cwd ?? Spec.Cwd) is { } piece:
                    // Un fichero o un paso de esta sesión, desde el directo o los cambios: en su propio panel. Un fichero
                    // pulsado en los cambios de un corcho va con las demás fichas («beside»), en un recuadro nuevo a la
                    // derecha («box») o en una pestaña nueva del recuadro de los cambios («tab»).
                    var where = Str(m, "where");
                    var goTo = Pieces.GoToOf(m, piece);
                    // Lo que se abre desde la paleta se coloca como si se pulsara en el panel desde el que se pidió: junto
                    // a la ficha, si era una ficha; con las demás fichas, si era Cambios.
                    var from = Origin ?? this;
                    if (where == "auto") where = from.Spec.Kind == "file" ? "tab" : "beside";
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (where is "beside" or "box" && piece.Kind == "file") CorkWindow.OpenFile(from, piece, fresh: where == "box");
                        else if (where == "beside") CorkWindow.OpenBeside(from, piece);
                        else if (where == "tab") CorkWindow.OpenAsTab(from, piece);
                        else Pieces.Open(piece, where);
                        Pieces.GoTo(piece, goTo);
                    });
                    break;
                case "finderOpen":
                    // La paleta de buscar, en su propia ventana centrada sobre todos los paneles.
                    var mode = Str(m, "mode");
                    var word = Str(m, "word");
                    var cwd = _session?.Trace?.Cwd ?? Spec.Cwd;
                    Dispatcher.UIThread.Post(() => FinderWindow.Open(this, mode, word, cwd));
                    break;
                case "finderClose":
                    var outside = m.TryGetProperty("outside", out var o) && o.ValueKind == JsonValueKind.True;
                    Dispatcher.UIThread.Post(() => FinderWindow.HideFor(this, outside));
                    break;
                case "pieceEmpty":
                    // Lo pide la ficha de un fichero al marcarlo como revisado: deja su sitio vacío para el siguiente.
                    Dispatcher.UIThread.Post(() => CorkWindow.EmptyWhereItIs(this));
                    break;
                case "pieceProfile" when Str(m, "profile") is { } profile:
                    Dispatcher.UIThread.Post(() => CorkWindow.OpenProfile(profile, Spec.SessionId, null));
                    break;
                case "codeTheme" when Str(m, "lang") is { } codeLang && Str(m, "theme") is { } codeTheme:
                    CodeThemes.Set(codeLang, codeTheme);
                    break;
                case "link" when Str(m, "url") is { } url:
                    SessionsView.OpenLink(url);
                    break;
                case "copy" when Str(m, "text") is { } copied:
                    Dispatcher.UIThread.Post(async () =>
                    {
                        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(copied);
                    });
                    break;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            JsonStore.LogError("piece-message", ex);
        }
    }

    /// <summary>Pone la ficha de un fichero en la ronda con que se vuelve a pedir, o en toda la rama: se abre como se ve
    /// la lista desde la que se pulsa.</summary>
    public void ShowRound(PieceSpec spec)
    {
        if (Spec.Kind != "file" || _round == spec.Round) return;
        _round = spec.Round;
        if (_ready) Post(new { type = "round", round = _round });
    }

    /// <summary>Manda a la página los colores del tema activo, al abrirse y cada vez que cambia.</summary>
    private void SendTheme()
    {
        if (!_ready || _web is null) return;
        _web.Background = new SolidColorBrush(PageTheme.PageColor());
        Background = new SolidColorBrush(PageTheme.PageColor());
        Post(PageTheme.Message());
    }

    /// <summary>Manda a la página los lenguajes y los temas del código, con el elegido para cada lenguaje.</summary>
    private void SendCode()
    {
        if (_ready) Post(new { type = "code", catalog = CodeThemes.Catalog.Raw, chosen = CodeThemes.Chosen });
    }

    /// <summary>Manda a la página si la respuesta final va con el formato de Arkko, el mismo ajuste que en Sesiones.</summary>
    private void SendFinalFormat()
    {
        if (_ready) Post(new { type = "finalFormat", on = FinalReplyFormat.On });
    }

    /// <summary>Manda a la página los corchos abiertos y los perfiles de corcho, para su menú de clavar; las fichas de
    /// fichero abiertas de su sesión, que su lista de cambios marca, y si sus notas de la revisión están abiertas aparte.</summary>
    private void SendCorks()
    {
        if (_ready)
            Post(new
            {
                type = "corks", list = CorkWindow.List(), profiles = CorkWindow.ProfileNames(), templates = CorkWindow.TemplateNames(), openFiles = Pieces.OpenFiles(Spec.SessionId),
                notesAside = Spec.Kind != "review" && Pieces.NotesAside(Spec.SessionId),
            });
    }

    /// <summary>Dice a la página si va en un corcho y, entonces, el color de su marco, el de su sesión, para seguir el de
    /// la cabecera del recuadro por los lados y por abajo.</summary>
    /// <remarks>Cambiar de ventana recarga la página: al arrancar, la ventana en que va es la de ahora.</remarks>
    private void SendHost()
    {
        if (!_ready) return;
        var cork = TopLevel.GetTopLevel(this) is CorkWindow;
        Post(new { type = "host", cork, edge = cork ? CorkWindow.EdgeHex(this) : null });
    }

    /// <summary>Manda a la página de una parte de sesión las sesiones abiertas, si han cambiado: de ahí saca si la
    /// respuesta final sigue sin revisar, y la pinta en rojo como la sección.</summary>
    private void SendActive()
    {
        if (!_ready || _session is null) return;
        var message = new { type = "active", items = Pieces.Active() };
        var json = JsonSerializer.Serialize(message, Json);
        if (json == _activeSent) return;
        _activeSent = json;
        Post(message);
    }

    /// <summary>Da la sesión por revisada si su chat está a la vista en la ventana activa, como hace la sección mientras
    /// se lee.</summary>
    private void ReviewIfRead()
    {
        if (Spec.Kind == "chat" && TopLevel.GetTopLevel(this) is Window { IsActive: true }) Pieces.MarkReviewed(Spec.SessionId);
    }

    /// <summary>Envía un mensaje a la página, llamando a su función de entrada con el JSON como objeto.</summary>
    /// <summary>Manda a la página algo que ha pedido otra pieza: ya, si está lista; si no, en cuanto lo esté.</summary>
    internal void Deliver(object payload)
    {
        if (_ready && _web is not null) Post(payload);
        else _waiting.Add(payload);
    }

    /// <summary>El panel desde el que se abrió, si este es la paleta de buscar: sus ficheros se abren como si se pulsaran
    /// ahí.</summary>
    internal PiecePane? Origin { get; set; }

    /// <summary>Da el teclado a la página del panel.</summary>
    internal void FocusPage() => _web?.Focus();

    private void Post(object payload)
    {
        if (_web is not { } web) return;
        _ = web.InvokeScript($"window.agentPulseReceive && window.agentPulseReceive({JsonSerializer.Serialize(payload, Json)})");
    }

    /// <summary>Valor de texto de una propiedad del mensaje, o null.</summary>
    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
