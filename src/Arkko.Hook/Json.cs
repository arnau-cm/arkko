using System.Text.Json;

namespace Arkko.Hook;

/// <summary>Lectura tolerante de rutas dentro de un JSON cuyo formato puede cambiar.</summary>
internal static class Json
{
    /// <summary>Devuelve el elemento en la ruta indicada, o null si falta algún tramo.</summary>
    public static JsonElement? Get(JsonElement root, params string[] path)
    {
        var cur = root;
        foreach (var name in path)
        {
            if (cur.ValueKind != JsonValueKind.Object || !cur.TryGetProperty(name, out cur)) return null;
        }
        return cur;
    }

    /// <summary>Texto en la ruta indicada, o null.</summary>
    public static string? Str(JsonElement root, params string[] path) =>
        Get(root, path) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    /// <summary>Número en la ruta indicada, o null.</summary>
    public static double? Num(JsonElement root, params string[] path) =>
        Get(root, path) is { ValueKind: JsonValueKind.Number } v ? v.GetDouble() : null;
}
