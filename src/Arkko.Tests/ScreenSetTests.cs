using Arkko.Core;

namespace Arkko.Tests;

public sealed class ScreenSetTests
{
    private static readonly ScreenArea Laptop = new(0, 0, 1920, 1200, 1.25);
    private static readonly ScreenArea Left = new(-2560, 0, 2560, 1440, 1);
    private static readonly ScreenArea Right = new(1920, -200, 2560, 1440, 1);

    [Fact]
    public void La_huella_no_depende_del_orden_de_las_pantallas() =>
        Assert.Equal(ScreenSet.Key([Laptop, Left, Right]), ScreenSet.Key([Right, Laptop, Left]));

    [Fact]
    public void Tres_pantallas_y_solo_el_portatil_son_juegos_distintos() =>
        Assert.NotEqual(ScreenSet.Key([Laptop, Left, Right]), ScreenSet.Key([Laptop]));

    [Fact]
    public void Cambiar_la_escala_cambia_la_huella() =>
        Assert.NotEqual(ScreenSet.Key([Laptop]), ScreenSet.Key([Laptop with { Scale = 1.5 }]));

    [Fact]
    public void La_huella_no_depende_del_idioma_del_sistema()
    {
        var before = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("es-ES");
            Assert.Equal("1920x1200@0,0*1.25", ScreenSet.Key([Laptop]));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = before;
        }
    }

    [Theory]
    [InlineData(100, 100, true)]
    [InlineData(-2000, 300, false)]
    [InlineData(1900, 100, false)]
    public void Se_puede_agarrar_solo_si_la_barra_cae_en_una_pantalla(int x, int y, bool reachable) =>
        Assert.Equal(reachable, ScreenSet.Reachable([Laptop], x, y));

    [Fact]
    public void Encaja_una_ventana_que_se_sale_de_la_pantalla()
    {
        var (x, y, width, height) = ScreenSet.Fit(Laptop, 1500, 900, 1000, 800);
        Assert.Equal((920, 400, 1000, 800), (x, y, width, height));
    }

    [Fact]
    public void Encoge_una_ventana_mas_grande_que_la_pantalla()
    {
        var (x, y, width, height) = ScreenSet.Fit(Laptop, -500, -100, 2560, 1440);
        Assert.Equal((0, 0, 1920, 1200), (x, y, width, height));
    }
}
