using Arkko.Core;

namespace Arkko.Tests;

/// <remarks>Cada prueba monta a mano el estado de una sesión y lo leído de ella, con la hora en minutos antes de las
/// 16:40 del 11 de noviembre.</remarks>
public sealed class SessionLiveTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 16, 40, 0, DateTimeKind.Utc);
    private int _order;

    private TraceStep Step(TraceKind kind, double from, double? to = null, string title = "", string? text = null, string? agent = null) => new()
    {
        Order = _order++,
        Kind = kind,
        Title = title,
        Text = text,
        AgentId = agent,
        StartUtc = Now.AddMinutes(-from),
        EndUtc = to is { } t ? Now.AddMinutes(-t) : null,
    };

    private static SessionTrace Trace(params TraceStep[] steps) => new() { SessionId = "s", Steps = [.. steps] };

    private static SessionState State(SessionStatus status, string? detail = null, double since = 3) => new()
    {
        SessionId = "s",
        Status = status,
        Detail = detail,
        StatusSinceUtc = Now.AddMinutes(-since),
    };

    [Fact]
    public void Una_pregunta_sin_respuesta_trae_su_texto_sus_opciones_y_desde_cuando()
    {
        var ask = Step(TraceKind.Tool, 6, title: "AskUserQuestion");
        ask.Questions =
        [
            new TraceQuestion { Question = "¿0.11.1.1 o 0.12.0?", Header = "Versión", Options = [new() { Label = "0.11.1.1" }, new() { Label = "0.12.0" }] },
            new TraceQuestion { Question = "¿Etiqueta?" },
        ];
        var p = SessionLive.Pending(State(SessionStatus.Waiting, "Te hace una pregunta", since: 5), Trace(Step(TraceKind.Tool, 8, 7, "Read · a.cs"), ask), false, null);

        Assert.NotNull(p);
        Assert.Equal("question", p.Kind);
        Assert.Equal("¿0.11.1.1 o 0.12.0?", p.Text);
        Assert.Equal("Versión", p.Header);
        Assert.Equal(["0.11.1.1", "0.12.0"], p.Options);
        Assert.Equal(1, p.More);
        Assert.Equal(Now.AddMinutes(-6), p.SinceUtc);
    }

    [Fact]
    public void Una_pregunta_ya_contestada_no_cuenta_aunque_la_sesion_siga_esperando()
    {
        var ask = Step(TraceKind.Tool, 6, 5, "AskUserQuestion");
        ask.Questions = [new TraceQuestion { Question = "¿Sigo?" }];
        var p = SessionLive.Pending(State(SessionStatus.Waiting, "Te hace una pregunta"), Trace(ask), false, null);

        Assert.NotNull(p);
        Assert.Equal("Te hace una pregunta", p.Text);
        Assert.Empty(p.Options);
    }

    [Fact]
    public void Un_permiso_dice_la_herramienta_que_espera()
    {
        var trace = Trace(Step(TraceKind.Tool, 4, 3, "Edit · MesaView.vue"), Step(TraceKind.Tool, 2, title: "Bash · npm run build"));
        var p = SessionLive.Pending(State(SessionStatus.Waiting, "Pide permiso: Claude needs your permission to use Bash", since: 2), trace, false, null);

        Assert.NotNull(p);
        Assert.Equal("permission", p.Kind);
        Assert.Equal("Quiere usar Bash · npm run build", p.Text);
        Assert.Equal(Now.AddMinutes(-2), p.SinceUtc);
    }

    [Fact]
    public void Un_permiso_sin_lo_leido_deja_lo_que_dice_el_hook()
    {
        var p = SessionLive.Pending(State(SessionStatus.Waiting, "Pide permiso: Claude needs your permission to use Bash"), null, false, null);

        Assert.NotNull(p);
        Assert.Equal("permission", p.Kind);
        Assert.Equal("Quiere usar Bash", p.Text);
    }

    [Fact]
    public void Un_permiso_con_otro_mensaje_lo_deja_sin_el_prefijo()
    {
        var other = SessionLive.Pending(State(SessionStatus.Waiting, "Pide permiso: Allow access to the folder?"), null, false, null);
        var bare = SessionLive.Pending(State(SessionStatus.Waiting, "Pide permiso"), null, false, null);

        Assert.Equal("Allow access to the folder?", other!.Text);
        Assert.Equal("Pide permiso para seguir", bare!.Text);
    }

    [Fact]
    public void Un_plan_sin_aprobar_es_un_plan()
    {
        var p = SessionLive.Pending(State(SessionStatus.Waiting, "Plan pendiente de aprobar"), Trace(Step(TraceKind.Tool, 1, title: "ExitPlanMode")), false, null);

        Assert.NotNull(p);
        Assert.Equal("plan", p.Kind);
        Assert.Equal(Now.AddMinutes(-1), p.SinceUtc);
    }

    [Fact]
    public void Una_terminada_sin_revisar_trae_su_resumen_o_el_texto_de_siempre()
    {
        var with = SessionLive.Pending(State(SessionStatus.Done, since: 4), null, true, "PR #31 abierta");
        var without = SessionLive.Pending(State(SessionStatus.Done), null, true, null);

        Assert.Equal("review", with!.Kind);
        Assert.Equal("PR #31 abierta", with.Text);
        Assert.Equal(Now.AddMinutes(-4), with.SinceUtc);
        Assert.Equal(SessionLive.ReviewFallback, without!.Text);
    }

    [Fact]
    public void Una_sesion_que_trabaja_no_espera_nada()
    {
        var ask = Step(TraceKind.Tool, 6, title: "AskUserQuestion");
        ask.Questions = [new TraceQuestion { Question = "¿Sigo?" }];

        Assert.Null(SessionLive.Pending(State(SessionStatus.Working), Trace(ask), false, null));
    }

    [Fact]
    public void Las_ultimas_acciones_van_de_la_mas_reciente_a_la_mas_antigua_con_su_actor()
    {
        var launch = Step(TraceKind.Agent, 9, 1, "Explore · busca el panel");
        launch.ChildAgentId = "a1";
        var trace = Trace(
            Step(TraceKind.Prompt, 10, title: "Prompt", text: "Arregla el redondeo\nde cotas"),
            launch,
            Step(TraceKind.Tool, 8, 7, "Grep · «task-helper»", agent: "a1"),
            Step(TraceKind.Thinking, 6, title: "Razonamiento", text: "..."),
            Step(TraceKind.Tool, 5, 4, "Bash · dotnet test"));

        var acts = SessionLive.Recent(trace, 4);

        Assert.Equal(4, acts.Count);
        Assert.Equal(new SessionAction("B", "run", "Ejecuta", "dotnet test", Now.AddMinutes(-5)), acts[0]);
        Assert.Equal(new SessionAction("C", "search", "Busca", "«task-helper»", Now.AddMinutes(-8)), acts[1]);
        Assert.Equal(("B", "agent", "Lanza"), (acts[2].Who, acts[2].Hue, acts[2].Verb));
        Assert.Equal(new SessionAction("A", "user", "Prompt", "Arregla el redondeo", Now.AddMinutes(-10)), acts[3]);
    }

    [Fact]
    public void La_actividad_reparte_el_trabajo_por_trozos_sin_contar_las_esperas()
    {
        var face = new ClockFace { From = 0, To = 4000 };
        var main = new ClockRing { Main = true };
        main.Spans.Add(new ClockSpan(0, 1000, "read"));
        main.Spans.Add(new ClockSpan(1500, 2000, "edit"));
        main.Spans.Add(new ClockSpan(2000, 4000, "wait"));
        var agent = new ClockRing();
        agent.Spans.Add(new ClockSpan(0, 1000, "run"));
        face.Rings.Add(main);
        face.Rings.Add(agent);

        Assert.Equal([100, 50, 0, 0], SessionLive.Activity(face, 4));
    }
}
