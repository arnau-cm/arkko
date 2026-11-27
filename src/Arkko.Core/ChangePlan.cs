using System.Text.RegularExpressions;

namespace Arkko.Core;

/// <summary>Una tarea del tasks.md de un change: su sección, su texto, si está hecha y cuándo se marcó en la sesión.</summary>
/// <param name="Id">El número de la tarea («1.3»), «F12» para una fila de triaje o «T4» si no lleva número.</param>
/// <param name="Title">La primera frase, sin marcas, para enseñarla en corto.</param>
/// <param name="Text">El texto entero, sin marcas, cortado a un largo razonable.</param>
/// <param name="Human">Si es de la persona: lleva «[humana]» o «[manual]».</param>
/// <param name="Files">Los nombres de fichero que cita, sin carpeta.</param>
/// <param name="MarkedUtc">Cuándo la marcó la sesión como hecha; null si no consta o si sigue sin hacer.</param>
/// <param name="AddedUtc">Cuándo la apuntó la sesión en el tasks.md; null si ya estaba antes de la sesión.</param>
/// <param name="Line">La línea del tasks.md en que está, desde 1; 0 si no se sabe.</param>
public sealed record PlanTask(string Id, string Title, string Text, string Section, bool Done, bool Human, IReadOnlyList<string> Files, DateTime? MarkedUtc, DateTime? AddedUtc = null, int Line = 0);

/// <summary>Un commit de la sesión: cuándo, quién lo hizo y su hash corto si lo trae el resultado.</summary>
public sealed record PlanCommit(DateTime AtUtc, string? AgentId, string? Hash);

/// <summary>El plano de la obra de una sesión: el change que sigue, dónde está su tasks.md, sus tareas y los commits.</summary>
public sealed record ChangePlanInfo(string Change, string TasksPath, bool Archived, IReadOnlyList<PlanTask> Tasks, IReadOnlyList<PlanCommit> Commits);

/// <summary>Lee el plano de la obra de una sesión del ciclo SDD: el tasks.md de su change en openspec y cuándo se marcó
/// cada tarea.</summary>
/// <remarks>
/// El change es el de la carpeta openspec/changes del worktree de la sesión. Si hay varios, manda el que lleva en el nombre
/// la clave de la rama («DEVLAB-183» busca «devlab-183»); si ya se archivó, se busca en archive. Cuándo se marcó una tarea
/// sale de los pasos que escriben en el tasks.md con «[x]»: una edición, el fichero entero o una orden que las marca en
/// bucle. Lo que no se ve en el transcript queda sin hora y se da por hecho desde el principio.
/// </remarks>
public static partial class ChangePlan
{
    // Cuántas carpetas se sube desde la de la sesión buscando la de openspec.
    private const int MaxUp = 4;

    // Largo máximo del texto de una tarea y de su título.
    private const int MaxText = 600;
    private const int MaxTitle = 110;

    /// <summary>Lee el plano de la sesión; null si no sigue un change de openspec.</summary>
    public static ChangePlanInfo? Read(string? cwd, string? branch, SessionTrace? trace)
    {
        var path = FindTasksFile(cwd, branch);
        if (path is null) return null;
        string text;
        try { text = File.ReadAllText(path); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        var tasks = Parse(text);
        var marks = trace is null ? new Dictionary<string, DateTime>() : Marks(trace, tasks.Select(t => t.Id).ToList());
        var added = trace is null ? new Dictionary<string, DateTime>() : Added(trace, tasks.Select(t => t.Id).ToList());
        // Una tarea hecha sin marca a la vista se da por hecha la última vez que la sesión tocó el tasks.md, no desde el principio.
        var lastTouch = trace?.Steps.Where(s => s.Kind == TraceKind.Tool && !s.IsError && s.Text is { } x && x.Contains("tasks.md", StringComparison.OrdinalIgnoreCase))
            .Select(s => s.EndUtc ?? s.StartUtc).LastOrDefault(d => d is not null);
        tasks = tasks.Select(t =>
        {
            var at = added.TryGetValue(t.Id, out var a) ? a : (DateTime?)null;
            DateTime? mark = !t.Done ? null : marks.TryGetValue(t.Id, out var m) ? m : lastTouch is { } l && (at is null || l >= at) ? l : null;
            return t with { MarkedUtc = mark, AddedUtc = at };
        }).ToList();
        var dir = Path.GetDirectoryName(path) ?? "";
        var archived = Path.GetFileName(Path.GetDirectoryName(dir) ?? "").Equals("archive", StringComparison.OrdinalIgnoreCase);
        return new ChangePlanInfo(Path.GetFileName(dir), path, archived, tasks, trace is null ? [] : Commits(trace));
    }

    /// <summary>El tasks.md del change que sigue la sesión, o null si no hay ninguno que encaje.</summary>
    public static string? FindTasksFile(string? cwd, string? branch)
    {
        if (string.IsNullOrWhiteSpace(cwd)) return null;
        string? changes = null;
        try
        {
            // La carpeta de la sesión puede no existir ya (un worktree cerrado): se sube igual, y en un repositorio con un
            // worktree por rama (carpeta .bare) se busca en develop y en master, donde queda el change archivado.
            var dir = new DirectoryInfo(cwd);
            for (var i = 0; dir is not null && i <= MaxUp && changes is null; i++, dir = dir.Parent)
            {
                var places = Directory.Exists(Path.Combine(dir.FullName, ".bare"))
                    ? new[] { dir.FullName, Path.Combine(dir.FullName, "develop"), Path.Combine(dir.FullName, "master") }
                    : [dir.FullName];
                changes = places.Select(p => Path.Combine(p, "openspec", "changes")).FirstOrDefault(Directory.Exists);
            }
            if (changes is null) return null;
            var key = KeyOf(branch) ?? KeyOf(Path.GetFileName(cwd.TrimEnd('/', '\\')));
            var active = Directory.GetDirectories(changes)
                .Where(d => !Path.GetFileName(d).Equals("archive", StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(d, "tasks.md")))
                .ToList();
            if (key is not null)
            {
                var mine = active.FirstOrDefault(d => Path.GetFileName(d).Contains(key, StringComparison.OrdinalIgnoreCase));
                if (mine is not null) return Path.Combine(mine, "tasks.md");
                var archive = Path.Combine(changes, "archive");
                if (Directory.Exists(archive))
                {
                    var old = Directory.GetDirectories(archive)
                        .Where(d => Path.GetFileName(d).Contains(key, StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(d, "tasks.md")))
                        .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal)
                        .FirstOrDefault();
                    if (old is not null) return Path.Combine(old, "tasks.md");
                }
                return null;
            }
            return active.Count == 1 ? Path.Combine(active[0], "tasks.md") : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>La ruta de un fichero dentro del repositorio de la sesión, con barras «/», para abrirlo en su ficha; null si
    /// la carpeta de la sesión no es un repositorio o el fichero queda fuera de él (un change archivado en develop).</summary>
    public static string? RepoPathOf(string? cwd, string? path)
    {
        if (path is null || GitChanges.RootOf(cwd, out _) is not { } root) return null;
        var relative = Path.GetRelativePath(root, path);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? null : relative.Replace('\\', '/');
    }

    /// <summary>La clave de tarea de una rama o carpeta («DEVLAB-183» da «devlab-183»); null si no la lleva.</summary>
    internal static string? KeyOf(string? name) =>
        name is not null && KeyPattern().Match(name) is { Success: true } m ? m.Value.ToLowerInvariant() : null;

    /// <summary>Las tareas de un tasks.md, en orden, con su sección.</summary>
    public static List<PlanTask> Parse(string text)
    {
        var tasks = new List<PlanTask>();
        var section = "";
        var loose = 0;
        var number = 0;
        foreach (var raw in text.Split('\n'))
        {
            number++;
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("## ", StringComparison.Ordinal)) { section = line[3..].Trim(); continue; }
            var m = TaskLine().Match(line);
            if (!m.Success) continue;
            var done = m.Groups[1].Value is "x" or "X";
            var body = m.Groups[2].Value.Trim();
            string id;
            if (NumberedId().Match(body) is { Success: true } num) { id = num.Groups[1].Value; body = body[num.Length..]; }
            else if (TriageId().Match(body) is { Success: true } tri) { id = "F" + tri.Groups[1].Value; body = body[tri.Length..]; }
            else id = "T" + ++loose;
            var human = body.Contains("[humana]", StringComparison.OrdinalIgnoreCase) || body.Contains("[manual]", StringComparison.OrdinalIgnoreCase);
            var clean = Tags().Replace(body, "").Replace("`", "").Replace("**", "").Trim();
            tasks.Add(new PlanTask(id, TitleOf(clean), Clip(clean, MaxText), section, done, human, FilesOf(body), null, Line: number));
        }
        return tasks;
    }

    /// <summary>Cuándo se marcó cada tarea en la sesión, por su id: el primer paso que la escribe con «[x]» en el tasks.md.</summary>
    public static Dictionary<string, DateTime> Marks(SessionTrace trace, IReadOnlyCollection<string> ids)
    {
        var known = new HashSet<string>(ids, StringComparer.Ordinal);
        var marks = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        foreach (var s in trace.Steps)
        {
            if (s.Kind != TraceKind.Tool || s.IsError || s.Text is not { } text || (s.EndUtc ?? s.StartUtc) is not { } at) continue;
            if (!text.Contains("tasks.md", StringComparison.OrdinalIgnoreCase) || !text.Contains("[x]", StringComparison.Ordinal)) continue;
            foreach (Match m in MarkedNumber().Matches(text)) Note(m.Groups[1].Value);
            foreach (Match m in MarkedTriage().Matches(text)) Note("F" + m.Groups[1].Value);
            foreach (Match loop in LoopIds().Matches(text))
                foreach (Match n in AnyNumber().Matches(loop.Groups[1].Value)) Note(n.Value);

            void Note(string id)
            {
                if (known.Contains(id)) marks.TryAdd(id, at);
            }
        }
        return marks;
    }

    /// <summary>Cuándo apuntó la sesión cada tarea, por su id: el primer paso que escribe en el tasks.md con esa tarea.</summary>
    /// <remarks>Las que no aparecen en ningún paso estaban antes de la sesión y no salen.</remarks>
    public static Dictionary<string, DateTime> Added(SessionTrace trace, IReadOnlyCollection<string> ids)
    {
        var wanted = ids.Where(id => !id.StartsWith('T')).ToList();
        var added = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        foreach (var s in trace.Steps)
        {
            if (added.Count == wanted.Count) break;
            if (s.Kind != TraceKind.Tool || s.IsError || s.Text is not { } text || (s.EndUtc ?? s.StartUtc) is not { } at) continue;
            if (!text.Contains("tasks.md", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var id in wanted)
            {
                if (added.ContainsKey(id)) continue;
                var pattern = id.StartsWith('F') ? @"Fila\s+" + id[1..] + @"\b" : @"\[[ xX]\]\s+" + Regex.Escape(id) + @"(?![\d.])";
                if (Regex.IsMatch(text, pattern)) added[id] = at;
            }
        }
        return added;
    }

    /// <summary>Los commits que hizo la sesión, en orden: las órdenes «git commit» que no fallaron.</summary>
    public static List<PlanCommit> Commits(SessionTrace trace) =>
        trace.Steps
            .Where(s => s.Kind == TraceKind.Tool && !s.IsError && s.Text is { } t && GitCommit().IsMatch(t) && (s.EndUtc ?? s.StartUtc) is not null
                        && (s.Title.StartsWith("Bash", StringComparison.Ordinal) || s.Title.StartsWith("PowerShell", StringComparison.Ordinal)))
            .Select(s => new PlanCommit((s.EndUtc ?? s.StartUtc)!.Value, s.AgentId, s.Result is { } r && CommitHash().Match(r) is { Success: true } h ? h.Groups[1].Value[..7] : null))
            .ToList();

    private static string TitleOf(string text)
    {
        var cut = text.Length;
        foreach (var stop in new[] { ". ", " (", ": " })
        {
            var i = text.IndexOf(stop, StringComparison.Ordinal);
            if (i > 8 && i < cut) cut = i;
        }
        return Clip(text[..cut].TrimEnd('.', ' '), MaxTitle);
    }

    private static List<string> FilesOf(string body) =>
        Quoted().Matches(body).Select(m => m.Groups[1].Value)
            .Where(v => FileName().IsMatch(v))
            .Select(v => v.Replace('\\', '/').Split('/')[^1])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";

    // Las claves de tarea del panel van en mayúsculas («DEVLAB-183»): así no casa el nombre de una rama como «escena-3d».
    [GeneratedRegex(@"(?<![A-Za-z0-9])[A-Z][A-Z0-9]*-\d+(?!\d)")]
    private static partial Regex KeyPattern();

    [GeneratedRegex(@"^\s*-\s\[([ xX])\]\s+(.+)$")]
    private static partial Regex TaskLine();

    // Números de tarea: «1.3», «6.11» o con letra de ronda, «A.7», «P.1».
    [GeneratedRegex(@"^((?:\d+|[A-Z])(?:\.\d+)+)\s+")]
    private static partial Regex NumberedId();

    [GeneratedRegex(@"^\[Triaje\]\s+Fila\s+(\d+):?\s*", RegexOptions.IgnoreCase)]
    private static partial Regex TriageId();

    [GeneratedRegex(@"\[(?:humana|manual|Mayor|Menor|Media)\]\s*", RegexOptions.IgnoreCase)]
    private static partial Regex Tags();

    [GeneratedRegex(@"`([^`\s]{3,120})`")]
    private static partial Regex Quoted();

    [GeneratedRegex(@"^[\w./\\-]*[\w-]\.[A-Za-z]{1,6}$")]
    private static partial Regex FileName();

    [GeneratedRegex(@"\[x\]\s+((?:\d+|[A-Z])(?:\.\d+)+)\b")]
    private static partial Regex MarkedNumber();

    [GeneratedRegex(@"\[x\]\s+\[Triaje\]\s+Fila\s+(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex MarkedTriage();

    [GeneratedRegex(@"for\s+\$?\w+\s+in\s+([^;]+?)\s*(?:;|\bdo\b)")]
    private static partial Regex LoopIds();

    [GeneratedRegex(@"\b(?:\d+|[A-Z])\.\d+\b")]
    private static partial Regex AnyNumber();

    [GeneratedRegex(@"git(?:\s+-C\s+\S+)?\s+commit\b")]
    private static partial Regex GitCommit();

    [GeneratedRegex(@"\[[^\]\s]+ ([0-9a-f]{7,40})\]")]
    private static partial Regex CommitHash();
}
