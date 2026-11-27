using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;

namespace Arkko.Desktop;

/// <summary>Colores que el código toma del tema en vez de llevarlos escritos.</summary>
/// <remarks>
/// Las gráficas se pintan en código, así que no pueden usar DynamicResource. Aquí se busca la clave en el
/// tema activo, con un color de respaldo para que un recurso que falte no deje nada sin pintar. Cada color
/// resuelto se guarda por clave y tema: las filas comparan pinceles por referencia y uno nuevo en cada
/// repintado daría trabajo de más.
/// </remarks>
public static class ThemeColors
{
    private static readonly Dictionary<(string Key, string Design), IBrush> Cache = new();
    private static readonly Dictionary<(string Key, string Design, double Opacity), IBrush> Translucent = new();

    /// <summary>Pincel de una clave del tema activo; si no está, uno con el color de respaldo.</summary>
    public static IBrush Of(string key, string fallback)
    {
        var id = (key, App.Design);
        if (Cache.TryGetValue(id, out var cached)) return cached;
        var variant = App.VariantOf(App.Design);
        var brush = Application.Current is { } app && app.TryGetResource(key, variant, out var found) && found is IBrush resource
            ? resource
            : new ImmutableSolidColorBrush(Color.Parse(fallback));
        Cache[id] = brush;
        return brush;
    }

    /// <summary>El mismo color de una clave del tema, con transparencia: el fondo tenue de una pastilla o un icono.</summary>
    public static IBrush Of(string key, string fallback, double opacity)
    {
        var id = (key, App.Design, opacity);
        if (Translucent.TryGetValue(id, out var cached)) return cached;
        var color = Of(key, fallback) is ISolidColorBrush solid ? solid.Color : Color.Parse(fallback);
        var brush = new ImmutableSolidColorBrush(color, opacity);
        Translucent[id] = brush;
        return brush;
    }

    /// <summary>Color de una clave del tema activo, para dibujos que necesitan el color y no el pincel.</summary>
    public static Color ColorOf(string key, string fallback) =>
        Of(key, fallback) is ISolidColorBrush solid ? solid.Color : Color.Parse(fallback);

    /// <summary>Letra del tema activo, para el texto que se pinta en código: la de ancho fijo con el diseño de terminal.</summary>
    public static FontFamily Font =>
        Application.Current?.TryGetResource("UiFont", App.VariantOf(App.Design), out var font) == true && font is FontFamily family ? family : FontFamily.Default;
}
