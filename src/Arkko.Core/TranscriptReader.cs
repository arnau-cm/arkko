using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Arkko.Core;

/// <summary>Calcula el consumo del último prompt a partir del transcript JSONL de la sesión.</summary>
/// <remarks>
/// El formato del transcript no es un contrato estable de Claude Code: todo se lee de forma
/// defensiva y lo que no se reconoce se ignora. Dos detalles importan para no contar de más:
/// una misma respuesta de la API se guarda en varias líneas (una por bloque de contenido) con el
/// mismo message.id y el mismo usage, y los subagentes escriben en ficheros aparte, en
/// &lt;sesión&gt;/subagents/*.jsonl junto al transcript principal.
/// </remarks>
public static class TranscriptReader
{
    /// <summary>Devuelve el consumo del último prompt humano, o null si no se puede calcular.</summary>
    public static TurnUsage? LastTurn(string transcriptPath) => LastTurn(transcriptPath, out _);

    /// <summary>Devuelve el consumo del último prompt humano y con qué empezó; null si no se puede calcular.</summary>
    /// <param name="opening">Si fue un prompt, un comando «!» (con el comando) o un aviso (con su texto).</param>
    public static TurnUsage? LastTurn(string transcriptPath, out (PromptKind Kind, string? Text) opening)
    {
        var entries = ReadEntries(transcriptPath);
        var start = LastHumanPrompt(entries);
        opening = start < 0 ? (PromptKind.Prompt, null) : Opening(entries, start);
        if (start < 0) return null;

        var startUtc = Timestamp(entries[start]);
        var messages = new Dictionary<string, (string Model, ModelTokens Tokens)>();
        var lastUtc = startUtc;

        for (var i = start + 1; i < entries.Count; i++)
            Collect(entries[i], messages, ref lastUtc);

        if (startUtc is not null)
        {
            foreach (var file in SubagentFiles(transcriptPath))
            {
                if (File.GetLastWriteTimeUtc(file) < startUtc.Value) continue;
                DateTime? ignored = null;
                foreach (var entry in ReadEntries(file))
                {
                    var ts = Timestamp(entry);
                    if (ts is not null && ts.Value >= startUtc.Value) Collect(entry, messages, ref ignored);
                }
            }
        }

        var usage = new TurnUsage();
        foreach (var (model, tokens) in messages.Values)
        {
            if (!usage.ByModel.TryGetValue(model, out var acc)) usage.ByModel[model] = acc = new ModelTokens();
            acc.Add(tokens);
        }

        if (startUtc is not null && lastUtc is not null)
            usage.DurationMs = (long)Math.Max(0, (lastUtc.Value - startUtc.Value).TotalMilliseconds);
        return usage;
    }

    /// <summary>Último recap que Claude Code escribió en la sesión, con su hora, o null si no hay.</summary>
    /// <remarks>Lo lleva el índice del transcript, que solo lee lo nuevo: ver <see cref="SessionCatalog.Recap"/>.</remarks>
    public static (string Text, DateTime Utc)? LatestRecap(string transcriptPath) => SessionCatalog.Recap(transcriptPath);

    /// <summary>Lee y parsea las líneas de un JSONL que Claude Code puede estar escribiendo.</summary>
    internal static List<JsonElement> ReadEntries(string path)
    {
        var result = new List<JsonElement>();
        if (!File.Exists(path)) return result;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(fs, Encoding.UTF8);
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                result.Add(doc.RootElement.Clone());
            }
            catch (JsonException) { }
        }
        return result;
    }

    /// <summary>Índice de la última entrada escrita por la persona, sin contar resultados de herramientas.</summary>
    internal static int LastHumanPrompt(List<JsonElement> entries)
    {
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            var e = entries[i];
            if (Str(e, "type") != "user") continue;
            if (IsSystem(e)) continue;
            if (!e.TryGetProperty("message", out var msg) || !msg.TryGetProperty("content", out var content)) continue;

            if (content.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(content.GetString()))
                return i;
            if (content.ValueKind == JsonValueKind.Array &&
                content.EnumerateArray().All(b => Str(b, "type") != "tool_result"))
                return i;
        }
        return -1;
    }

    /// <summary>Con qué empezó el turno que abre una entrada: un prompt, un comando «!» con su comando, o un aviso con su texto.</summary>
    /// <remarks>
    /// Un comando «!» deja dos mensajes, el comando y su salida, y el turno empieza en el de la salida: el comando se
    /// busca justo antes, sin pasar de otro mensaje de la persona ni de una respuesta de Claude.
    /// </remarks>
    internal static (PromptKind Kind, string? Text) Opening(List<JsonElement> entries, int start)
    {
        var text = UserText(entries[start]);
        if (!UserMessage.IsShellOutput(text)) return (UserMessage.KindOf(text), text);
        for (var i = start - 1; i >= 0; i--)
        {
            var type = Str(entries[i], "type");
            if (type == "assistant") break;
            if (type != "user") continue;
            var previous = UserText(entries[i]);
            if (UserMessage.KindOf(previous) == PromptKind.Shell) return (PromptKind.Shell, UserMessage.ShellCommand(previous));
            break;
        }
        return (PromptKind.Shell, null);
    }

    /// <summary>Texto de un mensaje de la persona, de cadena o de bloques; null si no lo tiene.</summary>
    private static string? UserText(JsonElement entry) =>
        entry.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.Object && msg.TryGetProperty("content", out var content)
            ? SessionTraceReader.PromptText(content)
            : null;

    /// <summary>Acumula el usage de una entrada de asistente, una sola vez por message.id.</summary>
    private static void Collect(JsonElement entry, Dictionary<string, (string, ModelTokens)> messages, ref DateTime? lastUtc)
    {
        if (!entry.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object) return;
        if (!msg.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object) return;

        var model = Str(msg, "model") ?? "?";
        if (model.StartsWith('<')) return;

        var id = Str(msg, "id") ?? Str(entry, "uuid") ?? Guid.NewGuid().ToString();
        messages[id] = (model, new ModelTokens
        {
            Input = Num(usage, "input_tokens"),
            Output = Num(usage, "output_tokens"),
            CacheWrite = Num(usage, "cache_creation_input_tokens"),
            CacheRead = Num(usage, "cache_read_input_tokens"),
            ApiCalls = 1,
        });

        var ts = Timestamp(entry);
        if (ts is not null && (lastUtc is null || ts > lastUtc)) lastUtc = ts;
    }

    /// <summary>Ficheros de subagentes de la sesión, si existen.</summary>
    internal static IEnumerable<string> SubagentFiles(string transcriptPath)
    {
        var dir = Path.Combine(Path.GetDirectoryName(transcriptPath) ?? "",
            Path.GetFileNameWithoutExtension(transcriptPath), "subagents");
        return Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*.jsonl") : [];
    }

    /// <summary>Marca de tiempo UTC de una entrada, si la tiene.</summary>
    internal static DateTime? Timestamp(JsonElement entry)
    {
        var raw = Str(entry, "timestamp");
        return raw is not null && DateTime.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var ts)
            ? ts
            : null;
    }

    /// <summary>Valor de texto de una propiedad, o null.</summary>
    internal static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    /// <summary>Indica si una entrada de la persona la escribió Claude Code: un mensaje de sistema o el resumen de una compactación.</summary>
    internal static bool IsSystem(JsonElement e) =>
        e.ValueKind == JsonValueKind.Object
        && (e.TryGetProperty("isMeta", out var meta) && meta.ValueKind == JsonValueKind.True
            || e.TryGetProperty("isCompactSummary", out var summary) && summary.ValueKind == JsonValueKind.True);

    /// <summary>Valor numérico entero de una propiedad, o 0.</summary>
    private static long Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;
}
