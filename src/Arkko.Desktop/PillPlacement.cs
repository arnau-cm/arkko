using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Arkko.Desktop;

/// <summary>Dónde va la pastilla: en qué monitor y en qué punto de su zona útil, según lo elegido en Configuración → Minimizado.</summary>
/// <remarks>
/// La posición se guarda en proporción al hueco libre de la zona útil, descontados la pastilla y su margen: 0 es
/// pegada al borde izquierdo (o al de arriba) y 1 al derecho (o al de abajo). Así no depende del tamaño de la
/// pastilla, que crece con las sesiones, ni de la resolución.
/// </remarks>
public static class PillPlacement
{
    /// <summary>Margen entre la pastilla y el borde de la zona útil, en unidades de la interfaz.</summary>
    public const double Margin = 12;

    /// <summary>Monitor elegido, o el de la ventana si no hay elegido o no está conectado.</summary>
    public static Screen? ScreenFor(Window window, string? choice)
    {
        var screens = window.Screens;
        var chosen = choice is null ? null : Monitors(screens.All).FirstOrDefault(m => m.Key == choice).Screen;
        return chosen ?? screens.ScreenFromWindow(window) ?? screens.Primary;
    }

    /// <summary>Zona útil (sin la barra de tareas) del monitor de la pastilla, en píxeles.</summary>
    public static PixelRect Area(Window window, string? choice) =>
        ScreenFor(window, choice)?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);

    /// <summary>Esquina superior izquierda de una pastilla de un tamaño en una zona, para una posición en proporción.</summary>
    public static PixelPoint Position(PixelRect area, PixelSize size, int margin, double x, double y)
    {
        var freeX = Math.Max(0, area.Width - size.Width - 2 * margin);
        var freeY = Math.Max(0, area.Height - size.Height - 2 * margin);
        return new PixelPoint(area.X + margin + (int)Math.Round(Math.Clamp(x, 0, 1) * freeX),
            area.Y + margin + (int)Math.Round(Math.Clamp(y, 0, 1) * freeY));
    }

    /// <summary>Posición en proporción de una pastilla cuya esquina superior izquierda está en un punto, pegada a los bordes cercanos.</summary>
    /// <param name="snap">Distancia, en píxeles, a la que un borde atrae a la pastilla.</param>
    public static (double X, double Y) Fractions(PixelRect area, PixelSize size, int margin, PixelPoint topLeft, int snap)
    {
        static double Fraction(double offset, double free, int snap)
        {
            if (free <= 0) return 1;
            offset = Math.Clamp(offset, 0, free);
            if (offset <= snap) return 0;
            if (free - offset <= snap) return 1;
            return offset / free;
        }

        return (Fraction(topLeft.X - area.X - margin, area.Width - size.Width - 2 * margin, snap),
            Fraction(topLeft.Y - area.Y - margin, area.Height - size.Height - 2 * margin, snap));
    }

    /// <summary>Hacia dónde se despliega el panel de la pastilla: hacia dentro de la pantalla, encima si está en la mitad de abajo.</summary>
    public static PlacementMode PanelPlacement(double y) => y < 0.5 ? PlacementMode.Bottom : PlacementMode.Top;

    /// <summary>Pantallas conectadas con su clave y su nombre, de izquierda a derecha.</summary>
    /// <remarks>
    /// La clave reconoce el monitor al volver a conectarlo: el nombre que da el sistema y, si no da ninguno, su
    /// resolución y su posición, que no cambian al conectar otro. Dos monitores con el mismo nombre se distinguen
    /// con «#2» detrás, por su orden de izquierda a derecha. El nombre que se enseña, sin nombre del sistema, es
    /// «Pantalla» y su número en ese orden.
    /// </remarks>
    public static IReadOnlyList<(string Key, string Label, Screen Screen)> Monitors(IReadOnlyList<Screen> all)
    {
        var list = new List<(string, string, Screen)>();
        var seen = new Dictionary<string, int>();
        var ordered = all.OrderBy(s => s.Bounds.X).ThenBy(s => s.Bounds.Y).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var screen = ordered[i];
            var b = screen.Bounds;
            var named = !string.IsNullOrWhiteSpace(screen.DisplayName);
            var name = named ? screen.DisplayName!.Trim() : $"Pantalla {i + 1}";
            var key = named ? name : $"{b.Width}x{b.Height}@{b.X},{b.Y}";
            seen[key] = seen.GetValueOrDefault(key) + 1;
            if (seen[key] > 1) key += $" #{seen[key]}";
            var label = $"{name} · {b.Width}×{b.Height}" + (screen.IsPrimary ? " · principal" : "");
            list.Add((key, label, screen));
        }
        return list;
    }
}
