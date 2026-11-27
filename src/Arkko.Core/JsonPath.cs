using System.Text.Json;

namespace Arkko.Core;

/// <summary>Rutas JSON sencillas para sacar datos de una respuesta: «$.a.b», «$.a[*].b», «$.a[0]» y «$['a b']».</summary>
/// <remarks>
/// Es lo justo para los perfiles de tablero, sin filtros ni expresiones: una librería entera sería una
/// DLL más que Smart App Control tendría que aceptar, y los perfiles solo necesitan bajar por la respuesta.
/// </remarks>
public static class JsonPath
{
    /// <summary>Todos los elementos a los que lleva la ruta; vacío si no lleva a ninguno.</summary>
    public static IEnumerable<JsonElement> Select(JsonElement root, string path)
    {
        IEnumerable<JsonElement> current = [root];
        foreach (var segment in Parse(path))
            current = current.SelectMany(e => Step(e, segment)).ToList();
        return current;
    }

    /// <summary>El primer elemento al que lleva la ruta; null si no lleva a ninguno o es null.</summary>
    public static JsonElement? First(JsonElement root, string path)
    {
        foreach (var e in Select(root, path))
            if (e.ValueKind != JsonValueKind.Null) return e;
        return null;
    }

    /// <summary>El texto del primer elemento de la ruta: una cadena tal cual, un número o un booleano escritos; null si no hay.</summary>
    public static string? String(JsonElement root, string path) => First(root, path) is { } e ? Text(e) : null;

    /// <summary>Los textos de todos los elementos de la ruta que tengan valor.</summary>
    public static List<string> Strings(JsonElement root, string path) =>
        Select(root, path).Select(Text).OfType<string>().ToList();

    /// <summary>Texto de un valor simple; null para null, objetos y listas.</summary>
    private static string? Text(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null,
    };

    /// <summary>Un paso de la ruta: entrar en una propiedad, en todos los elementos de una lista o en uno por su posición.</summary>
    private static IEnumerable<JsonElement> Step(JsonElement e, Segment s)
    {
        if (s.All)
        {
            if (e.ValueKind == JsonValueKind.Array) return e.EnumerateArray().ToList();
            if (e.ValueKind == JsonValueKind.Object) return e.EnumerateObject().Select(p => p.Value).ToList();
            return [];
        }
        if (s.Index is { } i)
            return e.ValueKind == JsonValueKind.Array && i < e.GetArrayLength() ? [e[i]] : [];
        return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(s.Name!, out var v) ? [v] : [];
    }

    private sealed record Segment(string? Name, int? Index, bool All);

    /// <summary>Parte una ruta en sus pasos; una ruta mal escrita es un error del perfil y se dice cuál.</summary>
    private static List<Segment> Parse(string path)
    {
        if (!path.StartsWith('$')) throw new FormatException($"La ruta «{path}» debe empezar por $.");
        var segments = new List<Segment>();
        var i = 1;
        while (i < path.Length)
        {
            if (path[i] == '.')
            {
                var end = i + 1;
                while (end < path.Length && path[end] != '.' && path[end] != '[') end++;
                if (end == i + 1) throw new FormatException($"La ruta «{path}» tiene un paso vacío.");
                segments.Add(new Segment(path[(i + 1)..end], null, false));
                i = end;
            }
            else if (path[i] == '[')
            {
                var close = path.IndexOf(']', i);
                if (close < 0) throw new FormatException($"La ruta «{path}» no cierra un corchete.");
                var inside = path[(i + 1)..close].Trim();
                if (inside == "*") segments.Add(new Segment(null, null, true));
                else if (int.TryParse(inside, out var index) && index >= 0) segments.Add(new Segment(null, index, false));
                else if (inside.Length >= 2 && inside[0] == '\'' && inside[^1] == '\'') segments.Add(new Segment(inside[1..^1], null, false));
                else throw new FormatException($"La ruta «{path}» tiene un corchete que no se entiende: [{inside}].");
                i = close + 1;
            }
            else
            {
                throw new FormatException($"La ruta «{path}» no se entiende a partir de «{path[i..]}».");
            }
        }
        return segments;
    }
}
