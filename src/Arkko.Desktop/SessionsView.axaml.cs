using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Sección «Sesiones»: aloja la página web que enseña cómo trabaja una sesión y le pasa los datos.</summary>
/// <remarks>
/// La página es la misma que la de la ventana de WPF (carpeta Web junto al ejecutable), abierta como fichero local
/// y sin red: cualquier navegación a otro sitio se cancela. Habla con la app por mensajes JSON: pide la lista, una
/// sesión o el detalle de un paso, y la app le contesta llamando a una función de la página. Leer transcripts tarda,
/// así que todo se lee fuera del hilo de la ventana, y una respuesta que llega tarde, cuando ya se ha pedido otra
/// cosa, se descarta.
/// </remarks>
public partial class SessionsView : UserControl
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private Func<IReadOnlyList<SessionState>> _openSessions = () => [];
    private Func<SessionState, (int Review, int Wait, string? Secret)> _attention = _ => (0, 0, null);
    private SecurityMonitor? _security;
    private Action<string> _markReviewed = _ => { };

    // Si la persona lee la sesión que ve (con el chat a la vista): solo entonces se da por revisada sola.
    private bool _reading = true;
    // La sesión que la persona tiene abierta en la página, aunque no tenga transcript que leer.
    private string? _viewedId;
    private Func<string?> _userName = () => null;
    private AppSettings? _settings;
    // Vista elegida en el desplegable de la página, que dura hasta cerrar la app; null, la de la configuración.
    private string? _view;
    private Action _save = () => { };
    private bool _started;
    private bool _ready;
    // Si la sección está a la vista: escondida, no se le manda la sesión, porque la pintaría para nada.
    private bool _active;
    private string? _pendingSelect;
    private int _listGeneration;

    // Transcript de cada sesión listada, para abrirla por su id.
    private readonly ConcurrentDictionary<string, string> _paths = new(StringComparer.OrdinalIgnoreCase);

    // En directo: la sesión abierta elegida se relee cada poco, solo lo nuevo, mientras se está en la sección.
    private readonly DispatcherTimer _live = new() { Interval = TimeSpan.FromSeconds(1.5) };

    // El navegador de la página; null mientras no está cargada o tras descargarla.
    private NativeWebView? _web;

    // Sin la sección a la vista durante este rato, se descarga el navegador: con una sesión grande pesa cientos de MB.
    private static readonly TimeSpan IdleUnload = TimeSpan.FromMinutes(10);
    private readonly DispatcherTimer _unload = new() { Interval = IdleUnload };

    // Ya se descargó una vez: no se vuelve a cargar en segundo plano, sino al entrar en la sección.
    private bool _unloaded;

    // Lo último que se mandó del panel de activas, para mandarlo solo cuando cambia.
    private string? _activeSent;

    // La sesión que se ve: mandarla, seguirla en directo, y lo de git y la revisión de su carpeta.
    private readonly SessionChannel _session;

    /// <summary>Crea la sección; la página no se carga hasta que se entra en ella o el panel queda libre.</summary>
    public SessionsView()
    {
        InitializeComponent();
        _session = new SessionChannel(Post, () => _security);
        AttachedToVisualTree += (_, _) =>
        {
            App.ThemeChanged += SendTheme;
            CorkWindow.ListChanged += SendCorks;
            Pieces.Reviewed += SendActive;
            ArtifactMonitor.Instance.Changed += OnArtifactsChanged;
            if (_security is not { } security) return;
            security.Changed += OnSecurityChanged;
            security.PendingChanged += OnSecurityChanged;
        };
        DetachedFromVisualTree += (_, _) =>
        {
            App.ThemeChanged -= SendTheme;
            CorkWindow.ListChanged -= SendCorks;
            Pieces.Reviewed -= SendActive;
            ArtifactMonitor.Instance.Changed -= OnArtifactsChanged;
            if (_security is not { } security) return;
            security.Changed -= OnSecurityChanged;
            security.PendingChanged -= OnSecurityChanged;
        };
        _live.Tick += (_, _) =>
        {
            ReviewViewed();
            SendActive();
            if (_ready) _session.Follow();
        };
        _unload.Tick += (_, _) =>
        {
            _unload.Stop();
            if (!_active) Unload();
        };
    }

    /// <summary>Da por revisada la sesión que se está viendo si termina mientras se lee, con la ventana delante.</summary>
    private void ReviewViewed()
    {
        if (_reading && _viewedId is { } id && TopLevel.GetTopLevel(this) is Window { IsActive: true }) _markReviewed(id);
    }

    /// <summary>Al salir de la sección o esconder el panel: deja de seguir la sesión en directo.</summary>
    public void Deactivate()
    {
        _active = false;
        _session.Changes.Paused = true;
        _session.Paused = true;
        _live.Stop();
        if (_started) _unload.Start();
    }

    /// <summary>Descarga el navegador y su página, para que no ocupe memoria mientras no se mira la sección.</summary>
    /// <remarks>
    /// Al volver a entrar se carga otra vez y se reabre la sesión que se estaba viendo. Lo leído de ella se queda en la
    /// app, así que reabrirla es rápido; lo que se pierde es solo lo de la vista: por dónde se iba, lo desplegado, la búsqueda.
    /// </remarks>
    private void Unload()
    {
        if (_web is null) return;
        Host.Children.Remove(_web);
        _web = null;
        (_started, _ready, _unloaded) = (false, false, true);
        _pendingSelect ??= _viewedId;
        _session.Forget();
        _activeSent = null;
        Loading.IsVisible = true;
        Loading.Opacity = 0;
        if (!Spinner.Classes.Contains("spinning")) Spinner.Classes.Add("spinning");
    }

    /// <summary>Para el recorrido de imágenes: descarga ya el navegador, sin esperar a que pase el rato.</summary>
    public void UnloadNow()
    {
        if (!_active) Unload();
    }

    /// <summary>Pide a la ventana que enseñe la sección Seguridad, desde un escudo de secreto.</summary>
    public event Action? SecurityRequested;

    /// <summary>Pide a la ventana que oscurezca lo que no es la página, mientras se ve una imagen a pantalla completa; false, que deje de hacerlo.</summary>
    public event Action<bool>? DimRequested;

    /// <summary>Cierra el visor de imágenes de la página, cuando se pulsa en lo que la ventana ha oscurecido.</summary>
    public void CloseLightbox()
    {
        if (_ready) Post(new { type = "closeLightbox" });
    }

    /// <summary>Pide a la ventana que enseñe una tarea en el Tablero, desde la cabecera de la sesión.</summary>
    public event Action<string>? TaskRequested;

    /// <summary>Recibe de dónde salen las sesiones abiertas, lo que pide cada una, cómo darla por revisada, los secretos de Seguridad y los ajustes de la app, con su forma de guardarlos.</summary>
    /// <remarks>Los ajustes son los de la ventana flotante: guardarlos aparte se pisaría con lo que guarda ella.</remarks>
    public void Init(Func<IReadOnlyList<SessionState>> openSessions, Func<SessionState, (int Review, int Wait, string? Secret)> attention, Action<string> markReviewed, SecurityMonitor security, AppSettings settings, Action save)
    {
        _openSessions = openSessions;
        SessionFeed.Init(openSessions);
        _attention = attention;
        _markReviewed = markReviewed;
        _security = security;
        _settings = settings;
        _save = save;
        _userName = () => settings.UserName;
    }

    /// <summary>Manda a la página los colores de los grupos y los del menú, tras cambiarlos aquí o en la configuración.</summary>
    public void SendGroups()
    {
        if (!_ready || _settings is not { } s) return;
        Post(new { type = "groups", colors = RepoColors.ForPage(), palette = AppSettings.DefaultGroupPalette.Concat(s.GroupPalette).Distinct(StringComparer.OrdinalIgnoreCase) });
    }

    /// <summary>Manda a la página los lenguajes y los temas del código, con el elegido para cada lenguaje.</summary>
    public void SendCode()
    {
        if (!_ready) return;
        Post(new { type = "code", catalog = CodeThemes.Catalog.Raw, chosen = CodeThemes.Chosen });
    }

    /// <summary>Manda a la página si la respuesta final del directo va con el formato de Arkko.</summary>
    public void SendFinalFormat()
    {
        if (_ready) Post(new { type = "finalFormat", on = FinalReplyFormat.On });
    }

    /// <summary>Abre en el navegador un enlace del chat.</summary>
    /// <remarks>Solo direcciones web: el texto viene de un transcript, y un esquema como file: lanzaría otro programa.</remarks>
    internal static void OpenLink(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return;
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            JsonStore.LogError("sessions-link", ex);
        }
    }

    /// <summary>Pone o quita el color de un grupo, lo guarda y se lo devuelve a la página.</summary>
    private void SetGroupColor(string group, string? color)
    {
        if (_settings is not { } s) return;
        // Lo que manda la página no se guarda sin comprobarlo.
        if (color is not null && !HexColor.IsValid(color)) return;
        if (color is null) s.GroupColors.Remove(group);
        else s.GroupColors[group] = color.ToLowerInvariant();
        _save();
        // Avisa al Tablero, el Diario y Worktrees; a la página, con el color que le queda (el de su subtablero, si lo quita).
        RepoColors.NotifyChanged();
    }

    /// <summary>Al entrar en la sección: carga la página la primera vez y, después, le pide que se actualice.</summary>
    /// <remarks>
    /// Si se entra para ver una sesión concreta, se refresca la lista y se abre solo esa: refrescar a secas volvería
    /// a leer antes la que se estaba viendo, que puede ser enorme.
    /// </remarks>
    public void Activate()
    {
        _active = true;
        _session.Changes.Paused = false;
        _session.Paused = false;
        _unload.Stop();
        _live.Start();
        if (!_started)
        {
            Load();
            return;
        }
        if (!_ready) return;
        if (_queuedSelect is { } id)
        {
            _queuedSelect = null;
            Post(new { type = "refresh", reopen = false });
            Post(new { type = "select", id, secret = TakeSecret() });
        }
        else
        {
            Post(new { type = "refresh" });
        }
    }

    // Sesión que se abre al entrar en la sección, pedida desde fuera de ella.
    private string? _queuedSelect;

    /// <summary>Carga la página sin entrar en la sección, para que la primera visita no espere al navegador.</summary>
    /// <remarks>Solo la primera vez: tras descargarla por no mirarla, se carga al volver a entrar, no antes.</remarks>
    public void Preload()
    {
        if (_started || _unloaded) return;
        Load();
        if (!_active) _unload.Start();
    }

    /// <summary>Crea el navegador y carga la página.</summary>
    private void Load()
    {
        _started = true;
        _unloaded = false;
        Start();
    }

    /// <summary>Abre una sesión concreta, como al pedir «Ver sesión» desde una tarjeta.</summary>
    /// <remarks>Solo la apunta: la abre Activate, al entrar en la sección, para no leer antes otra.</remarks>
    public void Select(string sessionId)
    {
        _focusSecret = null;
        if (_ready) _queuedSelect = sessionId;
        else _pendingSelect = sessionId;
    }

    /// <summary>Abre una sesión en el paso donde apareció un secreto, pedido desde Seguridad.</summary>
    /// <param name="secret">La referencia corta del secreto (<see cref="SessionChannel.SecretRef"/>), nunca su valor.</param>
    public void SelectSecret(string sessionId, string secret)
    {
        Select(sessionId);
        _focusSecret = secret;
    }

    // Secreto cuyo paso se enseña al abrir la sesión pedida; se manda una sola vez.
    private string? _focusSecret;

    /// <summary>Devuelve el secreto pendiente de enseñar y lo olvida.</summary>
    private string? TakeSecret()
    {
        var secret = _focusSecret;
        _focusSecret = null;
        return secret;
    }

    /// <summary>Manda el nombre de la persona, tras cambiarlo en la configuración.</summary>
    public void SendUser()
    {
        if (_ready) Post(new { type = "user", name = UserName() });
    }

    /// <summary>Manda la vista con la que se abre Sesiones: la elegida en la página mientras la app sigue abierta, o la de la configuración.</summary>
    /// <param name="fromSettings">Si se acaba de cambiar en la configuración: entonces manda esa, aunque en la página se hubiera elegido otra.</param>
    public void SendTab(bool fromSettings = false)
    {
        if (fromSettings) _view = null;
        if (_ready && _settings is { } s) Post(new { type = "tab", tab = _view ?? AppSettings.SessionsViewOf(s.SessionsTab) });
    }

    /// <summary>Crea el navegador, con su carpeta de datos propia, y carga la página local.</summary>
    private void Start()
    {
        try
        {
            var page = Path.Combine(AppContext.BaseDirectory, "Web", "sesiones.html");
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
                    // Edge deja de pintar una ventana que cree tapada o en segundo plano, y con varias pantallas se equivoca.
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
            // Lo que llegue de un navegador ya descargado no cuenta.
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
            // Se pone en la ventana después de atender sus avisos: con la sección a la vista arranca en el acto, y
            // pediría su carpeta de datos antes de que nadie la escuchara, con lo que usaría la de junto al ejecutable.
            Host.Children.Insert(0, web);
            Loading.Opacity = 1;
            web.IsVisible = true;
            web.Opacity = 0;
            web.Navigate(pageUri);
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
    }

    /// <summary>Enseña que no se ha podido abrir la página, con el motivo en errors.log.</summary>
    private void ShowFailure(Exception ex)
    {
        JsonStore.LogError("sessions-webview", ex);
        if (_web is not null) _web.IsVisible = false;
        Loading.IsVisible = false;
        Spinner.Classes.Remove("spinning");
        Failure.Text = OperatingSystem.IsLinux()
            ? "No se ha podido abrir la vista de sesiones: hace falta WebKitGTK (paquete libwebkit2gtk-4.1). El detalle está en errors.log."
            : "No se ha podido abrir la vista de sesiones: hace falta el componente WebView2 de Microsoft, que viene con Windows 11. El detalle está en errors.log.";
        Failure.IsVisible = true;
    }

    /// <summary>Atiende lo que pide la página.</summary>
    private void OnMessage(string? body)
    {
        if (string.IsNullOrEmpty(body)) return;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var m = doc.RootElement;
            var type = Str(m, "type");
            if (_session.Handle(type, m)) return;
            switch (type)
            {
                case "ready":
                    _ready = true;
                    SendTheme();
                    SendUser();
                    SendTab();
                    SendGroups();
                    SendCode();
                    SendFinalFormat();
                    SendCorks();
                    _activeSent = null;
                    SendActive();
                    if (_pendingSelect is { } id)
                    {
                        _pendingSelect = null;
                        Post(new { type = "select", id, secret = TakeSecret() });
                    }
                    // La página se enseña cuando ha aplicado todo lo anterior: los mensajes le llegan en orden, y enseñarla
                    // antes sacaba un primer fotograma con el tema oscuro de serie y sin las activas.
                    Post(new { type = "reveal" });
                    break;
                case "view" when Str(m, "view") is { } view:
                    _view = view;
                    break;
                case "revealed":
                    Loading.IsVisible = false;
                    Spinner.Classes.Remove("spinning");
                    if (_web is not null) _web.Opacity = 1;
                    break;
                case "codeTheme" when Str(m, "lang") is { } codeLang && Str(m, "theme") is { } codeTheme:
                    CodeThemes.Set(codeLang, codeTheme);
                    break;
                case "finalFormat":
                    FinalReplyFormat.Set(m.TryGetProperty("on", out var formatOn) && formatOn.ValueKind == JsonValueKind.True);
                    break;
                case "task" when Str(m, "key") is { } taskKey:
                    TaskRequested?.Invoke(taskKey);
                    break;
                case "taskAdd" when _session.Feed?.Summary is { } adding:
                    TaskPicker.Show(this, atPointer: true,
                        TaskDirectory.Instance.TasksOfSession(adding.SessionId, adding.Worktree ?? adding.Project).Tasks,
                        key => ChangeTasks(adding, () => TaskDirectory.Instance.AddTask(adding.SessionId, key)));
                    break;
                case "taskLink" when _session.Feed?.Summary is { } linking && Str(m, "key") is { } linkKey:
                    ChangeTasks(linking, () => TaskDirectory.Instance.AddTask(linking.SessionId, linkKey));
                    break;
                case "taskUnlink" when _session.Feed?.Summary is { } unlinking && Str(m, "key") is { } unlinkKey:
                    Dispatcher.UIThread.Post(async () =>
                    {
                        if (!await TaskDirectory.Instance.ConfirmRemoveAsync(TopLevel.GetTopLevel(this) as Window, unlinking.SessionId,
                                unlinking.Worktree ?? unlinking.Project, unlinkKey)) return;
                        ChangeTasks(unlinking, () => TaskDirectory.Instance.RemoveTask(unlinking.SessionId, unlinkKey));
                    });
                    break;
                case "taskPrincipal" when _session.Feed?.Summary is { } choosing && Str(m, "key") is { } principalKey:
                    ChangeTasks(choosing, () => TaskDirectory.Instance.SetPrincipal(choosing.SessionId, principalKey));
                    break;
                case "list":
                    LoadList(Str(m, "period") ?? "week", Str(m, "query") ?? "", Str(m, "day"));
                    break;
                case "open" when Str(m, "id") is { } open:
                    // Abrir una sesión terminada es revisarla, como pulsar su tarjeta en el inicio.
                    _viewedId = open;
                    _markReviewed(open);
                    SendActive();
                    SendCorks();
                    _session.Open(open, _paths.TryGetValue(open, out var openPath) ? openPath : null);
                    break;
                case "pieceFile" or "pieceOpen" when _session.PostedId is { } pieceId
                                                    && Pieces.SpecFrom(type, m, pieceId, _session.Trace?.Cwd) is { } piece:
                    // El panel es de esta sesión y de su carpeta: si después se elige otra aquí, sigue con la suya.
                    var where = Str(m, "where");
                    var goTo = Pieces.GoToOf(m, piece);
                    Dispatcher.UIThread.Post(() =>
                    {
                        Pieces.Open(piece, where);
                        Pieces.GoTo(piece, goTo);
                    });
                    break;
                case "pieceProfile" when _session.PostedId is { } profileSession && Str(m, "profile") is { } profile:
                    Dispatcher.UIThread.Post(() => CorkWindow.OpenProfile(profile, profileSession, null));
                    break;
                case "groupColor" when Str(m, "group") is { } group:
                    SetGroupColor(group, Str(m, "color"));
                    break;
                case "link" when Str(m, "url") is { } url:
                    OpenLink(url);
                    break;
                case "artifact" when Str(m, "url") is { } artifact:
                    SessionActions.RequestArtifact(artifact);
                    break;
                case "security":
                    SecurityRequested?.Invoke();
                    break;
                case "reading":
                    // Con el chat recogido (en directo), mirar el lienzo no es leer lo que contestó: sigue sin revisar.
                    _reading = m.TryGetProperty("on", out var reading) && reading.ValueKind == JsonValueKind.True;
                    ReviewViewed();
                    break;
                case "reviewed" when _viewedId is { } reviewed:
                    _markReviewed(reviewed);
                    SendActive();
                    break;
                case "dim":
                    DimRequested?.Invoke(m.TryGetProperty("on", out var on) && on.ValueKind == JsonValueKind.True);
                    break;
                case "delete" when Str(m, "id") is { } delete:
                    // Después de atender el mensaje: el diálogo es modal y no debe abrirse dentro del evento del navegador.
                    Dispatcher.UIThread.Post(() => DeleteSession(delete));
                    break;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            JsonStore.LogError("sessions-message", ex);
        }
    }

    /// <summary>Lleva a la papelera la conversación de una sesión cerrada, tras confirmarlo, y olvida el texto de sus prompts.</summary>
    /// <remarks>
    /// Se mueve el transcript y la carpeta de sus subagentes, no se borran: un error tiene arreglo desde la papelera.
    /// Las cifras del histórico se conservan; solo se quita el texto de los prompts. Una sesión abierta no se puede
    /// borrar: Claude Code seguiría escribiendo su transcript.
    /// </remarks>
    private async void DeleteSession(string id)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (_openSessions().Any(s => string.Equals(s.SessionId, id, StringComparison.OrdinalIgnoreCase)))
        {
            await AppDialog.ShowAsync(owner, "La sesión sigue abierta", "Ciérrala en su terminal para poder eliminarla.", DialogTone.Warning);
            return;
        }
        var path = _paths.TryGetValue(id, out var p) ? p : FindTranscript(id);
        if (path is null || !File.Exists(path))
        {
            await AppDialog.ShowAsync(owner, "No se encuentra la sesión", "Su conversación ya no está en disco.", DialogTone.Warning);
            Post(new { type = "deleted", id });
            return;
        }
        var agents = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path));
        var items = new List<string>
        {
            "La conversación va a la papelera del sistema: desde allí se puede recuperar.",
            "No se podrá retomar con claude --resume mientras esté en la papelera.",
            "Las estadísticas se conservan; solo se olvida el texto de sus prompts.",
        };
        if (_security?.PendingFor(id).Count > 0)
            items.Add("Tiene secretos sin revisar: siguen en Seguridad, pero ya no se podrá ir al fichero donde aparecen.");
        if (!await AppDialog.ConfirmAsync(owner, "Eliminar la sesión", "¿Seguro que quieres eliminar esta sesión?", "Eliminar",
                DialogTone.Warning, items, note: path))
            return;
        try
        {
            Trash.Move(path);
            if (Directory.Exists(agents)) Trash.Move(agents);
            new DataRetention().ForgetSession(id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            JsonStore.LogError("sessions-delete", ex);
            await AppDialog.ShowAsync(owner, "No se ha podido eliminar la sesión", "El detalle está en errors.log.", note: ex.Message);
            return;
        }
        _paths.TryRemove(id, out _);
        Post(new { type = "deleted", id });
    }

    /// <summary>Lee la lista de sesiones del periodo, o de un día, y la filtra por la búsqueda.</summary>
    /// <param name="period">Un periodo como el de Estadísticas.</param>
    /// <param name="day">Día local «aaaa-mm-dd» al que se limita la lista: las sesiones con actividad ese día, aunque siguieran después; null o vacío, todo el periodo.</param>
    /// <remarks>Con algo escrito en el buscador se busca en todas las sesiones, sea cual sea el periodo elegido; el día se respeta.</remarks>
    private async void LoadList(string period, string query, string? day)
    {
        var generation = ++_listGeneration;
        var open = _openSessions().Select(s => (s.SessionId, Status: MainViewModel.StatusWord(s), Key: StatusKey(s.Status))).ToList();
        if (!string.IsNullOrWhiteSpace(query)) period = "all";
        DateTime? only = DateTime.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        var since = only?.ToUniversalTime() ?? Since(period);
        try
        {
            var items = await Task.Run(() =>
            {
                var listed = SessionCatalog.List(since);
                if (only is { } date) listed = listed.Where(s => SessionCatalog.ActiveDays(s.TranscriptPath).Contains(date)).ToList();
                foreach (var s in listed) _paths[s.SessionId] = s.TranscriptPath;
                return SessionCatalog.Search(listed, query);
            });
            if (generation != _listGeneration) return;

            // Sin búsqueda, se aprovecha para leer los prompts de la lista: así la primera búsqueda no espera a leer todos los transcripts.
            if (string.IsNullOrWhiteSpace(query))
                _ = Task.Run(() => items.ForEach(s => SessionCatalog.Prompts(s.TranscriptPath)));

            var byId = open.ToDictionary(o => o.SessionId);
            Post(new
            {
                type = "sessions",
                items = items.Select(s =>
                {
                    var live = byId.TryGetValue(s.SessionId, out var o);
                    return new
                    {
                        id = s.SessionId, title = s.Title, project = s.Project, worktree = s.Worktree, firstPrompt = s.FirstPrompt,
                        last = s.LastUtc, open = live, status = live ? o.Status : null, statusKey = live ? o.Key : null,
                        models = s.Models?.Select(u => new { model = u.Model, effort = u.Effort }),
                    };
                }),
            });
        }
        catch (Exception ex)
        {
            JsonStore.LogError("sessions-list", ex);
            if (generation == _listGeneration) Post(new { type = "error", message = "No se ha podido leer la lista de sesiones." });
        }
    }

    /// <summary>Cambia las tareas de una sesión y manda a la página cómo quedan.</summary>
    private void ChangeTasks(SessionSummary summary, Action change)
    {
        change();
        Post(new { type = "tasks", id = summary.SessionId, task = SessionChannel.TaskOf(summary) });
    }

    /// <summary>Al cambiar lo hallado o lo resuelto en Seguridad, vuelve a mandar los escudos: los de las activas y los de la sesión que se ve.</summary>
    private void OnSecurityChanged()
    {
        _activeSent = null;
        SendActive();
        // Con la sección escondida no se manda: al volver, la página pide la sesión y le llega con los escudos al día.
        if (_ready && _active) _session.Repost();
    }

    /// <summary>Al cambiar lo publicado en claude.ai, vuelve a mandar la sesión que se ve, con su pastilla de artifacts al día.</summary>
    private void OnArtifactsChanged()
    {
        if (_ready && _active) _session.Repost();
    }

    /// <summary>Manda a la página las sesiones abiertas, agrupadas por repositorio o carpeta, si han cambiado desde el último envío.</summary>
    private void SendActive()
    {
        if (!_ready) return;
        var message = new { type = "active", items = ActiveItems(_openSessions(), _attention) };
        var json = JsonSerializer.Serialize(message, Json);
        if (json == _activeSent) return;
        _activeSent = json;
        Post(message);
    }

    /// <summary>Las sesiones abiertas tal como las espera la página en su mensaje «active»: agrupadas por repositorio o
    /// carpeta, con su estado y lo que piden.</summary>
    /// <remarks>Las piezas también lo reciben: de ahí sacan si la respuesta final sigue sin revisar.</remarks>
    internal static IReadOnlyList<object> ActiveItems(IEnumerable<SessionState> sessions, Func<SessionState, (int Review, int Wait, string? Secret)> attention) =>
        sessions
            .Select(s =>
            {
                var a = attention(s);
                return new
                {
                    id = s.SessionId,
                    group = s.Repo ?? s.Project ?? "Sin carpeta",
                    worktree = s.Repo is null ? null : s.Project,
                    name = s.SessionName,
                    // Abierta y aún sin prompt: Claude Code no crea el transcript hasta el primero.
                    fresh = s.TranscriptPath is { } transcript && !File.Exists(transcript),
                    started = s.StartedUtc,
                    status = MainViewModel.StatusWord(s),
                    statusKey = StatusKey(s.Status),
                    since = s.StatusSinceUtc,
                    // Lo que pide, con los mismos niveles que el inicio: 1 recién, 2 lleva rato; y el secreto pendiente, si lo hay.
                    review = a.Review,
                    wait = a.Wait,
                    secret = a.Secret,
                    // Por qué falló su último hook, y cuándo, si falló.
                    hooks = s.HookError,
                    hooksAt = s.HookErrorUtc,
                    // Para su tooltip.
                    model = s.Model,
                    effort = EffortLevels.Of(s.Effort).Text,
                    effortLevel = EffortLevels.Of(s.Effort).Level,
                    contextPct = s.ContextPct,
                    contextSize = s.ContextSize,
                    cost = s.CostUsd,
                    // Su trabajo en segundo plano: los agentes que espera y lo que dejó en marcha.
                    background = BackgroundOf(s),
                };
            })
            .OrderBy(s => s.group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.started)
            .ToList<object>();

    /// <summary>Busca el transcript de una sesión por su id entre las carpetas de proyecto; null si ya no está.</summary>
    internal static string? FindTranscript(string id)
    {
        var root = SecretScanner.ProjectsDir;
        if (!Directory.Exists(root)) return null;
        return Directory.EnumerateDirectories(root)
            .Select(dir => Path.Combine(dir, id + ".jsonl"))
            .FirstOrDefault(File.Exists);
    }

    /// <summary>Manda los colores del tema activo como variables CSS.</summary>
    private void SendTheme()
    {
        if (!_ready || _web is null) return;
        _web.Background = new SolidColorBrush(PageTheme.PageColor());
        Post(PageTheme.Message());
    }

    /// <summary>Para el recorrido de imágenes: la sesión que sigue en directo, cuántos pasos tiene la app y cuántos la página.</summary>
    public async Task<string> LiveProbeAsync()
    {
        if (_web is null) return "sin navegador: descargado";
        var page = await _web.InvokeScript(
            "JSON.stringify({ id: state.trace && state.trace.id, steps: state.trace && state.trace.steps.length, live: state.trace && state.trace.live, " +
            "loading: state.loadingId, stale: state.chatStale, hidden: document.hidden, visibility: document.visibilityState, " +
            "items: state.chatItems && state.chatItems.length, nodes: document.getElementById('chat').children.length, " +
            "pinned: state.chatPinned, following: following(), interaction: state.interaction, " +
            "elements: document.getElementsByTagName('*').length, chatEls: document.querySelectorAll('#chat *').length, " +
            "diagramEls: document.querySelectorAll('#diagram *').length, tab: state.tab })");
        var feed = _session.Feed;
        return $"app: sesión={feed?.Id[..8]} pasos={feed?.Trace?.Steps.Count} live={feed?.Live} version={feed?.Version} active={_active} timer={_live.IsEnabled} " +
               $"posts={_posts} kb={_postedChars / 1024} ultimo={_lastPost / 1024} mayor={_biggestPost / 1024} | página={page}";
    }

    /// <summary>Lo que la página tiene ahora, para el recorrido de imágenes: si está lista y cuántas sesiones y pasos enseña.</summary>
    /// <remarks>La página va en una ventana del sistema que la imagen de Avalonia no dibuja: se le pregunta a ella.</remarks>
    public async Task<string> ProbeAsync()
    {
        if (_web is null) return "sin navegador: descargado";
        var page = await _web.InvokeScript(
            "JSON.stringify({ title: document.title, sessions: document.querySelectorAll('#sessions .item, #sessions li, .session-item').length, " +
            "text: document.body.innerText.length, active: document.querySelectorAll('#active *').length })");
        return $"ready={_ready} active={_active} visible={_web.Opacity > 0} página={page}";
    }

    /// <summary>Manda a la página los corchos abiertos y los perfiles de corcho, para su menú de clavar, y las fichas de
    /// fichero abiertas de la sesión que se ve, que su lista de cambios marca.</summary>
    private void SendCorks()
    {
        if (_ready)
            Post(new { type = "corks", list = CorkWindow.List(), profiles = CorkWindow.ProfileNames(), templates = CorkWindow.TemplateNames(), openFiles = Pieces.OpenFiles(_viewedId ?? _session.PostedId) });
    }

    /// <summary>Envía un mensaje a la página, llamando a su función de entrada con el JSON como objeto.</summary>
    private void Post(object payload)
    {
        if (_web is not { } web) return;
        var json = JsonSerializer.Serialize(payload, Json);
        _posts++;
        _postedChars += json.Length;
        _lastPost = json.Length;
        _biggestPost = Math.Max(_biggestPost, json.Length);
        _ = web.InvokeScript($"window.agentPulseReceive && window.agentPulseReceive({json})");
    }

    // Para la sonda del recorrido: cuántos mensajes se han mandado a la página y cuánto ocupan, en caracteres.
    private long _posts, _postedChars, _lastPost, _biggestPost;

    /// <summary>Nombre de la persona para sus mensajes; «User» si no ha puesto ninguno.</summary>
    private string UserName() => string.IsNullOrWhiteSpace(_userName()) ? "User" : _userName()!.Trim();

    /// <summary>Primer instante del periodo pedido por la página, en UTC; null es «todo».</summary>
    private static DateTime? Since(string period) => (period switch
    {
        "today" => History.From(HistoryPeriod.Today, DateTime.Today),
        "month" => History.From(HistoryPeriod.Month, DateTime.Today),
        "all" => null,
        _ => History.From(HistoryPeriod.Week, DateTime.Today),
    })?.ToUniversalTime();

    /// <summary>El trabajo en segundo plano de una sesión como lo espera la página; null si no tiene nada.</summary>
    private static object? BackgroundOf(SessionState s) => s.Background is { } bg && (bg.Agents.Count > 0 || bg.Commands.Count > 0)
        ? new
        {
            agents = bg.Agents.Select(a => new { id = a.Id, description = a.Description, start = a.StartUtc, running = a.Running, finished = a.FinishedUtc }),
            commands = bg.Commands.Select(c => new { id = c.Id, description = c.Description, start = c.StartUtc, awaited = c.Awaited, by = c.By }),
            turnEnd = bg.TurnEndUtc,
        }
        : null;

    /// <summary>Clave del estado de una sesión abierta, para su color en la página.</summary>
    private static string StatusKey(SessionStatus status) => status switch
    {
        SessionStatus.Working => "working",
        SessionStatus.Waiting => "waiting",
        SessionStatus.Done => "done",
        SessionStatus.Agents => "agents",
        SessionStatus.Command => "command",
        _ => "idle",
    };

    /// <summary>Valor de texto de una propiedad del mensaje, o null.</summary>
    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
