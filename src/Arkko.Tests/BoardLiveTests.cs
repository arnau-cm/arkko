using Arkko.Core;
using Xunit.Abstractions;

namespace Arkko.Tests;

/// <summary>Prueba un perfil contra su panel real; solo se ejecuta con AGENTPULSE_LIVE_BOARD=1.</summary>
/// <remarks>
/// Sin esa variable no hace nada, para que las pruebas normales nunca salgan a la red. Todo lo del panel
/// llega por variables, porque nada de un panel concreto va en el repositorio: AGENTPULSE_LIVE_PROFILE, la
/// ruta del perfil; AGENTPULSE_LIVE_KEY_VAR, el nombre de la variable que tiene la clave, que se lee dentro
/// del proceso; AGENTPULSE_LIVE_PIN, la huella del certificado si su CA no está en el equipo; y, si se
/// quieren, AGENTPULSE_LIVE_PROJECT, AGENTPULSE_LIVE_BOARD_NAME y AGENTPULSE_LIVE_TASK. Solo escribe
/// recuentos: ni nombres, ni títulos, ni asignados.
/// </remarks>
public class BoardLiveTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Un_perfil_lee_proyectos_subtableros_y_tareas_de_su_panel_real()
    {
        if (Environment.GetEnvironmentVariable("AGENTPULSE_LIVE_BOARD") != "1") return;
        string? Var(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;

        var profile = BoardProfile.Parse(File.ReadAllText(Var("AGENTPULSE_LIVE_PROFILE") ?? throw new InvalidOperationException("Falta AGENTPULSE_LIVE_PROFILE.")));
        var keyVar = Var("AGENTPULSE_LIVE_KEY_VAR") ?? throw new InvalidOperationException("Falta AGENTPULSE_LIVE_KEY_VAR.");
        var secret = profile.Settings.First(s => s.Secret);
        var values = profile.Settings.Where(s => s.Default is not null).ToDictionary(s => s.Key, s => s.Default!);
        values[secret.Key] = Var(keyVar) ?? throw new InvalidOperationException($"La variable {keyVar} está vacía.");

        using var http = BoardHttp.Create(new BoardTrust(Var("AGENTPULSE_LIVE_PIN")));
        var client = new BoardClient(profile, values, http);

        var me = await client.MeAsync();
        output.WriteLine($"Quién soy: {(me is null ? "el perfil no lo dice" : $"con id {me.Id is not null}, con nombre {me.Name is not null}")}");

        var projects = await client.ProjectsAsync();
        output.WriteLine($"Proyectos: {projects.Count}, con clave: {projects.Count(p => p.Key.Length > 0)}, con nombre: {projects.Count(p => p.Name.Length > 0)}");
        Assert.NotEmpty(projects);

        var wanted = Var("AGENTPULSE_LIVE_PROJECT");
        var project = projects.FirstOrDefault(p => p.Key == wanted) ?? projects[0];
        output.WriteLine($"Proyecto pedido encontrado: {project.Key == wanted}");

        var boards = await client.BoardsAsync(project);
        var board = boards.FirstOrDefault(b => b.Name == Var("AGENTPULSE_LIVE_BOARD_NAME"));
        output.WriteLine($"Subtableros: {boards.Count}, el pedido encontrado: {board is not null}");

        var tasks = await client.TasksAsync(project, board);
        output.WriteLine($"Tareas: {tasks.Count}");
        foreach (var g in tasks.GroupBy(t => t.State).OrderBy(g => g.Key))
            output.WriteLine($"  {g.Key}: {g.Count()} (estados del panel: {string.Join(", ", g.Select(t => t.Status).Distinct())})");
        output.WriteLine($"Con asignado: {tasks.Count(t => t.Assignee is not null)}, con estimación: {tasks.Count(t => t.EstimatedHours is not null)}, " +
                         $"con etiquetas: {tasks.Count(t => t.Labels.Count > 0)}, con enlace: {tasks.Count(t => t.Url is not null)}");
        Assert.All(tasks, t => Assert.False(string.IsNullOrEmpty(t.Title)));
        if (me is not null)
            output.WriteLine($"Mías: {tasks.Count(t => TaskBoard.IsMine(t, me))}, sin asignar: {tasks.Count(t => t.Assignee is null && t.AssigneeId is null)}, " +
                             $"con id del asignado: {tasks.Count(t => t.AssigneeId is not null)}, con subtablero: {tasks.Count(t => t.BoardId is not null)}");

        if (Var("AGENTPULSE_LIVE_TASK") is { } key)
        {
            var one = await client.TaskAsync(key);
            output.WriteLine($"La tarea pedida por su clave: {(one is null ? "no" : $"sí, {one.State}")}");
        }
    }
}
