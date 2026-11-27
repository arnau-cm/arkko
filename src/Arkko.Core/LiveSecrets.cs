using System.Text.Json;

namespace Arkko.Core;

/// <summary>Secretos ya resueltos en Seguridad (falso positivo o rotado), para no volver a avisar de ellos.</summary>
/// <remarks>
/// Abre el almacén de Seguridad la primera vez que hace falta, porque casi ninguna herramienta lleva
/// secretos. Si no se puede abrir, no descarta nada: mejor un aviso de más que uno de menos.
/// </remarks>
public sealed class SettledSecrets(string? dataDir = null)
{
    private readonly Lazy<(Func<string, string>? Fingerprint, HashSet<string> Settled)> _store = new(() =>
    {
        try
        {
            var scanner = new SecretScanner(dataDir);
            return (scanner.Fingerprint, scanner.Settled());
        }
        catch (Exception ex)
        {
            JsonStore.LogError("security-live", ex);
            return (null, []);
        }
    });

    /// <summary>Huella del valor, la misma que guarda Seguridad; null si el almacén no se pudo abrir.</summary>
    public string? Fingerprint(string value) => _store.Value.Fingerprint?.Invoke(value);

    /// <summary>Indica si el secreto de esa huella ya está resuelto.</summary>
    public bool Contains(string fingerprint) => _store.Value.Settled.Contains(fingerprint);
}

/// <summary>Detección en el momento, desde los hooks: qué secreto acaba de pasar por una herramienta o un prompt.</summary>
/// <remarks>
/// Solo avisa: los hallazgos los sigue guardando el análisis de los transcripts, que es la fuente de la
/// sección Seguridad. Los textos llevan el tipo y el origen, nunca el valor. Con los resueltos a mano,
/// se saltan los que ya están en Seguridad como falso positivo o rotados, y las huellas de los que
/// quedan se apuntan para que la tarjeta retire el aviso si se resuelven después.
/// </remarks>
public static class LiveSecrets
{
    /// <summary>Aviso de los secretos de un PostToolUse, en su entrada o en su salida; null si no hay ninguno.</summary>
    /// <returns>«Token de GitHub (salida de Read · .env)», con varios separados por «; ».</returns>
    public static string? ForTool(JsonElement root, SettledSecrets? settled = null, List<string>? fingerprints = null)
    {
        var tool = Str(root, "tool_name") ?? "herramienta";
        var input = root.TryGetProperty("tool_input", out var i) ? i : default;
        var file = input.ValueKind == JsonValueKind.Object
            ? Str(input, "file_path") ?? Str(input, "path") ?? Str(input, "notebook_path")
            : null;
        var where = file is null ? "" : " · " + Path.GetFileName(file);

        var parts = new List<string>();
        Add(parts, Kinds(Ends(Text(input)), settled, fingerprints), $"entrada de {tool}{where}");
        Add(parts, Kinds(Ends(root.TryGetProperty("tool_response", out var r) ? Sent(r) : null), settled, fingerprints), $"salida de {tool}{where}");
        return parts.Count == 0 ? null : string.Join("; ", parts);
    }

    /// <summary>Tipos de secreto de un texto, sin repetir y sin los ya resueltos; vacío si no queda ninguno.</summary>
    /// <param name="fingerprints">Si se pasa, recibe las huellas de los secretos que quedan.</param>
    public static List<string> Kinds(string? text, SettledSecrets? settled = null, List<string>? fingerprints = null)
    {
        var kinds = new List<string>();
        foreach (var m in SecretDetector.Scan(text))
        {
            var fingerprint = settled?.Fingerprint(m.Value);
            if (fingerprint is not null && settled!.Contains(fingerprint)) continue;
            if (fingerprint is not null && fingerprints is not null && !fingerprints.Contains(fingerprint)) fingerprints.Add(fingerprint);
            if (!kinds.Contains(m.Kind)) kinds.Add(m.Kind);
        }
        return kinds;
    }

    /// <summary>Aviso de un prompt enviado con secretos: «Token de GitHub (tu mensaje)».</summary>
    public static string ForPrompt(IReadOnlyList<string> kinds) => $"{string.Join(", ", kinds)} (tu mensaje)";

    /// <summary>Motivo que enseña Claude Code al frenar un prompt: qué lleva, qué hacer y cómo quitar el bloqueo.</summary>
    public static string BlockReason(IReadOnlyList<string> kinds) =>
        $"Arkko ha frenado este prompt porque lleva un secreto: {string.Join(", ", kinds)}. Quítalo y vuelve a enviarlo. " +
        "El bloqueo se desactiva en Arkko, Configuración → Seguridad; desactivado, lo que envíes es responsabilidad tuya.";

    /// <summary>Línea que se enseña en la sesión al ver un secreto enviado.</summary>
    public static string Message(string alert) => $"⚠ Arkko: se ha enviado un secreto a la API: {alert}. Revísalo en Seguridad.";

    /// <summary>Añade el aviso de un origen si le queda algún secreto.</summary>
    private static void Add(List<string> parts, List<string> kinds, string origin)
    {
        if (kinds.Count > 0) parts.Add($"{string.Join(", ", kinds)} ({origin})");
    }

    // Lo que se mira de cada punta de una entrada o una salida enorme.
    internal const int EndChars = 256 * 1024;

    /// <summary>El principio y el final de un texto enorme, o el texto entero si no lo es.</summary>
    /// <remarks>
    /// Cuando el hook ve la salida de una herramienta ya está en la conversación: el aviso solo informa. Mirar
    /// megas en cada herramienta retrasaba a Claude Code; lo de en medio lo encuentra igual el análisis de los
    /// transcripts, que lo lee todo.
    /// </remarks>
    private static string? Ends(string? text) =>
        text is null || text.Length <= 2 * EndChars ? text : string.Concat(text.AsSpan(0, EndChars), "\n", text.AsSpan(text.Length - EndChars));

    // Campos de la respuesta de una herramienta que Claude Code guarda para sí y no manda al modelo: el fichero
    // entero antes de editarlo y los diffs. Con ellos, editar un fichero con secretos de prueba avisaba de algo
    // que nunca salió del equipo, y el análisis de los transcripts, que mira lo enviado, no lo encontraba.
    private static readonly HashSet<string> LocalOnly = ["originalFile", "structuredPatch", "bashEditDiff"];

    /// <summary>El texto de la respuesta de una herramienta que llega al modelo: todo salvo lo que Claude Code se guarda para sí.</summary>
    private static string? Sent(JsonElement response) => response.ValueKind == JsonValueKind.Object
        ? string.Join("\n", response.EnumerateObject().Where(p => !LocalOnly.Contains(p.Name)).Select(p => Text(p.Value)).OfType<string>())
        : Text(response);

    /// <summary>El texto de un valor JSON: el propio si es una cadena, o todas sus cadenas si es un objeto o una lista.</summary>
    private static string? Text(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Undefined or JsonValueKind.Null => null,
        JsonValueKind.String => e.GetString(),
        _ => SecretScanner.Strings(e),
    };

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
