using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Arkko.Core;

/// <summary>Plazos de conservación de los datos, en días; null es «siempre».</summary>
/// <remarks>Cada plazo se escribe aunque sea null: los ajustes se guardan sin los null, y «siempre» volvería al de serie.</remarks>
public sealed class RetentionSettings
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int? PromptsDays { get; set; } = 365;

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int? PromptTextDays { get; set; } = 30;

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int? LimitsDays { get; set; } = 365;

    /// <summary>Historial de vulnerabilidades: cuándo empezó a fallar o se arregló cada comprobación del stack.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int? VulnerabilitiesDays { get; set; } = 90;
}

/// <summary>Lo que ocupa y desde cuándo hay datos de una categoría.</summary>
public sealed record DataUsage(long Bytes, DateTime? Since);

/// <summary>Inventario de la carpeta de datos, para la ficha de datos y retención.</summary>
public sealed record DataInventory(long TotalBytes, DataUsage Prompts, DateTime? PromptTextSince, DataUsage Limits, DataUsage Vulnerabilities);

/// <summary>Resultado de una limpieza: cuántos ficheros se borraron y en cuántos se quitó el texto de los prompts.</summary>
public sealed record CleanupResult(int FilesDeleted, int TextStripped, long BytesFreed)
{
    public bool Any => FilesDeleted > 0 || TextStripped > 0;
}

/// <summary>Aplica los plazos de conservación y la limpieza de orden a la carpeta de datos de la app.</summary>
/// <remarks>
/// Solo toca ficheros de la carpeta de datos que se le indica, y nunca los del día en curso, que
/// ak puede estar escribiendo. Pasado el plazo del texto, el registro del prompt se conserva:
/// solo se le quita el texto, para no perder las cifras del histórico.
/// </remarks>
public sealed class DataRetention
{
    // Lo que se limpia siempre, al margen de los plazos elegidos.
    private static readonly TimeSpan StatuslineMaxAge = TimeSpan.FromDays(2);
    private static readonly TimeSpan OrphanSessionMaxAge = TimeSpan.FromDays(1);
    private static readonly TimeSpan TempMaxAge = TimeSpan.FromHours(1);
    private const long ErrorLogMax = 512 * 1024;
    private const long ErrorLogKeep = 200 * 1024;
    private const int BackupsKept = 5;

    private readonly string _data;
    private bool _dryRun;

    /// <summary>Trabaja sobre la carpeta de datos indicada; por defecto, la de la app.</summary>
    public DataRetention(string? dataDir = null) => _data = dataDir ?? AppPaths.Data;

    private string PromptsDir => Path.Combine(_data, "prompts");
    private string LimitsDir => Path.Combine(_data, "limits-history");
    private string StatesDir => Path.Combine(_data, "states");
    private string TaskHistoryDir => Path.Combine(_data, "task-history");
    private string DiaryDir => Path.Combine(_data, "diary");
    private string StatuslineDir => Path.Combine(_data, "statusline");
    private string SessionsDir => Path.Combine(_data, "sessions");
    private string BackupsDir => Path.Combine(_data, "backups");
    private string ErrorLog => Path.Combine(_data, "errors.log");
    private string VulnerabilitiesDir => Path.Combine(_data, "vulnerabilities", "history");

    /// <summary>Cuánto ocupa la carpeta y desde cuándo hay histórico de prompts, de su texto, de los límites y de las vulnerabilidades.</summary>
    public DataInventory Inventory()
    {
        var total = Directory.Exists(_data)
            ? new DirectoryInfo(_data).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length)
            : 0;
        DateTime? textSince = null;
        foreach (var (day, file) in DayFiles(PromptsDir))
        {
            if (!JsonStore.ReadLines<PromptRecord>(file).Any(r => !string.IsNullOrEmpty(r.Prompt))) continue;
            textSince = day;
            break;
        }
        return new DataInventory(total, Usage(PromptsDir), textSince, Usage(LimitsDir), Usage(VulnerabilitiesDir));
    }

    /// <summary>Aplica los plazos y la limpieza de orden; devuelve lo que se ha hecho.</summary>
    /// <param name="dryRun">Solo cuenta lo que se borraría, sin tocar nada: para confirmar antes de limpiar.</param>
    public CleanupResult Apply(RetentionSettings settings, DateTime nowLocal, bool dryRun = false)
    {
        _dryRun = dryRun;
        var deleted = 0;
        var stripped = 0;
        long freed = 0;
        var today = nowLocal.Date;

        // Los estados de las sesiones y los cambios de columna de las tareas son historia de la actividad, como los prompts.
        foreach (var (dir, days) in new[]
                 {
                     (PromptsDir, settings.PromptsDays), (StatesDir, settings.PromptsDays),
                     (TaskHistoryDir, settings.PromptsDays), (LimitsDir, settings.LimitsDays),
                     (VulnerabilitiesDir, settings.VulnerabilitiesDays),
                 })
        {
            if (days is not { } keep) continue;
            foreach (var (day, file) in DayFiles(dir))
                if (day < today && day < today.AddDays(-keep + 1)) Delete(file, ref deleted, ref freed);
        }

        if (settings.PromptTextDays is { } textDays)
        {
            foreach (var (day, file) in DayFiles(PromptsDir))
                if (day < today && day < today.AddDays(-textDays + 1) && StripPromptText(file, _ => true, _dryRun, ref freed)) stripped++;
            foreach (var (day, file) in DayFiles(DiaryDir, "*.json"))
                if (day < today && day < today.AddDays(-textDays + 1) && StripDiaryText(file, _ => true, _dryRun)) stripped++;
        }

        var nowUtc = nowLocal.ToUniversalTime();
        if (Directory.Exists(StatuslineDir))
            foreach (var f in new DirectoryInfo(StatuslineDir).EnumerateFiles("*.json"))
                if (nowUtc - f.LastWriteTimeUtc > StatuslineMaxAge) Delete(f.FullName, ref deleted, ref freed);

        // Las sesiones con proceso las retira la ventana al ver que ha terminado; aquí, las que no tienen.
        if (Directory.Exists(SessionsDir))
            foreach (var f in Directory.EnumerateFiles(SessionsDir, "*.json"))
                if (JsonStore.Read<SessionState>(f) is { ClaudePid: null } s && nowUtc - s.UpdatedUtc > OrphanSessionMaxAge)
                    Delete(f, ref deleted, ref freed);

        // Temporales de una escritura atómica que se cortó (nombre.pid.tmp): el fichero bueno ya está al lado.
        // Con una hora de margen, para no tocar una escritura en curso.
        if (Directory.Exists(_data))
            foreach (var f in new DirectoryInfo(_data).EnumerateFiles("*.tmp", SearchOption.AllDirectories))
                if (IsWriteLeftover(f.Name) && nowUtc - f.LastWriteTimeUtc > TempMaxAge) Delete(f.FullName, ref deleted, ref freed);

        if (Directory.Exists(BackupsDir))
            foreach (var f in new DirectoryInfo(BackupsDir).EnumerateFiles().OrderByDescending(f => f.LastWriteTimeUtc).Skip(BackupsKept))
                Delete(f.FullName, ref deleted, ref freed);

        if (!_dryRun) freed += TrimErrorLog();
        return new CleanupResult(deleted, stripped, freed);
    }

    /// <summary>Indica si un nombre es el de un temporal de JsonStore: «nombre.pid.tmp», con el pid en cifras.</summary>
    private static bool IsWriteLeftover(string name)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        var pid = Path.GetExtension(stem);
        return pid.Length > 1 && pid[1..].All(char.IsAsciiDigit);
    }

    /// <summary>Ficheros de un día (yyyy-MM-dd.jsonl) de una carpeta, del más antiguo al más reciente.</summary>
    private static IEnumerable<(DateTime Day, string File)> DayFiles(string dir, string pattern = "*.jsonl")
    {
        if (!Directory.Exists(dir)) yield break;
        foreach (var file in Directory.EnumerateFiles(dir, pattern).Order(StringComparer.Ordinal))
            if (DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var day))
                yield return (day, file);
    }

    /// <summary>Lo que ocupan los ficheros de días de una carpeta y el primer día que hay.</summary>
    private static DataUsage Usage(string dir)
    {
        var files = DayFiles(dir).ToList();
        return new DataUsage(files.Sum(f => new FileInfo(f.File).Length), files.Count == 0 ? null : files[0].Day);
    }

    /// <summary>Olvida lo que la app guarda de una sesión borrada: el texto de sus prompts y su statusline.</summary>
    /// <remarks>
    /// Los registros se conservan sin el texto, como al pasar su plazo, para no cambiar las cifras del
    /// histórico. Toca también el fichero de hoy: la sesión está cerrada y ak ya no escribe nada suyo,
    /// y si otra sesión añade una línea a la vez, se vuelve a leer en lugar de perderla.
    /// </remarks>
    /// <returns>Cuántos ficheros del histórico se han reescrito.</returns>
    public int ForgetSession(string sessionId)
    {
        var changed = 0;
        long freed = 0;
        foreach (var (_, file) in DayFiles(PromptsDir))
            if (StripPromptText(file, node => (string?)node["session_id"] == sessionId, false, ref freed)) changed++;
        foreach (var (_, file) in DayFiles(DiaryDir, "*.json"))
            if (StripDiaryText(file, block => block.SessionId == sessionId, false)) changed++;
        var deleted = 0;
        var statusline = Path.Combine(StatuslineDir, sessionId + ".json");
        if (File.Exists(statusline)) Delete(statusline, ref deleted, ref freed);
        return changed;
    }

    /// <summary>Quita el texto de los prompts de un fichero del histórico que cumplen una condición; false si no tenía.</summary>
    /// <remarks>
    /// Se reescribe entero a un temporal y se sustituye, para no dejarlo a medias. Si el fichero crece
    /// mientras tanto (ak ha añadido un prompt), se repite para no perder esa línea.
    /// </remarks>
    private static bool StripPromptText(string file, Func<JsonObject, bool> match, bool dryRun, ref long freed)
    {
        for (var attempt = 0; ; attempt++)
        {
            if (!StripOnce(file, match, dryRun, ref freed, out var raced)) return false;
            if (!raced || attempt == 4) return true;
        }
    }

    /// <summary>Quita el principio de los prompts de los bloques de un resumen del Diario que cumplen una condición; false si no tenía.</summary>
    /// <remarks>El Diario guarda ese principio para la ficha de cada bloque, y sigue los mismos plazos que el texto de los prompts.</remarks>
    private static bool StripDiaryText(string file, Func<DiaryBlock, bool> match, bool dryRun)
    {
        if (JsonStore.Read<DiaryDay>(file) is not { } day) return false;
        var blocks = day.Blocks.Where(b => b.Turns.Any(t => t.Text is not null) && match(b)).ToList();
        if (blocks.Count == 0) return false;
        if (dryRun) return true;
        foreach (var turn in blocks.SelectMany(b => b.Turns)) turn.Text = null;
        JsonStore.Write(file, day);
        return true;
    }

    /// <summary>Una pasada de <see cref="StripPromptText"/>; <paramref name="raced"/> si el fichero cambió antes de sustituirlo.</summary>
    private static bool StripOnce(string file, Func<JsonObject, bool> match, bool dryRun, ref long freed, out bool raced)
    {
        raced = false;
        var before = new FileInfo(file).Length;
        var lines = File.ReadAllLines(file);
        var changed = false;
        for (var i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]) || JsonNode.Parse(lines[i]) is not JsonObject node || !node.ContainsKey("prompt") || !match(node)) continue;
            node.Remove("prompt");
            lines[i] = node.ToJsonString();
            changed = true;
        }
        if (!changed) return false;
        if (dryRun) return true;
        var temp = file + ".tmp";
        File.WriteAllText(temp, string.Join("\n", lines.Where(l => l.Length > 0)) + "\n", new System.Text.UTF8Encoding(false));
        if (new FileInfo(file).Length != before)
        {
            File.Delete(temp);
            raced = true;
            return true;
        }
        File.Move(temp, file, overwrite: true);
        freed += before - new FileInfo(file).Length;
        return true;
    }

    /// <summary>Recorta el registro de errores a lo último cuando crece demasiado; devuelve lo liberado.</summary>
    private long TrimErrorLog()
    {
        var info = new FileInfo(ErrorLog);
        if (!info.Exists || info.Length <= ErrorLogMax) return 0;
        var text = File.ReadAllText(ErrorLog);
        var tail = text[^(int)Math.Min(text.Length, ErrorLogKeep)..];
        var cut = tail.IndexOf('\n');
        if (cut >= 0) tail = tail[(cut + 1)..];
        File.WriteAllText(ErrorLog, tail);
        return info.Length - new FileInfo(ErrorLog).Length;
    }

    /// <summary>Borra un fichero y lo cuenta; si está en uso, se deja para la próxima limpieza.</summary>
    private void Delete(string file, ref int deleted, ref long freed)
    {
        try
        {
            var length = new FileInfo(file).Length;
            if (!_dryRun) File.Delete(file);
            deleted++;
            freed += length;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
