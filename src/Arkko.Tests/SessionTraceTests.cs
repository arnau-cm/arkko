using System.Text.Json;
using Arkko.Core;

namespace Arkko.Tests;

/// <remarks>Las líneas imitan la forma real del transcript de Claude Code; el secreto se monta por partes, como en SecretDetectorTests.</remarks>
public sealed class SessionTraceTests : IDisposable
{
    private static readonly string Aws = "AK" + "IA" + "Q3EGRT7XYZW5LMNP";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cs-trace-" + Guid.NewGuid().ToString("N"));
    private DateTime _clock = new(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    /// <summary>Hora de la siguiente línea: cada una, un segundo después de la anterior.</summary>
    private string Next() => (_clock = _clock.AddSeconds(1)).ToString("O");

    private object User(object content, bool meta = false) =>
        new { type = "user", isMeta = meta, timestamp = Next(), cwd = @"C:\repos\demo", message = new { role = "user", content } };

    private object Assistant(object block, string? skill = null) =>
        new { type = "assistant", timestamp = Next(), attributionSkill = skill, message = new { id = "m" + _clock.Ticks, model = "claude-opus-5-5", content = new[] { block } } };

    private object Result(string id, string text, bool error = false) =>
        User(new[] { new { type = "tool_result", tool_use_id = id, content = text, is_error = error } });

    private static object ToolUse(string id, string name, object input) => new { type = "tool_use", id, name, input };

    /// <summary>Escribe un transcript JSONL con esas entradas.</summary>
    private static void Write(string path, IEnumerable<object> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, entries.Select(e => JsonSerializer.Serialize(e)));
    }

    /// <summary>Escribe el .meta.json de un subagente.</summary>
    private static void Meta(string agentFile, string type, string toolUseId, int depth, string? model = null) =>
        File.WriteAllText(Path.ChangeExtension(agentFile, ".meta.json"),
            JsonSerializer.Serialize(new { agentType = type, description = "Revisar el código", toolUseId, spawnDepth = depth, requestShape = "background", model }));

    /// <summary>Monta una sesión con prompts, comando, skill, herramientas, un agente con otro anidado y ruido del sistema.</summary>
    private string Session()
    {
        var path = Path.Combine(_root, "demo", "s1.jsonl");
        Write(path,
        [
            new { type = "ai-title", aiTitle = "Título generado", sessionId = "s1" },
            User("<local-command-caveat>Caveat</local-command-caveat>", meta: true),
            User("Revisa los modelos"),
            Assistant(new { type = "thinking", thinking = "" }),
            Assistant(new { type = "thinking", thinking = "Primero leo el fichero" }),
            Assistant(ToolUse("t1", "Read", new { file_path = @"C:\repos\demo\Models.cs" })),
            Result("t1", "AWS_KEY=" + Aws),
            Assistant(new { type = "text", text = "Hecho." }),
            User("<command-name>/review</command-name>\n<command-args>rápido</command-args>"),
            User("<local-command-stdout>salida</local-command-stdout>"),
            Assistant(ToolUse("s1", "Skill", new { skill = "diagrams" })),
            Result("s1", "Launching skill: diagrams"),
            User(new[] { new { type = "text", text = "Cuerpo de la skill" } }, meta: true),
            Assistant(ToolUse("t2", "Edit", new { file_path = @"C:\repos\demo\Models.cs" }), skill: "diagrams"),
            Result("t2", "No se encontró el texto", error: true),
            Assistant(ToolUse("a1", "Agent", new { subagent_type = "general-purpose", description = "Revisar el código", prompt = "Revisa Models.cs" })),
            Result("a1", "Lanzado en segundo plano"),
            User("<task-notification>\n<task-id>b1</task-id>\n<output-file>C:\\tmp\\b1.output</output-file>\n<status>completed</status>\n<summary>Agente terminado</summary>\n</task-notification>"),
            new { type = "custom-title", customTitle = "Mi título", sessionId = "s1" },
        ]);

        var agent = Path.Combine(_root, "demo", "s1", "subagents", "agent-abc.jsonl");
        Write(agent,
        [
            User("Revisa Models.cs"),
            Assistant(ToolUse("g1", "Grep", new { pattern = "class" })),
            Result("g1", "3 coincidencias"),
            Assistant(ToolUse("a2", "Agent", new { subagent_type = "Explore", description = "Buscar usos", prompt = "Busca usos" })),
            Result("a2", "listo"),
            Assistant(new { type = "text", text = "Revisado." }),
        ]);
        Meta(agent, "general-purpose", "a1", 1);

        var nested = Path.Combine(_root, "demo", "s1", "subagents", "agent-def.jsonl");
        Write(nested, [User("Busca usos"), Assistant(ToolUse("r1", "Read", new { file_path = @"C:\repos\demo\Uses.cs" })), Result("r1", "ok")]);
        Meta(nested, "Explore", "a2", 2, "haiku");
        return path;
    }

    [Fact]
    public void Un_mensaje_escrito_mientras_Claude_trabaja_es_un_prompt_de_la_interaccion_en_curso()
    {
        var path = Path.Combine(_root, "demo", "s6.jsonl");
        Write(path,
        [
            User("Arregla el test"),
            Assistant(ToolUse("t1", "Read", new { file_path = @"C:\repos\demo\Test.cs" })),
            new { type = "attachment", timestamp = Next(), attachment = new { type = "queued_command", commandMode = "prompt", prompt = new object[] { new { type = "text", text = "Mira también el otro" } } } },
            Result("t1", "ok"),
            Assistant(new { type = "text", text = "Hecho." }),
        ]);

        var prompts = SessionTraceReader.Read(path).Steps.Where(s => s.Kind == TraceKind.Prompt).ToList();

        Assert.Equal(["Arregla el test", "Mira también el otro"], prompts.Select(p => p.Text));
        Assert.Equal([false, true], prompts.Select(p => p.Queued));
        Assert.Equal([1, 1], prompts.Select(p => p.Interaction));
    }

    [Fact]
    public void Convierte_la_sesion_en_pasos_y_salta_el_ruido_del_sistema()
    {
        var trace = SessionTraceReader.Read(Session());
        var main = trace.Steps.Where(s => s.AgentId is null).ToList();

        Assert.Equal("Mi título", trace.Title);
        Assert.Equal(@"C:\repos\demo", trace.Cwd);
        Assert.Equal(
        [
            TraceKind.Prompt, TraceKind.Thinking, TraceKind.Tool, TraceKind.Reply,
            TraceKind.Command, TraceKind.Skill, TraceKind.Tool, TraceKind.Agent, TraceKind.Notification,
        ], main.Select(s => s.Kind));
        Assert.Equal("/review rápido", main[4].Title);
        Assert.Equal("Agente terminado", main[8].Text);
    }

    [Fact]
    public void El_contexto_es_el_de_la_ultima_respuesta_real_de_la_sesion_principal()
    {
        object Answer(string model, int input, int write, int read) => new
        {
            type = "assistant", timestamp = Next(),
            message = new
            {
                id = "m" + _clock.Ticks, model, content = new[] { new { type = "text", text = "ok" } },
                usage = new { input_tokens = input, cache_creation_input_tokens = write, cache_read_input_tokens = read, output_tokens = 50 },
            },
        };
        var path = Path.Combine(_root, "ctx", "s2.jsonl");
        Write(path, [User("Hola"), Answer("claude-opus-5-5", 10, 1000, 20000), User("Sigue"), Answer("claude-opus-5-5", 5, 500, 30000), Answer("<synthetic>", 0, 0, 0)]);
        var agent = Path.Combine(_root, "ctx", "s2", "subagents", "agent-x.jsonl");
        Write(agent, [User("Encargo"), Answer("claude-opus-5-5", 1, 1, 900000)]);

        Assert.Equal(30505, SessionTraceReader.Read(path).ContextTokens);
        Write(Path.Combine(_root, "ctx", "s3.jsonl"), [User("Sin respuesta")]);
        Assert.Null(SessionTraceReader.Read(Path.Combine(_root, "ctx", "s3.jsonl")).ContextTokens);
    }

    [Fact]
    public void Lo_pegado_y_los_comandos_que_empiezan_por_su_mensaje_son_de_la_persona()
    {
        var path = Path.Combine(_root, "paste", "s5.jsonl");
        const string pasted = "<pasted_content id=\"c9b6\">\nRun .\\tests\\run.ps1\n  FALLO  create\n</pasted_content id=\"c9b6\">\n";
        Write(path,
        [
            User(pasted),
            Assistant(new { type = "text", text = "Lo miro." }),
            User("<command-message>wayfinder</command-message>\n<command-name>/wayfinder</command-name>\n<command-args>scratch siguiente</command-args>"),
        ]);
        var trace = SessionTraceReader.Read(path);

        Assert.Equal([TraceKind.Prompt, TraceKind.Reply, TraceKind.Command], trace.Steps.Select(s => s.Kind));
        // El texto conserva las etiquetas para que la vista pinte lo pegado aparte; el resumen, no.
        Assert.Equal(pasted.Trim(), trace.Steps[0].Text);
        Assert.Equal(@"Run .\tests\run.ps1 FALLO create", trace.Interactions[0].Prompt);
        Assert.Equal("/wayfinder scratch siguiente", trace.Steps[2].Title);
    }

    [Fact]
    public void Un_comando_de_consola_abre_su_turno_con_su_salida_y_la_respuesta_queda_en_el()
    {
        var path = Path.Combine(_root, "shell", "s9.jsonl");
        Write(path,
        [
            User("Despliega"),
            Assistant(new { type = "text", text = "Lánzalo tú." }),
            User("<bash-input> powershell -File ./deploy.ps1</bash-input>"),
            User("<bash-stdout>Instalada la compilación</bash-stdout><bash-stderr></bash-stderr>"),
            Assistant(new { type = "text", text = "Instalada: reinicia la app." }),
        ]);
        var trace = SessionTraceReader.Read(path);

        Assert.Equal([TraceKind.Prompt, TraceKind.Reply, TraceKind.Command, TraceKind.Reply], trace.Steps.Select(s => s.Kind));
        Assert.Equal([1, 1, 2, 2], trace.Steps.Select(s => s.Interaction));
        var shell = trace.Steps[2];
        Assert.Equal("! powershell -File ./deploy.ps1", shell.Title);
        Assert.Equal("! powershell -File ./deploy.ps1", shell.Text);
        Assert.True(shell.Shell);
        Assert.Equal("Instalada la compilación", shell.Result);
        Assert.False(shell.IsError);
    }

    [Fact]
    public void Un_comando_de_consola_que_solo_escribe_errores_queda_fallido()
    {
        var path = Path.Combine(_root, "shell", "s10.jsonl");
        Write(path,
        [
            User("<bash-input>git pul</bash-input>"),
            User("<bash-stdout></bash-stdout><bash-stderr>git: 'pul' is not a git command.</bash-stderr>"),
        ]);
        var shell = SessionTraceReader.Read(path).Steps.Single();

        Assert.Equal("git: 'pul' is not a git command.", shell.Result);
        Assert.True(shell.IsError);
    }

    [Fact]
    public void Las_preguntas_a_la_persona_llevan_sus_opciones_y_lo_que_respondio()
    {
        var path = Path.Combine(_root, "ask", "s6.jsonl");
        var questions = new object[]
        {
            new { question = "¿Qué hacemos con los Menores?", header = "Menores", multiSelect = false,
                options = new[] { new { label = "Una tarea por change", description = "Menos ruido" }, new { label = "Una por hallazgo", description = "Más detalle" } } },
            new { question = "¿Clave " + Aws + "?", header = "Clave", multiSelect = false, options = new[] { new { label = "Sí", description = "" } } },
        };
        Write(path,
        [
            User("Pregunta lo que necesites"),
            Assistant(ToolUse("q1", "AskUserQuestion", new { questions })),
            new
            {
                type = "user", timestamp = Next(),
                message = new { role = "user", content = new[] { new { type = "tool_result", tool_use_id = "q1", content = "Your questions have been answered" } } },
                toolUseResult = new
                {
                    answers = new Dictionary<string, string> { ["¿Qué hacemos con los Menores?"] = "Una tarea por change", ["¿Clave " + Aws + "?"] = "lo escribo yo" },
                    annotations = new Dictionary<string, object> { ["¿Qué hacemos con los Menores?"] = new { notes = "y agrupadas por zona" } },
                },
            },
        ]);
        var ask = SessionTraceReader.Read(path).Steps.Single(s => s.Kind == TraceKind.Tool);

        Assert.Equal(2, ask.Questions!.Count);
        var (first, second) = (ask.Questions[0], ask.Questions[1]);
        Assert.Equal(("Menores", "Una tarea por change", "y agrupadas por zona"), (first.Header, first.Answer, first.Note));
        Assert.Equal(["Una tarea por change", "Una por hallazgo"], first.Options.Select(o => o.Label));
        Assert.Equal("Menos ruido", first.Options[0].Description);
        // El secreto se tapa en la pregunta y la respuesta se casa igual.
        Assert.DoesNotContain(Aws, second.Question);
        Assert.Equal("lo escribo yo", second.Answer);
    }

    [Fact]
    public void Las_imagenes_pegadas_y_las_de_las_herramientas_se_guardan_solo_si_son_de_un_tipo_seguro()
    {
        var path = Path.Combine(_root, "img", "s7.jsonl");
        object Image(string type, string data) => new { type = "image", source = new { type = "base64", media_type = type, data } };
        Write(path,
        [
            User(new object[] { new { type = "text", text = "Mira la captura [Image #1]" }, Image("image/png", "iVBORw0KGgo="), Image("image/svg+xml", "PHN2Zz4=") }),
            Assistant(ToolUse("r1", "Read", new { file_path = @"C:\tmp\shot.jpg" })),
            User(new[] { new { type = "tool_result", tool_use_id = "r1", content = new object[] { Image("image/jpeg", "/9j/4AAQ") } } }),
        ]);
        var trace = SessionTraceReader.Read(path);
        var prompt = trace.Steps.Single(s => s.Kind == TraceKind.Prompt);
        var read = trace.Steps.Single(s => s.Kind == TraceKind.Tool);

        // El SVG puede llevar código: no se guarda.
        Assert.Equal(("image/png", "iVBORw0KGgo="), (prompt.Images!.Single().MediaType, prompt.Images!.Single().Data));
        Assert.Equal("image/jpeg", read.Images!.Single().MediaType);
        Assert.Contains("Mira la captura", prompt.Text);
    }

    [Fact]
    public void Casa_cada_llamada_con_su_resultado_y_tapa_los_secretos()
    {
        var trace = SessionTraceReader.Read(Session());
        var read = trace.Steps.First(s => s.Kind == TraceKind.Tool);
        var edit = trace.Steps.First(s => s.Title.StartsWith("Edit", StringComparison.Ordinal));

        Assert.Equal("Read · Models.cs", read.Title);
        Assert.DoesNotContain(Aws, read.Result);
        Assert.Contains("AKIA…LMNP", read.Result);
        Assert.Equal(read.StartUtc!.Value.AddSeconds(1), read.EndUtc);
        Assert.True(edit.IsError);
        Assert.Equal("diagrams", edit.Skill);
        Assert.Equal("diagrams", trace.Steps.Single(s => s.Kind == TraceKind.Skill).Title);
    }

    [Fact]
    public void Cuelga_los_agentes_de_su_llamada_tambien_los_anidados()
    {
        var trace = SessionTraceReader.Read(Session());
        var call = trace.Steps.Single(s => s.Kind == TraceKind.Agent && s.AgentId is null);

        Assert.Equal("general-purpose · Revisar el código", call.Title);
        Assert.Equal("Revisa Models.cs", call.Text);
        Assert.Equal("agent-abc", call.ChildAgentId);

        var abc = trace.Agents.Single(a => a.Id == "agent-abc");
        var def = trace.Agents.Single(a => a.Id == "agent-def");
        Assert.Equal((1, null as string, true), (abc.Depth, abc.ParentAgentId, abc.Background));
        Assert.Equal((2, "agent-abc"), (def.Depth, def.ParentAgentId));

        // El modelo, del .meta.json; si no lo dice, el de la primera respuesta del agente.
        Assert.Equal("Haiku", def.Model);
        Assert.Equal("Opus 5.5", abc.Model);

        // El encargo del agente no se repite como prompt; sus pasos van a la interacción de su llamada.
        var inAgents = trace.Steps.Where(s => s.AgentId is not null).ToList();
        Assert.DoesNotContain(inAgents, s => s.Kind == TraceKind.Prompt);
        Assert.All(inAgents, s => Assert.Equal(call.Interaction, s.Interaction));
        Assert.Contains(inAgents, s => s.AgentId == "agent-def" && s.Title == "Read · Uses.cs");
    }

    [Fact]
    public void El_resumen_de_una_compactacion_no_es_un_prompt_de_la_persona()
    {
        var path = Path.Combine(_root, "compact", "s4.jsonl");
        Write(path,
        [
            User("Hola"),
            Assistant(new { type = "text", text = "Hola." }),
            new { type = "system", subtype = "compact_boundary", timestamp = Next() },
            new { type = "user", isCompactSummary = true, isVisibleInTranscriptOnly = true, timestamp = Next(), message = new { role = "user", content = "This session is being continued from a previous conversation." } },
            Assistant(new { type = "text", text = "Sigo." }),
            User("Sigue"),
        ]);

        var trace = SessionTraceReader.Read(path);

        Assert.Equal(["Hola", "Sigue"], trace.Interactions.Select(i => i.Prompt));
        Assert.DoesNotContain(trace.Steps, s => (s.Text ?? "").Contains("continued", StringComparison.Ordinal));
        Assert.Equal(["Hola", "Sigue"], SessionCatalog.Prompts(path));
    }

    [Fact]
    public void Parte_la_sesion_en_interacciones_con_su_resumen()
    {
        var trace = SessionTraceReader.Read(Session());

        Assert.Equal([1, 2], trace.Interactions.Select(i => i.Number));
        Assert.Equal("Revisa los modelos", trace.Interactions[0].Prompt);
        Assert.Equal((0, 1, 0), (trace.Interactions[0].Agents, trace.Interactions[0].Tools, trace.Interactions[0].Skills));
        // La segunda: la skill, el Edit, el agente con su Grep y el anidado con su Read.
        Assert.Equal((2, 3, 1), (trace.Interactions[1].Agents, trace.Interactions[1].Tools, trace.Interactions[1].Skills));
        Assert.Equal(Enumerable.Range(0, trace.Steps.Count), trace.Steps.Select(s => s.Order));
    }

    [Fact]
    public void En_directo_lee_solo_lo_nuevo_y_espera_a_las_lineas_a_medio_escribir()
    {
        var path = Path.Combine(_root, "live", "s3.jsonl");
        Write(path, [User("Arregla el test"), Assistant(ToolUse("t1", "Read", new { file_path = @"C:\repos\demo\A.cs" }))]);
        var result = JsonSerializer.Serialize(Result("t1", "contenido"));
        File.AppendAllText(path, result[..20]);

        var live = new SessionTraceBuilder(path);
        Assert.True(live.Update());
        var first = live.Snapshot();
        Assert.Equal([TraceKind.Prompt, TraceKind.Tool], first.Steps.Select(s => s.Kind));
        Assert.Null(first.Steps[1].Result);

        // Se termina la línea del resultado y llega la respuesta: el resultado se casa con la llamada ya leída.
        File.AppendAllText(path, result[20..] + "\n" + JsonSerializer.Serialize(Assistant(new { type = "text", text = "Listo." })) + "\n");
        Assert.True(live.Update());
        var second = live.Snapshot();
        Assert.Equal("contenido", second.Steps[1].Result);
        Assert.Equal(TraceKind.Reply, second.Steps[^1].Kind);
        Assert.False(live.Update());
    }

    [Fact]
    public void En_directo_un_agente_que_aparece_antes_que_su_llamada_espera_a_ella()
    {
        var path = Path.Combine(_root, "live", "s4.jsonl");
        Write(path, [User("Revisa")]);
        var agent = Path.Combine(_root, "live", "s4", "subagents", "agent-zz.jsonl");
        Write(agent, [User("Revisa A"), Assistant(ToolUse("g1", "Grep", new { pattern = "x" }))]);
        Meta(agent, "Explore", "a9", 1);

        var live = new SessionTraceBuilder(path);
        live.Update();
        Assert.Empty(live.Snapshot().Agents);

        File.AppendAllText(path, JsonSerializer.Serialize(Assistant(ToolUse("a9", "Agent", new { subagent_type = "Explore", description = "Revisar", prompt = "Revisa A" }))) + "\n");
        Assert.True(live.Update());
        var trace = live.Snapshot();
        Assert.Equal("agent-zz", trace.Steps.Single(s => s.Kind == TraceKind.Agent).ChildAgentId);
        Assert.Contains(trace.Steps, s => s.AgentId == "agent-zz" && s.Title == "Grep · x");

        // El agente sigue trabajando: su paso nuevo entra en la siguiente lectura.
        File.AppendAllText(agent, JsonSerializer.Serialize(Assistant(new { type = "text", text = "Hecho" })) + "\n");
        Assert.True(live.Update());
        Assert.Contains(live.Snapshot().Steps, s => s.AgentId == "agent-zz" && s.Kind == TraceKind.Reply);
    }

    [Fact]
    public void Un_agente_en_segundo_plano_trabaja_hasta_su_aviso_de_fin_y_otra_vez_si_se_le_retoma()
    {
        var path = Path.Combine(_root, "bg", "s6.jsonl");
        Write(path,
        [
            User("Haz el documento"),
            Assistant(ToolUse("a1", "Agent", new { subagent_type = "fork", description = "Revisar el código", prompt = "Haz el documento", run_in_background = true })),
            Result("a1", "Async agent launched successfully."),
        ]);
        var agent = Path.Combine(_root, "bg", "s6", "subagents", "agent-f1.jsonl");
        Write(agent, [User("Haz el documento"), Assistant(ToolUse("g1", "Grep", new { pattern = "x" }))]);
        Meta(agent, "fork", "a1", 1);

        // Su llamada ya ha vuelto, pero sigue trabajando: aún no ha llegado su aviso de fin. Callado diez minutos, se da por parado.
        var live = new SessionTraceBuilder(path);
        live.Update();
        var worker = live.Snapshot().Agents.Single();
        var last = worker.EndUtc!.Value;
        Assert.True(worker.IsBusy(last.AddMinutes(1)));
        Assert.False(worker.IsBusy(last.AddMinutes(11)));

        // El aviso llega a la cola de la sesión principal, que estaba trabajando.
        File.AppendAllText(path, JsonSerializer.Serialize(new
        {
            type = "queue-operation", operation = "enqueue", timestamp = Next(),
            content = "<task-notification>\n<task-id>f1</task-id>\n<status>completed</status>\n</task-notification>",
        }) + "\n");
        live.Update();
        worker = live.Snapshot().Agents.Single();
        Assert.NotNull(worker.FinishedUtc);
        Assert.False(worker.IsBusy(last.AddMinutes(1)));

        // Retomado con un mensaje: vuelve a escribir después del aviso, sin llamada nueva.
        _clock = _clock.AddMinutes(2);
        File.AppendAllText(agent, JsonSerializer.Serialize(User("Cambia los datos")) + "\n");
        live.Update();
        worker = live.Snapshot().Agents.Single();
        Assert.True(worker.IsBusy(worker.EndUtc!.Value.AddSeconds(30)));

        // Y su nuevo aviso, esta vez como mensaje a la sesión principal parada, lo vuelve a dar por terminado.
        File.AppendAllText(path, JsonSerializer.Serialize(User("<task-notification>\n<task-id>f1</task-id>\n<status>completed</status>\n</task-notification>")) + "\n");
        live.Update();
        worker = live.Snapshot().Agents.Single();
        Assert.False(worker.IsBusy(worker.EndUtc!.Value.AddSeconds(30)));
    }

    [Fact]
    public void Un_agente_sin_llamada_cuelga_del_padre_que_dice_su_meta()
    {
        var path = Path.Combine(_root, "fork", "s5.jsonl");
        Write(path,
        [
            User("Aplica"),
            Assistant(ToolUse("a1", "Agent", new { subagent_type = "general-purpose", description = "Apply phase", prompt = "Aplica" })),
            Result("a1", "Lanzado"),
        ]);
        var phase = Path.Combine(_root, "fork", "s5", "subagents", "agent-fase.jsonl");
        Write(phase, [User("Aplica"), Assistant(ToolUse("s1", "Skill", new { skill = "adversarial-review" }))]);
        Meta(phase, "general-purpose", "a1", 1);

        // Una skill con contexto propio lanza su agente sin llamada Agent: solo su .meta.json dice de quién cuelga.
        var review = Path.Combine(_root, "fork", "s5", "subagents", "agent-rev.jsonl");
        Write(review, [User("Revisa"), Assistant(new { type = "text", text = "Bien" })]);
        File.WriteAllText(Path.ChangeExtension(review, ".meta.json"),
            JsonSerializer.Serialize(new { agentType = "general-purpose", description = "/adversarial-review", spawnDepth = 2, parentAgentId = "fase" }));

        var trace = SessionTraceReader.Read(path);
        Assert.Equal("agent-fase", trace.Agents.Single(a => a.Id == "agent-rev").ParentAgentId);
    }

    [Fact]
    public void Un_prompt_y_una_respuesta_largos_se_leen_enteros()
    {
        var question = new string('p', SessionTraceReader.MaxText * 2);
        var answer = new string('r', SessionTraceReader.MaxText * 3);
        var path = Path.Combine(_root, "long", "s7.jsonl");
        Write(path, [User(question), Assistant(new { type = "text", text = answer })]);

        var steps = SessionTraceReader.Read(path).Steps;
        Assert.Equal(question, steps.Single(s => s.Kind == TraceKind.Prompt).Text);
        Assert.Equal(answer, steps.Single(s => s.Kind == TraceKind.Reply).Text);
    }

    [Fact]
    public void Un_resultado_enorme_se_corta_sin_dejar_ver_un_secreto_que_cruza_el_corte()
    {
        // El detector solo mira lo que se enseña y su margen: un secreto partido por el corte tiene que salir tapado igual.
        var huge = new string('x', SessionTraceReader.MaxText - 6) + " " + Aws + " " + new string('y', 200_000);
        var path = Path.Combine(_root, "huge", "s6.jsonl");
        Write(path,
        [
            User("Lee"),
            Assistant(ToolUse("t1", "Read", new { file_path = @"C:\repos\demo\log.txt" })),
            Result("t1", huge),
        ]);

        var result = SessionTraceReader.Read(path).Steps.Single(s => s.Kind == TraceKind.Tool).Result!;
        Assert.DoesNotContain("Q3EGRT7", result);
        Assert.StartsWith(new string('x', 100), result);
        Assert.EndsWith($"({huge.Length - SessionTraceReader.MaxText:N0} caracteres más)", result);
        Assert.True(result.Length < SessionTraceReader.MaxText + 100);
    }

    [Fact]
    public void Lee_una_linea_mas_larga_que_un_trozo_de_lectura()
    {
        // El lector va a trozos de 1 MB: una línea más larga tiene que leerse entera igualmente.
        var path = Path.Combine(_root, "long", "s7.jsonl");
        Write(path,
        [
            User("Lee el log"),
            Assistant(ToolUse("t1", "Read", new { file_path = "log.txt" })),
            Result("t1", new string('z', 1_500_000) + " FIN"),
            Assistant(new { type = "text", text = "Leído." }),
        ]);

        var trace = SessionTraceReader.Read(path);
        Assert.StartsWith("zzz", trace.Steps.Single(s => s.Kind == TraceKind.Tool).Result);
        Assert.Equal("Leído.", trace.Steps.Last().Text);
    }

    [Fact]
    public void Un_transcript_que_no_existe_da_una_sesion_vacia()
    {
        var trace = SessionTraceReader.Read(Path.Combine(_root, "no-existe.jsonl"));
        Assert.Empty(trace.Steps);
        Assert.Empty(trace.Interactions);
    }

    [Fact]
    public void El_catalogo_lista_las_sesiones_con_titulo_y_primer_prompt_y_busca_en_todos_los_prompts_sin_acentos()
    {
        File.SetLastWriteTimeUtc(Session(), _clock);
        var other = Path.Combine(_root, "otro", "s2.jsonl");
        Write(other, [User("Prepara el informe de ventas")]);
        File.SetLastWriteTimeUtc(other, _clock.AddDays(-3));

        var all = SessionCatalog.List(null, _root);
        Assert.Equal(["s1", "s2"], all.Select(s => s.SessionId));
        Assert.Equal(("demo", "Mi título", "Revisa los modelos"), (all[0].Project, all[0].Title, all[0].FirstPrompt));

        Assert.Equal(["s1"], SessionCatalog.List(_clock.AddDays(-1), _root).Select(s => s.SessionId));
        Assert.Equal(["s2"], SessionCatalog.Search(all, "VENTAS").Select(s => s.SessionId));
        // «/review rápido» no es el primer prompt de s1, y se busca sin acento.
        Assert.Equal(["s1"], SessionCatalog.Search(all, "review rapido").Select(s => s.SessionId));
        Assert.Equal(["Revisa los modelos", "/review rápido"], SessionCatalog.Prompts(all[0].TranscriptPath));
    }

    [Fact]
    public void Cada_respuesta_y_cada_agente_llevan_su_esfuerzo_y_la_sesion_cuenta_sus_modelos()
    {
        var path = Path.Combine(_root, "demo", "s3.jsonl");
        object Said(string model, string effort, string text) => new
        {
            type = "assistant", timestamp = Next(), perTurnEffort = effort,
            message = new { model, id = "m" + _clock.Ticks, content = new object[] { new { type = "text", text } } },
        };
        Write(path,
        [
            User("Empieza"),
            Said("claude-opus-5-5", "high", "Uno"),
            Said("claude-opus-5-5", "high", "Dos"),
            new
            {
                type = "assistant", timestamp = Next(), perTurnEffort = "high",
                message = new { model = "claude-opus-5-5", id = "m3", content = new[] { ToolUse("a1", "Agent", new { subagent_type = "general-purpose", model = "sonnet", effort = "medium", prompt = "Mira" }) } },
            },
        ]);
        var agent = Path.Combine(_root, "demo", "s3", "subagents", "agent-x.jsonl");
        Write(agent, [Said("claude-sonnet-5-5", "medium", "Mirado")]);
        File.WriteAllText(Path.ChangeExtension(agent, ".meta.json"),
            JsonSerializer.Serialize(new { agentType = "general-purpose", toolUseId = "a1", spawnDepth = 1, model = "sonnet", effort = "medium" }));

        var trace = SessionTraceReader.Read(path);
        Assert.All(trace.Steps.Where(s => s.Kind == TraceKind.Reply && s.AgentId is null), s => Assert.Equal("high", s.Effort));
        Assert.Equal(("Sonnet", "medium"), (trace.Agents.Single().Model, trace.Agents.Single().Effort));

        // La sesión principal pesa por sus respuestas (tres, contando la que lanza al agente) y el agente, uno; el «model» de lo que se pide al agente no cuenta.
        var models = SessionCatalog.Summarize(path)!.Models!;
        Assert.Equal([new ModelUse("Opus 5.5", "high", 3), new ModelUse("Sonnet", "medium", 1)], models);
    }

    [Fact]
    public void Las_imagenes_con_que_se_contesta_una_pregunta_son_de_la_pregunta()
    {
        var path = Path.Combine(_root, "demo", "s4.jsonl");
        var picture = new { type = "image", source = new { type = "base64", media_type = "image/png", data = "iVBORw0KGgo=" } };
        Write(path,
        [
            User("Pregúntame"),
            Assistant(ToolUse("q1", "AskUserQuestion", new { questions = new[] { new { question = "¿Se ve bien?", header = "Vista", options = new[] { new { label = "Sí" }, new { label = "No" } } } } })),
            User(new object[] { new { type = "tool_result", tool_use_id = "q1", content = "User has answered your questions: \"¿Se ve bien?\"=\"no, mira (Image attached)\"." }, picture }),
        ]);

        var trace = SessionTraceReader.Read(path);
        var ask = trace.Steps.Single(s => s.Title.StartsWith("AskUserQuestion", StringComparison.Ordinal));
        Assert.Single(ask.Images!);
        Assert.Equal("image/png", ask.Images![0].MediaType);
        // No es un prompt: la imagen no abre una interacción nueva.
        Assert.DoesNotContain(trace.Steps, s => s.Kind == TraceKind.Prompt && s.Images is not null);
    }

    [Fact]
    public void Una_sesion_de_varios_dias_tiene_actividad_en_cada_uno()
    {
        var path = Path.Combine(_root, "demo", "s4.jsonl");
        var first = User("Empieza el lunes");
        var monday = _clock;
        _clock = _clock.AddDays(2);
        var second = User("Sigue el miércoles");
        Write(path, [first, second]);

        var days = SessionCatalog.ActiveDays(path);

        Assert.Equal(new[] { monday.ToLocalTime().Date, _clock.ToLocalTime().Date }.ToHashSet(), days);
        Assert.DoesNotContain(monday.AddDays(1).ToLocalTime().Date, days);
    }

    [Fact]
    public void El_catalogo_lee_solo_lo_nuevo_de_un_transcript_que_crece()
    {
        var path = Path.Combine(_root, "demo", "s5.jsonl");
        Write(path, [User("Primero")]);
        Assert.Equal(["Primero"], SessionCatalog.Prompts(path));

        File.AppendAllLines(path, [JsonSerializer.Serialize(new { type = "custom-title", customTitle = "Con título" }), JsonSerializer.Serialize(User("Segundo"))]);

        Assert.Equal(["Primero", "Segundo"], SessionCatalog.Prompts(path));
        var summary = SessionCatalog.Summarize(path)!;
        Assert.Equal(("Con título", "Primero"), (summary.Title, summary.FirstPrompt));
    }

    [Fact]
    public void Un_transcript_reescrito_se_vuelve_a_leer_desde_el_principio()
    {
        var path = Path.Combine(_root, "demo", "s6.jsonl");
        Write(path, [User("Viejo"), User("Otro viejo")]);
        Assert.Equal(2, SessionCatalog.Prompts(path).Count);

        // Más corto que lo leído: se nota por el tamaño.
        Write(path, [User("Nuevo")]);
        Assert.Equal(["Nuevo"], SessionCatalog.Prompts(path));

        // Más largo, pero con otro principio: se nota por sus primeros bytes.
        Write(path, [User("Distinto y más largo que lo de antes"), User("Y otro")]);
        Assert.Equal(["Distinto y más largo que lo de antes", "Y otro"], SessionCatalog.Prompts(path));
    }

    [Fact]
    public void Una_linea_a_medio_escribir_espera_a_estar_entera_y_no_se_cuenta_dos_veces()
    {
        var path = Path.Combine(_root, "demo", "s7.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var half = JsonSerializer.Serialize(User("A medias"));
        File.WriteAllText(path, JsonSerializer.Serialize(User("Entero")) + "\n" + half[..(half.Length / 2)]);
        Assert.Equal(["Entero"], SessionCatalog.Prompts(path));

        // Entera aunque aún sin salto de línea: ya cuenta.
        File.AppendAllText(path, half[(half.Length / 2)..]);
        Assert.Equal(["Entero", "A medias"], SessionCatalog.Prompts(path));

        File.AppendAllText(path, "\n" + JsonSerializer.Serialize(User("Tercero")) + "\n");
        Assert.Equal(["Entero", "A medias", "Tercero"], SessionCatalog.Prompts(path));
    }

    [Fact]
    public void Cada_paso_lleva_su_rama()
    {
        var path = Path.Combine(_root, "demo", "s8.jsonl");
        object On(string branch, object entry)
        {
            var json = JsonSerializer.SerializeToNode(entry)!.AsObject();
            json["gitBranch"] = branch;
            return json;
        }
        Write(path,
        [
            On("main", User("Crea la rama")),
            On("main", Assistant(ToolUse("b1", "Bash", new { command = "git switch -c DEVLAB-9" }))),
            On("DEVLAB-9", Result("b1", "Switched to a new branch")),
            On("DEVLAB-9", Assistant(ToolUse("e1", "Edit", new { file_path = @"C:\repos\demo\Models.cs", old_string = "a", new_string = "b" }))),
            On("DEVLAB-9", Assistant(ToolUse("n1", "NotebookEdit", new { notebook_path = @"C:\repos\demo\nb.ipynb" }))),
            On("", Assistant(new { type = "text", text = "Hecho." })),
        ]);

        var trace = SessionTraceReader.Read(path);
        Assert.Equal(["main", "main", "DEVLAB-9", "DEVLAB-9", null], trace.Steps.Select(s => s.Branch));
        // La de la sesión es la última que se conoce: una rama vacía no la borra.
        Assert.Equal("DEVLAB-9", trace.Branch);
    }
}
