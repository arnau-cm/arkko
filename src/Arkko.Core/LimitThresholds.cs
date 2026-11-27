namespace Arkko.Core;

/// <summary>Un límite que acaba de pasar uno de los umbrales de aviso.</summary>
public sealed record LimitCrossing(LimitInfo Limit, int Threshold);

/// <summary>Detecta cuándo un límite de uso pasa un umbral de aviso, una sola vez por periodo.</summary>
/// <remarks>
/// Cada periodo de un límite se identifica por su clave y su hora de renovación: al renovarse, la
/// hora cambia y los umbrales vuelven a avisar. La primera pasada solo toma nota de lo que ya estaba
/// pasado al abrir la app, para no avisar de golpe de algo que ya se ve en la ventana.
/// </remarks>
public sealed class LimitThresholds
{
    private readonly Dictionary<string, int> _crossed = new();
    private bool _primed;

    /// <summary>Devuelve los límites que han pasado un umbral nuevo desde la última llamada.</summary>
    /// <remarks>Si un límite salta varios umbrales a la vez, solo se avisa del más alto.</remarks>
    public List<LimitCrossing> Check(IEnumerable<LimitInfo> limits, IReadOnlyCollection<int> thresholds)
    {
        var crossings = new List<LimitCrossing>();
        foreach (var limit in limits)
        {
            var reached = thresholds.Where(t => t > 0 && limit.UsedPct >= t).DefaultIfEmpty(0).Max();
            if (reached == 0) continue;
            var period = PeriodKey(limit);
            if (_crossed.TryGetValue(period, out var before) && before >= reached) continue;
            _crossed[period] = reached;
            if (_primed) crossings.Add(new LimitCrossing(limit, reached));
        }
        _primed = true;
        return crossings;
    }

    /// <summary>Clave del periodo en curso de un límite: su clave y la renovación, al minuto.</summary>
    private static string PeriodKey(LimitInfo limit) =>
        $"{limit.Key}|{limit.ResetsAtUtc?.ToUniversalTime().ToString("yyyyMMddHHmm") ?? "-"}";
}
