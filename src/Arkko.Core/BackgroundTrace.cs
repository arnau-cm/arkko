using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Arkko.Core;

/// <summary>Un agente que la sesión lanzó en segundo plano, tal como se ve al esperarlo: qué hace, desde cuándo y si sigue.</summary>
/// <param name="FinishedUtc">Cuándo avisó de que había acabado, si ya no trabaja; null si sigue o si se le perdió la pista.</param>
public sealed record BackgroundAgent(string Id, string Description, DateTime StartUtc, bool Running, DateTime? FinishedUtc);

/// <summary>Un comando que la sesión dejó en marcha en segundo plano: un build que acabará o un servidor que no.</summary>
/// <param name="Awaited">Si la sesión espera su resultado: Claude Code lo pasó a segundo plano por tardar, o lo dejó así uno
/// de sus agentes. Los que lanza la propia sesión en segundo plano pueden ser un servidor que no acaba: esos no.</param>
/// <param name="By">Qué agente lo lanzó, por su encargo; null si fue la propia sesión.</param>
public sealed record BackgroundCommand(string Id, string Description, DateTime StartUtc, bool Awaited = false, string? By = null);

/// <summary>El trabajo en segundo plano de una sesión en un momento: los agentes que espera, los comandos en marcha y si
/// el turno de la sesión principal ha acabado.</summary>
public sealed class SessionBackground
{
    /// <summary>Los agentes de la espera: los que siguen trabajando y los que acabaron desde que terminó el turno.</summary>
    public IReadOnlyList<BackgroundAgent> Agents { get; init; } = [];

    /// <summary>Los comandos que siguen en marcha, del más antiguo al más nuevo.</summary>
    public IReadOnlyList<BackgroundCommand> Commands { get; init; } = [];

    /// <summary>Si el último turno de la sesión principal ha acabado y no ha empezado otro.</summary>
    public bool MainIdle { get; init; }

    /// <summary>Si ha llegado el aviso de fin de algo suyo tras acabar el turno: la sesión va a retomar sola.</summary>
    public bool ResumePending { get; init; }

    /// <summary>Cuándo acabó el último turno de la sesión principal; null si aún no ha acabado ninguno.</summary>
    public DateTime? TurnEndUtc { get; init; }

    /// <summary>Cuántos agentes siguen trabajando.</summary>
    public int RunningAgents => Agents.Count(a => a.Running);

    /// <summary>Cuántos comandos en marcha espera la sesión.</summary>
    public int AwaitedCommands => Commands.Count(c => c.Awaited);
}

/// <summary>Sigue en el transcript de una sesión lo que lanza en segundo plano: agentes y comandos, sus avisos de fin y los
/// turnos de la sesión principal.</summary>
/// <remarks>
/// Un agente o un comando en segundo plano devuelve su llamada al momento: el turno acaba, llega el Stop y la sesión parece
/// terminada aunque siga trabajando. El transcript dice qué se lanzó (<c>async_launched</c> con el id del agente,
/// <c>backgroundTaskId</c> con el del comando) y cuándo acabó cada cosa (un <c>&lt;task-notification&gt;</c> con ese id). Un
/// agente puede despertar después de avisar: si sigue vivo lo dice el <c>.meta.json</c> de su transcript, que lleva
/// <c>finished</c> solo mientras está parado. Cada lectura sigue donde acabó la anterior.
/// </remarks>
public sealed partial class BackgroundTrace
{
    /// <summary>Lo que un agente puede pasar sin escribir nada antes de darlo por parado, si nadie ha dicho que acabó.</summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromMinutes(10);

    /// <summary>Lo que se lee del final de un transcript la primera vez: lo lanzado en los últimos turnos.</summary>
    /// <remarks>Se lee en el hilo de la ventana, cada segundo, y un transcript entero puede pasar de decenas de MB.</remarks>
    public const long FirstWindow = 2 * 1024 * 1024;

    /// <summary>Lo que se espera a un comando sin saber de él antes de dejar de esperarlo: su aviso de fin se puede perder.</summary>
    public static readonly TimeSpan AwaitLimit = TimeSpan.FromHours(12);

    private sealed class Launch
    {
        public string Id = "";
        public bool Agent;
        public string? Name;
        public string Description = "";
        public DateTime StartUtc;
        public DateTime WokenUtc;
        public DateTime? NoticeUtc;
        public DateTime? StoppedUtc;
        public bool Awaited;
        public string? From;
    }

    // Una llamada que puede dejar algo en segundo plano: su nombre, su descripción, cuándo se hizo y qué agente la hizo
    // (null, la propia sesión).
    private readonly record struct Call(string? Name, string Description, DateTime Utc, string? From);

    private long _offset = -1;
    private readonly Dictionary<string, Call> _calls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Launch> _tasks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _agentOffsets = new(StringComparer.Ordinal);

    // Los avisos de fin de tareas que aún no se conocen, por su id: el de un comando de un agente puede llegar al transcript
    // de la sesión, que se lee antes que el del agente que lo lanzó.
    private readonly Dictionary<string, DateTime> _early = new(StringComparer.Ordinal);
    private DateTime? _turnEnd;
    private DateTime? _activity;

    /// <summary>Lee lo que se ha escrito desde la última vez; indica si había algo nuevo.</summary>
    /// <remarks>Una línea a medio escribir se deja para la próxima lectura. Si el fichero ha encogido, se empieza de nuevo.</remarks>
    public bool Read(string path)
    {
        if (LengthOf(path) is not { } length) return false;
        if (length < _offset) Reset();
        if (ReadFrom(path, _offset) is not { } read) return false;
        _offset = read.Offset;
        foreach (var line in read.Lines) ReadLine(line);
        return read.Lines.Count > 0;
    }

    /// <summary>Lee lo nuevo de los transcripts de los agentes de la sesión: los comandos que dejan en segundo plano, los que
    /// paran y los avisos de fin que les llegan.</summary>
    /// <remarks>
    /// Un comando que deja un agente no sale en el transcript de la sesión: si el agente acaba esperándolo, la sesión parece
    /// terminada aunque el comando siga. Solo se miran a fondo las líneas que pueden decir algo de eso.
    /// </remarks>
    public void ReadAgents(SubagentFolder folder)
    {
        foreach (var (agentId, path) in folder.Transcripts())
        {
            var offset = _agentOffsets.GetValueOrDefault(agentId, -1);
            if (LengthOf(path) is not { } length) continue;
            if (length < offset) offset = -1;
            if (ReadFrom(path, offset) is not { } read) continue;
            _agentOffsets[agentId] = read.Offset;
            foreach (var line in read.Lines) ReadAgentLine(line, agentId);
        }
    }

    /// <summary>Lo que mide un fichero; null si no se puede saber.</summary>
    private static long? LengthOf(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Las líneas completas escritas en un fichero desde un punto, o las del final si es la primera vez (<paramref
    /// name="offset"/> −1), y hasta dónde llegan; null si no hay nada nuevo o no se ha podido leer.</summary>
    private static (List<string> Lines, long Offset)? ReadFrom(string path, long offset)
    {
        if (LengthOf(path) is not { } length || length == offset) return null;
        var start = offset >= 0 ? offset : Math.Max(0, length - FirstWindow);
        var skipPartial = offset < 0 && start > 0;
        byte[] bytes;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            stream.Seek(start, SeekOrigin.Begin);
            bytes = new byte[length - start];
            stream.ReadExactly(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var end = Array.LastIndexOf(bytes, (byte)'\n');
        if (end < 0) return offset < 0 ? ([], start) : null;
        var from = skipPartial ? Array.IndexOf(bytes, (byte)'\n') + 1 : 0;
        var lines = Encoding.UTF8.GetString(bytes, from, Math.Max(0, end + 1 - from)).Split('\n').Where(l => l.Length > 0).ToList();
        return (lines, start + end + 1);
    }

    /// <summary>Olvida lo leído, para empezar el fichero de nuevo.</summary>
    private void Reset()
    {
        _offset = -1;
        _calls.Clear();
        _tasks.Clear();
        _early.Clear();
        _agentOffsets.Clear();
        (_turnEnd, _activity) = (null, null);
    }

    /// <summary>Toma nota de una línea del transcript si dice algo del trabajo en segundo plano o de los turnos.</summary>
    /// <remarks>Antes de interpretar el JSON se mira si la línea puede interesar: casi todas son de herramientas y texto.</remarks>
    internal void ReadLine(string line)
    {
        if (!line.Contains("\"assistant\"", StringComparison.Ordinal) && !line.Contains("\"user\"", StringComparison.Ordinal) &&
            !line.Contains("task-notification", StringComparison.Ordinal) && !line.Contains("\"system\"", StringComparison.Ordinal)) return;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("isSidechain", out var side) && side.ValueKind == JsonValueKind.True) return;
            if (TranscriptReader.Timestamp(root) is not { } utc) return;
            switch (TranscriptReader.Str(root, "type"))
            {
                case "assistant":
                    Assistant(root, utc);
                    break;
                case "user":
                    User(root, utc);
                    break;
                case "queue-operation" when TranscriptReader.Str(root, "operation") == "enqueue":
                    Notice(TranscriptReader.Str(root, "content"), utc);
                    break;
                case "system" when TranscriptReader.Str(root, "subtype") is "stop_hook_summary" or "turn_duration":
                    if (_turnEnd is not { } before || utc > before) _turnEnd = utc;
                    break;
            }
        }
        catch (JsonException)
        {
        }
    }

    /// <summary>Toma nota de una línea del transcript de un agente si deja un comando en segundo plano, lo para o le llega
    /// el aviso de fin de uno.</summary>
    /// <remarks>Lo demás del agente no cuenta aquí: si sigue vivo lo dicen su .meta.json y cuándo escribió.</remarks>
    internal void ReadAgentLine(string line, string agentId)
    {
        if (!line.Contains("backgroundTaskId", StringComparison.Ordinal) && !line.Contains("task-notification", StringComparison.Ordinal) &&
            !line.Contains("\"Bash\"", StringComparison.Ordinal) && !line.Contains("\"PowerShell\"", StringComparison.Ordinal) &&
            !line.Contains("TaskStop", StringComparison.Ordinal) && !line.Contains("KillShell", StringComparison.Ordinal) &&
            !line.Contains("KillBash", StringComparison.Ordinal)) return;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || TranscriptReader.Timestamp(root) is not { } utc) return;
            switch (TranscriptReader.Str(root, "type"))
            {
                case "assistant" when root.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array:
                    Calls(content, utc, agentId);
                    break;
                case "user" when root.TryGetProperty("toolUseResult", out var result) && result.ValueKind == JsonValueKind.Object:
                    Launched(root, result, utc);
                    break;
                case "user" when root.TryGetProperty("message", out var msg):
                    Notice(TranscriptReader.Str(msg, "content"), utc);
                    break;
                case "queue-operation" when TranscriptReader.Str(root, "operation") == "enqueue":
                    Notice(TranscriptReader.Str(root, "content"), utc);
                    break;
            }
        }
        catch (JsonException)
        {
        }
    }

    /// <summary>Apunta que la sesión principal trabaja y guarda las llamadas que pueden lanzar algo en segundo plano o pararlo.</summary>
    private void Assistant(JsonElement root, DateTime utc)
    {
        Active(utc);
        if (!root.TryGetProperty("message", out var msg) || !msg.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) return;
        Calls(content, utc, null);
    }

    /// <summary>Guarda de un mensaje las llamadas que pueden dejar algo en segundo plano (un agente, un comando) y apunta las
    /// que despiertan un agente o paran algo.</summary>
    /// <param name="from">El agente que las hace; null, la propia sesión.</param>
    /// <remarks>
    /// Un comando cualquiera se guarda también: si tarda demasiado, Claude Code lo pasa a segundo plano sin que se pidiera, y
    /// solo su resultado lo dice. Al llegar el resultado se olvida, así que no se acumulan.
    /// </remarks>
    private void Calls(JsonElement content, DateTime utc, string? from)
    {
        foreach (var block in content.EnumerateArray())
        {
            if (TranscriptReader.Str(block, "type") != "tool_use" || TranscriptReader.Str(block, "id") is not { } id) continue;
            var name = TranscriptReader.Str(block, "name") ?? "";
            var input = block.TryGetProperty("input", out var i) && i.ValueKind == JsonValueKind.Object ? i : default;
            var has = input.ValueKind == JsonValueKind.Object;
            if (name is "Agent" or "Task")
            {
                if (from is null) _calls[id] = new(has ? TranscriptReader.Str(input, "name") : null, Clip(has ? TranscriptReader.Str(input, "description") : null) ?? "Agente", utc, null);
            }
            else if (has && (name is "Bash" or "PowerShell" || input.TryGetProperty("run_in_background", out var bg) && bg.ValueKind == JsonValueKind.True))
                _calls[id] = new(null, Clip(TranscriptReader.Str(input, "description") ?? TranscriptReader.Str(input, "command")) ?? name, utc, from);
            else if (from is null && name == "SendMessage" && has && TranscriptReader.Str(input, "to") is { } to &&
                     _tasks.Values.FirstOrDefault(t => t.Agent && (t.Id == to || t.Name == to)) is { } woken)
                woken.WokenUtc = utc;
            else if (name is "TaskStop" or "KillShell" or "KillBash" && has &&
                     (TranscriptReader.Str(input, "task_id") ?? TranscriptReader.Str(input, "shell_id")) is { } stopped &&
                     _tasks.TryGetValue(stopped, out var task))
                task.StoppedUtc = utc;
        }
    }

    /// <summary>Recoge el resultado de una llamada que lanzó algo en segundo plano, o un aviso, o un prompt de la persona.</summary>
    private void User(JsonElement root, DateTime utc)
    {
        if (root.TryGetProperty("toolUseResult", out var result) && result.ValueKind == JsonValueKind.Object)
        {
            Launched(root, result, utc);
            return;
        }
        if (!root.TryGetProperty("message", out var msg)) return;
        var text = TranscriptReader.Str(msg, "content");
        if (text?.StartsWith("<task-notification>", StringComparison.Ordinal) == true)
        {
            Notice(text, utc);
            Active(utc);
        }
        else if (msg.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array &&
                 content.EnumerateArray().Any(b => TranscriptReader.Str(b, "type") == "tool_result"))
        {
        }
        else if (!(root.TryGetProperty("isMeta", out var meta) && meta.ValueKind == JsonValueKind.True))
        {
            Active(utc);
        }
    }

    /// <summary>Da de alta el agente o el comando que lanzó una llamada, si la llamada lo dejó en segundo plano.</summary>
    /// <remarks>
    /// Un comando se espera si Claude Code lo pasó a segundo plano por tardar (su resultado trae <c>timedOutAfterMs</c>) o si
    /// lo dejó un agente: quien lo lanzó quería su resultado. Uno que la sesión lanzó así a propósito puede ser un servidor.
    /// </remarks>
    private void Launched(JsonElement root, JsonElement result, DateTime utc)
    {
        if (!root.TryGetProperty("message", out var msg) || !msg.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) return;
        var callId = content.EnumerateArray().Select(b => TranscriptReader.Str(b, "tool_use_id")).FirstOrDefault(s => s is not null);
        if (callId is null || !_calls.Remove(callId, out var call)) return;
        var agent = call.From is null && TranscriptReader.Str(result, "status") == "async_launched" ? TranscriptReader.Str(result, "agentId") : null;
        var id = agent ?? TranscriptReader.Str(result, "backgroundTaskId");
        if (id is null) return;
        var awaited = agent is null && (call.From is not null || result.TryGetProperty("timedOutAfterMs", out _));
        var task = _tasks[id] = new Launch
        {
            Id = id, Agent = agent is not null, Name = call.Name, Description = call.Description, StartUtc = call.Utc, WokenUtc = call.Utc,
            Awaited = awaited, From = call.From,
        };
        if (_early.Remove(id, out var notice) && notice >= call.Utc) task.NoticeUtc = notice;
    }

    /// <summary>Apunta el aviso de fin de una tarea que se sigue, venga en el transcript de la sesión o en el de un agente.</summary>
    private void Notice(string? text, DateTime utc)
    {
        if (text is null || !text.Contains("<task-notification>", StringComparison.Ordinal)) return;
        if (TaskId().Match(text) is not { Success: true } match) return;
        var id = match.Groups[1].Value;
        if (!_tasks.TryGetValue(id, out var task))
        {
            if (!_early.TryGetValue(id, out var seen) || utc > seen) _early[id] = utc;
            return;
        }
        if (task.NoticeUtc is not { } before || utc > before) task.NoticeUtc = utc;
    }

    /// <summary>Apunta que la sesión principal ha hecho algo en un momento.</summary>
    private void Active(DateTime utc)
    {
        if (_activity is not { } before || utc > before) _activity = utc;
    }

    /// <summary>La descripción de una llamada en una línea y sin pasarse de largo.</summary>
    private static string? Clip(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var line = text.Trim().Split('\n')[0].Trim();
        return line.Length <= 80 ? line : line[..79] + "…";
    }

    [GeneratedRegex(@"<task-id>([^<]+)</task-id>")]
    private static partial Regex TaskId();

    /// <summary>El trabajo en segundo plano en un momento: qué agentes espera la sesión, qué comandos siguen y si su turno
    /// ha acabado.</summary>
    /// <param name="subagents">La carpeta de los transcripts de sus agentes, para saber cuáles siguen vivos.</param>
    public SessionBackground Snapshot(DateTime nowUtc, SubagentFolder subagents)
    {
        var idle = _turnEnd is { } end && (_activity is not { } act || act <= end);
        var agents = new List<BackgroundAgent>();
        var commands = new List<BackgroundCommand>();
        foreach (var task in _tasks.Values.OrderBy(t => t.StartUtc))
        {
            if (!task.Agent)
            {
                if (task.NoticeUtc is not null || task.StoppedUtc is not null) continue;
                var awaited = task.Awaited && nowUtc - task.StartUtc < AwaitLimit;
                commands.Add(new BackgroundCommand(task.Id, task.Description, task.StartUtc, awaited, task.From is { } from ? subagents.Description(from) : null));
                continue;
            }
            var running = Running(task, nowUtc, subagents);
            var finishedNow = !running && task.NoticeUtc is { } done && _turnEnd is { } turn && done > turn;
            if (running || finishedNow) agents.Add(new BackgroundAgent(task.Id, task.Description, task.StartUtc, running, running ? null : task.NoticeUtc));
        }
        return new SessionBackground
        {
            Agents = agents,
            Commands = commands,
            MainIdle = idle,
            ResumePending = idle && _tasks.Values.Any(t => t.NoticeUtc is { } n && n > _turnEnd),
            TurnEndUtc = _turnEnd,
        };
    }

    /// <summary>Si un agente sigue trabajando: no ha avisado desde que se lanzó o se despertó, su .meta.json no dice que
    /// haya acabado y él o alguno de sus hijos ha escrito algo hace poco.</summary>
    /// <remarks>Solo la sesión principal lo despierta, con un SendMessage, así que un aviso posterior basta y ahorra mirar sus
    /// ficheros cada segundo. Claude Code pone <c>finished</c> al pararlo y lo quita al despertarlo; si la sesión se cerró a
    /// medias, nadie avisa y el silencio largo lo da por parado.</remarks>
    private static bool Running(Launch task, DateTime nowUtc, SubagentFolder subagents)
    {
        if (task.NoticeUtc is { } notice && notice >= task.WokenUtc) return false;
        if (subagents.Finished(task.Id)) return false;
        var written = subagents.LastWrite(task.Id) is { } w && w > task.WokenUtc ? w : task.WokenUtc;
        if (nowUtc - written < Quiet) return true;
        return subagents.Descendants(task.Id).Any(child => subagents.LastWrite(child) is { } w && nowUtc - w < Quiet);
    }
}

/// <summary>La carpeta con los transcripts de los agentes de una sesión: si cada uno ha acabado, cuándo escribió por última
/// vez y qué agentes lanzó.</summary>
public sealed class SubagentFolder(string? folder)
{
    /// <summary>La carpeta de los agentes de una sesión, junto a su transcript.</summary>
    public static SubagentFolder Of(string? transcriptPath, string sessionId) =>
        new(transcriptPath is null || Path.GetDirectoryName(transcriptPath) is not { } dir ? null : Path.Combine(dir, sessionId, "subagents"));

    /// <summary>Los transcripts de los agentes de la sesión: el id de cada uno y su fichero.</summary>
    public IEnumerable<(string AgentId, string Path)> Transcripts()
    {
        if (folder is null || !Directory.Exists(folder)) return [];
        try
        {
            return Directory.EnumerateFiles(folder, "agent-*.jsonl")
                .Select(path => (Path.GetFileName(path)["agent-".Length..^".jsonl".Length], path))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>El encargo de un agente, como lo dice su .meta.json; null si no lo tiene.</summary>
    public string? Description(string agentId)
    {
        if (folder is null) return null;
        try
        {
            var path = Path.Combine(folder, $"agent-{agentId}.meta.json");
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return TranscriptReader.Str(doc.RootElement, "description");
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Si el .meta.json del agente dice que ha acabado.</summary>
    public bool Finished(string agentId)
    {
        if (folder is null) return false;
        try
        {
            var path = Path.Combine(folder, $"agent-{agentId}.meta.json");
            if (!File.Exists(path)) return false;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("finished", out var f) && f.ValueKind == JsonValueKind.True;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Cuándo escribió el agente por última vez en su transcript; null si aún no tiene.</summary>
    public DateTime? LastWrite(string agentId)
    {
        if (folder is null) return null;
        var file = new FileInfo(Path.Combine(folder, $"agent-{agentId}.jsonl"));
        return file.Exists ? file.LastWriteTimeUtc : null;
    }

    /// <summary>Los agentes que lanzó un agente, y los que lanzaron estos, según los .meta.json de la carpeta.</summary>
    public IEnumerable<string> Descendants(string agentId)
    {
        if (folder is null || !Directory.Exists(folder)) return [];
        var parents = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var meta in Directory.EnumerateFiles(folder, "agent-*.meta.json"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(meta));
                    if (TranscriptReader.Str(doc.RootElement, "parentAgentId") is { Length: > 0 } parent)
                        parents[Path.GetFileName(meta)["agent-".Length..^".meta.json".Length]] = parent;
                }
                catch (JsonException)
                {
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
        var found = new List<string>();
        var pending = new Queue<string>([agentId]);
        while (pending.TryDequeue(out var current))
            foreach (var (child, parent) in parents)
                if (parent == current && !found.Contains(child))
                {
                    found.Add(child);
                    pending.Enqueue(child);
                }
        return found;
    }
}

/// <summary>El estado que se enseña de una sesión, con lo que dejó el hook y lo que se sabe de su trabajo en segundo plano.</summary>
public static class SessionActivity
{
    /// <summary>El estado de una sesión una vez mirado su trabajo en segundo plano.</summary>
    /// <remarks>
    /// Con el turno acabado, si le quedan agentes trabajando espera a sus agentes, aunque el hook diga Done (su Stop) o Working
    /// (las herramientas de sus agentes también disparan los hooks de la sesión); sin agentes, si espera el resultado de un
    /// comando en marcha, espera a ese comando. Si ya ha llegado el aviso de fin de algo
    /// suyo, va a retomar sola: sigue trabajando, y el aviso de «ha terminado» espera al turno que viene. Lo que te pide una
    /// respuesta no se toca.
    /// </remarks>
    public static SessionStatus Status(SessionStatus hook, SessionBackground? background)
    {
        if (hook is not (SessionStatus.Done or SessionStatus.Working) || background is not { MainIdle: true }) return hook;
        if (background.RunningAgents > 0) return SessionStatus.Agents;
        if (background.AwaitedCommands > 0) return SessionStatus.Command;
        return background.ResumePending ? SessionStatus.Working : hook;
    }
}
