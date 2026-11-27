namespace Arkko.Core;

/// <summary>Repositorio al que pertenece una carpeta que es un worktree de git.</summary>
/// <remarks>
/// En un worktree, la carpeta suele llamarse como la rama (develop, DEVLAB-123) y no dice de qué
/// repositorio es. Un worktree enlazado tiene un fichero .git, no una carpeta, que apunta a
/// &lt;repo&gt;/.git/worktrees/&lt;nombre&gt; o, con el repositorio en .bare, a &lt;repo&gt;/.bare/worktrees/&lt;nombre&gt;.
/// Un submódulo también tiene un fichero .git, pero apunta a .git/modules y no se toma por worktree.
/// </remarks>
public static class Worktree
{
    /// <summary>Nombre del repositorio de la carpeta si es un worktree enlazado; null si no lo es o no se puede leer.</summary>
    public static string? RepoOf(string? cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd)) return null;
        try
        {
            var dir = cwd.TrimEnd('/', '\\');
            var gitFile = Path.Combine(dir, ".git");
            if (!File.Exists(gitFile)) return null;

            var line = File.ReadLines(gitFile).FirstOrDefault()?.Trim();
            if (line is null || !line.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase)) return null;

            var gitDir = new DirectoryInfo(Path.GetFullPath(Path.Combine(dir, line["gitdir:".Length..].Trim())));
            if (gitDir.Parent is not { Name: "worktrees" } worktrees || worktrees.Parent is not { } common) return null;

            // .git y .bare viven dentro del repositorio; un repositorio bare suelto se llama repo.git.
            var repo = common.Name is ".git" or ".bare" ? common.Parent?.Name : common.Name;
            if (repo is not null && repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) repo = repo[..^4];
            if (string.IsNullOrEmpty(repo) || repo.Equals(Path.GetFileName(dir), StringComparison.OrdinalIgnoreCase)) return null;
            return repo;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
