using System.Text.RegularExpressions;

namespace Arkko.Core;

/// <summary>Un secreto encontrado en un texto. Solo vive en memoria: lo que se guarda es su máscara y su huella.</summary>
public sealed record SecretMatch(string Kind, string Value);

/// <summary>Busca secretos en un texto: claves con formato conocido, tokens, cadenas de conexión y variables con valor.</summary>
/// <remarks>
/// Las reglas van de la más concreta a la más general: un valor que ya ha encontrado una regla
/// concreta no lo vuelve a contar una general. Las reglas generales (variables, cadenas de conexión)
/// descartan además los valores de ejemplo y los que no parecen aleatorios, que son la mayoría de
/// falsos positivos.
/// </remarks>
public static partial class SecretDetector
{
    private sealed record Rule(string Kind, Regex Pattern, bool Generic);

    // Las expresiones se generan al compilar: ak arranca en cada herramienta, y compilarlas al vuelo
    // le añadía unos 60 ms a cada llamada. Cada una sigue con su límite de 250 ms.
    private const RegexOptions KnownOptions = RegexOptions.CultureInvariant;
    private const RegexOptions GenericOptions = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Multiline;
    private const int TimeoutMs = 250;

    private static readonly Rule[] Rules =
    [
        new("Clave privada", PrivateKey(), false),
        new("Clave de Anthropic", Anthropic(), false),
        new("Clave de OpenAI", OpenAi(), false),
        new("Token de GitHub", GitHub(), false),
        new("Token de GitLab", GitLab(), false),
        new("Token de Slack", Slack(), false),
        new("Clave de Google", Google(), false),
        new("Clave de AWS", Aws(), false),
        new("Clave de Stripe", Stripe(), false),
        new("Token de npm", Npm(), false),
        new("Token JWT", Jwt(), false),
        new("Token Bearer", Bearer(), true),
        new("Clave de Azure Storage", AzureStorage(), true),
        new("Firma SAS de Azure", AzureSas(), true),
        new("URL con credenciales", UrlCredentials(), true),
        new("Contraseña en cadena de conexión", ConnectionPassword(), true),
        new("Variable con secreto", EnvAssignment(), true),
        new("Variable con secreto", EnvColumns(), true),
        new("Secreto en configuración", ConfigSecret(), true),
    ];

    [GeneratedRegex(@"(?<v>-----BEGIN (?:[A-Z]+ )*PRIVATE KEY-----[\s\S]{20,}?-----END (?:[A-Z]+ )*PRIVATE KEY-----)", KnownOptions, TimeoutMs)]
    private static partial Regex PrivateKey();
    [GeneratedRegex(@"(?<![A-Za-z0-9_-])(?<v>sk-ant-[A-Za-z0-9_\-]{20,})", KnownOptions, TimeoutMs)]
    private static partial Regex Anthropic();
    [GeneratedRegex(@"(?<![A-Za-z0-9_-])(?<v>sk-(?:proj-)?[A-Za-z0-9_\-]{32,})", KnownOptions, TimeoutMs)]
    private static partial Regex OpenAi();
    [GeneratedRegex(@"(?<![A-Za-z0-9_])(?<v>(?:gh[pousr]_[A-Za-z0-9]{36,}|github_pat_[A-Za-z0-9_]{60,}))", KnownOptions, TimeoutMs)]
    private static partial Regex GitHub();
    [GeneratedRegex(@"(?<![A-Za-z0-9_-])(?<v>glpat-[A-Za-z0-9_\-]{20,})", KnownOptions, TimeoutMs)]
    private static partial Regex GitLab();
    [GeneratedRegex(@"(?<![A-Za-z0-9_-])(?<v>xox[baprs]-[A-Za-z0-9-]{10,})", KnownOptions, TimeoutMs)]
    private static partial Regex Slack();
    [GeneratedRegex(@"(?<![A-Za-z0-9_-])(?<v>AIza[0-9A-Za-z_\-]{35})", KnownOptions, TimeoutMs)]
    private static partial Regex Google();
    [GeneratedRegex(@"(?<![A-Z0-9])(?<v>(?:AKIA|ASIA)[0-9A-Z]{16})(?![A-Z0-9])", KnownOptions, TimeoutMs)]
    private static partial Regex Aws();
    [GeneratedRegex(@"(?<![A-Za-z0-9_])(?<v>[sr]k_live_[0-9A-Za-z]{20,})", KnownOptions, TimeoutMs)]
    private static partial Regex Stripe();
    [GeneratedRegex(@"(?<![A-Za-z0-9_])(?<v>npm_[A-Za-z0-9]{36})(?![A-Za-z0-9])", KnownOptions, TimeoutMs)]
    private static partial Regex Npm();
    [GeneratedRegex(@"(?<![A-Za-z0-9_-])(?<v>eyJ[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{2,}\.[A-Za-z0-9_\-]{10,})", KnownOptions, TimeoutMs)]
    private static partial Regex Jwt();
    [GeneratedRegex(@"authorization[""']?\s*[:=]\s*[""']?bearer\s+(?<v>[A-Za-z0-9\-._~+/]{20,}=*)", GenericOptions, TimeoutMs)]
    private static partial Regex Bearer();
    [GeneratedRegex(@"AccountKey\s*=\s*(?<v>[A-Za-z0-9+/]{40,}={0,2})", GenericOptions, TimeoutMs)]
    private static partial Regex AzureStorage();
    [GeneratedRegex(@"[?&]sig=(?<v>[A-Za-z0-9%+/=]{20,})", GenericOptions, TimeoutMs)]
    private static partial Regex AzureSas();
    [GeneratedRegex(@"\b[a-z][a-z0-9+.\-]*://(?<user>[^\s:/@""']+):(?<v>[^\s@/""']{3,})@[^\s/""']+", GenericOptions, TimeoutMs)]
    private static partial Regex UrlCredentials();
    [GeneratedRegex(@"(?:server|data source|host|uid|user id)\s*=[^;\r\n]*;[^\r\n]*?(?:password|pwd)\s*=\s*(?<v>[^;'""\s]{4,})", GenericOptions, TimeoutMs)]
    private static partial Regex ConnectionPassword();
    [GeneratedRegex(@"^\s*(?:export\s+|set\s+|\$env:)?(?<name>[A-Z][A-Z0-9_]*(?:API_?KEY|SECRET|TOKEN|PASSWORD|PASSWD|PRIVATE_KEY|ACCESS_KEY))\s*[=:]\s*[""']?(?<v>[^\s""'`;#]{8,})", GenericOptions, TimeoutMs)]
    private static partial Regex EnvAssignment();
    [GeneratedRegex(@"^(?<name>[A-Z][A-Z0-9_]*(?:API_?KEY|SECRET|TOKEN|PASSWORD|PASSWD|PRIVATE_KEY|ACCESS_KEY))[ \t]{2,}(?<v>\S{8,})\s*$", GenericOptions, TimeoutMs)]
    private static partial Regex EnvColumns();
    [GeneratedRegex(@"[""'](?<name>[a-z0-9_.\-]*(?:api[_\-]?key|secret|token|password|passwd))[""']\s*[:=]\s*[""'](?<v>[^""'\s]{8,})[""']", GenericOptions, TimeoutMs)]
    private static partial Regex ConfigSecret();

    // Trozos que delatan un valor de ejemplo, de plantilla o ya enmascarado, estén donde estén.
    private static readonly string[] PlaceholderParts =
    [
        "xxx", "example", "placeholder", "changeme", "change_me", "your_", "your-", "dummy", "sample", "redacted",
        "<", ">", "${", "$(", "{{", "***", "...", "…",
    ];

    // Valores que, enteros, no son un secreto. Como trozos sí pueden salir dentro de una clave aleatoria.
    private static readonly string[] PlaceholderValues =
    [
        "todo", "null", "undefined", "none", "true", "false", "password", "secret", "token", "fake", "test", "string",
        "pass", "passwd", "pwd", "user", "username", "usuario", "contraseña", "contrasena", "clave",
    ];

    /// <summary>Secretos del texto, sin repetir el mismo valor.</summary>
    public static List<SecretMatch> Scan(string? text)
    {
        var found = new List<SecretMatch>();
        if (string.IsNullOrEmpty(text) || text.Length < 8) return found;
        foreach (var rule in Rules)
        {
            MatchCollection matches;
            try
            {
                matches = rule.Pattern.Matches(text);
                foreach (Match m in matches)
                {
                    var value = m.Groups["v"].Value.TrimEnd('.', ',', ')', ']', '}');
                    if (value.Length == 0 || found.Any(f => f.Value.Contains(value) || value.Contains(f.Value))) continue;
                    if (IsCodeInUrl(m)) continue;
                    if (rule.Generic && !LooksReal(value, m.Groups["name"].Value)) continue;
                    if (!rule.Generic && IsPlaceholder(value)) continue;
                    found.Add(new SecretMatch(rule.Kind, value));
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // Un texto enorme y raro no debe parar el análisis: esa regla se salta en ese texto.
            }
        }
        return found;
    }

    /// <summary>Sustituye cada secreto del texto por su máscara, para poder enseñarlo sin el valor.</summary>
    public static string? Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        foreach (var m in Scan(text)) text = text.Replace(m.Value, Mask(m.Value), StringComparison.Ordinal);
        return text;
    }

    // Cuánto texto se pasa por el detector a cada lado del valor antes de recortar el trozo que se enseña: un secreto
    // vecino cortado por el borde de lo analizado no se reconoce, pero queda muy lejos del trozo.
    private const int AroundWindow = 4000;

    // Caracteres que se enseñan a cada lado del valor.
    private const int AroundRadius = 160;

    // Marca provisional del valor mientras se tapa lo demás: no aparece en un texto normal.
    private const char Placeholder = '\u0001';

    /// <summary>El trozo de texto alrededor de un valor, con él y cualquier otro secreto tapados por su máscara; null si no aparece.</summary>
    /// <remarks>
    /// El valor se sustituye antes de pasar el detector, así queda tapado aunque solo se reconociera por su contexto. Los
    /// saltos de línea y tabuladores se dejan como espacios, para leerlo en una línea.
    /// </remarks>
    public static string? Around(string? text, string value)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(value)) return null;
        var i = text.IndexOf(value, StringComparison.Ordinal);
        if (i < 0) return null;
        var from = Math.Max(0, i - AroundWindow);
        var to = Math.Min(text.Length, i + value.Length + AroundWindow);
        var chunk = Redact(text[from..to].Replace(value, Placeholder.ToString(), StringComparison.Ordinal))!;
        var at = chunk.IndexOf(Placeholder);
        var start = Math.Max(0, at - AroundRadius);
        var end = Math.Min(chunk.Length, at + 1 + AroundRadius);
        var shown = WhiteRun().Replace(chunk[start..end], " ").Replace(Placeholder.ToString(), $"[{Mask(value)}]", StringComparison.Ordinal);
        return (from > 0 || start > 0 ? "…" : "") + shown.Trim() + (to < text.Length || end < chunk.Length ? "…" : "");
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteRun();

    /// <summary>Si una URL con usuario y contraseña es en realidad código de prueba: un usuario «javascript», «data» o
    /// «vbscript», o una contraseña con paréntesis, como <c>https://javascript:alert(1)@localhost</c>.</summary>
    private static bool IsCodeInUrl(Match m) =>
        m.Groups["user"] is { Success: true } user
        && (user.Value.ToLowerInvariant() is "javascript" or "data" or "vbscript" || m.Groups["v"].Value.IndexOfAny(['(', ')']) >= 0);

    /// <summary>Valor enmascarado: los 4 primeros y los 4 últimos caracteres; nada si es corto.</summary>
    public static string Mask(string value)
    {
        var clean = value.Replace("\r", "").Replace("\n", " ");
        if (clean.StartsWith("-----BEGIN", StringComparison.Ordinal)) return clean[..Math.Min(clean.Length, clean.IndexOf("-----", 5, StringComparison.Ordinal) + 5)] + " …";
        return clean.Length < 12 ? "••••••" : $"{clean[..4]}…{clean[^4..]}";
    }

    /// <summary>Si un valor de una regla general parece un secreto de verdad y no un ejemplo o una referencia.</summary>
    private static bool LooksReal(string value, string name)
    {
        if (IsPlaceholder(value)) return false;
        if (value.StartsWith('$') || (name.Length > 0 && value.Equals(name, StringComparison.OrdinalIgnoreCase))) return false;
        if (value.Distinct().Count() < 5) return false;
        return Entropy(value) >= 2.5;
    }

    /// <summary>Si el valor es un ejemplo, una plantilla o un texto ya enmascarado.</summary>
    private static bool IsPlaceholder(string value)
    {
        var lower = value.ToLowerInvariant();
        if (lower.Length > 1 && lower[0] == '%' && lower[^1] == '%') return true;
        return PlaceholderParts.Any(lower.Contains) || PlaceholderValues.Contains(lower);
    }

    /// <summary>Entropía de Shannon por carácter: baja en palabras y repeticiones, alta en claves aleatorias.</summary>
    private static double Entropy(string value) =>
        -value.GroupBy(c => c).Select(g => (double)g.Count() / value.Length).Sum(p => p * Math.Log2(p));
}
