using Arkko.Core;

namespace Arkko.Tests;

public class LimitForecastTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private static LimitInfo FiveHour(double pct, TimeSpan left) =>
        new() { Key = "five_hour", Label = "5 h", UsedPct = pct, ResetsAtUtc = Now + left };

    [Fact]
    public void Calcula_cuando_llega_al_100_si_es_antes_de_renovarse()
    {
        // 2 h transcurridas al 60 %: 30 % por hora, faltan 40 puntos → 1 h 20 min.
        var p = LimitForecast.Project(FiveHour(60, TimeSpan.FromHours(3)), Now);
        Assert.NotNull(p);
        Assert.Equal(80, p.ToFull!.Value.TotalMinutes, 3);
        Assert.Equal(30, p.PerHour, 3);
    }

    [Fact]
    public void Si_no_llega_da_el_porcentaje_al_renovarse()
    {
        // 4 h transcurridas al 40 %: 10 % por hora, queda 1 h → 50 %.
        var p = LimitForecast.Project(FiveHour(40, TimeSpan.FromHours(1)), Now);
        Assert.NotNull(p);
        Assert.Null(p.ToFull);
        Assert.Equal(50, p.AtReset, 3);
    }

    [Fact]
    public void Sin_base_suficiente_no_da_prevision()
    {
        Assert.Null(LimitForecast.Project(FiveHour(5, TimeSpan.FromHours(4.9)), Now));
        Assert.Null(LimitForecast.Project(FiveHour(0, TimeSpan.FromHours(2)), Now));
        Assert.Null(LimitForecast.Project(FiveHour(100, TimeSpan.FromHours(2)), Now));
        Assert.Null(LimitForecast.Project(new LimitInfo { Key = "otro", UsedPct = 50, ResetsAtUtc = Now.AddHours(1) }, Now));
    }

    [Fact]
    public void Sirve_para_el_semanal()
    {
        // 3,5 días al 25 %: al renovarse, 50 %.
        var p = LimitForecast.Project(new LimitInfo { Key = "seven_day", UsedPct = 25, ResetsAtUtc = Now.AddDays(3.5) }, Now);
        Assert.NotNull(p);
        Assert.Equal(50, p.AtReset, 3);
    }
}
