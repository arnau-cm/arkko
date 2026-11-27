using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Un widget del corcho en directo («Te toca» y los que vengan) en su página web local, que recibe la foto de
/// las sesiones de <see cref="LiveSessions"/> y solo dibuja.</summary>
/// <remarks>
/// Todos los widgets en directo comparten página (directo.html), que pinta el que se le pide en la dirección. La foto
/// solo llega mientras el widget se ve; el navegador se descarga al rato de dejar de verse.
/// </remarks>
internal sealed class LiveView : Grid
{
    // Sin verse durante este rato, se descarga el navegador.
    private static readonly TimeSpan IdleUnload = TimeSpan.FromMinutes(2);

    private readonly string _widget;
    private readonly bool _faces;
    private readonly TextBlock _loading;
    private readonly TextBlock _failure;
    private readonly Avalonia.Threading.DispatcherTimer _unload = new() { Interval = IdleUnload };
    private NativeWebView? _web;
    private IDisposable? _listening;
    private string? _snapshot;
    private string? _options;
    private bool _ready, _active;

    /// <summary>Se pulsa una sesión: hay que abrirla.</summary>
    public event Action<string>? SessionRequested;

    /// <summary>Se da por vista la revisión de una sesión.</summary>
    public event Action<string>? ReviewSeen;

    /// <summary>Crea la vista de un widget, por su nombre en la página («tetoca»); la página no se carga hasta verse.</summary>
    /// <param name="faces">Si el widget necesita también el reloj de cada sesión.</param>
    public LiveView(string widget, bool faces = false)
    {
        _widget = widget;
        _faces = faces;
        _loading = new TextBlock { Text = "Cargando…", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _loading.Bind(TextBlock.ForegroundProperty, _loading.GetResourceObservable("FgMuted"));
        _failure = new TextBlock
        {
            IsVisible = false, TextWrapping = TextWrapping.Wrap, MaxWidth = 420, Margin = new Avalonia.Thickness(16),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center,
            Text = "No se ha podido abrir el widget: hace falta el componente WebView2 de Microsoft, que viene con Windows 11. El detalle está en errors.log.",
        };
        _failure.Bind(TextBlock.ForegroundProperty, _failure.GetResourceObservable("FgMuted"));
        Children.Add(_loading);
        Children.Add(_failure);
        AttachedToVisualTree += (_, _) => App.ThemeChanged += SendTheme;
        DetachedFromVisualTree += (_, _) => App.ThemeChanged -= SendTheme;
        _unload.Tick += (_, _) =>
        {
            _unload.Stop();
            if (!_active) Unload();
        };
    }

    /// <summary>Al verse: carga la página si hace falta y empieza a recibir la foto de las sesiones.</summary>
    public void Activate()
    {
        _active = true;
        _unload.Stop();
        if (_web is null) Start();
        _listening ??= LiveSessions.Listen(OnSnapshot, _faces);
    }

    /// <summary>Al dejar de verse: deja de recibir; el navegador se descarga al rato.</summary>
    public void Deactivate()
    {
        _active = false;
        _listening?.Dispose();
        _listening = null;
        if (_web is not null) _unload.Start();
    }

    /// <summary>Se deja de usar para siempre: deja de recibir y descarga el navegador ya.</summary>
    public void Close()
    {
        Deactivate();
        _unload.Stop();
        Unload();
    }

    /// <summary>Pasa a la página lo elegido en el widget (en el carril, a qué sesión sigue); se le vuelve a pasar al
    /// recargarse.</summary>
    public void SetOptions(object options)
    {
        _options = JsonSerializer.Serialize(new { type = "options", options }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        SendOptions();
    }

    /// <summary>Manda a la página lo elegido en el widget, si está lista.</summary>
    private void SendOptions()
    {
        if (_ready && _options is not null && _web is { } web) _ = web.InvokeScript($"window.agentPulseReceive && window.agentPulseReceive({_options})");
    }

    /// <summary>Llega una foto nueva: se guarda y, si la página está lista, se le pasa.</summary>
    private void OnSnapshot(string json)
    {
        _snapshot = json;
        if (_ready && _web is { } web) _ = web.InvokeScript($"window.agentPulseReceive && window.agentPulseReceive({json})");
    }

    /// <summary>Crea el navegador, con la misma carpeta de datos que el de Sesiones, y carga la página con su widget.</summary>
    private void Start()
    {
        try
        {
            var page = Path.Combine(AppContext.BaseDirectory, "Web", "directo.html");
            var pageUri = new UriBuilder(new Uri(page)) { Query = $"w={Uri.EscapeDataString(_widget)}" }.Uri;
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
            Children.Insert(0, web);
            _loading.IsVisible = true;
            web.IsVisible = true;
            web.Opacity = 0;
            web.Navigate(pageUri);
        }
        catch (Exception ex)
        {
            JsonStore.LogError("live-webview", ex);
            if (_web is not null) _web.IsVisible = false;
            _loading.IsVisible = false;
            _failure.IsVisible = true;
        }
    }

    /// <summary>Descarga el navegador y su página, para que no ocupe memoria mientras no se ve.</summary>
    private void Unload()
    {
        if (_web is null) return;
        Children.Remove(_web);
        _web = null;
        _ready = false;
        _loading.IsVisible = true;
    }

    /// <summary>Atiende lo que pide la página: que está lista, abrir una sesión o dar por vista su revisión.</summary>
    private void OnMessage(string? body)
    {
        if (string.IsNullOrEmpty(body)) return;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var m = doc.RootElement;
            var id = m.TryGetProperty("id", out var i) ? i.GetString() : null;
            switch (m.TryGetProperty("type", out var t) ? t.GetString() : null)
            {
                case "ready":
                    _ready = true;
                    SendTheme();
                    SendOptions();
                    if (_snapshot is not null) OnSnapshot(_snapshot);
                    if (_web is { } web)
                    {
                        web.Opacity = 1;
                        _loading.IsVisible = false;
                    }
                    break;
                case "open" when id is { Length: > 0 }:
                    SessionRequested?.Invoke(id);
                    break;
                case "seen" when id is { Length: > 0 }:
                    ReviewSeen?.Invoke(id);
                    break;
            }
        }
        catch (JsonException)
        {
            // Un mensaje que no se entiende no rompe el widget.
        }
    }

    /// <summary>Manda a la página los colores del tema activo.</summary>
    private void SendTheme()
    {
        if (!_ready || _web is not { } web) return;
        web.Background = new SolidColorBrush(PageTheme.PageColor());
        var json = JsonSerializer.Serialize(PageTheme.Message(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        _ = web.InvokeScript($"window.agentPulseReceive && window.agentPulseReceive({json})");
    }
}
