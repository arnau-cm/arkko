using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Arkko.Desktop;

/// <summary>El punto de una sesión que espera a un comando en marcha: un recuadro de consola con «>_», que respira.</summary>
/// <remarks>La forma, distinta del punto de los demás estados y de la órbita de los agentes, dice que espera a un comando
/// también para quien no distingue bien el cian. La respiración la pone el estilo <c>CommandDot</c> de Templates.axaml.</remarks>
public sealed class CommandDot : Control
{
    public static readonly StyledProperty<IBrush?> BrushProperty = AvaloniaProperty.Register<CommandDot, IBrush?>(nameof(Brush));

    static CommandDot() => AffectsRender<CommandDot>(BrushProperty);

    /// <summary>El color del recuadro y del «>_».</summary>
    public IBrush? Brush
    {
        get => GetValue(BrushProperty);
        set => SetValue(BrushProperty, value);
    }

    /// <summary>Pinta el recuadro con su fondo tenue y el «>_» en el centro del hueco que tenga.</summary>
    public override void Render(DrawingContext context)
    {
        if (Brush is not ISolidColorBrush brush) return;
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0) return;
        const double stroke = 1.3;
        var box = new Rect(stroke / 2, stroke / 2, width - stroke, height - stroke);
        var radius = Math.Min(width, height) * 0.25;
        context.DrawRectangle(new SolidColorBrush(brush.Color, 0.2), new Pen(brush, stroke), box, radius, radius);
        var text = new FormattedText(">_", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Consolas", FontStyle.Normal, FontWeight.Bold), height * 0.5, brush);
        context.DrawText(text, new Point((width - text.Width) / 2, (height - text.Height) / 2));
    }
}
