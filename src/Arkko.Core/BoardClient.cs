using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Arkko.Core;

/// <summary>Un proyecto de un proveedor de tareas.</summary>
public sealed record BoardProject(string Id, string Key, string Name);

/// <summary>Un subtablero de un proyecto: una vista con su filtro, como WEB dentro de DEV.</summary>
/// <param name="Color">Color que le da el proveedor, en hexadecimal, si el perfil sabe leerlo.</param>
public sealed record BoardRef(string Id, string Name, string? Color = null);

/// <summary>Una tarea, igual venga del proveedor que venga; lo que su perfil no sabe rellenar queda en null.</summary>
public sealed record BoardTask(string Key, string Title)
{
    public string? Id { get; init; }
    public string? Description { get; init; }

    /// <summary>Subtablero en que está, para verlo por subtableros cuando se mira el proyecto entero.</summary>
    public string? BoardId { get; init; }

    /// <summary>Estado tal como lo llama el proveedor («En curso»), y la columna de la app que le toca.</summary>
    public string? Status { get; init; }
    public TaskState State { get; init; }

    public string? Type { get; init; }
    public string? Priority { get; init; }
    public IReadOnlyList<string> Labels { get; init; } = [];
    public string? Assignee { get; init; }

    /// <summary>Identificador del asignado, para reconocer las propias aunque dos personas se llamen igual.</summary>
    public string? AssigneeId { get; init; }
    public double? EstimatedHours { get; init; }
    public string? Url { get; init; }
    public DateTime? CreatedUtc { get; init; }
    public DateTime? UpdatedUtc { get; init; }
    public DateTime? DueDate { get; init; }
}

/// <summary>Motor de los perfiles de tablero: hace las llamadas que describe un perfil y devuelve proyectos, subtableros y tareas.</summary>
/// <remarks>
/// Solo lee, y solo por HTTPS. Los valores de la configuración (dirección, usuario, clave) llegan ya
/// descifrados en <c>values</c> y no se escriben en ningún mensaje de error. Una respuesta se corta tras
/// MaxPages páginas, para que un perfil con la paginación mal descrita no pida sin fin.
/// </remarks>
public sealed class BoardClient(BoardProfile profile, IReadOnlyDictionary<string, string> values, HttpClient http)
{
    // Dirección de la última llamada, para completar los enlaces relativos de lo que devolvió.
    private Uri? _origin;

    private const int MaxPages = 50;

    public BoardProfile Profile => profile;

    /// <summary>Comprueba la conexión pidiendo los proyectos: si la dirección o la clave no valen, lo dice.</summary>
    public async Task TestAsync(CancellationToken ct = default) => await ProjectsAsync(ct);

    /// <summary>Los proyectos que ve la clave configurada.</summary>
    public async Task<List<BoardProject>> ProjectsAsync(CancellationToken ct = default)
    {
        var items = await FetchAsync("projects", new Dictionary<string, string?>(), ct);
        return items.Select(e => new BoardProject(Field(e, "project", "id") ?? "", Field(e, "project", "key") ?? "", Field(e, "project", "name") ?? ""))
            .Where(p => p.Id.Length > 0 || p.Key.Length > 0)
            .ToList();
    }

    /// <summary>Los subtableros de un proyecto; vacío si el perfil no los ofrece.</summary>
    public async Task<List<BoardRef>> BoardsAsync(BoardProject project, CancellationToken ct = default)
    {
        if (!profile.Requests.ContainsKey("boards")) return [];
        var items = await FetchAsync("boards", ProjectVars(project), ct);
        return items.Select(e => new BoardRef(Field(e, "board", "id") ?? "", Field(e, "board", "name") ?? "", Field(e, "board", "color")))
            .Where(b => b.Id.Length > 0)
            .ToList();
    }

    /// <summary>Las tareas de un proyecto, o solo las de uno de sus subtableros.</summary>
    public async Task<List<BoardTask>> TasksAsync(BoardProject project, BoardRef? board = null, CancellationToken ct = default)
    {
        var vars = ProjectVars(project);
        vars["board.id"] = board?.Id;
        vars["board.name"] = board?.Name;
        var items = await FetchAsync("tasks", vars, ct);
        return items.Select(ToTask).OfType<BoardTask>().ToList();
    }

    /// <summary>Quién es la persona de la clave configurada; null si el perfil no ofrece la llamada «me».</summary>
    public async Task<BoardPerson?> MeAsync(CancellationToken ct = default)
    {
        if (!profile.Requests.ContainsKey("me")) return null;
        var items = await FetchAsync("me", new Dictionary<string, string?>(), ct);
        return items.Select(e => new BoardPerson(Field(e, "me", "id"), Field(e, "me", "name")))
            .FirstOrDefault(p => p.Id is not null || p.Name is not null);
    }

    /// <summary>Una tarea por su clave; null si el perfil no ofrece esta llamada o no existe.</summary>
    public async Task<BoardTask?> TaskAsync(string key, CancellationToken ct = default)
    {
        if (!profile.Requests.ContainsKey("task")) return null;
        var items = await FetchAsync("task", new Dictionary<string, string?> { ["key"] = key }, ct);
        return items.Select(ToTask).OfType<BoardTask>().FirstOrDefault();
    }

    /// <summary>Variables de un proyecto para las plantillas.</summary>
    private static Dictionary<string, string?> ProjectVars(BoardProject project) => new()
    {
        ["project.id"] = project.Id,
        ["project.key"] = project.Key,
        ["project.name"] = project.Name,
    };

    /// <summary>Convierte un elemento de la respuesta en tarea; null si no tiene clave.</summary>
    private BoardTask? ToTask(JsonElement e)
    {
        var key = Field(e, "task", "key");
        if (string.IsNullOrWhiteSpace(key)) return null;
        var status = Field(e, "task", "status");
        var state = profile.StateOf(status);
        // Un proveedor que marca la columna como cerrada lo dice mejor que su nombre, que se puede renombrar;
        // pero una descartada también está cerrada, y no por eso está hecha.
        if (Field(e, "task", "done") == "true" && state is not (TaskState.Discarded or TaskState.ToDiscard)) state = TaskState.Done;
        return new BoardTask(key, Field(e, "task", "title") ?? "")
        {
            Id = Field(e, "task", "id"),
            Description = Field(e, "task", "description"),
            BoardId = Field(e, "task", "boardId"),
            Status = status,
            State = state,
            Type = Field(e, "task", "type"),
            Priority = Field(e, "task", "priority"),
            Labels = Fields(e, "task", "labels"),
            Assignee = Field(e, "task", "assignee"),
            AssigneeId = Field(e, "task", "assigneeId"),
            EstimatedHours = double.TryParse(Field(e, "task", "estimatedHours"), NumberStyles.Float, CultureInfo.InvariantCulture, out var h) ? h : null,
            Url = Link(Field(e, "task", "url", ("key", key))),
            CreatedUtc = Date(Field(e, "task", "created")),
            UpdatedUtc = Date(Field(e, "task", "updated")),
            DueDate = Date(Field(e, "task", "due")),
        };
    }

    /// <summary>Un dato de un elemento: por su ruta JSON o, si el perfil da una plantilla, rellenándola como dirección (un enlace hecho con la clave).</summary>
    private string? Field(JsonElement e, string kind, string field, params (string Name, string Value)[] extra)
    {
        if (!profile.Fields.TryGetValue(kind, out var map) || !map.TryGetValue(field, out var spec)) return null;
        if (spec.StartsWith('$')) return JsonPath.String(e, spec);
        return BoardTemplate.FillUrl(spec, name => extra.FirstOrDefault(x => x.Name == name).Value ?? Value(name));
    }

    /// <summary>Un enlace completo: si el proveedor da una ruta relativa («/issues/…»), se completa con la dirección de la llamada que la devolvió.</summary>
    private string? Link(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || Uri.TryCreate(url, UriKind.Absolute, out _)) return url;
        return _origin is not null && Uri.TryCreate(_origin, url, out var full) ? full.AbsoluteUri : url;
    }

    /// <summary>Todos los valores de un dato que es una lista, como las etiquetas.</summary>
    private List<string> Fields(JsonElement e, string kind, string field) =>
        profile.Fields.TryGetValue(kind, out var map) && map.TryGetValue(field, out var spec) && spec.StartsWith('$')
            ? JsonPath.Strings(e, spec)
            : [];

    /// <summary>Fecha de un texto ISO; con hora, en UTC.</summary>
    private static DateTime? Date(string? text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d : null;

    /// <summary>Valor de la configuración por su nombre; null si no está o está vacío.</summary>
    private string? Value(string name) => values.TryGetValue(name, out var v) && !string.IsNullOrEmpty(v) ? v : null;

    /// <summary>Hace una llamada del perfil con todas sus páginas y, si tiene segundo paso, sus lotes; devuelve los elementos.</summary>
    private async Task<List<JsonElement>> FetchAsync(string name, Dictionary<string, string?> vars, CancellationToken ct)
    {
        if (!profile.Requests.TryGetValue(name, out var request)) throw new BoardException($"El perfil «{profile.Id}» no tiene la llamada «{name}».");
        string? Lookup(string key) => vars.TryGetValue(key, out var v) ? v : Value(key);

        var items = new List<JsonElement>();
        var paging = request.Paging;
        string? nextUrl = null;
        var offset = 0;
        for (var page = 0; page < MaxPages; page++)
        {
            vars["offset"] = offset.ToString(CultureInfo.InvariantCulture);
            vars["size"] = (paging?.Size ?? 50).ToString(CultureInfo.InvariantCulture);
            var url = nextUrl ?? BoardTemplate.FillUrl(request.Url, Lookup);
            if (page == 0) _origin = Uri.TryCreate(url, UriKind.Absolute, out var origin) ? origin : null;
            using var doc = await SendAsync(request.Method, url, BoardTemplate.FillBody(request.Body, Lookup)?.ToJsonString(), ct);
            var root = doc.RootElement;
            var found = JsonPath.Select(root, request.Items).Select(x => x.Clone()).ToList();
            items.AddRange(found);

            if (paging is null) break;
            if (paging.Type == "cursor")
            {
                var next = paging.Next is { } n ? JsonPath.String(root, n) : null;
                var more = paging.More is { } m ? JsonPath.String(root, m) : null;
                if (string.IsNullOrEmpty(next) || more == "false") break;
                vars["cursor"] = next;
            }
            else if (paging.Type == "offset")
            {
                offset += found.Count;
                var total = paging.Total is { } t && int.TryParse(JsonPath.String(root, t), out var tt) ? tt : (int?)null;
                if (found.Count < paging.Size || found.Count == 0 || (total is { } all && offset >= all)) break;
            }
            else
            {
                nextUrl = paging.Next is { } n ? JsonPath.String(root, n) : null;
                if (string.IsNullOrEmpty(nextUrl)) break;
                if (!SameHost(url, nextUrl)) throw new BoardException("La página siguiente apunta a otro servidor; no se sigue.");
            }
        }

        if (request.Then is not { } then) return items;
        var ids = items.SelectMany(e => JsonPath.Strings(e, then.Ids)).Distinct().ToList();
        var result = new List<JsonElement>();
        foreach (var batch in ids.Chunk(Math.Max(1, then.Batch)))
        {
            vars["ids"] = string.Join(",", batch);
            var url = BoardTemplate.FillUrl(then.Url, Lookup);
            using var doc = await SendAsync(then.Method, url, BoardTemplate.FillBody(then.Body, Lookup)?.ToJsonString(), ct);
            result.AddRange(JsonPath.Select(doc.RootElement, then.Items).Select(x => x.Clone()));
        }
        return result;
    }

    /// <summary>Hace una petición con la autenticación del perfil y devuelve su JSON; un error se explica sin enseñar valores de la configuración.</summary>
    private async Task<JsonDocument> SendAsync(string method, string url, string? body, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new BoardException("Solo se admiten direcciones https://: revisa la dirección de la conexión.");

        using var message = new HttpRequestMessage(new HttpMethod(method.ToUpperInvariant()), uri);
        if (body is not null) message.Content = new StringContent(body, Encoding.UTF8, "application/json");
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        Authenticate(message);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new BoardException($"No se ha podido conectar con {uri.Host}: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new BoardException($"{uri.Host} no ha contestado a tiempo.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var code = (int)response.StatusCode;
                throw new BoardException(code is 401 or 403
                    ? $"{uri.Host} no acepta la clave ({code}): revisa que sea válida y tenga permiso de lectura."
                    : $"{uri.Host} ha contestado con un error ({code} {response.ReasonPhrase}).");
            }
            var text = await response.Content.ReadAsStringAsync(ct);
            try
            {
                return JsonDocument.Parse(text);
            }
            catch (JsonException ex)
            {
                throw new BoardException($"{uri.Host} no ha contestado con JSON: revisa la dirección de la conexión.", ex);
            }
        }
    }

    /// <summary>Pone la cabecera de autenticación que pide el perfil.</summary>
    private void Authenticate(HttpRequestMessage message)
    {
        if (profile.Auth is not { } auth) return;
        string Fill(string? template) => BoardTemplate.Fill(template ?? "", Value, forUrl: false);
        switch (auth.Type)
        {
            case "bearer":
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Fill(auth.Token));
                break;
            case "basic":
                var pair = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Fill(auth.User)}:{Fill(auth.Password)}"));
                message.Headers.Authorization = new AuthenticationHeaderValue("Basic", pair);
                break;
            case "header":
                message.Headers.TryAddWithoutValidation(Fill(auth.Header), Fill(auth.Value));
                break;
        }
    }

    /// <summary>Indica si dos direcciones van al mismo servidor.</summary>
    private static bool SameHost(string a, string b) =>
        Uri.TryCreate(a, UriKind.Absolute, out var x) && Uri.TryCreate(b, UriKind.Absolute, out var y) &&
        string.Equals(x.Host, y.Host, StringComparison.OrdinalIgnoreCase) && y.Scheme == Uri.UriSchemeHttps;
}
