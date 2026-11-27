using System.Text.Json;

namespace Arkko.Core;

/// <summary>Un tema de colores para el código: su id, cómo se llama, si es claro u oscuro (null si sigue a la app) y su
/// muestra para los menús.</summary>
public sealed record CodeTheme(string Id, string Name, string? Tone, string? Hint, CodeSample? Sample = null);

/// <summary>La muestra de un tema: su fondo, su borde y sus colores principales (palabra clave, método, tipo, cadena y
/// comentario), en «#rrggbb».</summary>
public sealed record CodeSample(string Background, string Edge, IReadOnlyList<string> Colors);

/// <summary>Un lenguaje que la página de Sesiones colorea: su id, cómo se llama, sus extensiones y su tema de serie.</summary>
public sealed record CodeLanguage(string Id, string Name, IReadOnlyList<string> Extensions, string DefaultTheme);

/// <summary>Los lenguajes y los temas del código, leídos de <c>Web/codigo.json</c>, junto a la página de Sesiones.</summary>
/// <remarks>
/// Ese fichero es el único sitio donde se dan de alta: la página lo recibe entero, con lo que necesita para colorear
/// (palabras clave, comentarios, motor), y la ventana solo lee aquí los nombres y los temas de serie, para Configuración.
/// Añadir un lenguaje o un tema es añadir su entrada; un tema necesita además sus colores en <c>sesiones.css</c>.
/// </remarks>
public sealed class CodeCatalog
{
    /// <summary>El tema que sigue a la app: oscuro o claro según su diseño.</summary>
    public const string AppTheme = "app";

    public IReadOnlyList<CodeTheme> Themes { get; }
    public IReadOnlyList<CodeLanguage> Languages { get; }

    /// <summary>El fichero tal cual, para mandárselo a la página.</summary>
    public JsonElement Raw { get; }

    private CodeCatalog(IReadOnlyList<CodeTheme> themes, IReadOnlyList<CodeLanguage> languages, JsonElement raw)
    {
        Themes = themes;
        Languages = languages;
        Raw = raw;
    }

    /// <summary>Un catálogo vacío, para cuando no se puede leer el fichero: el código se ve sin colores.</summary>
    public static CodeCatalog Empty { get; } = new([new CodeTheme(AppTheme, "Como la app", null, null)], [], JsonDocument.Parse("{}").RootElement.Clone());

    /// <summary>Lee el catálogo de su fichero; si no existe o no se entiende, el vacío y por qué.</summary>
    public static (CodeCatalog Catalog, string? Error) Load(string path)
    {
        try
        {
            return (Parse(File.ReadAllText(path)), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return (Empty, ex.Message);
        }
    }

    /// <summary>El catálogo de un texto JSON.</summary>
    /// <remarks>Un lenguaje sin tema de serie, o con uno que no existe, sigue a la app.</remarks>
    public static CodeCatalog Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        static string? Text(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        static CodeSample? SampleOf(JsonElement t) =>
            t.TryGetProperty("sample", out var s) && s.ValueKind == JsonValueKind.Object && Text(s, "bg") is { } bg
                ? new CodeSample(bg, Text(s, "edge") ?? bg,
                    s.TryGetProperty("colors", out var c) && c.ValueKind == JsonValueKind.Array ? c.EnumerateArray().Select(x => x.GetString() ?? "").Where(HexColor.IsValid).ToList() : [])
                : null;
        var themes = root.GetProperty("themes").EnumerateArray()
            .Select(t => new CodeTheme(Text(t, "id")!, Text(t, "name") ?? Text(t, "id")!, Text(t, "tone"), Text(t, "hint"), SampleOf(t)))
            .Where(t => !string.IsNullOrEmpty(t.Id))
            .ToList();
        var known = themes.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        var languages = root.GetProperty("languages").EnumerateArray()
            .Select(l => new CodeLanguage(
                Text(l, "id")!,
                Text(l, "name") ?? Text(l, "id")!,
                l.TryGetProperty("extensions", out var ext) && ext.ValueKind == JsonValueKind.Array
                    ? ext.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList()
                    : [],
                Text(l, "defaultTheme") is { } theme && known.Contains(theme) ? theme : AppTheme))
            .Where(l => !string.IsNullOrEmpty(l.Id))
            .ToList();
        return new CodeCatalog(themes, languages, root.Clone());
    }

    /// <summary>El tema de un lenguaje: el elegido si existe; si no, el de serie del lenguaje; si no, el de la app.</summary>
    public string ThemeOf(string languageId, IReadOnlyDictionary<string, string> chosen)
    {
        if (chosen.TryGetValue(languageId, out var mine) && Themes.Any(t => t.Id == mine)) return mine;
        return Languages.FirstOrDefault(l => l.Id == languageId)?.DefaultTheme ?? AppTheme;
    }
}
