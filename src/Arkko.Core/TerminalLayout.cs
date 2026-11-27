using System.Globalization;
using System.Text;

namespace Arkko.Core;

/// <summary>Piezas que se pueden colocar en la statusline de Claude Code.</summary>
public static class TerminalBlocks
{
    public const string Model = "model";
    public const string Repo = "repo";
    public const string Worktree = "worktree";
    public const string Session = "session";
    public const string Context = "context";
    public const string Effort = "effort";
    public const string FiveHour = "limit_five_hour";
    public const string SevenDay = "limit_seven_day";
    public const string OtherLimits = "limit_other";
    public const string Cost = "cost";
    public const string Separator = "separator";

    /// <summary>Todas las piezas colocables, en el orden en que se ofrecen; el separador se añade aparte.</summary>
    public static readonly string[] All = [Model, Repo, Worktree, Session, Context, Effort, FiveHour, SevenDay, OtherLimits, Cost];

    /// <summary>Nombre de la pieza para el editor.</summary>
    public static string Label(string kind) => kind switch
    {
        Model => "Modelo",
        Repo => "Repositorio",
        Worktree => "Worktree",
        Session => "Nombre de la sesión",
        Context => "Contexto",
        Effort => "Esfuerzo",
        FiveHour => "Límite 5 h",
        SevenDay => "Límite semanal",
        OtherLimits => "Otros límites",
        Cost => "Coste $",
        Separator => "|",
        _ => kind,
    };

    /// <summary>Indica si la pieza es una barra de límite, que admite el color «según el uso».</summary>
    public static bool IsLimit(string kind) => kind is FiveHour or SevenDay or OtherLimits;
}

/// <summary>Una pieza colocada en la statusline, con su color de texto; null es el color por defecto.</summary>
/// <remarks>En las barras de límite, null significa «según el uso»: verde, ámbar o rojo por porcentaje.</remarks>
public sealed class LayoutItem
{
    public string Kind { get; set; } = "";
    public string? Color { get; set; }

    /// <summary>Copia independiente, para editar sin tocar la configuración guardada.</summary>
    public LayoutItem Clone() => new() { Kind = Kind, Color = Color };
}

/// <summary>Configuración de lo que ak pinta en la terminal, en data/terminal-layout.json.</summary>
public sealed class TerminalLayout
{
    public List<LayoutItem> Items { get; set; } = [];
    public bool ShowSummary { get; set; } = true;

    /// <summary>Piezas del resumen de cada prompt, en orden; sin el campo en el fichero, todas.</summary>
    public List<string> SummaryItems { get; set; } = [.. SummaryBlocks.All];
    public bool ShowStatusline { get; set; } = true;

    /// <summary>Versión de la distribución guardada, para añadir una sola vez las piezas que salen después.</summary>
    /// <remarks>
    /// 0 es anterior a la pieza Worktree. Al leerla se coloca detrás de Repositorio, que desde entonces
    /// pinta el repositorio y no la carpeta, para que la terminal siga diciendo en qué rama se está.
    /// Si la persona la quita, se guarda ya con la versión al día y no vuelve.
    /// </remarks>
    public int Revision { get; set; }

    public const int CurrentRevision = 1;

    /// <summary>Distribución por defecto: la de siempre, en una sola línea.</summary>
    public static TerminalLayout Default() => new()
    {
        ShowSummary = true,
        ShowStatusline = true,
        Revision = CurrentRevision,
        Items =
        [
            new() { Kind = TerminalBlocks.Model, Color = "#56B6C2" },
            new() { Kind = TerminalBlocks.Repo },
            new() { Kind = TerminalBlocks.Worktree },
            new() { Kind = TerminalBlocks.Separator },
            new() { Kind = TerminalBlocks.Context },
            new() { Kind = TerminalBlocks.Separator },
            new() { Kind = TerminalBlocks.Effort },
            new() { Kind = TerminalBlocks.Separator },
            new() { Kind = TerminalBlocks.FiveHour },
            new() { Kind = TerminalBlocks.SevenDay },
            new() { Kind = TerminalBlocks.OtherLimits },
            new() { Kind = TerminalBlocks.Separator },
            new() { Kind = TerminalBlocks.Cost },
        ],
    };

    /// <summary>Configuración guardada, o la de por defecto si no hay o no se puede leer.</summary>
    public static TerminalLayout Load()
    {
        var saved = JsonStore.Read<TerminalLayout>(AppPaths.TerminalLayoutFile);
        return saved is { Items.Count: > 0 } ? saved.Upgraded() : Default();
    }

    /// <summary>La distribución con las piezas que han salido desde que se guardó; no la guarda.</summary>
    public TerminalLayout Upgraded()
    {
        if (Revision >= CurrentRevision) return this;
        var repo = Items.FindIndex(i => i.Kind == TerminalBlocks.Repo);
        if (repo >= 0 && Items.All(i => i.Kind != TerminalBlocks.Worktree))
            Items.Insert(repo + 1, new LayoutItem { Kind = TerminalBlocks.Worktree });
        Revision = CurrentRevision;
        return this;
    }

    /// <summary>Guarda la configuración para que ak la use en su siguiente pintado.</summary>
    public void Save() => JsonStore.Write(AppPaths.TerminalLayoutFile, this);

    /// <summary>Copia independiente, para editar sin tocar la configuración guardada.</summary>
    public TerminalLayout Clone() => new()
    {
        ShowSummary = ShowSummary,
        ShowStatusline = ShowStatusline,
        Revision = Revision,
        Items = Items.Select(i => i.Clone()).ToList(),
        SummaryItems = [.. SummaryItems],
    };
}

/// <summary>Datos de una sesión que se pintan en la statusline.</summary>
public sealed record TerminalData(
    string? Model,
    string? Repo,
    string? Session,
    double? ContextPct,
    string? Effort,
    IReadOnlyList<LimitInfo> Limits,
    double? CostUsd,
    string? Worktree = null);

/// <summary>Un trozo de texto pintado, con su color (#RRGGBB) o null para el color por defecto.</summary>
public sealed record TerminalSegment(string Text, string? Color);

/// <summary>Pinta la statusline según la configuración; lo usan ak y la vista previa del editor.</summary>
public static class TerminalRenderer
{
    private const string Esc = "\u001b[";
    private const string SeparatorColor = "#6E7383";

    /// <summary>Trozos de la línea, sin separadores sobrantes al principio, al final ni repetidos.</summary>
    /// <remarks>
    /// Las piezas sin dato (sesión sin nombre, límites que no llegan) se omiten, y con ellas el
    /// separador que se quedaría colgando.
    /// </remarks>
    public static List<TerminalSegment> Segments(TerminalLayout layout, TerminalData data)
    {
        var pieces = new List<List<TerminalSegment>>();
        foreach (var item in layout.Items)
        {
            if (item.Kind == TerminalBlocks.Separator)
            {
                if (pieces.Count > 0 && !IsSeparator(pieces[^1])) pieces.Add([new TerminalSegment("|", SeparatorColor)]);
                continue;
            }
            var piece = Piece(item, data);
            if (piece.Count > 0) pieces.Add(piece);
        }
        while (pieces.Count > 0 && IsSeparator(pieces[^1])) pieces.RemoveAt(pieces.Count - 1);

        var result = new List<TerminalSegment>();
        for (var i = 0; i < pieces.Count; i++)
        {
            if (i > 0) result.Add(new TerminalSegment(" ", null));
            result.AddRange(pieces[i]);
        }
        return result;
    }

    /// <summary>La línea con colores ANSI de 24 bits, lista para escribir en la terminal.</summary>
    public static string Ansi(IEnumerable<TerminalSegment> segments)
    {
        var sb = new StringBuilder();
        foreach (var s in segments)
        {
            if (s.Color is { } hex && TryRgb(hex, out var r, out var g, out var b))
                sb.Append($"{Esc}38;2;{r};{g};{b}m").Append(s.Text).Append($"{Esc}0m");
            else
                sb.Append(s.Text);
        }
        return sb.ToString();
    }

    /// <summary>Color de una barra de límite según lo usado: pasa de verde a amarillo, naranja y rojo.</summary>
    /// <remarks>Es la misma escala que las barras de la ventana: se recorre el tono, no se mezcla en RGB.</remarks>
    public static string UsageColor(double pct)
    {
        (double At, double Hue)[] stops = [(0, 130), (45, 58), (70, 32), (100, -4)];
        var at = Math.Clamp(pct, 0, 100);
        var hue = stops[^1].Hue;
        for (var i = 1; i < stops.Length; i++)
        {
            if (at > stops[i].At) continue;
            var (a, b) = (stops[i - 1], stops[i]);
            hue = a.Hue + (b.Hue - a.Hue) * (at - a.At) / (b.At - a.At);
            break;
        }
        return Hsv(hue, 0.78, 0.92);
    }

    /// <summary>Color «#RRGGBB» a partir de tono (grados), saturación y brillo (0-1).</summary>
    private static string Hsv(double hue, double saturation, double value)
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
        return $"#{(int)Math.Round((r + m) * 255):X2}{(int)Math.Round((g + m) * 255):X2}{(int)Math.Round((b + m) * 255):X2}";
    }

    /// <summary>Trozos de una pieza, o lista vacía si no hay dato que pintar.</summary>
    private static List<TerminalSegment> Piece(LayoutItem item, TerminalData d) => item.Kind switch
    {
        TerminalBlocks.Model => Text(d.Model is null ? null : $"[{d.Model}]", item.Color),
        TerminalBlocks.Repo => Text(d.Repo, item.Color),
        TerminalBlocks.Worktree => Text(d.Worktree, item.Color),
        TerminalBlocks.Session => Text(d.Session, item.Color),
        TerminalBlocks.Context => Text(d.ContextPct is { } c ? $"ctx {(int)c}%" : null, item.Color),
        TerminalBlocks.Effort => Text(d.Effort, item.Color),
        TerminalBlocks.FiveHour => Limits(d.Limits.Where(l => l.Key == "five_hour"), item.Color),
        TerminalBlocks.SevenDay => Limits(d.Limits.Where(l => l.Key == "seven_day"), item.Color),
        TerminalBlocks.OtherLimits => Limits(d.Limits.Where(l => l.Key is not ("five_hour" or "seven_day")), item.Color),
        TerminalBlocks.Cost => Text(d.CostUsd is { } c ? "$" + c.ToString("0.00", CultureInfo.InvariantCulture) : null, item.Color),
        _ => [],
    };

    /// <summary>Pieza de texto simple, o vacía si no hay texto.</summary>
    private static List<TerminalSegment> Text(string? text, string? color) =>
        string.IsNullOrWhiteSpace(text) ? [] : [new TerminalSegment(text, color)];

    /// <summary>Barras de uno o varios límites: «5 h [##--------] 22% (rst 1h 45m)».</summary>
    /// <remarks>El color elegido pinta la barra; sin color, la barra va según el uso.</remarks>
    private static List<TerminalSegment> Limits(IEnumerable<LimitInfo> limits, string? color)
    {
        var result = new List<TerminalSegment>();
        foreach (var l in limits)
        {
            if (result.Count > 0) result.Add(new TerminalSegment("  ", null));
            var reset = Format.Until(l.ResetsAtUtc);
            result.Add(new TerminalSegment($"{l.Label} ", null));
            result.Add(new TerminalSegment(Bar(l.UsedPct), color ?? UsageColor(l.UsedPct)));
            result.Add(new TerminalSegment($" {(int)l.UsedPct}%" + (reset.Length > 0 ? $" (rst {reset})" : ""), null));
        }
        return result;
    }

    /// <summary>Barra de 10 posiciones en ASCII, legible en cualquier codepage.</summary>
    private static string Bar(double pct)
    {
        var filled = (int)Math.Clamp(Math.Floor(pct / 10), 0, 10);
        return "[" + new string('#', filled) + new string('-', 10 - filled) + "]";
    }

    /// <summary>Indica si una pieza ya pintada es un separador.</summary>
    private static bool IsSeparator(List<TerminalSegment> piece) => piece is [{ Text: "|" }];

    /// <summary>Convierte «#RRGGBB» en sus componentes.</summary>
    public static bool TryRgb(string hex, out int r, out int g, out int b)
    {
        r = g = b = 0;
        var h = hex.TrimStart('#');
        return h.Length == 6
               && int.TryParse(h[..2], NumberStyles.HexNumber, null, out r)
               && int.TryParse(h[2..4], NumberStyles.HexNumber, null, out g)
               && int.TryParse(h[4..], NumberStyles.HexNumber, null, out b);
    }
}
