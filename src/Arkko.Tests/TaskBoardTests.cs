using Arkko.Core;

namespace Arkko.Tests;

public class TaskBoardTests
{
    private const string Conn = "c1";
    private const string Board = "c1/p1/*";
    private static readonly DateTime Now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

    private static BoardTask Task(string key, TaskState state, string? assigneeId = null, string? assignee = null, DateTime? updated = null,
        string? board = null, params string[] labels) =>
        new(key, "Tarea " + key) { State = state, AssigneeId = assigneeId, Assignee = assignee, UpdatedUtc = updated ?? Now, Labels = labels, BoardId = board };

    private static TaskBoardView Arrange(IEnumerable<BoardTask> tasks, TaskPlan? plan = null, TaskFilter? filter = null) =>
        TaskBoard.Arrange(tasks, plan ?? new TaskPlan(), Conn, Board, filter ?? new TaskFilter());

    [Fact]
    public void Cada_tarea_va_a_su_columna_y_el_backlog_aparte()
    {
        var view = Arrange([
            Task("A-1", TaskState.Backlog), Task("A-2", TaskState.Todo), Task("A-3", TaskState.Doing), Task("A-4", TaskState.Blocked),
            Task("A-5", TaskState.Review), Task("A-6", TaskState.Done), Task("A-7", TaskState.Discarded), Task("A-8", TaskState.Unknown),
            Task("A-9", TaskState.ToDiscard),
        ]);

        Assert.Equal(["A-1", "A-8"], view.Backlog.Select(t => t.Key).Order());
        Assert.Equal(["A-2"], view[TaskState.Todo].Select(t => t.Key));
        Assert.Equal(["A-3"], view[TaskState.Doing].Select(t => t.Key));
        Assert.Equal(["A-4"], view[TaskState.Blocked].Select(t => t.Key));
        Assert.Equal(["A-5"], view[TaskState.Review].Select(t => t.Key));
        Assert.Equal(["A-6"], view[TaskState.Done].Select(t => t.Key));
        Assert.Equal(["A-7"], view[TaskState.Discarded].Select(t => t.Key));
        Assert.Equal(["A-9"], view[TaskState.ToDiscard].Select(t => t.Key));
        Assert.Empty(view.History);
    }

    [Fact]
    public void Traer_y_devolver_es_local_y_vale_tambien_para_lo_que_el_proveedor_tiene_por_hacer()
    {
        var plan = new TaskPlan();
        plan.Pull(TaskPlan.Id(Conn, "A-1"));
        plan.Return(TaskPlan.Id(Conn, "A-2"));
        var tasks = new[] { Task("A-1", TaskState.Backlog), Task("A-2", TaskState.Todo) };

        var view = Arrange(tasks, plan);
        Assert.Equal(["A-1"], view[TaskState.Todo].Select(t => t.Key));
        Assert.Equal(["A-2"], view.Backlog.Select(t => t.Key));

        // Devolver lo traído y traer lo devuelto deja cada una donde la tiene el proveedor.
        plan.Return(TaskPlan.Id(Conn, "A-1"));
        plan.Pull(TaskPlan.Id(Conn, "A-2"));
        view = Arrange(tasks, plan);
        Assert.Equal(["A-1"], view.Backlog.Select(t => t.Key));
        Assert.Equal(["A-2"], view[TaskState.Todo].Select(t => t.Key));
    }

    [Fact]
    public void Una_tarea_traida_que_avanza_va_a_su_columna_aunque_siga_en_el_plan()
    {
        var plan = new TaskPlan();
        plan.Pull(TaskPlan.Id(Conn, "A-1"));
        Assert.Equal(["A-1"], Arrange([Task("A-1", TaskState.Doing)], plan)[TaskState.Doing].Select(t => t.Key));
    }

    [Fact]
    public void Limpiar_las_hechas_quita_las_de_antes_y_deja_las_que_se_cierren_despues()
    {
        var plan = new TaskPlan();
        plan.DoneClearedUtc[Board] = Now;
        var view = Arrange([Task("A-1", TaskState.Done, updated: Now.AddHours(-1)), Task("A-2", TaskState.Done, updated: Now.AddMinutes(5))], plan);
        Assert.Equal(["A-2"], view[TaskState.Done].Select(t => t.Key));
        Assert.Equal(["A-1"], view.History.Select(t => t.Key));
        // La limpieza es de ese tablero: en otro, las dos.
        Assert.Equal(2, TaskBoard.Arrange([Task("A-1", TaskState.Done), Task("A-2", TaskState.Done)], plan, Conn, "c1/p2/*", new TaskFilter())[TaskState.Done].Count);
    }

    [Fact]
    public void Quitar_una_hecha_o_descartada_la_lleva_al_historial_y_restaurarla_la_devuelve()
    {
        var plan = new TaskPlan();
        plan.Hide(TaskPlan.Id(Conn, "A-1"));
        plan.Hide(TaskPlan.Id(Conn, "A-3"));
        var tasks = new[] { Task("A-1", TaskState.Done), Task("A-2", TaskState.Done), Task("A-3", TaskState.Discarded) };
        var view = Arrange(tasks, plan);
        Assert.Equal(["A-2"], view[TaskState.Done].Select(t => t.Key));
        Assert.Empty(view[TaskState.Discarded]);
        Assert.Equal(["A-1", "A-3"], view.History.Select(t => t.Key).Order());

        plan.Restore(TaskPlan.Id(Conn, "A-1"));
        Assert.Equal(["A-1", "A-2"], Arrange(tasks, plan)[TaskState.Done].Select(t => t.Key).Order());
    }

    [Fact]
    public void Del_backlog_se_lleva_a_por_descartar_y_de_ahi_se_devuelve()
    {
        var plan = new TaskPlan();
        plan.Discard(TaskPlan.Id(Conn, "A-1"));
        plan.Return(TaskPlan.Id(Conn, "A-2"));
        var tasks = new[] { Task("A-1", TaskState.Backlog), Task("A-2", TaskState.ToDiscard) };
        var view = Arrange(tasks, plan);
        Assert.Equal(["A-1"], view[TaskState.ToDiscard].Select(t => t.Key));
        Assert.Equal(["A-2"], view.Backlog.Select(t => t.Key));

        // Traerla a Por hacer la saca de Por descartar.
        plan.Pull(TaskPlan.Id(Conn, "A-1"));
        Assert.Equal(["A-1"], Arrange(tasks, plan)[TaskState.Todo].Select(t => t.Key));
    }

    [Fact]
    public void Filtra_por_uno_o_varios_subtableros()
    {
        BoardTask[] tasks = [Task("A-1", TaskState.Todo, board: "b1"), Task("A-2", TaskState.Todo, board: "b2"), Task("A-3", TaskState.Todo, board: "b3"),
            Task("A-4", TaskState.Todo)];
        HashSet<string> Boards(params string[] ids) => new(ids, StringComparer.OrdinalIgnoreCase);

        Assert.Equal(["A-1"], Arrange(tasks, filter: new TaskFilter(BoardIds: Boards("B1")))[TaskState.Todo].Select(t => t.Key));
        Assert.Equal(["A-1", "A-3"], Arrange(tasks, filter: new TaskFilter(BoardIds: Boards("b1", "b3")))[TaskState.Todo].Select(t => t.Key).Order());
        // Sin ninguno elegido, todas: también las que no tienen subtablero.
        Assert.Equal(4, Arrange(tasks, filter: new TaskFilter(BoardIds: Boards()))[TaskState.Todo].Count);
    }

    [Fact]
    public void Mias_son_solo_las_asignadas_a_la_persona_por_id_o_por_nombre()
    {
        var me = new BoardPerson("u1", "Persona Uno");
        var view = Arrange([
            Task("A-1", TaskState.Todo, assigneeId: "u1"),
            Task("A-2", TaskState.Todo, assigneeId: "u2", assignee: "Persona Uno"),
            Task("A-3", TaskState.Todo),
            Task("A-4", TaskState.Todo, assignee: "persona uno"),
            Task("A-5", TaskState.Todo, assignee: "Otra Persona"),
        ], filter: new TaskFilter(Me: me));

        // A-2 se llama igual pero su id es otro: no es de la persona; A-3 no la lleva nadie.
        Assert.Equal(["A-1", "A-4"], view[TaskState.Todo].Select(t => t.Key).Order());
    }

    [Fact]
    public void Filtra_por_etiqueta_y_busca_por_clave_o_titulo_sin_acentos()
    {
        var tasks = new[]
        {
            Task("A-1", TaskState.Todo, labels: ["backend"]), Task("A-2", TaskState.Todo, labels: ["frontend"]),
            new BoardTask("A-3", "Revisión del menú") { State = TaskState.Todo },
        };
        Assert.Equal(["A-1"], Arrange(tasks, filter: new TaskFilter(Labels: new HashSet<string> { "backend" }))[TaskState.Todo].Select(t => t.Key));
        Assert.Equal(["A-3"], Arrange(tasks, filter: new TaskFilter(Query: "revision"))[TaskState.Todo].Select(t => t.Key));
        Assert.Equal(["A-2"], Arrange(tasks, filter: new TaskFilter(Query: "a-2"))[TaskState.Todo].Select(t => t.Key));
    }

    [Fact]
    public void Dentro_de_cada_columna_lo_ultimo_que_ha_cambiado_va_arriba()
    {
        var view = Arrange([Task("A-1", TaskState.Doing, updated: Now.AddDays(-2)), Task("A-2", TaskState.Doing, updated: Now)]);
        Assert.Equal(["A-2", "A-1"], view[TaskState.Doing].Select(t => t.Key));
    }

    [Fact]
    public void El_plan_se_guarda_y_se_lee_sin_distinguir_mayusculas()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccs-plan-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new BoardStore(dir);
            var plan = new TaskPlan();
            plan.Pull("c1/A-1");
            plan.Hide("c1/A-2");
            plan.DoneClearedUtc[Board] = Now;
            store.SavePlan(plan);

            var read = store.LoadPlan();
            Assert.Contains("C1/a-1", read.Pulled);
            Assert.Contains("C1/a-2", read.Hidden);
            Assert.Equal(Now, read.DoneClearedUtc[Board]);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
