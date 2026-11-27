using Arkko.Core;

namespace Arkko.Tests;

/// <remarks>Cada prueba monta a mano lo leído de una sesión, con la hora en minutos antes de las 10:49 del 10 de
/// noviembre, y mira el reloj de su última hora.</remarks>
public sealed class SessionClockTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 10, 49, 0, DateTimeKind.Utc);
    private int _order;

    /// <summary>El instante de hace tantos minutos, en milisegundos como los da el reloj.</summary>
    private static long Ago(double minutes) => new DateTimeOffset(Now.AddMinutes(-minutes)).ToUnixTimeMilliseconds();

    /// <summary>Un paso que empieza hace <paramref name="from"/> minutos y, si se da, acaba hace <paramref name="to"/>.</summary>
    private TraceStep Step(TraceKind kind, double from, double? to = null, string title = "", string? agent = null, string? child = null) => new()
    {
        Order = _order++,
        Kind = kind,
        Title = title,
        AgentId = agent,
        ChildAgentId = child,
        StartUtc = Now.AddMinutes(-from),
        EndUtc = to is { } t ? Now.AddMinutes(-t) : null,
    };

    private static SessionTrace Trace(params TraceStep[] steps) => new() { SessionId = "s", Steps = [.. steps] };

    [Fact]
    public void Una_herramienta_de_antes_de_la_hora_se_recorta_al_principio_de_la_ventana()
    {
        var face = SessionClock.Build(Trace(Step(TraceKind.Tool, 70, 50, "Bash · npm test")), Now, working: false);

        var span = Assert.Single(face.Rings[0].Spans);
        Assert.Equal(new ClockSpan(Ago(60), Ago(50), "run"), span);
        Assert.Equal(Ago(60), face.From);
        Assert.Equal(Ago(0), face.To);
    }

    [Theory]
    [InlineData("Read", "read")]
    [InlineData("Grep", "search")]
    [InlineData("Glob", "search")]
    [InlineData("WebFetch", "search")]
    [InlineData("Edit", "edit")]
    [InlineData("Write", "edit")]
    [InlineData("Bash", "run")]
    [InlineData("PowerShell", "run")]
    [InlineData("AskUserQuestion", "ask")]
    [InlineData("mcp__playwright__browser_click", "other")]
    [InlineData("TodoWrite", "other")]
    public void Cada_herramienta_lleva_el_color_de_lo_que_hace(string tool, string hue) => Assert.Equal(hue, SessionClock.Hue(tool));

    [Fact]
    public void El_hueco_corto_entre_dos_herramientas_es_Claude_pensando()
    {
        var face = SessionClock.Build(Trace(
            Step(TraceKind.Tool, 20, 19, "Read · a.cs"),
            Step(TraceKind.Tool, 17, 16, "Edit · a.cs")), Now, working: false);

        Assert.Equal(
            [new ClockSpan(Ago(20), Ago(19), "read"), new ClockSpan(Ago(19), Ago(17), "think"), new ClockSpan(Ago(17), Ago(16), "edit")],
            face.Rings[0].Spans);
    }

    [Fact]
    public void Un_hueco_largo_o_que_acaba_en_un_prompt_es_la_sesion_parada()
    {
        var face = SessionClock.Build(Trace(
            Step(TraceKind.Tool, 40, 39, "Read · a.cs"),
            Step(TraceKind.Tool, 30, 29, "Read · b.cs"),
            Step(TraceKind.Prompt, 28),
            Step(TraceKind.Tool, 27, 26, "Read · c.cs")), Now, working: false);

        Assert.DoesNotContain(face.Rings[0].Spans, s => s.Hue == "think" && s.End <= Ago(28));
        Assert.Contains(new ClockSpan(Ago(28), Ago(27), "think"), face.Rings[0].Spans);
        Assert.Equal([Ago(28)], face.Prompts);
    }

    [Fact]
    public void Lo_que_no_ha_terminado_llega_hasta_ahora_si_la_sesion_trabaja()
    {
        var steps = new[] { Step(TraceKind.Tool, 3, title: "Bash · npm test") };

        var working = SessionClock.Build(Trace(steps), Now, working: true);
        var stopped = SessionClock.Build(Trace(steps), Now, working: false);

        Assert.Equal(new ClockSpan(Ago(3), Ago(0), "run"), Assert.Single(working.Rings[0].Spans));
        Assert.Equal(new ClockSpan(Ago(3), Ago(3), "run"), Assert.Single(stopped.Rings[0].Spans));
    }

    [Fact]
    public void La_sesion_espera_a_rayas_a_su_agente_y_el_agente_va_en_su_anillo_con_su_letra()
    {
        var face = SessionClock.Build(Trace(
            Step(TraceKind.Prompt, 30),
            Step(TraceKind.Agent, 29, 10, "Agent · Apply", child: "a1"),
            Step(TraceKind.Tool, 28, 20, "Edit · a.cs", agent: "a1"),
            Step(TraceKind.Tool, 19, 11, "Bash · npm test", agent: "a1")), Now, working: false);

        Assert.Equal(2, face.Rings.Count);
        Assert.Contains(new ClockSpan(Ago(29), Ago(10), "wait"), face.Rings[0].Spans);
        Assert.DoesNotContain(face.Rings[0].Spans, s => s.Hue == "think" && s.Start >= Ago(29));
        Assert.Equal(new ClockTag("C", Ago(28)), Assert.Single(face.Rings[1].Tags));
        Assert.Contains(new ClockSpan(Ago(28), Ago(20), "edit"), face.Rings[1].Spans);
    }

    [Fact]
    public void Los_agentes_que_no_coinciden_comparten_anillo_y_los_que_coinciden_no()
    {
        var face = SessionClock.Build(Trace(
            Step(TraceKind.Agent, 50, 40, "Agent · uno", child: "a1"),
            Step(TraceKind.Agent, 50, 30, "Agent · dos", child: "a2"),
            Step(TraceKind.Agent, 35, 20, "Agent · tres", child: "a3"),
            Step(TraceKind.Tool, 49, 41, "Read · a.cs", agent: "a1"),
            Step(TraceKind.Tool, 49, 31, "Read · b.cs", agent: "a2"),
            Step(TraceKind.Tool, 34, 21, "Read · c.cs", agent: "a3")), Now, working: false);

        Assert.Equal(3, face.Rings.Count);
        Assert.Equal(["C", "E"], face.Rings[1].Tags.Select(t => t.Letter));
        Assert.Equal(["D"], face.Rings[2].Tags.Select(t => t.Letter));
    }

    [Fact]
    public void Un_agente_que_trabaja_hace_esperar_hasta_ahora()
    {
        var face = SessionClock.Build(Trace(
            Step(TraceKind.Agent, 20, null, "Agent · Apply", child: "a1"),
            Step(TraceKind.Tool, 19, 2, "Edit · a.cs", agent: "a1")), Now, working: true);

        Assert.Contains(new ClockSpan(Ago(20), Ago(0), "wait"), face.Rings[0].Spans);
        Assert.Contains(new ClockSpan(Ago(2), Ago(0), "think"), face.Rings[1].Spans);
    }

    [Fact]
    public void Los_mensajes_son_marcas_y_lo_ultimo_dice_que_herramienta_y_de_quien()
    {
        var face = SessionClock.Build(Trace(
            Step(TraceKind.Reply, 10),
            Step(TraceKind.Agent, 9, 1, "Agent · Apply", child: "a1"),
            Step(TraceKind.Tool, 8, 2, "Edit · sesiones.js", agent: "a1")), Now, working: false);

        Assert.Contains(new ClockSpan(Ago(10), Ago(10), "say"), face.Rings[0].Spans);
        Assert.Equal("Edit · sesiones.js", face.Doing);
        Assert.Equal("C", face.DoingLetter);
        Assert.Equal(Ago(1), face.Last);
    }

    [Fact]
    public void Una_sesion_sin_nada_en_la_ultima_hora_tiene_solo_su_anillo_vacio()
    {
        var face = SessionClock.Build(Trace(Step(TraceKind.Tool, 120, 110, "Read · a.cs")), Now, working: false);

        Assert.Empty(Assert.Single(face.Rings).Spans);
        Assert.Empty(face.Prompts);
        Assert.Null(face.Doing);
        Assert.Equal(Ago(110), face.Last);
    }
}
