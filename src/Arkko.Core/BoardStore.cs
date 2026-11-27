using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Arkko.Core;

/// <summary>Un proyecto, o uno de sus subtableros, que la persona ha elegido ver de una conexión.</summary>
/// <param name="BoardId">Subtablero elegido; null es el proyecto entero.</param>
public sealed record BoardSelection(string ProjectId, string ProjectKey, string ProjectName, string? BoardId = null, string? BoardName = null);

/// <summary>Una conexión con un proveedor de tareas: qué perfil usa, sus valores y qué tableros se ven. Nunca lleva las claves.</summary>
public sealed class BoardConnection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Id del perfil con el que se habla (como «mi-panel»).</summary>
    public string Profile { get; set; } = "";

    /// <summary>Nombre y color con que se distingue de otras conexiones, también de otra del mismo proveedor.</summary>
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#61AFEF";

    /// <summary>Valores de los ajustes del perfil que no son claves, como la dirección.</summary>
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Claves que se leen de una variable de entorno: ajuste del perfil y nombre de la variable.</summary>
    /// <remarks>Las que no están aquí se guardan cifradas en la app.</remarks>
    public Dictionary<string, string> SecretEnv { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Huella del certificado del servidor que la persona ha confirmado, para una CA que no está en el equipo.</summary>
    public string? PinnedSha256 { get; set; }

    /// <summary>Certificado raíz propio, en base64, como el de una CA interna, y el nombre del fichero del que salió.</summary>
    public string? RootCertificate { get; set; }
    public string? RootName { get; set; }

    public List<BoardSelection> Boards { get; set; } = [];

    /// <summary>Color elegido para cada subtablero, por su id, en lugar del que da el proveedor o el que pone la app.</summary>
    public Dictionary<string, string> SubBoardColors { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Cuándo funcionó la última prueba; hasta entonces, la conexión no se ofrece en Tareas.</summary>
    public DateTime? VerifiedUtc { get; set; }
}

/// <summary>Cómo se dejó la sección Tareas: subtableros, filtros, historial y la tarea abierta en el detalle.</summary>
/// <remarks>Solo claves y filtros, sin datos de personas: el nombre de la persona de «Mías» no se guarda.</remarks>
public sealed class TasksViewState
{
    /// <summary>Los subtableros elegidos; vacío, todos.</summary>
    public List<string> SubBoards { get; set; } = [];

    /// <summary>El único subtablero que se podía elegir antes; solo se lee, para no perder lo guardado, y ya no se escribe.</summary>
    public string? SubBoard { get; set; }
    public bool Mine { get; set; } = true;
    public bool WithSession { get; set; }
    public List<string> Labels { get; set; } = [];
    public string Query { get; set; } = "";
    public bool History { get; set; }
    public int Page { get; set; }

    /// <summary>Clave de la tarea abierta en el detalle; null si no había ninguna.</summary>
    public string? Selected { get; set; }

    /// <summary>Cómo se ve el subtablero de cada tarea, elegido en Configuración: «Stripe» (franja con su nombre), «Subtle» (borde fino y pastilla) o «Lanes» (carriles).</summary>
    public string Style { get; set; } = "Stripe";

    /// <summary>Carriles plegados, por el id de su subtablero.</summary>
    public List<string> FoldedLanes { get; set; } = [];
}

/// <summary>Toda la configuración de los tableros: las conexiones y cómo se reconoce la tarea de una sesión.</summary>
public sealed class BoardSettings
{
    public const string DefaultTaskPattern = @"[A-Z][A-Z0-9]+-\d+";

    /// <summary>Un prompt que empieza por un comando con barra: «/opsx:apply DEVLAB-87», «/review ABC-12»…, salvo cerrar su worktree.</summary>
    /// <remarks>«/close-worktree DEVLAB-87» retira la carpeta de una tarea ya terminada: no es trabajar en ella.</remarks>
    public const string DefaultTaskSwitch = @"^\s*/(?!(?:[\w.\-]+:)?close-worktree\b)[\w:.\-]+";

    /// <summary>La regla de serie de antes, que también contaba «/close-worktree»; al leerla, pasa a la de ahora.</summary>
    internal const string OldTaskSwitchRule = @"^\s*/[\w:.\-]+";

    public List<BoardConnection> Connections { get; set; } = [];

    /// <summary>Último tablero elegido en Tareas (TaskPlan.BoardId), para volver a él.</summary>
    public string? LastBoard { get; set; }

    /// <summary>Expresión que encuentra la clave de una tarea en el nombre de la rama o del worktree (DEVLAB-99).</summary>
    public string TaskPattern { get; set; } = DefaultTaskPattern;

    /// <summary>Expresión que reconoce, en la primera línea de un prompt, que añade una tarea a la sesión: la clave que lleve.</summary>
    /// <remarks>Vacía, ningún prompt añade tareas.</remarks>
    public string TaskSwitch { get; set; } = DefaultTaskSwitch;

    /// <summary>Columnas de Tareas plegadas o desplegadas a mano, por su estado («Backlog», «Done»…).</summary>
    /// <remarks>Las que no están aquí siguen su valor de serie: plegadas las de apartar (descartadas, por descartar y backlog).</remarks>
    public Dictionary<string, bool> ColumnFolds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Si una columna de Tareas está plegada: a mano, o si no, su valor de serie.</summary>
    public bool IsFolded(TaskState state) => ColumnFolds.TryGetValue(state.ToString(), out var folded)
        ? folded
        : state is TaskState.Backlog or TaskState.ToDiscard or TaskState.Discarded;

    /// <summary>Si los datos del detalle de una tarea (asignado, estimación…) están plegados.</summary>
    public bool FactsFolded { get; set; }

    /// <summary>Cómo se dejó la sección Tareas, para volver a ella igual.</summary>
    public TasksViewState TasksView { get; set; } = new();

    /// <summary>La tarea que añade un prompt: la clave de su primera línea, si esa línea cumple la regla; null si no la cumple.</summary>
    public string? SwitchKeyIn(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt) || string.IsNullOrWhiteSpace(TaskSwitch)) return null;
        var first = prompt.TrimStart().Split('\n', 2)[0];
        try
        {
            return Regex.IsMatch(first, TaskSwitch, RegexOptions.None, TimeSpan.FromMilliseconds(100)) ? TaskKeyIn(first)?.ToUpperInvariant() : null;
        }
        catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
        {
            return null;
        }
    }

    /// <summary>Todas las claves de tarea de un texto, en mayúsculas y sin repetir; vacío si no hay o el patrón no es válido.</summary>
    public List<string> TaskKeysIn(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        try
        {
            return Regex.Matches(text, TaskPattern, RegexOptions.None, TimeSpan.FromMilliseconds(100))
                .Select(m => m.Value.ToUpperInvariant()).Distinct().ToList();
        }
        catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
        {
            return [];
        }
    }

    /// <summary>La clave de tarea que contiene un nombre de rama o carpeta; null si no hay o el patrón no es válido.</summary>
    public string? TaskKeyIn(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        try
        {
            var m = Regex.Match(name, TaskPattern, RegexOptions.None, TimeSpan.FromMilliseconds(100));
            return m.Success ? m.Value : null;
        }
        catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
        {
            return null;
        }
    }
}

/// <summary>Guarda la configuración de los tableros y sus claves, y crea el cliente de cada conexión.</summary>
/// <remarks>
/// La configuración va en data\boards.json, sin ninguna clave. Las claves escritas en la app van aparte,
/// cifradas para el usuario en data\security, la carpeta que Claude Code tiene prohibido leer. Una clave que la
/// persona prefiere en una variable de entorno no se copia: solo se guarda el nombre de la variable.
/// </remarks>
public sealed class BoardStore(string? dataDir = null)
{
    private readonly string _data = dataDir ?? AppPaths.Data;

    private string ConfigFile => Path.Combine(_data, "boards.json");
    private string SecretsFile => Path.Combine(_data, "security", "board-secrets.bin");
    private string ProfilesDir => Path.Combine(_data, "board-profiles");

    // Los perfiles ya leídos y la huella de la carpeta de la que salieron: cada fichero con su fecha y su tamaño.
    private readonly object _profilesLock = new();
    private string _profilesKey = "";
    private List<BoardProfile> _profiles = [];

    /// <summary>Los perfiles importados, por nombre; uno que ya no se puede leer se salta y se anota.</summary>
    /// <remarks>
    /// Se piden cada segundo y por cada sesión: solo se vuelven a leer si cambia algún fichero de la carpeta,
    /// lo que se sabe al recorrerla, sin abrirlos.
    /// </remarks>
    public List<BoardProfile> Profiles()
    {
        if (!Directory.Exists(ProfilesDir)) return [];
        var files = new DirectoryInfo(ProfilesDir).EnumerateFiles("*.json").ToList();
        var key = string.Join("|", files.Select(f => $"{f.Name}:{f.LastWriteTimeUtc.Ticks}:{f.Length}"));
        lock (_profilesLock)
        {
            if (key == _profilesKey) return [.. _profiles];
        }
        var result = new List<BoardProfile>();
        foreach (var file in files)
        {
            try
            {
                result.Add(BoardProfile.Parse(File.ReadAllText(file.FullName)));
            }
            catch (Exception ex) when (ex is BoardException or IOException)
            {
                JsonStore.LogError("board-profile", ex);
            }
        }
        result = result.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        lock (_profilesLock)
        {
            (_profilesKey, _profiles) = (key, result);
        }
        return [.. result];
    }

    /// <summary>Comprueba un perfil pegado y lo guarda con su id; si no vale, lanza BoardException con el motivo.</summary>
    /// <remarks>Se guarda el texto tal cual, con sus comentarios, para que «Copiar» devuelva lo mismo que se pegó.</remarks>
    public BoardProfile SaveProfile(string json)
    {
        var profile = BoardProfile.Parse(json);
        JsonStore.WriteText(ProfileFile(profile.Id), json.Replace("\r\n", "\n").Trim() + "\n");
        return profile;
    }

    /// <summary>Indica si ya hay un perfil importado con ese id.</summary>
    public bool HasProfile(string id) => File.Exists(ProfileFile(id));

    /// <summary>El texto de un perfil importado, para copiarlo y compartirlo; null si no existe.</summary>
    public string? ProfileText(string id) => File.Exists(ProfileFile(id)) ? File.ReadAllText(ProfileFile(id)) : null;

    /// <summary>Borra un perfil importado.</summary>
    public void DeleteProfile(string id)
    {
        if (File.Exists(ProfileFile(id))) File.Delete(ProfileFile(id));
    }

    /// <summary>El perfil de una conexión, entre los importados; null si ya no existe.</summary>
    public BoardProfile? ProfileOf(BoardConnection connection) =>
        Profiles().FirstOrDefault(p => string.Equals(p.Id, connection.Profile, StringComparison.OrdinalIgnoreCase));

    private string ProfileFile(string id) => Path.Combine(ProfilesDir, id + ".json");

    /// <summary>La configuración guardada; vacía si no hay o no se puede leer.</summary>
    public BoardSettings Load()
    {
        var settings = JsonStore.Read<BoardSettings>(ConfigFile) ?? new BoardSettings();
        if (settings.TaskSwitch == BoardSettings.OldTaskSwitchRule) settings.TaskSwitch = BoardSettings.DefaultTaskSwitch;
        return settings;
    }

    /// <summary>Guarda la configuración, sin claves.</summary>
    public void Save(BoardSettings settings) => JsonStore.Write(ConfigFile, settings);

    private string PlanFile => Path.Combine(_data, "task-plan.json");

    /// <summary>La planificación local de las tareas; vacía si no hay.</summary>
    public TaskPlan LoadPlan()
    {
        var plan = JsonStore.Read<TaskPlan>(PlanFile) ?? new TaskPlan();
        // Al leerlos, los conjuntos pierden su comparador: se rehacen para que no distingan mayúsculas.
        var read = new TaskPlan
        {
            Pulled = new HashSet<string>(plan.Pulled, StringComparer.OrdinalIgnoreCase),
            Returned = new HashSet<string>(plan.Returned, StringComparer.OrdinalIgnoreCase),
            Hidden = new HashSet<string>(plan.Hidden, StringComparer.OrdinalIgnoreCase),
            Discarding = new HashSet<string>(plan.Discarding, StringComparer.OrdinalIgnoreCase),
            SessionLinks = new Dictionary<string, SessionLink>(plan.SessionLinks, StringComparer.OrdinalIgnoreCase),
            DoneClearedUtc = new Dictionary<string, DateTime>(plan.DoneClearedUtc, StringComparer.OrdinalIgnoreCase),
        };
        // Lo vinculado con el formato anterior, una tarea por sesión, pasa a añadido.
        foreach (var (session, key) in plan.SessionTasks) read.AddTask(session, key);
        return read;
    }

    /// <summary>Guarda la planificación local.</summary>
    public void SavePlan(TaskPlan plan) => JsonStore.Write(PlanFile, plan);

    /// <summary>Guarda una clave de una conexión, cifrada; vacía, la quita.</summary>
    public void SetSecret(string connectionId, string key, string? value)
    {
        var all = Secrets();
        if (!all.TryGetValue(connectionId, out var mine)) all[connectionId] = mine = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(value)) mine.Remove(key);
        else mine[key] = value;
        if (mine.Count == 0) all.Remove(connectionId);
        WriteSecrets(all);
    }

    /// <summary>Indica si hay una clave guardada en la app para un ajuste de una conexión, sin descifrarla para nada más.</summary>
    public bool HasSecret(string connectionId, string key) =>
        Secrets().TryGetValue(connectionId, out var mine) && mine.ContainsKey(key);

    /// <summary>Olvida todas las claves de una conexión, al eliminarla.</summary>
    public void RemoveSecrets(string connectionId)
    {
        var all = Secrets();
        if (all.Remove(connectionId)) WriteSecrets(all);
    }

    /// <summary>Los valores con que se habla con el proveedor: los ajustes, sus valores por defecto y las claves de la app o de su variable.</summary>
    /// <remarks>La variable de entorno se busca en el proceso y, si no está, en las del usuario: una app abierta antes de crearla no la hereda.</remarks>
    public Dictionary<string, string> Resolve(BoardProfile profile, BoardConnection connection)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var secrets = Secrets().GetValueOrDefault(connection.Id);
        foreach (var setting in profile.Settings)
        {
            string? value;
            if (!setting.Secret) value = connection.Values.GetValueOrDefault(setting.Key) is { Length: > 0 } v ? v : setting.Default;
            else if (connection.SecretEnv.TryGetValue(setting.Key, out var variable)) value = Environment(variable);
            else value = secrets?.GetValueOrDefault(setting.Key);
            if (!string.IsNullOrEmpty(value)) values[setting.Key] = value;
        }
        return values;
    }

    /// <summary>En qué confía la conexión además de las CA del equipo.</summary>
    public static BoardTrust Trust(BoardConnection connection) =>
        new(connection.PinnedSha256, connection.RootCertificate is { Length: > 0 } root ? Convert.FromBase64String(root) : null);

    /// <summary>Cliente de una conexión, con sus valores y su confianza de certificado.</summary>
    public BoardClient Client(BoardProfile profile, BoardConnection connection, HttpClient? http = null) =>
        new(profile, Resolve(profile, connection), http ?? BoardHttp.Create(Trust(connection)));

    /// <summary>Valor de una variable de entorno, del proceso o del usuario.</summary>
    private static string? Environment(string name) =>
        System.Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v
            : OperatingSystem.IsWindows() ? System.Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User) : null;

    /// <summary>Las claves guardadas, descifradas: conexión, ajuste y valor; vacío si no hay o no se pueden leer.</summary>
    private Dictionary<string, Dictionary<string, string>> Secrets()
    {
        if (!File.Exists(SecretsFile)) return new(StringComparer.OrdinalIgnoreCase);
        try
        {
            var stored = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(UserCipher.Unprotect(File.ReadAllBytes(SecretsFile)));
            return new(stored?.ToDictionary(c => c.Key, c => new Dictionary<string, string>(c.Value, StringComparer.OrdinalIgnoreCase))
                ?? new Dictionary<string, Dictionary<string, string>>(), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException)
        {
            JsonStore.LogError("board-secrets", ex);
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>Guarda las claves cifradas para este usuario.</summary>
    private void WriteSecrets(Dictionary<string, Dictionary<string, string>> all) =>
        JsonStore.WriteBytes(SecretsFile, UserCipher.Protect(JsonSerializer.SerializeToUtf8Bytes(all)));
}
