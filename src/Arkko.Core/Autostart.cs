using System.Security;
using Microsoft.Win32;

namespace Arkko.Core;

/// <summary>Abre la ventana al iniciar sesión: la clave Run en Windows, una entrada de autostart en Linux y un LaunchAgent en macOS.</summary>
/// <remarks>Fuera de Windows se aplica en el siguiente inicio de sesión: no se arranca nada al activarlo.</remarks>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "ClaudeStats";
    private const string MacLabel = "com.agentpulse.window";

    /// <summary>Activa el arranque de la ventana que está en una ruta.</summary>
    public static void Enable(string appPath)
    {
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            key.SetValue(RunValue, $"\"{appPath}\"");
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(EntryFile)!);
        File.WriteAllText(EntryFile, OperatingSystem.IsMacOS() ? LaunchAgent(appPath) : DesktopEntry(appPath));
    }

    /// <summary>Retira el arranque, si estaba.</summary>
    public static void Disable()
    {
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(RunValue, throwOnMissingValue: false);
            return;
        }
        if (File.Exists(EntryFile)) File.Delete(EntryFile);
    }

    /// <summary>Si el arranque abre la ventana de una ruta, lo cambia para que abra la de otra; si abre otra cosa, o no
    /// está activado, no lo toca.</summary>
    /// <remarks>Lo usa la ventana al renombrarse: así el arranque de una copia de prueba, en otra carpeta, no cambia el de
    /// la app de verdad.</remarks>
    public static void Retarget(string oldPath, string newPath)
    {
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (Retargeted(key?.GetValue(RunValue) as string, oldPath, newPath) is { } value) key!.SetValue(RunValue, value);
            return;
        }
        if (File.Exists(EntryFile) && File.ReadAllText(EntryFile).Contains(oldPath, StringComparison.Ordinal)) Enable(newPath);
    }

    /// <summary>El valor nuevo del arranque de Windows si el de ahora abre la ventana de la ruta vieja; null si no.</summary>
    internal static string? Retargeted(string? current, string oldPath, string newPath) =>
        string.Equals(current?.Trim(), $"\"{oldPath}\"", StringComparison.OrdinalIgnoreCase) ? $"\"{newPath}\"" : null;

    /// <summary>Indica si el arranque está activado.</summary>
    public static bool IsEnabled
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return File.Exists(EntryFile);
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) is not null;
        }
    }

    /// <summary>Fichero que lo activa fuera de Windows: el LaunchAgent en macOS y la entrada de autostart en Linux.</summary>
    private static string EntryFile
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (OperatingSystem.IsMacOS()) return Path.Combine(home, "Library", "LaunchAgents", MacLabel + ".plist");
            var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x ? x : Path.Combine(home, ".config");
            return Path.Combine(config, "autostart", "agent-pulse.desktop");
        }
    }

    /// <summary>Entrada de autostart de freedesktop.org, la que leen GNOME, KDE y el resto de escritorios de Linux.</summary>
    /// <remarks>Las líneas se unen con \n a propósito: un texto literal heredaría el CRLF de este fichero.</remarks>
    internal static string DesktopEntry(string appPath) => Lines(
        "[Desktop Entry]",
        "Type=Application",
        "Name=Arkko",
        $"Exec=\"{appPath.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"",
        "X-GNOME-Autostart-enabled=true");

    /// <summary>LaunchAgent de macOS que abre la ventana al iniciar sesión.</summary>
    internal static string LaunchAgent(string appPath) => Lines(
        """<?xml version="1.0" encoding="UTF-8"?>""",
        """<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">""",
        """<plist version="1.0">""",
        "<dict>",
        "  <key>Label</key>",
        $"  <string>{MacLabel}</string>",
        "  <key>ProgramArguments</key>",
        "  <array>",
        $"    <string>{SecurityElement.Escape(appPath)}</string>",
        "  </array>",
        "  <key>RunAtLoad</key>",
        "  <true/>",
        "</dict>",
        "</plist>");

    /// <summary>Une unas líneas con \n y termina en salto de línea.</summary>
    private static string Lines(params string[] lines) => string.Join("\n", lines) + "\n";
}
