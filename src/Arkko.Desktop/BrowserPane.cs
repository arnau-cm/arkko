using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Un navegador dentro de la app, para ver una página web sin salir de ella: su barra (atrás, adelante, recargar, la
/// dirección, copiarla y abrirla en el navegador del sistema) y la página. Se usa donde haga falta ver una web.</summary>
/// <remarks>
/// Es un visor, no un navegador para ir a cualquier sitio: la dirección no se escribe, solo se copia. Va aparte de la página
/// de Sesiones, con su propia carpeta de datos: ahí se queda la sesión iniciada de las webs que se abren (la de claude.ai,
/// para los artifacts privados), y ninguna página de fuera puede hablar con la app, porque no hay puente: no se atiende
/// ningún mensaje suyo. Solo abre direcciones http y https, y una página que pide otra ventana la abre en este mismo visor.
/// El componente del navegador se crea la primera vez que se abre algo, no antes.
/// </remarks>
public sealed class BrowserPane : DockPanel
{
    private static string Folder => Path.Combine(AppPaths.Home, "webview-navegador");

    private readonly Button _back;
    private readonly Button _forward;
    private readonly Button _reload;
    private readonly TextBlock _address;
    private readonly Panel _page = new();
    private readonly TextBlock _hint;
    private NativeWebView? _web;
    private Uri? _current;

    /// <summary>Crea el visor vacío, con su barra y un texto que dice qué se verá.</summary>
    /// <param name="hint">Lo que se lee mientras no se ha abierto nada.</param>
    public BrowserPane(string hint = "Aquí se ve la página que se abra.")
    {
        _back = Tool("", "Atrás", () => _web?.GoBack());
        _forward = Tool("", "Adelante", () => _web?.GoForward());
        _reload = Tool("", "Recargar", () => _web?.Refresh());
        _address = new TextBlock
        {
            FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0),
        };
        _address.Bind(TextBlock.FontFamilyProperty, this.GetResourceObservable("MonoFont"));
        _address.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("FgMuted"));
        var copy = Tool("", "Copiar la dirección", () => _ = Copy());
        var outside = Tool("", "Abrir en el navegador", () =>
        {
            if (_current is { } uri) SessionsView.OpenLink(uri.AbsoluteUri);
        });

        var tools = new DockPanel { Margin = new Thickness(8, 6), LastChildFill = true };
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { _back, _forward, _reload } };
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { copy, outside } };
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(right, Dock.Right);
        tools.Children.Add(left);
        tools.Children.Add(right);
        // La dirección, en una caja como la de un navegador: se lee y se copia, no se escribe.
        var field = new Border { CornerRadius = new CornerRadius(7), Padding = new Thickness(2, 3), Margin = new Thickness(6, 0), Child = _address };
        field.Bind(Border.BackgroundProperty, this.GetResourceObservable("ButtonFill"));
        tools.Children.Add(field);

        var bar = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = tools };
        bar.Bind(Border.BorderBrushProperty, this.GetResourceObservable("TaskCardEdge"));
        SetDock(bar, Dock.Top);
        Children.Add(bar);

        _hint = new TextBlock
        {
            Text = hint, FontSize = 12, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 360,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        _hint.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("FgFaint"));
        _page.Children.Add(_hint);
        Children.Add(_page);
        Refresh();
    }

    /// <summary>Lo que se ve ahora; null si nada.</summary>
    public Uri? Current => _current;

    /// <summary>Avisa de la página que se ve al terminar de cargarla, también si se llegó a ella desde otra.</summary>
    public event Action<Uri>? Navigated;

    /// <summary>Abre una dirección web; las que no son http ni https no se abren.</summary>
    public void Navigate(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !IsWeb(uri)) return;
        if (_web is null && !Create()) return;
        _current = uri;
        _web!.Navigate(uri);
        Refresh();
    }

    /// <summary>Si una dirección es de la web: lo demás (file:, un programa del sistema) no se abre aquí.</summary>
    private static bool IsWeb(Uri uri) => uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp;

    /// <summary>Crea el componente del navegador, con su carpeta de datos propia; false si el equipo no lo tiene.</summary>
    private bool Create()
    {
        try
        {
            var web = new NativeWebView();
            web.EnvironmentRequested += (_, e) =>
            {
#if !DEBUG
                e.EnableDevTools = false;
#endif
                switch (e)
                {
                    // Todos los visores comparten la carpeta, y dos navegadores con la misma carpeta tienen que pedirla igual.
                    case WindowsWebView2EnvironmentRequestedEventArgs edge:
                        edge.UserDataFolder = Folder;
                        break;
                    case GtkWebViewEnvironmentRequestedEventArgs gtk:
                        gtk.BaseDataDirectory = Folder;
                        gtk.BaseCacheDirectory = Path.Combine(Folder, "cache");
                        break;
                }
            };
            web.NavigationStarted += (_, e) =>
            {
                if (e.Request is { } uri && !IsWeb(uri))
                {
                    e.Cancel = true;
                    return;
                }
                if (e.Request is { } next) _current = next;
                Refresh();
            };
            web.NavigationCompleted += (_, _) =>
            {
                Refresh();
                if (_current is { } shown) Navigated?.Invoke(shown);
            };
            // Una página que pide otra ventana se abre aquí mismo: el visor no abre ventanas.
            web.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                if (e.Request is { } uri && IsWeb(uri)) web.Navigate(uri);
            };
            _page.Children.Clear();
            _page.Children.Add(web);
            _web = web;
            return true;
        }
        catch (Exception ex)
        {
            JsonStore.LogError("browser", ex);
            _hint.Text = "No se ha podido abrir el navegador integrado: hace falta el componente WebView2 de Microsoft. Usa «Abrir en el navegador».";
            return false;
        }
    }

    /// <summary>Pone al día la barra: la dirección y qué botones se pueden pulsar.</summary>
    private void Refresh()
    {
        _address.Text = _current?.AbsoluteUri ?? "";
        _back.IsEnabled = _web?.CanGoBack == true;
        _forward.IsEnabled = _web?.CanGoForward == true;
        _reload.IsEnabled = _web is not null && _current is not null;
    }

    /// <summary>Copia la dirección de lo que se ve.</summary>
    private async Task Copy()
    {
        try
        {
            if (_current is { } uri && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(uri.AbsoluteUri);
        }
        catch (Exception ex)
        {
            JsonStore.LogError("browser", ex);
        }
    }

    /// <summary>Un botón de la barra, con su icono y lo que hace.</summary>
    private Button Tool(string icon, string tip, Action act)
    {
        var glyph = new TextBlock { Text = icon, FontSize = 11, Margin = new Thickness(0, 2) };
        glyph.Classes.Add("icon");
        var button = new Button { Content = glyph, Theme = this.FindResource("ChipButton") as Avalonia.Styling.ControlTheme };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => act();
        return button;
    }
}
