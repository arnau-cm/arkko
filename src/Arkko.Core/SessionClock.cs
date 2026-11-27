namespace Arkko.Core;

/// <summary>Un tramo del reloj de una sesión: lo que hizo un actor entre dos instantes, en milisegundos desde 1970 (UTC).</summary>
/// <param name="Hue">Lo que hacía, por su color: read, search, edit, run, think, say, ask, other, o wait si esperaba a un agente.</param>
public sealed record ClockSpan(long Start, long End, string Hue);

/// <summary>La letra de un agente en su anillo, donde empieza lo suyo dentro de la hora.</summary>
public sealed record ClockTag(string Letter, long At);

/// <summary>Un anillo del reloj: la sesión, o uno o varios agentes que no coinciden en el tiempo.</summary>
public sealed class ClockRing
{
    public bool Main { get; init; }
    public List<ClockSpan> Spans { get; } = [];
    public List<ClockTag> Tags { get; } = [];
}

/// <summary>El reloj de una sesión en una ventana de tiempo: sus anillos, los arranques de sus prompts y su último paso.</summary>
public sealed class ClockFace
{
    public long From { get; init; }
    public long To { get; init; }

    /// <summary>De dentro afuera: la sesión primero y después sus agentes.</summary>
    public List<ClockRing> Rings { get; } = [];

    /// <summary>Cuándo empezó cada prompt de la persona que cae en la ventana.</summary>
    public List<long> Prompts { get; } = [];

    /// <summary>Lo último que hizo, en cualquier actor; null si no tiene pasos.</summary>
    public long? Last { get; init; }

    /// <summary>La última herramienta que usó dentro de la ventana, como «Edit · sesiones.js»; null si ninguna.</summary>
    public string? Doing { get; init; }

    /// <summary>La letra del actor de <see cref="Doing"/>: B la sesión, C y siguientes sus agentes.</summary>
    public string? DoingLetter { get; init; }
}

/// <summary>Saca de lo leído de una sesión lo que pinta su reloj: qué hizo cada actor minuto a minuto en la última hora.</summary>
/// <remarks>
/// Cada herramienta ocupa de su llamada a su resultado, del color de lo que hace. El hueco entre dos pasos seguidos de
/// un mismo actor es Claude pensando o escribiendo, salvo que sea largo o que acabe en un prompt de la persona, que es
/// la sesión parada. Mientras un actor espera a un agente que lanzó, su anillo va a rayas. Los agentes que no coinciden
/// en el tiempo comparten anillo, así que hay tantos anillos de agentes como agentes trabajaron a la vez.
/// </remarks>
public static class SessionClock
{
    /// <summary>Lo que enseña el reloj: la última hora.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(1);

    /// <summary>Hueco más largo entre dos pasos que aún cuenta como Claude pensando; más, es que la sesión estaba parada.</summary>
    public static readonly TimeSpan ThinkGap = TimeSpan.FromMinutes(5);

    // Dos tramos del mismo color separados por menos de esto se dibujan como uno.
    private const long JoinMs = 1000;

    private const string MainLane = "";

    /// <summary>Construye el reloj de la última hora de una sesión.</summary>
    /// <param name="working">Si la sesión trabaja ahora: entonces lo que no ha terminado llega hasta ahora.</param>
    public static ClockFace Build(SessionTrace trace, DateTime nowUtc, bool working)
    {
        var to = Ms(nowUtc);
        var from = to - (long)Window.TotalMilliseconds;
        var steps = trace.Steps.Where(s => s.StartUtc is not null).OrderBy(s => s.StartUtc).ThenBy(s => s.Order).ToList();
        var letters = Letters(trace);
        var lanes = steps.GroupBy(s => s.AgentId ?? MainLane).ToDictionary(g => g.Key, g => g.ToList());
        var running = RunningAgents(trace, steps, to, working);

        // Las esperas de cada actor a los agentes que lanzó, para no tomarlas por Claude pensando.
        var waits = new Dictionary<string, List<ClockSpan>>();
        foreach (var call in steps.Where(s => s.Kind == TraceKind.Agent))
        {
            var parent = call.AgentId ?? MainLane;
            var end = AgentEnd(call, lanes, running, to);
            if (end <= Ms(call.StartUtc!.Value)) continue;
            if (!waits.TryGetValue(parent, out var list)) waits[parent] = list = [];
            list.Add(new ClockSpan(Ms(call.StartUtc.Value), end, "wait"));
        }

        var main = new ClockRing { Main = true };
        main.Spans.AddRange(Clip(Lane(lanes.GetValueOrDefault(MainLane) ?? [], waits.GetValueOrDefault(MainLane) ?? [], to,
            working && !Waiting(waits.GetValueOrDefault(MainLane), to)), from, to));
        var face = new ClockFace
        {
            From = from,
            To = to,
            Last = steps.Count == 0 ? null : steps.Max(s => Ms(s.EndUtc ?? s.StartUtc!.Value)),
            Doing = LastTool(steps, from)?.Title,
            DoingLetter = LastTool(steps, from) is { } last ? letters.GetValueOrDefault(last.AgentId ?? MainLane, "B") : null,
        };
        face.Rings.Add(main);
        face.Prompts.AddRange(steps
            .Where(s => s.Kind == TraceKind.Prompt && s.AgentId is null)
            .Select(s => Ms(s.StartUtc!.Value))
            .Where(t => t >= from && t <= to));

        // Cada agente, al primer anillo que haya quedado libre cuando empieza lo suyo dentro de la hora.
        var agents = lanes.Where(l => l.Key != MainLane)
            .Select(l => (Id: l.Key, Spans: Clip(Lane(l.Value, waits.GetValueOrDefault(l.Key) ?? [], to,
                running.Contains(l.Key) && !Waiting(waits.GetValueOrDefault(l.Key), to)), from, to)))
            .Where(a => a.Spans.Count > 0)
            .OrderBy(a => a.Spans[0].Start)
            .ToList();
        var ends = new List<long>();
        foreach (var (id, spans) in agents)
        {
            var at = ends.FindIndex(end => end <= spans[0].Start);
            if (at < 0)
            {
                face.Rings.Add(new ClockRing());
                ends.Add(0);
                at = ends.Count - 1;
            }
            var ring = face.Rings[at + 1];
            ring.Spans.AddRange(spans);
            ring.Tags.Add(new ClockTag(letters.GetValueOrDefault(id, "?"), spans[0].Start));
            ends[at] = spans.Max(s => s.End);
        }
        return face;
    }

    /// <summary>El color de una herramienta por su nombre: leer, buscar, cambiar, ejecutar, preguntarte u otra cosa.</summary>
    public static string Hue(string tool)
    {
        if (tool.StartsWith("mcp__", StringComparison.Ordinal)) return "other";
        return tool switch
        {
            "Read" or "NotebookRead" => "read",
            "Grep" or "Glob" or "LS" or "ToolSearch" => "search",
            "Edit" or "MultiEdit" or "NotebookEdit" or "Write" => "edit",
            "Bash" or "PowerShell" or "BashOutput" or "KillShell" or "Monitor" => "run",
            "AskUserQuestion" => "ask",
            _ when tool.StartsWith("Web", StringComparison.Ordinal) => "search",
            _ => "other",
        };
    }

    /// <summary>Los tramos de un actor: sus herramientas, sus mensajes y, entre pasos seguidos, Claude pensando.</summary>
    /// <param name="live">Si el actor sigue trabajando: lo último que no ha terminado llega hasta ahora.</param>
    private static List<ClockSpan> Lane(List<TraceStep> steps, List<ClockSpan> waits, long now, bool live)
    {
        var spans = new List<ClockSpan>(waits);
        long? previous = null;
        for (var i = 0; i < steps.Count; i++)
        {
            var s = steps[i];
            var start = Ms(s.StartUtc!.Value);
            var isLast = i == steps.Count - 1;
            if (previous is { } gapFrom && Fills(gapFrom, start, s.Kind, waits)) spans.Add(new ClockSpan(gapFrom, start, "think"));
            switch (s.Kind)
            {
                case TraceKind.Tool or TraceKind.Skill:
                    var end = s.EndUtc is { } e ? Math.Max(start, Ms(e)) : isLast && live ? now : start;
                    spans.Add(new ClockSpan(start, end, s.Kind == TraceKind.Skill ? "other" : Hue(ToolName(s.Title))));
                    previous = end;
                    break;
                case TraceKind.Reply:
                    spans.Add(new ClockSpan(start, start, "say"));
                    previous = start;
                    break;
                case TraceKind.Thinking:
                    spans.Add(new ClockSpan(start, start, "think"));
                    previous = start;
                    break;
                case TraceKind.Agent:
                    previous = start;
                    break;
                default:
                    previous = start;
                    break;
            }
        }
        if (live && previous is { } tail && Fills(tail, now, TraceKind.Reply, waits)) spans.Add(new ClockSpan(tail, now, "think"));
        return spans;
    }

    /// <summary>Si el hueco entre dos pasos es Claude pensando: corto, sin acabar en algo de la persona y sin esperar a un agente.</summary>
    private static bool Fills(long from, long to, TraceKind next, List<ClockSpan> waits) =>
        to > from
        && to - from <= (long)ThinkGap.TotalMilliseconds
        && next is not (TraceKind.Prompt or TraceKind.Command or TraceKind.Notification)
        && !waits.Any(w => from >= w.Start && from < w.End);

    /// <summary>Si un actor está esperando ahora a un agente suyo.</summary>
    private static bool Waiting(List<ClockSpan>? waits, long now) => waits?.Any(w => w.End >= now) == true;

    /// <summary>Cuándo dejó de esperarse a un agente: al volver su llamada, o al acabar lo suyo si iba en segundo plano.</summary>
    private static long AgentEnd(TraceStep call, Dictionary<string, List<TraceStep>> lanes, HashSet<string> running, long now)
    {
        var child = call.ChildAgentId;
        if (child is not null && running.Contains(child)) return now;
        if (call.EndUtc is { } end && !(child is not null && Background(call, child, lanes))) return Ms(end);
        if (child is not null && lanes.TryGetValue(child, out var own) && own.Count > 0) return own.Max(s => Ms(s.EndUtc ?? s.StartUtc!.Value));
        return call.EndUtc is { } e ? Ms(e) : Ms(call.StartUtc!.Value);
    }

    /// <summary>Si un agente siguió trabajando después de volver su llamada, que es lo que hace uno en segundo plano.</summary>
    private static bool Background(TraceStep call, string child, Dictionary<string, List<TraceStep>> lanes) =>
        call.EndUtc is { } end && lanes.TryGetValue(child, out var own) && own.Any(s => (s.EndUtc ?? s.StartUtc) > end);

    /// <summary>Los agentes que trabajan ahora: su llamada no ha vuelto, o van en segundo plano y han hecho algo hace poco.</summary>
    private static HashSet<string> RunningAgents(SessionTrace trace, List<TraceStep> steps, long now, bool working)
    {
        var running = new HashSet<string>(StringComparer.Ordinal);
        if (!working) return running;
        var background = trace.Agents.Where(a => a.Background).Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var call in steps.Where(s => s.Kind == TraceKind.Agent && s.ChildAgentId is not null))
        {
            var child = call.ChildAgentId!;
            var last = steps.Where(s => s.AgentId == child).Select(s => Ms(s.EndUtc ?? s.StartUtc!.Value)).DefaultIfEmpty(Ms(call.StartUtc!.Value)).Max();
            var recent = now - last <= (long)ThinkGap.TotalMilliseconds;
            if (call.EndUtc is null || background.Contains(child) && recent) running.Add(child);
        }
        return running;
    }

    /// <summary>La letra de cada actor, como en el Diagrama: B la sesión y, desde C, sus agentes por orden de aparición.</summary>
    internal static Dictionary<string, string> Letters(SessionTrace trace)
    {
        var letters = new Dictionary<string, string>(StringComparer.Ordinal) { [MainLane] = "B" };
        foreach (var s in trace.Steps.OrderBy(s => s.Order))
        {
            foreach (var id in new[] { s.AgentId, s.Kind == TraceKind.Agent ? s.ChildAgentId : null })
            {
                if (id is null || letters.ContainsKey(id)) continue;
                letters[id] = Letter(letters.Count + 1);
            }
        }
        return letters;
    }

    /// <summary>La letra de la posición dada, contando desde A; pasada la Z, AA, AB…</summary>
    private static string Letter(int index) =>
        index < 26 ? ((char)('A' + index)).ToString() : Letter(index / 26 - 1) + (char)('A' + index % 26);

    /// <summary>La última herramienta o agente lanzado dentro de la ventana; null si no hay.</summary>
    private static TraceStep? LastTool(List<TraceStep> steps, long from) =>
        steps.LastOrDefault(s => s.Kind is TraceKind.Tool or TraceKind.Agent or TraceKind.Skill && Ms(s.StartUtc!.Value) >= from);

    /// <summary>Recorta los tramos a la ventana, los ordena y junta los seguidos del mismo color.</summary>
    private static List<ClockSpan> Clip(List<ClockSpan> spans, long from, long to)
    {
        var inside = spans
            .Where(s => s.End >= from && s.Start <= to && !(s.End == s.Start && s.Start < from))
            .Select(s => s with { Start = Math.Max(s.Start, from), End = Math.Min(s.End, to) })
            .OrderBy(s => s.Hue == "wait" ? 0 : 1)
            .ThenBy(s => s.Start)
            .ToList();
        var joined = new List<ClockSpan>();
        foreach (var s in inside)
        {
            var at = joined.FindLastIndex(j => j.Hue == s.Hue);
            if (at >= 0 && at == joined.Count - 1 && s.Start - joined[at].End <= JoinMs) joined[at] = joined[at] with { End = Math.Max(joined[at].End, s.End) };
            else joined.Add(s);
        }
        return joined;
    }

    /// <summary>El nombre de la herramienta de un paso, que va al principio de su título: «Read» en «Read · Models.cs».</summary>
    internal static string ToolName(string title)
    {
        var cut = title.IndexOf(" · ", StringComparison.Ordinal);
        return cut < 0 ? title.Trim() : title[..cut].Trim();
    }

    /// <summary>Un instante en milisegundos desde 1970, tomándolo como UTC.</summary>
    private static long Ms(DateTime t) => new DateTimeOffset(DateTime.SpecifyKind(t, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
}
