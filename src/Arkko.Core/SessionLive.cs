namespace Arkko.Core;

/// <summary>Lo que una sesión espera de la persona: una pregunta, un plan, un permiso o una revisión.</summary>
/// <param name="Kind">«question», «plan», «permission» o «review».</param>
/// <param name="Text">Lo que pide, en una frase: la pregunta, la herramienta que quiere usar o su resumen.</param>
/// <param name="Header">El rótulo corto de la pregunta, si lo trae.</param>
/// <param name="Options">Las opciones de la pregunta; vacío si no tiene.</param>
/// <param name="More">Cuántas preguntas más hace en la misma llamada.</param>
/// <param name="SinceUtc">Desde cuándo espera.</param>
public sealed record SessionPending(string Kind, string Text, string? Header, IReadOnlyList<string> Options, int More, DateTime SinceUtc);

/// <summary>Una acción reciente de una sesión, para listarla: quién, de qué color, qué hizo y sobre qué.</summary>
/// <param name="Who">La letra del actor: A la persona, B Claude, C y siguientes sus agentes.</param>
/// <param name="Hue">El color de lo que hizo, como en el reloj: read, search, edit, run, ask, say, agent, user u other.</param>
/// <param name="Verb">Lo que hizo, en castellano: «Lee», «Ejecuta», «Prompt».</param>
/// <param name="Target">Sobre qué: el fichero, el comando o el principio del texto.</param>
public sealed record SessionAction(string Who, string Hue, string Verb, string Target, DateTime AtUtc);

/// <summary>Lo que el corcho en directo necesita de cada sesión abierta, sacado de su estado y de lo leído de ella: qué
/// espera de la persona, sus últimas acciones y cuánto trabajó en la última hora.</summary>
public static class SessionLive
{
    /// <summary>El texto de una revisión cuando la sesión no deja resumen.</summary>
    public const string ReviewFallback = "Terminada, sin revisar";

    private const int TargetMax = 90;

    /// <summary>Lo que la sesión espera de la persona ahora; null si no espera nada.</summary>
    /// <param name="needsReview">Si terminó y está sin marcar como vista.</param>
    /// <param name="recap">Su último resumen, para la revisión.</param>
    /// <remarks>
    /// Que espere lo dicen los hooks; qué pregunta y con qué opciones, lo leído: una llamada que pregunta y aún no tiene
    /// resultado. Si lo leído va por detrás de los hooks, se enseña lo que dicen ellos hasta la siguiente lectura.
    /// </remarks>
    public static SessionPending? Pending(SessionState state, SessionTrace? trace, bool needsReview, string? recap)
    {
        if (needsReview)
            return new SessionPending("review", string.IsNullOrWhiteSpace(recap) ? ReviewFallback : recap.Trim(), null, [], 0, state.StatusSinceUtc);
        if (state.Status != SessionStatus.Waiting) return null;

        var open = trace?.Steps
            .Where(s => s.AgentId is null && s.Kind == TraceKind.Tool && s.StartUtc is not null && s.EndUtc is null)
            .OrderBy(s => s.Order)
            .ToList() ?? [];
        if (open.LastOrDefault(s => s.Questions is { Count: > 0 }) is { } ask)
        {
            var q = ask.Questions![0];
            return new SessionPending("question", q.Question, q.Header, q.Options.Select(o => o.Label).ToList(), ask.Questions.Count - 1, ask.StartUtc!.Value);
        }
        if (open.LastOrDefault(s => ToolName(s.Title) == "ExitPlanMode") is { } plan)
            return new SessionPending("plan", "Tiene un plan listo para que lo apruebes", null, [], 0, plan.StartUtc!.Value);

        var detail = state.Detail?.Trim() ?? "";
        if (detail.StartsWith("Pide permiso", StringComparison.Ordinal))
        {
            var tool = open.LastOrDefault(s => s.Questions is null && ToolName(s.Title) != "ExitPlanMode");
            var text = tool is not null ? $"Quiere usar {tool.Title}" : PermissionText(detail);
            return new SessionPending("permission", text, null, [], 0, state.StatusSinceUtc);
        }
        if (detail == "Plan pendiente de aprobar")
            return new SessionPending("plan", "Tiene un plan listo para que lo apruebes", null, [], 0, state.StatusSinceUtc);
        return new SessionPending("question", detail.Length > 0 ? detail : "Espera tu respuesta", null, [], 0, state.StatusSinceUtc);
    }

    /// <summary>Las últimas acciones de la sesión y de sus agentes, de la más reciente a la más antigua.</summary>
    public static IReadOnlyList<SessionAction> Recent(SessionTrace trace, int count)
    {
        var letters = SessionClock.Letters(trace);
        return trace.Steps
            .Where(s => s.StartUtc is not null && s.Kind is not TraceKind.Thinking and not TraceKind.Notification)
            .OrderByDescending(s => s.StartUtc)
            .ThenByDescending(s => s.Order)
            .Take(count)
            .Select(s => Action(s, letters.TryGetValue(s.AgentId ?? "", out var l) ? l : "B"))
            .ToList();
    }

    /// <summary>Cuánto trabajó la sesión en cada trozo de la ventana de su reloj, de 0 a 100, del más antiguo al de ahora.</summary>
    /// <remarks>Cuenta el trabajo de la sesión y el de sus agentes, sin las esperas a ellos; si trabajan varios a la vez,
    /// el trozo no pasa de 100.</remarks>
    public static IReadOnlyList<int> Activity(ClockFace face, int buckets)
    {
        var width = (double)(face.To - face.From) / buckets;
        var busy = new double[buckets];
        foreach (var span in face.Rings.SelectMany(r => r.Spans).Where(s => s.Hue != "wait"))
        {
            var first = Math.Max(0, (int)((span.Start - face.From) / width));
            var last = Math.Min(buckets - 1, (int)((span.End - face.From) / width));
            for (var i = first; i <= last; i++)
            {
                var from = face.From + i * width;
                busy[i] += Math.Max(0, Math.Min(span.End, from + width) - Math.Max(span.Start, from));
            }
        }
        return busy.Select(b => (int)Math.Round(Math.Min(100, b / width * 100))).ToList();
    }

    /// <summary>Un paso como acción de la lista.</summary>
    private static SessionAction Action(TraceStep s, string letter)
    {
        var at = s.StartUtc!.Value;
        switch (s.Kind)
        {
            case TraceKind.Prompt:
                return new SessionAction("A", "user", "Prompt", FirstLine(s.Text), at);
            case TraceKind.Command:
                return new SessionAction("A", "user", s.Shell ? "Consola" : "Comando", FirstLine(s.Title), at);
            case TraceKind.Reply:
                return new SessionAction(letter, "say", "Responde", FirstLine(s.Text), at);
            case TraceKind.Agent:
                return new SessionAction(letter, "agent", "Lanza", FirstLine(s.Title), at);
            case TraceKind.Skill:
                return new SessionAction(letter, "other", "Skill", FirstLine(s.Title), at);
        }
        var tool = ToolName(s.Title);
        var target = FirstLine(s.Title.Length > tool.Length ? s.Title[tool.Length..].TrimStart(' ', '·') : "");
        var hue = SessionClock.Hue(tool);
        var verb = hue switch
        {
            "read" => "Lee",
            "search" => "Busca",
            "edit" => "Edita",
            "run" => "Ejecuta",
            "ask" => "Pregunta",
            _ => tool.StartsWith("mcp__", StringComparison.Ordinal) ? tool.Split("__").Last() : tool,
        };
        if (hue == "ask" && s.Questions is [var q, ..]) target = FirstLine(q.Question);
        return new SessionAction(letter, hue, verb, target, at);
    }

    /// <summary>Lo que dice el hook de un permiso, sin el «Pide permiso» que ya da el tipo y en castellano si es el aviso
    /// de siempre de Claude Code («Claude needs your permission to use Bash»).</summary>
    private static string PermissionText(string detail)
    {
        var message = detail.StartsWith("Pide permiso:", StringComparison.Ordinal) ? detail["Pide permiso:".Length..].Trim() : "";
        if (message.Length == 0) return "Pide permiso para seguir";
        const string Usual = "Claude needs your permission to use ";
        return message.StartsWith(Usual, StringComparison.OrdinalIgnoreCase) ? $"Quiere usar {message[Usual.Length..].Trim()}" : message;
    }

    /// <summary>La primera línea de un texto, recortada.</summary>
    private static string FirstLine(string? text)
    {
        var line = (text ?? "").Trim().Split('\n', 2)[0].Trim();
        return line.Length > TargetMax ? line[..(TargetMax - 1)] + "…" : line;
    }

    private static string ToolName(string title) => SessionClock.ToolName(title);
}
