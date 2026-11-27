namespace Arkko.Core;

/// <summary>Horario laboral: los días que se trabaja y la franja de cada uno, en hora local.</summary>
/// <remarks>
/// Siempre hay uno: sin tocar nada, de lunes a viernes de 8:00 a 17:00. Las horas van en minutos desde
/// medianoche y en pasos de media hora, que es lo que deja elegir la configuración.
/// </remarks>
public sealed class WorkSchedule
{
    public const int Step = 30;

    public List<DayOfWeek> Days { get; set; } =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    public int StartMinutes { get; set; } = 8 * 60;
    public int EndMinutes { get; set; } = 17 * 60;

    /// <summary>Indica si el día es laborable.</summary>
    public bool IsWorkDay(DateTime day) => Days.Contains(day.DayOfWeek);

    /// <summary>Indica si un instante local cae en día laborable y dentro de la franja.</summary>
    public bool Contains(DateTime local)
    {
        var minutes = local.Hour * 60 + local.Minute;
        return IsWorkDay(local) && minutes >= StartMinutes && minutes < EndMinutes;
    }

    /// <summary>Primera y última hora en punto que toca la franja, para el eje de la gráfica por horas.</summary>
    public (int First, int Last) Hours => (StartMinutes / 60, Math.Max(StartMinutes / 60, (EndMinutes + 59) / 60 - 1));

    /// <summary>Horario en una línea: «L–V · 8:00–17:00» o «L, X, V · 9:00–14:30».</summary>
    public string Describe()
    {
        var days = Order.Where(Days.Contains).ToList();
        var indexes = days.Select(d => Array.IndexOf(Order, d)).ToList();
        var consecutive = indexes.Count > 2 && indexes[^1] - indexes[0] == indexes.Count - 1;
        var text = days.Count == 0 ? "Ningún día"
            : consecutive ? $"{Letter(days[0])}–{Letter(days[^1])}"
            : string.Join(", ", days.Select(Letter));
        return $"{text} · {Time(StartMinutes)}–{Time(EndMinutes)}";
    }

    /// <summary>Días de la semana empezando por el lunes, como se leen en castellano.</summary>
    public static readonly DayOfWeek[] Order =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    /// <summary>Inicial del día en castellano: L, M, X, J, V, S, D.</summary>
    public static string Letter(DayOfWeek day) => "LMXJVSD"[Array.IndexOf(Order, day)].ToString();

    /// <summary>Minutos desde medianoche como hora: «8:00», «17:30», «24:00».</summary>
    public static string Time(int minutes) => $"{minutes / 60}:{minutes % 60:00}";
}
