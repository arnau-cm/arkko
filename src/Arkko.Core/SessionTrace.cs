using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Arkko.Core;

/// <summary>Qué es un paso del trabajo de una sesión.</summary>
public enum TraceKind
{
    /// <summary>Un prompt escrito por la persona.</summary>
    Prompt,
    /// <summary>Un comando «/» de la persona.</summary>
    Command,
    /// <summary>Un aviso que llega a la sesión sin escribirlo nadie, como el fin de un agente en segundo plano.</summary>
    Notification,
    /// <summary>Texto de Claude para la persona.</summary>
    Reply,
    /// <summary>Razonamiento de Claude, cuando el transcript trae su texto.</summary>
    Thinking,
    /// <summary>Llamada a una herramienta, con su resultado.</summary>
    Tool,
    /// <summary>Llamada que lanza un subagente.</summary>
    Agent,
    /// <summary>Llamada que carga una skill.</summary>
    Skill,
}

/// <summary>Un paso del trabajo de una sesión: quién, qué, cuándo y con qué resultado. Los textos van con los secretos tapados.</summary>
public sealed class TraceStep
{
    /// <summary>Posición en el orden en que pasó, de 0 en adelante.</summary>
    public int Order { get; set; }

    public TraceKind Kind { get; set; }

    /// <summary>Subagente donde pasó; null en la sesión principal.</summary>
    public string? AgentId { get; set; }

    /// <summary>Interacción a la que pertenece: 1 la del primer prompt; 0 lo de antes del primer prompt.</summary>
    public int Interaction { get; set; }

    /// <summary>Línea corta: «Read · Models.cs», el nombre de la skill, el tipo y la descripción del agente.</summary>
    public string Title { get; set; } = "";

    /// <summary>Lo que dice o pide el paso: el prompt, la respuesta, el razonamiento o la entrada de la herramienta.</summary>
    public string? Text { get; set; }

    /// <summary>Resultado de la herramienta, si lo hubo.</summary>
    public string? Result { get; set; }
    public bool IsError { get; set; }

    public DateTime? StartUtc { get; set; }

    /// <summary>Cuándo llegó el resultado de la herramienta; null en los pasos que no esperan nada.</summary>
    public DateTime? EndUtc { get; set; }

    /// <summary>Skill que provocó el paso, si alguna.</summary>
    public string? Skill { get; set; }

    /// <summary>Modelo que escribió la respuesta o el razonamiento.</summary>
    public string? Model { get; set; }

    /// <summary>Esfuerzo de razonamiento con que se escribió («low», «medium», «high», «xhigh», «max»); null si no consta.</summary>
    public string? Effort { get; set; }

    /// <summary>En una llamada a agente, el subagente que lanzó.</summary>
    public string? ChildAgentId { get; set; }

    /// <summary>En un prompt, si la persona lo escribió mientras Claude trabajaba y se juntó a la interacción en curso.</summary>
    public bool Queued { get; set; }

    /// <summary>Si un hook de Arkko falló en este paso: en qué momento y por qué, dicho en corto; null si no.</summary>
    public string? HookError { get; set; }

    /// <summary>En un comando, si es uno de consola lanzado con «!»: se enseña aparte, y solo el comando, sin su salida.</summary>
    public bool Shell { get; set; }

    /// <summary>Id de la llamada en el transcript, para casar resultados y subagentes.</summary>
    public string? ToolUseId { get; set; }

    /// <summary>En una llamada que pregunta a la persona (AskUserQuestion), sus preguntas y, cuando llegan, sus respuestas.</summary>
    public List<TraceQuestion>? Questions { get; set; }

    /// <summary>Imágenes del paso: las pegadas en un prompt o las que devolvió una herramienta; null si no hay.</summary>
    public List<TraceImage>? Images { get; set; }

    /// <summary>Rama de git en que estaba la sesión al escribirse el paso; null fuera de un repositorio.</summary>
    public string? Branch { get; set; }
}

/// <summary>Una imagen del transcript, tal como la guarda Claude Code: su tipo y su contenido en base64.</summary>
public sealed class TraceImage
{
    public string MediaType { get; set; } = "";
    public string Data { get; set; } = "";
}

/// <summary>Una pregunta que la sesión hace a la persona, con sus opciones y lo que respondió.</summary>
public sealed class TraceQuestion
{
    public string Question { get; set; } = "";

    /// <summary>Rótulo corto de la pregunta.</summary>
    public string? Header { get; set; }
    public bool MultiSelect { get; set; }
    public List<TraceOption> Options { get; set; } = [];

    /// <summary>Lo que respondió: la etiqueta de una opción, varias separadas por comas o un texto propio; null mientras no conteste.</summary>
    public string? Answer { get; set; }

    /// <summary>Nota que añadió a su respuesta, si alguna.</summary>
    public string? Note { get; set; }
}

/// <summary>Una opción de una pregunta.</summary>
public sealed class TraceOption
{
    public string Label { get; set; } = "";
    public string? Description { get; set; }
}

/// <summary>Un subagente de la sesión: su tipo, su encargo y de qué agente cuelga.</summary>
public sealed class TraceAgent
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>1 si lo lanzó la sesión principal, 2 si lo lanzó otro agente, y así.</summary>
    public int Depth { get; set; }

    /// <summary>Agente que lo lanzó; null si fue la sesión principal o no se sabe.</summary>
    public string? ParentAgentId { get; set; }
    public bool Background { get; set; }

    /// <summary>Nombre corto del modelo con que trabaja («Haiku», «Opus 5.5»); null si aún no se sabe.</summary>
    public string? Model { get; set; }

    /// <summary>Esfuerzo de razonamiento con que trabaja («medium», «high»…); null si aún no se sabe.</summary>
    public string? Effort { get; set; }
    public DateTime? StartUtc { get; set; }
    public DateTime? EndUtc { get; set; }

    /// <summary>Cuándo llegó a la sesión principal su último aviso de fin; null si aún no ha llegado ninguno.</summary>
    public DateTime? FinishedUtc { get; set; }

    /// <summary>Si, en segundo plano, seguía trabajando al leer la sesión.</summary>
    public bool Busy { get; set; }

    // Un agente en segundo plano callado tanto tiempo se da por parado: si la sesión se cerró, su aviso de fin no llega nunca.
    private static readonly TimeSpan Quiet = TimeSpan.FromMinutes(10);

    /// <summary>Si un agente en segundo plano sigue trabajando en un momento: ha escrito algo después de su último aviso de fin, o
    /// aún no tiene ninguno, y no lleva mucho tiempo callado.</summary>
    /// <remarks>
    /// Su llamada vuelve en cuanto se lanza, y uno retomado con un mensaje no tiene llamada: solo su transcript y su aviso dicen
    /// si trabaja. El margen cubre lo que tarda el aviso en llegar tras su última línea.
    /// </remarks>
    public bool IsBusy(DateTime nowUtc) => Background && EndUtc is { } end && nowUtc - end < Quiet
                                           && (FinishedUtc is not { } done || end > done.AddSeconds(5));
}

/// <summary>Una interacción: un prompt de la persona y todo lo que provocó hasta devolverle el control.</summary>
public sealed class TraceInteraction
{
    public int Number { get; set; }

    /// <summary>Principio del prompt en una línea, con los secretos tapados.</summary>
    public string Prompt { get; set; } = "";
    public DateTime? StartUtc { get; set; }
    public DateTime? EndUtc { get; set; }
    public int Agents { get; set; }
    public int Tools { get; set; }
    public int Skills { get; set; }
    public int Steps { get; set; }
}

/// <summary>El trabajo de una sesión entera: sus pasos en orden, sus subagentes y sus interacciones.</summary>
public sealed class SessionTrace
{
    public string SessionId { get; set; } = "";
    public string? Title { get; set; }
    public string? Cwd { get; set; }

    /// <summary>Última rama de git de la sesión principal; null fuera de un repositorio.</summary>
    public string? Branch { get; set; }

    public List<TraceStep> Steps { get; set; } = [];
    public List<TraceAgent> Agents { get; set; } = [];
    public List<TraceInteraction> Interactions { get; set; } = [];

    /// <summary>Tokens de contexto de la última respuesta de la sesión principal: lo que entró, de caché o no; null si no hay.</summary>
    public long? ContextTokens { get; set; }
}

/// <summary>Lo que dice el .meta.json de un subagente: su tipo, su encargo, qué llamada lo lanzó y con qué modelo y esfuerzo.</summary>
internal sealed record AgentMeta(string? Type = null, string? Description = null, string? ToolUseId = null, int? Depth = null,
    bool Background = false, string? Model = null, string? ParentAgentId = null, string? Effort = null);

/// <summary>Lee el transcript de una sesión y los de sus subagentes y los convierte en pasos.</summary>
/// <remarks>
/// El formato del transcript no es un contrato estable de Claude Code: se lee de forma defensiva y lo
/// que no se reconoce se ignora (adjuntos de hooks, recordatorios, entradas de sistema). Cada subagente
/// escribe su propio transcript en &lt;sesión&gt;/subagents, con un .meta.json que dice qué llamada lo
/// lanzó; sus pasos se cuelgan de esa llamada y cuentan en su misma interacción, aunque terminen
/// después, como pasa con los lanzados en segundo plano.
/// </remarks>
public static partial class SessionTraceReader
{
    /// <summary>Largo máximo de cada texto; lo que sobra se corta con una nota de cuánto queda.</summary>
    public const int MaxText = 8000;

    /// <summary>Largo máximo de un prompt, una respuesta o un razonamiento, que se leen enteros.</summary>
    /// <remarks>
    /// El corte de <see cref="MaxText"/> es para las salidas de herramienta, que pueden tener megas. Un mensaje
    /// rara vez pasa de unas decenas de miles de caracteres, y cortarlo dejaba a medias justo lo que se abre para leer.
    /// </remarks>
    public const int MaxMessage = 100_000;

    private const int MaxPromptPreview = 140;

    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Lee la sesión de un transcript de una vez; vacía si el fichero no existe.</summary>
    public static SessionTrace Read(string transcriptPath)
    {
        var builder = new SessionTraceBuilder(transcriptPath);
        builder.Update(final: true);
        return builder.Snapshot();
    }

    /// <summary>Paso de un texto de la persona según cómo empieza; null si es ruido del sistema.</summary>
    internal static TraceStep? UserStep(string text)
    {
        var trimmed = text.TrimStart();
        // Claude Code pone primero el nombre o el mensaje del comando, según de dónde salga.
        if (trimmed.StartsWith("<command-name>", StringComparison.Ordinal) || trimmed.StartsWith("<command-message>", StringComparison.Ordinal))
        {
            var name = Tag(trimmed, "command-name") ?? Tag(trimmed, "command-message") ?? "";
            var args = Tag(trimmed, "command-args");
            var command = string.IsNullOrWhiteSpace(args) ? name : $"{name} {args}";
            return new TraceStep { Kind = TraceKind.Command, Title = command, Text = RedactClip(command) };
        }
        if (UserMessage.KindOf(trimmed) == PromptKind.Shell)
        {
            // Un comando «!» abre su propio turno, como un comando «/»: lo que Claude conteste después es de él. Su salida
            // llega en otro mensaje, que se le cuelga como su resultado.
            var shell = SecretDetector.Redact("! " + Collapse(UserMessage.ShellCommand(trimmed) ?? ""))!;
            return new TraceStep { Kind = TraceKind.Command, Title = shell, Text = shell, Shell = true };
        }
        if (trimmed.StartsWith("<task-notification>", StringComparison.Ordinal))
        {
            // El resumen dice qué tarea acabó y cómo; el resto (ids, ruta de su salida) es ruido para quien lo lee.
            var body = Tag(trimmed, "summary") ?? AnyTag().Replace(trimmed, " ");
            return new TraceStep { Kind = TraceKind.Notification, Title = "Aviso de tarea", Text = RedactClip(Collapse(body)) };
        }
        // Lo pegado llega entre etiquetas propias: es de la persona, aunque el mensaje empiece por ellas. Se quedan, para pintarlo aparte.
        if (trimmed.StartsWith('<') && !trimmed.StartsWith("<pasted_content", StringComparison.Ordinal)) return null;
        return new TraceStep { Kind = TraceKind.Prompt, Title = "Prompt", Text = RedactClip(text.Trim(), MaxMessage) };
    }

    /// <summary>Paso de un bloque de Claude; null si no aporta nada (un razonamiento sin texto).</summary>
    internal static TraceStep? Assistant(JsonElement block, string? model)
    {
        switch (TranscriptReader.Str(block, "type"))
        {
            case "text" when TranscriptReader.Str(block, "text") is { } text && !string.IsNullOrWhiteSpace(text):
                return new TraceStep { Kind = TraceKind.Reply, Title = "Respuesta", Text = RedactClip(text.Trim(), MaxMessage), Model = model };
            case "thinking" when TranscriptReader.Str(block, "thinking") is { } thought && !string.IsNullOrWhiteSpace(thought):
                return new TraceStep { Kind = TraceKind.Thinking, Title = "Razonamiento", Text = RedactClip(thought.Trim(), MaxMessage), Model = model };
            case "tool_use":
                var name = TranscriptReader.Str(block, "name") ?? "herramienta";
                var input = block.TryGetProperty("input", out var i) ? i : default;
                var kind = name switch { "Agent" or "Task" => TraceKind.Agent, "Skill" => TraceKind.Skill, _ => TraceKind.Tool };
                return new TraceStep
                {
                    Kind = kind,
                    Title = ToolTitle(name, kind, input),
                    Text = RedactClip(kind == TraceKind.Agent ? Field(input, "prompt") : InputText(input)),
                    ToolUseId = TranscriptReader.Str(block, "id"),
                    Questions = name == "AskUserQuestion" ? Questions(input) : null,
                };
            default:
                return null;
        }
    }

    /// <summary>Datos del .meta.json de un subagente; vacíos si no hay o no se puede leer.</summary>
    internal static AgentMeta ReadMeta(string agentFile)
    {
        var path = Path.ChangeExtension(agentFile, ".meta.json");
        try
        {
            if (!File.Exists(path)) return new();
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var m = doc.RootElement;
            int? depth = m.TryGetProperty("spawnDepth", out var d) && d.TryGetInt32(out var n) ? n : null;
            return new(TranscriptReader.Str(m, "agentType"), TranscriptReader.Str(m, "description"), TranscriptReader.Str(m, "toolUseId"), depth,
                TranscriptReader.Str(m, "requestShape") == "background", TranscriptReader.Str(m, "model"),
                TranscriptReader.Str(m, "parentAgentId") is { Length: > 0 } parent ? "agent-" + parent : null, TranscriptReader.Str(m, "effort"));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    /// <summary>Resumen de cada interacción a partir de sus pasos ya ordenados.</summary>
    internal static List<TraceInteraction> Summarize(List<TraceStep> steps) => steps
        .GroupBy(s => s.Interaction)
        .OrderBy(g => g.Key)
        .Select(g =>
        {
            var prompt = g.FirstOrDefault(s => s.Kind is TraceKind.Prompt or TraceKind.Command);
            var times = g.SelectMany(s => new[] { s.StartUtc, s.EndUtc }).OfType<DateTime>().ToList();
            return new TraceInteraction
            {
                Number = g.Key,
                Prompt = prompt?.Text is { } text ? Preview(text) : "(antes del primer prompt)",
                StartUtc = prompt?.StartUtc ?? (times.Count > 0 ? times.Min() : null),
                EndUtc = times.Count > 0 ? times.Max() : null,
                Agents = g.Count(s => s.Kind == TraceKind.Agent),
                Tools = g.Count(s => s.Kind == TraceKind.Tool),
                Skills = g.Count(s => s.Kind == TraceKind.Skill),
                Steps = g.Count(),
            };
        })
        .ToList();

    /// <summary>Título de una llamada: la herramienta y su objetivo, el tipo y la descripción del agente, o la skill.</summary>
    private static string ToolTitle(string name, TraceKind kind, JsonElement input)
    {
        if (kind == TraceKind.Agent)
            return Join(Field(input, "subagent_type") ?? "agente", Field(input, "description"));
        if (kind == TraceKind.Skill)
            return Field(input, "skill") ?? "skill";

        var file = Field(input, "file_path") ?? Field(input, "notebook_path") ?? Field(input, "path");
        var target = file is not null
            ? Path.GetFileName(file.TrimEnd('/', '\\'))
            : Field(input, "command") ?? Field(input, "pattern") ?? Field(input, "url") ?? Field(input, "query") ?? Field(input, "description");
        return Join(name, target is null ? null : Preview(target, 70));
    }

    /// <summary>Preguntas de una llamada AskUserQuestion, con sus opciones; null si la entrada no trae ninguna.</summary>
    private static List<TraceQuestion>? Questions(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object || !input.TryGetProperty("questions", out var list) || list.ValueKind != JsonValueKind.Array) return null;
        var questions = list.EnumerateArray().Where(q => q.ValueKind == JsonValueKind.Object).Select(q => new TraceQuestion
        {
            Question = SecretDetector.Redact(TranscriptReader.Str(q, "question")) ?? "",
            Header = TranscriptReader.Str(q, "header"),
            MultiSelect = q.TryGetProperty("multiSelect", out var multi) && multi.ValueKind == JsonValueKind.True,
            Options = q.TryGetProperty("options", out var options) && options.ValueKind == JsonValueKind.Array
                ? options.EnumerateArray().Where(o => o.ValueKind == JsonValueKind.Object).Select(o => new TraceOption
                {
                    Label = SecretDetector.Redact(TranscriptReader.Str(o, "label")) ?? "",
                    Description = SecretDetector.Redact(TranscriptReader.Str(o, "description")),
                }).ToList()
                : [],
        }).ToList();
        return questions.Count > 0 ? questions : null;
    }

    /// <summary>Apunta en cada pregunta lo que respondió la persona y su nota, del resultado estructurado que guarda Claude Code.</summary>
    /// <remarks>Las respuestas van por el texto de la pregunta; se casan con los secretos ya tapados en los dos lados.</remarks>
    internal static void Answer(List<TraceQuestion> questions, JsonElement entry)
    {
        if (!entry.TryGetProperty("toolUseResult", out var result) || result.ValueKind != JsonValueKind.Object) return;
        var answers = Pairs(result, "answers", a => a.ValueKind == JsonValueKind.String ? a.GetString() : null);
        var notes = Pairs(result, "annotations", a => a.ValueKind == JsonValueKind.Object ? TranscriptReader.Str(a, "notes") : null);
        foreach (var q in questions)
        {
            if (answers.TryGetValue(q.Question, out var answer)) q.Answer = answer;
            if (notes.TryGetValue(q.Question, out var note) && !string.IsNullOrWhiteSpace(note)) q.Note = note;
        }
    }

    /// <summary>Campos de un objeto del resultado como diccionario pregunta → texto, ya con los secretos tapados.</summary>
    private static Dictionary<string, string> Pairs(JsonElement result, string name, Func<JsonElement, string?> value)
    {
        var pairs = new Dictionary<string, string>();
        if (!result.TryGetProperty(name, out var obj) || obj.ValueKind != JsonValueKind.Object) return pairs;
        foreach (var p in obj.EnumerateObject())
            if (value(p.Value) is { } text) pairs[SecretDetector.Redact(p.Name) ?? p.Name] = SecretDetector.Redact(text) ?? text;
        return pairs;
    }

    /// <summary>«nombre · detalle», o solo el nombre si no hay detalle.</summary>
    private static string Join(string name, string? detail) => string.IsNullOrWhiteSpace(detail) ? name : $"{name} · {detail}";

    /// <summary>Entrada de una herramienta como texto legible: JSON con sangría y sin escapar los acentos.</summary>
    private static string? InputText(JsonElement input) =>
        input.ValueKind == JsonValueKind.Undefined ? null : JsonSerializer.Serialize(input, Indented);

    /// <summary>Texto del resultado de una herramienta, sea una cadena o una lista de bloques.</summary>
    internal static string? ResultText(JsonElement block)
    {
        if (!block.TryGetProperty("content", out var c)) return null;
        return c.ValueKind switch
        {
            JsonValueKind.String => c.GetString(),
            JsonValueKind.Array => string.Join("\n", c.EnumerateArray().Select(b =>
                TranscriptReader.Str(b, "type") == "text" ? TranscriptReader.Str(b, "text") : $"[{TranscriptReader.Str(b, "type") ?? "contenido"}]")),
            _ => null,
        };
    }

    /// <summary>Tokens de contexto de una respuesta: los de entrada, los escritos en caché y los leídos de ella; null si no trae uso.</summary>
    internal static long? ContextTokens(JsonElement message)
    {
        if (!message.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object) return null;
        long Tokens(string name) => usage.TryGetProperty(name, out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt64(out var v) ? v : 0;
        var total = Tokens("input_tokens") + Tokens("cache_creation_input_tokens") + Tokens("cache_read_input_tokens");
        return total > 0 ? total : null;
    }

    /// <summary>Texto de un mensaje de la persona: la cadena, o sus bloques de texto juntos; null si no lleva texto.</summary>
    internal static string? PromptText(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String) return string.IsNullOrWhiteSpace(content.GetString()) ? null : content.GetString();
        if (content.ValueKind != JsonValueKind.Array) return null;
        var blocks = content.EnumerateArray().ToList();
        if (blocks.Any(b => TranscriptReader.Str(b, "type") == "tool_result")) return null;
        var text = string.Join("\n", blocks.Select(b => TranscriptReader.Str(b, "type") switch
        {
            "text" => TranscriptReader.Str(b, "text"),
            "image" => "[imagen]",
            _ => null,
        }).OfType<string>());
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>Imágenes de unos bloques, solo de los tipos que se pueden enseñar sin riesgo; null si no hay ninguna.</summary>
    /// <remarks>Una imagen de más de 10 MB en base64 se salta: no cabe en un mensaje razonable a la página.</remarks>
    internal static List<TraceImage>? Images(IEnumerable<JsonElement> blocks)
    {
        var images = blocks
            .Where(b => b.ValueKind == JsonValueKind.Object && TranscriptReader.Str(b, "type") == "image"
                        && b.TryGetProperty("source", out var s) && s.ValueKind == JsonValueKind.Object && TranscriptReader.Str(s, "type") == "base64")
            .Select(b => b.GetProperty("source"))
            .Select(s => (Type: TranscriptReader.Str(s, "media_type"), Data: TranscriptReader.Str(s, "data")))
            .Where(i => i.Type is "image/png" or "image/jpeg" or "image/gif" or "image/webp" && i.Data is { Length: > 0 and <= 10_000_000 })
            .Select(i => new TraceImage { MediaType = i.Type!, Data = i.Data! })
            .ToList();
        return images.Count > 0 ? images : null;
    }

    /// <summary>Imágenes que devuelve un resultado de herramienta, dentro de su lista de bloques.</summary>
    internal static List<TraceImage>? ResultImages(JsonElement block) =>
        block.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.Array ? Images(c.EnumerateArray()) : null;

    /// <summary>Bloques de un contenido: los de la lista, o ninguno si es una cadena.</summary>
    internal static IEnumerable<JsonElement> Blocks(JsonElement content) =>
        content.ValueKind == JsonValueKind.Array ? content.EnumerateArray() : [];

    /// <summary>Valor de texto de un campo de la entrada de una herramienta.</summary>
    private static string? Field(JsonElement input, string name) =>
        input.ValueKind == JsonValueKind.Object ? TranscriptReader.Str(input, name) : null;

    /// <summary>Contenido de la primera etiqueta con ese nombre, o null.</summary>
    private static string? Tag(string text, string name)
    {
        var m = Regex.Match(text, $"<{name}>(.*?)</{name}>", RegexOptions.Singleline);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    /// <summary>Texto en una sola línea, sin espacios repetidos.</summary>
    private static string Collapse(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Principio de un texto en una línea, sin las etiquetas de lo pegado, con «…» si se corta.</summary>
    internal static string Preview(string text, int max = MaxPromptPreview)
    {
        var line = Collapse(PastedTag().Replace(text, " "));
        return line.Length <= max ? line : line[..max].TrimEnd() + "…";
    }

    /// <summary>Corta un texto largo y dice cuánto se ha dejado fuera.</summary>
    internal static string? Clip(string? text, int max = MaxText) =>
        text is null || text.Length <= max ? text : text[..max] + $"\n… ({text.Length - max:N0} caracteres más)";

    // Lo que se pasa por el detector más allá de lo que se enseña: un secreto que empieza antes del corte
    // y acaba después tiene que verse entero para taparlo. Cubre de sobra una clave privada PEM.
    private const int RedactMargin = 8000;

    /// <summary>Tapa los secretos de un texto y lo corta, mirando solo lo que se va a enseñar y su margen.</summary>
    /// <remarks>
    /// Un resultado de herramienta puede tener megas de los que se guardan 8.000 caracteres; pasar el
    /// detector por el texto entero era lo más caro de leer una sesión.
    /// </remarks>
    internal static string? RedactClip(string? text, int max = MaxText)
    {
        if (text is null || text.Length <= max + RedactMargin) return Clip(SecretDetector.Redact(text), max);
        var shown = SecretDetector.Redact(text[..(max + RedactMargin)])!;
        return (shown.Length <= max ? shown : shown[..max]) + $"\n… ({text.Length - max:N0} caracteres más)";
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex AnyTag();

    [GeneratedRegex("</?pasted_content[^>]*>")]
    private static partial Regex PastedTag();
}

/// <summary>Lee una sesión poco a poco: cada <see cref="Update"/> interpreta solo lo nuevo de sus transcripts.</summary>
/// <remarks>
/// Recuerda hasta qué byte ha leído cada fichero, el principal y los de sus subagentes, y solo toma
/// líneas completas: Claude Code puede estar escribiendo la última. Así una sesión abierta se puede
/// seguir mientras trabaja sin releerla entera. Los resultados que llegan después se casan con su
/// llamada ya leída, y los subagentes nuevos se cuelgan de ella en cuanto aparecen.
/// </remarks>
public sealed partial class SessionTraceBuilder(string transcriptPath)
{
    private readonly List<TraceStep> _steps = [];

    // Último aviso de fin de cada subagente en segundo plano, por su id.
    private readonly Dictionary<string, DateTime> _finished = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TraceStep> _calls = new(StringComparer.Ordinal);
    private readonly List<TraceAgent> _agents = [];
    private readonly Dictionary<string, long> _offsets = new(StringComparer.OrdinalIgnoreCase);

    // Subagentes ya colgados, por su fichero, con la interacción a la que van sus pasos.
    private readonly Dictionary<string, (TraceAgent Agent, int Interaction)> _known = new(StringComparer.OrdinalIgnoreCase);

    // Interacción en curso del transcript principal: sube con cada prompt o comando de la persona.
    private int _current;
    private string? _cwd;
    private string? _branch;
    private string? _custom;
    private string? _ai;
    private long? _context;

    public string TranscriptPath => transcriptPath;

    /// <summary>Lee lo nuevo de la sesión y de sus subagentes; true si ha cambiado algo.</summary>
    /// <param name="final">
    /// Si es la última lectura: un subagente cuya llamada aún no se ha leído va por su hora en lugar de
    /// esperar. En directo la llamada suele llegar en la lectura siguiente, así que se espera.
    /// </param>
    public bool Update(bool final = false)
    {
        var changed = ReadNew(transcriptPath, e =>
        {
            ReadTitle(e);
            Parse(e, null, null);
        });

        foreach (var (file, (agent, interaction)) in _known)
        {
            if (!ReadNew(file, e =>
                {
                    Parse(e, agent.Id, interaction);
                    Stretch(agent, TranscriptReader.Timestamp(e));
                })) continue;
            changed = true;
            FillModel(agent);
        }

        // Un agente lanzado por otro solo se puede colgar cuando ya se han leído los pasos de su padre: por eso se repite.
        var pending = TranscriptReader.SubagentFiles(transcriptPath)
            .Where(file => !_known.ContainsKey(file))
            .Select(file => (File: file, Meta: SessionTraceReader.ReadMeta(file)))
            .ToList();
        while (true)
        {
            var index = pending.FindIndex(p => p.Meta.ToolUseId is { } use && _calls.ContainsKey(use));
            if (index < 0) index = pending.FindIndex(p => final || p.Meta.ToolUseId is null);
            if (index < 0) break;
            var (file, meta) = pending[index];
            pending.RemoveAt(index);
            AddAgent(file, meta);
            changed = true;
        }
        return changed;
    }

    /// <summary>La sesión tal como va: los pasos en el orden en que pasaron, sus subagentes y sus interacciones.</summary>
    public SessionTrace Snapshot()
    {
        // Por hora y, a igual hora o sin ella, en el orden en que se leyó.
        var ordered = _steps.Select((s, i) => (s, i))
            .OrderBy(x => x.s.StartUtc ?? DateTime.MinValue).ThenBy(x => x.i)
            .Select(x => x.s).ToList();
        for (var i = 0; i < ordered.Count; i++) ordered[i].Order = i;
        var now = DateTime.UtcNow;
        foreach (var agent in _agents)
        {
            agent.FinishedUtc = _finished.TryGetValue(agent.Id, out var done) ? done : null;
            agent.Busy = agent.IsBusy(now);
        }
        return new SessionTrace
        {
            SessionId = Path.GetFileNameWithoutExtension(transcriptPath),
            Title = _custom ?? _ai,
            Cwd = _cwd,
            Branch = _branch,
            Steps = ordered,
            Agents = [.. _agents],
            Interactions = SessionTraceReader.Summarize(ordered),
            ContextTokens = _context,
        };
    }

    /// <summary>Cuelga un subagente de la llamada que lo lanzó, o de la interacción de su hora si no la tiene, y lee sus pasos.</summary>
    private void AddAgent(string file, AgentMeta meta)
    {
        var id = Path.GetFileNameWithoutExtension(file);
        // Hace falta su primera hora antes de leer sus pasos: se guardan sus entradas, copiadas. Un subagente pesa poco.
        var entries = new List<JsonElement>();
        ReadNew(file, e => entries.Add(e.Clone()));
        var call = meta.ToolUseId is { } use && _calls.TryGetValue(use, out var found) ? found : null;
        if (call is not null) call.ChildAgentId = id;

        var first = entries.Select(TranscriptReader.Timestamp).OfType<DateTime>().DefaultIfEmpty(DateTime.MaxValue).Min();
        var interaction = call?.Interaction
                          ?? _steps.Where(s => s.Kind is TraceKind.Prompt or TraceKind.Command && s.StartUtc <= first)
                              .Select(s => s.Interaction).DefaultIfEmpty(0).Max();
        var agent = new TraceAgent
        {
            Id = id,
            Type = meta.Type ?? call?.Title ?? "agente",
            Description = meta.Description,
            Depth = meta.Depth ?? 1,
            // Sin llamada (un agente que lanza una skill con contexto propio), el padre lo dice el .meta.json.
            ParentAgentId = call is not null ? call.AgentId : meta.ParentAgentId,
            Background = meta.Background,
            Model = meta.Model is { } model ? Format.ModelName(model) : null,
            Effort = meta.Effort,
        };
        _agents.Add(agent);
        _known[file] = (agent, interaction);
        foreach (var e in entries)
        {
            Parse(e, id, interaction);
            Stretch(agent, TranscriptReader.Timestamp(e));
        }
        FillModel(agent);
    }

    /// <summary>Si el .meta.json no dice el modelo o el esfuerzo del agente, los toma de su primera respuesta.</summary>
    private void FillModel(TraceAgent agent)
    {
        if (agent.Model is null && _steps.FirstOrDefault(s => s.AgentId == agent.Id && s.Model is not null)?.Model is { } model)
            agent.Model = Format.ModelName(model);
        agent.Effort ??= _steps.FirstOrDefault(s => s.AgentId == agent.Id && s.Effort is not null)?.Effort;
    }

    /// <summary>Alarga el inicio y el fin de un agente con la hora de una de sus entradas, si la lleva.</summary>
    private static void Stretch(TraceAgent agent, DateTime? at)
    {
        if (at is not { } time) return;
        if (agent.StartUtc is not { } start || time < start) agent.StartUtc = time;
        if (agent.EndUtc is not { } end || time > end) agent.EndUtc = time;
    }

    /// <summary>Convierte una entrada de un transcript en sus pasos.</summary>
    /// <param name="agentId">Subagente del transcript; null en el de la sesión principal.</param>
    /// <param name="interaction">Interacción fija para todos los pasos de un subagente; null para irlas contando por prompts.</param>
    private void Parse(JsonElement e, string? agentId, int? interaction)
    {
        var type = TranscriptReader.Str(e, "type");
        if (agentId is null && type is "queue-operation" or "user") ReadNotice(e);
        if (type == "attachment")
        {
            if (agentId is null) ReadQueued(e);
            ReadHook(e, agentId);
            return;
        }
        if (type is not ("user" or "assistant")) return;
        if (!e.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object) return;
        if (!msg.TryGetProperty("content", out var content)) return;
        var at = TranscriptReader.Timestamp(e);

        if (type == "user")
        {
            ReadUser(e, content, at, agentId, interaction);
            return;
        }

        var model = TranscriptReader.Str(msg, "model");
        if (model is not null && model.StartsWith('<')) model = null;
        // Las respuestas sintéticas («<synthetic>») no pasan por el modelo y no dicen nada del contexto.
        if (agentId is null && model is not null && SessionTraceReader.ContextTokens(msg) is { } tokens) _context = tokens;
        var skill = TranscriptReader.Str(e, "attributionSkill");
        // «perTurnEffort» es el del turno; «effort» suelto también aparece en lo que se pide a un agente.
        var effort = TranscriptReader.Str(e, "perTurnEffort") ?? TranscriptReader.Str(e, "effort");
        var branch = Branch(e);
        foreach (var block in SessionTraceReader.Blocks(content))
        {
            var step = SessionTraceReader.Assistant(block, model);
            if (step is null) continue;
            (step.AgentId, step.Interaction, step.StartUtc, step.Skill, step.Branch) = (agentId, interaction ?? _current, at, skill, branch);
            if (step.Model is not null) step.Effort = effort;
            _steps.Add(step);
            if (step.ToolUseId is not null) _calls[step.ToolUseId] = step;
        }
    }
    /// <summary>Lee una entrada de la persona: un prompt, un comando, un aviso o los resultados de herramientas.</summary>
    private void ReadUser(JsonElement e, JsonElement content, DateTime? at, string? agentId, int? interaction)
    {
        // Los resultados se casan con su llamada; el resto de bloques de la persona no van en un agente.
        foreach (var block in SessionTraceReader.Blocks(content).Where(b => TranscriptReader.Str(b, "type") == "tool_result"))
        {
            if (TranscriptReader.Str(block, "tool_use_id") is not { } id || !_calls.TryGetValue(id, out var call)) continue;
            call.Result = SessionTraceReader.RedactClip(SessionTraceReader.ResultText(block));
            call.IsError = block.TryGetProperty("is_error", out var err) && err.ValueKind == JsonValueKind.True;
            call.EndUtc = at;
            call.Images = SessionTraceReader.ResultImages(block);
            if (call.Questions is { } questions)
            {
                SessionTraceReader.Answer(questions, e);
                // Lo que la persona pega al contestar va en el mismo mensaje que la respuesta, pero fuera de ella.
                if (SessionTraceReader.Images(SessionTraceReader.Blocks(content)) is { Count: > 0 } pasted) call.Images = [.. call.Images ?? [], .. pasted];
            }
        }

        // Los mensajes de sistema (el cuerpo de una skill, avisos, el resumen de una compactación) y el encargo de un subagente, que ya está en su llamada, no son pasos.
        if (agentId is not null || TranscriptReader.IsSystem(e)) return;
        if (SessionTraceReader.PromptText(content) is not { } text) return;
        if (UserMessage.ShellOutput(text) is { } output)
        {
            // La salida de un comando «!» llega en su propio mensaje, justo detrás: va con él, como el resultado de una llamada.
            if (_steps.LastOrDefault(s => s.Kind == TraceKind.Command && s.Shell) is { Result: null } command)
            {
                command.Result = SessionTraceReader.RedactClip(output.Text);
                command.IsError = output.Failed;
                command.EndUtc = at;
            }
            return;
        }

        var step = SessionTraceReader.UserStep(text);
        if (step is null) return;
        if (interaction is null && step.Kind is TraceKind.Prompt or TraceKind.Command) _current++;
        (step.Interaction, step.StartUtc, step.Branch) = (interaction ?? _current, at, Branch(e));
        if (step.Kind == TraceKind.Prompt) step.Images = SessionTraceReader.Images(SessionTraceReader.Blocks(content));
        _steps.Add(step);
    }

    /// <summary>Lee un mensaje que la persona escribió mientras Claude trabajaba: un prompt de la interacción en curso, con sus imágenes.</summary>
    /// <remarks>Claude Code no lo guarda como mensaje de la persona sino como un adjunto («queued_command») que junta a lo que está haciendo.</remarks>
    private void ReadQueued(JsonElement e)
    {
        if (!e.TryGetProperty("attachment", out var a) || a.ValueKind != JsonValueKind.Object) return;
        if (TranscriptReader.Str(a, "type") != "queued_command" || TranscriptReader.Str(a, "commandMode") is { } mode && mode != "prompt") return;
        if (!a.TryGetProperty("prompt", out var prompt) || SessionTraceReader.PromptText(prompt) is not { } text) return;
        if (SessionTraceReader.UserStep(text) is not { Kind: TraceKind.Prompt } step) return;
        (step.Interaction, step.StartUtc, step.Queued, step.Branch) = (_current, TranscriptReader.Timestamp(e), true, Branch(e));
        step.Images = SessionTraceReader.Images(SessionTraceReader.Blocks(prompt));
        _steps.Add(step);
    }

    /// <summary>Apunta en su paso un hook de Arkko que ha fallado: en la llamada a la que iba o, si no iba a ninguna, en el
    /// último paso del mismo transcript, que es donde estaba la sesión.</summary>
    private void ReadHook(JsonElement e, string? agentId)
    {
        if (!e.TryGetProperty("attachment", out var a) || a.ValueKind != JsonValueKind.Object) return;
        if (HookTrace.Run(a, TranscriptReader.Timestamp(e) ?? DateTime.UtcNow) is not { Ok: false } run) return;
        var step = TranscriptReader.Str(a, "toolUseID") is { } id && _calls.TryGetValue(id, out var call)
            ? call
            : _steps.LastOrDefault(s => s.AgentId == agentId);
        if (step is not null) step.HookError = HookTrace.Describe(run);
    }

    /// <summary>Apunta el aviso de fin de una tarea en segundo plano, si la entrada lo lleva: el de un subagente que ha terminado.</summary>
    /// <remarks>Llega a la sesión principal como un mensaje, o a su cola si estaba trabajando: se toma de los dos sitios.</remarks>
    private void ReadNotice(JsonElement e)
    {
        var text = TranscriptReader.Str(e, "content")
                   ?? (e.TryGetProperty("message", out var msg) ? TranscriptReader.Str(msg, "content") : null);
        if (text is null || !text.StartsWith("<task-notification>", StringComparison.Ordinal)) return;
        if (NoticeId().Match(text) is not { Success: true } match || TranscriptReader.Timestamp(e) is not { } at) return;
        var id = "agent-" + match.Groups[1].Value;
        if (!_finished.TryGetValue(id, out var seen) || at > seen) _finished[id] = at;
    }

    [GeneratedRegex(@"<task-id>([^<]+)</task-id>")]
    private static partial Regex NoticeId();

    /// <summary>Rama de git de una entrada; null si no la lleva o va vacía.</summary>
    /// <remarks>Claude Code la vuelve a mirar en cada entrada: tras un «git switch» de Claude, la siguiente ya trae la nueva.</remarks>
    private static string? Branch(JsonElement e) => TranscriptReader.Str(e, "gitBranch") is { Length: > 0 } branch ? branch : null;

    /// <summary>Carpeta, rama y títulos de la sesión: el que puso la persona y el que generó Claude Code, el último de cada uno.</summary>
    private void ReadTitle(JsonElement e)
    {
        _cwd ??= TranscriptReader.Str(e, "cwd");
        _branch = Branch(e) ?? _branch;
        switch (TranscriptReader.Str(e, "type"))
        {
            case "custom-title": _custom = TranscriptReader.Str(e, "customTitle") ?? _custom; break;
            case "ai-title": _ai = TranscriptReader.Str(e, "aiTitle") ?? _ai; break;
        }
    }

    // Lo que se lee de una vez de un transcript; una línea más larga agranda el trozo lo que haga falta.
    private const int Chunk = 1 << 20;

    /// <summary>Entrega una a una las entradas nuevas de un fichero desde la última lectura; solo las de líneas completas.</summary>
    /// <remarks>
    /// Una última línea sin salto se toma si ya es JSON válido: una línea a medio escribir no lo es. Se lee a
    /// trozos, cada línea se interpreta desde sus bytes y se entrega sin copiarla, así que solo vale mientras se
    /// atiende: cargar entero un transcript de decenas de MB, o guardar todas sus entradas antes de leerlas,
    /// pedía cientos de MB de golpe que la app ya no devolvía.
    /// </remarks>
    /// <returns>Si había alguna entrada nueva.</returns>
    private bool ReadNew(string file, Action<JsonElement> take)
    {
        var any = false;
        try
        {
            // Mirar el tamaño no abre el fichero: en directo se repasan todos los de la sesión, y casi ninguno ha crecido.
            var start = _offsets.GetValueOrDefault(file);
            var info = new FileInfo(file);
            if (!info.Exists || info.Length <= start) return false;
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
            if (fs.Length <= start) return false;
            fs.Seek(start, SeekOrigin.Begin);
            var buffer = ArrayPool<byte>.Shared.Rent(Chunk);
            try
            {
                var filled = 0;
                var done = start;
                while (true)
                {
                    if (filled == buffer.Length)
                    {
                        var bigger = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                        Buffer.BlockCopy(buffer, 0, bigger, 0, filled);
                        ArrayPool<byte>.Shared.Return(buffer);
                        buffer = bigger;
                    }
                    var read = fs.Read(buffer, filled, buffer.Length - filled);
                    if (read == 0) break;
                    filled += read;
                    var from = 0;
                    int newline;
                    while ((newline = Array.IndexOf(buffer, (byte)'\n', from, filled - from)) >= 0)
                    {
                        any |= TakeLine(buffer.AsMemory(from, newline - from), take);
                        from = newline + 1;
                    }
                    done += from;
                    Buffer.BlockCopy(buffer, from, buffer, 0, filled - from);
                    filled -= from;
                }
                _offsets[file] = done;
                if (filled > 0 && TakeLine(buffer.AsMemory(0, filled), take))
                {
                    any = true;
                    _offsets[file] = done + filled;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Si no se puede leer ahora, se intenta en la próxima lectura desde el mismo sitio.
        }
        return any;
    }

    /// <summary>Entrega la entrada de una línea JSON; false si está vacía o no es JSON.</summary>
    /// <remarks>Sin copiarla: solo vale mientras se atiende, porque sus bytes son de un trozo que se reutiliza.</remarks>
    private static bool TakeLine(ReadOnlyMemory<byte> line, Action<JsonElement> take)
    {
        if (line.Span.Trim(" \t\r"u8).IsEmpty) return false;
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return false;
        }
        using (doc) take(doc.RootElement);
        return true;
    }
}
