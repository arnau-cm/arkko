using System.Text.Json;
using Arkko.Core;

namespace Arkko.Tests;

/// <remarks>Cada prueba escribe en una carpeta temporal un transcript con las entradas que Claude Code apunta al lanzar
/// agentes y comandos en segundo plano, con la forma que tienen de verdad, y la carpeta de los transcripts de sus agentes.</remarks>
public sealed class BackgroundTraceTests : IDisposable
{
    private const string Session = "sesion";
    private static readonly DateTime T0 = new(2026, 11, 9, 9, 11, 0, DateTimeKind.Utc);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "cs-bg-" + Guid.NewGuid().ToString("N"));
    private readonly string _transcript;
    private readonly string _subagents;

    public BackgroundTraceTests()
    {
        _subagents = Path.Combine(_folder, Session, "subagents");
        Directory.CreateDirectory(_subagents);
        _transcript = Path.Combine(_folder, Session + ".jsonl");
        File.WriteAllText(_transcript, "");
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static string At(int seconds) => T0.AddSeconds(seconds).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

    /// <summary>Añade al transcript una línea con el objeto dado, como hace Claude Code.</summary>
    private void Append(object entry) => File.AppendAllText(_transcript, JsonSerializer.Serialize(entry) + "\n");

    private void Prompt(int s, string text = "hazlo") => Append(new { type = "user", timestamp = At(s), message = new { role = "user", content = text } });

    private void Call(int s, string id, string name, object input) => Append(new
    {
        type = "assistant", timestamp = At(s),
        message = new { role = "assistant", content = new object[] { new { type = "tool_use", id, name, input } } },
    });

    private void Result(int s, string callId, object result) => Append(new
    {
        type = "user", timestamp = At(s), toolUseResult = result,
        message = new { role = "user", content = new object[] { new { type = "tool_result", tool_use_id = callId, content = "ok" } } },
    });

    /// <summary>Lanza un agente en segundo plano: la llamada y su resultado, que vuelve al momento con el id del agente.</summary>
    private void LaunchAgent(int s, string agentId, string description)
    {
        Call(s, "call-" + agentId, "Agent", new { description, prompt = "…", subagent_type = "general-purpose" });
        Result(s + 1, "call-" + agentId, new { isAsync = true, status = "async_launched", agentId, description });
    }

    private void LaunchCommand(int s, string taskId, string command)
    {
        Call(s, "call-" + taskId, "Bash", new { command, run_in_background = true });
        Result(s + 1, "call-" + taskId, new { stdout = "", stderr = "", interrupted = false, backgroundTaskId = taskId });
    }

    private void TurnEnd(int s)
    {
        Append(new { type = "system", subtype = "stop_hook_summary", timestamp = At(s) });
        Append(new { type = "system", subtype = "turn_duration", timestamp = At(s) });
    }

    private static string NoticeText(string taskId) =>
        $"<task-notification>\n<task-id>{taskId}</task-id>\n<status>completed</status>\n<summary>finished</summary>\n</task-notification>";

    private void Notice(int s, string taskId) => Append(new { type = "queue-operation", operation = "enqueue", timestamp = At(s), content = NoticeText(taskId) });

    private void Reply(int s) => Append(new { type = "assistant", timestamp = At(s), message = new { role = "assistant", content = new object[] { new { type = "text", text = "sigo" } } } });

    /// <summary>Escribe el transcript y el .meta.json de un agente, con su hora de última escritura y si ha acabado.</summary>
    /// <param name="lines">Las líneas de su transcript, como las escribe Claude Code; sin ellas, una vacía.</param>
    private void Agent(string id, DateTime written, bool finished, string? parent = null, string? description = null, IEnumerable<object>? lines = null)
    {
        var file = Path.Combine(_subagents, $"agent-{id}.jsonl");
        File.WriteAllText(file, lines is null ? "{}\n" : string.Concat(lines.Select(l => JsonSerializer.Serialize(l) + "\n")));
        File.SetLastWriteTimeUtc(file, written);
        var meta = new Dictionary<string, object> { ["agentType"] = "general-purpose", ["requestShape"] = "background" };
        if (parent is not null) meta["parentAgentId"] = parent;
        if (description is not null) meta["description"] = description;
        if (finished) meta["finished"] = true;
        File.WriteAllText(Path.Combine(_subagents, $"agent-{id}.meta.json"), JsonSerializer.Serialize(meta));
    }

    /// <summary>Una llamada de un agente, en su transcript.</summary>
    private static object AgentCall(int s, string id, string name, object input) => new
    {
        type = "assistant", isSidechain = true, timestamp = At(s),
        message = new { role = "assistant", content = new object[] { new { type = "tool_use", id, name, input } } },
    };

    /// <summary>El resultado de una llamada de un agente, en su transcript.</summary>
    private static object AgentResult(int s, string callId, object result) => new
    {
        type = "user", isSidechain = true, timestamp = At(s), toolUseResult = result,
        message = new { role = "user", content = new object[] { new { type = "tool_result", tool_use_id = callId, content = "ok" } } },
    };

    /// <summary>Un comando que tardó tanto que Claude Code lo pasó a segundo plano: su resultado trae el id y el tiempo.</summary>
    private static object MovedToBackground(string taskId) => new { stdout = "", stderr = "", interrupted = false, backgroundTaskId = taskId, timedOutAfterMs = 180000 };

    private SessionBackground Read(DateTime now)
    {
        var trace = new BackgroundTrace();
        trace.Read(_transcript);
        var folder = SubagentFolder.Of(_transcript, Session);
        trace.ReadAgents(folder);
        return trace.Snapshot(now, folder);
    }

    [Fact]
    public void Un_agente_que_trabaja_tras_acabar_el_turno_es_una_espera()
    {
        Prompt(0);
        LaunchAgent(10, "a1", "Apply DEVLAB-100 design round");
        Reply(12);
        TurnEnd(15);
        Agent("a1", T0.AddSeconds(60), finished: false);

        var bg = Read(T0.AddSeconds(90));

        Assert.True(bg.MainIdle);
        var agent = Assert.Single(bg.Agents);
        Assert.Equal(("a1", "Apply DEVLAB-100 design round", true), (agent.Id, agent.Description, agent.Running));
        Assert.Equal(T0.AddSeconds(10), agent.StartUtc);
        Assert.Equal(SessionStatus.Agents, SessionActivity.Status(SessionStatus.Done, bg));
        Assert.Equal(SessionStatus.Agents, SessionActivity.Status(SessionStatus.Working, bg));
        Assert.Equal(SessionStatus.Waiting, SessionActivity.Status(SessionStatus.Waiting, bg));
    }

    [Fact]
    public void Un_agente_recien_lanzado_sin_transcript_cuenta_desde_que_se_lanzo()
    {
        LaunchAgent(10, "a1", "Review");
        TurnEnd(15);

        Assert.Equal(1, Read(T0.AddMinutes(2)).RunningAgents);
        Assert.Equal(0, Read(T0.AddMinutes(11)).RunningAgents);
    }

    [Fact]
    public void Mientras_la_sesion_principal_trabaja_manda_el_hook()
    {
        LaunchAgent(10, "a1", "Review");
        TurnEnd(15);
        Prompt(30, "otra cosa");
        Agent("a1", T0.AddSeconds(40), finished: false);

        var bg = Read(T0.AddSeconds(45));

        Assert.False(bg.MainIdle);
        Assert.Equal(SessionStatus.Working, SessionActivity.Status(SessionStatus.Working, bg));
    }

    [Fact]
    public void Con_el_aviso_de_fin_la_sesion_va_a_retomar_y_no_avisa_de_terminada()
    {
        LaunchAgent(10, "a1", "Review");
        TurnEnd(15);
        Agent("a1", T0.AddSeconds(100), finished: true);
        Notice(101, "a1");

        var bg = Read(T0.AddSeconds(102));

        Assert.True(bg.ResumePending);
        var agent = Assert.Single(bg.Agents);
        Assert.False(agent.Running);
        Assert.Equal(T0.AddSeconds(101), agent.FinishedUtc);
        Assert.Equal(SessionStatus.Working, SessionActivity.Status(SessionStatus.Done, bg));
    }

    [Fact]
    public void Al_retomar_y_acabar_otra_vez_ya_ha_terminado()
    {
        LaunchAgent(10, "a1", "Review");
        TurnEnd(15);
        Agent("a1", T0.AddSeconds(100), finished: true);
        Notice(101, "a1");
        Prompt(102, NoticeText("a1"));
        Reply(110);
        TurnEnd(112);

        var bg = Read(T0.AddSeconds(120));

        Assert.True(bg.MainIdle);
        Assert.False(bg.ResumePending);
        Assert.Empty(bg.Agents);
        Assert.Equal(SessionStatus.Done, SessionActivity.Status(SessionStatus.Done, bg));
    }

    [Fact]
    public void Un_agente_despertado_tras_avisar_vuelve_a_trabajar()
    {
        LaunchAgent(10, "a1", "Review");
        TurnEnd(15);
        Notice(100, "a1");
        Prompt(101, NoticeText("a1"));
        Call(105, "send-1", "SendMessage", new { to = "a1", message = "otra ronda" });
        TurnEnd(108);
        Agent("a1", T0.AddSeconds(200), finished: false);

        var bg = Read(T0.AddSeconds(210));

        Assert.Equal(1, bg.RunningAgents);
        Assert.Equal(SessionStatus.Agents, SessionActivity.Status(SessionStatus.Done, bg));
    }

    [Fact]
    public void Sin_finished_en_el_meta_el_aviso_dice_que_acabo()
    {
        LaunchAgent(10, "a1", "Review");
        TurnEnd(15);
        Agent("a1", T0.AddSeconds(100), finished: false);
        Notice(101, "a1");

        Assert.Equal(0, Read(T0.AddSeconds(102)).RunningAgents);
    }

    [Fact]
    public void Un_agente_callado_sigue_vivo_mientras_trabaja_alguno_de_sus_hijos()
    {
        LaunchAgent(10, "a1", "Apply");
        TurnEnd(15);
        var now = T0.AddMinutes(30);
        Agent("a1", now.AddMinutes(-20), finished: false);
        Agent("hijo", now.AddMinutes(-25), finished: true, parent: "a1");
        Agent("nieto", now.AddMinutes(-1), finished: false, parent: "hijo");

        Assert.Equal(1, Read(now).RunningAgents);
        Agent("nieto", now.AddMinutes(-15), finished: true, parent: "hijo");
        Assert.Equal(0, Read(now).RunningAgents);
    }

    [Fact]
    public void Un_comando_en_marcha_se_ve_hasta_que_acaba_o_se_para()
    {
        LaunchCommand(10, "b1", "npm run dev");
        LaunchCommand(20, "b2", "dotnet build");
        LaunchCommand(30, "b3", "npm test");
        Notice(60, "b2");
        Call(70, "stop-1", "TaskStop", new { task_id = "b3" });
        TurnEnd(75);

        var bg = Read(T0.AddSeconds(80));

        var command = Assert.Single(bg.Commands);
        Assert.Equal(("b1", "npm run dev"), (command.Id, command.Description));
        Assert.Empty(bg.Agents);
        Assert.Equal(SessionStatus.Done, SessionActivity.Status(SessionStatus.Done, Read(T0.AddSeconds(50))));
    }

    [Fact]
    public void Un_comando_lanzado_aparte_por_la_sesion_no_es_una_espera()
    {
        LaunchCommand(10, "b1", "npm run dev");
        TurnEnd(15);

        var bg = Read(T0.AddSeconds(30));

        Assert.False(Assert.Single(bg.Commands).Awaited);
        Assert.Equal(SessionStatus.Done, SessionActivity.Status(SessionStatus.Done, bg));
    }

    [Fact]
    public void Un_comando_que_la_sesion_paso_a_segundo_plano_por_tardar_es_una_espera_hasta_su_aviso()
    {
        Call(10, "call-b1", "PowerShell", new { command = "& .\\mutate.ps1", description = "Mutation batch" });
        Result(190, "call-b1", MovedToBackground("b1"));
        TurnEnd(195);

        var waiting = Read(T0.AddSeconds(300));
        var command = Assert.Single(waiting.Commands);
        Assert.Equal(("b1", "Mutation batch", true, (string?)null), (command.Id, command.Description, command.Awaited, command.By));
        Assert.Equal(SessionStatus.Command, SessionActivity.Status(SessionStatus.Done, waiting));

        Notice(400, "b1");
        Assert.Equal(SessionStatus.Working, SessionActivity.Status(SessionStatus.Done, Read(T0.AddSeconds(410))));
    }

    [Fact]
    public void El_comando_que_deja_un_agente_que_ya_acabo_es_una_espera_de_la_sesion()
    {
        LaunchAgent(10, "a1", "Apply phase DEVLAB-184");
        Agent("a1", T0.AddSeconds(250), finished: true, description: "Apply phase DEVLAB-184", lines:
        [
            AgentCall(40, "call-m", "PowerShell", new { command = "& .\\mutate.ps1", description = "Mutation batch" }),
            AgentResult(220, "call-m", MovedToBackground("bwenucufk")),
        ]);
        Notice(260, "a1");
        TurnEnd(270);

        var bg = Read(T0.AddSeconds(900));

        Assert.Equal(0, bg.RunningAgents);
        var command = Assert.Single(bg.Commands);
        Assert.Equal(("bwenucufk", "Mutation batch", true, "Apply phase DEVLAB-184"), (command.Id, command.Description, command.Awaited, command.By));
        Assert.Equal(SessionStatus.Command, SessionActivity.Status(SessionStatus.Done, bg));
    }

    [Fact]
    public void El_comando_de_un_agente_acaba_con_su_aviso_en_cualquier_transcript_o_al_pararlo()
    {
        LaunchAgent(10, "a1", "Apply");
        Agent("a1", T0.AddSeconds(250), finished: true, lines:
        [
            AgentCall(40, "call-1", "Bash", new { command = "dotnet test", run_in_background = true }),
            AgentResult(41, "call-1", new { backgroundTaskId = "c1" }),
            AgentCall(50, "call-2", "PowerShell", new { command = "build.ps1" }),
            AgentResult(170, "call-2", MovedToBackground("c2")),
            AgentCall(180, "call-3", "Bash", new { command = "grep -r x ~/.nuget" }),
            AgentResult(300, "call-3", MovedToBackground("c3")),
            AgentCall(302, "stop", "TaskStop", new { task_id = "c3" }),
            new { type = "queue-operation", operation = "enqueue", isSidechain = true, timestamp = At(310), content = NoticeText("c2") },
        ]);
        Notice(260, "a1");
        TurnEnd(270);
        Notice(400, "c1");

        var bg = Read(T0.AddSeconds(500));

        Assert.Empty(bg.Commands);
        Assert.Equal(SessionStatus.Working, SessionActivity.Status(SessionStatus.Done, bg));
    }

    [Fact]
    public void Un_comando_esperado_sin_noticias_en_horas_deja_de_esperarse()
    {
        Call(10, "call-b1", "Bash", new { command = "./largo.sh" });
        Result(130, "call-b1", MovedToBackground("b1"));
        TurnEnd(135);

        var bg = Read(T0 + BackgroundTrace.AwaitLimit + TimeSpan.FromMinutes(1));

        Assert.False(Assert.Single(bg.Commands).Awaited);
        Assert.Equal(SessionStatus.Done, SessionActivity.Status(SessionStatus.Done, bg));
    }

    [Fact]
    public void Con_agentes_y_comandos_esperando_mandan_los_agentes()
    {
        Call(10, "call-b1", "Bash", new { command = "./largo.sh" });
        Result(130, "call-b1", MovedToBackground("b1"));
        LaunchAgent(140, "a1", "Review");
        TurnEnd(150);
        Agent("a1", T0.AddSeconds(160), finished: false);

        var bg = Read(T0.AddSeconds(200));

        Assert.Equal((1, 1), (bg.RunningAgents, bg.AwaitedCommands));
        Assert.Equal(SessionStatus.Agents, SessionActivity.Status(SessionStatus.Done, bg));
    }

    [Fact]
    public void Lo_que_no_es_de_la_sesion_principal_no_cuenta()
    {
        Call(10, "call-sync", "Agent", new { description = "En primer plano", prompt = "…" });
        Result(20, "call-sync", new { status = "completed", agentId = "sync1" });
        Notice(30, "otro-agente");
        Append(new { type = "assistant", isSidechain = true, timestamp = At(40), message = new { role = "assistant", content = Array.Empty<object>() } });
        TurnEnd(35);

        var bg = Read(T0.AddSeconds(50));

        Assert.Empty(bg.Agents);
        Assert.Empty(bg.Commands);
        Assert.True(bg.MainIdle);
        Assert.False(bg.ResumePending);
    }

    [Fact]
    public void Lee_por_partes_y_deja_una_linea_a_medias_para_la_siguiente()
    {
        var trace = new BackgroundTrace();
        LaunchAgent(10, "a1", "Review");
        var line = JsonSerializer.Serialize(new { type = "system", subtype = "stop_hook_summary", timestamp = At(15) });
        File.AppendAllText(_transcript, line[..20]);
        trace.Read(_transcript);
        Assert.False(trace.Snapshot(T0.AddSeconds(20), SubagentFolder.Of(_transcript, Session)).MainIdle);

        File.AppendAllText(_transcript, line[20..] + "\n");
        trace.Read(_transcript);
        var bg = trace.Snapshot(T0.AddSeconds(20), SubagentFolder.Of(_transcript, Session));
        Assert.True(bg.MainIdle);
        Assert.Equal(1, bg.RunningAgents);
    }

    [Fact]
    public void Sin_trabajo_en_segundo_plano_no_cambia_nada()
    {
        Prompt(0);
        Reply(5);
        TurnEnd(6);

        var bg = Read(T0.AddSeconds(10));

        Assert.Equal(SessionStatus.Done, SessionActivity.Status(SessionStatus.Done, bg));
        Assert.Equal(SessionStatus.Working, SessionActivity.Status(SessionStatus.Working, null));
    }
}
