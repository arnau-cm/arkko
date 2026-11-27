namespace Arkko.Core;

/// <summary>Piezas que se pueden colocar en el resumen que sale en Claude Code al terminar cada prompt.</summary>
public static class SummaryBlocks
{
    public const string Duration = "duration";
    public const string Tokens = "tokens";
    public const string Breakdown = "breakdown";
    public const string Cache = "cache";
    public const string Calls = "calls";
    public const string Models = "models";
    public const string Limits = "limits";

    /// <summary>Todas las piezas, en el orden en que se ofrecen y en el del resumen por defecto.</summary>
    public static readonly string[] All = [Duration, Tokens, Breakdown, Cache, Calls, Models, Limits];

    /// <summary>Nombre de la pieza para el editor.</summary>
    public static string Label(string kind) => kind switch
    {
        Duration => "Duración",
        Tokens => "Tokens nuevos",
        Breakdown => "Desglose de tokens",
        Cache => "Lectura de caché",
        Calls => "Llamadas a la API",
        Models => "Por modelo",
        Limits => "Límites",
        _ => kind,
    };
}

/// <summary>Monta la línea de resumen de un prompt; la usan ak y la vista previa del editor.</summary>
/// <remarks>
/// La cifra principal son los tokens nuevos (entrada, salida y escritura en caché); la lectura de
/// caché va aparte porque se relee entera en cada llamada y cuesta en torno al 10 % de un token
/// normal: sumarla daría millones por un prompt corto con mucho contexto.
/// </remarks>
public static class PromptSummary
{
    private const string Joiner = " | ";

    /// <summary>Resumen con las piezas elegidas, en su orden; las que no tienen dato no salen.</summary>
    public static string Render(IEnumerable<string> pieces, TurnUsage usage, IReadOnlyList<LimitInfo> limits) =>
        string.Join(Joiner, pieces.Select(p => Piece(p, usage, limits)).Where(t => !string.IsNullOrEmpty(t)));

    /// <summary>Texto de una pieza, o null si no aplica a este prompt.</summary>
    private static string? Piece(string kind, TurnUsage usage, IReadOnlyList<LimitInfo> limits)
    {
        var t = usage.Totals();
        return kind switch
        {
            SummaryBlocks.Duration => Format.Duration(TimeSpan.FromMilliseconds(usage.DurationMs)),
            SummaryBlocks.Tokens => $"{Format.Tokens(NewTokens(t))} tokens",
            SummaryBlocks.Breakdown =>
                $"entrada {Format.Tokens(t.Input)} · salida {Format.Tokens(t.Output)} · escritura en caché {Format.Tokens(t.CacheWrite)}",
            SummaryBlocks.Cache => t.CacheRead > 0 ? $"+{Format.Tokens(t.CacheRead)} de caché" : null,
            SummaryBlocks.Calls => t.ApiCalls == 1 ? "1 llamada a la API" : $"{t.ApiCalls} llamadas a la API",
            SummaryBlocks.Models => usage.ByModel.Count > 1
                ? string.Join(", ", usage.ByModel
                    .OrderByDescending(m => NewTokens(m.Value))
                    .Select(m => $"{Format.ModelName(m.Key)} {Format.Tokens(NewTokens(m.Value))}"))
                : null,
            SummaryBlocks.Limits => limits.Count > 0
                ? string.Join(" · ", limits.Select(l => $"{l.Label} {(int)l.UsedPct}%"))
                : null,
            _ => null,
        };
    }

    /// <summary>Tokens de trabajo nuevo: entrada, salida y escritura en caché, sin la lectura de caché.</summary>
    public static long NewTokens(ModelTokens t) => t.Input + t.Output + t.CacheWrite;
}
