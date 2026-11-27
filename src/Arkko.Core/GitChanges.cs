using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Arkko.Core;

/// <summary>Desde dónde se cuentan los cambios de una sesión.</summary>
public enum GitScope
{
    /// <summary>Lo pendiente: lo que aún no se ha commiteado, que se puede commitear o descartar.</summary>
    Pending,

    /// <summary>Todo lo de la rama: desde donde se separó de su rama base.</summary>
    Branch,
}

/// <summary>Un fichero cambiado: su ruta en el repositorio, qué le pasó y cuántas líneas entran y salen (null si es binario).</summary>
/// <param name="Status">«added», «modified», «deleted», «renamed», «copied», «type» o «untracked».</param>
public sealed record GitChange(string Path, string Status, int? Added, int? Deleted, string? OldPath = null);

/// <summary>Lo cambiado en la carpeta de una sesión desde una base, o por qué no se ha podido saber.</summary>
/// <param name="Root">Raíz del repositorio, con el formato del sistema.</param>
/// <param name="Base">Commit con que se compara; null si solo cuenta lo no commiteado.</param>
/// <param name="BaseLabel">Qué es esa base para la persona: «develop», «origin/main»…</param>
/// <param name="Note">Un aviso sobre la base elegida, si no es la que se pidió.</param>
/// <param name="Upstream">La rama remota que sigue la actual; null si aún no se ha publicado.</param>
/// <param name="Ahead">Commits de la rama que aún no se han subido a <paramref name="Upstream"/>; null sin ella.</param>
public sealed record GitChangeSet(string? Root, string? Branch, string? Base, string? BaseLabel, string? Note,
    IReadOnlyList<GitChange> Files, bool Truncated, string? Error, string? Upstream = null, int? Ahead = null);

/// <summary>Un texto sacado de git o del disco: el diff de un fichero o el fichero entero; si se cortó, o por qué no se leyó.</summary>
public sealed record GitText(string? Text, bool Truncated, string? Error);

/// <summary>Una línea de un fichero en que aparece lo buscado.</summary>
/// <param name="Column">Dónde empieza la primera coincidencia de la línea, contando desde 1, en bytes.</param>
public sealed record GitGrepHit(string Path, int Line, int Column, string Text);

/// <summary>Lo encontrado al buscar un texto en el repositorio de una sesión; si hay más de lo que se devuelve, o por qué
/// no se ha podido buscar.</summary>
public sealed record GitGrep(IReadOnlyList<GitGrepHit> Hits, bool Truncated, string? Error);

/// <summary>Lee de git, solo para mirar, en qué rama está la carpeta de una sesión y qué ha cambiado en ella.</summary>
/// <remarks>
/// La ruta de un fichero llega de la página, así que solo se lee dentro del repositorio y sin enlaces que lleven fuera.
/// git se lanza sin shell y sin tomar el candado del índice (GIT_OPTIONAL_LOCKS=0): la sesión puede estar usando git en
/// esa misma carpeta, y un «git status» que lo tomara le haría fallar un commit. Tampoco ejecuta lo que el repositorio
/// configure para mirar ficheros (fsmonitor, diff externo, textconv).
/// </remarks>
public static partial class GitChanges
{
    /// <summary>Lo que se lee como mucho de un diff o un fichero; el resto se corta.</summary>
    public const int MaxBytes = 512 * 1024;

    /// <summary>Ficheros que se listan como mucho.</summary>
    public const int MaxFiles = 500;

    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    // El algoritmo con que cuentan las líneas Gitea y GitHub: con el de serie de git, un mismo cambio sale con algunas
    // líneas más o menos añadidas y quitadas (el mismo neto, emparejado de otra forma), y las cifras no cuadraban.
    private const string Algorithm = "--diff-algorithm=histogram";

    // Ramas con que se compara «toda la rama», por orden: la de integración y, si no hay, la principal.
    private static readonly string[] Bases = ["develop", "origin/develop", "main", "origin/main", "master", "origin/master"];

    /// <summary>Los ficheros cambiados en la carpeta de una sesión: lo pendiente, o todo lo de la rama (lo commiteado desde
    /// su base y lo que aún no); con la rama y cuántos commits faltan por subir.</summary>
    public static GitChangeSet List(string? cwd, GitScope scope) => List(cwd, scope, null);

    /// <summary>Los ficheros cambiados en la carpeta de una sesión desde un commit de su rama hasta lo que hay en disco, como
    /// los de toda la rama pero contra ese commit.</summary>
    public static GitChangeSet Since(string? cwd, string commit) =>
        CommitId().IsMatch(commit) ? List(cwd, GitScope.Branch, commit) : Failed(null, "Esa base no es un commit.");

    /// <summary>El commit donde la rama de un repositorio se separó de su base; null si no tiene ninguna con que compararla.</summary>
    internal static string? BranchStart(string root)
    {
        var head = Run(root, "rev-parse", "HEAD", "--abbrev-ref", "HEAD");
        var lines = head.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return head.Ok && lines.Length >= 2 ? KnownBase(root, lines[1] == "HEAD" ? null : lines[1], lines[0]).Commit : null;
    }

    /// <summary>Lo cambiado de un alcance o, si se da, desde un commit concreto.</summary>
    private static GitChangeSet List(string? cwd, GitScope scope, string? since)
    {
        if (RootOf(cwd, out var error) is not { } root) return Failed(null, error);
        // El commit y el nombre de la rama, en una sola llamada.
        var head = Run(root, "rev-parse", "HEAD", "--abbrev-ref", "HEAD");
        var lines = head.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!head.Ok || lines.Length < 2) return Failed(root, "El repositorio aún no tiene ningún commit.");
        var (commit, name) = (lines[0], lines[1]);
        var detached = name == "HEAD";
        var branch = detached ? Run(root, "rev-parse", "--short", "HEAD").Out.Trim() : name;

        // Lo que falta por subir no depende de lo demás: se pregunta a la vez, que cada git que se lanza cuesta.
        var unpushed = detached ? Task.FromResult<(string?, int?)>((null, null)) : Task.Run(() => Unpushed(root));
        string? start = null, label = null, note = null;
        if (since is not null) start = since;
        else if (scope == GitScope.Branch) (start, label, note) = KnownBase(root, detached ? null : name, commit);

        var files = Files(root, start, out var truncated, out var failure);
        var (upstream, ahead) = unpushed.Result;
        return failure is not null
            ? Failed(root, failure)
            : new(root, branch, start, label, note, files, truncated, null, upstream, ahead);
    }

    /// <summary>La rama remota que sigue la actual y cuántos commits suyos aún no se han subido; null y null si no sigue ninguna.</summary>
    internal static (string? Upstream, int? Ahead) Unpushed(string root)
    {
        var upstream = Run(root, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}");
        if (!upstream.Ok || upstream.Out.Trim() is not { Length: > 0 } tracked) return (null, null);
        var count = Run(root, "rev-list", "--count", "@{upstream}..HEAD");
        return (tracked, count.Ok && int.TryParse(count.Out.Trim(), out var n) ? n : null);
    }

    /// <summary>El diff de un fichero desde una base hasta lo que hay ahora en disco, con git.</summary>
    /// <param name="baseCommit">El commit que devolvió <see cref="List"/>; null para compararlo con HEAD.</param>
    /// <param name="oldPath">Su ruta anterior, si se renombró.</param>
    public static GitText Diff(string? cwd, string? baseCommit, string path, string? oldPath = null)
    {
        if (RootOf(cwd, out var error) is not { } root) return new(null, false, error);
        if (baseCommit is not null && !CommitId().IsMatch(baseCommit)) return new(null, false, "Esa base no es un commit.");
        if (Inside(root, path) is null || (oldPath is not null && Inside(root, oldPath) is null))
            return new(null, false, "Solo se enseñan ficheros del repositorio de la sesión.");
        List<string> args = ["diff", "-M", Algorithm, "--no-ext-diff", "--no-textconv", "--no-color", baseCommit ?? "HEAD", "--"];
        if (oldPath is not null) args.Add(oldPath);
        args.Add(path);
        var diff = Run(root, MaxBytes, [.. args]);
        if (!diff.Ok) return new(null, false, diff.Error ?? "git no ha podido sacar el diff.");
        return new(diff.Out, diff.Truncated, null);
    }

    /// <summary>Un fichero del repositorio de una sesión, entero y tal como está ahora en disco.</summary>
    public static GitText ReadFile(string? cwd, string path)
    {
        if (RootOf(cwd, out var error) is not { } root) return new(null, false, error);
        if (Inside(root, path) is not { } full) return new(null, false, "Solo se enseñan ficheros del repositorio de la sesión.");
        var info = new FileInfo(full);
        if (!info.Exists) return new(null, false, "El fichero ya no está en disco: se ha borrado.");
        if (LeavesRoot(root, info)) return new(null, false, "Solo se enseñan ficheros del repositorio de la sesión.");
        try
        {
            using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[Math.Min(info.Length, MaxBytes)];
            var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            if (Array.IndexOf(buffer, (byte)0, 0, read) >= 0) return new(null, false, "Es un fichero binario: no se enseña.");
            return new(Encoding.UTF8.GetString(buffer, 0, read), info.Length > MaxBytes, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(null, false, "No se ha podido leer el fichero.");
        }
    }

    /// <summary>Ficheros del repositorio que se listan como mucho al enseñarlo entero.</summary>
    public const int MaxRepoFiles = 20000;

    /// <summary>Todos los ficheros del repositorio de una sesión que hay en disco: los versionados y los nuevos que el
    /// <c>.gitignore</c> no deja fuera, ordenados por ruta y con la rama en que está.</summary>
    /// <remarks>Cada fichero va con el estado «tracked» y sin líneas: aquí no se mira qué ha cambiado.</remarks>
    public static GitChangeSet ListAll(string? cwd)
    {
        if (RootOf(cwd, out var error) is not { } root) return Failed(null, error);
        var branchRun = Task.Run(() => Run(root, "rev-parse", "--abbrev-ref", "HEAD"));
        var goneRun = Task.Run(() => Run(root, "ls-files", "--deleted", "-z"));
        var listed = Run(root, "ls-files", "--cached", "--others", "--exclude-standard", "-z");
        var (branch, gone) = (branchRun.Result, goneRun.Result);
        if (!listed.Ok || !gone.Ok) return Failed(root, listed.Error ?? gone.Error ?? "git no ha podido listar los ficheros del repositorio.");

        // Un fichero borrado del disco y aún sin commitear sigue en el índice: no se puede abrir.
        var deleted = gone.Out.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var paths = listed.Out.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !deleted.Contains(p))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var name = branch.Ok ? branch.Out.Trim() : "";
        var files = paths.Take(MaxRepoFiles).Select(p => new GitChange(p, "tracked", null, null)).ToList();
        return new(root, name is "" or "HEAD" ? null : name, null, null, null, files, paths.Count > MaxRepoFiles, null);
    }

    /// <summary>Coincidencias que se devuelven como mucho al buscar texto en el repositorio.</summary>
    public const int MaxGrepHits = 2000;

    /// <summary>Caracteres de cada línea encontrada que se devuelven; el resto se corta.</summary>
    public const int MaxGrepLine = 300;

    // Lo que se lee como mucho de la salida de una búsqueda: de sobra para las coincidencias que se devuelven.
    private const int MaxGrepChars = 4 * 1024 * 1024;

    // Lo que puede tardar una búsqueda en un repositorio grande.
    private static readonly TimeSpan GrepLimit = TimeSpan.FromSeconds(10);

    /// <summary>Busca un texto en los ficheros del repositorio de una sesión, con git grep: los versionados y los nuevos que
    /// el <c>.gitignore</c> no deja fuera, sin binarios.</summary>
    /// <param name="matchCase">Si distingue mayúsculas.</param>
    /// <param name="wholeWord">Si solo vale la palabra entera.</param>
    /// <param name="regex">Si lo buscado es una expresión regular extendida; si no, se busca tal cual.</param>
    /// <remarks>Lo buscado va detrás de <c>-e</c>: aunque empiece por un guion, git no lo toma por una opción.</remarks>
    public static GitGrep Grep(string? cwd, string query, bool matchCase, bool wholeWord, bool regex)
    {
        if (string.IsNullOrEmpty(query)) return new([], false, "Escribe algo que buscar.");
        if (RootOf(cwd, out var error) is not { } root) return new([], false, error);
        List<string> args = ["grep", "-n", "--column", "-I", "--untracked", "--null", "--no-color", regex ? "-E" : "-F"];
        if (!matchCase) args.Add("-i");
        if (wholeWord) args.Add("-w");
        args.AddRange(["-e", query, "--"]);
        var found = Run(root, MaxGrepChars, null, GrepLimit, [.. args]);
        // Sin coincidencias, git grep acaba con error y sin decir nada: no es un fallo.
        if (!found.Ok && found.Error is null && found.Err.Trim().Length == 0) return new([], false, null);
        if (!found.Ok)
            return new([], false, found.Error ?? (regex ? "La expresión regular no es válida." : "git no ha podido buscar en el repositorio."));

        var hits = new List<GitGrepHit>();
        var lines = found.Out.Split('\n');
        // Con la salida cortada, la última línea puede estar a medias: no se cuenta.
        var usable = found.Truncated ? lines.Length - 1 : lines.Length;
        var more = found.Truncated;
        for (var i = 0; i < usable; i++)
        {
            var parts = lines[i].Split('\0', 4);
            if (parts.Length < 4 || !int.TryParse(parts[1], out var line) || !int.TryParse(parts[2], out var column)) continue;
            if (hits.Count == MaxGrepHits)
            {
                more = true;
                break;
            }
            var text = parts[3].TrimEnd('\r');
            hits.Add(new(parts[0], line, column, text.Length > MaxGrepLine ? text[..MaxGrepLine] : text));
        }
        return new(hits, more, null);
    }

    /// <summary>El alcance que pide la página por su nombre; lo pendiente si no lo reconoce.</summary>
    public static GitScope ScopeOf(string? name) => name == "branch" ? GitScope.Branch : GitScope.Pending;

    // La raíz del repositorio de cada carpeta, con cuándo se preguntó.
    private static readonly ConcurrentDictionary<string, (string Root, DateTime AtUtc)> Roots = new();

    // Lo que se da por buena una raíz ya preguntada.
    private static readonly TimeSpan RootFor = TimeSpan.FromSeconds(30);

    /// <summary>Raíz del repositorio de una carpeta; null, con el motivo, si no lo es o no existe.</summary>
    /// <remarks>
    /// Se recuerda un rato: cada lectura de lo cambiado o de la revisión la pide, varias veces seguidas, y cada git que se
    /// lanza cuesta. Una carpeta que ya no existe no se da por buena, aunque se recuerde.
    /// </remarks>
    internal static string? RootOf(string? cwd, out string error)
    {
        error = "";
        if (McpFiles.Root(cwd) is not { } dir || !Directory.Exists(dir))
        {
            error = "No se sabe en qué carpeta trabaja la sesión, o ya no existe.";
            return null;
        }
        if (Roots.TryGetValue(dir, out var known) && DateTime.UtcNow - known.AtUtc < RootFor) return known.Root;
        var top = Run(dir, "rev-parse", "--show-toplevel");
        if (!top.Ok || top.Out.Trim() is not { Length: > 0 } path)
        {
            error = top.Error ?? "La carpeta de la sesión no es un repositorio de git.";
            return null;
        }
        var root = Path.GetFullPath(path);
        Roots[dir] = (root, DateTime.UtcNow);
        return root;
    }

    // La base de cada rama en cada commit, con cuándo se buscó.
    private static readonly ConcurrentDictionary<string, ((string? Commit, string? Label, string? Note) Base, DateTime AtUtc)> KnownBases = new();

    // Lo que se da por buena una base ya buscada: la rama base puede avanzar al traer cambios del remoto.
    private static readonly TimeSpan BaseFor = TimeSpan.FromMinutes(1);

    /// <summary>La base de la rama (<see cref="BranchBase"/>), recordada mientras la rama siga en el mismo commit.</summary>
    /// <remarks>Mientras se trabaja, lo cambiado se vuelve a leer con cada fichero que se guarda, y la base no cambia: buscarla
    /// son tres llamadas a git seguidas. Un commit nuevo la vuelve a buscar, y la que se recuerda caduca al minuto.</remarks>
    private static (string? Commit, string? Label, string? Note) KnownBase(string root, string? branch, string commit)
    {
        var key = $"{root}|{branch}|{commit}";
        if (KnownBases.TryGetValue(key, out var known) && DateTime.UtcNow - known.AtUtc < BaseFor) return known.Base;
        var found = BranchBase(root, branch);
        KnownBases[key] = (found, DateTime.UtcNow);
        return found;
    }

    /// <summary>Donde se separó la rama de su base, con el nombre de la base; o, sin base, solo lo no commiteado y por qué.</summary>
    /// <remarks>
    /// En la rama base misma (develop, main), «toda la rama» es lo que aún no se ha subido: se compara con la que sigue.
    /// </remarks>
    private static (string? Commit, string? Label, string? Note) BranchBase(string root, string? branch)
    {
        var originHead = Run(root, "symbolic-ref", "--quiet", "--short", "refs/remotes/origin/HEAD");
        var candidates = Bases.ToList();
        if (originHead.Ok && originHead.Out.Trim() is { Length: > 0 } remote) candidates.Insert(2, remote);
        var isBase = branch is not null && candidates.Any(c => c == branch || c == "origin/" + branch);
        if (isBase)
        {
            var upstream = Run(root, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}");
            if (upstream.Ok && upstream.Out.Trim() is { Length: > 0 } tracked && MergeBase(root, tracked) is { } pushed)
                return (pushed, tracked, "Es una rama base: se ve lo que aún no se ha subido.");
            return (null, null, "Es una rama base sin otra con que compararla: se ve solo lo no commiteado.");
        }
        foreach (var candidate in candidates.Distinct())
        {
            if (!Run(root, "rev-parse", "--verify", "--quiet", candidate + "^{commit}").Ok) continue;
            if (MergeBase(root, candidate) is { } start) return (start, candidate, null);
        }
        return (null, null, "No hay develop, main ni master con que compararla: se ve solo lo no commiteado.");
    }

    /// <summary>El último commit que la rama actual comparte con otra; null si no comparten ninguno.</summary>
    private static string? MergeBase(string root, string other)
    {
        var found = Run(root, "merge-base", "HEAD", other);
        return found.Ok && CommitId().IsMatch(found.Out.Trim()) ? found.Out.Trim() : null;
    }

    /// <summary>Los ficheros cambiados desde un commit (o desde HEAD) hasta lo que hay en disco, más los nuevos sin seguimiento.</summary>
    private static List<GitChange> Files(string root, string? baseCommit, out bool truncated, out string? error)
    {
        truncated = false;
        error = null;
        var from = baseCommit ?? "HEAD";
        // Las tres lecturas, a la vez: solo leen (GIT_OPTIONAL_LOCKS=0), y cada git que se lanza cuesta lo suyo.
        var statusRun = Task.Run(() => Run(root, "diff", "--name-status", "-z", "-M", "--no-ext-diff", from));
        var countsRun = Task.Run(() => Run(root, "diff", "--numstat", "-z", "-M", Algorithm, "--no-ext-diff", "--no-textconv", from));
        var others = Run(root, "ls-files", "--others", "--exclude-standard", "-z");
        var (status, counts) = (statusRun.Result, countsRun.Result);
        if (!status.Ok || !counts.Ok || !others.Ok)
        {
            error = status.Error ?? counts.Error ?? others.Error ?? "git no ha podido decir qué ha cambiado.";
            return [];
        }

        var lines = NumStat(counts.Out);
        var files = new List<GitChange>();
        var parts = status.Out.Split('\0');
        for (var i = 0; i + 1 < parts.Length && parts[i].Length > 0; i += 2)
        {
            var code = parts[i][0];
            string? old = null;
            if (code is 'R' or 'C' && i + 2 < parts.Length)
            {
                old = parts[i + 1];
                i++;
            }
            var path = parts[i + 1];
            var (added, deleted) = lines.GetValueOrDefault(path);
            files.Add(new(path, StatusName(code), added, deleted, old));
        }
        foreach (var path in others.Out.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            files.Add(new(path, "untracked", LineCount(Path.Combine(root, path)), 0));

        truncated = files.Count > MaxFiles;
        return files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).Take(MaxFiles).ToList();
    }

    /// <summary>Líneas añadidas y quitadas de cada fichero, de la salida de «git diff --numstat -z»; null en un binario.</summary>
    private static Dictionary<string, (int? Added, int? Deleted)> NumStat(string output)
    {
        var result = new Dictionary<string, (int?, int?)>(StringComparer.Ordinal);
        var parts = output.Split('\0');
        for (var i = 0; i < parts.Length; i++)
        {
            var fields = parts[i].Split('\t');
            if (fields.Length < 3) continue;
            int? added = int.TryParse(fields[0], out var a) ? a : null;
            int? deleted = int.TryParse(fields[1], out var d) ? d : null;
            // Un renombrado deja la ruta vacía y pone después la de antes y la de ahora.
            var path = fields[2];
            if (path.Length == 0 && i + 2 < parts.Length)
            {
                path = parts[i + 2];
                i += 2;
            }
            result[path] = (added, deleted);
        }
        return result;
    }

    /// <summary>Lo que le pasó a un fichero según la letra de «git diff --name-status».</summary>
    private static string StatusName(char code) => code switch
    {
        'A' => "added",
        'D' => "deleted",
        'R' => "renamed",
        'C' => "copied",
        'T' => "type",
        _ => "modified",
    };

    /// <summary>Líneas de un fichero nuevo, para contarlas como añadidas; null si es binario, grande o no se puede leer.</summary>
    private static int? LineCount(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxBytes) return null;
            var bytes = File.ReadAllBytes(path);
            if (Array.IndexOf(bytes, (byte)0) >= 0) return null;
            var count = bytes.Count(b => b == (byte)'\n');
            return bytes.Length > 0 && bytes[^1] != (byte)'\n' ? count + 1 : count;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>La ruta completa de un fichero del repositorio dada su ruta relativa; null si es absoluta o sale de él.</summary>
    internal static string? Inside(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) return null;
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return full.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, comparison) ? full : null;
    }

    /// <summary>Si el fichero, o alguna carpeta entre él y la raíz, es un enlace: llevaría fuera sin que la ruta lo dijera.</summary>
    private static bool LeavesRoot(string root, FileInfo file)
    {
        var top = Path.TrimEndingDirectorySeparator(root);
        if (file.Attributes.HasFlag(FileAttributes.ReparsePoint)) return true;
        for (var dir = file.Directory; dir is not null && dir.FullName.Length > top.Length; dir = dir.Parent)
            if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint)) return true;
        return false;
    }

    /// <summary>Un fallo con su motivo y sin ficheros.</summary>
    private static GitChangeSet Failed(string? root, string error) => new(root, null, null, null, null, [], false, error);

    /// <summary>Lo que devolvió git: si salió bien, su salida (cortada si hacía falta), sus avisos y, si falló, por qué.</summary>
    internal sealed record Result(bool Ok, string Out, string Err, bool Truncated, string? Error);

    /// <summary>Lanza git en una carpeta con unos argumentos, sin límite de salida.</summary>
    internal static Result Run(string dir, params string[] args) => Run(dir, int.MaxValue, null, Limit, args);

    /// <summary>Lanza git en una carpeta, lee como mucho tantos caracteres de su salida y lo para si tarda.</summary>
    private static Result Run(string dir, int maxChars, params string[] args) => Run(dir, maxChars, null, Limit, args);

    /// <summary>Lanza git para que hable con el remoto, que puede tardar más que lo local (red, credenciales).</summary>
    internal static Result RunRemote(string dir, params string[] args) => Run(dir, int.MaxValue, null, TimeSpan.FromSeconds(20), args);

    /// <summary>Lanza git en una carpeta con un texto en su entrada, lee como mucho tantos caracteres de su salida y lo para
    /// si tarda más de lo que se le da.</summary>
    /// <remarks>
    /// Sin ventanas de credenciales (GCM_INTERACTIVE=never): una orden que hable con el remoto y no tenga permiso falla, en
    /// lugar de quedarse esperando a que la persona escriba algo.
    /// </remarks>
    internal static Result Run(string dir, int maxChars, string? input, TimeSpan limit, params string[] args)
    {
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = dir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = input is not null,
            StandardInputEncoding = input is null ? null : new UTF8Encoding(false),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in (string[])["-c", "core.fsmonitor=false", "-c", "core.quotepath=false", .. args]) info.ArgumentList.Add(arg);
        info.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        info.Environment["GCM_INTERACTIVE"] = "never";
        info.Environment["LC_ALL"] = "C";
        try
        {
            using var git = Process.Start(info);
            if (git is null) return new(false, "", "", false, "No se ha podido lanzar git.");
            if (input is not null)
            {
                git.StandardInput.Write(input);
                git.StandardInput.Close();
            }
            var err = git.StandardError.ReadToEndAsync();
            var read = Task.Run(() => ReadUpTo(git.StandardOutput, maxChars));
            if (!read.Wait(limit))
            {
                Stop(git);
                return new(false, "", "", false, "git ha tardado demasiado en contestar.");
            }
            var (text, truncated) = read.Result;
            // Con la salida cortada, git se queda esperando a que se lea el resto: se para.
            if (truncated)
            {
                Stop(git);
                return new(true, text, "", true, null);
            }
            if (!git.WaitForExit(limit))
            {
                Stop(git);
                return new(false, "", "", false, "git ha tardado demasiado en contestar.");
            }
            return new(git.ExitCode == 0, text, err.Wait(limit) ? err.Result : "", false, null);
        }
        catch (Win32Exception)
        {
            return new(false, "", "", false, "No se encuentra git en este equipo.");
        }
    }

    /// <summary>Lee de un flujo hasta tantos caracteres; dice si quedaba más.</summary>
    private static (string Text, bool Truncated) ReadUpTo(StreamReader reader, int maxChars)
    {
        var text = new StringBuilder();
        var buffer = new char[8192];
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (text.Length + read > maxChars)
            {
                text.Append(buffer, 0, maxChars - text.Length);
                return (text.ToString(), true);
            }
            text.Append(buffer, 0, read);
        }
        return (text.ToString(), false);
    }

    /// <summary>Para un git que tarda o cuya salida ya no se va a leer.</summary>
    private static void Stop(Process git)
    {
        try
        {
            git.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Ya había terminado.
        }
    }

    [GeneratedRegex("^[0-9a-f]{7,64}$")]
    private static partial Regex CommitId();
}
