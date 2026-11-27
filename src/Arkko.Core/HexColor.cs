using System.Globalization;
using System.Text.RegularExpressions;

namespace Arkko.Core;

/// <summary>Colores como código «#rrggbb» y su paso a tono, saturación y brillo, para el selector de color.</summary>
public static partial class HexColor
{
    /// <summary>Si el texto es un color «#rrggbb».</summary>
    public static bool IsValid(string? text) => text is not null && Pattern().IsMatch(text);

    /// <summary>Componentes rojo, verde y azul de un código; null si no es un color.</summary>
    public static (byte R, byte G, byte B)? ToRgb(string? text)
    {
        if (!IsValid(text)) return null;
        var n = int.Parse(text![1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return ((byte)(n >> 16), (byte)(n >> 8), (byte)n);
    }

    /// <summary>Código «#rrggbb», en minúsculas, de unos componentes.</summary>
    public static string FromRgb(byte r, byte g, byte b) => $"#{r:x2}{g:x2}{b:x2}";

    /// <summary>Tono (0–360), saturación y brillo (0–1) de un código; null si no es un color.</summary>
    /// <remarks>Un gris no tiene tono: sale 0, y quien lo muestre conserva el que tenía.</remarks>
    public static (double H, double S, double V)? ToHsv(string? text)
    {
        if (ToRgb(text) is not { } rgb) return null;
        double r = rgb.R / 255.0, g = rgb.G / 255.0, b = rgb.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var delta = max - Math.Min(r, Math.Min(g, b));
        double h = 0;
        if (delta > 0)
        {
            if (max == r) h = 60 * ((g - b) / delta % 6);
            else if (max == g) h = 60 * ((b - r) / delta + 2);
            else h = 60 * ((r - g) / delta + 4);
        }
        if (h < 0) h += 360;
        return (h, max == 0 ? 0 : delta / max, max);
    }

    /// <summary>Código «#rrggbb» de un tono (0–360), una saturación y un brillo (0–1); fuera de rango, se recortan.</summary>
    public static string FromHsv(double h, double s, double v)
    {
        h = (h % 360 + 360) % 360;
        s = Math.Clamp(s, 0, 1);
        v = Math.Clamp(v, 0, 1);
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;
        var (r, g, b) = (int)(h / 60) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        static byte Byte(double d) => (byte)Math.Round(d * 255);
        return FromRgb(Byte(r + m), Byte(g + m), Byte(b + m));
    }

    /// <summary>Luminosidad mínima, en OKLCH, de un color que se ve sobre un fondo oscuro.</summary>
    /// <remarks>Por debajo de la paleta de grupos por defecto, que no cambia: solo sube lo que no se lee, como un azul marino.</remarks>
    public const double MinLightOnDark = 0.62;

    /// <summary>Luminosidad máxima, en OKLCH, de un color que se ve sobre un fondo claro.</summary>
    public const double MaxLightOnLight = 0.65;

    /// <summary>El mismo color con la luminosidad justa para verse sobre el fondo del tema: aclarado en uno oscuro y oscurecido en uno claro.</summary>
    /// <remarks>
    /// Conserva el tono y, si cabe, la intensidad: solo cambia la luminosidad, en OKLCH, que es la que percibe el ojo. Un color
    /// que ya se ve sale igual. Es la misma regla que aplica la página de Sesiones en CSS (sesiones.css, «--g»), para que un
    /// repositorio se vea del mismo color en la ventana y en la página.
    /// </remarks>
    public static (byte R, byte G, byte B) Readable((byte R, byte G, byte B) rgb, bool dark) =>
        WithLightness(rgb, l => dark ? Math.Max(l, MinLightOnDark) : Math.Min(l, MaxLightOnLight));

    /// <summary>Luminosidad máxima, en OKLCH, de un fondo con texto blanco encima.</summary>
    public const double MaxLightUnderWhite = 0.6;

    /// <summary>El mismo color, oscurecido lo justo para que un texto blanco encima se lea; uno ya oscuro sale igual.</summary>
    /// <remarks>Para las franjas con el nombre del repositorio: el texto va siempre en blanco, también sobre un color claro.</remarks>
    public static (byte R, byte G, byte B) UnderWhite((byte R, byte G, byte B) rgb) =>
        WithLightness(rgb, l => Math.Min(l, MaxLightUnderWhite));

    /// <summary>El mismo color con otra luminosidad en OKLCH, conservando el tono y, si cabe en sRGB, la intensidad.</summary>
    private static (byte R, byte G, byte B) WithLightness((byte R, byte G, byte B) rgb, Func<double, double> lightness)
    {
        var (l, a, b) = ToOklab(rgb);
        var target = lightness(l);
        if (Math.Abs(target - l) < 1e-9) return rgb;
        // Con la luminosidad nueva, la intensidad original puede no caber en sRGB: se rebaja lo justo para que quepa.
        double low = 0, high = 1;
        if (!InGamut(FromOklab(target, a, b)))
            for (var i = 0; i < 20; i++)
            {
                var mid = (low + high) / 2;
                if (InGamut(FromOklab(target, a * mid, b * mid))) low = mid;
                else high = mid;
            }
        else low = 1;
        var (r, g, bl) = FromOklab(target, a * low, b * low);
        return (Encode(r), Encode(g), Encode(bl));
    }

    /// <summary>Un color sRGB en OKLab: luminosidad (0–1) y sus dos ejes de color.</summary>
    private static (double L, double A, double B) ToOklab((byte R, byte G, byte B) rgb)
    {
        double r = Linear(rgb.R), g = Linear(rgb.G), b = Linear(rgb.B);
        var l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        var m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        var s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
        return (0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
            1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
            0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
    }

    /// <summary>Un color OKLab en sRGB lineal, sin recortar: fuera de 0–1 es que no cabe.</summary>
    private static (double R, double G, double B) FromOklab(double lightness, double a, double b)
    {
        var l = Math.Pow(lightness + 0.3963377774 * a + 0.2158037573 * b, 3);
        var m = Math.Pow(lightness - 0.1055613458 * a - 0.0638541728 * b, 3);
        var s = Math.Pow(lightness - 0.0894841775 * a - 1.2914855480 * b, 3);
        return (4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
            -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
            -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s);
    }

    private static bool InGamut((double R, double G, double B) c) =>
        c.R is >= -1e-6 and <= 1 + 1e-6 && c.G is >= -1e-6 and <= 1 + 1e-6 && c.B is >= -1e-6 and <= 1 + 1e-6;

    private static double Linear(byte c) => c / 255.0 is var v && v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);

    private static byte Encode(double linear)
    {
        var v = Math.Clamp(linear, 0, 1);
        var srgb = v <= 0.0031308 ? v * 12.92 : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;
        return (byte)Math.Round(Math.Clamp(srgb, 0, 1) * 255);
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex Pattern();
}
