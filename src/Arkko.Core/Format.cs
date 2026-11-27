using System.Globalization;

namespace Arkko.Core;

/// <summary>Formatos compartidos por la statusline, los hooks y la ventana.</summary>
public static class Format
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    /// <summary>Abrevia un número de tokens: 950, 12,3k, 1,2M.</summary>
    public static string Tokens(long n) => n switch
    {
        >= 1_000_000 => (n / 1_000_000d).ToString("0.0", Es) + "M",
        >= 1_000 => (n / 1_000d).ToString("0.0", Es) + "k",
        _ => n.ToString(Es),
    };

    /// <summary>Formatea una duración corta: 7,3 s, 2m05s, 1h10m.</summary>
    public static string Duration(TimeSpan span)
    {
        if (span.TotalSeconds < 60) return span.TotalSeconds.ToString("0.0", Es) + " s";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}m{span.Seconds:00}s";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours}h{span.Minutes:00}m";
        return $"{(int)span.TotalDays}d{span.Hours}h";
    }

    /// <summary>Tiempo que falta hasta un reinicio, o cadena vacía si no se conoce.</summary>
    public static string Until(DateTime? utc)
    {
        if (utc is null) return "";
        var left = utc.Value - DateTime.UtcNow;
        if (left <= TimeSpan.Zero) return "ya";
        if (left.TotalDays >= 1) return $"{(int)left.TotalDays}d {left.Hours}h";
        if (left.TotalHours >= 1) return $"{(int)left.TotalHours}h {left.Minutes:00}m";
        return $"{Math.Max(1, (int)left.TotalMinutes)}m";
    }

    /// <summary>Antigüedad legible de una marca de tiempo: ahora, 3 min, 2 h.</summary>
    public static string Ago(DateTime utc)
    {
        var span = DateTime.UtcNow - utc;
        if (span.TotalSeconds < 45) return "ahora";
        if (span.TotalMinutes < 60) return $"{Math.Max(1, (int)span.TotalMinutes)} min";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours} h";
        return $"{(int)span.TotalDays} d";
    }

    /// <summary>Nombre corto de un modelo: claude-fable-5-1 → Fable 5.1.</summary>
    public static string ModelName(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return "?";
        var id = modelId.Trim();
        if (id.StartsWith("claude-", StringComparison.OrdinalIgnoreCase)) id = id[7..];
        var parts = id.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return modelId;

        var family = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(parts[0]);
        var version = parts.Skip(1).TakeWhile(p => p.Length <= 2 && p.All(char.IsDigit)).ToArray();
        return version.Length == 0 ? family : $"{family} {string.Join('.', version)}";
    }
}
