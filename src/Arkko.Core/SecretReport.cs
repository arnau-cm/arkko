namespace Arkko.Core;

/// <summary>Un secreto y todas las veces que ha aparecido, en cualquier sesión.</summary>
public sealed record SecretGroup(string Fingerprint, string Kind, string Masked, IReadOnlyList<SecretFinding> Appearances)
{
    public DateTime FirstUtc => Appearances.Min(a => a.WhenUtc);
    public DateTime LastUtc => Appearances.Max(a => a.WhenUtc);

    /// <summary>Sesiones donde se envió; una clave escrita en un fichero no es de ninguna.</summary>
    public int Sessions => Appearances.Where(a => !a.Written).Select(a => a.SessionId).Distinct().Count();

    /// <summary>Si alguna vez pasó por una conversación con Claude, y por tanto llegó a Anthropic.</summary>
    public bool Sent => Appearances.Any(a => !a.Written);

    /// <summary>Si ahora mismo está escrita tal cual en algún fichero de configuración.</summary>
    public bool InFile => Appearances.Any(a => a.Written);

    /// <summary>Proyectos donde apareció, del que más veces al que menos.</summary>
    public IReadOnlyList<string> Projects => MostFirst(Appearances.Select(SecretReport.ProjectOf));

    /// <summary>Worktrees donde apareció, del que más veces al que menos; vacío si nunca fue en uno.</summary>
    public IReadOnlyList<string> Worktrees => MostFirst(Appearances.Select(SecretReport.WorktreeOf).OfType<string>());

    /// <summary>Los nombres sin repetir, del más frecuente al menos y, a igualdad, por orden alfabético.</summary>
    private static List<string> MostFirst(IEnumerable<string> names) => names
        .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
        .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
        .Select(g => g.Key).ToList();
}

/// <summary>Resumen de los hallazgos para la sección de seguridad.</summary>
public static class SecretReport
{
    /// <summary>Proyecto de un hallazgo como se muestra: el repositorio en un worktree, la carpeta si no, o «(sin proyecto)».</summary>
    public static string ProjectOf(SecretFinding f) =>
        f.Repo ?? (string.IsNullOrWhiteSpace(f.Project) ? "(sin proyecto)" : f.Project!);

    /// <summary>Worktree de un hallazgo: su carpeta si la sesión estaba en uno; null si no.</summary>
    public static string? WorktreeOf(SecretFinding f) => f.Repo is null ? null : f.Project;

    /// <summary>Proyecto y worktree en una línea de texto: «Tienda · DEVLAB-86», o solo el proyecto.</summary>
    public static string PlaceOf(SecretFinding f) => WorktreeOf(f) is { } w ? $"{ProjectOf(f)} · {w}" : ProjectOf(f);

    /// <summary>Otros secretos que aparecieron en algún fichero donde también salió este.</summary>
    /// <remarks>Solo cuentan los ficheros: un mensaje o la salida de un comando no tienen ruta que compartir.</remarks>
    public static HashSet<string> SharingFiles(IReadOnlyCollection<SecretFinding> findings, string fingerprint)
    {
        var files = findings
            .Where(f => f.Fingerprint == fingerprint && !string.IsNullOrWhiteSpace(f.File))
            .Select(f => f.File!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return findings
            .Where(f => f.Fingerprint != fingerprint && f.File is { } file && files.Contains(file))
            .Select(f => f.Fingerprint)
            .ToHashSet();
    }

    /// <summary>Agrupa los hallazgos por secreto, con el visto más recientemente primero.</summary>
    public static List<SecretGroup> Group(IEnumerable<SecretFinding> findings) => findings
        .GroupBy(f => f.Fingerprint)
        .Select(g =>
        {
            var appearances = g.OrderByDescending(f => f.WhenUtc).ToList();
            return new SecretGroup(g.Key, appearances[0].Kind, appearances[0].Masked, appearances);
        })
        .OrderByDescending(g => g.LastUtc)
        .ToList();

    /// <summary>Cuántos secretos distintos hay de cada tipo, del más frecuente al menos.</summary>
    public static List<(string Kind, int Count)> ByKind(IEnumerable<SecretGroup> groups) => groups
        .GroupBy(g => g.Kind)
        .Select(g => (g.Key, g.Count()))
        .OrderByDescending(x => x.Item2).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>Cuántos secretos distintos han aparecido en cada proyecto, del que más al que menos.</summary>
    public static List<(string Project, int Count)> ByProject(IEnumerable<SecretGroup> groups) => groups
        .SelectMany(g => g.Projects.Select(p => (Project: p, g.Fingerprint)))
        .GroupBy(x => x.Project)
        .Select(g => (g.Key, g.Select(x => x.Fingerprint).Distinct().Count()))
        .OrderByDescending(x => x.Item2).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
        .ToList();
}
