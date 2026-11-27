using System.Globalization;

namespace Arkko.Core;

/// <summary>Una pantalla conectada: su rectángulo en píxeles de la pantalla virtual y su escala.</summary>
public sealed record ScreenArea(int X, int Y, int Width, int Height, double Scale)
{
    /// <summary>Si el punto, en píxeles, cae dentro de la pantalla.</summary>
    public bool Contains(int x, int y) => x >= X && y >= Y && x < X + Width && y < Y + Height;
}

/// <summary>El juego de pantallas conectadas a un equipo, para recordar dónde va cada ventana con cada uno: con tres
/// pantallas en la oficina y con solo la del portátil en casa.</summary>
public static class ScreenSet
{
    /// <summary>La huella de un juego de pantallas: cada una con su tamaño, su sitio y su escala, sin importar el orden en
    /// que las numere el sistema. Dos juegos iguales dan la misma.</summary>
    public static string Key(IEnumerable<ScreenArea> screens) =>
        string.Join("|", screens
            .OrderBy(s => s.X).ThenBy(s => s.Y)
            .Select(s => string.Create(CultureInfo.InvariantCulture, $"{s.Width}x{s.Height}@{s.X},{s.Y}*{s.Scale:0.##}")));

    /// <summary>Si una ventana con la esquina en ese punto se puede agarrar: el principio de su barra cae en alguna de las
    /// zonas útiles de las pantallas.</summary>
    public static bool Reachable(IEnumerable<ScreenArea> workAreas, int x, int y) =>
        workAreas.Any(a => a.Contains(x + 40, y + 20));

    /// <summary>Encoge y mueve un rectángulo, en píxeles, lo justo para que quepa entero en una zona útil.</summary>
    public static (int X, int Y, int Width, int Height) Fit(ScreenArea area, int x, int y, int width, int height)
    {
        width = Math.Min(width, area.Width);
        height = Math.Min(height, area.Height);
        return (Math.Clamp(x, area.X, area.X + area.Width - width), Math.Clamp(y, area.Y, area.Y + area.Height - height), width, height);
    }
}
