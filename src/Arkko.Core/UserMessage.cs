using System.Text.RegularExpressions;

namespace Arkko.Core;

/// <summary>Con qué empieza un turno: un prompt de la persona, un comando «!» suyo o un aviso que llega sin escribirlo nadie.</summary>
public enum PromptKind
{
    /// <summary>Un prompt escrito por la persona, o un comando «/».</summary>
    Prompt,
    /// <summary>Un comando de consola lanzado con «!» en Claude Code.</summary>
    Shell,
    /// <summary>Un aviso de Claude Code, como el fin de una tarea en segundo plano.</summary>
    Notification,
}

/// <summary>Reconoce los mensajes que Claude Code escribe en nombre de la persona y saca de ellos lo que se enseña.</summary>
/// <remarks>
/// Claude Code guarda como mensajes de la persona, con etiquetas propias, dos cosas que no escribió ella como prompt: los
/// comandos «!», con su salida en un segundo mensaje, y los avisos de las tareas en segundo plano.
/// </remarks>
public static partial class UserMessage
{
    /// <summary>De qué tipo es un mensaje de la persona por cómo empieza; un prompt si no lleva ninguna etiqueta conocida.</summary>
    public static PromptKind KindOf(string? text)
    {
        var trimmed = text?.TrimStart() ?? "";
        if (trimmed.StartsWith("<task-notification>", StringComparison.Ordinal)) return PromptKind.Notification;
        if (trimmed.StartsWith("<bash-input>", StringComparison.Ordinal)) return PromptKind.Shell;
        return PromptKind.Prompt;
    }

    /// <summary>Si el mensaje es la salida de un comando «!», que Claude Code guarda aparte, justo después del comando.</summary>
    public static bool IsShellOutput(string? text)
    {
        var trimmed = text?.TrimStart() ?? "";
        return trimmed.StartsWith("<bash-stdout>", StringComparison.Ordinal) || trimmed.StartsWith("<bash-stderr>", StringComparison.Ordinal);
    }

    /// <summary>La salida de un comando «!»: lo que escribió en su salida y en la de errores, juntos, y si falló (solo
    /// escribió errores); null si el mensaje no es una salida.</summary>
    public static (string Text, bool Failed)? ShellOutput(string? text)
    {
        if (!IsShellOutput(text)) return null;
        var stdout = Tag(text!, "bash-stdout") ?? "";
        var stderr = Tag(text!, "bash-stderr") ?? "";
        return (string.Join('\n', new[] { stdout, stderr }.Where(s => s.Length > 0)), stdout.Length == 0 && stderr.Length > 0);
    }

    /// <summary>El comando de un mensaje «!», sin la etiqueta; null si no lo es.</summary>
    public static string? ShellCommand(string? text) => text is null ? null : Tag(text, "bash-input");

    /// <summary>Lo que dice un aviso: su resumen, o el texto sin etiquetas si no lo trae.</summary>
    public static string NotificationText(string text) =>
        Tag(text, "summary") ?? string.Join(' ', AnyTag().Replace(text, " ").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Contenido de la primera etiqueta con ese nombre, o null.</summary>
    internal static string? Tag(string text, string name)
    {
        var m = Regex.Match(text, $"<{name}>(.*?)</{name}>", RegexOptions.Singleline);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex AnyTag();
}
