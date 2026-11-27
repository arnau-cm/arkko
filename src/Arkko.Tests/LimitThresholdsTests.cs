using Arkko.Core;

namespace Arkko.Tests;

public class LimitThresholdsTests
{
    private static readonly int[] Thresholds = [80, 95];
    private static readonly DateTime Reset = new(2026, 10, 1, 18, 0, 0, DateTimeKind.Utc);

    private static LimitInfo Limit(double pct, DateTime? reset = null) =>
        new() { Key = "seven_day", Label = "7 días", UsedPct = pct, ResetsAtUtc = reset ?? Reset };

    private static List<int> Crossed(LimitThresholds sut, double pct, DateTime? reset = null) =>
        sut.Check([Limit(pct, reset)], Thresholds).Select(c => c.Threshold).ToList();

    [Fact]
    public void No_avisa_de_lo_que_ya_estaba_pasado_al_abrir()
    {
        var sut = new LimitThresholds();
        Assert.Empty(Crossed(sut, 85));
        Assert.Empty(Crossed(sut, 88));
    }

    [Fact]
    public void Avisa_una_vez_de_cada_umbral()
    {
        var sut = new LimitThresholds();
        Crossed(sut, 50);
        Assert.Equal([80], Crossed(sut, 81));
        Assert.Empty(Crossed(sut, 90));
        Assert.Equal([95], Crossed(sut, 96));
        Assert.Empty(Crossed(sut, 99));
    }

    [Fact]
    public void Si_salta_varios_umbrales_avisa_solo_del_mas_alto()
    {
        var sut = new LimitThresholds();
        Crossed(sut, 50);
        Assert.Equal([95], Crossed(sut, 97));
    }

    [Fact]
    public void Vuelve_a_avisar_en_el_periodo_siguiente()
    {
        var sut = new LimitThresholds();
        Crossed(sut, 50);
        Assert.Equal([80], Crossed(sut, 82));
        var next = Reset.AddDays(7);
        Assert.Empty(Crossed(sut, 10, next));
        Assert.Equal([80], Crossed(sut, 83, next));
    }
}
