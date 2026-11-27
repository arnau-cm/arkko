using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Arkko.Desktop;

/// <summary>La pieza Navegador del corcho: el navegador integrado en un recuadro, que recuerda la página que enseña y donde
/// otros widgets del mismo corcho abren lo suyo (Artifacts, sus artifacts).</summary>
/// <remarks>
/// Es el mismo visor que Artifacts (<see cref="BrowserPane"/>), con su barra y sin escribir direcciones: enseña lo que se le
/// manda abrir y lo que se pulsa dentro. Se pinta una sola vez: el visor no se rehace al repintar.
/// </remarks>
internal sealed class BrowserWidget : CorkWidget
{
    private static readonly HashSet<BrowserWidget> Live = [];

    private readonly BrowserPane _pane = new("Aquí se ve lo que se abra desde otro widget del corcho, como un artifact.");
    private bool _opened;

    /// <summary>Crea el navegador vacío, sin el margen de los widgets: la barra va de borde a borde.</summary>
    public BrowserWidget()
    {
        Padding = new Thickness(0);
        _pane.Navigated += uri => Set("url", uri.AbsoluteUri);
    }

    public override PanelSection Section => PanelSection.Artifacts;

    public override bool InPanel => false;

    protected override void Attached() => Live.Add(this);

    protected override void Detached() => Live.Remove(this);

    // Abre la página guardada la primera vez que se ve, no al cargar el corcho.
    protected override void Started()
    {
        if (_opened || Get("url") is not { } url) return;
        _opened = true;
        _pane.Navigate(url);
    }

    public override IEnumerable<Control> MenuItems()
    {
        if (_pane.Current is { } uri) yield return Ui.Item("Abrir en el navegador del sistema", () => SessionsView.OpenLink(uri.AbsoluteUri));
    }

    protected override Control Build() => _pane;

    /// <summary>Enseña una página.</summary>
    private void Open(string url)
    {
        _opened = true;
        _pane.Navigate(url);
    }

    /// <summary>Abre una página en el navegador del corcho donde va un control: en el que ya haya o, si no hay ninguno, en uno
    /// nuevo al lado; false si el control no va en un corcho.</summary>
    public static bool OpenIn(Control from, string url)
    {
        if (TopLevel.GetTopLevel(from) is not CorkWindow) return false;
        if (Live.FirstOrDefault(b => TopLevel.GetTopLevel(b) == TopLevel.GetTopLevel(from)) is { } browser)
        {
            browser.Open(url);
            if (browser.FindAncestorOfType<PiecePane>() is { } shown) Pieces.Reveal(shown);
            return true;
        }
        if (from.FindAncestorOfType<PiecePane>() is not { } beside) return false;
        var pane = Pieces.Make(NativeViews.Spec("browser") with { Path = Guid.NewGuid().ToString("N")[..8] });
        pane.WidgetState = new Dictionary<string, string> { ["url"] = url };
        return CorkWindow.AddBeside(beside, pane);
    }
}
