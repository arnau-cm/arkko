using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace Arkko.Desktop;

/// <summary>El destello de Claude, el mismo que dibuja la página de Sesiones; se escala al tamaño que se le dé.</summary>
public sealed class ClaudeLogo : Viewbox
{
    /// <summary>Dibuja el destello.</summary>
    public ClaudeLogo() => Child = new Path
    {
        Width = 24,
        Height = 24,
        Stroke = new SolidColorBrush(Color.Parse("#D97757")),
        StrokeThickness = 2.4,
        StrokeLineCap = PenLineCap.Round,
        Data = Geometry.Parse("M14.18,12.31 L22.40,13.46 M13.73,13.35 L18.30,16.93 M12.82,14.04 L15.93,21.74 M11.69,14.18 L10.89,19.92 M10.65,13.73 L5.54,20.27 M9.96,12.82 L4.58,15.00 M9.82,11.69 L1.60,10.54 M10.27,10.65 L5.70,7.07 M11.18,9.96 L8.07,2.26 M12.31,9.82 L13.11,4.08 M13.35,10.27 L18.46,3.73 M14.04,11.18 L19.42,9.00"),
    };
}

/// <summary>Escudo de secreto sin revisar: el mismo dibujo que en Sesiones, rojo con la exclamación blanca.</summary>
public sealed class ShieldIcon : Viewbox
{
    /// <summary>Dibuja el escudo a 12 px.</summary>
    public ShieldIcon()
    {
        Width = 12;
        Height = 12;
        var shield = new Path { Data = Geometry.Parse("M8,1 L2.5,3 V7.2 C2.5,10.5 4.8,13.5 8,15 C11.2,13.5 13.5,10.5 13.5,7.2 V3 Z") };
        shield.Bind(Shape.FillProperty, shield.GetResourceObservable("Danger"));
        Child = new Canvas
        {
            Width = 16,
            Height = 16,
            Children =
            {
                shield,
                new Path
                {
                    Stroke = Brushes.White,
                    StrokeThickness = 1.7,
                    StrokeLineCap = PenLineCap.Round,
                    Data = Geometry.Parse("M8,4.6 V8.4 M8,10.7 V10.9"),
                },
            },
        };
    }
}

/// <summary>Aviso de hooks que fallan: un triángulo del color de peligro con la exclamación blanca.</summary>
public sealed class HookIcon : Viewbox
{
    /// <summary>Dibuja el triángulo a 12 px.</summary>
    public HookIcon()
    {
        Width = 12;
        Height = 12;
        var triangle = new Path { Data = Geometry.Parse("M8,1.2 L15.2,14.4 H0.8 Z") };
        triangle.Bind(Shape.FillProperty, triangle.GetResourceObservable("Danger"));
        Child = new Canvas
        {
            Width = 16,
            Height = 16,
            Children =
            {
                triangle,
                new Path
                {
                    Stroke = Brushes.White,
                    StrokeThickness = 1.7,
                    StrokeLineCap = PenLineCap.Round,
                    Data = Geometry.Parse("M8,5.6 V9.4 M8,11.8 V12"),
                },
            },
        };
    }
}

/// <summary>Trazos para dibujar con un Path, con el color que se le dé.</summary>
public static class GlyphShapes
{
    /// <summary>Una PR: dos commits unidos por la rama y la flecha hacia la base, en 16 × 16.</summary>
    public static Geometry PullRequest { get; } = Geometry.Parse(
        "M2.4,3.5 A1.6,1.6 0 1 1 5.6,3.5 A1.6,1.6 0 1 1 2.4,3.5 M2.4,12.5 A1.6,1.6 0 1 1 5.6,12.5 A1.6,1.6 0 1 1 2.4,12.5 " +
        "M10.4,12.5 A1.6,1.6 0 1 1 13.6,12.5 A1.6,1.6 0 1 1 10.4,12.5 M4,5.1 V10.9 M12,10.9 V7 C12,5.6 11,4.5 9.5,4.5 H7.6 M9.1,3 L7.6,4.5 L9.1,6");
}
