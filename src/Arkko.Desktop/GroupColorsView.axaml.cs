using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Configuración → General: los colores del menú de los grupos de Sesiones, los de serie y los añadidos.</summary>
/// <remarks>Cada cambio se guarda al momento con los ajustes de la app y se avisa a Sesiones para que rehaga su menú.</remarks>
public partial class GroupColorsView : UserControl
{
    private readonly GroupColorsModel _model = new();
    private AppSettings? _settings;
    private Action _save = () => { };

    /// <summary>Se dispara al añadir o quitar un color, para que Sesiones lo vea sin reiniciar.</summary>
    public event Action? Changed;

    /// <summary>Crea la vista; se llena con Init.</summary>
    public GroupColorsView()
    {
        InitializeComponent();
        DataContext = _model;
        Picker.Chosen += Picker_Chosen;
    }

    /// <summary>Conecta la vista con los ajustes de la app, que se guardan en cuanto cambian.</summary>
    public void Init(AppSettings settings, Action save)
    {
        _settings = settings;
        _save = save;
        Show();
    }

    /// <summary>Pinta las muestras: las de serie, sin cruz, y las añadidas, con ella.</summary>
    private void Show()
    {
        if (_settings is not { } s) return;
        _model.Swatches.Clear();
        foreach (var hex in AppSettings.DefaultGroupPalette) _model.Swatches.Add(new GroupSwatch(hex, removable: false));
        foreach (var hex in s.GroupPalette) _model.Swatches.Add(new GroupSwatch(hex, removable: true));
        _model.Refresh();
    }

    /// <summary>Añade el color escrito al menú, si es válido y no estaba ya.</summary>
    private void Add()
    {
        if (_settings is not { } s || !_model.CanAdd) return;
        s.GroupPalette.Add(_model.Code.ToLowerInvariant());
        _save();
        _model.Code = "";
        _model.Notice = "Añadido. Ya sale en el menú de los grupos.";
        Show();
        Changed?.Invoke();
    }

    private void Add_Click(object? sender, RoutedEventArgs e) => Add();

    /// <summary>Intro en el campo del código añade el color, como el botón.</summary>
    private void Code_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        Add();
    }

    /// <summary>Quita un color añadido; los grupos que lo tenían lo conservan.</summary>
    private void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (_settings is not { } s || (sender as Control)?.DataContext is not GroupSwatch swatch) return;
        s.GroupPalette.RemoveAll(c => c.Equals(swatch.Hex, StringComparison.OrdinalIgnoreCase));
        _save();
        _model.Notice = "Quitado del menú. Los grupos que ya lo tenían lo conservan.";
        Show();
        Changed?.Invoke();
    }

    /// <summary>Abre el selector en el color del campo, si hay uno válido.</summary>
    private void Pick_Click(object? sender, RoutedEventArgs e)
    {
        Picker.SetColor(_model.Code);
        PickerPopup.IsOpen = true;
    }

    /// <summary>Deja en el campo el color del selector, listo para añadir, y lo cierra.</summary>
    private void Picker_Chosen(string hex)
    {
        _model.Code = hex;
        PickerPopup.IsOpen = false;
    }
}

/// <summary>Una muestra del menú de colores.</summary>
public sealed class GroupSwatch(string hex, bool removable)
{
    public string Hex { get; } = hex;
    public IBrush Brush { get; } = new ImmutableSolidColorBrush(GroupColorsModel.Parse(hex) ?? Colors.Transparent);
    public bool RemoveVisible { get; } = removable;
}

/// <summary>Estado de la vista de colores: las muestras, el código que se escribe y su vista previa.</summary>
public sealed class GroupColorsModel : INotifyPropertyChanged
{
    private string _code = "";
    private string _notice = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<GroupSwatch> Swatches { get; } = [];

    /// <summary>Código del color que se va a añadir, como «#3fb950».</summary>
    public string Code
    {
        get => _code;
        set
        {
            _code = (value ?? "").Trim();
            OnChanged();
            Refresh();
        }
    }

    public string Notice
    {
        get => _notice;
        set
        {
            _notice = value;
            OnChanged();
        }
    }

    /// <summary>El color escrito, mientras sea válido; transparente si no.</summary>
    public IBrush Preview => new ImmutableSolidColorBrush(Parse(_code) ?? Colors.Transparent);

    /// <summary>Si el código es un color válido que aún no está en el menú.</summary>
    public bool CanAdd => Parse(_code) is not null && Swatches.All(s => !s.Hex.Equals(_code, StringComparison.OrdinalIgnoreCase));

    /// <summary>Avisa de que la vista previa y el botón de añadir pueden haber cambiado.</summary>
    public void Refresh()
    {
        OnChanged(nameof(Preview));
        OnChanged(nameof(CanAdd));
    }

    /// <summary>Color de un código «#rrggbb»; null si no lo es.</summary>
    public static Color? Parse(string text) => HexColor.ToRgb(text) is { } c ? Color.FromRgb(c.R, c.G, c.B) : null;

    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
