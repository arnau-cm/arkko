using System.Text.Json;
using Arkko.Core;

namespace Arkko.Tests;

/// <remarks>Los tasks.md de prueba copian la forma de los reales: secciones con «## », tareas numeradas, filas de triaje y
/// tareas de persona; los pasos llevan la entrada como la guarda el transcript.</remarks>
public sealed class ChangePlanTests : IDisposable
{
    private static readonly DateTime At = new(2026, 11, 11, 14, 0, 0, DateTimeKind.Utc);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "arkko-plan-" + Guid.NewGuid().ToString("N"));
    private int _order;

    private const string Tasks = """
        ## 1. Backend: configuración

        - [x] 1.1 Dependencia y configuración base (`src/Ventas.Api`). En `Ventas.Api.csproj`: un GUID nuevo.
        - [ ] 1.2 Factoría de tests compartida (`tests/Ventas.Api.Tests`). Crear `VentasApiFactory.cs` y `TestAuthHandler.cs`.

        ## 5. Acciones y comprobaciones de persona

        - [ ] 5.1 [humana] Registrar la aplicación de **desarrollo** en Entra ID.
        - [x] 5.4 [manual] Desarrollo, en tu PC: abrir la web.

        ## Triaje — Ronda 1 (2026-11-11)

        - [x] [Triaje] Fila 1: `GET /auth/login/` con barra final da un 401 vacío — fix en `AuthSetup.cs`.
        - [ ] Renombrar el DTO de respuesta `MeResponse` a `MeResponseModel`
        """;

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private TraceStep Tool(string title, object input, int minutes, string? agent = null, string? result = null, bool error = false) => new()
    {
        Order = _order++,
        Kind = TraceKind.Tool,
        Title = title,
        Text = JsonSerializer.Serialize(input),
        AgentId = agent,
        Result = result,
        IsError = error,
        StartUtc = At.AddMinutes(minutes),
        EndUtc = At.AddMinutes(minutes).AddSeconds(5),
    };

    private string Change(string name, bool archived = false, string? text = null)
    {
        var dir = archived ? Path.Combine(_root, "openspec", "changes", "archive", name) : Path.Combine(_root, "openspec", "changes", name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "tasks.md"), text ?? Tasks);
        return Path.Combine(dir, "tasks.md");
    }

    [Fact]
    public void Parse_reads_sections_ids_people_and_files()
    {
        var tasks = ChangePlan.Parse(Tasks);

        Assert.Equal(["1.1", "1.2", "5.1", "5.4", "F1", "T1"], tasks.Select(t => t.Id));
        Assert.Equal([3, 4, 8, 9, 13, 14], tasks.Select(t => t.Line));
        Assert.Equal("1. Backend: configuración", tasks[0].Section);
        Assert.True(tasks[0].Done);
        Assert.False(tasks[1].Done);
        Assert.Equal("Dependencia y configuración base", tasks[0].Title);
        Assert.Contains("Ventas.Api.csproj", tasks[0].Files);
        Assert.Equal(["VentasApiFactory.cs", "TestAuthHandler.cs"], tasks[1].Files.Where(f => f.EndsWith(".cs")));
        Assert.True(tasks[2].Human);
        Assert.True(tasks[3].Human);
        Assert.DoesNotContain("[humana]", tasks[2].Text);
        Assert.DoesNotContain("**", tasks[2].Text);
        Assert.Equal("Triaje — Ronda 1 (2026-11-11)", tasks[4].Section);
        Assert.StartsWith("GET /auth/login/ con barra final", tasks[4].Title);
        Assert.Contains("AuthSetup.cs", tasks[4].Files);
        Assert.Equal("Renombrar el DTO de respuesta MeResponse a MeResponseModel", tasks[5].Title);
    }

    [Fact]
    public void Parse_and_Marks_read_ids_with_a_round_letter()
    {
        var tasks = ChangePlan.Parse("## Ronda de diseño\n\n- [x] A.7 Panel visible\n- [ ] P.12 Tabla de líneas\n");
        Assert.Equal(["A.7", "P.12"], tasks.Select(t => t.Id));
        Assert.Equal("Panel visible", tasks[0].Title);

        var trace = new SessionTrace { Steps = [Tool("Edit · tasks.md", new { file_path = "C:/r/openspec/changes/x/tasks.md", new_string = "- [x] P.12 Tabla" }, 3)] };
        Assert.Equal(At.AddMinutes(3).AddSeconds(5), ChangePlan.Marks(trace, ["A.7", "P.12"])["P.12"]);
    }

    [Fact]
    public void FindTasksFile_prefers_the_change_with_the_branch_key()
    {
        Change("devlab-180-otra-cosa");
        var mine = Change("devlab-183-inicio-sesion");

        Assert.Equal(mine, ChangePlan.FindTasksFile(_root, "DEVLAB-183"));
    }

    [Fact]
    public void FindTasksFile_finds_an_archived_change_and_looks_up_from_a_subfolder()
    {
        var old = Change("2026-11-12-devlab-183-inicio-sesion", archived: true);
        var sub = Path.Combine(_root, "src", "web");
        Directory.CreateDirectory(sub);

        Assert.Equal(old, ChangePlan.FindTasksFile(sub, "DEVLAB-183"));
        Assert.True(ChangePlan.Read(sub, "DEVLAB-183", null)!.Archived);
    }

    [Fact]
    public void FindTasksFile_finds_the_archived_change_in_develop_when_the_worktree_is_gone()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".bare"));
        var dir = Path.Combine(_root, "develop", "openspec", "changes", "archive", "2026-11-12-devlab-183-inicio");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "tasks.md"), Tasks);

        Assert.Equal(Path.Combine(dir, "tasks.md"), ChangePlan.FindTasksFile(Path.Combine(_root, "DEVLAB-183"), "DEVLAB-183"));
    }

    [Fact]
    public void FindTasksFile_without_a_key_takes_the_only_change_or_none()
    {
        Assert.Null(ChangePlan.FindTasksFile(_root, "main"));
        var only = Change("un-cambio");
        Assert.Equal(only, ChangePlan.FindTasksFile(_root, "main"));
        Change("otro-cambio");
        Assert.Null(ChangePlan.FindTasksFile(_root, "main"));
    }

    [Fact]
    public void Marks_come_from_edits_loops_and_whole_writes_of_tasks_md()
    {
        var trace = new SessionTrace
        {
            Steps =
            [
                Tool("Edit · tasks.md", new { file_path = "C:/r/openspec/changes/x/tasks.md", old_string = "- [ ] 1.1 Dep", new_string = "- [x] 1.1 Dep" }, 5),
                Tool("Bash · for id in 1.2 5.4; do sed", new { command = "cd openspec/changes/x && for id in 1.2 5.4; do sed -i \"s/^- \\[ \\] $id /- [x] $id /\" tasks.md; done" }, 9),
                Tool("Write · tasks.md", new { file_path = "C:/r/openspec/changes/x/tasks.md", content = "- [x] 1.1 Dep\n- [x] [Triaje] Fila 1: arreglo" }, 20),
                Tool("Edit · otro.md", new { file_path = "C:/r/docs/otro.md", new_string = "- [x] 5.1 no cuenta" }, 30),
            ],
        };

        var marks = ChangePlan.Marks(trace, ["1.1", "1.2", "5.1", "5.4", "F1"]);

        Assert.Equal(At.AddMinutes(5).AddSeconds(5), marks["1.1"]);
        Assert.Equal(At.AddMinutes(9).AddSeconds(5), marks["1.2"]);
        Assert.Equal(At.AddMinutes(9).AddSeconds(5), marks["5.4"]);
        Assert.Equal(At.AddMinutes(20).AddSeconds(5), marks["F1"]);
        Assert.False(marks.ContainsKey("5.1"));
    }

    [Fact]
    public void Added_is_the_first_write_of_tasks_md_that_names_the_task()
    {
        var trace = new SessionTrace
        {
            Steps =
            [
                Tool("Write · tasks.md", new { file_path = "C:/r/openspec/changes/x/tasks.md", content = "- [ ] 1.1 Dep\n- [ ] 1.2 Otra" }, 1),
                Tool("Edit · tasks.md", new { file_path = "C:/r/openspec/changes/x/tasks.md", new_string = "- [ ] [Triaje] Fila 1: arreglo\n- [ ] 1.12 Nueva" }, 40),
            ],
        };

        var added = ChangePlan.Added(trace, ["1.1", "1.2", "1.12", "F1", "T1"]);

        Assert.Equal(At.AddMinutes(1).AddSeconds(5), added["1.1"]);
        Assert.Equal(At.AddMinutes(1).AddSeconds(5), added["1.2"]);
        Assert.Equal(At.AddMinutes(40).AddSeconds(5), added["1.12"]);
        Assert.Equal(At.AddMinutes(40).AddSeconds(5), added["F1"]);
        Assert.False(added.ContainsKey("T1"));
    }

    [Fact]
    public void Read_gives_a_done_task_without_a_visible_mark_the_last_touch_of_tasks_md()
    {
        Change("devlab-183-x");
        var trace = new SessionTrace
        {
            Steps =
            [
                Tool("Edit · tasks.md", new { file_path = "C:/r/openspec/changes/x/tasks.md", old_string = "- [ ] 1.1 Dep", new_string = "- [x] 1.1 Dep" }, 5),
                Tool("Bash · python marca.py tasks.md", new { command = "python marca.py tasks.md" }, 50),
            ],
        };

        var plan = ChangePlan.Read(_root, "DEVLAB-183", trace)!;

        Assert.Equal(At.AddMinutes(5).AddSeconds(5), plan.Tasks.Single(t => t.Id == "1.1").MarkedUtc);
        Assert.Equal(At.AddMinutes(50).AddSeconds(5), plan.Tasks.Single(t => t.Id == "5.4").MarkedUtc);
        Assert.Null(plan.Tasks.Single(t => t.Id == "1.2").MarkedUtc);
    }

    [Fact]
    public void Commits_are_the_git_commits_that_did_not_fail()
    {
        var trace = new SessionTrace
        {
            Steps =
            [
                Tool("Bash · git commit -m \"x\"", new { command = "git commit -m \"x\"" }, 1, agent: "a1", result: "[DEVLAB-183 dc20db2] x\n 2 files changed"),
                Tool("PowerShell · git -C repo commit", new { command = "git -C repo commit -qm y" }, 2),
                Tool("Bash · git commit -m z", new { command = "git commit -m z" }, 3, error: true),
                Tool("Bash · git status", new { command = "git status" }, 4),
            ],
        };

        var commits = ChangePlan.Commits(trace);

        Assert.Equal(2, commits.Count);
        Assert.Equal("dc20db2", commits[0].Hash);
        Assert.Equal("a1", commits[0].AgentId);
        Assert.Null(commits[1].Hash);
    }

    [Fact]
    public void KeyOf_reads_the_task_key_from_a_branch_or_folder()
    {
        Assert.Equal("devlab-183", ChangePlan.KeyOf("DEVLAB-183"));
        Assert.Equal("devlab-12", ChangePlan.KeyOf("feature/DEVLAB-12-algo"));
        Assert.Null(ChangePlan.KeyOf("main"));
        Assert.Null(ChangePlan.KeyOf("feature/escena-3d"));
        Assert.Null(ChangePlan.KeyOf(null));
    }
}
