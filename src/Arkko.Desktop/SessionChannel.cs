using System.Text.Json;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Lo que una página de Sesiones hace con la sesión que enseña: la manda, la sigue en directo, le da el detalle
/// de un paso, sus imágenes y los ficheros de una MCP de navegador, y lo de git y la revisión de su carpeta.</summary>
/// <remarks>
/// Lo usan igual la sección Sesiones, que cambia de sesión al elegir otra, y las ventanas aparte, que enseñan siempre la
/// suya. La lectura de la sesión es de <see cref="SessionFeed"/>, compartida por todas las páginas que la enseñan; lo
/// que se manda a cada página (y desde dónde, en directo) es de cada una. Todo se llama desde el hilo de la ventana.
/// </remarks>
internal sealed class SessionChannel : IDisposable
{
    private readonly Action<object> _post;
    private readonly Func<SecurityMonitor?> _security;
    private int _generation;
    private int _seen;

    // Huella de cada paso tal como se mandó por última vez, para mandar en directo solo lo que ha cambiado.
    private List<string>? _postedMarks;

    // Lo último que se mandó del plano de la obra, para no repetirlo en cada lectura del directo.
    private string? _postedPlan;

    /// <summary>Crea el canal de una página.</summary>
    /// <param name="post">Cómo se le manda un mensaje a la página.</param>
    /// <param name="security">Los secretos de Seguridad, para los escudos de cada paso; null mientras no se tienen.</param>
    public SessionChannel(Action<object> post, Func<SecurityMonitor?> security)
    {
        _post = post;
        _security = security;
        Changes = new ChangesChannel(post, () => Trace is { } trace && PostedId is { } id ? (id, trace.Cwd) : null);
    }

    /// <summary>Lo de git y la revisión de la carpeta de la sesión.</summary>
    public ChangesChannel Changes { get; }

    /// <summary>La lectura de la sesión que enseña la página; null si aún no ha pedido ninguna.</summary>
    public SessionFeed? Feed { get; private set; }

    /// <summary>La sesión que se mandó a la página por última vez, y su id; null si ninguna.</summary>
    public SessionTrace? Trace { get; private set; }

    /// <inheritdoc cref="Trace"/>
    public string? PostedId { get; private set; }

    /// <summary>Si la página no se ve: lo nuevo de su sesión no se le manda al momento, sino cuando vuelve a verse y la
    /// sigue (<see cref="Follow"/>).</summary>
    public bool Paused { get; set; }

    /// <summary>Deja de enseñar la sesión y suelta su lectura y lo de su carpeta, al cerrarse la página.</summary>
    public void Dispose()
    {
        Use(null);
        Changes.Dispose();
    }

    /// <summary>Pasa a enseñar la lectura de otra sesión, o ninguna: suelta la de antes y deja de escuchar sus avisos.</summary>
    private void Use(SessionFeed? feed)
    {
        if (Feed is { } old)
        {
            old.Updated -= OnFeedUpdated;
            old.Release();
        }
        Feed = feed;
        if (feed is not null) feed.Updated += OnFeedUpdated;
    }

    /// <summary>La lectura de la sesión ha cambiado, la haya leído esta página u otra: si la página ya la tiene y se ve,
    /// le manda lo nuevo en el acto, a la vez que las demás.</summary>
    private void OnFeedUpdated()
    {
        if (Paused || Feed is not { } feed || feed.Version == _seen || feed.Trace is not { } trace) return;
        if (!string.Equals(PostedId, feed.Id, StringComparison.OrdinalIgnoreCase)) return;
        _seen = feed.Version;
        PostTrace(feed.Id, trace, patch: true);
    }

    /// <summary>Atiende un mensaje de la página si es de la sesión que enseña: el detalle de un paso, una imagen, un
    /// fichero de una MCP, o algo de git o de la revisión.</summary>
    /// <returns>true si era suyo; false si lo tiene que atender otro.</returns>
    public bool Handle(string? type, JsonElement m)
    {
        if (Changes.Handle(type, m)) return true;
        switch (type)
        {
            case "detail" when m.TryGetProperty("order", out var o) && o.TryGetInt32(out var order):
                SendDetail(order);
                return true;
            case "image" when m.TryGetProperty("order", out var io) && io.TryGetInt32(out var imageOrder)
                              && m.TryGetProperty("index", out var ii) && ii.TryGetInt32(out var index):
                SendImage(imageOrder, index);
                return true;
            case "mcpFile" when m.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String && p.GetString() is { } mcpPath:
                SendMcpFile(mcpPath);
                return true;
            case "fileHistory" when m.TryGetProperty("path", out var hp) && hp.ValueKind == JsonValueKind.String && hp.GetString() is { } historyPath:
                SendFileHistory(historyPath);
                return true;
            case "secretSpot" when m.TryGetProperty("ref", out var sr) && sr.ValueKind == JsonValueKind.String && sr.GetString() is { } secret:
                SendSecretSpot(secret);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Lee la sesión (entera la primera vez y, después, solo lo nuevo) y manda sus pasos, sin el detalle de los
    /// que van plegados.</summary>
    /// <param name="path">Su transcript, si la página ya lo sabe; si no, se busca.</param>
    /// <remarks>Al volver a la sección, la página pide otra vez la sesión que tenía: releerla entera costaba casi un
    /// segundo con una sesión grande, y así solo se lee lo nuevo.</remarks>
    public async void Open(string id, string? path)
    {
        var generation = ++_generation;
        if (!string.Equals(Feed?.Id, id, StringComparison.OrdinalIgnoreCase)) Use(SessionFeed.Acquire(id));
        var feed = Feed!;
        try
        {
            var trace = await feed.ReadAsync(path);
            if (generation != _generation) return;
            if (trace is null)
            {
                _post(new { type = "error", message = "Esta sesión ya no tiene transcript en disco." });
                return;
            }
            _seen = feed.Version;
            PostTrace(id, trace);
        }
        catch (Exception ex)
        {
            JsonStore.LogError("sessions-trace", ex);
            if (generation == _generation) _post(new { type = "error", message = "No se ha podido leer esta sesión." });
        }
    }

    /// <summary>En directo: pide a la sesión que lea lo nuevo y, si ha cambiado algo desde lo que se mandó, lo manda.</summary>
    /// <remarks>Lo que haya cambiado puede haberlo leído otra página que enseña la misma sesión: cuenta igual. Al
    /// cerrarse la sesión llega una última versión, y después ya no se sigue. Si mientras tanto se ha elegido otra
    /// sesión, lo leído se descarta.</remarks>
    public async void Follow()
    {
        if (Feed is not { } feed || (!feed.Live && feed.Version == _seen)) return;
        var generation = _generation;
        try
        {
            await feed.FollowAsync();
            if (generation != _generation || feed != Feed || feed.Version == _seen || feed.Trace is not { } trace) return;
            _seen = feed.Version;
            PostTrace(feed.Id, trace, patch: true);
        }
        catch (Exception ex)
        {
            JsonStore.LogError("sessions-live", ex);
        }
    }

    /// <summary>Vuelve a mandar la sesión que se mandó por última vez, entera: al cambiar sus secretos en Seguridad.</summary>
    public void Repost()
    {
        if (Trace is { } trace && PostedId is { } id) PostTrace(id, trace);
    }

    /// <summary>Olvida lo que se mandó, porque la página se ha descargado: la nueva recibirá la sesión entera.</summary>
    public void Forget()
    {
        PostedId = null;
        _postedMarks = null;
        _postedPlan = null;
    }

    /// <summary>Las tareas de una sesión, para su cabecera: las suyas con la principal delante, las sugeridas y si se pueden añadir.</summary>
    internal static object? TaskOf(SessionSummary? summary)
    {
        if (summary is null) return null;
        var directory = TaskDirectory.Instance;
        var tasks = directory.TasksOfSession(summary.SessionId, summary.Worktree ?? summary.Project);
        return new
        {
            tasks = tasks.Tasks.Select(k => new { key = k, line = directory.LineOf(k) ?? k, principal = k == tasks.Principal }),
            suggestions = directory.SuggestionsOf(tasks).Select(k => new { key = k, line = directory.LineOf(k) ?? k }),
            canAdd = directory.Boards().Count > 0,
        };
    }

    /// <summary>Manda a la página una sesión: sus pasos sin el detalle de los plegados, sus agentes e interacciones, y si va en directo.</summary>
    /// <remarks>
    /// Con <paramref name="patch"/>, si la página ya tiene esta sesión, solo van los pasos desde el primero que ha
    /// cambiado: en directo, una sesión grande eran más de 1 MB de JSON cada segundo y medio para un paso nuevo.
    /// </remarks>
    private void PostTrace(string id, SessionTrace t, bool patch = false)
    {
        var secrets = _security()?.PendingFor(id) ?? [];
        var marks = t.Steps.Select(s => StepMark(s, secrets)).ToList();
        var from = 0;
        if (patch && PostedId == id && _postedMarks is { } before)
            while (from < before.Count && from < marks.Count && before[from] == marks[from]) from++;
        Trace = t;
        PostedId = id;
        _postedMarks = marks;
        var summary = Feed?.Summary;
        // Antes del primer prompt no hay resumen: el sitio lo dan los hooks.
        var opened = summary is null ? Feed?.Opened() : null;
        _post(new
        {
            type = patch && from > 0 ? "tracePatch" : "trace",
            from,
            id,
            title = t.Title ?? summary?.Title,
            project = summary?.Project ?? opened?.Repo ?? opened?.Project,
            worktree = summary?.Worktree ?? (opened?.Repo is null ? null : opened.Project),
            branch = t.Branch,
            task = TaskOf(summary),
            live = Feed?.Live ?? false,
            // También trabaja mientras le quede un subagente en segundo plano en marcha, aunque la principal espere.
            working = Feed is { Live: true } && (Feed.Working || t.Agents.Any(a => a.Busy)),
            context = new { tokens = t.ContextTokens, size = ContextSize(id, t) },
            interactions = t.Interactions,
            agents = t.Agents,
            secrets = secrets.Select(f => f.Fingerprint).Distinct().Count(),
            artifacts = Artifacts(id),
            steps = t.Steps.Skip(from).Select(s =>
            {
                var inline = s.Kind is TraceKind.Prompt or TraceKind.Command or TraceKind.Reply or TraceKind.Notification or TraceKind.Thinking;
                return new
                {
                    order = s.Order, kind = s.Kind, agentId = s.AgentId, interaction = s.Interaction, title = s.Title,
                    text = inline ? s.Text : null,
                    hasDetail = !inline && (s.Text is not null || s.Result is not null),
                    isError = s.IsError ? true : (bool?)null,
                    start = s.StartUtc, end = s.EndUtc, skill = s.Skill,
                    model = s.Model is null ? null : Format.ModelName(s.Model),
                    effort = s.Effort,
                    childAgentId = s.ChildAgentId,
                    queued = s.Queued ? true : (bool?)null,
                    shell = s.Shell ? true : (bool?)null,
                    // La salida de un comando «!», para su terminal en la ficha.
                    output = s.Shell ? s.Result : null,
                    questions = s.Questions,
                    // Las imágenes pesan: solo cuántas hay; la página pide cada una al pintarla.
                    images = s.Images?.Count,
                    secrets = StepSecrets(s, secrets),
                    hookError = s.HookError,
                    branch = s.Branch,
                };
            }),
        });
        // Con la sesión entera (al abrirla o al recargarse la página) el plano va siempre: la página puede no tenerlo.
        if (!(patch && from > 0)) _postedPlan = null;
        PostPlan(id, t);
    }

    /// <summary>Manda a la página el plano de la Obra: el tasks.md del change que sigue la sesión, cuándo se marcó cada tarea
    /// y los commits; solo si ha cambiado desde lo último que se mandó. Sin change, va vacío: la Obra lo dice.</summary>
    private void PostPlan(string id, SessionTrace t)
    {
        ChangePlanInfo? plan = null;
        string? tasksPath = null;
        var cwd = t.Cwd ?? Feed?.Summary?.Worktree;
        try
        {
            plan = ChangePlan.Read(cwd, t.Branch, t);
            tasksPath = ChangePlan.RepoPathOf(cwd, plan?.TasksPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { JsonStore.LogError("obra-plan", ex); }
        var message = new
        {
            type = "obraPlan",
            id,
            change = plan?.Change,
            archived = plan?.Archived ?? false,
            // Ruta del tasks.md dentro del repositorio de la sesión, para abrirlo en su ficha; null si queda fuera.
            tasksPath,
            tasks = plan?.Tasks.Select(k => new { id = k.Id, title = k.Title, text = k.Text, section = k.Section, done = k.Done, human = k.Human, files = k.Files, marked = k.MarkedUtc, added = k.AddedUtc, line = k.Line }),
            commits = plan?.Commits.Select(c => new { at = c.AtUtc, agentId = c.AgentId, hash = c.Hash }),
        };
        var key = JsonSerializer.Serialize(message);
        if (key == _postedPlan) return;
        _postedPlan = key;
        _post(message);
    }

    /// <summary>Manda a la página la historia de un fichero en la sesión: cada vez que lo cambió y, si la revisión ya conoce
    /// sus rondas, en cuál.</summary>
    /// <param name="path">El fichero, relativo a la raíz del repositorio, como lo dan los cambios.</param>
    private void SendFileHistory(string path)
    {
        if (Trace is not { } trace) return;
        var rounds = GitReview.KnownRoundsOf(trace.Cwd).Where(r => r.StartUtc is not null).OrderBy(r => r.StartUtc).ToList();
        var touches = FileHistory.OfRepoFile(trace, path).Select(t => new
        {
            order = t.Order, interaction = t.Interaction, who = t.Who, verb = t.Verb, added = t.Added, removed = t.Removed, at = t.AtUtc, reply = t.Reply,
            round = t.AtUtc is { } at ? rounds.LastOrDefault(r => r.StartUtc <= at)?.Round ?? 0 : 0,
        });
        _post(new { type = "fileHistory", path, rounds = rounds.Count > 0, touches });
    }

    /// <summary>Lo publicado en claude.ai desde la sesión, de lo último a lo primero, para su pastilla en la barra.</summary>
    private static object[] Artifacts(string id) => ArtifactMonitor.Instance.ForSession(id).OrderByDescending(a => a.UpdatedUtc)
        .Select(a => (object)new { url = a.Url, title = a.Title, kind = ArtifactTexts.Kind(a.Kind), when = a.UpdatedUtc, versions = a.Versions }).ToArray();

    /// <summary>Huella de lo que puede cambiar de un paso ya leído: su fin y su resultado, su agente, la respuesta a su
    /// pregunta, sus secretos y un hook que falló en él.</summary>
    private static string StepMark(TraceStep s, IReadOnlyList<SecretFinding> secrets) =>
        $"{s.Kind}|{s.StartUtc?.Ticks}|{s.EndUtc?.Ticks}|{s.Title?.Length}|{s.Text?.Length}|{s.Result is not null}|{s.IsError}|{s.ChildAgentId}|" +
        $"{s.Images?.Count}|{s.Questions?.Sum(q => q.Answer?.Length ?? 0)}|{(secrets.Count == 0 ? 0 : StepSecrets(s, secrets)?.Length ?? 0)}|{s.HookError?.Length}";

    /// <summary>Secretos pendientes de un paso: su tipo, su máscara, su referencia y, si se sabe, dónde están y su contexto tapado;
    /// null si ninguno.</summary>
    /// <remarks>
    /// El texto del paso ya va enmascarado con la misma máscara que guarda el hallazgo: basta con buscarla. Pero el paso solo
    /// guarda el principio de una salida larga, así que un hallazgo de su misma llamada también es suyo aunque su máscara no
    /// esté en lo guardado: entonces va como oculto, para que la página diga dónde está.
    /// </remarks>
    private static object[]? StepSecrets(TraceStep s, IReadOnlyList<SecretFinding> secrets)
    {
        if (secrets.Count == 0) return null;
        var found = secrets
            .Select(f => (f, shown: f.Masked.Length > 0 && new[] { s.Title, s.Text, s.Result }.Any(t => t?.Contains(f.Masked, StringComparison.Ordinal) == true)))
            .Where(x => x.shown || (s.ToolUseId is not null && x.f.ToolUseId == s.ToolUseId))
            .DistinctBy(x => x.f.Fingerprint)
            .Select(x => (object)new
            {
                kind = x.f.Kind, masked = x.f.Masked, id = SecretRef(x.f), hidden = !x.shown,
                at = x.f.Position, of = x.f.TextLength,
            })
            .ToArray();
        return found.Length > 0 ? found : null;
    }

    /// <summary>Manda a la página el texto de alrededor de un secreto pendiente de la sesión y la llamada que lo produjo,
    /// tapados, leídos de su transcript en segundo plano; vacíos si ya no se encuentran.</summary>
    private void SendSecretSpot(string secret)
    {
        if (PostedId is not { } id) return;
        var finding = _security()?.PendingFor(id).FirstOrDefault(f => SecretRef(f) == secret);
        if (finding is null) return;
        Task.Run(() =>
        {
            try { return new SecretScanner().Spot(finding); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                           or System.Security.Cryptography.CryptographicException)
            {
                JsonStore.LogError("secret-spot", ex);
                return null;
            }
        }).ContinueWith(task =>
        {
            var spot = task.IsCompletedSuccessfully ? task.Result : null;
            if (PostedId == id) _post(new { type = "secretSpot", @ref = secret, context = spot?.Context, call = spot?.Call });
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Referencia corta de un secreto para encontrar su paso en la página: el principio de su huella, que no deja recuperarlo.</summary>
    internal static string SecretRef(SecretFinding f) => f.Fingerprint.Length > 8 ? f.Fingerprint[..8] : f.Fingerprint;

    /// <summary>Tamaño de la ventana de contexto de una sesión: el que dio la statusline, abierta o guardada; null si no se conoce.</summary>
    /// <remarks>Sin ese dato, un contexto por encima de 200k solo cabe en la ventana de 1M.</remarks>
    private static long? ContextSize(string id, SessionTrace t)
    {
        var state = SessionFeed.OpenSessions().FirstOrDefault(s => s.SessionId == id)
                    ?? SessionStore.LoadAll().FirstOrDefault(s => s.SessionId == id);
        return state?.ContextSize ?? (t.ContextTokens > 200_000 ? 1_000_000L : null);
    }

    /// <summary>Manda el texto y el resultado completos de un paso, al desplegarlo.</summary>
    private void SendDetail(int order)
    {
        if (Trace?.Steps.ElementAtOrDefault(order) is not { } step) return;
        _post(new { type = "detail", order, text = step.Text, result = step.Result });
    }

    /// <summary>Manda el texto de un fichero que dejó una MCP de navegador en la carpeta de la sesión, o por qué no se lee.</summary>
    private void SendMcpFile(string path)
    {
        var file = McpFiles.Read(Trace?.Cwd, path);
        _post(new { type = "mcpFile", path, text = file.Text, truncated = file.Truncated, error = file.Error });
    }

    /// <summary>Manda una imagen de un paso como dirección «data:», que la página enseña sin cargar nada de fuera.</summary>
    private void SendImage(int order, int index)
    {
        if (Trace?.Steps.ElementAtOrDefault(order)?.Images?.ElementAtOrDefault(index) is not { } image) return;
        _post(new { type = "image", order, index, src = $"data:{image.MediaType};base64,{image.Data}" });
    }
}
