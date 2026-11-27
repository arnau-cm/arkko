using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace Arkko.Desktop;

/// <summary>El tema activo de la app como lo entiende una página web: sus colores como variables CSS, su diseño y si es
/// claro; el mismo en la sección Sesiones y en las ventanas aparte que cargan su página.</summary>
internal static class PageTheme
{
    /// <summary>Letras de la página de Sesiones: la de siempre y la de ancho fijo del diseño de terminal.</summary>
    private const string UiCss = "\"Segoe UI Variable Text\", \"Segoe UI\", \"Segoe UI Emoji\", sans-serif";
    private const string MonoCss = "\"Cascadia Mono\", Consolas, Menlo, \"DejaVu Sans Mono\", monospace";

    /// <summary>El mensaje «theme» para la página: los colores del tema activo como variables CSS, el diseño y si es claro.</summary>
    public static object Message()
    {
        string Var(string key, string fallback) => Css(Resource(key)) ?? fallback;
        var terminal = App.Design == "terminal";
        return new
        {
            type = "theme",
            light = App.Light,
            design = App.Design,
            vars = new Dictionary<string, string>
            {
                ["--page"] = Var("GlassBase", "#16181f"),
                ["--fg"] = Var("Fg", "#f2f3f7"),
                ["--fg-strong"] = Var("FgStrong", "#f2f3f7"),
                ["--fg-title"] = Var("FgTitle", "#f2f3f7"),
                ["--fg-muted"] = Var("FgMuted", "#b3b6c4"),
                ["--fg-faint"] = Var("FgFaint", "#8a8d9c"),
                ["--accent"] = Var("Accent", "#f09a76"),
                ["--lens"] = Var("LensFill", "rgba(255,255,255,0.05)"),
                ["--lens-edge"] = Solid(Resource("LensEdge")) ?? "rgba(255,255,255,0.12)",
                ["--button"] = Var("ButtonFill", "rgba(255,255,255,0.08)"),
                ["--button-hover"] = Var("ButtonHover", "rgba(255,255,255,0.2)"),
                ["--track"] = Var("TrackFill", "rgba(255,255,255,0.12)"),
                ["--track-edge"] = Var("TrackEdge", "rgba(255,255,255,0.15)"),
                ["--chip-on"] = Var("ChipOn", "rgba(163,20,29,0.4)"),
                ["--chip-on-edge"] = Var("ChipOnEdge", "#c2323c"),
                ["--danger"] = Var("Danger", "#e03e4c"),
                ["--tip"] = Var("TipFill", "#202028"),
                ["--tip-edge"] = Var("TipEdge", "rgba(255,255,255,0.25)"),
                ["--st-idle"] = Css(Brushes_.Idle) ?? "#8a8d9c",
                ["--st-working"] = Css(Brushes_.Working) ?? "#56b6c2",
                ["--st-waiting"] = Css(Brushes_.Waiting) ?? "#e5c07b",
                ["--st-done"] = Css(Brushes_.Done) ?? "#98c379",
                // La letra de la página: la de ancho fijo con el diseño de terminal, como el resto de la app.
                ["--ui-font"] = terminal ? MonoCss : UiCss,
                // Los colores de la persona y las skills: los de un AS/400 con el diseño de terminal. Claude, en naranja siempre.
                ["--c-user"] = terminal ? "#6EE36E" : "#3fb950",
                ["--c-main"] = "#f0883e",
                ["--c-skill"] = terminal ? "#F2D46A" : "#e3b341",
                // Los comandos de consola, en lila como el modo «!» de Claude Code: no son prompts de la persona.
                ["--c-shell"] = terminal ? "#D99CF0" : "#c678dd",
                ["--on-actor"] = terminal ? "#070b07" : "#fff",
                ["--on-accent"] = terminal ? "#070b07" : "#fff",
            },
        };
    }

    /// <summary>Pincel del tema activo por su clave; null si no está.</summary>
    private static IBrush? Resource(string key) =>
        Application.Current?.TryGetResource(key, Application.Current.ActualThemeVariant, out var value) == true ? value as IBrush : null;

    /// <summary>Color de fondo de la página mientras carga, el del tema, para que no destelle en blanco.</summary>
    public static Color PageColor() => Resource("GlassBase") switch
    {
        ISolidColorBrush s => Color.FromRgb(s.Color.R, s.Color.G, s.Color.B),
        IGradientBrush { GradientStops.Count: > 0 } g => Color.FromRgb(g.GradientStops[0].Color.R, g.GradientStops[0].Color.G, g.GradientStops[0].Color.B),
        _ => Color.FromRgb(0x16, 0x18, 0x1f),
    };

    /// <summary>Un pincel como valor CSS: color, o degradado lineal; null si es de otro tipo.</summary>
    private static string? Css(IBrush? brush) => brush switch
    {
        ISolidColorBrush s => Rgba(s.Color, s.Opacity),
        ILinearGradientBrush g when g.GradientStops.Count > 0 =>
            $"linear-gradient({Angle(g.StartPoint.Point, g.EndPoint.Point).ToString("0", CultureInfo.InvariantCulture)}deg, " +
            string.Join(", ", g.GradientStops.Select(stop => $"{Rgba(stop.Color, g.Opacity)} {(stop.Offset * 100).ToString("0.#", CultureInfo.InvariantCulture)}%")) + ")",
        _ => null,
    };

    /// <summary>El primer color de un pincel, para los bordes, que en CSS no admiten degradados.</summary>
    private static string? Solid(IBrush? brush) => brush switch
    {
        ISolidColorBrush s => Rgba(s.Color, s.Opacity),
        IGradientBrush g when g.GradientStops.Count > 0 => Rgba(g.GradientStops[0].Color, g.Opacity),
        _ => null,
    };

    /// <summary>Un color como «rgba(r, g, b, a)».</summary>
    private static string Rgba(Color c, double opacity) =>
        $"rgba({c.R}, {c.G}, {c.B}, {(c.A / 255.0 * opacity).ToString("0.###", CultureInfo.InvariantCulture)})";

    /// <summary>Ángulo CSS de un degradado: 0 hacia arriba, 90 hacia la derecha, 180 hacia abajo.</summary>
    private static double Angle(Point start, Point end) =>
        (Math.Atan2(end.X - start.X, -(end.Y - start.Y)) * 180 / Math.PI + 360) % 360;
}
