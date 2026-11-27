using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Arkko.Desktop;

/// <summary>Ventana suelta con un panel: la ficha de un fichero, o el lienzo, el chat o los cambios de una sesión.</summary>
/// <remarks>Solo es el marco: la barra con el nombre del panel y los botones de la ventana. Lo que enseña, y cómo habla
/// con la app, es del panel (<see cref="PiecePane"/>), que se puede sacar de aquí para ponerlo en otro sitio.</remarks>
public partial class PieceWindow : Window, IPieceHost
{
    private PiecePane? _pane;

    /// <summary>Lo necesita el diseñador de XAML; la app abre la ventana con <see cref="Hold"/>.</summary>
    public PieceWindow() : this(null)
    {
    }

    /// <summary>Monta la ventana alrededor de un panel, con su nombre y un tamaño a la medida de lo que enseña.</summary>
    private PieceWindow(PiecePane? pane)
    {
        InitializeComponent();
        _pane = pane;
        DiaryInk.Tip(PinButton, DiaryInk.Card("Clavar en el corcho", "Lo pone en un recuadro nuevo del último corcho que se usó, y cierra esta ventana."));
        DiaryInk.Tip(WidgetMenuButton, DiaryInk.Card("Opciones", "Lo que se puede elegir en este widget, y abrir su sección en el panel."));
        if (pane is not null)
        {
            WidgetMenuButton.IsVisible = pane.Widget is not null;
            Host.Children.Add(pane);
            Title = $"{pane.Spec.Title} · Arkko";
            Heading.Text = pane.Spec.Title;
            // El chat es una columna; los cambios, la lista y el fichero al lado.
            (Width, Height) = pane.Spec.Kind switch
            {
                "chat" or "home" or "review" => (560, 860),
                "changes" or "board" => (1300, 860),
                "artifacts" => (900, 760),
                "diary" or "stats" => (1200, 860),
                _ => (1000, 800),
            };
        }
        PropertyChanged += (_, e) =>
        {
            if (e.Property != WindowStateProperty) return;
            // Minimizada no se ve: el panel deja de seguir su sesión y se pone al día al volver.
            if (_pane is { } shown) shown.Paused = WindowState == WindowState.Minimized;
            var maximized = WindowState == WindowState.Maximized;
            MaximizeButton.Content = maximized ? "" : "";
            ToolTip.SetTip(MaximizeButton, maximized ? "Restaurar" : "Maximizar");
        };
        // Al cerrar la ventana se cierra su panel, salvo que se haya llevado a otro sitio.
        Closed += (_, _) =>
        {
            if (_pane is { } left)
            {
                Host.Children.Remove(left);
                left.Close();
            }
            CorkWindow.NotifyListChanged();
        };
        Opened += (_, _) => CorkWindow.NotifyListChanged();
    }

    /// <summary>Enseña un panel en una ventana suelta nueva: centrada, o con su barra donde se diga, en píxeles de la pantalla.</summary>
    /// <remarks>Donde se diga, la ventana se encoge y se corre lo justo para caber entera en la pantalla de ese punto.</remarks>
    internal static void Hold(PiecePane pane, PixelPoint? at = null)
    {
        var window = new PieceWindow(pane);
        if (at is { } point)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            var corner = new PixelPoint(point.X - 120, point.Y - 20);
            if (window.Screens.ScreenFromPoint(point) is { } screen)
            {
                var area = screen.WorkingArea;
                var scale = screen.Scaling;
                window.Width = Math.Min(window.Width, area.Width / scale);
                window.Height = Math.Min(window.Height, area.Height / scale);
                var (width, height) = ((int)(window.Width * scale), (int)(window.Height * scale));
                corner = new PixelPoint(Math.Clamp(corner.X, area.X, area.Right - width), Math.Clamp(corner.Y, area.Y, area.Bottom - height));
            }
            window.Position = corner;
        }
        window.Show();
    }

    /// <summary>Trae la ventana al frente.</summary>
    void IPieceHost.Reveal(PiecePane pane)
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    /// <summary>Saca el panel sin cerrarlo, para ponerlo en otro sitio, y cierra la ventana.</summary>
    internal PiecePane? Release()
    {
        var pane = _pane;
        _pane = null;
        if (pane is not null) Host.Children.Remove(pane);
        Close();
        return pane;
    }

    /// <summary>Lleva el panel al corcho que se usó por última vez, en un recuadro nuevo, y cierra esta ventana.</summary>
    private void Pin_Click(object? sender, RoutedEventArgs e)
    {
        if (Release() is { } pane) CorkWindow.Pin(pane);
    }

    /// <summary>Abre el menú del widget de la ventana: sus opciones y, si tiene sección, «Abrir en el panel».</summary>
    private void WidgetMenu_Click(object? sender, RoutedEventArgs e)
    {
        if (_pane?.Widget is not { } widget) return;
        var menu = new ContextMenu { Placement = Avalonia.Controls.PlacementMode.BottomEdgeAlignedRight };
        foreach (var item in widget.MenuItems()) menu.Items.Add(item);
        if (widget.InPanel)
        {
            if (menu.Items.Count > 0) menu.Items.Add(new Separator());
            menu.Items.Add(Ui.Item("Abrir en el panel", () => SessionActions.RequestSection(widget.Section)));
        }
        if (menu.Items.Count > 0) menu.Open(WidgetMenuButton);
    }

    /// <summary>Minimiza la ventana.</summary>
    private void Minimize_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    /// <summary>Maximiza la ventana, o la devuelve a su tamaño si ya lo estaba.</summary>
    private void Maximize_Click(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    /// <summary>Cierra la ventana.</summary>
    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
