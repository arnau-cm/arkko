using System.Globalization;

namespace Arkko.Core;

/// <summary>Rutas de la aplicación: binarios, almacén de datos y configuración de Claude Code.</summary>
public static class AppPaths
{
    /// <summary>Carpeta raíz de la app; se cambia con CLAUDE_STATS_HOME.</summary>
    /// <remarks>
    /// Por defecto, ClaudeStats en la carpeta de datos locales del usuario: %LOCALAPPDATA% en Windows,
    /// ~/.local/share en Linux y ~/Library/Application Support en macOS.
    /// </remarks>
    public static string Home { get; } = ResolveHome();

    public static string Bin => Path.Combine(Home, "bin");

    /// <summary>Ruta del ejecutable de la ventana, en su propia carpeta junto a la de ak.</summary>
    public static string WindowPath => Path.Combine(Home, "desktop", OperatingSystem.IsWindows() ? "Arkko.exe" : "Arkko");

    /// <summary>Ruta que tenía el ejecutable de la ventana cuando la app se llamaba Agent Pulse, para cambiar lo que
    /// aún apunte a ella.</summary>
    public static string OldWindowPath => Path.Combine(Home, "desktop", OperatingSystem.IsWindows() ? "AgentPulse.exe" : "AgentPulse");
    public static string Data => Path.Combine(Home, "data");
    public static string Sessions => Path.Combine(Data, "sessions");
    public static string Prompts => Path.Combine(Data, "prompts");
    public static string Backups => Path.Combine(Data, "backups");
    public static string LimitsFile => Path.Combine(Data, "limits.json");
    public static string StatuslineRawFile => Path.Combine(Data, "statusline-last.json");
    public static string AppSettingsFile => Path.Combine(Data, "app-settings.json");
    public static string TerminalLayoutFile => Path.Combine(Data, "terminal-layout.json");
    public static string ErrorLog => Path.Combine(Data, "errors.log");

    /// <summary>Hallazgos de secretos, cifrados; Claude Code tiene prohibido leer esta carpeta.</summary>
    public static string Security => Path.Combine(Data, "security");

    /// <summary>Carpeta de configuración de Claude Code, respetando CLAUDE_CONFIG_DIR.</summary>
    public static string ClaudeConfigDir
    {
        get
        {
            var custom = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            return string.IsNullOrWhiteSpace(custom)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude")
                : custom;
        }
    }

    public static string ClaudeSettingsFile => Path.Combine(ClaudeConfigDir, "settings.json");

    /// <summary>Devuelve el fichero de estado de una sesión.</summary>
    public static string SessionFile(string sessionId) => Path.Combine(Sessions, SafeName(sessionId) + ".json");

    /// <summary>Devuelve el registro de prompts de un día local.</summary>
    public static string PromptsFile(DateTime localDay) =>
        Path.Combine(Prompts, localDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".jsonl");

    /// <summary>Devuelve el histórico de los límites de un día local.</summary>
    public static string LimitsHistoryFile(DateTime localDay) =>
        Path.Combine(Data, "limits-history", localDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".jsonl");

    /// <summary>Resuelve la carpeta raíz a partir del entorno.</summary>
    private static string ResolveHome()
    {
        var custom = Environment.GetEnvironmentVariable("CLAUDE_STATS_HOME");
        if (!string.IsNullOrWhiteSpace(custom)) return custom;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudeStats");
    }

    /// <summary>Limpia un identificador para usarlo como nombre de fichero.</summary>
    private static string SafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return chars.Length == 0 ? "sin-id" : new string(chars);
    }
}
