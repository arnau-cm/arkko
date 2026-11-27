using Arkko.Core;

namespace Arkko.Tests;

public sealed class ActivityLogTests : IDisposable
{
    private static readonly DateTime Morning = new(2026, 10, 26, 9, 0, 0, DateTimeKind.Local);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccs-activity-" + Guid.NewGuid().ToString("N"));

    public ActivityLogTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        // Solo la carpeta temporal que ha creado este test.
        if (_dir.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)) Directory.Delete(_dir, recursive: true);
    }

    private static BoardTask Task(string key, TaskState state, string? status = null) => new(key, "título") { State = state, Status = status };

    [Fact]
    public void Los_estados_se_leen_por_dia_local_y_en_orden()
    {
        var start = Morning.ToUniversalTime();
        StateLog.Append("s", SessionStatus.Working, start, _dir);
        StateLog.Append("s", SessionStatus.Waiting, start.AddMinutes(3), _dir);
        StateLog.Append("s", null, start.AddMinutes(9), _dir);

        var day = StateLog.Read(Morning.Date, _dir);
        Assert.Equal([SessionStatus.Working, SessionStatus.Waiting, null], day.Select(c => c.Status));
        Assert.Equal(start.AddMinutes(3), day[1].WhenUtc);
        Assert.Empty(StateLog.Read(Morning.Date.AddDays(1), _dir));
    }

    [Fact]
    public void Una_tarea_vista_por_primera_vez_no_es_un_cambio()
    {
        var changes = TaskHistory.Record([Task("DEVLAB-1", TaskState.Doing)], Morning.ToUniversalTime(), _dir);
        Assert.Empty(changes);
        Assert.Empty(TaskHistory.Read(Morning.Date, _dir));
    }

    [Fact]
    public void Un_cambio_de_columna_se_apunta_con_la_hora_de_la_lectura()
    {
        var first = Morning.ToUniversalTime();
        TaskHistory.Record([Task("DEVLAB-1", TaskState.Doing), Task("DEVLAB-2", TaskState.Todo)], first, _dir);
        var changes = TaskHistory.Record([Task("DEVLAB-1", TaskState.Review, "En revisión"), Task("DEVLAB-2", TaskState.Todo)], first.AddMinutes(15), _dir);

        var change = Assert.Single(changes);
        Assert.Equal(("DEVLAB-1", TaskState.Doing, TaskState.Review, "En revisión"), (change.Key, change.From, change.To, change.Status));
        Assert.Equal(first.AddMinutes(15), change.WhenUtc);
        Assert.Single(TaskHistory.Read(Morning.Date, _dir));
    }

    [Fact]
    public void Las_tareas_de_otro_tablero_conservan_su_ultima_columna()
    {
        var first = Morning.ToUniversalTime();
        TaskHistory.Record([Task("DEVLAB-1", TaskState.Doing)], first, _dir);
        TaskHistory.Record([Task("TIENDA-7", TaskState.Todo)], first.AddMinutes(1), _dir);
        var changes = TaskHistory.Record([Task("devlab-1", TaskState.Done)], first.AddMinutes(2), _dir);

        Assert.Equal(TaskState.Done, Assert.Single(changes).To);
    }
}
