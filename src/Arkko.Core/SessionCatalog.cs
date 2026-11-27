using System.Collections.Concurrent;
using System.Globalization;

namespace Arkko.Core;

/// <summary>Una sesión del historial, con lo justo para listarla y buscarla.</summary>
/// <param name="Project">Repositorio si la carpeta era un worktree, la carpeta si no.</param>
/// <param name="Worktree">Carpeta del worktree; null en una carpeta normal o si ya no existe en disco.</param>
/// <param name="FirstPrompt">Principio del primer prompt, con los secretos tapados.</param>
/// <param name="Models">Modelos y esfuerzos con que trabajó, la sesión y sus agentes, del más usado al que menos.</param>
public sealed record SessionSummary(string SessionId, string TranscriptPath, DateTime LastUtc, string? Cwd, string Project,
    string? Worktree, string? Title, string? FirstPrompt, IReadOnlyList<ModelUse>? Models = null);

/// <summary>Un modelo con un esfuerzo («Opus 5.5», «high») y cuánto se usó: respuestas en la sesión, uno por agente.</summary>
/// <remarks>La cuenta solo sirve para ordenarlos; respuestas y agentes no se comparan entre sí.</remarks>
public sealed record ModelUse(string Model, string? Effort, int Count);

/// <summary>Lista las sesiones de Claude Code del equipo cuyo transcript sigue en disco.</summary>
/// <remarks>
/// Lo que se sabe de cada transcript lo lleva <see cref="TranscriptIndex"/>: se lee una vez y después solo lo
/// nuevo. El primer prompt puede estar lejos: una sesión que empieza con un comando trabaja mucho antes de que
/// la persona escriba. Un worktree ya borrado no se puede reconocer: su sesión sale con la carpeta como proyecto.
/// </remarks>
public static class SessionCatalog
{
    /// <summary>Sesiones con actividad desde un instante, de la más reciente a la más antigua; todas si es null.</summary>
    public static List<SessionSummary> List(DateTime? sinceUtc, string? projectsDir = null)
    {
        var root = projectsDir ?? SecretScanner.ProjectsDir;
        if (!Directory.Exists(root)) return [];
        return Directory.EnumerateDirectories(root)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.jsonl", SearchOption.TopDirectoryOnly))
            .Select(path => (Path: path, Last: File.GetLastWriteTimeUtc(path)))
            .Where(x => sinceUtc is null || x.Last >= sinceUtc)
            .OrderByDescending(x => x.Last)
            .Select(x => Summarize(x.Path, x.Last))
            .OfType<SessionSummary>()
            .ToList();
    }

    // La carpeta de cada transcript, por su ruta: no cambia en toda la sesión.
    private static readonly ConcurrentDictionary<string, string> FolderOf = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Las carpetas en que han trabajado las sesiones cuyo transcript sigue en disco, sin repetir.</summary>
    /// <remarks>
    /// Solo lee el principio de cada transcript, donde Claude Code apunta la carpeta: leerlos enteros, como hace la
    /// lista, cuesta segundos con un historial grande, y para esto no hace falta.
    /// </remarks>
    public static List<string> Folders(string? projectsDir = null)
    {
        var root = projectsDir ?? SecretScanner.ProjectsDir;
        if (!Directory.Exists(root)) return [];
        return Directory.EnumerateDirectories(root)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.jsonl", SearchOption.TopDirectoryOnly))
            .Select(path =>
            {
                if (FolderOf.TryGetValue(path, out var known)) return known;
                // Una sesión recién empezada puede no haberla escrito aún: solo se recuerda cuando se encuentra.
                var found = FirstCwd(path);
                if (found is not null) FolderOf[path] = found;
                return found;
            })
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>La primera carpeta que apunta un transcript en sus primeras líneas; null si no la tiene o no se puede leer.</summary>
    private static string? FirstCwd(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            for (var i = 0; i < 64 && reader.ReadLine() is { } line; i++)
            {
                if (!line.Contains("\"cwd\"", StringComparison.Ordinal)) continue;
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(line);
                    if (doc.RootElement.TryGetProperty("cwd", out var cwd) && cwd.GetString() is { Length: > 0 } folder) return folder;
                }
                catch (System.Text.Json.JsonException)
                {
                    // Una línea a medio escribir: se mira la siguiente.
                }
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Resumen de un transcript; null si no se puede leer.</summary>
    public static SessionSummary? Summarize(string path, DateTime? lastUtc = null)
    {
        try
        {
            var index = TranscriptIndex.Of(path);
            var cwd = index.Cwd;
            var folder = cwd is null ? Path.GetFileName(Path.GetDirectoryName(path)) ?? "" : Path.GetFileName(cwd.TrimEnd('/', '\\'));
            var repo = Worktree.RepoOf(cwd);
            return new SessionSummary(Path.GetFileNameWithoutExtension(path), path, lastUtc ?? File.GetLastWriteTimeUtc(path), cwd,
                repo ?? folder, repo is null ? null : folder, index.Title, index.FirstPrompt, Models(path, index.Models));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Lo que dice el .meta.json de cada agente, por su ruta y su hora: no cambia una vez terminado, y la lista se pide a menudo.
    private static readonly ConcurrentDictionary<string, (DateTime Written, string? Model, string? Effort)> AgentModels = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Los modelos de la sesión principal y los de sus agentes, juntos y del más usado al que menos.</summary>
    private static List<ModelUse> Models(string path, List<ModelUse> main)
    {
        var all = main.ToDictionary(m => (m.Model, m.Effort), m => m.Count);
        foreach (var file in TranscriptReader.SubagentFiles(path))
        {
            var meta = Path.ChangeExtension(file, ".meta.json");
            var written = File.Exists(meta) ? File.GetLastWriteTimeUtc(meta) : DateTime.MinValue;
            if (!AgentModels.TryGetValue(meta, out var known) || known.Written != written)
            {
                var read = SessionTraceReader.ReadMeta(file);
                known = (written, read.Model, read.Effort);
                AgentModels[meta] = known;
            }
            if (known.Model is null) continue;
            var key = (Format.ModelName(known.Model), known.Effort);
            all[key] = all.GetValueOrDefault(key) + 1;
        }
        return all.OrderByDescending(m => m.Value).Select(m => new ModelUse(m.Key.Item1, m.Key.Item2, m.Value)).ToList();
    }

    /// <summary>Sesiones que contienen el texto buscado en su título, proyecto, worktree o cualquiera de sus prompts.</summary>
    /// <remarks>Sin distinguir mayúsculas ni acentos: «sesion» encuentra «Sesión».</remarks>
    public static List<SessionSummary> Search(IEnumerable<SessionSummary> sessions, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return sessions.ToList();
        var q = query.Trim();
        bool Has(string? text) => text is not null && Compare.IndexOf(text, q, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
        return sessions.Where(s =>
                Has(s.Title) || Has(s.Project) || Has(s.Worktree) || Has(s.FirstPrompt) || Prompts(s.TranscriptPath).Any(Has))
            .ToList();
    }

    private static readonly CompareInfo Compare = CultureInfo.GetCultureInfo("es-ES").CompareInfo;

    /// <summary>Todos los prompts y comandos de la persona en un transcript, en orden; vacío si no se puede leer.</summary>
    /// <remarks>No cuentan los resultados de herramienta, los mensajes de sistema ni el resumen de una compactación.</remarks>
    public static List<string> Prompts(string path)
    {
        try
        {
            return TranscriptIndex.Of(path).Prompts;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Días locales en que la sesión tuvo actividad: los de la hora de cada entrada del transcript; vacío si no se puede leer.</summary>
    /// <remarks>Una sesión que dura varios días sale en cada uno, no solo en el de su última actividad.</remarks>
    public static HashSet<DateTime> ActiveDays(string path)
    {
        try
        {
            return TranscriptIndex.Of(path).Days;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Último recap que Claude Code escribió en la sesión, con su hora, o null si no hay o no se puede leer.</summary>
    /// <remarks>
    /// Claude Code lo guarda como una entrada de sistema con subtype «away_summary» cuando la sesión lleva un
    /// rato parada. Se lee del índice: en una sesión que trabaja, el transcript crece casi cada segundo.
    /// </remarks>
    public static (string Text, DateTime Utc)? Recap(string path)
    {
        try
        {
            return TranscriptIndex.Of(path).Recap;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
