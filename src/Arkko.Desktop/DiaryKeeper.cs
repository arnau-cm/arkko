using Avalonia.Threading;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Guarda el resumen del Diario de hoy y de ayer mientras se puede calcular: al arrancar y cada hora.</summary>
/// <remarks>
/// Los commits de un día se leen en las carpetas de sus sesiones, y un worktree se borra al cerrar su tarea. Guardar
/// el resumen a menudo, aunque no se abra el Diario, evita que un día pierda sus commits. Se calcula fuera del hilo de
/// la ventana, porque lee transcripts y pregunta a git y al remoto.
/// </remarks>
public sealed class DiaryKeeper
{
    // Van antes que Instance: los estáticos se inicializan en el orden en que están escritos.
    private static readonly TimeSpan Every = TimeSpan.FromHours(1);
    private static readonly TimeSpan FirstAfter = TimeSpan.FromSeconds(30);

    public static DiaryKeeper Instance { get; } = new();

    private readonly DispatcherTimer _timer = new() { Interval = Every };
    private bool _started;
    private bool _running;

    private DiaryKeeper() => _timer.Tick += async (_, _) => await KeepAsync();

    /// <summary>Empieza a guardar: poco después de arrancar, cuando los tableros ya se han leído, y luego cada hora.</summary>
    public void Start()
    {
        if (_started) return;
        _started = true;
        var first = new DispatcherTimer { Interval = FirstAfter };
        first.Tick += async (_, _) =>
        {
            first.Stop();
            await KeepAsync();
        };
        first.Start();
        _timer.Start();
    }

    /// <summary>Calcula y guarda los resúmenes de hoy y de ayer que aún pueden cambiar.</summary>
    public async Task KeepAsync()
    {
        if (_running) return;
        _running = true;
        try
        {
            var (settings, links, title) = Snapshot();
            await Task.Run(() =>
            {
                foreach (var day in new[] { DateTime.Today.AddDays(-1), DateTime.Today })
                    DiaryStore.Get(day, settings, links, title, pullRequests: true, DateTime.UtcNow);
            });
        }
        catch (Exception ex)
        {
            JsonStore.LogError("diary", ex);
        }
        finally
        {
            _running = false;
        }
    }

    /// <summary>Lo que el cálculo necesita de los tableros, copiado en el hilo de la ventana para usarlo fuera de él.</summary>
    public static (BoardSettings Settings, IReadOnlyDictionary<string, SessionLink> Links, Func<string, string?> Title) Snapshot()
    {
        var directory = TaskDirectory.Instance;
        var links = new Dictionary<string, SessionLink>(directory.Links, StringComparer.OrdinalIgnoreCase);
        var titles = directory.AllTasks().ToDictionary(t => t.Key, t => t.Title, StringComparer.OrdinalIgnoreCase);
        return (directory.Settings, links, key => titles.GetValueOrDefault(key));
    }
}
