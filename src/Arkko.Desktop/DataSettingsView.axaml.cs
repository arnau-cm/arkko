using System.ComponentModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Sección de datos y retención de la configuración: cuánto ocupa cada cosa, cuánto se conserva y la limpieza.</summary>
/// <remarks>
/// Elegir un plazo lo aplica al momento. Solo se pregunta antes de lo que no se puede deshacer: si
/// con el plazo nuevo hay datos que borrar (un simulacro los cuenta). Si se cancela, el plazo vuelve
/// al que estaba.
/// </remarks>
public partial class DataSettingsView : UserControl
{
    private Func<AppSettings>? _settings;
    private Action<RetentionSettings, CleanupResult?>? _saved;
    private DataModel? _model;

    /// <summary>Crea la sección vacía; se llena con Init.</summary>
    public DataSettingsView() => InitializeComponent();

    private Window? Owner => TopLevel.GetTopLevel(this) as Window;

    /// <summary>Conecta la sección con los ajustes de la app y con quien guarda los plazos y apunta la limpieza.</summary>
    public void Init(Func<AppSettings> settings, Action<RetentionSettings, CleanupResult?> saved)
    {
        _settings = settings;
        _saved = saved;
        Reload();
    }

    /// <summary>Vuelve a leer el inventario y los plazos guardados, también los de Claude Code.</summary>
    public void Reload()
    {
        ClaudeCode.Reload();
        if (_settings is null) return;
        var s = _settings();
        _model = new DataModel(s.Retention, s.LastCleanupUtc, s.LastCleanupText);
        // Después del clic: el chip anterior ya se ha desmarcado y el diálogo no interrumpe el cambio.
        _model.Chosen += () => Dispatcher.UIThread.Post(Apply);
        DataContext = _model;
    }

    /// <summary>Aplica el plazo recién elegido; si la persona cancela una confirmación, lo devuelve al que estaba.</summary>
    private async void Apply()
    {
        if (_model is not { } model) return;
        if (await ApplyIfConfirmed() is not (true, var result))
        {
            Reload();
            return;
        }
        model.Refresh(result, cleaned: result is not null);
        model.Notice = "Guardado.";
    }

    /// <summary>Limpia ahora con los plazos guardados, sin esperar a la limpieza automática.</summary>
    private async void CleanNow_Click(object? sender, RoutedEventArgs e)
    {
        if (await ApplyIfConfirmed() is (true, var result)) _model?.Refresh(result, cleaned: true);
    }

    /// <summary>Cuenta lo que se borraría con los plazos elegidos, lo confirma, lo aplica y guarda los plazos.</summary>
    /// <returns>Si se ha aplicado, y lo que se borró; no se aplica si la persona cancela o falla la limpieza, y entonces no se guarda nada.</returns>
    private async Task<(bool Applied, CleanupResult? Result)> ApplyIfConfirmed()
    {
        if (_model is null || _saved is null) return (false, null);
        var retention = _model.Selected();
        var store = new DataRetention();
        CleanupResult? result = null;
        try
        {
            var preview = store.Apply(retention, DateTime.Now, dryRun: true);
            if (preview.Any)
            {
                var items = new List<string>();
                if (preview.FilesDeleted > 0) items.Add($"{preview.FilesDeleted} fichero(s): histórico pasado de plazo y lo que ya no sirve");
                if (preview.TextStripped > 0) items.Add($"El texto de los prompts de {preview.TextStripped} día(s), conservando sus cifras");
                if (!await AppDialog.ConfirmAsync(Owner, "Borrar datos pasados de plazo", "Con estos plazos se borrará ahora lo siguiente. No se puede deshacer.",
                        "Borrar", DialogTone.Warning, items)) return (false, null);
                result = store.Apply(retention, DateTime.Now);
            }
        }
        catch (Exception ex)
        {
            JsonStore.LogError("retention", ex);
            await AppDialog.ShowAsync(Owner, "No se ha podido limpiar la carpeta de datos", ex.Message,
                note: "No se han guardado los plazos. El detalle está en errors.log.");
            return (false, null);
        }
        _saved(retention, result);
        return (true, result);
    }

    /// <summary>Abre la carpeta de datos.</summary>
    private void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            Folders.Open(AppPaths.Data);
        }
        catch (Exception ex)
        {
            JsonStore.LogError("data-folder", ex);
        }
    }
}

/// <summary>Estado de la ficha: el inventario, los plazos de cada categoría y la última limpieza.</summary>
public sealed class DataModel : INotifyPropertyChanged
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
    private static readonly (string Label, int? Days)[] LongTerms = [("90 días", 90), ("6 meses", 180), ("1 año", 365), ("2 años", 730), ("Siempre", null)];
    private static readonly (string Label, int? Days)[] TextTerms = [("7 días", 7), ("30 días", 30), ("90 días", 90), ("1 año", 365), ("Siempre", null)];
    private IReadOnlyList<RetentionCategory> _categories = [];
    private string _totalText = "";
    private string _lastCleanup;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Prepara la ficha con los plazos guardados y lo que hay en la carpeta de datos.</summary>
    public DataModel(RetentionSettings current, DateTime? lastCleanupUtc, string? lastCleanupText)
    {
        _lastCleanup = lastCleanupUtc is { } at
            ? $"Última limpieza: {At(at.ToLocalTime())} · {lastCleanupText ?? "sin detalle"}"
            : "Todavía no se ha hecho ninguna limpieza.";
        Load(current);
    }

    /// <summary>Se dispara al elegir un plazo.</summary>
    public event Action? Chosen;

    private string _notice = "";

    /// <summary>Confirmación breve tras aplicar un plazo.</summary>
    public string Notice { get => _notice; set { _notice = value; OnChanged(nameof(Notice)); } }

    public IReadOnlyList<RetentionCategory> Categories { get => _categories; private set { _categories = value; OnChanged(nameof(Categories)); } }
    public string TotalText { get => _totalText; private set { _totalText = value; OnChanged(nameof(TotalText)); } }
    public string LastCleanup { get => _lastCleanup; private set { _lastCleanup = value; OnChanged(nameof(LastCleanup)); } }

    /// <summary>Plazos elegidos ahora mismo en la ficha.</summary>
    public RetentionSettings Selected() => new()
    {
        PromptsDays = _categories[0].Days,
        PromptTextDays = _categories[1].Days,
        LimitsDays = _categories[2].Days,
        VulnerabilitiesDays = _categories[3].Days,
    };

    /// <summary>Tras guardar o limpiar, relee el inventario conservando los plazos elegidos; si se limpió, lo apunta.</summary>
    public void Refresh(CleanupResult? result, bool cleaned)
    {
        Load(Selected());
        if (cleaned)
            LastCleanup = $"Última limpieza: {At(DateTime.Now)} · {(result is null ? "no había nada que borrar" : CleanupNotes.CleanupText(result))}";
    }

    /// <summary>Lee el inventario y monta las categorías con el plazo indicado marcado.</summary>
    private void Load(RetentionSettings marked)
    {
        var inventory = new DataRetention().Inventory();
        TotalText = "Ocupa " + CleanupNotes.Size(inventory.TotalBytes);
        Categories =
        [
            RetentionCategory.Of("prompts", "Histórico de prompts", Detail(inventory.Prompts.Since, inventory.Prompts.Bytes), LongTerms,
                marked.PromptsDays, OnChosen),
            RetentionCategory.Of("text", "Texto de los prompts", inventory.PromptTextSince is { } t ? "desde el " + Day(t) : "todavía no hay",
                TextTerms, marked.PromptTextDays, OnChosen),
            RetentionCategory.Of("limits", "Evolución de los límites", Detail(inventory.Limits.Since, inventory.Limits.Bytes), LongTerms,
                marked.LimitsDays, OnChosen),
            RetentionCategory.Of("vulnerabilities", "Historial de vulnerabilidades", Detail(inventory.Vulnerabilities.Since, inventory.Vulnerabilities.Bytes),
                LongTerms, marked.VulnerabilitiesDays, OnChosen),
        ];
    }

    /// <summary>Avisa de que la persona ha elegido un plazo.</summary>
    private void OnChosen() => Chosen?.Invoke();

    /// <summary>«desde el 29 de septiembre · 191 KB», o que aún no hay datos.</summary>
    private static string Detail(DateTime? since, long bytes) => since is { } d ? $"desde el {Day(d)} · {CleanupNotes.Size(bytes)}" : "todavía no hay";

    private static string Day(DateTime d) => d.ToString("d 'de' MMMM", Es);

    private static string At(DateTime local) => local.Date == DateTime.Today ? "hoy " + local.ToString("HH:mm", Es) : local.ToString("d MMM, HH:mm", Es).Replace(".", "");

    /// <summary>Avisa a la vista de un cambio de propiedad.</summary>
    private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
