using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Arkko.Desktop;

/// <summary>Pinceles de estado, inmutables para compartirlos sin coste; los mismos colores que la ventana de WPF.</summary>
public static class Brushes_
{
    private static readonly Palette DarkPalette = new()
    {
        Idle = Make(0xA7, 0xAB, 0xB8),
        Working = Make(0x7A, 0xB8, 0xFF),
        Waiting = Make(0xFF, 0xC8, 0x57),
        Done = Make(0x6B, 0xE3, 0xA4),
        // High es el Danger de cada tema, en sus dos paletas: si cambia uno, cambia el otro.
        High = Make(0xE0, 0x3E, 0x4C),
        EffortOff = Make(0xFF, 0xFF, 0xFF, 0x26),
        EffortLow = Make(0xA5, 0xB4, 0xFC),
        EffortMedium = Make(0xA5, 0xB4, 0xFC),
        EffortHigh = Make(0xA5, 0xB4, 0xFC),
        EffortXHigh = Make(0xA5, 0xB4, 0xFC),
        EffortMax = Make(0xA5, 0xB4, 0xFC),
        WorkingEdge = Make(0x7A, 0xB8, 0xFF, 0xB0),
        WorkingFill = Tint(Color.FromArgb(0x1F, 0x7A, 0xB8, 0xFF), Color.FromArgb(0x08, 0x7A, 0xB8, 0xFF)),
        WaitWarnEdge = Make(0xFF, 0xC8, 0x57, 0xB0),
        WaitHighEdge = Make(0xE0, 0x3E, 0x4C, 0xD0),
        DoneEdge = Make(0x6B, 0xE3, 0xA4, 0xB0),
        WaitingCardFill = Tint(Color.FromArgb(0x14, 0xFF, 0xD2, 0x6B), Color.FromArgb(0x05, 0xFF, 0xD2, 0x6B)),
        HighCardFill = Tint(Color.FromArgb(0x1A, 0xE0, 0x3E, 0x4C), Color.FromArgb(0x06, 0xE0, 0x3E, 0x4C)),
        DoneCardFill = Tint(Color.FromArgb(0x14, 0x6B, 0xE3, 0xB4), Color.FromArgb(0x05, 0x6B, 0xE3, 0xB4)),
        IdleTint = Make(0xA7, 0xAB, 0xB8, 0x1F),
        WorkingTint = Make(0x7A, 0xB8, 0xFF, 0x29),
        WaitingTint = Make(0xFF, 0xC8, 0x57, 0x29),
        DoneTint = Make(0x6B, 0xE3, 0xA4, 0x26),
        // Violeta: lo que espera a sus agentes, que no se confunde con ninguno de los otros cuatro.
        Agents = Make(0xB7, 0x9C, 0xFF),
        AgentsEdge = Make(0xB7, 0x9C, 0xFF, 0xB0),
        AgentsCardFill = Tint(Color.FromArgb(0x1A, 0xB7, 0x9C, 0xFF), Color.FromArgb(0x06, 0xB7, 0x9C, 0xFF)),
        AgentsTint = Make(0xB7, 0x9C, 0xFF, 0x29),
        // Cian, como la shell de Claude Code: lo que espera a un comando en marcha; lejos del azul de lo que trabaja.
        Command = Make(0x22, 0xD3, 0xEE),
        CommandEdge = Make(0x22, 0xD3, 0xEE, 0xB0),
        CommandCardFill = Tint(Color.FromArgb(0x1A, 0x22, 0xD3, 0xEE), Color.FromArgb(0x06, 0x22, 0xD3, 0xEE)),
        CommandTint = Make(0x22, 0xD3, 0xEE, 0x29),
    };

    // En claro, los mismos tonos más oscuros y saturados: sobre blanco, los pasteles no se leen.
    private static readonly Palette LightPalette = new()
    {
        Idle = Make(0x6B, 0x70, 0x80),
        Working = Make(0x25, 0x6B, 0xD8),
        Waiting = Make(0xB8, 0x74, 0x0C),
        Done = Make(0x16, 0x94, 0x5C),
        High = Make(0xD2, 0x38, 0x3E),
        EffortOff = Make(0x00, 0x00, 0x00, 0x1F),
        EffortLow = Make(0x4F, 0x46, 0xE5),
        EffortMedium = Make(0x4F, 0x46, 0xE5),
        EffortHigh = Make(0x4F, 0x46, 0xE5),
        EffortXHigh = Make(0x4F, 0x46, 0xE5),
        EffortMax = Make(0x4F, 0x46, 0xE5),
        WorkingEdge = Make(0x25, 0x6B, 0xD8, 0x90),
        WorkingFill = Tint(Color.FromArgb(0xF2, 0xDA, 0xE7, 0xFB), Color.FromArgb(0xE6, 0xE9, 0xF0, 0xFB)),
        WaitWarnEdge = Make(0xE0, 0x96, 0x1A, 0xB0),
        WaitHighEdge = Make(0xD2, 0x38, 0x3E, 0xC0),
        DoneEdge = Make(0x16, 0x94, 0x5C, 0x90),
        WaitingCardFill = Tint(Color.FromArgb(0xF2, 0xFC, 0xEE, 0xD4), Color.FromArgb(0xE6, 0xFB, 0xF4, 0xE6)),
        HighCardFill = Tint(Color.FromArgb(0xF2, 0xFC, 0xE1, 0xE1), Color.FromArgb(0xE6, 0xFB, 0xEE, 0xEE)),
        DoneCardFill = Tint(Color.FromArgb(0xF2, 0xDD, 0xF4, 0xE7), Color.FromArgb(0xE6, 0xEC, 0xF8, 0xF1)),
        IdleTint = Make(0x6B, 0x70, 0x80, 0x1A),
        WorkingTint = Make(0x25, 0x6B, 0xD8, 0x1F),
        WaitingTint = Make(0xE0, 0x96, 0x1A, 0x24),
        DoneTint = Make(0x16, 0x94, 0x5C, 0x1F),
        Agents = Make(0x70, 0x48, 0xD6),
        AgentsEdge = Make(0x70, 0x48, 0xD6, 0x90),
        AgentsCardFill = Tint(Color.FromArgb(0xF2, 0xEC, 0xE6, 0xFB), Color.FromArgb(0xE6, 0xF4, 0xF0, 0xFC)),
        AgentsTint = Make(0x70, 0x48, 0xD6, 0x1F),
        Command = Make(0x0E, 0x74, 0x90),
        CommandEdge = Make(0x0E, 0x74, 0x90, 0x90),
        CommandCardFill = Tint(Color.FromArgb(0xF2, 0xE0, 0xF6, 0xFA), Color.FromArgb(0xE6, 0xEE, 0xFA, 0xFC)),
        CommandTint = Make(0x0E, 0x74, 0x90, 0x1F),
    };

    // En terminal, los colores de una pantalla de AS/400 (IBM 5250), con el reparto de los otros temas: azul lo que
    // trabaja, verde lo hecho, gris lo que reposa, amarillo lo que espera y rojo lo urgente. Tonos algo apagados: se lee
    // mucho rato.
    private static readonly Palette TerminalPalette = new()
    {
        Idle = Make(0x9A, 0x9A, 0x9A),
        Working = Make(0x6F, 0xAE, 0xF0),
        Waiting = Make(0xF2, 0xD4, 0x6A),
        Done = Make(0x8A, 0xE8, 0x8A),
        High = Make(0xD6, 0x45, 0x45),
        EffortOff = Make(0xE8, 0xE8, 0xE8, 0x26),
        EffortLow = Make(0x6E, 0xD6, 0xD6),
        EffortMedium = Make(0x6E, 0xD6, 0xD6),
        EffortHigh = Make(0x6E, 0xD6, 0xD6),
        EffortXHigh = Make(0x6E, 0xD6, 0xD6),
        EffortMax = Make(0x6E, 0xD6, 0xD6),
        WorkingEdge = Make(0x6F, 0xAE, 0xF0, 0xC0),
        WorkingFill = Tint(Color.FromArgb(0x24, 0x6F, 0xAE, 0xF0), Color.FromArgb(0x08, 0x6F, 0xAE, 0xF0)),
        WaitWarnEdge = Make(0xF2, 0xD4, 0x6A, 0xC0),
        WaitHighEdge = Make(0xD6, 0x45, 0x45, 0xD0),
        DoneEdge = Make(0x8A, 0xE8, 0x8A, 0xB0),
        WaitingCardFill = Tint(Color.FromArgb(0x1A, 0xF2, 0xD4, 0x6A), Color.FromArgb(0x05, 0xF2, 0xD4, 0x6A)),
        HighCardFill = Tint(Color.FromArgb(0x1F, 0xD6, 0x45, 0x45), Color.FromArgb(0x06, 0xD6, 0x45, 0x45)),
        DoneCardFill = Tint(Color.FromArgb(0x16, 0x8A, 0xE8, 0x8A), Color.FromArgb(0x05, 0x8A, 0xE8, 0x8A)),
        IdleTint = Make(0x9A, 0x9A, 0x9A, 0x22),
        WorkingTint = Make(0x6F, 0xAE, 0xF0, 0x29),
        WaitingTint = Make(0xF2, 0xD4, 0x6A, 0x29),
        DoneTint = Make(0x8A, 0xE8, 0x8A, 0x26),
        // El rosa de la pantalla 5250, para lo que espera a sus agentes.
        Agents = Make(0xE0, 0x8C, 0xE8),
        AgentsEdge = Make(0xE0, 0x8C, 0xE8, 0xC0),
        AgentsCardFill = Tint(Color.FromArgb(0x1F, 0xE0, 0x8C, 0xE8), Color.FromArgb(0x06, 0xE0, 0x8C, 0xE8)),
        AgentsTint = Make(0xE0, 0x8C, 0xE8, 0x29),
        // El turquesa de la pantalla 5250, para lo que espera a un comando.
        Command = Make(0x4F, 0xE0, 0xE6),
        CommandEdge = Make(0x4F, 0xE0, 0xE6, 0xC0),
        CommandCardFill = Tint(Color.FromArgb(0x1F, 0x4F, 0xE0, 0xE6), Color.FromArgb(0x06, 0x4F, 0xE0, 0xE6)),
        CommandTint = Make(0x4F, 0xE0, 0xE6, 0x29),
    };

    private static Palette Current => App.Design == "terminal" ? TerminalPalette : App.Light ? LightPalette : DarkPalette;

    public static IBrush Idle => Current.Idle;
    public static IBrush Working => Current.Working;
    public static IBrush Waiting => Current.Waiting;
    public static IBrush Done => Current.Done;
    public static IBrush High => Current.High;
    public static IBrush EffortOff => Current.EffortOff;
    public static IBrush EffortLow => Current.EffortLow;
    public static IBrush EffortMedium => Current.EffortMedium;
    public static IBrush EffortHigh => Current.EffortHigh;
    public static IBrush EffortXHigh => Current.EffortXHigh;
    public static IBrush EffortMax => Current.EffortMax;
    public static IBrush WorkingEdge => Current.WorkingEdge;
    public static IBrush WorkingFill => Current.WorkingFill;
    public static IBrush WaitWarnEdge => Current.WaitWarnEdge;
    public static IBrush WaitHighEdge => Current.WaitHighEdge;
    public static IBrush DoneEdge => Current.DoneEdge;
    public static IBrush WaitingCardFill => Current.WaitingCardFill;
    public static IBrush HighCardFill => Current.HighCardFill;
    public static IBrush DoneCardFill => Current.DoneCardFill;
    public static IBrush IdleTint => Current.IdleTint;
    public static IBrush WorkingTint => Current.WorkingTint;
    public static IBrush WaitingTint => Current.WaitingTint;
    public static IBrush DoneTint => Current.DoneTint;
    public static IBrush Agents => Current.Agents;
    public static IBrush AgentsEdge => Current.AgentsEdge;
    public static IBrush AgentsCardFill => Current.AgentsCardFill;
    public static IBrush AgentsTint => Current.AgentsTint;
    public static IBrush Command => Current.Command;
    public static IBrush CommandEdge => Current.CommandEdge;
    public static IBrush CommandCardFill => Current.CommandCardFill;
    public static IBrush CommandTint => Current.CommandTint;

    // ESCALA CONTINUA DE USO: se recorre el tono (verde, amarillo, naranja, rojo) con saturación y brillo
    // fijos. Mezclar en RGB entre esos colores deja intermedios grises o marrones.
    private static readonly (double At, double Hue)[] HueStops =
    [
        (0, 130),
        (45, 58),
        (70, 32),
        (100, -4),
    ];

    // Un pincel por tramo de 5 % y forma: las filas comparan pinceles por referencia y un pincel nuevo en
    // cada refresco obligaría a repintar la ventana cada segundo.
    private static readonly Dictionary<(bool Ring, int Bucket), IBrush> ScaleCache = new();

    /// <summary>Pincel de la escala de uso para un porcentaje: degradado diagonal (anillo) u horizontal (barra).</summary>
    public static IBrush Scale(double pct, bool ring)
    {
        var bucket = (int)Math.Round(Math.Clamp(pct, 0, 100) / 5) * 5;
        var key = (ring, bucket);
        if (ScaleCache.TryGetValue(key, out var cached)) return cached;

        // Mismos colores en los dos temas; las barras, algo más apagadas que los anillos: son más largas y pesan más.
        var color = FromHsv(HueAt(bucket), 0.82, ring ? 0.80 : 0.70);
        var highlight = Blend(color, Colors.White, ring ? 0.25 : 0.12);
        var brush = ring ? Gradient(highlight, color, 1, 1) : Gradient(highlight, color, 1, 0);
        ScaleCache[key] = brush;
        return brush;
    }

    /// <summary>Tono de la escala en un porcentaje, interpolando entre las paradas que lo rodean.</summary>
    private static double HueAt(double at)
    {
        for (var i = 1; i < HueStops.Length; i++)
        {
            if (at > HueStops[i].At) continue;
            var (a, b) = (HueStops[i - 1], HueStops[i]);
            return a.Hue + (b.Hue - a.Hue) * (at - a.At) / (b.At - a.At);
        }
        return HueStops[^1].Hue;
    }

    /// <summary>Color a partir de tono (grados), saturación y brillo (0-1).</summary>
    private static Color FromHsv(double hue, double saturation, double value)
    {
        hue = ((hue % 360) + 360) % 360;
        var c = value * saturation;
        var x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
        var m = value - c;
        var (r, g, b) = (int)(hue / 60) switch
        {
            0 => (c, x, 0d),
            1 => (x, c, 0d),
            2 => (0d, c, x),
            3 => (0d, x, c),
            4 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    /// <summary>Mezcla dos colores: 0 devuelve el primero y 1 el segundo.</summary>
    private static Color Blend(Color from, Color to, double t) => Color.FromRgb(
        (byte)Math.Round(from.R + (to.R - from.R) * t),
        (byte)Math.Round(from.G + (to.G - from.G) * t),
        (byte)Math.Round(from.B + (to.B - from.B) * t));

    private sealed class Palette
    {
        public required IBrush Idle, Working, Waiting, Done, High;
        public required IBrush EffortOff, EffortLow, EffortMedium, EffortHigh, EffortXHigh, EffortMax;
        public required IBrush WorkingEdge, WorkingFill, WaitWarnEdge, WaitHighEdge;
        public required IBrush IdleTint, WorkingTint, WaitingTint, DoneTint;
        public required IBrush DoneEdge, WaitingCardFill, HighCardFill, DoneCardFill;
        public required IBrush Agents, AgentsEdge, AgentsCardFill, AgentsTint;
        public required IBrush Command, CommandEdge, CommandCardFill, CommandTint;
    }

    /// <summary>Crea un pincel sólido inmutable.</summary>
    private static IBrush Make(byte r, byte g, byte b, byte a = 0xFF) => new ImmutableSolidColorBrush(Color.FromArgb(a, r, g, b));

    /// <summary>Degradado vertical translúcido para tintar una tarjeta.</summary>
    private static IBrush Tint(Color top, Color bottom) => Gradient(top, bottom, 0, 1);

    /// <summary>Degradado inmutable desde arriba a la izquierda hasta el punto relativo dado.</summary>
    public static IBrush Gradient(Color from, Color to, double endX, double endY) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(endX, endY, RelativeUnit.Relative),
        GradientStops = { new GradientStop(from, 0), new GradientStop(to, 1) },
    }.ToImmutable();
}
