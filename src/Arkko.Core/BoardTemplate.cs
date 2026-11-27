using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Arkko.Core;

/// <summary>Rellena las plantillas de un perfil de tablero: «{baseUrl}», «{project.id}», y «{cursor?}», que puede faltar.</summary>
/// <remarks>
/// Un valor que falta sin «?» es un error de configuración: se dice cuál, en lugar de llamar a una
/// dirección a medias. En la dirección, los valores van codificados para URL; en el cuerpo, tal cual.
/// </remarks>
public static partial class BoardTemplate
{
    [GeneratedRegex(@"\{([A-Za-z0-9_.]+)(\?)?\}")]
    private static partial Regex Placeholder();

    /// <summary>Rellena un texto; los que faltan con «?» quedan vacíos.</summary>
    public static string Fill(string template, Func<string, string?> value, bool forUrl)
    {
        return Placeholder().Replace(template, m =>
        {
            var name = m.Groups[1].Value;
            var v = value(name);
            if (v is null)
            {
                if (m.Groups[2].Success) return "";
                throw new BoardException($"Falta el valor «{name}».");
            }
            return forUrl ? Uri.EscapeDataString(v) : v;
        });
    }

    /// <summary>Rellena una dirección: como un texto, salvo que el primer valor, la base, va sin codificar.</summary>
    /// <remarks>La base («{baseUrl}») ya es una dirección, con sus barras y sus dos puntos: codificarla la rompería.</remarks>
    public static string FillUrl(string template, Func<string, string?> value)
    {
        var first = Placeholder().Match(template);
        if (!first.Success || first.Index != 0) return Fill(template, value, forUrl: true);
        var head = Fill(first.Value, value, forUrl: false).TrimEnd('/');
        return head + Fill(template[first.Length..], value, forUrl: true);
    }

    /// <summary>Copia del cuerpo con las plantillas rellenas; una propiedad que es solo «{x?}» y no tiene valor se quita.</summary>
    public static JsonNode? FillBody(JsonNode? body, Func<string, string?> value)
    {
        if (body is null) return null;
        var copy = body.DeepClone();
        Walk(copy, value);
        return copy;
    }

    /// <summary>Recorre el cuerpo y rellena cada cadena, quitando las opcionales vacías.</summary>
    private static void Walk(JsonNode node, Func<string, string?> value)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, child) in obj.ToList())
                {
                    if (child is JsonValue v && v.TryGetValue<string>(out var text))
                    {
                        if (IsMissingOptional(text, value)) obj.Remove(key);
                        else obj[key] = Fill(text, value, forUrl: false);
                    }
                    else if (child is not null)
                    {
                        Walk(child, value);
                    }
                }
                break;
            case JsonArray array:
                for (var i = array.Count - 1; i >= 0; i--)
                {
                    if (array[i] is JsonValue v && v.TryGetValue<string>(out var text))
                    {
                        if (IsMissingOptional(text, value)) array.RemoveAt(i);
                        else array[i] = Fill(text, value, forUrl: false);
                    }
                    else if (array[i] is { } child)
                    {
                        Walk(child, value);
                    }
                }
                break;
        }
    }

    /// <summary>Indica si el texto es solo una plantilla opcional sin valor.</summary>
    private static bool IsMissingOptional(string text, Func<string, string?> value)
    {
        var m = Placeholder().Match(text);
        return m.Success && m.Length == text.Length && m.Groups[2].Success && value(m.Groups[1].Value) is null;
    }
}

/// <summary>Error de un tablero que se le puede enseñar a la persona: configuración incompleta, perfil mal escrito o respuesta inesperada.</summary>
public sealed class BoardException(string message, Exception? inner = null) : Exception(message, inner);
