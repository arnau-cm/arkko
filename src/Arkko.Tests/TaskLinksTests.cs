using Arkko.Core;

namespace Arkko.Tests;

public class TaskLinksTests
{
    private static readonly DateTime Day = new(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc);

    private static PromptRecord Prompt(string session, string? folder, long output, long ms, int minutes = 0, string? text = null, string? name = null)
    {
        var usage = new TurnUsage { DurationMs = ms };
        usage.ByModel["claude-opus-5-5"] = new ModelTokens { Output = output, ApiCalls = 1 };
        return new PromptRecord
        {
            WhenUtc = Day.AddMinutes(minutes), SessionId = session, Project = folder, Repo = "repo", Usage = usage, Prompt = text, SessionName = name,
        };
    }

    [Fact]
    public void Una_sesion_suma_las_tareas_de_sus_comandos_y_no_las_de_su_nombre()
    {
        var sessions = TaskLinks.Sessions([
            Prompt("s1", "DEVLAB-99", 10, 0, 0, "arregla el test"),
            Prompt("s1", "DEVLAB-99", 20, 0, 1, "/opsx:apply DEVLAB-87\ncon cuidado"),
            Prompt("s1", "DEVLAB-99", 30, 0, 2, "sigue", "DEVLAB-12 repaso"),
        ], new BoardSettings());

        var s1 = sessions["s1"];
        Assert.Equal("DEVLAB-99", s1.Principal);
        Assert.Equal(["DEVLAB-99", "DEVLAB-87"], s1.Tasks);
        Assert.Empty(s1.Suggestions);
        // Todo el coste va a la principal.
        var usage = TaskLinks.UsageByTask([
            Prompt("s1", "DEVLAB-99", 10, 0, 0), Prompt("s1", "DEVLAB-99", 20, 0, 1, "/opsx:apply DEVLAB-87"),
        ], new BoardSettings());
        Assert.Equal(["DEVLAB-99"], usage.Keys);
        Assert.Equal(30, usage["DEVLAB-99"].NewTokens);
    }

    [Fact]
    public void Lo_anadido_a_mano_suma_y_lo_quitado_resta_tambien_la_de_la_carpeta()
    {
        PromptRecord[] records = [Prompt("s1", "DEVLAB-99", 10, 0, 0, "/opsx:apply DEVLAB-87"), Prompt("s2", "develop", 5, 0, 0, "hola")];
        var plan = new TaskPlan();
        plan.AddTask("S1", "devlab-64");
        plan.AddTask("s1", "DEVLAB-62");
        plan.RemoveTask("s1", "DEVLAB-87");
        plan.RemoveTask("s1", "DEVLAB-99");
        plan.AddTask("s2", "DEVLAB-12");

        var sessions = TaskLinks.Sessions(records, new BoardSettings(), plan.SessionLinks);
        // Quitada la de la carpeta, la principal pasa a la siguiente que queda, y con ella el coste.
        Assert.Equal(["DEVLAB-64", "DEVLAB-62"], sessions["s1"].Tasks);
        Assert.Equal("DEVLAB-64", sessions["s1"].Principal);
        Assert.Equal(10, TaskLinks.UsageByTask(records, new BoardSettings(), plan.SessionLinks)["DEVLAB-64"].NewTokens);
        Assert.Equal(["DEVLAB-64", "DEVLAB-62"], sessions["s1"].Manual);
        // Sin clave en la carpeta, la primera añadida es la principal y se lleva el coste.
        Assert.Equal("DEVLAB-12", sessions["s2"].Principal);
        Assert.Equal(5, TaskLinks.UsageByTask(records, new BoardSettings(), plan.SessionLinks)["DEVLAB-12"].NewTokens);
        Assert.Equal(["s1"], TaskLinks.SessionsByTask(sessions.Values)["DEVLAB-64"]);
    }

    [Fact]
    public void La_principal_se_elige_a_mano_y_al_quitarlas_todas_la_sesion_se_queda_sin_tarea()
    {
        PromptRecord[] records = [Prompt("s1", "DEVLAB-99", 10, 0, 0, "/opsx:apply DEVLAB-87")];
        var plan = new TaskPlan();
        plan.SetPrincipal("s1", "devlab-87");

        var s1 = TaskLinks.Sessions(records, new BoardSettings(), plan.SessionLinks)["s1"];
        Assert.Equal("DEVLAB-87", s1.Principal);
        Assert.Equal(["DEVLAB-87", "DEVLAB-99"], s1.Tasks);
        Assert.Equal(["DEVLAB-87"], TaskLinks.UsageByTask(records, new BoardSettings(), plan.SessionLinks).Keys);

        // Quitar la elegida devuelve la principal a la de serie; quitarlas todas deja la sesión sin tarea ni coste.
        plan.RemoveTask("s1", "DEVLAB-87");
        Assert.Equal("DEVLAB-99", TaskLinks.Sessions(records, new BoardSettings(), plan.SessionLinks)["s1"].Principal);
        plan.RemoveTask("s1", "DEVLAB-99");
        Assert.Null(TaskLinks.Sessions(records, new BoardSettings(), plan.SessionLinks)["s1"].Principal);
        Assert.Empty(TaskLinks.UsageByTask(records, new BoardSettings(), plan.SessionLinks));
    }

    [Fact]
    public void Cerrar_un_worktree_no_es_trabajar_en_su_tarea()
    {
        var settings = new BoardSettings();
        Assert.Null(settings.SwitchKeyIn("/close-worktree DEVLAB-87"));
        Assert.Null(settings.SwitchKeyIn("/arkko-stack:close-worktree DEVLAB-87"));
        Assert.Equal("DEVLAB-87", settings.SwitchKeyIn("/opsx:apply DEVLAB-87"));
    }

    [Fact]
    public void Sin_regla_de_comando_solo_cuenta_la_carpeta()
    {
        PromptRecord[] records = [Prompt("s1", "DEVLAB-99", 10, 0, 0, "/opsx:apply DEVLAB-87", "DEVLAB-12")];
        Assert.Equal(["DEVLAB-99"], TaskLinks.Sessions(records, new BoardSettings { TaskSwitch = "" })["s1"].Tasks);
    }

    [Fact]
    public void Solo_se_sugiere_lo_mencionado_a_menudo_del_mismo_proyecto_en_una_sesion_con_tarea()
    {
        PromptRecord[] Mentions(string session, string folder) =>
        [
            Prompt(session, folder, 1, 0, 0, "mira DEVLAB-64 y ABC-1"),
            Prompt(session, folder, 1, 0, 1, "DEVLAB-64 otra vez, y ABC-1"),
            Prompt(session, folder, 1, 0, 2, "sigue con DEVLAB-64, ABC-1 y DEVLAB-45"),
        ];
        // DEVLAB-64 sale en tres prompts y es del proyecto de la principal; DEVLAB-45, solo en uno; ABC-1, de otro proyecto.
        Assert.Equal(["DEVLAB-64"], TaskLinks.Sessions(Mentions("s1", "DEVLAB-99"), new BoardSettings())["s1"].Suggestions);
        // Sin tarea principal, nada: una sesión que solo habla de tareas no trabaja en ellas.
        Assert.Empty(TaskLinks.Sessions(Mentions("s2", "claude-stats"), new BoardSettings())["s2"].Suggestions);
    }

    [Fact]
    public void La_clave_sale_de_la_carpeta_con_el_patron_y_en_mayusculas()
    {
        var settings = new BoardSettings();
        Assert.Equal("DEVLAB-99", TaskLinks.KeyOf(settings, "DEVLAB-99"));
        Assert.Null(TaskLinks.KeyOf(settings, "develop"));
        Assert.Null(TaskLinks.KeyOf(settings, (string?)null));
        Assert.Equal("ABC-7", TaskLinks.KeyOf(new BoardSettings { TaskPattern = "(?i)abc-\\d+" }, "feature/abc-7"));
    }

    [Fact]
    public void Suma_por_tarea_sus_prompts_sesiones_tokens_y_tiempo()
    {
        var usage = TaskLinks.UsageByTask([
            Prompt("s1", "DEVLAB-99", 100, 60_000, 0),
            Prompt("s2", "DEVLAB-99", 50, 30_000, 10),
            Prompt("s3", "DEVLAB-94", 7, 1_000, 5),
            Prompt("s4", "develop", 999, 9_000, 1),
        ], new BoardSettings());

        Assert.Equal(["DEVLAB-94", "DEVLAB-99"], usage.Keys.Order());
        var t = usage["devlab-99"];
        Assert.Equal((2, 2, 150L, TimeSpan.FromSeconds(90)), (t.Prompts, t.Sessions, t.NewTokens, t.Working));
        Assert.Equal((Day, Day.AddMinutes(10)), (t.FirstUtc, t.LastUtc));
    }

    [Fact]
    public void Reparte_el_consumo_entre_tareas_sin_contar_las_carpetas_sin_clave()
    {
        var byTask = TaskLinks.ByTask([
            Prompt("s1", "DEVLAB-99", 300, 0), Prompt("s2", "DEVLAB-94", 100, 0), Prompt("s3", "develop", 5000, 0),
        ], new BoardSettings());

        Assert.Equal(["DEVLAB-99", "DEVLAB-94"], byTask.Select(t => t.Key));
        Assert.Equal([75.0, 25.0], byTask.Select(t => t.Pct));
    }

    [Fact]
    public void Suma_por_sesion()
    {
        var bySession = TaskLinks.UsageBySession([Prompt("s1", "DEVLAB-99", 10, 0), Prompt("s1", "DEVLAB-99", 5, 0), Prompt("s2", "x", 1, 0)]);
        Assert.Equal(15, bySession["s1"].NewTokens);
        Assert.Equal(2, bySession["s1"].Prompts);
    }
}
