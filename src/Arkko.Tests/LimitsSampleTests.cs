using Arkko.Core;

namespace Arkko.Tests;

public class LimitsSampleTests
{
    private static readonly DateTime Reset = new(2026, 10, 1, 18, 0, 0, DateTimeKind.Utc);

    private static List<LimitInfo> Limits(double fiveHour, DateTime? reset = null) =>
    [
        new() { Key = "five_hour", Label = "5 h", UsedPct = fiveHour, ResetsAtUtc = reset ?? Reset },
        new() { Key = "seven_day", Label = "7 días", UsedPct = 28, ResetsAtUtc = Reset.AddDays(3) },
    ];

    [Fact]
    public void Sin_foto_anterior_cuenta_como_cambio() => Assert.True(LimitsSample.Changed(null, Limits(10)));

    [Fact]
    public void Un_decimal_que_baila_no_es_un_cambio() => Assert.False(LimitsSample.Changed(Limits(10.2), Limits(9.8)));

    [Fact]
    public void Cambia_al_subir_un_punto() => Assert.True(LimitsSample.Changed(Limits(10), Limits(11)));

    [Fact]
    public void Cambia_al_renovarse() => Assert.True(LimitsSample.Changed(Limits(10), Limits(10, Reset.AddHours(5))));

    [Fact]
    public void Cambia_si_aparece_un_limite_nuevo()
    {
        var now = Limits(10);
        now.Add(new LimitInfo { Key = "seven_day_fable", Label = "7 días Fable", UsedPct = 3 });
        Assert.True(LimitsSample.Changed(Limits(10), now));
    }
}
