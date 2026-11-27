using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Las estadísticas: las cifras del periodo, el histórico, el consumo por proyecto o por tarea, la caché, los
/// límites y los prompts más caros.</summary>
/// <remarks>
/// La enseña el panel, con su modelo y con el periodo en su barra, y la relee él. En un corcho va su widget
/// (<see cref="StatsWidget"/>), no esta vista.
/// </remarks>
public partial class StatsView : UserControl
{
    private WorkSchedule _work = new();

    // Lo último pintado: solo se repinta si hay algo nuevo, porque rehacer las barras cerraría la ficha que se esté mirando.
    private (HistoryPeriod, DateTime, int, DateTime?, int, string?, long)? _shown;

    /// <summary>Crea la vista del panel, que toma su modelo del panel.</summary>
    public StatsView()
    {
        InitializeComponent();
    }

    private PanelModel Model => (PanelModel)DataContext!;

    /// <summary>Recibe el horario laboral del panel, con el que se filtra «Horario laboral».</summary>
    public void Init(WorkSchedule work) => _work = work;

    /// <summary>Olvida lo pintado, para que la próxima lectura repinte aunque no haya prompts nuevos.</summary>
    public void Forget() => _shown = null;

    /// <summary>Relee y repinta aunque no haya prompts nuevos: al cambiar el periodo o el desglose, o tras una limpieza.</summary>
    public void ForceReload()
    {
        Forget();
        Reload();
    }

    /// <summary>Relee el histórico del periodo y repinta las cifras, si hay algo nuevo.</summary>
    public void Reload()
    {
        try
        {
            var records = LoadRecords();
            var work = Model.WorkHoursActive ? _work : null;
            var limits = History.LoadLimits(History.From(Model.Period, DateTime.Today));
            var now = DateTime.Now;
            // El tramo anterior crece con la hora: cada diez minutos se repinta aunque no haya prompts nuevos.
            var tick = Model.Period == HistoryPeriod.All ? 0 : now.Ticks / TimeSpan.FromMinutes(10).Ticks;
            var key = (Model.Period, DateTime.Today, records.Count, records.LastOrDefault()?.WhenUtc, limits.Count, work?.Describe(), tick);
            if (key == _shown) return;
            _shown = key;
            var (_, series) = History.Series(records, Model.Period, now, work);
            Model.Show(History.Summarize(records), series.Where(b => b.Start <= now).ToList(), Previous(now), FirstDay());
            var (size, buckets) = History.Buckets(records, Model.Period, DateTime.Now, work);
            Model.ShowHistory(size, buckets);
            Model.ShowProjects(Model.ByTask ? TaskBreakdown(records) : History.ByProject(records));
            var (cacheSize, cache) = History.CacheBuckets(records, Model.Period, DateTime.Now, work);
            Model.ShowCache(cacheSize, cache);
            Model.ShowLimits(limits, History.From(Model.Period, DateTime.Today), DateTime.Now,
                Model.Period == HistoryPeriod.Today ? work : null);
            Model.ShowCostliest(History.Costliest(records, PanelModel.CostliestCount));
        }
        catch (Exception ex)
        {
            JsonStore.LogError("panel", ex);
        }
    }

    /// <summary>Exporta a CSV los prompts del periodo, en la carpeta que elija la persona, y lo dice en el aviso del pie.</summary>
    public async Task ExportAsync()
    {
        if (TopLevel.GetTopLevel(this) is not { } top) return;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exportar a CSV",
            SuggestedFileName = $"arkko-{PanelModel.PeriodSlug(Model.Period)}-{DateTime.Today:yyyy-MM-dd}.csv",
            FileTypeChoices = [new FilePickerFileType("CSV (separado por punto y coma)") { Patterns = ["*.csv"] }],
            DefaultExtension = "csv",
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        try
        {
            HistoryCsv.Write(path, LoadRecords());
            Model.Notice = $"Exportado a {path}";
        }
        catch (Exception ex)
        {
            JsonStore.LogError("csv", ex);
            Model.Notice = "No se ha podido exportar: el fichero puede estar abierto en otra aplicación.";
        }
    }

    /// <summary>Prompts del periodo elegido, solo los de horario laboral si el filtro está puesto.</summary>
    private List<PromptRecord> LoadRecords()
    {
        var records = History.Load(History.From(Model.Period, DateTime.Today));
        return Model.WorkHoursActive ? History.InSchedule(records, _work) : records;
    }

    /// <summary>Cifras del tramo anterior con el que se compara el periodo, con el mismo filtro de horario; null en «Todo».</summary>
    private PreviousSummary? Previous(DateTime now)
    {
        if (History.PreviousWindow(Model.Period, now) is not { } window) return null;
        var records = History.Between(History.Load(window.From, window.To), window.From, window.To);
        if (Model.WorkHoursActive) records = History.InSchedule(records, _work);
        return new PreviousSummary(History.Summarize(records), window.From, window.To);
    }

    /// <summary>El consumo del periodo por tarea, con la forma de «Por proyecto»: cada tarea por su clave; el título lo pone la fila.</summary>
    private static List<ProjectUsage> TaskBreakdown(IReadOnlyCollection<PromptRecord> records)
    {
        var directory = TaskDirectory.Instance;
        return TaskLinks.ByTask(records, directory.Settings, directory.Links)
            .Select(t => new ProjectUsage(t.Key, t.NewTokens, t.Prompts, t.Sessions, t.Pct))
            .ToList();
    }

    /// <summary>Primer día con datos en el histórico, para el pie.</summary>
    private static DateTime? FirstDay()
    {
        if (!Directory.Exists(AppPaths.Prompts)) return null;
        var first = Directory.EnumerateFiles(AppPaths.Prompts, "*.jsonl")
            .Select(Path.GetFileNameWithoutExtension).Order(StringComparer.Ordinal).FirstOrDefault();
        return DateTime.TryParseExact(first, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;
    }

    /// <summary>Abre o cierra el desglose por worktree del repositorio pulsado en «Por proyecto».</summary>
    private void ProjectRow_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        if ((sender as Control)?.DataContext is ProjectRow { Expandable: true } row) Model.ToggleProject(row.Name);
    }

    /// <summary>Abre en el Tablero la tarea de la fila de «Por tarea» cuyo menú se ha usado.</summary>
    private void ViewTask_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ProjectRow { TaskKey: { } key }) SessionActions.RequestTask(key);
    }

    /// <summary>Abre en Sesiones la sesión del prompt pulsado en «Prompts más caros».</summary>
    private void CostlyRow_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        if ((sender as Control)?.DataContext is CostlyRow { CanOpen: true } row) SessionActions.RequestSession(row.SessionId);
    }
}
