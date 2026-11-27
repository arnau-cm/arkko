using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Arkko.Core;

/// <summary>Columna de la app en la que cae una tarea, traducida del estado que le da su proveedor.</summary>
public enum TaskState
{
    /// <summary>Un estado que el perfil no sabe traducir.</summary>
    Unknown,

    /// <summary>Pendiente de planificar: va al panel de backlog, no al tablero.</summary>
    Backlog,
    Todo,
    Doing,
    Blocked,
    Review,
    Done,

    /// <summary>Propuesta para descartar, a la espera de que una persona lo confirme en el panel.</summary>
    ToDiscard,
    Discarded,
}

/// <summary>Un valor que pide un perfil al configurar una conexión: la dirección, el usuario, la clave…</summary>
/// <param name="Secret">Se guarda cifrado y no se enseña nunca entero.</param>
/// <param name="Env">Variable de entorno que se sugiere si la persona prefiere leer la clave de ahí.</param>
public sealed record ProfileSetting(string Key, string Label, string? Default = null, bool Secret = false, string? Hint = null, string? Env = null);

/// <summary>Cómo se autentica un perfil: «bearer» (Token), «basic» (User y Password) o «header» (Header y Value).</summary>
/// <remarks>Cada campo es una plantilla: «{apiKey}» toma el valor de la configuración.</remarks>
public sealed record ProfileAuth(string Type, string? Token = null, string? User = null, string? Password = null, string? Header = null,
    string? Value = null);

/// <summary>Cómo se pide la página siguiente: «cursor» (Next y More), «offset» (Size y Total) o «link» (Next, una dirección).</summary>
public sealed record ProfilePaging(string Type, string? Next = null, string? More = null, string? Total = null, int Size = 50);

/// <summary>Segundo paso de una llamada: con los identificadores de la primera, pedir los elementos por lotes, como en Azure DevOps.</summary>
/// <param name="Ids">Ruta, en cada elemento del primer paso, de su identificador; el lote va en «{ids}», separado por comas.</param>
public sealed record ProfileThen(string Ids, string Url, string Items, string Method = "GET", JsonNode? Body = null, int Batch = 100);

/// <summary>Una llamada de un perfil: método, dirección, cuerpo y dónde están los elementos en la respuesta.</summary>
public sealed record ProfileRequest(string Url, string Items, string Method = "GET", JsonNode? Body = null, ProfilePaging? Paging = null,
    ProfileThen? Then = null);

/// <summary>Cómo hablar con un proveedor de tareas, escrito como datos: el motor (BoardClient) lo ejecuta sin código propio de cada uno.</summary>
/// <remarks>
/// La app no trae ninguno: la persona los importa pegándolos en Configuración → Tableros, y se guardan en
/// data\board-profiles. Las llamadas que entiende son «projects», «boards» (de un proyecto), «tasks» (de un proyecto o un
/// subtablero) y «task» (una por su clave); un perfil sin alguna simplemente no la ofrece. «fields» dice,
/// para «project», «board» y «task», en qué ruta de cada elemento está cada dato, y «statuses» traduce los
/// estados del proveedor a las columnas de la app.
/// </remarks>
public sealed class BoardProfile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public List<ProfileSetting> Settings { get; init; } = [];
    public ProfileAuth? Auth { get; init; }
    public Dictionary<string, ProfileRequest> Requests { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Dictionary<string, string>> Fields { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, TaskState> Statuses { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Lee un perfil y comprueba que se puede usar; si no, dice qué le falta.</summary>
    public static BoardProfile Parse(string json)
    {
        BoardProfile? profile;
        try
        {
            profile = JsonSerializer.Deserialize<BoardProfile>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new BoardException($"El perfil no es un JSON válido: {ex.Message}", ex);
        }
        if (profile is null) throw new BoardException("El perfil está vacío.");
        // Al leerlo, los diccionarios se crean sin su comparador: se rehacen para que no distingan mayúsculas.
        profile = new BoardProfile
        {
            Id = profile.Id,
            Name = profile.Name,
            Settings = profile.Settings,
            Auth = profile.Auth,
            Requests = new(profile.Requests, StringComparer.OrdinalIgnoreCase),
            Fields = profile.Fields.ToDictionary(f => f.Key, f => new Dictionary<string, string>(f.Value, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase),
            Statuses = new(profile.Statuses, StringComparer.OrdinalIgnoreCase),
        };
        profile.Validate();
        return profile;
    }

    /// <summary>Lo que hace el perfil, en una frase para la persona que lo importa: qué pide, cómo se identifica, qué lee y qué traduce.</summary>
    public string Summary()
    {
        var parts = new List<string>();
        if (Settings.Count > 0) parts.Add("Pide: " + Join(Settings.Select(s => s.Label.ToLowerInvariant())));
        parts.Add(Auth?.Type switch
        {
            "bearer" => "se identifica con una clave Bearer",
            "basic" => "se identifica con usuario y contraseña o token",
            "header" => $"se identifica con la cabecera {Auth.Header}",
            _ => "sin identificación",
        });
        var reads = new[] { ("projects", "proyectos"), ("boards", "subtableros"), ("tasks", "tareas"), ("task", "una tarea por su clave"),
                ("me", "quién eres") }
            .Where(r => Requests.ContainsKey(r.Item1)).Select(r => r.Item2).ToList();
        if (reads.Count > 0) parts.Add("lee " + Join(reads));
        if (Fields.TryGetValue("task", out var task) && task.Count > 0)
            parts.Add("de cada tarea sabe " + Join(task.Keys.Select(k => FieldNames.GetValueOrDefault(k, k))));
        parts.Add(Statuses.Count == 1 ? "traduce 1 estado" : $"traduce {Statuses.Count} estados");
        return string.Join(" · ", parts);
    }

    // Nombre de cada dato de una tarea, para el resumen.
    private static readonly Dictionary<string, string> FieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["key"] = "clave", ["id"] = "id", ["title"] = "título", ["description"] = "descripción", ["status"] = "estado",
        ["done"] = "si está cerrada", ["type"] = "tipo", ["priority"] = "prioridad", ["labels"] = "etiquetas", ["assignee"] = "asignado", ["assigneeId"] = "id del asignado",
        ["boardId"] = "subtablero",
        ["estimatedHours"] = "estimación", ["url"] = "enlace", ["created"] = "creación", ["updated"] = "último cambio", ["due"] = "vencimiento",
    };

    /// <summary>Une una lista en castellano: «a, b y c».</summary>
    private static string Join(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count <= 1 ? string.Join("", list) : string.Join(", ", list.Take(list.Count - 1)) + " y " + list[^1];
    }

    /// <summary>Indica si el perfil sabe rellenar un dato de un tipo de elemento: lo que la interfaz puede enseñar.</summary>
    public bool Has(string kind, string field) => Fields.TryGetValue(kind, out var map) && map.ContainsKey(field);

    /// <summary>Columna de la app para un estado del proveedor; Unknown si el perfil no lo traduce.</summary>
    public TaskState StateOf(string? status) =>
        status is not null && Statuses.TryGetValue(status.Trim(), out var state) ? state : TaskState.Unknown;

    /// <summary>Comprueba lo mínimo para poder usarlo: nombre, llamadas con dirección y elementos, y rutas bien escritas.</summary>
    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name)) throw new BoardException("El perfil necesita «id» y «name».");
        // El id da nombre a su fichero: sin barras, puntos ni espacios no puede salir de su carpeta.
        if (!Regex.IsMatch(Id, "^[a-z0-9][a-z0-9_-]{0,39}$"))
            throw new BoardException($"El «id» «{Id}» solo admite minúsculas, cifras, guiones y guiones bajos, hasta 40.");
        if (Requests.Count == 0) throw new BoardException($"El perfil «{Id}» no tiene ninguna llamada en «requests».");
        foreach (var (name, r) in Requests)
        {
            if (string.IsNullOrWhiteSpace(r.Url) || string.IsNullOrWhiteSpace(r.Items))
                throw new BoardException($"La llamada «{name}» del perfil «{Id}» necesita «url» e «items».");
            Check(r.Items);
            if (r.Paging is { } p)
            {
                if (p.Type is not ("cursor" or "offset" or "link")) throw new BoardException($"Paginación «{p.Type}» desconocida en «{name}».");
                foreach (var path in new[] { p.Next, p.More, p.Total }.OfType<string>()) Check(path);
            }
            if (r.Then is { } t)
            {
                Check(t.Ids);
                Check(t.Items);
            }
        }
        foreach (var path in Fields.Values.SelectMany(f => f.Values).Where(v => v.StartsWith('$'))) Check(path);
        if (Auth is { Type: not ("bearer" or "basic" or "header") }) throw new BoardException($"Autenticación «{Auth.Type}» desconocida.");
    }

    /// <summary>Comprueba que una ruta JSON está bien escrita, con un mensaje que nombra el perfil.</summary>
    private void Check(string path)
    {
        try
        {
            JsonPath.Select(JsonDocument.Parse("{}").RootElement, path).ToList();
        }
        catch (FormatException ex)
        {
            throw new BoardException($"Perfil «{Id}»: {ex.Message}", ex);
        }
    }
}
