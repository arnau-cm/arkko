using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Arkko.Desktop;

/// <summary>La paleta de buscar en el repositorio de un corcho o de una ventana suelta: una ventana sin bordes, centrada
/// sobre la ventana desde la que se pide, por encima de todos sus paneles.</summary>
/// <remarks>
/// Cada panel es una página aparte que no puede pintar fuera de su recuadro: por eso la paleta va en su propia ventana.
/// Hay una por ventana, y al cerrarla se esconde en lugar de cerrarse, para que al volver a abrirla siga lo buscado. Al
/// pulsar fuera de ella la cierra su página, que es la que sabe cuándo pierde el teclado: la ventana se da por
/// desactivada también cuando el teclado pasa a la página que lleva dentro.
/// </remarks>
internal sealed class FinderWindow : Window
{
    // El tamaño de la paleta, en puntos, si cabe; si no, lo que deje la ventana desde la que se pide.
    private const double FinderWidth = 980;
    private const double FinderHeight = 620;

    private static readonly Dictionary<Window, FinderWindow> ByOwner = new();
    private readonly Window _owner;
    private PiecePane? _pane;

    /// <summary>Crea la ventana de la paleta de una ventana, aún sin página.</summary>
    private FinderWindow(Window owner)
    {
        _owner = owner;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        ShowInTaskbar = false;
        CanResize = false;
        Background = new SolidColorBrush(PageTheme.PageColor());
        owner.Closed += (_, _) =>
        {
            ByOwner.Remove(owner);
            _pane?.Close();
            Close();
        };
    }

    /// <summary>Abre la paleta sobre la ventana de un panel, en un modo y con lo buscado si se da.</summary>
    /// <param name="cwd">La carpeta de la sesión del panel, donde se busca.</param>
    public static void Open(PiecePane origin, string? mode, string? word, string? cwd)
    {
        if (TopLevel.GetTopLevel(origin) is not Window owner) return;
        if (!ByOwner.TryGetValue(owner, out var finder)) ByOwner[owner] = finder = new FinderWindow(owner);
        finder.Load(origin, cwd);
        finder.Place();
        if (!finder.IsVisible) finder.Show(owner);
        finder.Activate();
        finder._pane!.Deliver(new { type = "finderOpen", mode, word, here = true });
        // El teclado se le da a la página cuando la ventana ya está activa: antes, se lo queda la ventana.
        DispatcherTimer.RunOnce(() => finder._pane?.FocusPage(), TimeSpan.FromMilliseconds(120));
    }

    /// <summary>Esconde la paleta en la que va un panel, si es la suya, y devuelve el teclado a la ventana de detrás.</summary>
    /// <param name="outside">Si se esconde porque se ha pulsado fuera: entonces el teclado ya lo tiene lo pulsado y no se
    /// toca.</param>
    /// <remarks>Al esconder la ventana activa, Windows activa la siguiente que encuentra, que puede ser de otro
    /// programa.</remarks>
    public static void HideFor(PiecePane pane, bool outside)
    {
        if (TopLevel.GetTopLevel(pane) is not FinderWindow finder) return;
        finder.Hide();
        if (!outside) finder._owner.Activate();
    }

    /// <summary>Pone la página de la paleta de la sesión del panel: la que ya tenía, si es de la misma, o una nueva.</summary>
    private void Load(PiecePane origin, string? cwd)
    {
        if (_pane is null || !string.Equals(_pane.Spec.SessionId, origin.Spec.SessionId, StringComparison.OrdinalIgnoreCase))
        {
            _pane?.Close();
            _pane = Pieces.Make(new PieceSpec("finder", origin.Spec.SessionId, "Buscar", cwd));
            Content = _pane;
        }
        _pane.Origin = origin;
    }

    /// <summary>Centra la paleta sobre su ventana; si la ventana es más pequeña que la paleta, sobre su pantalla.</summary>
    private void Place()
    {
        var scale = _owner.DesktopScaling;
        var area = new PixelRect(_owner.Position, PixelSize.FromSize(_owner.ClientSize, scale));
        if (_owner.ClientSize.Width < FinderWidth * 0.75 || _owner.ClientSize.Height < FinderHeight * 0.75)
            area = Screens.ScreenFromWindow(_owner)?.WorkingArea ?? area;
        var width = Math.Min(FinderWidth, area.Width / scale - 48);
        var height = Math.Min(FinderHeight, area.Height / scale - 48);
        Width = width;
        Height = height;
        Position = new PixelPoint(area.X + (int)((area.Width - width * scale) / 2), area.Y + (int)((area.Height - height * scale) / 2));
    }
}
