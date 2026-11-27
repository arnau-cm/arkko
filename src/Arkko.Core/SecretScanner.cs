using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Arkko.Core;

/// <summary>Un secreto encontrado en un transcript. Nunca guarda el valor: solo su máscara y su huella.</summary>
public sealed class SecretFinding
{
    public DateTime WhenUtc { get; set; }
    public string SessionId { get; set; } = "";
    public string? Project { get; set; }

    /// <summary>Repositorio cuando la carpeta era un worktree; null en una carpeta normal y en los hallazgos anteriores a guardarlo.</summary>
    public string? Repo { get; set; }

    public string Transcript { get; set; } = "";
    public string Kind { get; set; } = "";

    /// <summary>Los 4 primeros y los 4 últimos caracteres del valor.</summary>
    public string Masked { get; set; } = "";

    /// <summary>HMAC del valor con la clave del equipo: reconoce el mismo secreto sin permitir recuperarlo.</summary>
    public string Fingerprint { get; set; } = "";

    /// <summary>Dónde apareció: «Tu mensaje», «Respuesta de Claude», «Entrada de Bash», «Salida de Read»…</summary>
    public string Source { get; set; } = "";
    public string? Tool { get; set; }

    /// <summary>Fichero leído o escrito por la herramienta, si lo había.</summary>
    public string? File { get; set; }

    /// <summary>Si apareció en el transcript de un subagente.</summary>
    public bool Subagent { get; set; }

    /// <summary>Si está escrita tal cual en un fichero de configuración que Claude puede leer, aún sin enviar; false si pasó por una conversación.</summary>
    public bool Written { get; set; }

    /// <summary>La llamada a herramienta en cuya entrada o salida apareció; null en un mensaje, un prompt o un fichero.</summary>
    public string? ToolUseId { get; set; }

    /// <summary>En qué carácter del texto analizado empieza el valor; null si no se sabe.</summary>
    public int? Position { get; set; }

    /// <summary>Cuántos caracteres tenía el texto analizado: con <see cref="Position"/>, dice si cae más allá de lo que enseña un paso.</summary>
    public int? TextLength { get; set; }

    /// <summary>Si ya se buscó dónde apareció: en los hallazgos de antes de guardarlo, se busca una sola vez en su transcript.</summary>
    public bool Located { get; set; }
}

/// <summary>Lo que se enseña de una aparición al pedirlo, leído de su transcript en el momento y nunca guardado.</summary>
/// <param name="Context">El texto de alrededor, con el secreto y cualquier otro tapados por su máscara.</param>
/// <param name="Call">La llamada que lo produjo, si salió en la salida de una herramienta: su nombre y su entrada, tapadas.</param>
public sealed record SecretSpot(string Context, string? Call);

/// <summary>Qué bloques se miran en un fichero de configuración.</summary>
public enum ConfigKind
{
    /// <summary>Un settings.json o settings.local.json: su bloque env.</summary>
    Settings,

    /// <summary>El .mcp.json de un proyecto: sus servidores MCP.</summary>
    Mcp,

    /// <summary>El ~/.claude.json de Claude Code: sus servidores MCP y los de cada proyecto.</summary>
    ClaudeState,
}

/// <summary>Un fichero de configuración donde puede haber claves escritas.</summary>
/// <param name="ProjectDir">Carpeta del proyecto al que pertenece; null si es de todo el equipo.</param>
public sealed record ConfigFile(string Path, ConfigKind Kind, string? ProjectDir = null);

/// <summary>Analiza transcripts en busca de secretos y guarda los hallazgos en data/security.</summary>
/// <remarks>
/// El análisis es incremental: de cada transcript se recuerda hasta qué byte se leyó, y solo se leen
/// líneas completas, porque Claude Code puede estar escribiendo la última. Un mismo secreto se apunta
/// una vez por sesión.
///
/// Los hallazgos, los ignorados y los rotados se guardan cifrados para el usuario: aunque van enmascarados, dicen qué
/// tipo de secreto hay en qué fichero de qué proyecto. El estado del análisis (rutas y bytes leídos)
/// va en claro: no dice nada de los secretos.
/// </remarks>
public sealed partial class SecretScanner
{
    private readonly string _dir;
    private readonly byte[] _key;

    // Repositorio de cada carpeta ya vista: cada línea del transcript trae su carpeta, y mirar el disco por línea sobra.
    private readonly Dictionary<string, string?> _repos = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Trabaja sobre la carpeta de datos indicada; por defecto, la de la app.</summary>
    public SecretScanner(string? dataDir = null)
    {
        _dir = dataDir is null ? AppPaths.Security : Path.Combine(dataDir, "security");
        Directory.CreateDirectory(_dir);
        _key = LoadKey();
        MigratePlain();
    }

    private string FindingsFile => Path.Combine(_dir, "findings.bin");
    private string IgnoredFile => Path.Combine(_dir, "ignored.bin");
    private string RotatedFile => Path.Combine(_dir, "rotated.bin");
    private string StateFile => Path.Combine(_dir, "scan-state.json");
    private string KeyFile => Path.Combine(_dir, "key.bin");

    // Claves escritas en la configuración: una foto de cómo están ahora, que se rehace en cada análisis.
    private string WrittenFile => Path.Combine(_dir, "written.bin");

    // Formato en claro de la primera versión; se cifra y se borra al abrir el almacén.
    private string PlainFindingsFile => Path.Combine(_dir, "findings.jsonl");
    private string PlainIgnoredFile => Path.Combine(_dir, "ignored.json");

    /// <summary>Carpeta de transcripts de Claude Code de este equipo.</summary>
    public static string ProjectsDir => Path.Combine(AppPaths.ClaudeConfigDir, "projects");

    /// <summary>Todos los transcripts del equipo: los de Claude Code, con los de sus subagentes, y los de Cowork.</summary>
    public static IEnumerable<string> AllTranscripts() =>
        (Directory.Exists(ProjectsDir) ? Directory.EnumerateFiles(ProjectsDir, "*.jsonl", SearchOption.AllDirectories) : [])
        .Concat(CoworkDirs().SelectMany(CoworkTranscripts));

    // Carpeta en que Claude Desktop guarda cada sesión de Cowork, dentro de sus datos.
    private const string CoworkFolder = "local-agent-mode-sessions";

    /// <summary>Carpetas donde Claude Desktop guarda las sesiones de Cowork, las que existan.</summary>
    /// <remarks>
    /// Instalado de la forma normal, en %APPDATA%\Claude; desde la Microsoft Store, Windows guarda sus datos
    /// dentro de su paquete, en %LOCALAPPDATA%\Packages\Claude_…\LocalCache\Roaming\Claude. En macOS, en
    /// ~/Library/Application Support/Claude, y en Linux, en ~/.config/Claude.
    /// </remarks>
    public static List<string> CoworkDirs()
    {
        var appData = OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dirs = new List<string> { Path.Combine(appData, "Claude", CoworkFolder) };
        var packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
        if (Directory.Exists(packages))
            dirs.AddRange(Directory.EnumerateDirectories(packages, "Claude_*")
                .Select(p => Path.Combine(p, "LocalCache", "Roaming", "Claude", CoworkFolder)));
        return dirs.Where(Directory.Exists).ToList();
    }

    /// <summary>Transcripts de las sesiones de Cowork de una carpeta: los de la carpeta .claude\projects de cada una.</summary>
    /// <remarks>
    /// Cada sesión lleva su propio Claude Code, con su carpeta .claude, y los guarda con el formato de siempre.
    /// Su audit.jsonl repite la misma conversación con otro formato: analizarlo daría cada secreto dos veces.
    /// </remarks>
    public static IEnumerable<string> CoworkTranscripts(string coworkDir)
    {
        var projects = Path.DirectorySeparatorChar + ".claude" + Path.DirectorySeparatorChar + "projects" + Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(coworkDir, "*.jsonl", SearchOption.AllDirectories)
            .Where(p => p.Contains(projects, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Indica si un transcript es de una sesión de Cowork.</summary>
    public static bool IsCowork(string path) =>
        path.Contains(Path.DirectorySeparatorChar + CoworkFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>Transcripts escritos desde un instante: los únicos que pueden traer algo nuevo.</summary>
    public static IEnumerable<string> TranscriptsSince(DateTime sinceUtc) =>
        AllTranscripts().Where(p => System.IO.File.GetLastWriteTimeUtc(p) >= sinceUtc);

    /// <summary>Historial de prompts de Claude Code: lo que se escribe en todas las sesiones. Ningún plazo lo borra.</summary>
    public static string HistoryFile => Path.Combine(AppPaths.ClaudeConfigDir, "history.jsonl");

    /// <summary>Textos largos pegados en un prompt, uno por fichero; el historial los cita por su nombre.</summary>
    public static string PasteCacheDir => Path.Combine(AppPaths.ClaudeConfigDir, "paste-cache");

    // Un texto pegado que ningún prompt cita aún puede estar esperando a que se envíe; pasado este rato, no se envió.
    private static readonly TimeSpan PasteWait = TimeSpan.FromDays(1);

    /// <summary>Estado de Claude Code, con sus proyectos y sus servidores MCP: ~/.claude.json, o dentro de CLAUDE_CONFIG_DIR si se ha cambiado.</summary>
    public static string ClaudeStateFile
    {
        get
        {
            var custom = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            return string.IsNullOrWhiteSpace(custom)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json")
                : Path.Combine(custom, ".claude.json");
        }
    }

    // Los ficheros de Claude Code admiten comentarios y comas de sobra; un JSON estricto se saltaría algunos.
    private static readonly JsonDocumentOptions ConfigJson = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    /// <summary>Ficheros de configuración donde puede haber claves escritas, sin repetir y existan o no.</summary>
    /// <remarks>
    /// Los de todo el equipo, los de cada proyecto que conoce Claude Code (sus carpetas salen de
    /// ~/.claude.json) y las copias de settings.json que guarda la app antes de cambiarlo.
    /// </remarks>
    public static List<ConfigFile> ConfigFiles()
    {
        var files = new List<ConfigFile>
        {
            new(AppPaths.ClaudeSettingsFile, ConfigKind.Settings),
            new(Path.Combine(AppPaths.ClaudeConfigDir, "settings.local.json"), ConfigKind.Settings),
            new(ClaudeStateFile, ConfigKind.ClaudeState),
        };
        foreach (var dir in ProjectDirs(ClaudeStateFile))
        {
            files.Add(new(Path.Combine(dir, ".claude", "settings.json"), ConfigKind.Settings, dir));
            files.Add(new(Path.Combine(dir, ".claude", "settings.local.json"), ConfigKind.Settings, dir));
            files.Add(new(Path.Combine(dir, ".mcp.json"), ConfigKind.Mcp, dir));
        }
        if (Directory.Exists(AppPaths.Backups))
            files.AddRange(Directory.EnumerateFiles(AppPaths.Backups, "*.json").Select(p => new ConfigFile(p, ConfigKind.Settings)));
        // La carpeta personal también puede ser un proyecto: su .claude\settings.json es el de todo el equipo.
        return files.DistinctBy(f => Path.GetFullPath(f.Path), StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Carpetas de los proyectos que conoce Claude Code y siguen existiendo.</summary>
    private static List<string> ProjectDirs(string stateFile)
    {
        try
        {
            if (!System.IO.File.Exists(stateFile)) return [];
            using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(stateFile), ConfigJson);
            if (!doc.RootElement.TryGetProperty("projects", out var projects) || projects.ValueKind != JsonValueKind.Object) return [];
            return projects.EnumerateObject().Select(p => Path.GetFullPath(p.Name)).Where(Directory.Exists).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            JsonStore.LogError("security-config", ex);
            return [];
        }
    }

    /// <summary>Hallazgos guardados y claves escritas en la configuración, sin los marcados como falso positivo.</summary>
    public List<SecretFinding> Findings()
    {
        var ignored = Ignored();
        return LoadFindings().Concat(LoadWritten()).Where(f => !ignored.Contains(f.Fingerprint)).ToList();
    }

    /// <summary>Hallazgos y claves escritas de los secretos marcados como falso positivo.</summary>
    public List<SecretFinding> IgnoredFindings()
    {
        var ignored = Ignored();
        return LoadFindings().Concat(LoadWritten()).Where(f => ignored.Contains(f.Fingerprint)).ToList();
    }

    /// <summary>Todos los hallazgos de lo enviado, también los marcados como falso positivo; sin las claves escritas, que no se eliminan.</summary>
    public List<SecretFinding> AllFindings() => LoadFindings();

    /// <summary>Cuántos secretos distintos están marcados como falso positivo.</summary>
    public int IgnoredCount() => Ignored().Count;

    /// <summary>Marca un secreto como falso positivo: no vuelve a salir, en ninguna sesión.</summary>
    public void Ignore(string fingerprint)
    {
        using var _ = JsonStore.Lock("security-ignored");
        var ignored = Ignored();
        if (ignored.Add(fingerprint)) WriteProtected(IgnoredFile, ignored.Order().ToList());
    }

    /// <summary>Quita la marca de falso positivo a un secreto: vuelve a la lista de pendientes.</summary>
    public void Unignore(string fingerprint)
    {
        using var _ = JsonStore.Lock("security-ignored");
        var ignored = Ignored();
        if (ignored.Remove(fingerprint)) WriteProtected(IgnoredFile, ignored.Order().ToList());
    }

    /// <summary>Borra del almacén todas las apariciones de unos secretos; devuelve cuántas ha borrado.</summary>
    /// <remarks>
    /// No vuelven al analizar de nuevo, porque lo ya leído de cada transcript no se relee; si el mismo
    /// secreto aparece en algo escrito después, sale otra vez. Las marcas de rotado y falso positivo
    /// se conservan: son del valor, no de la aparición.
    /// </remarks>
    public int Delete(IReadOnlySet<string> fingerprints)
    {
        // El mismo cerrojo que el análisis: si no, un análisis en marcha reescribiría lo borrado.
        using var _ = JsonStore.Lock("security-scan");
        var all = LoadFindings();
        var kept = all.Where(f => !fingerprints.Contains(f.Fingerprint)).ToList();
        if (kept.Count < all.Count) WriteProtected(FindingsFile, kept);
        return all.Count - kept.Count;
    }

    /// <summary>Vuelve a mostrar todos los marcados como falso positivo.</summary>
    public void ClearIgnored()
    {
        using var _ = JsonStore.Lock("security-ignored");
        System.IO.File.Delete(IgnoredFile);
    }

    /// <summary>Secretos que la persona ya ha cambiado en su servicio: siguen en la lista, pero no como pendientes.</summary>
    public HashSet<string> Rotated() => [.. ReadProtected<List<string>>(RotatedFile, out _) ?? []];

    /// <summary>Secretos ya resueltos en Seguridad: marcados como falso positivo o como rotados.</summary>
    public HashSet<string> Settled() => [.. Ignored(), .. Rotated()];

    /// <summary>Marca o desmarca un secreto como ya rotado.</summary>
    public void SetRotated(string fingerprint, bool rotated)
    {
        using var _ = JsonStore.Lock("security-rotated");
        var set = Rotated();
        if (rotated ? set.Add(fingerprint) : set.Remove(fingerprint)) WriteProtected(RotatedFile, set.Order().ToList());
    }

    /// <summary>Analiza lo nuevo de unos transcripts y devuelve los hallazgos nuevos.</summary>
    public List<SecretFinding> Scan(IEnumerable<string> transcripts)
    {
        using var _ = JsonStore.Lock("security-scan");
        var state = ReadState();
        var all = LoadFindings();
        var known = all.Select(f => (f.Fingerprint, f.SessionId)).ToHashSet();
        var fresh = new List<SecretFinding>();
        foreach (var path in transcripts.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var from = state.GetValueOrDefault(path);
                var (found, to) = ScanFile(path, from);
                state[path] = to;
                fresh.AddRange(found.Where(f => known.Add((f.Fingerprint, f.SessionId))));
            }
            catch (IOException ex)
            {
                JsonStore.LogError("security-scan", ex);
            }
        }
        Save(all, fresh, state);
        return fresh;
    }

    /// <summary>Analiza lo nuevo del historial de prompts y de los textos pegados, y devuelve los hallazgos nuevos.</summary>
    /// <remarks>
    /// Recoge lo que se envió aunque su transcript ya no exista: el historial no lo borra ningún plazo.
    /// Se lee como un transcript, desde el byte al que se llegó. Cada texto pegado se lee una sola vez,
    /// porque no cambia, y toma la sesión del prompt que lo cita; uno que no cita ningún prompt no se
    /// envió, y se deja.
    /// </remarks>
    public List<SecretFinding> ScanHistory(string historyFile, string pasteCacheDir)
    {
        using var _ = JsonStore.Lock("security-scan");
        var state = ReadState();
        var all = LoadFindings();
        var known = all.Select(f => (f.Fingerprint, f.SessionId)).ToHashSet();
        var found = new List<SecretFinding>();
        try
        {
            if (System.IO.File.Exists(historyFile))
            {
                var to = state.GetValueOrDefault(historyFile);
                foreach (var root in JsonLines.Complete(historyFile, to, end => to = end)) ScanPrompt(root, historyFile, found);
                state[historyFile] = to;
            }
            ScanPastes(historyFile, pasteCacheDir, state, found);
        }
        catch (IOException ex)
        {
            JsonStore.LogError("security-scan", ex);
        }
        var fresh = found.Where(f => known.Add((f.Fingerprint, f.SessionId))).ToList();
        Save(all, fresh, state);
        return fresh;
    }

    /// <summary>Busca claves escritas tal cual en unos ficheros de configuración y guarda la foto, en lugar de la anterior.</summary>
    /// <remarks>
    /// No se acumula como lo enviado: una clave que se quita del fichero deja de salir en el siguiente
    /// análisis. Solo se miran los bloques donde se ponen claves (env y los servidores MCP), no el
    /// fichero entero, que en ~/.claude.json está lleno de cachés. Una referencia a una variable,
    /// como ${GITHUB_TOKEN}, no es una clave escrita.
    /// </remarks>
    /// <returns>Las claves escritas que hay ahora.</returns>
    public List<SecretFinding> ScanConfig(IEnumerable<ConfigFile> files)
    {
        var found = new List<SecretFinding>();
        foreach (var file in files)
        {
            try
            {
                if (System.IO.File.Exists(file.Path)) ScanConfigFile(file, found);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                JsonStore.LogError("security-config", ex);
            }
        }
        var written = found.DistinctBy(f => (f.Fingerprint, f.File, f.Source)).ToList();
        using var _ = JsonStore.Lock("security-scan");
        WriteProtected(WrittenFile, written);
        return written;
    }

    /// <summary>Claves escritas de la última foto de la configuración.</summary>
    private List<SecretFinding> LoadWritten() => ReadProtected<List<SecretFinding>>(WrittenFile, out _) ?? [];

    /// <summary>Busca claves escritas en los bloques que tocan de un fichero de configuración.</summary>
    private void ScanConfigFile(ConfigFile file, List<SecretFinding> found)
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(file.Path), ConfigJson);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return;
        var when = System.IO.File.GetLastWriteTimeUtc(file.Path);

        Origin OriginOf(string? projectDir)
        {
            var (project, repo) = Place(projectDir);
            return new Origin(when, "", project, repo, file.Path, Written: true);
        }

        switch (file.Kind)
        {
            case ConfigKind.Settings:
                if (root.TryGetProperty("env", out var env) && env.ValueKind == JsonValueKind.Object)
                    foreach (var variable in env.EnumerateObject().Where(v => v.Value.ValueKind == JsonValueKind.String))
                        Add(found, $"{variable.Name}={variable.Value.GetString()}", OriginOf(file.ProjectDir), $"Variable {variable.Name} en env", file: file.Path);
                break;
            case ConfigKind.Mcp:
                ScanServers(root, OriginOf(file.ProjectDir), file.Path, found);
                break;
            case ConfigKind.ClaudeState:
                ScanServers(root, OriginOf(null), file.Path, found);
                if (root.TryGetProperty("projects", out var projects) && projects.ValueKind == JsonValueKind.Object)
                    foreach (var project in projects.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Object))
                        ScanServers(project.Value, OriginOf(project.Name), file.Path, found);
                break;
        }
    }

    /// <summary>Busca claves escritas en la definición de cada servidor MCP de un bloque mcpServers.</summary>
    /// <remarks>Se analiza el JSON de cada servidor tal cual: así las reglas ven el nombre de cada valor, como «"API_KEY": "…"» o «"Authorization": "Bearer …"».</remarks>
    private void ScanServers(JsonElement owner, Origin origin, string path, List<SecretFinding> found)
    {
        if (!owner.TryGetProperty("mcpServers", out var servers) || servers.ValueKind != JsonValueKind.Object) return;
        foreach (var server in servers.EnumerateObject())
            Add(found, server.Value.GetRawText(), origin, $"Servidor MCP «{server.Name}»", file: path);
    }

    /// <summary>Si un hallazgo salió del historial de prompts o de un texto pegado, que se pueden volver a leer.</summary>
    public static bool IsPrompt(SecretFinding f) => !f.Written && (IsHistory(f.Transcript) || IsPaste(f.Transcript));

    /// <summary>El prompt o el texto pegado donde salió un hallazgo, con cada secreto ya sustituido por su máscara.</summary>
    /// <remarks>Se lee en el momento: la app no guarda el texto. El prompt se busca por su sesión y su hora exacta.</remarks>
    /// <returns>El texto tapado; null si Claude Code ya no lo tiene o el hallazgo no viene de ahí.</returns>
    public static string? PromptText(SecretFinding f)
    {
        if (IsPaste(f.Transcript))
            return System.IO.File.Exists(f.Transcript) ? SecretDetector.Redact(System.IO.File.ReadAllText(f.Transcript)) : null;
        if (!IsHistory(f.Transcript) || !System.IO.File.Exists(f.Transcript)) return null;
        var ms = new DateTimeOffset(DateTime.SpecifyKind(f.WhenUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        foreach (var root in JsonLines.Complete(f.Transcript, 0))
        {
            if (Str(root, "sessionId") != f.SessionId || !root.TryGetProperty("timestamp", out var ts) || !ts.TryGetInt64(out var at) || at != ms)
                continue;
            var parts = new List<string?> { Str(root, "display") };
            if (root.TryGetProperty("pastedContents", out var pasted) && pasted.ValueKind == JsonValueKind.Object)
                foreach (var paste in pasted.EnumerateObject())
                    if (Str(paste.Value, "content") is { } content) parts.Add($"[Texto pegado #{paste.Name}]\n{content}");
            return SecretDetector.Redact(string.Join("\n\n", parts.Where(p => !string.IsNullOrEmpty(p))));
        }
        return null;
    }

    /// <summary>Si una ruta es la del historial de prompts de Claude Code.</summary>
    private static bool IsHistory(string path) => Path.GetFileName(path).Equals("history.jsonl", StringComparison.OrdinalIgnoreCase);

    /// <summary>Si una ruta es la de un texto pegado de paste-cache.</summary>
    private static bool IsPaste(string path) =>
        Path.GetFileName(Path.GetDirectoryName(path) ?? "").Equals("paste-cache", StringComparison.OrdinalIgnoreCase);

    /// <summary>Hasta qué byte se ha leído cada fichero analizado.</summary>
    private Dictionary<string, long> ReadState() =>
        JsonStore.Read<Dictionary<string, long>>(StateFile) ?? new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Guarda los hallazgos nuevos junto a los que había, y después hasta dónde se ha leído.</summary>
    private void Save(List<SecretFinding> all, List<SecretFinding> fresh, Dictionary<string, long> state)
    {
        // Hallazgos antes que estado: si algo falla entre los dos, se vuelve a leer en lugar de perderse.
        if (fresh.Count > 0) WriteProtected(FindingsFile, all.Concat(fresh).ToList());
        JsonStore.Write(StateFile, state);
    }

    /// <summary>Hallazgos guardados, incluidos los ignorados.</summary>
    /// <remarks>
    /// Si el fichero existe pero no se puede descifrar (otro usuario de Windows, o dañado), se olvida
    /// también el estado del análisis: el siguiente análisis recupera lo que siga en los transcripts
    /// en lugar de darlo por visto.
    /// </remarks>
    private List<SecretFinding> LoadFindings()
    {
        var list = ReadProtected<List<SecretFinding>>(FindingsFile, out var unreadable);
        if (unreadable) System.IO.File.Delete(StateFile);
        return list ?? [];
    }

    /// <summary>Secretos marcados como falso positivo.</summary>
    private HashSet<string> Ignored() => [.. ReadProtected<List<string>>(IgnoredFile, out _) ?? []];

    /// <summary>Cifra los ficheros en claro de la primera versión y los borra.</summary>
    private void MigratePlain()
    {
        if (!System.IO.File.Exists(PlainFindingsFile) && !System.IO.File.Exists(PlainIgnoredFile)) return;
        using var scan = JsonStore.Lock("security-scan");
        using var ignoredLock = JsonStore.Lock("security-ignored");
        if (System.IO.File.Exists(PlainFindingsFile))
        {
            var current = LoadFindings();
            var known = current.Select(f => (f.Fingerprint, f.SessionId)).ToHashSet();
            var plain = JsonStore.ReadLines<SecretFinding>(PlainFindingsFile).Where(f => known.Add((f.Fingerprint, f.SessionId)));
            WriteProtected(FindingsFile, current.Concat(plain).ToList());
            System.IO.File.Delete(PlainFindingsFile);
        }
        if (System.IO.File.Exists(PlainIgnoredFile))
        {
            var ignored = Ignored();
            ignored.UnionWith(JsonStore.Read<List<string>>(PlainIgnoredFile) ?? []);
            WriteProtected(IgnoredFile, ignored.Order().ToList());
            System.IO.File.Delete(PlainIgnoredFile);
        }
    }

    /// <summary>Guarda un valor como JSON cifrado para este usuario.</summary>
    private static void WriteProtected<T>(string path, T value) =>
        JsonStore.WriteBytes(path, UserCipher.Protect(JsonSerializer.SerializeToUtf8Bytes(value, JsonStore.Options)));

    /// <summary>Lee un JSON cifrado para el usuario; null si no existe o no se puede leer.</summary>
    /// <param name="unreadable">True si el fichero está pero no se puede descifrar ni entender; false si falta o está ocupado.</param>
    private static T? ReadProtected<T>(string path, out bool unreadable) where T : class
    {
        unreadable = false;
        if (!System.IO.File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(UserCipher.Unprotect(System.IO.File.ReadAllBytes(path)), JsonStore.Options);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException)
        {
            JsonStore.LogError("security-store", ex);
            unreadable = ex is not IOException;
            return null;
        }
    }

    /// <summary>Analiza un transcript desde un byte y devuelve lo encontrado y hasta dónde se ha leído.</summary>
    private (List<SecretFinding> Found, long To) ScanFile(string path, long from)
    {
        var found = new List<SecretFinding>();
        var to = from;
        var subagent = path.Contains(Path.DirectorySeparatorChar + "subagents" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        var tools = new Dictionary<string, (string Name, string? File)>();
        foreach (var root in JsonLines.Complete(path, from, end => to = end)) ScanEntry(root, path, subagent, tools, found);
        return (found, to);
    }

    /// <summary>De dónde viene un texto analizado: cuándo, de qué sesión y proyecto, y en qué fichero se leyó.</summary>
    private sealed record Origin(DateTime WhenUtc, string Session, string? Project, string? Repo, string Transcript, bool Subagent = false,
        bool Written = false);

    /// <summary>Apunta los secretos de un texto como hallazgos de un origen, con dónde aparece cada uno y su contexto tapado.</summary>
    private void Add(List<SecretFinding> found, string? text, Origin origin, string source, string? tool = null, string? file = null, string? toolUseId = null)
    {
        foreach (var m in SecretDetector.Scan(text))
        {
            var position = text!.IndexOf(m.Value, StringComparison.Ordinal);
            found.Add(new SecretFinding
            {
                WhenUtc = origin.WhenUtc, SessionId = origin.Session, Project = origin.Project, Repo = origin.Repo,
                Transcript = origin.Transcript, Kind = m.Kind, Masked = SecretDetector.Mask(m.Value), Fingerprint = Fingerprint(m.Value),
                Source = source, Tool = tool, File = file, Subagent = origin.Subagent, Written = origin.Written,
                ToolUseId = toolUseId, Position = position < 0 ? null : position, TextLength = text.Length, Located = true,
            });
        }
    }

    // Largo máximo de la llamada que se enseña junto a una salida.
    private const int MaxCall = 400;

    [System.Text.RegularExpressions.GeneratedRegex(@"\s*\r?\n\s*")]
    private static partial System.Text.RegularExpressions.Regex LineBreaks();

    /// <summary>El texto de alrededor de una aparición y la llamada que lo produjo, tapados; null si su fichero ya no está o
    /// el valor ya no aparece.</summary>
    /// <remarks>Se lee en el momento: la app no guarda ningún texto de la conversación, solo dónde salió.</remarks>
    public SecretSpot? Spot(SecretFinding f)
    {
        if (Find(f) is not { } hit) return null;
        // Los valores de la entrada van uno por línea (el comando, su descripción): se enseñan en una, separados.
        var call = hit.Call is null ? null : SessionTraceReader.Clip(LineBreaks().Replace(SecretDetector.Redact(hit.Call.Trim())!, " · "), MaxCall);
        return new SecretSpot(SecretDetector.Around(hit.Text, hit.Value) ?? "", call);
    }

    /// <summary>El valor real de una aparición, para enseñarlo solo mientras se pide; null si ya no se encuentra.</summary>
    /// <remarks>Se lee del mismo fichero donde se halló, y quien lo pide no debe guardarlo, copiarlo ni escribirlo en ningún sitio.</remarks>
    public string? Reveal(SecretFinding f) => Find(f)?.Value;

    /// <summary>Busca una aparición en su fichero por su huella: el texto donde está, el valor y, si salió de una herramienta, su llamada.</summary>
    /// <remarks>
    /// En un transcript mira los mismos textos que el análisis, y si sabe la llamada, solo los de esa llamada. En el
    /// historial de prompts, cada línea entera. En un texto pegado o un fichero de configuración, el fichero entero, como
    /// texto o, si es JSON, sus valores.
    /// </remarks>
    private (string Text, string Value, string? Call)? Find(SecretFinding f)
    {
        var path = f.Transcript;
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return null;
        // La máscara da los primeros caracteres del valor: un texto que no los lleva no hace falta pasarlo por el detector.
        var head = f.Masked.Length >= 4 && !f.Masked.StartsWith('•') ? f.Masked[..4] : null;
        (string, string, string?)? Match(string? text, string? call)
        {
            if (string.IsNullOrEmpty(text) || (head is not null && !text.Contains(head, StringComparison.Ordinal))) return null;
            foreach (var m in SecretDetector.Scan(text))
                if (Fingerprint(m.Value) == f.Fingerprint) return (text, m.Value, call);
            return null;
        }
        try
        {
            if (!path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
            {
                var whole = System.IO.File.ReadAllText(path);
                try
                {
                    using var doc = JsonDocument.Parse(whole);
                    if (Match(Strings(doc.RootElement), null) is { } inJson) return inJson;
                }
                catch (JsonException)
                {
                    // No es JSON: se mira como texto.
                }
                return Match(whole, null);
            }
            var calls = new Dictionary<string, string>();
            foreach (var root in JsonLines.Complete(path, 0))
            {
                var type = Str(root, "type");
                if (type is not ("user" or "assistant") || !root.TryGetProperty("message", out var message))
                {
                    if (Match(Strings(root), null) is { } line) return line;
                    continue;
                }
                if (!message.TryGetProperty("content", out var content)) continue;
                if (content.ValueKind == JsonValueKind.String)
                {
                    if (f.ToolUseId is null && Match(content.GetString(), null) is { } said) return said;
                    continue;
                }
                if (content.ValueKind != JsonValueKind.Array) continue;
                foreach (var part in content.EnumerateArray())
                {
                    switch (Str(part, "type"))
                    {
                        case "text" when f.ToolUseId is null:
                            if (Match(Str(part, "text"), null) is { } text) return text;
                            break;
                        case "tool_use":
                            var id = Str(part, "id");
                            var input = part.TryGetProperty("input", out var i) ? Strings(i) : "";
                            if (id is not null) calls[id] = $"{Str(part, "name") ?? "herramienta"}: {input}";
                            if ((f.ToolUseId is null || f.ToolUseId == id) && Match(input, null) is { } given) return given;
                            break;
                        case "tool_result":
                            var useId = Str(part, "tool_use_id");
                            if (f.ToolUseId is not null && f.ToolUseId != useId) break;
                            var result = part.TryGetProperty("content", out var c) ? c : default;
                            var output = result.ValueKind == JsonValueKind.String ? result.GetString() : Strings(result);
                            if (Match(output, useId is not null && calls.TryGetValue(useId, out var call) ? call : null) is { } got) return got;
                            break;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            JsonStore.LogError("security-find", ex);
        }
        return null;
    }

    /// <summary>Busca en su transcript dónde apareció cada hallazgo de antes de guardarlo y lo apunta en el almacén.</summary>
    /// <param name="which">Qué hallazgos completar; todos los que falten si es null.</param>
    /// <param name="maxTranscripts">Cuántos transcripts se leen como mucho en esta llamada: cada uno se lee entero.</param>
    /// <returns>Cuántos hallazgos se han completado.</returns>
    /// <remarks>
    /// Un hallazgo se busca una sola vez: si su transcript ya no existe o el valor no aparece, queda marcado igual.
    /// Los prompts del historial, los textos pegados y las claves escritas en un fichero no tienen paso que buscar.
    /// </remarks>
    public int Locate(Func<SecretFinding, bool>? which = null, int maxTranscripts = 20)
    {
        using var _ = JsonStore.Lock("security-scan");
        var all = LoadFindings();
        var missing = all.Where(f => !f.Located && !f.Written && !IsPrompt(f) && (which is null || which(f))).ToList();
        if (missing.Count == 0) return 0;
        var located = 0;
        foreach (var group in missing.GroupBy(f => f.Transcript, StringComparer.OrdinalIgnoreCase).Take(maxTranscripts))
        {
            var seen = new Dictionary<string, SecretFinding>();
            if (System.IO.File.Exists(group.Key))
            {
                try
                {
                    foreach (var f in ScanFile(group.Key, 0).Found) seen.TryAdd(f.Fingerprint, f);
                }
                catch (IOException ex)
                {
                    JsonStore.LogError("security-locate", ex);
                    continue;
                }
            }
            foreach (var f in group)
            {
                f.Located = true;
                if (!seen.TryGetValue(f.Fingerprint, out var at)) continue;
                (f.ToolUseId, f.Position, f.TextLength) = (at.ToolUseId, at.Position, at.TextLength);
                located++;
            }
        }
        WriteProtected(FindingsFile, all);
        return located;
    }

    /// <summary>Proyecto y repositorio de una carpeta de trabajo: el nombre de la carpeta, y el repositorio si es un worktree.</summary>
    private (string? Project, string? Repo) Place(string? cwd)
    {
        if (cwd is null) return (null, null);
        if (!_repos.TryGetValue(cwd, out var repo)) _repos[cwd] = repo = Worktree.RepoOf(cwd);
        return (Path.GetFileName(cwd.TrimEnd('\\', '/')), repo);
    }

    /// <summary>Origen de una línea del historial de prompts: su sesión, su proyecto y su hora.</summary>
    private Origin PromptOrigin(JsonElement root, string historyFile)
    {
        var when = root.TryGetProperty("timestamp", out var ts) && ts.TryGetInt64(out var ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime
            : DateTime.UtcNow;
        var (project, repo) = Place(Str(root, "project"));
        return new Origin(when, Str(root, "sessionId") ?? "", project, repo, historyFile);
    }

    /// <summary>Analiza un prompt del historial: lo escrito y lo pegado que va dentro de la propia línea.</summary>
    /// <remarks>Lo pegado largo no va en la línea, sino en un fichero de paste-cache: lo analiza <see cref="ScanPastes"/>.</remarks>
    private void ScanPrompt(JsonElement root, string historyFile, List<SecretFinding> found)
    {
        var origin = PromptOrigin(root, historyFile);
        Add(found, Str(root, "display"), origin, "Historial de prompts");
        if (!root.TryGetProperty("pastedContents", out var pasted) || pasted.ValueKind != JsonValueKind.Object) return;
        foreach (var paste in pasted.EnumerateObject())
            Add(found, Str(paste.Value, "content"), origin, "Texto pegado");
    }

    /// <summary>Analiza los textos pegados que aún no se han leído, cada uno con la sesión del prompt que lo cita.</summary>
    private void ScanPastes(string historyFile, string dir, Dictionary<string, long> state, List<SecretFinding> found)
    {
        if (!Directory.Exists(dir)) return;
        var pending = Directory.EnumerateFiles(dir).Select(p => new FileInfo(p)).Where(f => state.GetValueOrDefault(f.FullName, -1) != f.Length).ToList();
        if (pending.Count == 0) return;
        var cites = PasteCites(historyFile);
        foreach (var file in pending)
        {
            if (cites.TryGetValue(Path.GetFileNameWithoutExtension(file.Name), out var origin))
                Add(found, System.IO.File.ReadAllText(file.FullName), origin with { Transcript = file.FullName }, "Texto pegado");
            // Sin prompt que lo cite, se vuelve a mirar en el siguiente análisis, hasta que pasa demasiado tiempo para que se envíe.
            else if (DateTime.UtcNow - file.LastWriteTimeUtc < PasteWait) continue;
            state[file.FullName] = file.Length;
        }
    }

    /// <summary>Qué prompt del historial cita cada texto pegado, por el nombre de su fichero.</summary>
    private Dictionary<string, Origin> PasteCites(string historyFile)
    {
        var cites = new Dictionary<string, Origin>(StringComparer.OrdinalIgnoreCase);
        if (!System.IO.File.Exists(historyFile)) return cites;
        foreach (var root in JsonLines.Complete(historyFile, 0))
        {
            if (!root.TryGetProperty("pastedContents", out var pasted) || pasted.ValueKind != JsonValueKind.Object) continue;
            foreach (var paste in pasted.EnumerateObject())
                if (Str(paste.Value, "contentHash") is { } hash) cites.TryAdd(hash, PromptOrigin(root, historyFile));
        }
        return cites;
    }

    /// <summary>Analiza una entrada del transcript: los textos de su mensaje, cada uno con su origen.</summary>
    private void ScanEntry(JsonElement root, string path, bool subagent, Dictionary<string, (string Name, string? File)> tools, List<SecretFinding> found)
    {
        var type = Str(root, "type");
        if (type is not ("user" or "assistant") || !root.TryGetProperty("message", out var message)) return;
        var when = root.TryGetProperty("timestamp", out var ts) && ts.TryGetDateTime(out var t) ? t.ToUniversalTime() : DateTime.UtcNow;
        // En Cowork, la carpeta de trabajo es una de salida de la propia sesión: no dice nada de en qué se trabajaba.
        var (project, repo) = IsCowork(path) ? ("Cowork", (string?)null) : Place(Str(root, "cwd"));
        var origin = new Origin(when, Str(root, "sessionId") ?? Path.GetFileNameWithoutExtension(path), project, repo, path, subagent);

        void Check(string? text, string source, string? tool = null, string? file = null, string? useId = null) => Add(found, text, origin, source, tool, file, useId);

        if (!message.TryGetProperty("content", out var content)) return;
        if (content.ValueKind == JsonValueKind.String)
        {
            Check(content.GetString(), type == "user" ? "Tu mensaje" : "Respuesta de Claude");
            return;
        }
        if (content.ValueKind != JsonValueKind.Array) return;
        foreach (var part in content.EnumerateArray())
        {
            switch (Str(part, "type"))
            {
                case "text":
                    Check(Str(part, "text"), type == "user" ? "Tu mensaje" : "Respuesta de Claude");
                    break;
                case "tool_use":
                    var name = Str(part, "name") ?? "herramienta";
                    var input = part.TryGetProperty("input", out var i) ? i : default;
                    var file = input.ValueKind == JsonValueKind.Object ? Str(input, "file_path") ?? Str(input, "path") ?? Str(input, "notebook_path") : null;
                    var id = Str(part, "id");
                    if (id is not null) tools[id] = (name, file);
                    Check(Strings(input), "Entrada de " + name, name, file, id);
                    break;
                case "tool_result":
                    var useId = Str(part, "tool_use_id");
                    var (tool, toolFile) = useId is not null && tools.TryGetValue(useId, out var known) ? known : ("herramienta", null);
                    var result = part.TryGetProperty("content", out var c) ? c : default;
                    Check(result.ValueKind == JsonValueKind.String ? result.GetString() : Strings(result), "Salida de " + tool, tool, toolFile, useId);
                    break;
            }
        }
    }

    /// <summary>Todos los textos de un valor JSON, uno por línea, para analizarlos juntos, salvo los datos en base64.</summary>
    /// <remarks>Una imagen o un PDF en base64 son cientos de miles de caracteres al azar, y alguno acaba teniendo la forma
    /// de una clave («AIza» y 35 más) sin que haya nada que ver. Se saltan el «data» de un origen en base64 (los bloques
    /// de imagen y de documento) y el «base64» con que Claude Code guarda lo que leyó un Read de una imagen.</remarks>
    public static string Strings(JsonElement e)
    {
        var sb = new StringBuilder();
        void Walk(JsonElement x)
        {
            switch (x.ValueKind)
            {
                case JsonValueKind.String: sb.AppendLine(x.GetString()); break;
                case JsonValueKind.Object:
                    var encoded = x.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "base64";
                    foreach (var p in x.EnumerateObject())
                        if (!p.NameEquals("base64") && !(encoded && p.NameEquals("data"))) Walk(p.Value);
                    break;
                case JsonValueKind.Array: foreach (var v in x.EnumerateArray()) Walk(v); break;
            }
        }
        if (e.ValueKind != JsonValueKind.Undefined) Walk(e);
        return sb.ToString();
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Huella de un valor: HMAC-SHA256 con la clave del equipo, en hexadecimal corto.</summary>
    public string Fingerprint(string value) => Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(value)))[..24];

    /// <summary>Clave de las huellas: aleatoria, creada la primera vez y guardada cifrada para este usuario.</summary>
    private byte[] LoadKey()
    {
        // Con la ventana y el análisis abriendo el almacén a la vez, la primera vez solo uno debe crear la clave.
        using var _ = JsonStore.Lock("security-key");
        if (System.IO.File.Exists(KeyFile))
        {
            try
            {
                return UserCipher.Unprotect(System.IO.File.ReadAllBytes(KeyFile));
            }
            catch (CryptographicException)
            {
                // Clave de otro usuario o dañada: se crea otra; las huellas viejas dejan de coincidir.
            }
        }
        var key = RandomNumberGenerator.GetBytes(32);
        System.IO.File.WriteAllBytes(KeyFile, UserCipher.Protect(key));
        return key;
    }
}
