using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Arkko.Desktop;

/// <summary>El punto de una sesión que espera a sus agentes: un punto dentro de un círculo de rayas que gira despacio.</summary>
/// <remarks>Las rayas dicen que el trabajo sigue fuera de la sesión, también para quien no distingue bien el violeta. El giro lo
/// pone el estilo <c>OrbitDot</c> de Templates.axaml.</remarks>
public sealed class OrbitDot : Control
{
    public static readonly StyledProperty<IBrush?> BrushProperty = AvaloniaProperty.Register<OrbitDot, IBrush?>(nameof(Brush));

    static OrbitDot() => AffectsRender<OrbitDot>(BrushProperty);

    /// <summary>Crea el punto con su giro preparado.</summary>
    public OrbitDot()
    {
        RenderTransform = new RotateTransform();
        RenderTransformOrigin = RelativePoint.Center;
    }

    /// <summary>El color del círculo y del punto.</summary>
    public IBrush? Brush
    {
        get => GetValue(BrushProperty);
        set => SetValue(BrushProperty, value);
    }

    /// <summary>Pinta el círculo de rayas y el punto, centrados en el hueco que tenga.</summary>
    public override void Render(DrawingContext context)
    {
        if (Brush is not { } brush) return;
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        const double stroke = 1.4;
        var ring = new Pen(brush, stroke, new DashStyle([1.6, 1.3], 0));
        var radius = (size - stroke) / 2;
        context.DrawEllipse(null, ring, center, radius, radius);
        var dot = size * 0.2;
        context.DrawEllipse(brush, null, center, dot, dot);
    }
}
