using System.Text;
using System.Text.Json;

namespace Arkko.Core;

/// <summary>Una ejecución de un hook de Arkko tal como la apunta Claude Code: cuándo, de qué evento, si fue bien y,
/// si no, por qué, dicho en corto.</summary>
public sealed record HookRun(DateTime Utc, string? Event, bool Ok, string? Reason);

/// <summary>Sigue en el transcript de una sesión lo que Claude Code apunta de cada hook de Arkko: la última ejecución y
/// el último turno que terminó sin que llegara su Stop.</summary>
/// <remarks>
/// Claude Code escribe una entrada por hook ejecutado, «hook_success» o un error con su código de salida y lo que dijo
/// por stderr, y sigue trabajando aunque falle. Es la única señal que queda cuando <c>ak.exe</c> no arranca. Cada lectura
/// sigue donde acabó la anterior; la primera empieza cerca del final, porque solo interesa lo reciente.
/// </remarks>
public sealed class HookTrace
{
    /// <summary>Lo que se lee del final de un transcript la primera vez.</summary>
    public const long FirstWindow = 512 * 1024;

    private long _offset = -1;

    /// <summary>La última ejecución de un hook de Arkko; null si aún no se ha visto ninguna.</summary>
    public HookRun? Last { get; private set; }

    /// <summary>Cuándo terminó el último turno cuyo Stop de Arkko falló; null si no hay ninguno.</summary>
    public DateTime? MissedStopUtc { get; private set; }

    /// <summary>Lee lo que se ha escrito desde la última vez; indica si ha cambiado lo que se sabe.</summary>
    /// <remarks>Una línea a medio escribir se deja para la próxima lectura. Si el fichero ha encogido, se empieza de nuevo.</remarks>
    public bool Read(string path)
    {
        long length;
        try
        {
            length = new FileInfo(path).Length;
        }
        catch (IOException)
        {
            return false;
        }
        if (length < _offset) (_offset, Last, MissedStopUtc) = (-1, null, null);
        if (length == _offset) return false;

        var start = _offset >= 0 ? _offset : Math.Max(0, length - FirstWindow);
        var skipPartial = _offset < 0 && start > 0;
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
            return false;
        }

        var end = Array.LastIndexOf(bytes, (byte)'\n');
        if (end < 0)
        {
            if (_offset < 0) _offset = start;
            return false;
        }
        var from = skipPartial ? Array.IndexOf(bytes, (byte)'\n') + 1 : 0;
        _offset = start + end + 1;

        var before = (Last, MissedStopUtc);
        foreach (var line in Encoding.UTF8.GetString(bytes, from, Math.Max(0, end + 1 - from)).Split('\n'))
            if (line.Contains("hook_", StringComparison.Ordinal)) ReadLine(line);
        return (Last, MissedStopUtc) != before;
    }

    /// <summary>Toma nota de una línea del transcript si es la ejecución de un hook de Arkko o el resumen de un Stop que
    /// falló.</summary>
    internal void ReadLine(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            var utc = Time(root);
            switch (Str(root, "type"))
            {
                case "attachment" when root.TryGetProperty("attachment", out var a) && a.ValueKind == JsonValueKind.Object:
                    Attachment(a, utc);
                    break;
                case "system" when Str(root, "subtype") == "stop_hook_summary":
                    StopSummary(root, utc);
                    break;
            }
        }
        catch (JsonException)
        {
        }
    }

    /// <summary>Apunta una ejecución de un hook de Arkko: bien si salió con 0, mal si no, con su motivo.</summary>
    private void Attachment(JsonElement a, DateTime utc)
    {
        if (Run(a, utc) is not { } run) return;
        Last = run;
        if (!run.Ok && run.Event == "Stop") MissedStopUtc = utc;
    }

    /// <summary>La ejecución de un hook de Arkko que describe un adjunto del transcript; null si es de otro hook o no es
    /// de un hook.</summary>
    public static HookRun? Run(JsonElement a, DateTime utc)
    {
        var type = Str(a, "type") ?? "";
        if (!type.StartsWith("hook_", StringComparison.Ordinal) || !SettingsInstaller.IsOwnCommand(Str(a, "command"))) return null;
        int? exit = a.TryGetProperty("exitCode", out var code) && code.TryGetInt32(out var c) ? c : null;
        var failed = exit is { } e ? e != 0 : type.Contains("error", StringComparison.Ordinal) || type.Contains("cancel", StringComparison.Ordinal);
        return new HookRun(utc, Str(a, "hookEvent") ?? Str(a, "hookName"), !failed, failed ? Reason(Str(a, "stderr"), exit, type) : null);
    }

    /// <summary>Un fallo de un hook de Arkko dicho para quien mira la sesión: en qué momento falló y por qué.</summary>
    public static string Describe(HookRun run) => $"El hook de Arkko falló {Moment(run.Event)}: {run.Reason}";

    /// <summary>El momento de la sesión en que salta un evento de hook, en palabras.</summary>
    private static string Moment(string? evt) => evt switch
    {
        "Stop" => "al cerrar el turno",
        "UserPromptSubmit" => "al enviar el prompt",
        "PreToolUse" => "antes de la herramienta",
        "PostToolUse" => "tras la herramienta",
        "Notification" => "con un aviso",
        "SessionStart" => "al abrir la sesión",
        "SessionEnd" => "al cerrar la sesión",
        _ => evt is null ? "" : $"en {evt}",
    };

    /// <summary>Con el resumen del Stop, que se escribe al acabar el turno, da por terminado el turno cuyo Stop de Arkko
    /// acaba de fallar.</summary>
    private void StopSummary(JsonElement root, DateTime utc)
    {
        var errors = root.TryGetProperty("hookErrors", out var list) && list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0;
        var ours = root.TryGetProperty("hookInfos", out var infos) && infos.ValueKind == JsonValueKind.Array &&
                   infos.EnumerateArray().Any(i => i.ValueKind == JsonValueKind.Object && SettingsInstaller.IsOwnCommand(Str(i, "command")));
        if (errors && ours && Last is { Ok: false, Event: "Stop" }) MissedStopUtc = utc;
    }

    /// <summary>Por qué ha fallado un hook, en pocas palabras, a partir de lo que dijo y su código de salida.</summary>
    public static string Reason(string? stderr, int? exitCode, string? type = null)
    {
        var text = stderr ?? "";
        bool Has(string s) => text.Contains(s, StringComparison.OrdinalIgnoreCase);
        if (Has("install .NET") || Has("hostfxr") || Has("missing_runtime")) return "No encuentra .NET";
        if (Has("Application Control") || Has("Smart App Control") || Has("blocked by")) return "Windows lo ha bloqueado (Smart App Control)";
        if (Has("is not recognized") || Has("No such file") || Has("cannot find the file") || Has("no se encuentra")) return "No encuentra ak.exe";
        if (type?.Contains("cancel", StringComparison.Ordinal) == true) return "Se cortó por tardar demasiado";

        const string prefix = "Failed with non-blocking status code:";
        var first = text.Replace(prefix, "", StringComparison.OrdinalIgnoreCase)
            .Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        if (first is not null) return first.Length <= 120 ? first : first[..119] + "…";
        return exitCode is { } e ? $"Salió con el código {e}" : "Falló sin decir por qué";
    }

    /// <summary>El instante de una entrada, en UTC; ahora si no lo lleva o no se entiende.</summary>
    private static DateTime Time(JsonElement root) =>
        Str(root, "timestamp") is { } text && DateTime.TryParse(text, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var utc)
            ? utc
            : DateTime.UtcNow;

    /// <summary>Un texto de un objeto JSON; null si no está o no es texto.</summary>
    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
