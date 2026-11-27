using System.Text.Json;
using Avalonia.Controls;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Donde vive un panel: una ventana suelta o un corcho.</summary>
internal interface IPieceHost
{
    /// <summary>Pone a la vista uno de sus paneles: trae su ventana al frente y, si va en pestañas, elige la suya.</summary>
    void Reveal(PiecePane pane);
}

/// <summary>Abre las partes de Sesiones que se sacan de la ventana principal: decide dónde va cada una y no deja que
/// haya dos paneles con lo mismo.</summary>
internal static class Pieces
{
    private static Func<SecurityMonitor?> _security = () => null;
    private static Func<string> _userName = () => "User";
    private static Func<IReadOnlyList<SessionState>> _sessions = () => [];
    private static Func<SessionState, (int Review, int Wait, string? Secret)> _attention = _ => (0, 0, null);
    private static Action<string> _markReviewed = _ => { };

    /// <summary>Recibe los secretos de Seguridad y el nombre de la persona, que necesitan los paneles de una sesión; las
    /// sesiones abiertas, cuyo estado enseñan los corchos; lo que pide cada una, y cómo darla por revisada.</summary>
    public static void Init(Func<SecurityMonitor?> security, Func<string> userName, Func<IReadOnlyList<SessionState>> sessions,
        Func<SessionState, (int Review, int Wait, string? Secret)> attention, Action<string> markReviewed)
    {
        _security = security;
        _userName = userName;
        _sessions = sessions;
        _attention = attention;
        _markReviewed = markReviewed;
    }

    /// <summary>Avisa de que una sesión se ha dado por revisada, desde donde sea: las páginas que la enseñan se ponen al
    /// día en el acto.</summary>
    public static event Action? Reviewed;

    /// <summary>Lo llama la app al dar una sesión por revisada, para que lo sepan todas las páginas.</summary>
    public static void NotifyReviewed() => Reviewed?.Invoke();

    /// <summary>Las sesiones abiertas ahora.</summary>
    public static IReadOnlyList<SessionState> Sessions() => _sessions();

    /// <summary>Las sesiones abiertas como las espera la página en su mensaje «active».</summary>
    public static IReadOnlyList<object> Active() => SessionsView.ActiveItems(_sessions(), _attention);

    /// <summary>Da una sesión por revisada, si lo estaba pidiendo.</summary>
    public static void MarkReviewed(string id) => _markReviewed(id);

    /// <summary>Una sesión abierta por su id; null si ya se cerró.</summary>
    public static SessionState? Session(string id) =>
        _sessions().FirstOrDefault(s => string.Equals(s.SessionId, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Abre un panel con una parte: sin decir dónde, en el corcho que se usó por última vez, y si ya está abierto,
    /// lo pone a la vista donde esté; diciendo dónde, lo lleva allí aunque esté abierto en otro sitio.</summary>
    /// <param name="where">Null, el último corcho; «new», un corcho nuevo; «window», una ventana suelta; o el número de un
    /// corcho.</param>
    public static void Open(PieceSpec spec, string? where = null)
    {
        if (where is null && PiecePane.Find(spec) is { } open)
        {
            open.ShowRound(spec);
            Reveal(open);
            return;
        }
        var pane = Claim(spec);
        pane.ShowRound(spec);
        Place(pane, where);
    }

    /// <summary>El panel con eso, sin sitio, para ponerlo en otro: el que ya está abierto, sacado de donde esté, o uno nuevo.</summary>
    public static PiecePane Claim(PieceSpec spec)
    {
        if (PiecePane.Find(spec) is not { } open) return Make(spec);
        switch (TopLevel.GetTopLevel(open))
        {
            case CorkWindow:
                CorkWindow.TakeOut(open);
                break;
            case PieceWindow loose:
                loose.Release();
                break;
        }
        return open;
    }

    /// <summary>Pone un panel sin sitio donde se pide: en una ventana suelta, en un corcho nuevo, en uno por su número o
    /// en el último que se usó.</summary>
    private static void Place(PiecePane pane, string? where)
    {
        if (where == "window") PieceWindow.Hold(pane);
        else CorkWindow.PinTo(pane, int.TryParse(where, out var number) ? number : null, fresh: where == "new");
    }

    /// <summary>Lo que pide clavar una página de una sesión: una de sus partes («pieceOpen»: el directo, el chat, los
    /// cambios o la ficha de un paso) o la ficha de un fichero («pieceFile»); null si el mensaje no es eso.</summary>
    /// <param name="cwd">La carpeta de la sesión, que necesita la ficha de un fichero.</param>
    public static PieceSpec? SpecFrom(string? type, JsonElement m, string sessionId, string? cwd)
    {
        string? Text(string name) => m.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        switch (type)
        {
            case "pieceFile" when Text("path") is { } path:
                var scope = Text("scope") is "branch" or ChangesChannel.RepoScope ? Text("scope")! : "pending";
                int? round = scope == "branch" ? RoundOf(m) : null;
                // Un fichero del repositorio entero no tiene diff que enseñar: siempre se ve entero.
                var tab = scope == ChangesChannel.RepoScope || Text("tab") == "file" ? "file" : "diff";
                return new PieceSpec("file", sessionId, Text("title") ?? path, cwd, scope, path, tab, round);
            case "pieceOpen" when Text("piece") is "step" && Text("key") is { } key:
                return new PieceSpec("step", sessionId, Text("title") ?? "Paso", Path: key, Tab: Text("interaction"));
            case "pieceOpen" when Text("piece") is { } part && PieceSpec.IsPart(part):
                return new PieceSpec(part, sessionId, Text("title") ?? "Sesión");
            default:
                return null;
        }
    }

    /// <summary>Lo que hay que mandar a la ficha de un fichero para llevarla a una línea, si el «pieceFile» la trae: la línea
    /// y, si se pidió, lo que hay que buscar en ella con la barra de Ctrl+F; null si no trae línea.</summary>
    /// <remarks>Se saca del mensaje en el momento: el mensaje deja de valer en cuanto se termina de atender.</remarks>
    public static object? GoToOf(JsonElement m, PieceSpec piece)
    {
        if (piece.Kind != "file" || !m.TryGetProperty("line", out var l) || l.ValueKind != JsonValueKind.Number || !l.TryGetInt32(out var line)) return null;
        string? query = m.TryGetProperty("findQuery", out var q) && q.ValueKind == JsonValueKind.String ? q.GetString() : null;
        bool Flag(string name) => m.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
        return new { type = "goTo", scope = piece.Scope, path = piece.Path, line, findQuery = query, findCase = Flag("findCase"), findWord = Flag("findWord") };
    }

    /// <summary>Lleva a su línea la ficha de un fichero recién abierta o puesta a la vista, si se pidió alguna.</summary>
    public static void GoTo(PieceSpec piece, object? goTo)
    {
        if (goTo is not null && PiecePane.Find(piece) is { } pane) pane.Deliver(goTo);
    }

    /// <summary>La ronda de la revisión que trae un mensaje de la página; null si no trae ninguna, que es toda la rama.</summary>
    /// <remarks>Toda la rama llega como null, y leer un número de un null lanza una excepción.</remarks>
    public static int? RoundOf(JsonElement m) =>
        m.TryGetProperty("round", out var r) && r.ValueKind == JsonValueKind.Number && r.TryGetInt32(out var n) && n > 0 ? n : null;

    /// <summary>Crea el panel de una parte, aún sin sitio.</summary>
    public static PiecePane Make(PieceSpec spec) => new(spec, _security, _userName);

    /// <summary>Las fichas de fichero abiertas de una sesión, para que su lista de cambios las marque: su alcance, su ruta
    /// y dónde están (el nombre del corcho o «una ventana suelta»).</summary>
    public static IReadOnlyList<object> OpenFiles(string? sessionId) =>
        sessionId is null ? []
        : PiecePane.All
            .Where(p => p.Spec.Kind == "file" && string.Equals(p.Spec.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
            .Select(p => (object)new
            {
                scope = p.Spec.Scope, path = p.Spec.Path,
                place = TopLevel.GetTopLevel(p) switch
                {
                    CorkWindow cork => cork.DisplayName,
                    PieceWindow => "una ventana suelta",
                    _ => null,
                },
            })
            .ToList();

    /// <summary>Si las notas de la revisión de una sesión están abiertas aparte, en su propio panel.</summary>
    public static bool NotesAside(string? sessionId) =>
        sessionId is not null && PiecePane.All.Any(p => p.Spec.Kind == "review" && string.Equals(p.Spec.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Pone a la vista un panel abierto, donde esté.</summary>
    public static void Reveal(PiecePane pane)
    {
        if (TopLevel.GetTopLevel(pane) is IPieceHost host) host.Reveal(pane);
    }
}
