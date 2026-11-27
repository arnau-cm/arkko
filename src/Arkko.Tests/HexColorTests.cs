using Arkko.Core;

namespace Arkko.Tests;

public sealed class HexColorTests
{
    [Theory]
    [InlineData("#3fb950", true)]
    [InlineData("#3FB950", true)]
    [InlineData("3fb950", false)]
    [InlineData("#3fb95", false)]
    [InlineData("#3fb95g", false)]
    [InlineData(null, false)]
    public void Reconoce_solo_codigos_de_seis_cifras(string? text, bool valid) => Assert.Equal(valid, HexColor.IsValid(text));

    [Theory]
    [InlineData("#ff0000", 0, 1, 1)]
    [InlineData("#00ff00", 120, 1, 1)]
    [InlineData("#0000ff", 240, 1, 1)]
    [InlineData("#ffffff", 0, 0, 1)]
    [InlineData("#000000", 0, 0, 0)]
    [InlineData("#808080", 0, 0, 0.502)]
    public void Pasa_a_tono_saturacion_y_brillo(string hex, double h, double s, double v)
    {
        var hsv = HexColor.ToHsv(hex)!.Value;
        Assert.Equal(h, hsv.H, 1);
        Assert.Equal(s, hsv.S, 3);
        Assert.Equal(v, hsv.V, 3);
    }

    [Theory]
    [InlineData("#e5534b")]
    [InlineData("#f0883e")]
    [InlineData("#d4a72c")]
    [InlineData("#3fb950")]
    [InlineData("#4493f8")]
    [InlineData("#a371f7")]
    [InlineData("#ff00ff")]
    [InlineData("#123456")]
    public void Ida_y_vuelta_da_el_mismo_codigo(string hex)
    {
        var (h, s, v) = HexColor.ToHsv(hex)!.Value;
        Assert.Equal(hex, HexColor.FromHsv(h, s, v));
    }

    [Fact]
    public void Recorta_lo_que_se_sale_de_rango() =>
        Assert.Equal("#ff0000", HexColor.FromHsv(360, 2, 1.5));

    [Fact]
    public void Un_codigo_invalido_no_da_color() => Assert.Null(HexColor.ToHsv("rojo"));

    private static string Readable(string hex, bool dark)
    {
        var (r, g, b) = HexColor.Readable(HexColor.ToRgb(hex)!.Value, dark);
        return HexColor.FromRgb(r, g, b);
    }

    /// <summary>Luminancia relativa de un color, la de las pautas de contraste: 0 el negro, 1 el blanco.</summary>
    private static double Luminance(string hex)
    {
        var (r, g, b) = HexColor.ToRgb(hex)!.Value;
        static double Linear(byte c) => c / 255.0 is var v && v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        return 0.2126 * Linear(r) + 0.7152 * Linear(g) + 0.0722 * Linear(b);
    }

    [Theory]
    [InlineData("#e5534b")]
    [InlineData("#f0883e")]
    [InlineData("#d4a72c")]
    [InlineData("#3fb950")]
    [InlineData("#4493f8")]
    [InlineData("#a371f7")]
    public void En_tema_oscuro_la_paleta_de_grupos_no_cambia(string hex) => Assert.Equal(hex, Readable(hex, dark: true));

    [Theory]
    [InlineData("#1f3a5f", true)]
    [InlineData("#5a1010", true)]
    [InlineData("#0000ff", true)]
    [InlineData("#d4a72c", false)]
    [InlineData("#f2e85c", false)]
    public void Un_color_que_no_se_lee_cambia_de_luz_y_conserva_el_tono(string hex, bool dark)
    {
        var before = HexColor.ToHsv(hex)!.Value;
        var after = HexColor.ToHsv(Readable(hex, dark))!.Value;

        Assert.True(dark ? Luminance(Readable(hex, dark)) > Luminance(hex) : Luminance(Readable(hex, dark)) < Luminance(hex), $"{hex} → {Readable(hex, dark)}");
        // El tono se conserva en OKLCH; medido en HSV se mueve unos grados, más en los azules puros.
        var turn = Math.Abs(after.H - before.H);
        Assert.True(Math.Min(turn, 360 - turn) < 20, $"{hex} → {Readable(hex, dark)}: el tono se ha ido de {before.H:0} a {after.H:0}");
    }

    [Theory]
    [InlineData("#1f3a5f", true)]
    [InlineData("#0000ff", true)]
    [InlineData("#f2e85c", false)]
    public void Ajustar_dos_veces_da_lo_mismo_que_una(string hex, bool dark)
    {
        var once = Readable(hex, dark);
        var twice = Readable(once, dark);
        foreach (var (a, b) in new[] { (once[1..3], twice[1..3]), (once[3..5], twice[3..5]), (once[5..], twice[5..]) })
            Assert.InRange(Math.Abs(Convert.ToInt32(a, 16) - Convert.ToInt32(b, 16)), 0, 2);
    }

    [Theory]
    [InlineData("#f09a76")]
    [InlineData("#e5c07b")]
    [InlineData("#98c379")]
    [InlineData("#61afef")]
    [InlineData("#f2e85c")]
    public void Bajo_un_texto_blanco_un_color_claro_se_oscurece_hasta_que_el_blanco_se_lee(string hex)
    {
        var (r, g, b) = HexColor.UnderWhite(HexColor.ToRgb(hex)!.Value);
        var band = HexColor.FromRgb(r, g, b);

        Assert.True((1 + 0.05) / (Luminance(band) + 0.05) >= 3.5, $"{hex} → {band}");
        var turn = Math.Abs(HexColor.ToHsv(band)!.Value.H - HexColor.ToHsv(hex)!.Value.H);
        Assert.True(Math.Min(turn, 360 - turn) < 20, $"{hex} → {band}");
    }

    [Theory]
    [InlineData("#1f3a5f")]
    [InlineData("#86050d")]
    public void Bajo_un_texto_blanco_un_color_oscuro_no_cambia(string hex)
    {
        var (r, g, b) = HexColor.UnderWhite(HexColor.ToRgb(hex)!.Value);
        Assert.Equal(hex, HexColor.FromRgb(r, g, b));
    }

    [Fact]
    public void Un_color_oscuro_no_cambia_en_tema_claro_ni_uno_claro_en_tema_oscuro()
    {
        Assert.Equal("#1f3a5f", Readable("#1f3a5f", dark: false));
        Assert.Equal("#f2e85c", Readable("#f2e85c", dark: true));
    }
}
