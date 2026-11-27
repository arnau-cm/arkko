using System.Text.Json.Nodes;

namespace Arkko.Core;

/// <summary>Plazos de Claude Code en días; null es el comportamiento por defecto de Claude Code.</summary>
public sealed record ClaudeRetentionDays(int? Terminal, int? Desktop);

/// <summary>Plazos con los que Claude Code borra sus propios transcripts, guardados en su settings.json.</summary>
/// <remarks>
/// La app solo cambia el ajuste: el borrado lo hace Claude Code en su limpieza automática. Sin el
/// ajuste, las sesiones de terminal duran 30 días y las de Claude Desktop y Cowork no caducan.
/// </remarks>
public static class ClaudeRetention
{
    public const string TerminalKey = "cleanupPeriodDays";
    public const string DesktopKey = "desktopSessionCleanupPeriodDays";

    /// <summary>Plazo de las sesiones de terminal cuando no hay ajuste.</summary>
    public const int TerminalDefault = 30;

    /// <summary>Configuración gestionada por la empresa en Windows; si fija un plazo, manda sobre la del usuario.</summary>
    public static string ManagedFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ClaudeCode", "managed-settings.json");

    /// <summary>Plazos que tiene un settings.</summary>
    public static ClaudeRetentionDays Read(JsonObject settings) => new(Days(settings, TerminalKey), Days(settings, DesktopKey));

    /// <summary>Pone los plazos en el settings; un plazo null quita su clave y vuelve al de Claude Code.</summary>
    public static void Apply(JsonObject settings, ClaudeRetentionDays days)
    {
        Set(settings, TerminalKey, days.Terminal);
        Set(settings, DesktopKey, days.Desktop);
    }

    /// <summary>Plazos fijados por la configuración gestionada; nulos si no hay o no se puede leer.</summary>
    public static ClaudeRetentionDays Managed(string? path = null)
    {
        try
        {
            return Read(SettingsInstaller.Load(path ?? ManagedFile));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            return new(null, null);
        }
    }

    /// <summary>Plazo de una clave si es un número entero válido para Claude Code, de 1 en adelante.</summary>
    private static int? Days(JsonObject settings, string key) =>
        settings[key] is JsonValue v && v.TryGetValue<int>(out var days) && days >= 1 ? days : null;

    /// <summary>Escribe o quita una clave de plazo.</summary>
    private static void Set(JsonObject settings, string key, int? days)
    {
        if (days is { } d) settings[key] = Math.Max(1, d);
        else settings.Remove(key);
    }
}
