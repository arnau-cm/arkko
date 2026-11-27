using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Ficha de Claude Code en Datos y retención: cuánto ocupa su carpeta y cuánto guarda los transcripts de sus sesiones.</summary>
/// <remarks>
/// Los plazos van al settings.json de Claude Code, y el borrado lo hace él en su limpieza automática.
/// Alargar un plazo se aplica al momento; acortarlo se confirma antes, porque una sesión borrada ya
/// no se puede retomar. Si se cancela, el plazo vuelve al que estaba.
/// </remarks>
public partial class ClaudeCodeRetentionView : UserControl
{
    private ClaudeCodeModel? _model;

    /// <summary>Crea la ficha vacía; se llena con Reload.</summary>
    public ClaudeCodeRetentionView() => InitializeComponent();

    private Window? Owner => TopLevel.GetTopLevel(this) as Window;

    /// <summary>Vuelve a leer los plazos guardados en el settings.json de Claude Code y a medir su carpeta.</summary>
    public void Reload()
    {
        _model = new ClaudeCodeModel();
        // Después del clic: el chip anterior ya se ha desmarcado y el diálogo no interrumpe el cambio.
        _model.Chosen += () => Dispatcher.UIThread.Post(Apply);
        DataContext = _model;
        Measure(_model);
    }

    /// <summary>Mide en segundo plano lo que ocupa la carpeta de Claude Code y lo pone en la ficha.</summary>
    private static async void Measure(ClaudeCodeModel model)
    {
        try
        {
            var bytes = await Task.Run(() => FolderSize(AppPaths.ClaudeConfigDir));
            model.SizeText = "Ocupa " + CleanupNotes.Size(bytes);
        }
        catch (Exception ex)
        {
            JsonStore.LogError("claude-folder-size", ex);
            model.SizeText = "";
        }
    }

    /// <summary>Suma lo que ocupan los ficheros de una carpeta y sus subcarpetas; sin carpeta, cero.</summary>
    /// <remarks>
    /// No entra en enlaces ni junctions: lo que cuelga de ellos vive en otra carpeta y no es de Claude
    /// Code. Se salta lo que no deja leer en vez de fallar.
    /// </remarks>
    private static long FolderSize(string path)
    {
        if (!Directory.Exists(path)) return 0;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        return new DirectoryInfo(path).EnumerateFiles("*", options).Sum(f => f.Length);
    }

    /// <summary>Abre la carpeta de Claude Code.</summary>
    private void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            Folders.Open(AppPaths.ClaudeConfigDir);
        }
        catch (Exception ex)
        {
            JsonStore.LogError("claude-folder", ex);
        }
    }

    /// <summary>Aplica el plazo recién elegido; si la persona cancela la confirmación, lo devuelve al que estaba.</summary>
    private async void Apply()
    {
        if (_model is not { } model) return;
        if (!await ApplyIfConfirmed())
        {
            Reload();
            return;
        }
        model.Load();
        model.Notice = "Guardado.";
    }

    /// <summary>Si han cambiado los plazos, los guarda en el settings.json de Claude Code; si se acortan, lo confirma antes.</summary>
    /// <returns>False si la persona cancela o no se puede guardar.</returns>
    private async Task<bool> ApplyIfConfirmed()
    {
        if (_model is null) return false;
        var saved = _model.Saved;
        var chosen = _model.Selected();
        var terminalChanged = (chosen.Terminal ?? ClaudeRetention.TerminalDefault) != (saved.Terminal ?? ClaudeRetention.TerminalDefault);
        var desktopChanged = chosen.Desktop != saved.Desktop;
        if (!terminalChanged && !desktopChanged) return true;

        var lowered = (terminalChanged && (chosen.Terminal ?? ClaudeRetention.TerminalDefault) < (saved.Terminal ?? ClaudeRetention.TerminalDefault)) ||
                      (desktopChanged && chosen.Desktop is { } d && (saved.Desktop is null || d < saved.Desktop));
        var changes = new List<string>();
        if (terminalChanged) changes.Add($"Sesiones de terminal: {TerminalText(chosen.Terminal)} (ahora {TerminalText(saved.Terminal)})");
        if (desktopChanged) changes.Add($"Sesiones de Desktop y Cowork: {DesktopText(chosen.Desktop)} (ahora {DesktopText(saved.Desktop)})");
        // Alargar el plazo no borra nada: se aplica sin preguntar.
        if (lowered && !await AppDialog.ConfirmAsync(Owner, "Acortar cuánto guarda Claude Code sus transcripts",
                "En su próxima limpieza automática, Claude Code borrará las sesiones que lleven más de ese plazo sin actividad, y ya no se podrán retomar. Los chats normales de Claude no se tocan: están en tu cuenta.",
                "Acortar", DialogTone.Warning, changes,
                $"Se cambia {AppPaths.ClaudeSettingsFile}, con copia previa en {AppPaths.Backups}.")) return false;
        try
        {
            var path = AppPaths.ClaudeSettingsFile;
            var settings = SettingsInstaller.Load(path);
            // Solo se escribe lo que ha cambiado: un plazo sin tocar sigue como estaba, con ajuste o sin él.
            ClaudeRetention.Apply(settings, new ClaudeRetentionDays(
                terminalChanged ? chosen.Terminal : saved.Terminal,
                desktopChanged ? chosen.Desktop : saved.Desktop));
            SettingsInstaller.Save(path, settings);
            return true;
        }
        catch (Exception ex)
        {
            JsonStore.LogError("claude-retention", ex);
            await AppDialog.ShowAsync(Owner, "No se ha podido cambiar el settings.json de Claude Code", ex.Message,
                note: "No se ha guardado nada. El detalle está en errors.log.");
            return false;
        }
    }

    /// <summary>Plazo de las sesiones de terminal en texto: «14 días», o el de Claude Code si no hay ajuste.</summary>
    private static string TerminalText(int? days) => days is { } d ? $"{d} días" : $"{ClaudeRetention.TerminalDefault} días, el de Claude Code";

    /// <summary>Plazo de las sesiones de Desktop y Cowork en texto: «14 días» o «sin límite».</summary>
    private static string DesktopText(int? days) => days is { } d ? $"{d} días" : "sin límite";
}

/// <summary>Estado de la ficha: los plazos de Claude Code, quién los fija y el aviso tras guardar.</summary>
public sealed class ClaudeCodeModel : INotifyPropertyChanged
{
    private static readonly (string Label, int? Days)[] TerminalTerms = [("7 días", 7), ("14 días", 14), ("30 días", 30), ("90 días", 90)];
    private static readonly (string Label, int? Days)[] DesktopTerms = [.. TerminalTerms, ("Sin límite", null)];
    private IReadOnlyList<RetentionCategory> _categories = [];
    private string _note = "";
    private string _notice = "";
    private string _sizeText = "Midiendo…";

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Se dispara al elegir un plazo.</summary>
    public event Action? Chosen;

    /// <summary>Prepara la ficha con los plazos que hay ahora en el settings.json de Claude Code.</summary>
    public ClaudeCodeModel() => Load();

    public IReadOnlyList<RetentionCategory> Categories { get => _categories; private set { _categories = value; OnChanged(nameof(Categories)); } }

    /// <summary>Aviso bajo los plazos: si alguno lo fija la empresa o no se puede leer el settings.json.</summary>
    public string Note
    {
        get => _note;
        private set
        {
            _note = value;
            OnChanged(nameof(Note));
            OnChanged(nameof(NoteVisible));
        }
    }
    public bool NoteVisible => _note.Length > 0;

    /// <summary>Confirmación breve tras aplicar un plazo.</summary>
    public string Notice { get => _notice; set { _notice = value; OnChanged(nameof(Notice)); } }

    /// <summary>Lo que ocupa la carpeta de Claude Code: «Ocupa 375 MB», o que aún se está midiendo.</summary>
    public string SizeText { get => _sizeText; set { _sizeText = value; OnChanged(nameof(SizeText)); } }

    /// <summary>Plazos que hay ahora en el settings.json de Claude Code.</summary>
    public ClaudeRetentionDays Saved { get; private set; } = new(null, null);

    /// <summary>Plazos elegidos en la ficha; los que no se pueden cambiar, tal como están guardados.</summary>
    public ClaudeRetentionDays Selected() => new(
        _categories[0].Enabled ? _categories[0].Days : Saved.Terminal,
        _categories[1].Enabled ? _categories[1].Days : Saved.Desktop);

    /// <summary>Lee los plazos del settings.json de Claude Code y de la configuración de la empresa, y monta sus filas.</summary>
    public void Load()
    {
        var readable = true;
        try
        {
            Saved = ClaudeRetention.Read(SettingsInstaller.Load(AppPaths.ClaudeSettingsFile));
        }
        catch (Exception ex)
        {
            JsonStore.LogError("claude-retention", ex);
            Saved = new(null, null);
            readable = false;
        }
        var managed = ClaudeRetention.Managed();
        Categories =
        [
            RetentionCategory.Of("claude-terminal", "Sesiones de terminal",
                managed.Terminal is null ? "Las que lleven más de este plazo sin actividad" : "Plazo fijado por la empresa",
                TerminalTerms, managed.Terminal ?? Saved.Terminal ?? ClaudeRetention.TerminalDefault, () => Chosen?.Invoke(),
                readable && managed.Terminal is null),
            RetentionCategory.Of("claude-desktop", "Sesiones de Desktop y Cowork",
                managed.Desktop is null ? "Code y Cowork; los chats normales no" : "Plazo fijado por la empresa",
                DesktopTerms, managed.Desktop ?? Saved.Desktop, () => Chosen?.Invoke(), readable && managed.Desktop is null),
        ];
        Note = !readable
            ? "No se puede leer el settings.json de Claude Code: revisa que sea un JSON válido. El detalle está en errors.log."
            : managed.Terminal is not null || managed.Desktop is not null
                ? "Algún plazo lo fija la configuración de la empresa, que manda sobre la tuya, y no se puede cambiar aquí."
                : "";
    }

    /// <summary>Avisa a la vista de un cambio de propiedad.</summary>
    private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
