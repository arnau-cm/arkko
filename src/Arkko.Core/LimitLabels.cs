using System.Globalization;

namespace Arkko.Core;

/// <summary>Nombres legibles para las claves de rate_limits que envía Claude Code.</summary>
/// <remarks>
/// Las claves no se fijan en código: cualquier límite nuevo que aparezca en el JSON (por ejemplo,
/// uno semanal propio de un modelo) se muestra con un nombre derivado de su clave.
/// </remarks>
public static class LimitLabels
{
    private static readonly Dictionary<string, string> Periods = new(StringComparer.OrdinalIgnoreCase)
    {
        ["five_hour"] = "5 h",
        ["seven_day"] = "7 días",
    };

    /// <summary>Devuelve la etiqueta de una clave: seven_day_fable → 7 días Fable.</summary>
    public static string For(string key)
    {
        foreach (var (prefix, label) in Periods.OrderByDescending(p => p.Key.Length))
        {
            if (key.Equals(prefix, StringComparison.OrdinalIgnoreCase)) return label;
            if (key.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase))
                return label + " " + Title(key[(prefix.Length + 1)..]);
        }
        return Title(key);
    }

    /// <summary>Orden de presentación: primero 5 h, luego 7 días, luego el resto.</summary>
    public static int Order(string key)
    {
        if (key.Equals("five_hour", StringComparison.OrdinalIgnoreCase)) return 0;
        if (key.Equals("seven_day", StringComparison.OrdinalIgnoreCase)) return 1;
        return 2;
    }

    /// <summary>Convierte snake_case en palabras con mayúscula inicial.</summary>
    private static string Title(string snake) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(snake.Replace('_', ' '));
}
