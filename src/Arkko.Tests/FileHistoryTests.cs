using System.Text.Json;
using Arkko.Core;

namespace Arkko.Tests;

/// <remarks>Cada prueba monta a mano los pasos de una sesión que trabaja en C:/repo, con las entradas como las guarda el
/// transcript.</remarks>
public sealed class FileHistoryTests
{
    private const string Cwd = "C:/repo";
    private static readonly DateTime At = new(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc);
    private int _order;

    private TraceStep Tool(string title, object input, int interaction = 1, string? agent = null, bool error = false) => new()
    {
        Order = _order++,
        Kind = TraceKind.Tool,
        Title = title,
        Text = JsonSerializer.Serialize(input),
        Interaction = interaction,
        AgentId = agent,
        IsError = error,
        StartUtc = At.AddMinutes(_order),
    };

    private TraceStep Reply(string text, int interaction = 1) => new() { Order = _order++, Kind = TraceKind.Reply, Text = text, Interaction = interaction };

    private static SessionTrace Trace(params TraceStep[] steps) => new() { SessionId = "s", Cwd = Cwd, Steps = [.. steps] };

    [Fact]
    public void Una_edicion_cuenta_las_lineas_que_cambia_y_casa_la_ruta_con_cualquier_barra()
    {
        var trace = Trace(
            Tool("Edit · LiveSessions.cs", new { file_path = @"C:\repo\src\LiveSessions.cs", old_string = "a\nb\nc", new_string = "a\nB\nb2\nc" }),
            Tool("Edit · Otro.cs", new { file_path = "C:/repo/src/Otro.cs", old_string = "x", new_string = "y" }));

        var touches = FileHistory.Of(trace, "src/LiveSessions.cs");

        var t = Assert.Single(touches);
        Assert.Equal(("Edita", 2, 1), (t.Verb, t.Added, t.Removed));
        Assert.Equal("B", t.Who);
    }

    [Fact]
    public void Escribir_el_fichero_entero_cuenta_todas_sus_lineas()
    {
        var trace = Trace(Tool("Write · a.md", new { file_path = "C:/repo/a.md", content = "uno\ndos\ntres\n" }));

        var t = Assert.Single(FileHistory.Of(trace, "C:/repo/a.md"));

        Assert.Equal(("Escribe", 3, 0), (t.Verb, t.Added, t.Removed));
    }

    [Fact]
    public void Una_edicion_que_fallo_no_cuenta()
    {
        var trace = Trace(Tool("Edit · a.cs", new { file_path = "C:/repo/a.cs", old_string = "x", new_string = "y" }, error: true));

        Assert.Empty(FileHistory.Of(trace, "a.cs"));
    }

    [Fact]
    public void Un_comando_cuenta_si_nombra_el_fichero_y_escribe_en_el()
    {
        var trace = Trace(
            Tool("Bash · sed -i", new { command = "sed -i 's/a/b/' src/a.cs" }),
            Tool("Bash · grep", new { command = "grep -n Foo src/a.cs" }));

        var t = Assert.Single(FileHistory.Of(trace, "src/a.cs"));

        Assert.Equal(("Comando", 0, 0), (t.Verb, t.Added, t.Removed));
    }

    [Fact]
    public void Un_agente_va_con_su_letra_y_se_apunta_lo_que_dijo_despues_la_sesion()
    {
        var launch = new TraceStep { Order = _order++, Kind = TraceKind.Agent, Title = "Explore · busca", ChildAgentId = "a1", Interaction = 1 };
        var trace = Trace(
            launch,
            Tool("Edit · a.cs", new { file_path = "C:/repo/a.cs", old_string = "x", new_string = "y" }, agent: "a1"),
            Reply("Lo ha cambiado el agente.\nDetalle", 1),
            Reply("Otra cosa", 2));

        var t = Assert.Single(FileHistory.Of(trace, "a.cs"));

        Assert.Equal("C", t.Who);
        Assert.Equal("Lo ha cambiado el agente.", t.Reply);
    }

    [Fact]
    public void Una_entrada_recortada_aun_dice_el_fichero_aunque_no_las_lineas()
    {
        var step = Tool("Write · grande.txt", new { file_path = "C:/repo/grande.txt", content = "x" });
        step.Text = "{\n  \"file_path\": \"C:\\\\repo\\\\grande.txt\",\n  \"content\": \"muy largo…";
        var trace = Trace(step);

        var t = Assert.Single(FileHistory.Of(trace, "grande.txt"));

        Assert.Equal(("Escribe", 0, 0), (t.Verb, t.Added, t.Removed));
    }

    [Fact]
    public void El_recuento_de_lineas_no_cuenta_lo_que_se_queda_igual()
    {
        Assert.Equal((1, 1), FileHistory.LineDiff("a\nb\nc", "a\nX\nc"));
        Assert.Equal((2, 0), FileHistory.LineDiff("a", "a\nb\nc"));
        Assert.Equal((0, 0), FileHistory.LineDiff("", ""));
    }
}
