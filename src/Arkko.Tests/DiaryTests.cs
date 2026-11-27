using System.Diagnostics;
using Arkko.Core;

namespace Arkko.Tests;

public sealed class DiaryTests : IDisposable
{
    private static readonly DateTime Day = new(2026, 10, 21);
    private static readonly DateTime Nine = new DateTime(2026, 10, 21, 9, 0, 0, DateTimeKind.Local).ToUniversalTime();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccs-diary-" + Guid.NewGuid().ToString("N"));

    public DiaryTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        // Solo la carpeta temporal que ha creado este test; git deja sus ficheros de solo lectura.
        if (!_dir.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)) return;
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, recursive: true);
    }

    /// <summary>Un prompt de una sesión que terminó a una hora, con lo que duró y sus tokens nuevos.</summary>
    private static PromptRecord Prompt(string session, DateTime endUtc, int minutes, long output = 100, string? text = null, string project = "repo")
    {
        var usage = new TurnUsage { DurationMs = minutes * 60_000L };
        usage.ByModel["claude-opus-5-5"] = new ModelTokens { Output = output, ApiCalls = 1 };
        return new PromptRecord { SessionId = session, WhenUtc = endUtc, Usage = usage, Prompt = text, Repo = project, Project = "DEVLAB-99" };
    }

    private static DiaryInputs Inputs(IReadOnlyList<PromptRecord> prompts, IReadOnlyList<StateChange>? states = null,
        IReadOnlyList<TaskStateChange>? moves = null, IReadOnlyList<DiaryCommit>? commits = null,
        IReadOnlyList<DiaryPullRequest>? prs = null, Dictionary<string, SessionTasks>? tasks = null) =>
        new(Day, prompts, states ?? [], moves ?? [], commits ?? [], prs ?? [],
            tasks ?? new Dictionary<string, SessionTasks> { ["s1"] = SessionTasks.Of("s1", "DEVLAB-99") },
            key => key == "DEVLAB-99" ? "Modo revisión" : null, 78);

    [Fact]
    public void Los_prompts_seguidos_de_una_sesion_forman_un_bloque_y_una_pausa_larga_lo_parte()
    {
        var prompts = new[]
        {
            Prompt("s1", Nine.AddMinutes(20), 20),
            Prompt("s1", Nine.AddMinutes(35), 10),
            Prompt("s1", Nine.AddMinutes(90), 15),
        };
        var day = Diary.Build(Inputs(prompts), Nine);

        Assert.Equal(2, day.Blocks.Count);
        Assert.Equal((Nine, Nine.AddMinutes(35)), (day.Blocks[0].StartUtc, day.Blocks[0].EndUtc));
        Assert.Equal(2, day.Blocks[0].Prompts);
        Assert.Equal(30 * 60_000L, day.Blocks[0].WorkingMs);
        Assert.Equal("DEVLAB-99", day.Blocks[1].Task);
    }

    [Fact]
    public void Los_avisos_y_los_comandos_de_consola_suman_su_trabajo_pero_no_cuentan_como_prompts()
    {
        var prompts = new[]
        {
            Prompt("s1", Nine.AddMinutes(10), 10, text: "Arregla el test"),
            Prompt("s1", Nine.AddMinutes(12), 2, text: "<task-notification><task-id>b1</task-id><summary>Espera terminada</summary></task-notification>"),
            new PromptRecord { SessionId = "s1", WhenUtc = Nine.AddMinutes(14), Usage = Prompt("s1", Nine, 2).Usage, Prompt = "deploy.ps1", Kind = PromptKind.Shell, Repo = "repo", Project = "DEVLAB-99" },
        };
        var block = Diary.Build(Inputs(prompts), Nine).Blocks.Single();

        Assert.Equal(1, block.Prompts);
        Assert.Equal(14 * 60_000L, block.WorkingMs);
        Assert.Equal([PromptKind.Prompt, PromptKind.Notification, PromptKind.Shell], block.Turns.Select(t => t.Kind));
        Assert.Equal(["Arregla el test", "Espera terminada", "deploy.ps1"], block.Turns.Select(t => t.Text));
        Assert.Equal(1, History.Summarize(prompts).Prompts);
    }

    [Fact]
    public void Las_esperas_se_recortan_al_bloque_y_una_sin_final_no_cuenta()
    {
        var states = new[]
        {
            new StateChange { SessionId = "s1", WhenUtc = Nine.AddMinutes(5), Status = SessionStatus.Waiting },
            new StateChange { SessionId = "s1", WhenUtc = Nine.AddMinutes(12), Status = SessionStatus.Working },
            new StateChange { SessionId = "s1", WhenUtc = Nine.AddMinutes(18), Status = SessionStatus.Waiting },
        };
        var day = Diary.Build(Inputs([Prompt("s1", Nine.AddMinutes(20), 20)], states), Nine);

        var wait = Assert.Single(day.Blocks[0].Waits);
        Assert.Equal((Nine.AddMinutes(5), Nine.AddMinutes(12)), (wait.StartUtc, wait.EndUtc));
        Assert.Equal(7 * 60_000L, day.Tasks[0].WaitMs);
    }

    [Fact]
    public void Solo_se_sabe_cuanto_espero_un_bloque_si_ya_se_apuntaban_los_estados()
    {
        var prompts = new[] { Prompt("s1", Nine.AddMinutes(10), 10), Prompt("s1", Nine.AddMinutes(200), 10) };
        var states = new[] { new StateChange { SessionId = "s1", WhenUtc = Nine.AddMinutes(150), Status = SessionStatus.Working } };

        var day = Diary.Build(Inputs(prompts, states), Nine);
        Assert.Equal(Nine.AddMinutes(150), day.StatesSinceUtc);
        Assert.False(day.WaitsKnown(day.Blocks[0]));
        Assert.True(day.WaitsKnown(day.Blocks[1]));
        Assert.False(Diary.Build(Inputs(prompts), Nine).WaitsKnown(day.Blocks[1]));
    }

    [Fact]
    public void El_parte_junta_por_tarea_sus_commits_su_PR_y_sus_cambios_de_columna()
    {
        var prompts = new[] { Prompt("s1", Nine.AddMinutes(60), 60), Prompt("s2", Nine.AddMinutes(30), 30, project: "arkko-stack") };
        var commits = new[] { new DiaryCommit { Hash = "a", Subject = "Revisión: elegir líneas", Task = "DEVLAB-99", Project = "repo", WhenUtc = Nine } };
        var moves = new[]
        {
            new TaskStateChange { Key = "DEVLAB-99", From = TaskState.Doing, To = TaskState.Review, WhenUtc = Nine },
            new TaskStateChange { Key = "DEVLAB-87", From = TaskState.Review, To = TaskState.Done, WhenUtc = Nine },
        };
        var tasks = new Dictionary<string, SessionTasks> { ["s1"] = SessionTasks.Of("s1", "DEVLAB-99"), ["s2"] = SessionTasks.Of("s2", null) };
        var day = Diary.Build(Inputs(prompts, moves: moves, commits: commits, prs: [new DiaryPullRequest("DEVLAB-99", "repo", 23)], tasks: tasks), Nine);

        Assert.Equal(["DEVLAB-99", null, "DEVLAB-87"], day.Tasks.Select(t => t.Key));
        var main = day.Tasks[0];
        Assert.Equal(("Modo revisión", 1, 23), (main.Title, main.Commits.Count, main.PullRequests.Single()));
        Assert.Equal(TaskState.Review, main.Moves.Single().To);
        Assert.Equal("arkko-stack", day.Tasks[1].Project);
        Assert.Equal(0, day.Tasks[2].Sessions);
    }

    [Fact]
    public void El_parte_en_texto_dice_lo_hecho_por_tarea()
    {
        var prompts = new[] { Prompt("s1", Nine.AddMinutes(250), 250, output: 2_100_000) };
        var commits = new[] { new DiaryCommit { Hash = "a", Subject = "Cambios: pestañas de alcance", Task = "DEVLAB-99", Project = "repo", WhenUtc = Nine } };
        var moves = new[] { new TaskStateChange { Key = "DEVLAB-99", From = TaskState.Doing, To = TaskState.Review, WhenUtc = Nine } };
        var day = Diary.Build(Inputs(prompts, moves: moves, commits: commits, prs: [new DiaryPullRequest("DEVLAB-99", "repo", 23)]), Nine);

        Assert.Equal("""
            Miércoles 21 de octubre
            4h 10m de agente · 2,1M tokens · pico 5 h: 78 %

            DEVLAB-99 · Modo revisión — 1 sesión · 4h 10m
              - Cambios: pestañas de alcance
              PR #23 · En curso → En revisión
            """.ReplaceLineEndings(), Diary.Report([day]).ReplaceLineEndings());
    }

    [Fact]
    public void Sin_tarea_el_parte_va_por_repositorio_y_carpeta_del_worktree()
    {
        var master = Prompt("s2", Nine.AddMinutes(10), 5, project: "arkko-stack");
        master.Project = "master";
        var develop = Prompt("s3", Nine.AddMinutes(20), 5, project: "arkko-stack");
        develop.Project = "develop";
        var tasks = new Dictionary<string, SessionTasks> { ["s2"] = SessionTasks.Of("s2", null), ["s3"] = SessionTasks.Of("s3", null) };
        var day = Diary.Build(Inputs([master, develop], tasks: tasks), Nine);

        Assert.Equal("master", day.Blocks[0].Folder);
        Assert.Equal(["arkko-stack (develop)", "arkko-stack (master)"], day.Tasks.Select(Diary.NameOf).Order());
        Assert.Single(Diary.BlocksOf(day, day.Tasks.Single(t => t.Folder == "master")));
    }

    [Fact]
    public void Sin_commits_el_parte_cuenta_lo_que_se_pidio()
    {
        var prompts = new[]
        {
            Prompt("s1", Nine.AddMinutes(5), 5, text: "Revisa el guardrail de worktrees"),
            Prompt("s1", Nine.AddMinutes(8), 2, text: "Revisa el guardrail de worktrees"),
        };
        var day = Diary.Build(Inputs(prompts), Nine);

        Assert.Equal(2, day.Blocks.Single().Turns.Count);
        Assert.Equal(["Revisa el guardrail de worktrees"], Diary.Asked(day, day.Tasks.Single()));
        Assert.Contains("  - Pedido: «Revisa el guardrail de worktrees»", Diary.Report([day]));
    }

    [Fact]
    public void Un_resumen_calculado_tras_cerrar_el_dia_ya_no_cambia_y_se_guarda_tal_cual()
    {
        var day = Diary.Build(Inputs([Prompt("s1", Nine.AddMinutes(20), 20)]), Nine);
        Assert.False(DiaryStore.Final(day));
        day.BuiltUtc = new DateTime(2026, 10, 22, 3, 0, 0, DateTimeKind.Local).ToUniversalTime();
        Assert.True(DiaryStore.Final(day));
        // Uno de una versión anterior del formato se vuelve a calcular, aunque su día ya acabara.
        day.Version--;
        Assert.False(DiaryStore.Final(day));
        day.Version++;

        DiaryStore.Save(day, _dir);
        var read = DiaryStore.Load(Day, _dir)!;
        Assert.Equal((day.Blocks.Count, day.Tasks[0].Key, day.WorkingMs), (read.Blocks.Count, read.Tasks[0].Key, read.WorkingMs));
    }

    [Fact]
    public void Dos_hilos_pueden_guardar_el_mismo_dia_a_la_vez()
    {
        var day = Diary.Build(Inputs([Prompt("s1", Nine.AddMinutes(20), 20)]), Nine);
        Parallel.For(0, 16, _ => DiaryStore.Save(day, _dir));
        Assert.Single(DiaryStore.Load(Day, _dir)!.Blocks);
    }

    [Fact]
    public void Los_commits_del_dia_son_los_de_la_persona_en_la_rama_de_la_carpeta()
    {
        var repo = Path.Combine(_dir, "repo");
        Directory.CreateDirectory(repo);
        Git(repo, "init", "-q");
        Git(repo, "config", "user.email", "persona@example.com");
        Git(repo, "config", "user.name", "Persona");
        Git(repo, "config", "commit.gpgsign", "false");
        Git(repo, "commit", "-q", "--allow-empty", "-m", "Lo de hoy");
        Git(repo, "-c", "user.email=otra@example.com", "commit", "-q", "--allow-empty", "-m", "De otra persona");

        var commits = DiaryStore.CommitsIn(repo, DateTime.Today, "DEVLAB-99", "repo");
        var commit = Assert.Single(commits);
        Assert.Equal(("Lo de hoy", "DEVLAB-99"), (commit.Subject, commit.Task));
        Assert.Empty(DiaryStore.CommitsIn(repo, DateTime.Today.AddDays(-1), null, "repo"));
    }

    [Fact]
    public void El_repositorio_de_una_carpeta_borrada_es_la_raiz_con_bare_de_encima()
    {
        var root = Path.Combine(_dir, "Tienda");
        Directory.CreateDirectory(Path.Combine(root, ".bare"));
        Directory.CreateDirectory(Path.Combine(_dir, "suelta"));

        Assert.Equal("Tienda", DiaryStore.RepoOfFolder(Path.Combine(root, "DEVLAB-6")));
        Assert.Equal("Tienda", DiaryStore.RepoOfFolder(Path.Combine(root, "develop") + "\\"));
        Assert.Null(DiaryStore.RepoOfFolder(Path.Combine(_dir, "suelta", "otra")));
    }

    /// <summary>Lanza git en una carpeta y falla el test si git falla.</summary>
    private static void Git(string dir, params string[] args)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = dir, UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var git = Process.Start(info)!;
        git.WaitForExit();
        Assert.True(git.ExitCode == 0, git.StandardError.ReadToEnd());
    }
}
