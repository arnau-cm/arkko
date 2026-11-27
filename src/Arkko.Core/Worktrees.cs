using System.Collections.Concurrent;

namespace Arkko.Core;

/// <summary>Cómo está la PR de una rama.</summary>
public enum PrState
{
    /// <summary>No tiene PR, o no se ha podido preguntar al remoto.</summary>
    None,

    /// <summary>Tiene PR y lo que apunta aún no está en la rama base.</summary>
    /// <remarks>Una PR cerrada sin mergear también sale así: el remoto publica sus commits, no si sigue abierta.</remarks>
    Open,

    /// <summary>Lo que apunta su PR ya está dentro de la rama base.</summary>
    Merged,
}

/// <summary>Un worktree de un repositorio y cómo está: cambios sin commit, commits sin subir y su PR.</summary>
/// <param name="Path">Su carpeta, con el formato del sistema.</param>
/// <param name="Folder">El nombre de su carpeta, que en un repositorio con un worktree por rama es el de la rama.</param>
/// <param name="Branch">Su rama; null si está en un commit suelto.</param>
/// <param name="Missing">Si su carpeta ya no existe: git lo sigue apuntando hasta un <c>git worktree prune</c>.</param>
/// <param name="IsBase">Si es una rama base (develop, main o master), que no se cierra nunca.</param>
/// <param name="Pending">Ficheros con cambios sin commit, nuevos incluidos; null si no se ha podido leer.</param>
/// <param name="Upstream">La rama remota que sigue; null si aún no se ha subido.</param>
/// <param name="Ahead">Commits que aún no se han subido a <paramref name="Upstream"/>; null sin ella.</param>
public sealed record WorktreeInfo(string Path, string Folder, string? Branch, bool Missing, bool IsBase, int? Pending,
    string? Upstream, int? Ahead, int? Pr, PrState PrState)
{
    /// <summary>Indica si sobra: la carpeta ya no existe, su PR está mergeada o su tarea está cerrada.</summary>
    public bool Leftover(bool taskClosed) => !IsBase && (Missing || PrState == PrState.Merged || taskClosed);
}

/// <summary>Los worktrees de un repositorio, con la rama base con que se mira si una PR está mergeada.</summary>
/// <param name="CommonDir">La carpeta de git que comparten sus worktrees (.git o .bare).</param>
/// <param name="BaseBranch">develop, main o master, la primera que tenga el remoto; null si no se ha podido preguntar.</param>
/// <param name="RemoteError">Por qué no se sabe la PR de sus ramas; null si el remoto contestó.</param>
/// <param name="Remote">La dirección de su remoto <c>origin</c>; null si no tiene.</param>
public sealed record RepoWorktrees(string Name, string CommonDir, string? BaseBranch, IReadOnlyList<WorktreeInfo> Worktrees, string? RemoteError,
    string? Remote = null)
{
    /// <summary>La página web de una PR del repositorio; null si su remoto no es de GitHub ni de Azure DevOps.</summary>
    public string? PullUrl(int number) => Arkko.Core.Worktrees.PullUrl(Remote, number);
}

/// <summary>Lee, solo para mirar, los worktrees de los repositorios en que han trabajado las sesiones y cómo está cada uno.</summary>
/// <remarks>
/// Lo único que escribe es la referencia remota de la rama base (<c>origin/develop</c>), con un <c>git fetch</c> de esa
/// rama sola cuando su último commit aún no está en el equipo: sin él, una PR mergeada saldría abierta hasta el
/// siguiente <c>git pull</c>. No toca carpetas ni ramas locales.
/// </remarks>
public static class Worktrees
{
    // Ramas base, por orden: la de integración y, si el remoto no la tiene, la principal.
    private static readonly string[] BaseBranches = ["develop", "main", "master"];

    private static readonly TimeSpan RemoteFresh = TimeSpan.FromMinutes(30);

    // Lo que dijo el remoto de cada repositorio, y cuándo: las PR casi nunca cambian, y preguntar tarda.
    private static readonly ConcurrentDictionary<string, (DateTime AtUtc, RemoteRefs? Refs, string? Error)> Remotes =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Las ramas del remoto y sus PR, por el commit al que apunta cada una.</summary>
    internal sealed record RemoteRefs(Dictionary<string, int> PullBySha, Dictionary<string, string> Heads);

    /// <summary>Un worktree tal como lo lista <c>git worktree list --porcelain</c>.</summary>
    internal sealed record Entry(string Path, string? Head, string? Branch, bool Bare, bool Prunable);

    /// <summary>Los repositorios con worktrees enlazados de unas carpetas de sesión, con el estado de cada worktree.</summary>
    /// <param name="remote">Si se pregunta al remoto por las PR; sin ello, se usa lo que contestó la última vez.</param>
    /// <param name="fresh">Si se pregunta aunque la última respuesta tenga menos de media hora.</param>
    /// <remarks>Un clon con un solo worktree no sale: no tiene nada que cerrar.</remarks>
    public static List<RepoWorktrees> Read(IEnumerable<string?> folders, bool remote = true, bool fresh = false)
    {
        var commons = folders.Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f!.TrimEnd('/', '\\'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(CommonDirOf)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase);
        // Los repositorios a la vez: preguntar al remoto tarda unos segundos en cada uno.
        return commons.AsParallel().WithDegreeOfParallelism(4)
            .Select(c => ReadRepo(c, remote, fresh)).OfType<RepoWorktrees>()
            .ToList()
            .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Indica si a algún repositorio le falta una respuesta del remoto de menos de media hora.</summary>
    public static bool NeedsRemote(IEnumerable<RepoWorktrees> repos) =>
        repos.Any(r => !Remotes.TryGetValue(r.CommonDir, out var known) || DateTime.UtcNow - known.AtUtc >= RemoteFresh);

    /// <summary>La carpeta de git que comparten los worktrees del repositorio de una carpeta; null si no está en uno.</summary>
    /// <remarks>
    /// Se lee de disco, sin lanzar git: las sesiones son cientos de carpetas. Una carpeta que ya no existe vale si su
    /// carpeta de encima es la raíz de un repositorio con un worktree por rama (lleva .bare).
    /// </remarks>
    internal static string? CommonDirOf(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                var parent = Path.GetDirectoryName(folder);
                var bare = parent is null ? null : Path.Combine(parent, ".bare");
                return bare is not null && Directory.Exists(bare) ? Path.GetFullPath(bare) : null;
            }
            for (var dir = new DirectoryInfo(folder); dir is not null; dir = dir.Parent)
            {
                var git = Path.Combine(dir.FullName, ".git");
                if (Directory.Exists(git)) return Path.GetFullPath(git);
                if (!File.Exists(git)) continue;
                var line = File.ReadLines(git).FirstOrDefault()?.Trim();
                if (line is null || !line.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase)) return null;
                var gitDir = Path.GetFullPath(Path.Combine(dir.FullName, line["gitdir:".Length..].Trim()));
                // Un worktree enlazado apunta a .git/worktrees/<nombre>, y su fichero commondir, a la carpeta compartida.
                var common = Path.Combine(gitDir, "commondir");
                return File.Exists(common) ? Path.GetFullPath(Path.Combine(gitDir, File.ReadAllText(common).Trim())) : gitDir;
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Los worktrees de un repositorio y cómo está cada uno; null si no se pueden listar o solo tiene uno.</summary>
    private static RepoWorktrees? ReadRepo(string common, bool remote, bool fresh)
    {
        if (!Directory.Exists(common)) return null;
        var list = GitChanges.Run(common, "worktree", "list", "--porcelain");
        if (!list.Ok) return null;
        var entries = ParseList(list.Out);
        var trees = entries.Where(e => !e.Bare).ToList();
        if (trees.Count == 0 || (!entries.Any(e => e.Bare) && trees.Count < 2)) return null;

        var (refs, error) = RemoteOf(common, remote, fresh);
        var baseBranch = refs is null ? null : BaseBranches.FirstOrDefault(refs.Heads.ContainsKey);
        var baseSha = baseBranch is null ? null : refs!.Heads[baseBranch];

        var items = trees.AsParallel().AsOrdered().WithDegreeOfParallelism(4)
            .Select(e => Inspect(common, e, refs, baseSha))
            .ToList()
            .OrderByDescending(w => w.IsBase)
            .ThenBy(w => w.Folder, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var origin = GitChanges.Run(common, "config", "--get", "remote.origin.url");
        return new RepoWorktrees(NameOf(common), common, baseBranch, items, error, origin.Ok && origin.Out.Trim() is { Length: > 0 } url ? url : null);
    }

    /// <summary>Cómo está un worktree: lo que tiene sin commit y sin subir, y su PR si el remoto la conoce.</summary>
    private static WorktreeInfo Inspect(string common, Entry entry, RemoteRefs? refs, string? baseSha)
    {
        var folder = Path.GetFileName(entry.Path);
        var isBase = entry.Branch is { } b && BaseBranches.Contains(b, StringComparer.OrdinalIgnoreCase);
        if (entry.Prunable || !Directory.Exists(entry.Path))
            return new(entry.Path, folder, entry.Branch, true, isBase, null, null, null, null, PrState.None);

        var status = GitChanges.Run(entry.Path, "status", "--porcelain", "--untracked-files=normal");
        int? pending = status.Ok ? status.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length : null;
        var (upstream, ahead) = entry.Branch is null ? (null, null) : GitChanges.Unpushed(entry.Path);
        if (isBase || entry.Branch is null || refs is null)
            return new(entry.Path, folder, entry.Branch, false, isBase, pending, upstream, ahead, null, PrState.None);

        // La PR apunta al último commit subido: el que tiene el remoto o, si ya borró la rama, el que se subió por última vez.
        var upstreamSha = upstream is null ? null : GitChanges.Run(entry.Path, "rev-parse", "@{upstream}") is { Ok: true } u ? u.Out.Trim() : null;
        var (pr, prSha) = new[] { refs.Heads.GetValueOrDefault(entry.Branch), upstreamSha, entry.Head }
            .OfType<string>()
            .Select(sha => (Number: refs.PullBySha.TryGetValue(sha, out var n) ? n : (int?)null, Sha: sha))
            .FirstOrDefault(x => x.Number is not null);
        if (pr is null)
            return new(entry.Path, folder, entry.Branch, false, false, pending, upstream, ahead, null, PrState.None);

        var merged = baseSha is not null && GitChanges.Run(common, "merge-base", "--is-ancestor", prSha!, baseSha).Ok;
        return new(entry.Path, folder, entry.Branch, false, false, pending, upstream, ahead, pr, merged ? PrState.Merged : PrState.Open);
    }

    /// <summary>Las ramas y PR del remoto de un repositorio, o por qué no se saben; la respuesta guardada si es reciente o no se pregunta.</summary>
    /// <remarks>
    /// Una sola consulta por repositorio, no una por worktree. Si el último commit de la rama base no está en el equipo,
    /// se trae esa rama sola, para poder ver qué PR ya están dentro.
    /// </remarks>
    private static (RemoteRefs? Refs, string? Error) RemoteOf(string common, bool ask, bool fresh)
    {
        var known = Remotes.TryGetValue(common, out var k) ? k : default;
        if (!ask || (!fresh && known.AtUtc != default && DateTime.UtcNow - known.AtUtc < RemoteFresh)) return (known.Refs, known.Error);

        // Preguntar al remoto puede fallar una vez y salir bien a la siguiente: se reintenta una vez.
        var listed = GitChanges.RunRemote(common, "ls-remote", "origin", "refs/pull/*/head", "refs/heads/*");
        if (!listed.Ok) listed = GitChanges.RunRemote(common, "ls-remote", "origin", "refs/pull/*/head", "refs/heads/*");
        if (!listed.Ok)
        {
            var error = listed.Error ?? "El remoto no ha contestado: puede que haga falta volver a iniciar sesión en git.";
            // Lo que se supo la última vez sigue valiendo mejor que nada; el fallo se dice igual.
            Remotes[common] = (DateTime.UtcNow, known.Refs, error);
            return (known.Refs, error);
        }

        var refs = ParseRemote(listed.Out);
        if (BaseBranches.FirstOrDefault(refs.Heads.ContainsKey) is { } baseBranch
            && !GitChanges.Run(common, "cat-file", "-e", refs.Heads[baseBranch] + "^{commit}").Ok)
            GitChanges.RunRemote(common, "fetch", "--no-tags", "--no-write-fetch-head", "origin",
                $"+refs/heads/{baseBranch}:refs/remotes/origin/{baseBranch}");
        Remotes[common] = (DateTime.UtcNow, refs, null);
        return (refs, null);
    }

    /// <summary>Los worktrees de la salida de <c>git worktree list --porcelain</c>.</summary>
    internal static List<Entry> ParseList(string porcelain)
    {
        var result = new List<Entry>();
        foreach (var block in porcelain.Replace("\r", "").Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            string? path = null, head = null, branch = null;
            bool bare = false, prunable = false;
            foreach (var line in block.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("worktree ", StringComparison.Ordinal)) path = line[9..];
                else if (line.StartsWith("HEAD ", StringComparison.Ordinal)) head = line[5..];
                else if (line.StartsWith("branch refs/heads/", StringComparison.Ordinal)) branch = line[18..];
                else if (line == "bare") bare = true;
                else if (line.StartsWith("prunable", StringComparison.Ordinal)) prunable = true;
            }
            if (path is not null) result.Add(new Entry(Path.GetFullPath(path), head, branch, bare, prunable));
        }
        return result;
    }

    /// <summary>La página web de una PR por la dirección del remoto; null si no se sabe cómo es.</summary>
    /// <remarks>
    /// Vale la dirección https y la de ssh, con usuario o sin él: <c>git@github.com:dueño/repo.git</c> lleva a
    /// <c>https://github.com/dueño/repo/pull/N</c>, y <c>git@ssh.dev.azure.com:v3/org/proyecto/repo</c>, a
    /// <c>https://dev.azure.com/org/proyecto/_git/repo/pullrequest/N</c>. Un servidor propio por http o https
    /// (<c>http://git-interno:3000/dueño/repo.git</c>) se toma por Gitea, que es quien publica las PR como GitHub: su página es
    /// <c>/dueño/repo/pulls/N</c>, en el mismo servidor y puerto. Por ssh, de un servidor propio no se sabe la web.
    /// </remarks>
    public static string? PullUrl(string? remote, int number)
    {
        if (string.IsNullOrWhiteSpace(remote)) return null;
        var text = remote.Trim();
        string host, path;
        Uri? web = null;
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" or "ssh")
        {
            (host, path) = (uri.Host, uri.AbsolutePath);
            if (uri.Scheme != "ssh") web = uri;
        }
        else if (text.IndexOf(':') is var colon and > 0 && !text.Contains("://", StringComparison.Ordinal))
            (host, path) = (text[..colon][(text[..colon].LastIndexOf('@') + 1)..], text[(colon + 1)..]);
        else return null;
        path = path.Trim('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) path = path[..^4];
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray();
        string Part(string p) => Uri.EscapeDataString(p);

        if (host.Equals("github.com", StringComparison.OrdinalIgnoreCase) && parts.Length == 2)
            return $"https://github.com/{Part(parts[0])}/{Part(parts[1])}/pull/{number}";
        // Azure DevOps por https: dev.azure.com/org/proyecto/_git/repo; por ssh: ssh.dev.azure.com:v3/org/proyecto/repo.
        if (host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase) && parts.Length == 4 && parts[2] == "_git")
            return $"https://dev.azure.com/{Part(parts[0])}/{Part(parts[1])}/_git/{Part(parts[3])}/pullrequest/{number}";
        if (host.Equals("ssh.dev.azure.com", StringComparison.OrdinalIgnoreCase) && parts.Length == 4 && parts[0] == "v3")
            return $"https://dev.azure.com/{Part(parts[1])}/{Part(parts[2])}/_git/{Part(parts[3])}/pullrequest/{number}";
        if (web is not null && parts.Length == 2 && !host.Contains("github", StringComparison.OrdinalIgnoreCase)
            && !host.Contains("azure", StringComparison.OrdinalIgnoreCase) && !host.Contains("visualstudio", StringComparison.OrdinalIgnoreCase))
            return $"{web.Scheme}://{web.Authority}/{Part(parts[0])}/{Part(parts[1])}/pulls/{number}";
        return null;
    }

    /// <summary>Las ramas y las PR de la salida de <c>git ls-remote</c>; de cada commit, la PR de número más alto.</summary>
    internal static RemoteRefs ParseRemote(string output)
    {
        var pulls = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var heads = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split('\t');
            if (parts.Length != 2) continue;
            var (sha, name) = (parts[0], parts[1]);
            if (name.StartsWith("refs/heads/", StringComparison.Ordinal)) heads[name[11..]] = sha;
            else if (name.StartsWith("refs/pull/", StringComparison.Ordinal) && name.EndsWith("/head", StringComparison.Ordinal)
                     && int.TryParse(name[10..^5], out var number) && (!pulls.TryGetValue(sha, out var known) || number > known))
                pulls[sha] = number;
        }
        return new RemoteRefs(pulls, heads);
    }

    /// <summary>El nombre del repositorio por su carpeta de git: la de encima de .git o .bare, o la suya sin «.git».</summary>
    private static string NameOf(string common)
    {
        var dir = new DirectoryInfo(common);
        var name = dir.Name is ".git" or ".bare" ? dir.Parent?.Name ?? dir.Name : dir.Name;
        return name.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }
}
