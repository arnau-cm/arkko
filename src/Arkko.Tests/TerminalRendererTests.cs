using Arkko.Core;

namespace Arkko.Tests;

public class TerminalRendererTests
{
    private static readonly TerminalData Data = new(
        "Opus 5.5", "claude-stats", null, 39, "medium",
        [
            new LimitInfo { Key = "five_hour", Label = "5 h", UsedPct = 22 },
            new LimitInfo { Key = "seven_day", Label = "7 días", UsedPct = 90 },
        ],
        5.25);

    private static string Plain(TerminalLayout layout) =>
        string.Concat(TerminalRenderer.Segments(layout, Data).Select(s => s.Text));

    private static TerminalLayout Layout(params string[] kinds) =>
        new() { Items = kinds.Select(k => new LayoutItem { Kind = k }).ToList() };

    [Fact]
    public void Pinta_las_piezas_en_el_orden_elegido()
    {
        var text = Plain(Layout(TerminalBlocks.Cost, TerminalBlocks.Separator, TerminalBlocks.Model, TerminalBlocks.Context));
        Assert.Equal("$5.25 | [Opus 5.5] ctx 39%", text);
    }

    [Fact]
    public void Omite_piezas_sin_dato_y_separadores_sobrantes()
    {
        var text = Plain(Layout(TerminalBlocks.Separator, TerminalBlocks.Session, TerminalBlocks.Separator,
            TerminalBlocks.Model, TerminalBlocks.Separator, TerminalBlocks.Separator, TerminalBlocks.Effort, TerminalBlocks.Separator));
        Assert.Equal("[Opus 5.5] | medium", text);
    }

    [Fact]
    public void La_barra_va_segun_el_uso_si_no_tiene_color()
    {
        var segments = TerminalRenderer.Segments(Layout(TerminalBlocks.SevenDay), Data);
        Assert.Contains(segments, s => s.Text.StartsWith('[') && s.Color == TerminalRenderer.UsageColor(90));
    }

    [Fact]
    public void El_worktree_sale_detras_del_repositorio_y_se_omite_fuera_de_un_worktree()
    {
        var layout = Layout(TerminalBlocks.Repo, TerminalBlocks.Worktree, TerminalBlocks.Separator, TerminalBlocks.Model);
        Assert.Equal("tienda develop | [Opus 5.5]",
            string.Concat(TerminalRenderer.Segments(layout, Data with { Repo = "tienda", Worktree = "develop" }).Select(s => s.Text)));
        Assert.Equal("claude-stats | [Opus 5.5]", Plain(layout));
    }

    [Fact]
    public void Una_distribucion_antigua_gana_el_worktree_detras_del_repositorio_una_sola_vez()
    {
        var old = Layout(TerminalBlocks.Model, TerminalBlocks.Repo, TerminalBlocks.Context).Upgraded();
        Assert.Equal([TerminalBlocks.Model, TerminalBlocks.Repo, TerminalBlocks.Worktree, TerminalBlocks.Context], old.Items.Select(i => i.Kind));

        // Guardada ya al día y sin la pieza, porque la persona la quitó: no vuelve.
        var removed = Layout(TerminalBlocks.Model, TerminalBlocks.Repo);
        removed.Revision = TerminalLayout.CurrentRevision;
        Assert.Equal([TerminalBlocks.Model, TerminalBlocks.Repo], removed.Upgraded().Items.Select(i => i.Kind));
    }

    [Fact]
    public void El_color_elegido_sale_en_ansi_de_24_bits()
    {
        var layout = new TerminalLayout { Items = [new LayoutItem { Kind = TerminalBlocks.Model, Color = "#FF8000" }] };
        var ansi = TerminalRenderer.Ansi(TerminalRenderer.Segments(layout, Data));
        Assert.Equal("\u001b[38;2;255;128;0m[Opus 5.5]\u001b[0m", ansi);
    }
}
