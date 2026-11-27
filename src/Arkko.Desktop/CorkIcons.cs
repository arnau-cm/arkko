using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace Arkko.Desktop;

/// <summary>Los iconos del corcho, dibujados con trazos sobre una cuadrícula de 24: la chincheta y los del menú de un
/// recuadro.</summary>
/// <remarks>Van dibujados y no con la letra de iconos de la app, que es de Windows: el corcho tiene que verse igual en
/// macOS y en Linux.</remarks>
internal static class CorkIcons
{
    private static readonly Dictionary<string, string> Shapes = new()
    {
        ["pin"] = "M9 4h6l-1 6 4 3v2H6v-2l4-3z M12 15v6",
        ["split-right"] = "M5 4h14a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2z M12 4v16",
        ["split-down"] = "M5 4h14a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2z M3 12h18",
        ["tabs"] = "M3 9h7V5h11v15H3z",
        ["rotate"] = "M4 12a8 8 0 0 1 14-5.3 M18 3v4h-4 M20 12a8 8 0 0 1-14 5.3 M6 21v-4h4",
        ["columns"] = "M4 5h16v14H4z M9.3 5v14 M14.7 5v14",
        ["rows"] = "M4 5h16v14H4z M4 9.7h16 M4 14.3h16",
        ["grid"] = "M4 5h16v14H4z M12 5v14 M4 12h16",
        ["move"] = "M14 4h6v6 M20 4l-9 9 M18 14v5a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1h5",
        ["window"] = "M5 5h14a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2z M3 9h18",
        ["plus"] = "M12 5v14 M5 12h14",
        ["close"] = "M6 6l12 12 M18 6L6 18",
        ["edit"] = "M4 20h4L18.5 9.5a2.1 2.1 0 0 0-3-3L5 17v3z M13.5 6.5l3 3",
    };

    /// <summary>Un icono por su nombre, del tamaño pedido, en el color de texto apagado del tema o en el que se diga.</summary>
    public static Control Make(string name, double size = 15, IBrush? color = null)
    {
        var path = new Avalonia.Controls.Shapes.Path
        {
            Data = StreamGeometry.Parse(Shapes[name]), Width = size, Height = size, Stretch = Stretch.Uniform,
            StrokeThickness = 1.8, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
        };
        if (color is not null) path.Stroke = color;
        else path.Bind(Shape.StrokeProperty, path.GetResourceObservable("FgMuted"));
        return path;
    }
}
