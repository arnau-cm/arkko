using System.ComponentModel;
using Avalonia.Controls;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Configuración → Seguridad: los avisos de secretos, las vulnerabilidades y qué tipos de fichero no puede leer
/// Claude Code, con un interruptor cada uno.</summary>
/// <remarks>
/// Cada interruptor escribe el settings.json de Claude Code en el momento, con copia previa. Solo se tocan
/// las reglas de la lista: las que la persona puso a mano se quedan.
/// </remarks>
public partial class DenyRulesView : UserControl
{
    private readonly DenyRulesModel _model = new();

    /// <summary>Crea la vista; se llena con Reload al entrar en la configuración.</summary>
    public DenyRulesView()
    {
        InitializeComponent();
        DataContext = _model;
        _model.Toggled += Save;
        Reload();
    }

    /// <summary>Conecta los avisos de secretos y las vulnerabilidades con los ajustes de la app, que se guardan en cuanto cambian.</summary>
    /// <remarks>Los ajustes son los de la ventana flotante: guardarlos aparte se pisaría con lo que guarda ella.</remarks>
    public void Init(AppSettings settings, Action save) => _model.UseSettings(settings, save);

    /// <summary>La persona ha dejado o ha dejado de dejar a Arkko comprobar solo las vulnerabilidades.</summary>
    public event Action? StackAutoChanged
    {
        add => _model.StackAutoChanged += value;
        remove => _model.StackAutoChanged -= value;
    }

    /// <summary>Lee las reglas que hay ahora en settings.json y pone cada interruptor según estén; si falta una de las fijas de la app, la añade.</summary>
    /// <remarks>
    /// Las fijas las pone ak install, pero una instalación anterior a alguna de ellas no la tiene, y
    /// actualizar la app no toca settings.json: sin esto, la regla nueva no llegaría nunca.
    /// </remarks>
    public void Reload() => Load(repair: true);

    /// <summary>Lee las reglas y pone los interruptores; con <paramref name="repair"/>, añade las fijas que falten.</summary>
    /// <remarks>Tras un fallo al guardar se lee sin reparar: si no, volvería a intentarlo sin fin.</remarks>
    private void Load(bool repair)
    {
        try
        {
            var present = SettingsInstaller.DenyRules(SettingsInstaller.Load(AppPaths.ClaudeSettingsFile));
            _model.Show(present);
            _model.Notice = "";
            if (repair && _model.Rows.Where(r => !r.CanChange).SelectMany(r => r.Rules).Any(rule => !present.Contains(rule))) Save();
        }
        catch (Exception ex)
        {
            JsonStore.LogError("deny-rules", ex);
            _model.Show([]);
            _model.Notice = "No se ha podido leer settings.json; el detalle está en errors.log.";
        }
    }

    /// <summary>Escribe en settings.json las reglas de los tipos encendidos y quita las de los apagados.</summary>
    /// <remarks>Si no se puede escribir, avisa y vuelve a leer, para que los interruptores no enseñen algo que no está.</remarks>
    private void Save()
    {
        try
        {
            var path = AppPaths.ClaudeSettingsFile;
            var settings = SettingsInstaller.Load(path);
            var before = SettingsInstaller.DenyRules(settings);
            SettingsInstaller.AddDenyRules(settings, _model.Rows.Where(r => r.IsOn).SelectMany(r => r.Rules));
            SettingsInstaller.RemoveDenyRules(settings, _model.Rows.Where(r => !r.IsOn).SelectMany(r => r.Rules));
            if (before.SequenceEqual(SettingsInstaller.DenyRules(settings))) return;
            SettingsInstaller.Save(path, settings);
            _model.Notice = "Guardado en settings.json. Las sesiones abiertas lo aplican al reiniciarse.";
        }
        catch (Exception ex)
        {
            JsonStore.LogError("deny-rules", ex);
            _ = AppDialog.ShowAsync(TopLevel.GetTopLevel(this) as Window, "No se ha podido guardar", ex.Message,
                note: "No se ha cambiado nada. El detalle está en errors.log.");
            Load(repair: false);
        }
    }
}

/// <summary>Un tipo de fichero de la lista, con sus reglas y si está activo.</summary>
/// <param name="locked">Una regla de los ficheros de la propia app: siempre activa.</param>
public sealed class DenyRow(string name, string why, string[] rules, bool isOn, bool locked) : INotifyPropertyChanged
{
    private bool _isOn = isOn;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; } = name;
    public string Why { get; } = why;
    public string[] Rules { get; } = rules;
    public string RulesText => string.Join("   ", Rules);
    public bool CanChange { get; } = !locked;
    public string SwitchTip => CanChange ? "Encendido: Claude no puede leer estos ficheros" : "Siempre activa: la pone Arkko para proteger sus propios ficheros";

    /// <summary>Se dispara cuando la persona cambia el interruptor.</summary>
    public event Action? Toggled;

    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (_isOn == value) return;
            _isOn = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsOn)));
            Toggled?.Invoke();
        }
    }
}

/// <summary>Estado de la vista: los tipos de fichero y el aviso tras guardar.</summary>
public sealed class DenyRulesModel : INotifyPropertyChanged
{
    private IReadOnlyList<DenyRow> _rows = [];
    private string _notice = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<DenyRow> Rows { get => _rows; private set { _rows = value; OnChanged(nameof(Rows)); } }
    public string Notice { get => _notice; set { _notice = value; OnChanged(nameof(Notice)); } }

    /// <summary>Se dispara cuando la persona cambia el interruptor de cualquier fila.</summary>
    public event Action? Toggled;

    private AppSettings? _settings;
    private Action? _save;

    /// <summary>Recibe los ajustes de la app y su forma de guardarlos, para los avisos de secretos.</summary>
    public void UseSettings(AppSettings settings, Action save)
    {
        (_settings, _save) = (settings, save);
        OnChanged(nameof(AlertsOn));
        OnChanged(nameof(BlockOn));
        OnChanged(nameof(StackAutoOn));
        OnChanged(nameof(StackAlertsOn));
    }

    /// <summary>Se dispara al cambiar «Comprobar solo», para mirar en el momento si toca comprobar.</summary>
    public event Action? StackAutoChanged;

    /// <summary>Dejar que Arkko lance solo la comprobación de vulnerabilidades; apagado por defecto.</summary>
    public bool StackAutoOn
    {
        get => _settings?.StackAutoCheck ?? false;
        set
        {
            if (_settings is null || _settings.StackAutoCheck == value) return;
            _settings.StackAutoCheck = value;
            _save?.Invoke();
            OnChanged(nameof(StackAutoOn));
            StackAutoChanged?.Invoke();
        }
    }

    /// <summary>Avisar de lo nuevo en las vulnerabilidades; encendido por defecto.</summary>
    public bool StackAlertsOn
    {
        get => _settings?.StackAlerts ?? true;
        set
        {
            if (_settings is null || _settings.StackAlerts == value) return;
            _settings.StackAlerts = value;
            _save?.Invoke();
            OnChanged(nameof(StackAlertsOn));
        }
    }

    /// <summary>Avisar en el momento de un secreto enviado; lo lee el hook en cada evento.</summary>
    public bool AlertsOn
    {
        get => _settings?.SecretAlerts ?? true;
        set
        {
            if (_settings is null || _settings.SecretAlerts == value) return;
            _settings.SecretAlerts = value;
            _save?.Invoke();
            OnChanged(nameof(AlertsOn));
        }
    }

    /// <summary>Frenar los prompts que llevan un secreto; desactivado por defecto.</summary>
    public bool BlockOn
    {
        get => _settings?.BlockSecretPrompts ?? false;
        set
        {
            if (_settings is null || _settings.BlockSecretPrompts == value) return;
            _settings.BlockSecretPrompts = value;
            _save?.Invoke();
            OnChanged(nameof(BlockOn));
        }
    }

    /// <summary>Monta una fila por tipo según las reglas presentes; las de los ficheros de la app, primero y fijas.</summary>
    /// <remarks>Un tipo con varias reglas sale encendido solo si las tiene todas; al guardarlo, se completan.</remarks>
    public void Show(IReadOnlyCollection<string> present)
    {
        DenyRow[] own =
        [
            new("Hallazgos de Arkko", "Los secretos que ha encontrado esta app, cifrados",
                [SettingsInstaller.SecurityDenyRule(AppPaths.Security)], isOn: true, locked: true),
            new("Copias de settings.json de Arkko", "Las que guarda la app antes de cambiarlo, con las claves que tuviera; restaurar una solo pide copiarla",
                [SettingsInstaller.BackupsDenyRule(AppPaths.Backups)], isOn: true, locked: true),
        ];
        Rows = own.Concat(SettingsInstaller.DenyGroups.Select(g => new DenyRow(g.Name, g.Why, g.Rules, g.Rules.All(present.Contains), locked: false)))
            .ToList();
        foreach (var row in Rows) row.Toggled += () => Toggled?.Invoke();
    }

    /// <summary>Avisa a la vista de un cambio de propiedad.</summary>
    private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
