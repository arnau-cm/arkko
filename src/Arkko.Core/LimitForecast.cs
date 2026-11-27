namespace Arkko.Core;

/// <summary>Previsión de un límite al ritmo actual: cuándo llega al 100 %, o cuánto marcará al renovarse.</summary>
/// <param name="ToFull">Tiempo hasta el 100 %, si llega antes de renovarse; null si no llega.</param>
/// <param name="AtReset">Porcentaje previsto al renovarse, cuando no llega al 100 %.</param>
/// <param name="PerHour">El ritmo medio del periodo en curso, en puntos por hora.</param>
public sealed record LimitProjection(TimeSpan? ToFull, double AtReset, double PerHour);

/// <summary>Calcula la previsión de los límites con el ritmo medio del periodo en curso.</summary>
/// <remarks>
/// El periodo empieza a la hora de renovación menos su duración, así que el ritmo se conoce sin
/// guardar historial: uso actual entre tiempo transcurrido. Al principio del periodo hay poco dato
/// y la previsión no es fiable, por eso no se da hasta pasado un mínimo.
/// </remarks>
public static class LimitForecast
{
    private static readonly TimeSpan MinElapsed = TimeSpan.FromMinutes(10);

    /// <summary>Previsión del límite en el instante dado, o null si no hay base para darla.</summary>
    public static LimitProjection? Project(LimitInfo limit, DateTime nowUtc)
    {
        if (limit.ResetsAtUtc is not { } reset || Period(limit.Key) is not { } period) return null;
        reset = reset.ToUniversalTime();
        var left = reset - nowUtc;
        var elapsed = period - left;
        if (left <= TimeSpan.Zero || elapsed < MinElapsed || elapsed > period) return null;
        if (limit.UsedPct < 1 || limit.UsedPct >= 100) return null;

        var perHour = limit.UsedPct / elapsed.TotalHours;
        var toFull = TimeSpan.FromHours((100 - limit.UsedPct) / perHour);
        return toFull < left
            ? new LimitProjection(toFull, 100, perHour)
            : new LimitProjection(null, limit.UsedPct + perHour * left.TotalHours, perHour);
    }

    /// <summary>Devuelve la duración del periodo de un límite según su clave; null si no se conoce.</summary>
    public static TimeSpan? Period(string key)
    {
        if (key.StartsWith("five_hour", StringComparison.OrdinalIgnoreCase)) return TimeSpan.FromHours(5);
        if (key.StartsWith("seven_day", StringComparison.OrdinalIgnoreCase)) return TimeSpan.FromDays(7);
        return null;
    }
}
