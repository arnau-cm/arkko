using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.Shapes;

namespace Arkko.Desktop;

/// <summary>La tarjeta que sigue al cursor mientras se arrastra un panel de un corcho: su nombre y, fuera de todos los
/// corchos, que irá a una ventana suelta.</summary>
/// <remarks>
/// Es una ventanita aparte, por encima de todo y que no se puede pulsar, como <see cref="DropHint"/>: el cursor pasa por
/// encima de páginas web y de otros corchos, y lo que la app dibuja dentro de una ventana queda por debajo de ellas.
/// </remarks>
internal sealed class DragGhost : Window
{
    // Cuánto se separa la tarjeta del cursor, abajo a la derecha, para no tapar dónde caerá.
    private static readonly Point Offset = new(16, 12);

    private readonly TextBlock _title;
    private readonly TextBlock _where;

    /// <summary>Crea la tarjeta, sin enseñarla.</summary>
    public DragGhost()
    {
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        IsHitTestVisible = false;
        SizeToContent = SizeToContent.WidthAndHeight;

        _title = new TextBlock { FontSize = 12, FontWeight = FontWeight.SemiBold, MaxWidth = 260, TextTrimming = TextTrimming.CharacterEllipsis };
        _title.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("Fg"));
        _where = new TextBlock { FontSize = 11, Text = "A una ventana suelta", IsVisible = false };
        _where.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("Accent"));
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _title, _where } };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { Grip(), texts } };
        var card = new Border
        {
            Child = row, Padding = new Thickness(12, 8, 14, 8), Margin = new Thickness(10), CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1), RenderTransform = new RotateTransform(-2),
            BoxShadow = BoxShadows.Parse("0 10 24 0 #8C000000"),
        };
        card.Bind(Border.BackgroundProperty, this.GetResourceObservable("TipFill"));
        card.Bind(Border.BorderBrushProperty, this.GetResourceObservable("TipEdge"));
        Content = card;
    }

    /// <summary>Los seis puntos de agarrar, dibujados para no depender de una letra de iconos.</summary>
    private Control Grip()
    {
        var dots = new Canvas { Width = 8, Height = 12, VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < 6; i++)
        {
            var dot = new Ellipse { Width = 2.6, Height = 2.6 };
            dot.Bind(Shape.FillProperty, this.GetResourceObservable("FgFaint"));
            Canvas.SetLeft(dot, i % 2 * 5);
            Canvas.SetTop(dot, i / 2 * 4.5);
            dots.Children.Add(dot);
        }
        return dots;
    }

    /// <summary>Prepara la tarjeta con el nombre del panel que se empieza a arrastrar.</summary>
    public void Start(string title)
    {
        _title.Text = title;
        _where.IsVisible = false;
    }

    /// <summary>Lleva la tarjeta junto al cursor, en píxeles de la pantalla, y dice si soltar ahí lo saca a una ventana suelta.</summary>
    public void Follow(PixelPoint cursor, bool loose)
    {
        _where.IsVisible = loose;
        var scale = Screens.ScreenFromPoint(cursor)?.Scaling ?? 1;
        Position = new PixelPoint(cursor.X + (int)(Offset.X * scale), cursor.Y + (int)(Offset.Y * scale));
        if (!IsVisible) Show();
    }
}
