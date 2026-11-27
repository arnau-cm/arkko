using System.Text.RegularExpressions;

namespace Arkko.Core;

/// <summary>Una variable de entorno que parece un token: su nombre, si es del usuario o del equipo, su valor tapado y de qué tipo parece.</summary>
public sealed record EnvCandidate(string Name, bool Machine, string Masked, string? Kind);

/// <summary>Busca, entre las variables de entorno, las que parecen tokens, para añadirlas a la sección Tokens.</summary>
/// <remarks>
/// Por el nombre (TOKEN, SECRET, API_KEY, KEY, PAT, API) o porque el detector de Seguridad reconoce su valor. Deja
/// fuera las contraseñas, que no se rotan como un token, y las que ya son la variable de algún token. El valor solo
/// se usa para decidir y para enseñarlo tapado; no sale de aquí.
/// </remarks>
public static partial class TokenDiscovery
{
    // Tipos del detector que no son un token: una URL con usuario y contraseña, o una asignación de texto.
    private static readonly HashSet<string> NotTokens = ["URL con credenciales", "Variable con secreto"];

    /// <summary>Las variables que parecen tokens, ordenadas por nombre; si el mismo nombre está en el usuario y en el equipo, gana la del usuario.</summary>
    public static List<EnvCandidate> Find(IEnumerable<(string Name, string Value, bool Machine)> variables, IEnumerable<SecretCard> cards)
    {
        var taken = new HashSet<string>(cards.Select(c => c.EnvVar.Trim()).Where(n => n.Length > 0), StringComparer.OrdinalIgnoreCase);
        return variables
            .Where(v => v.Value.Trim().Length > 0 && !taken.Contains(v.Name) && !Password().IsMatch(v.Name))
            .OrderBy(v => v.Machine)
            .DistinctBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .Select(v => (v.Name, v.Machine, v.Value, Kind: KindOf(v.Value.Trim())))
            .Where(v => v.Kind is not null || TokenName().IsMatch(v.Name))
            .Select(v => new EnvCandidate(v.Name, v.Machine, SecretDetector.Mask(v.Value.Trim()), v.Kind))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>De qué tipo de token parece un valor, según el detector de Seguridad; null si no parece ninguno.</summary>
    private static string? KindOf(string value) => SecretDetector.Scan(value).Select(m => m.Kind).FirstOrDefault(k => !NotTokens.Contains(k));

    [GeneratedRegex(@"TOKEN|SECRET|API_?KEY|ACCESS_?KEY|(^|_)(KEY|PAT|API)(_|$)", RegexOptions.IgnoreCase)]
    private static partial Regex TokenName();

    [GeneratedRegex(@"PASSWORD|PASSWD|(^|_)PWD(_|$)", RegexOptions.IgnoreCase)]
    private static partial Regex Password();
}
