using Arkko.Core;

namespace Arkko.Tests;

public class PromptSummaryTests
{
    private static readonly LimitInfo[] Limits =
    [
        new() { Key = "five_hour", Label = "5 h", UsedPct = 10 },
        new() { Key = "seven_day", Label = "7 días", UsedPct = 28 },
    ];

    private static TurnUsage Usage(params (string Model, long Output, long CacheRead)[] models)
    {
        var usage = new TurnUsage { DurationMs = 153_000 };
        foreach (var (model, output, cacheRead) in models)
            usage.ByModel[model] = new ModelTokens { Input = 20, Output = output, CacheWrite = 1_000, CacheRead = cacheRead, ApiCalls = 3 };
        return usage;
    }

    [Fact]
    public void Pinta_las_piezas_en_el_orden_elegido()
    {
        var text = PromptSummary.Render(
            [SummaryBlocks.Limits, SummaryBlocks.Duration, SummaryBlocks.Tokens],
            Usage(("claude-opus-5-5", 5_000, 0)), Limits);
        Assert.Equal("5 h 10% · 7 días 28% | 2m33s | 6,0k tokens", text);
    }

    [Fact]
    public void Separa_la_lectura_de_cache_del_trabajo_nuevo()
    {
        var text = PromptSummary.Render([SummaryBlocks.Tokens, SummaryBlocks.Cache], Usage(("claude-opus-5-5", 5_000, 1_200_000)), Limits);
        Assert.Equal("6,0k tokens | +1,2M de caché", text);
    }

    [Fact]
    public void Omite_las_piezas_sin_dato()
    {
        // Un solo modelo, sin lectura de caché y sin límites: «Por modelo», «Lectura de caché» y «Límites» no salen.
        var text = PromptSummary.Render([SummaryBlocks.Models, SummaryBlocks.Cache, SummaryBlocks.Calls, SummaryBlocks.Limits],
            Usage(("claude-opus-5-5", 5_000, 0)), []);
        Assert.Equal("3 llamadas a la API", text);
    }

    [Fact]
    public void Desglosa_por_modelo_cuando_hay_varios()
    {
        var text = PromptSummary.Render([SummaryBlocks.Models], Usage(("claude-sonnet-5-5", 2_000, 0), ("claude-opus-5-5", 9_000, 0)), []);
        Assert.Equal("Opus 5.5 10,0k, Sonnet 5.5 3,0k", text);
    }

    [Fact]
    public void Sin_el_campo_en_el_fichero_salen_todas_las_piezas()
    {
        Assert.Equal(SummaryBlocks.All, new TerminalLayout().SummaryItems);
    }
}
