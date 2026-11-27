using System.Text.Json;
using System.Text.RegularExpressions;

namespace Arkko.Core;

/// <summary>Una vez que la sesión tocó un fichero: en qué prompt, quién, cómo, cuántas líneas y lo que dijo después.</summary>
/// <param name="Order">El paso que lo tocó, para abrir su ficha.</param>
/// <param name="Interaction">El número del prompt en que pasó.</param>
/// <param name="Who">La letra del actor: B Claude, C y siguientes sus agentes.</param>
/// <param name="Verb">Cómo: «Edita», «Escribe» (el fichero entero) o «Comando», si lo cambió un comando de terminal.</param>
/// <param name="Added">Líneas añadidas; en un comando no se sabe y va a 0.</param>
/// <param name="Removed">Líneas quitadas; en un comando no se sabe y va a 0.</param>
/// <param name="Reply">La primera línea de lo que dijo Claude después, en el mismo prompt; null si no dijo nada.</param>
public sealed record FileTouch(int Order, int Interaction, string Who, string Verb, int Added, int Removed, DateTime? AtUtc, string? Reply);

/// <summary>La historia de un fichero en una sesión: cada paso que lo cambió, sacado de lo leído de ella.</summary>
/// <remarks>
/// Las ediciones se reconocen por la ruta que llevan; un comando, solo si nombra el fichero y escribe (un <c>sed -i</c>, una
/// redirección, un script que guarda): lo que cambia un comando sin nombrarlo no se puede atribuir.
/// </remarks>
public static partial class FileHistory
{
    private const int ReplyMax = 140;

    // Lo que delata que un comando escribe en el fichero que nombra, y no solo lo lee.
    private static readonly string[] WriteMarks = ["sed -i", " > ", ">>", "Set-Content", "Out-File", "Add-Content", "WriteAllText", "WriteAllBytes", ".write(", "mv ", "cp ", "git checkout", "git restore", "git mv", "Move-Item", "Copy-Item"];

    /// <summary>Devuelve las veces que la sesión cambió un fichero dado por su ruta dentro del repositorio, como la dan los
    /// cambios, de la primera a la última.</summary>
    public static IReadOnlyList<FileTouch> OfRepoFile(SessionTrace trace, string repoPath) =>
        Of(trace, GitChanges.RootOf(trace.Cwd, out _) is { } root ? Path.Combine(root, repoPath) : repoPath);

    /// <summary>Devuelve las veces que la sesión cambió un fichero, de la primera a la última.</summary>
    /// <param name="path">El fichero: su ruta entera o relativa a la carpeta de la sesión.</param>
    public static IReadOnlyList<FileTouch> Of(SessionTrace trace, string path)
    {
        var target = Normal(Full(trace.Cwd, path));
        var name = Path.GetFileName(target);
        var letters = SessionClock.Letters(trace);
        var steps = trace.Steps.OrderBy(s => s.Order).ToList();
        var touches = new List<FileTouch>();
        for (var i = 0; i < steps.Count; i++)
        {
            var s = steps[i];
            if (s.Kind != TraceKind.Tool || s.IsError) continue;
            var tool = SessionClock.ToolName(s.Title);
            if (Input(s.Text) is not { } input)
            {
                // Una entrada grande se guarda recortada y ya no es JSON: el fichero se saca de su principio, sin las líneas.
                if (tool is "Edit" or "MultiEdit" or "Write" && ClippedPath(s.Text) is { } clipped && string.Equals(Normal(Full(trace.Cwd, clipped)), target, StringComparison.OrdinalIgnoreCase))
                    touches.Add(new FileTouch(s.Order, s.Interaction, WhoOf(s, letters), tool == "Write" ? "Escribe" : "Edita", 0, 0, s.StartUtc, ReplyAfter(steps, i)));
                continue;
            }
            (string Verb, int Added, int Removed)? touch = tool switch
            {
                "Edit" or "MultiEdit" when Same(input, trace.Cwd, target) => Edits(input),
                "Write" when Same(input, trace.Cwd, target) => ("Escribe", Lines(Str(input, "content")), 0),
                "NotebookEdit" when Same(input, trace.Cwd, target) => ("Edita", Lines(Str(input, "new_source")), 0),
                "Bash" or "PowerShell" when Str(input, "command") is { } command && Writes(command, name) => ("Comando", 0, 0),
                _ => null,
            };
            if (touch is not { } t) continue;
            touches.Add(new FileTouch(s.Order, s.Interaction, WhoOf(s, letters), t.Verb, t.Added, t.Removed, s.StartUtc, ReplyAfter(steps, i)));
        }
        return touches;
    }

    /// <summary>La letra de quien dio un paso: B la sesión, C y siguientes sus agentes.</summary>
    private static string WhoOf(TraceStep s, Dictionary<string, string> letters) =>
        s.AgentId is { } agent && letters.TryGetValue(agent, out var letter) ? letter : "B";

    /// <summary>El fichero de una entrada recortada, del campo con que empieza; null si no se encuentra.</summary>
    private static string? ClippedPath(string? text)
    {
        if (text is null || PathField().Match(text) is not { Success: true } match) return null;
        try
        {
            return JsonSerializer.Deserialize<string>($"\"{match.Groups[1].Value}\"");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"""(?:file_path|notebook_path)""\s*:\s*""((?:[^""\\]|\\.)*)""")]
    private static partial Regex PathField();

    /// <summary>Las líneas que añade y quita una edición, o todas las de una edición múltiple.</summary>
    private static (string, int, int) Edits(JsonElement input)
    {
        var edits = input.TryGetProperty("edits", out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().ToList() : [input];
        var (added, removed) = (0, 0);
        foreach (var e in edits)
        {
            var (a, r) = LineDiff(Str(e, "old_string") ?? "", Str(e, "new_string") ?? "");
            added += a;
            removed += r;
        }
        return ("Edita", added, removed);
    }

    /// <summary>Cuántas líneas añade y quita cambiar un texto por otro, sin contar las de principio y final que no cambian.</summary>
    internal static (int Added, int Removed) LineDiff(string before, string after)
    {
        var a = Split(before);
        var b = Split(after);
        var start = 0;
        while (start < a.Length && start < b.Length && a[start] == b[start]) start++;
        var end = 0;
        while (end < a.Length - start && end < b.Length - start && a[^(end + 1)] == b[^(end + 1)]) end++;
        return (b.Length - start - end, a.Length - start - end);
    }

    /// <summary>La primera línea de lo siguiente que dijo Claude en el mismo prompt, sin contar a sus agentes.</summary>
    private static string? ReplyAfter(List<TraceStep> steps, int from)
    {
        var interaction = steps[from].Interaction;
        for (var i = from + 1; i < steps.Count && steps[i].Interaction == interaction; i++)
        {
            if (steps[i].Kind != TraceKind.Reply || steps[i].AgentId is not null || string.IsNullOrWhiteSpace(steps[i].Text)) continue;
            var line = steps[i].Text!.Trim().Split('\n', 2)[0].Trim();
            return line.Length > ReplyMax ? line[..(ReplyMax - 1)] + "…" : line;
        }
        return null;
    }

    /// <summary>Si un comando escribe en el fichero: lo nombra y hace algo que escribe.</summary>
    private static bool Writes(string command, string name) =>
        command.Contains(name, StringComparison.OrdinalIgnoreCase) && WriteMarks.Any(w => command.Contains(w, StringComparison.OrdinalIgnoreCase));

    /// <summary>Si la llamada es sobre el fichero buscado.</summary>
    private static bool Same(JsonElement input, string? cwd, string target) =>
        (Str(input, "file_path") ?? Str(input, "notebook_path")) is { } file && string.Equals(Normal(Full(cwd, file)), target, StringComparison.OrdinalIgnoreCase);

    /// <summary>Una ruta entera: tal cual si ya lo es, o dentro de la carpeta de la sesión.</summary>
    private static string Full(string? cwd, string path) =>
        Path.IsPathRooted(path) || cwd is null ? path : Path.Combine(cwd, path);

    /// <summary>Una ruta con las barras de un solo tipo y sin la final, para comparar.</summary>
    private static string Normal(string path) => path.Replace('\\', '/').TrimEnd('/');

    /// <summary>La entrada de una llamada como JSON; null si no la tiene o no se entiende.</summary>
    private static JsonElement? Input(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Un campo de texto de un objeto JSON; null si no está.</summary>
    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Las líneas de un texto; ninguna si está vacío.</summary>
    private static string[] Split(string text) => text.Length == 0 ? [] : text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

    /// <summary>Cuántas líneas tiene un texto.</summary>
    private static int Lines(string? text) => Split(text ?? "").Length;
}
