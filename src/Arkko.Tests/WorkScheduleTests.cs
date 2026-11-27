using Arkko.Core;

namespace Arkko.Tests;

public sealed class WorkScheduleTests
{
    // El 1 de octubre de 2026 es jueves; el 3, sábado.
    private static readonly DateTime Thursday = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Local);

    private static PromptRecord At(DateTime local, long output)
    {
        var usage = new TurnUsage();
        usage.ByModel["claude-opus-5-5"] = new ModelTokens { Output = output, ApiCalls = 1 };
        return new PromptRecord { WhenUtc = local.ToUniversalTime(), SessionId = "s", Project = "p", Usage = usage };
    }

    [Fact]
    public void Por_defecto_es_de_lunes_a_viernes_de_8_a_17()
    {
        var work = new WorkSchedule();
        Assert.Equal("L–V · 8:00–17:00", work.Describe());
        Assert.True(work.Contains(Thursday.AddHours(8)));
        Assert.True(work.Contains(Thursday.AddHours(16).AddMinutes(59)));
        Assert.False(work.Contains(Thursday.AddHours(17)));
        Assert.False(work.Contains(Thursday.AddHours(7).AddMinutes(59)));
        Assert.False(work.Contains(Thursday.AddDays(2).AddHours(10)));
    }

    [Fact]
    public void Los_dias_sueltos_se_listan_y_las_medias_horas_se_respetan()
    {
        var work = new WorkSchedule { Days = [DayOfWeek.Friday, DayOfWeek.Monday, DayOfWeek.Wednesday], StartMinutes = 9 * 60 + 30, EndMinutes = 14 * 60 + 30 };
        Assert.Equal("L, X, V · 9:30–14:30", work.Describe());
        Assert.Equal((9, 14), work.Hours);
        Assert.False(work.Contains(Thursday.AddHours(10)));
    }

    [Fact]
    public void Con_horario_hoy_solo_lleva_las_horas_de_la_franja_y_la_semana_solo_los_laborables()
    {
        var work = new WorkSchedule();
        var records = History.InSchedule(
        [
            At(Thursday.AddHours(9), 10),
            At(Thursday.AddHours(20), 99),
            At(Thursday.AddDays(2).AddHours(10), 99),
        ], work);
        Assert.Equal(10, Assert.Single(records).Usage.ByModel.Values.Single().Output);

        var (_, today) = History.Buckets(records, HistoryPeriod.Today, Thursday.AddHours(12), work);
        Assert.Equal(9, today.Count);
        Assert.Equal(8, today[0].Start.Hour);
        Assert.Equal(16, today[^1].Start.Hour);

        var (_, week) = History.Buckets(records, HistoryPeriod.Week, Thursday.AddDays(3).AddHours(12), work);
        Assert.Equal(5, week.Count);
        Assert.DoesNotContain(week, b => b.Start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
    }
}
