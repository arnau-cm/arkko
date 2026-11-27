using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Arkko.Core;

/// <summary>Qué es algo publicado en claude.ai: una página o un artifact de uno de sus tipos.</summary>
public enum ArtifactKind
{
    Page,
    Document,
    Slides,
    Dashboard,
    Design,
    Animation,
    Other,

    /// <summary>Sin clase conocida: lo que solo se sabe por la galería, mientras la persona no le da una.</summary>
    Unknown,
}

/// <summary>Algo que un agente ha publicado en su web desde una sesión de este equipo: un artifact o un documento de claude.ai.</summary>
public sealed class AgentArtifact
{
    /// <summary>Su enlace: lo que lo identifica.</summary>
    public string Url { get; set; } = "";

    /// <summary>El agente que lo publicó; por ahora solo Claude, «claude».</summary>
    public string Agent { get; set; } = "claude";

    public ArtifactKind Kind { get; set; }

    /// <summary>El título de la página o el que se le dio al crearlo; null si no se sabe.</summary>
    public string? Title { get; set; }

    /// <summary>El HTML de origen de una página, donde Claude lo escribió; null en lo que no sale de un fichero.</summary>
    public string? File { get; set; }

    /// <summary>La sesión que lo publicó por primera vez.</summary>
    public string SessionId { get; set; } = "";

    /// <summary>El nombre de esa sesión cuando se conoció; se guarda porque la app olvida las sesiones viejas.</summary>
    public string? SessionName { get; set; }

    /// <summary>Carpeta de trabajo de esa sesión.</summary>
    public string? Cwd { get; set; }

    /// <summary>Nombre de la carpeta de trabajo.</summary>
    public string? Project { get; set; }

    /// <summary>Repositorio cuando la carpeta era un worktree; null en una carpeta normal.</summary>
    public string? Repo { get; set; }

    /// <summary>Si salió de una sesión de Cowork y no de Claude Code.</summary>
    public bool Cowork { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }

    /// <summary>Cuántas veces se ha publicado: la primera y cada actualización.</summary>
    public int Versions { get; set; }

    /// <summary>Si la persona lo ha quitado de la lista. Sigue en el índice para que no vuelva al releer los transcripts.</summary>
    public bool Removed { get; set; }

    /// <summary>Si una sesión lo borró de claude.ai.</summary>
    public bool Deleted { get; set; }

    /// <summary>Si solo se sabe por la galería del agente: no está en ningún transcript de este equipo, así que no tiene
    /// sesión, carpeta ni clase.</summary>
    public bool FromGallery { get; set; }

    /// <summary>Las llamadas que ya cuentan como versión, para no sumar dos veces la misma.</summary>
    public List<string> Calls { get; set; } = [];

    /// <summary>El repositorio con el que se agrupa: el del worktree o, si no lo es, la carpeta.</summary>
    [JsonIgnore]
    public string Group => Repo ?? Project ?? "Sin carpeta";
}

/// <summary>Índice de lo que Claude ha publicado en claude.ai, leído de los transcripts y guardado en data/artifacts.json.</summary>
/// <remarks>
/// Se guarda en la app porque Claude Code borra los transcripts viejos. Cuenta como publicación una llamada a la
/// herramienta Artifact que publica, con su respuesta, y una del conector de Claude Docs que crea o cambia un
/// documento; los enlaces que solo salen en el texto, y las acciones que no publican (quickstart, read, list), no.
/// Lee solo lo nuevo de cada transcript: la llamada y su respuesta pueden quedar en lecturas distintas, así que las
/// llamadas sin respuesta se guardan hasta que llega.
/// </remarks>
public sealed partial class ArtifactIndex
{
    // Los tipos de artifact de claude.ai, por el identificador de su enlace.
    private static readonly Dictionary<string, ArtifactKind> Types = new(StringComparer.Ordinal)
    {
        ["Rp9naXUCj2xozpUkyQy19W"] = ArtifactKind.Document,
        ["8jTsAFQMFDb2oA8MsPJ2eL"] = ArtifactKind.Slides,
        ["GZLZthZ2sQjn7T16skfdeF"] = ArtifactKind.Dashboard,
        ["QKN21svewxgyPb6SYRqWnd"] = ArtifactKind.Design,
        ["5M7UeXXcx16TP3vzVFNDzd"] = ArtifactKind.Design,
        ["S4skLPbN9WeoFmuiTfaEFy"] = ArtifactKind.Animation,
    };

    // Una llamada sin respuesta después de esto ya no la tendrá: Claude Code la cortó o la sesión se cerró.
    private static readonly TimeSpan PendingFor = TimeSpan.FromDays(1);

    private readonly string _dir;

    // Repositorio de cada carpeta ya vista: cada línea trae su carpeta, y mirar el disco por línea sobra.
    private readonly Dictionary<string, string?> _repos = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Trabaja sobre la carpeta de datos indicada; por defecto, la de la app.</summary>
    public ArtifactIndex(string? dataDir = null) => _dir = dataDir ?? AppPaths.Data;

    /// <summary>El fichero con la lista, para saber si ha cambiado.</summary>
    public string ListFile => Path.Combine(_dir, "artifacts.json");

    private string StateFile => Path.Combine(_dir, "artifacts-scan.json");

    /// <summary>La lista de la galería del agente, que deja una sesión de Claude Code; la app no tiene acceso a la cuenta.</summary>
    public string GalleryFile => Path.Combine(_dir, "artifacts-gallery.json");

    /// <summary>Todo lo publicado, también lo quitado de la lista y lo borrado.</summary>
    public List<AgentArtifact> All() => JsonStore.Read<List<AgentArtifact>>(ListFile) ?? [];

    /// <summary>Lo del índice y, además, lo de la galería que el índice no conoce: lo hecho en el chat, en otro equipo o en
    /// sesiones cuyo transcript ya se borró.</summary>
    /// <remarks>De lo que conocen los dos, el título es el de la galería: es el de ahora, y el del índice es el que se le dio
    /// al publicarlo.</remarks>
    public List<AgentArtifact> AllWithGallery()
    {
        var all = All();
        if (JsonStore.Read<GalleryList>(GalleryFile) is not { } gallery) return all;
        var byUrl = all.ToDictionary(a => a.Url, StringComparer.Ordinal);
        foreach (var entry in gallery.Items.Where(e => e.Url.Length > 0))
        {
            if (byUrl.TryGetValue(entry.Url, out var known))
            {
                if (!string.IsNullOrWhiteSpace(entry.Title)) known.Title = entry.Title;
                continue;
            }
            all.Add(byUrl[entry.Url] = new AgentArtifact
            {
                Url = entry.Url, Agent = entry.Agent, Title = entry.Title, Kind = ArtifactKind.Unknown, FromGallery = true,
                CreatedUtc = entry.UpdatedUtc, UpdatedUtc = entry.UpdatedUtc,
            });
        }
        return all;
    }

    /// <summary>Lee lo nuevo de unos transcripts y lo añade al índice; true si ha cambiado algo.</summary>
    public bool Scan(IEnumerable<string> transcripts)
    {
        using var _ = JsonStore.Lock("artifacts");
        var state = JsonStore.Read<ScanState>(StateFile) ?? new ScanState();
        var byUrl = All().ToDictionary(a => a.Url, StringComparer.Ordinal);
        var changed = false;
        foreach (var path in transcripts.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var from = state.Read.GetValueOrDefault(path);
                var to = from;
                var cowork = SecretScanner.IsCowork(path);
                foreach (var root in JsonLines.Complete(path, from, end => to = end, Mentions))
                    changed |= Read(root, cowork, state.Pending, byUrl);
                state.Read[path] = to;
            }
            catch (IOException ex)
            {
                JsonStore.LogError("artifacts-scan", ex);
            }
        }
        changed |= RememberNames(byUrl.Values);
        var stale = DateTime.UtcNow - PendingFor;
        foreach (var id in state.Pending.Where(p => p.Value.WhenUtc < stale).Select(p => p.Key).ToList()) state.Pending.Remove(id);
        // La lista antes que el estado: si algo falla entre los dos, se vuelve a leer en lugar de perderse.
        if (changed) JsonStore.Write(ListFile, byUrl.Values.OrderBy(a => a.CreatedUtc).ToList());
        JsonStore.Write(StateFile, state);
        return changed;
    }

    /// <summary>Quita algo de la lista o lo devuelve a ella. No toca claude.ai ni los transcripts.</summary>
    public void SetRemoved(AgentArtifact artifact, bool removed) => Change(artifact, a => a.Removed = removed);

    /// <summary>Cambia algo de lo publicado y lo guarda: quitarlo de la lista, o darle repositorio o clase a lo de la galería.</summary>
    /// <remarks>Lo que solo está en la galería entra en el índice al cambiarlo, para recordar el cambio.</remarks>
    public void Change(AgentArtifact artifact, Action<AgentArtifact> change)
    {
        using var _ = JsonStore.Lock("artifacts");
        var all = All();
        if (all.FirstOrDefault(a => a.Url == artifact.Url) is not { } item)
        {
            if (!artifact.FromGallery) return;
            all.Add(item = artifact);
        }
        change(item);
        JsonStore.Write(ListFile, all);
    }

    /// <summary>Apunta el nombre de su sesión a lo que aún no lo tiene, si la app conoce esa sesión; true si ha apuntado alguno.</summary>
    private bool RememberNames(IEnumerable<AgentArtifact> items)
    {
        var any = false;
        foreach (var item in items.Where(a => a.SessionName is null && a.SessionId.Length > 0))
        {
            var file = Path.Combine(_dir, "sessions", Path.GetFileName(AppPaths.SessionFile(item.SessionId)));
            if (JsonStore.Read<SessionState>(file)?.SessionName is not { Length: > 0 } name) continue;
            item.SessionName = name;
            any = true;
        }
        return any;
    }

    /// <summary>Si una línea puede traer una publicación o su respuesta: nombra la herramienta, el conector o un enlace de claude.ai.</summary>
    private static bool Mentions(ReadOnlySpan<byte> line) =>
        line.IndexOf("\"Artifact\""u8) >= 0 || line.IndexOf("Claude_Docs"u8) >= 0 || line.IndexOf("claude.ai/"u8) >= 0;

    /// <summary>Apunta las llamadas de una línea y resuelve las respuestas que trae; true si ha cambiado el índice.</summary>
    private bool Read(JsonElement root, bool cowork, Dictionary<string, PendingCall> pending, Dictionary<string, AgentArtifact> byUrl)
    {
        // Las líneas de petición repiten llamadas que ya están en su línea de siempre.
        if (Str(root, "type") == "api-request-blob") return false;
        if (!root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object ||
            !message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) return false;
        var changed = false;
        foreach (var block in content.EnumerateArray())
        {
            switch (Str(block, "type"))
            {
                case "tool_use":
                    if (Call(block, root, cowork) is { } call) pending[call.Id] = call;
                    break;
                case "tool_result":
                    if (Str(block, "tool_use_id") is { } id && pending.Remove(id, out var asked))
                        changed |= Resolve(asked, block, byUrl);
                    break;
            }
        }
        return changed;
    }

    /// <summary>La llamada de un bloque si puede publicar algo; null si es de otra herramienta o no publica.</summary>
    private static PendingCall? Call(JsonElement block, JsonElement root, bool cowork)
    {
        if (Str(block, "id") is not { } id || Str(block, "name") is not { } name) return null;
        var input = block.TryGetProperty("input", out var i) && i.ValueKind == JsonValueKind.Object ? i : default;
        var call = new PendingCall
        {
            Id = id, Session = Str(root, "sessionId") ?? "", Cwd = Str(root, "cwd"), Cowork = cowork,
            WhenUtc = Str(root, "timestamp") is { } ts && DateTime.TryParse(ts, null, System.Globalization.DateTimeStyles.RoundtripKind, out var when)
                ? when.ToUniversalTime() : DateTime.UtcNow,
        };
        if (name == "Artifact")
        {
            var action = Str(input, "action") ?? "publish";
            if (action == "delete" && Str(input, "path") is null && Str(input, "url") is { } gone) return call with { Action = CallAction.Delete, Url = gone };
            if (action != "publish" || (input.ValueKind == JsonValueKind.Object && input.TryGetProperty("asset", out var asset) && asset.ValueKind == JsonValueKind.True))
                return null;
            return call with
            {
                Action = CallAction.Publish, Url = Str(input, "url"), File = Str(input, "file_path"), Title = Str(input, "title"),
                Kind = Str(input, "type_url") is { } type ? KindOf(type) : null,
            };
        }
        var docs = DocsTool().Match(name);
        if (!docs.Success) return null;
        var create = input.ValueKind == JsonValueKind.Object && input.TryGetProperty("container", out var container) &&
                     container.ValueKind == JsonValueKind.Object && container.TryGetProperty("create", out var made) ? made : default;
        return docs.Groups[1].Value == "create" || create.ValueKind == JsonValueKind.Object
            ? call with { Action = CallAction.Create, Kind = ArtifactKind.Document, Title = Str(create, "name") ?? Str(input, "name") ?? Str(input, "title") }
            : call with { Action = CallAction.Change };
    }

    /// <summary>Apunta en el índice lo que dice la respuesta de una llamada; true si ha cambiado algo.</summary>
    private bool Resolve(PendingCall call, JsonElement result, Dictionary<string, AgentArtifact> byUrl)
    {
        if (result.TryGetProperty("is_error", out var error) && error.ValueKind == JsonValueKind.True) return false;
        var text = Text(result);
        switch (call.Action)
        {
            case CallAction.Delete:
                if (call.Url is null || !byUrl.TryGetValue(call.Url, out var gone) || gone.Deleted) return false;
                gone.Deleted = true;
                return true;
            case CallAction.Publish:
                // Una publicación rechazada (un conflicto, un permiso) también trae enlaces: solo cuenta la que dice que se hizo.
                if (!Done().IsMatch(text)) return false;
                break;
            case CallAction.Change:
                // Un cambio de un documento solo cuenta si es de uno que ya está en el índice.
                if (Link().Match(text) is not { Success: true } changed || !byUrl.ContainsKey(changed.Value)) return false;
                break;
        }
        if (Link().Match(text) is not { Success: true } link) return false;
        if (!byUrl.TryGetValue(link.Value, out var item))
        {
            var (project, repo) = Place(call.Cwd);
            item = new AgentArtifact
            {
                Url = link.Value, Kind = call.Kind ?? ArtifactKind.Page, SessionId = call.Session, Cwd = call.Cwd, Project = project, Repo = repo,
                Cowork = call.Cowork, CreatedUtc = call.WhenUtc, UpdatedUtc = call.WhenUtc,
            };
            byUrl[link.Value] = item;
        }
        if (item.Calls.Contains(call.Id)) return false;
        item.Calls.Add(call.Id);
        item.Versions++;
        if (call.WhenUtc > item.UpdatedUtc) item.UpdatedUtc = call.WhenUtc;
        if (call.WhenUtc < item.CreatedUtc) item.CreatedUtc = call.WhenUtc;
        if (call.Kind is { } kind && item.Kind == ArtifactKind.Page) item.Kind = kind;
        if (call.File is { } file)
        {
            item.File = call.Cwd is { } cwd && !Path.IsPathRooted(file) ? Path.GetFullPath(Path.Combine(cwd, file)) : file;
            item.Title = PageTitle(item.File) ?? call.Title ?? item.Title ?? Path.GetFileNameWithoutExtension(file);
        }
        else if (call.Title is { } title) item.Title = title;
        return true;
    }

    /// <summary>El tipo de un artifact por el enlace de su tipo en claude.ai.</summary>
    private static ArtifactKind KindOf(string typeUrl) =>
        Types.TryGetValue(typeUrl.TrimEnd('/').Split('/')[^1], out var kind) ? kind : ArtifactKind.Other;

    /// <summary>El título de una página, de su etiqueta title; null si el fichero ya no está o no la tiene.</summary>
    /// <remarks>Solo se miran los primeros 8 KB, donde va la cabecera.</remarks>
    private static string? PageTitle(string file)
    {
        try
        {
            if (!System.IO.File.Exists(file)) return null;
            using var reader = new StreamReader(file);
            var head = new char[8192];
            var read = reader.ReadBlock(head, 0, head.Length);
            var match = TitleTag().Match(new string(head, 0, read));
            return match.Success && WebUtility.HtmlDecode(match.Groups[1].Value).Trim() is { Length: > 0 } title ? title : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Proyecto y repositorio de una carpeta de trabajo: el nombre de la carpeta, y el repositorio si es un worktree.</summary>
    private (string? Project, string? Repo) Place(string? cwd)
    {
        if (cwd is null) return (null, null);
        if (!_repos.TryGetValue(cwd, out var repo)) _repos[cwd] = repo = Worktree.RepoOf(cwd);
        return (Path.GetFileName(cwd.TrimEnd('\\', '/')), repo);
    }

    /// <summary>El texto de la respuesta de una herramienta, venga como texto o como bloques.</summary>
    private static string Text(JsonElement result)
    {
        if (!result.TryGetProperty("content", out var content)) return "";
        if (content.ValueKind == JsonValueKind.String) return content.GetString() ?? "";
        if (content.ValueKind != JsonValueKind.Array) return "";
        return string.Join("\n", content.EnumerateArray().Select(b => Str(b, "text")).OfType<string>());
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    [GeneratedRegex(@"https://claude\.ai/(?:code/)?artifact/[A-Za-z0-9-]+")]
    private static partial Regex Link();

    [GeneratedRegex(@"^\s*(?:Published|Updated|Created)\b")]
    private static partial Regex Done();

    [GeneratedRegex(@"^mcp__.*Claude_Docs__(batch|create|update)$")]
    private static partial Regex DocsTool();

    [GeneratedRegex(@"<title[^>]*>([^<]*)</title>", RegexOptions.IgnoreCase)]
    private static partial Regex TitleTag();

    /// <summary>Qué hace una llamada que se espera: publicar, borrar, crear un documento o cambiarlo.</summary>
    public enum CallAction
    {
        Publish,
        Delete,
        Create,
        Change,
    }

    /// <summary>Una llamada que puede publicar algo y aún no tiene respuesta.</summary>
    public sealed record PendingCall
    {
        public string Id { get; init; } = "";
        public CallAction Action { get; init; }
        public string Session { get; init; } = "";
        public string? Cwd { get; init; }
        public bool Cowork { get; init; }
        public DateTime WhenUtc { get; init; }
        public string? Url { get; init; }
        public string? File { get; init; }
        public string? Title { get; init; }
        public ArtifactKind? Kind { get; init; }
    }

    /// <summary>La lista de la galería de un agente tal como la deja una sesión: cuándo se trajo y lo que había.</summary>
    public sealed class GalleryList
    {
        public DateTime FetchedUtc { get; set; }
        public List<GalleryEntry> Items { get; set; } = [];
    }

    /// <summary>Un elemento de la galería: su enlace, su título y su última versión.</summary>
    public sealed class GalleryEntry
    {
        public string Url { get; set; } = "";
        public string Agent { get; set; } = "claude";
        public string? Title { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }

    /// <summary>Hasta dónde se ha leído cada transcript y las llamadas que esperan su respuesta.</summary>
    public sealed class ScanState
    {
        public Dictionary<string, long> Read { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, PendingCall> Pending { get; set; } = new(StringComparer.Ordinal);
    }
}
