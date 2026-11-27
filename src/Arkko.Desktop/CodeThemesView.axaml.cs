using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.VisualTree;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Configuración → Código: el tema del código de cada lenguaje, con su muestra y un menú para cambiarlo.</summary>
/// <remarks>Escribe donde el visor de Sesiones, en <see cref="CodeThemes"/>; los dos se ponen al día con su aviso.</remarks>
public partial class CodeThemesView : UserControl
{
    private readonly CodeThemesModel _model = new();

    /// <summary>Crea la vista con los lenguajes del catálogo.</summary>
    public CodeThemesView()
    {
        InitializeComponent();
        DataContext = _model;
        _model.Fill();
        CodeThemes.Changed += _model.Refresh;
    }

    /// <summary>Abre el menú de temas de un lenguaje bajo su botón, con el que tiene marcado.</summary>
    private void Theme_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: CodeLanguageRow row } button) return;
        _model.Open(row);
        ThemePopup.PlacementTarget = button;
        ThemePopup.IsOpen = true;
    }

    /// <summary>Pone el tema elegido al lenguaje del menú y lo cierra.</summary>
    private void Option_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not CodeThemeOption option || _model.Current is not { } row) return;
        ThemePopup.IsOpen = false;
        CodeThemes.Set(row.Id, option.Id);
    }

    /// <summary>Deshace lo elegido: cada lenguaje vuelve al tema con que venía.</summary>
    private void Reset_Click(object? sender, RoutedEventArgs e) => CodeThemes.Reset();

    /// <summary>Abre el menú del primer lenguaje y guarda su imagen, para el recorrido de imágenes.</summary>
    /// <remarks>El menú es una capa propia, fuera de la ventana: la imagen de la ventana no lo recoge.</remarks>
    internal async Task SnapshotMenuAsync()
    {
        if (this.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.DataContext is CodeLanguageRow) is not { } button) return;
        Theme_Click(button, new RoutedEventArgs());
        await Task.Delay(800);
        if (ThemePopup.Child is Visual menu) Snapshot.Save(menu, "tour-codemenu-popup");
    }
}

/// <summary>La muestra de un tema, en pinceles: su fondo, su borde y sus colores principales.</summary>
public sealed record CodeSampleLook(IBrush Background, IBrush Edge, IReadOnlyList<IBrush> Colors)
{
    /// <summary>La muestra de un tema del catálogo; gris si no la tiene.</summary>
    public static CodeSampleLook Of(CodeTheme? theme)
    {
        static IBrush Brush(string? hex) => HexColor.ToRgb(hex) is { } c ? new ImmutableSolidColorBrush(Color.FromRgb(c.R, c.G, c.B)) : Brushes.Gray;
        var sample = theme?.Sample;
        return new CodeSampleLook(Brush(sample?.Background), Brush(sample?.Edge), (sample?.Colors ?? []).Select(Brush).ToList());
    }
}

/// <summary>Los lenguajes con su tema y el menú de temas del que se está cambiando.</summary>
public sealed class CodeThemesModel : INotifyPropertyChanged
{
    public ObservableCollection<CodeLanguageRow> Languages { get; } = [];
    public ObservableCollection<CodeThemeOption> Options { get; } = [];

    /// <summary>El lenguaje cuyo menú está abierto.</summary>
    public CodeLanguageRow? Current { get; private set; }

    public string MenuTitle => Current is { } row ? $"Colores de {(row.Id == "other" ? "los demás lenguajes" : row.Name)}" : "";
    public string? MenuHint => Options.FirstOrDefault(o => o.IsCurrent)?.Hint;
    public bool MenuHintVisible => MenuHint is not null;

    /// <summary>Si se ha elegido el tema de algún lenguaje.</summary>
    public bool CanReset => CodeThemes.Chosen.Count > 0;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Pone un lenguaje por cada uno del catálogo.</summary>
    public void Fill()
    {
        Languages.Clear();
        foreach (var language in CodeThemes.Catalog.Languages) Languages.Add(new CodeLanguageRow(language));
        Refresh();
    }

    /// <summary>Prepara el menú de un lenguaje: cada tema con su muestra y el que tiene.</summary>
    public void Open(CodeLanguageRow row)
    {
        Current = row;
        Options.Clear();
        foreach (var theme in CodeThemes.Catalog.Themes)
            Options.Add(new CodeThemeOption(theme.Id, theme.Name, theme.Hint, CodeSampleLook.Of(theme), theme.Id == row.ThemeId));
        OnChanged(nameof(MenuTitle));
        OnChanged(nameof(MenuHint));
        OnChanged(nameof(MenuHintVisible));
    }

    /// <summary>Pone al día el tema de cada lenguaje, tras un cambio desde aquí o desde el visor.</summary>
    public void Refresh()
    {
        foreach (var row in Languages) row.Refresh();
        OnChanged(nameof(CanReset));
    }

    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Un tema en el menú: su nombre, su muestra y si es el que tiene el lenguaje.</summary>
public sealed record CodeThemeOption(string Id, string Name, string? Hint, CodeSampleLook Sample, bool IsCurrent);

/// <summary>Un lenguaje en Configuración: su nombre, sus extensiones y su tema, con su muestra.</summary>
public sealed class CodeLanguageRow(CodeLanguage language) : INotifyPropertyChanged
{
    public string Id => language.Id;
    public string Name => language.Name;
    public string DefaultTheme => language.DefaultTheme;

    /// <summary>Sus extensiones o, si no tiene, para qué sirve.</summary>
    public string Detail => language.Extensions.Count > 0
        ? string.Join(" · ", language.Extensions.Select(e => "." + e))
        : "Los ficheros de lenguajes que la app no conoce";

    public string ThemeId => CodeThemes.ThemeOf(language.Id);
    private CodeTheme? Theme => CodeThemes.Catalog.Themes.FirstOrDefault(t => t.Id == ThemeId);
    public string ThemeName => Theme?.Name ?? ThemeId;
    public CodeSampleLook Sample => CodeSampleLook.Of(Theme);

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Avisa de que su tema puede haber cambiado.</summary>
    public void Refresh()
    {
        foreach (var name in new[] { nameof(ThemeId), nameof(ThemeName), nameof(Sample) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
