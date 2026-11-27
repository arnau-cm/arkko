using Avalonia;
using Avalonia.Media;

namespace Arkko.Desktop;

/// <summary>Un diseño de la pastilla: sus colores, su forma y su letra, fijos, sin seguir al tema de la app.</summary>
/// <param name="Prompt">Texto que va en lugar del logo, como el aviso de una terminal; null, el logo.</param>
public sealed record PillDesign(
    string Key, string Name, string Description,
    IBrush Back, IBrush Edge, IBrush Text, IBrush Muted, IBrush Track,
    double Radius, bool ShowLogo, bool ShowLimit, string? Prompt, bool Mono)
{
    public CornerRadius Corner => new(Radius);
    public bool HasPrompt => Prompt is not null;

    /// <summary>Esquinas del panel: las de la ventana en los diseños redondeados y las de la pastilla en los rectos.</summary>
    public CornerRadius PanelCorner => new(Radius >= 10 ? 16 : Radius);

    /// <summary>Letra del diseño: la de la app o, en los de terminal, una de ancho fijo.</summary>
    public FontFamily Font => Mono ? PillDesigns.MonoFont : PillDesigns.UiFont;

    /// <summary>Color de la negrita y de las etiquetas; sin él, el del texto.</summary>
    public IBrush? Strong { get; init; }

    /// <summary>Color de los títulos y las cifras grandes; sin él, el del texto.</summary>
    public IBrush? Title { get; init; }

    /// <summary>Si el diseño pinta el texto en varias intensidades, como el de terminal: entonces su panel lleva la clase «terminal».</summary>
    public bool Tiered => Strong is not null;
}

/// <summary>Los diseños de la pastilla que se pueden elegir en Configuración → Pastilla.</summary>
public static class PillDesigns
{
    public const string DefaultKey = "glass-dark";

    internal static readonly FontFamily UiFont = new("Segoe UI, Inter");
    internal static readonly FontFamily MonoFont = new("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace");

    /// <summary>Pincel de un color en hexadecimal, con transparencia opcional.</summary>
    private static SolidColorBrush Solid(string hex, double opacity = 1) => new(Color.Parse(hex), opacity);

    /// <summary>Degradado de arriba abajo entre dos colores, para los de cristal.</summary>
    private static LinearGradientBrush Glass(string top, string bottom) => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = [new GradientStop(Color.Parse(top), 0), new GradientStop(Color.Parse(bottom), 1)],
    };

    public static IReadOnlyList<PillDesign> All { get; } =
    [
        new(DefaultKey, "Cristal oscuro", "La de siempre: cristal oscuro translúcido.",
            Glass("#E62A2D35", "#E61B1D22"), Solid("#FFFFFF", 0.18), Solid("#E6E8EB"), Solid("#8B919A"), Solid("#FFFFFF", 0.10),
            19, true, true, null, false),
        new("glass-light", "Cristal claro", "Cristal claro translúcido, con el texto oscuro.",
            Glass("#F2FFFFFF", "#E6EEF0F4"), Solid("#000000", 0.14), Solid("#1F2328"), Solid("#5F6670"), Solid("#000000", 0.08),
            19, true, true, null, false),
        new("solid", "Sólida", "Negra y opaca: destaca sobre cualquier fondo.",
            Solid("#0E0F12"), Solid("#2A2D33"), Solid("#F2F3F5"), Solid("#9097A0"), Solid("#FFFFFF", 0.10),
            19, true, true, null, false),
        new("brand", "Granate", "Con el color de la app de fondo y el contenido en blanco.",
            Glass("#9A2A38", "#6E1B27"), Solid("#C24A5A"), Solid("#FFFFFF"), Solid("#F2C9CE"), Solid("#FFFFFF", 0.18),
            19, true, true, null, false),
        new("terminal", "Terminal", "Los colores de una pantalla de AS/400 sobre negro.",
            Solid("#070B07"), Solid("#3FA23F"), Solid("#6EE36E"), Solid("#5DBA5D"), Solid("#142414"),
            2, false, true, "AP>", true)
        {
            Strong = Solid("#6FAEF0"),
            Title = Solid("#E8E8E8"),
        },
    ];

    /// <summary>Diseño de una clave; uno que no se conoce es el de serie.</summary>
    public static PillDesign Find(string? key) => All.FirstOrDefault(d => d.Key == key) ?? All[0];
}

/// <summary>Una muestra de diseño en Configuración → Pastilla: «Como la app» (sin clave) o un diseño; marcarla lo elige.</summary>
public sealed class PillDesignOption(string? key, MainViewModel model) : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    /// <summary>El diseño que se ve en la muestra: el suyo o, en «Como la app», el de la app.</summary>
    public PillDesign Design => PillDesigns.Find(key ?? model.Design);

    public string Name => key is null ? "Como la app" : Design.Name;

    public string Description => key is null ? $"Sigue el diseño de la app; ahora, {Design.Name}." : Design.Description;

    public bool IsSelected
    {
        get => model.PillDesignKey == key;
        set { if (value) model.SetPillDesign(key); }
    }

    /// <summary>Avisa a la vista de que han cambiado el elegido o el diseño de la app, que se ve en «Como la app».</summary>
    public void Refresh()
    {
        foreach (var name in new[] { nameof(IsSelected), nameof(Design), nameof(Description) })
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    }
}

/// <summary>Una muestra de diseño en Configuración → General: el diseño y si es el de la app; marcarla lo pone a toda la app.</summary>
public sealed class AppDesignOption(PillDesign design, MainViewModel model) : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public PillDesign Design { get; } = design;

    public bool IsSelected
    {
        get => model.Design == Design.Key;
        set { if (value) model.Design = Design.Key; }
    }

    /// <summary>Avisa a la vista de que el diseño elegido ha cambiado.</summary>
    public void Refresh() => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
}
