using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Arkko.Desktop;

/// <summary>La marca de dónde caerá un panel que se arrastra: un rectángulo del color de acento, por encima de todo.</summary>
/// <remarks>
/// Es una ventanita aparte, transparente y que no se puede pulsar: las páginas web de los paneles van en su propia capa
/// del sistema, y lo que la app dibuja dentro de su ventana queda por debajo de ellas.
/// </remarks>
internal sealed class DropHint : Window
{
    private readonly Border _fill;

    /// <summary>Crea la marca, sin enseñarla.</summary>
    public DropHint()
    {
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        IsHitTestVisible = false;
        _fill = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(2) };
        _fill.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Accent"));
        var accent = Application.Current?.FindResource("Accent") as ISolidColorBrush;
        _fill.Background = new SolidColorBrush(accent?.Color ?? Colors.Orange, 0.22);
        Content = _fill;
    }

    /// <summary>Pone la marca sobre un rectángulo de la pantalla, en píxeles; null la esconde.</summary>
    public void ShowAt(PixelRect? area)
    {
        if (area is not { Width: > 0, Height: > 0 } rect)
        {
            if (IsVisible) Hide();
            return;
        }
        var scale = Screens.ScreenFromPoint(rect.Center)?.Scaling ?? 1;
        Position = rect.Position;
        Width = rect.Width / scale;
        Height = rect.Height / scale;
        if (!IsVisible) Show();
    }
}
