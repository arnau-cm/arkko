using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Notificaciones y sonido del sistema: las modernas de Windows, notify-send en Linux y el centro de notificaciones en macOS.</summary>
/// <remarks>
/// En Windows son las mismas que las de la ventana de WPF, con su cabecera y su etiqueta por sesión. En Linux
/// y macOS se lanzan con la herramienta del sistema: el título y el texto nunca llevan un secreto, solo su
/// tipo y origen, así que pueden ir en la línea de comandos.
/// </remarks>
public static class SystemNotifications
{
    private static readonly string Icon = Path.Combine(AppContext.BaseDirectory, "Assets", "arkko-notificacion.png");

    /// <summary>Muestra una notificación con una etiqueta; false si el sistema no la admite.</summary>
    public static bool Show(string tag, string title, string text, bool sound)
    {
#if WINDOWS
        return ModernToasts.Show(tag, title, text, sound);
#else
        if (OperatingSystem.IsMacOS())
        {
            var script = $"display notification \"{AppleScript(text)}\" with title \"{AppleScript(title)}\"" + (sound ? " sound name \"Glass\"" : "");
            return Run("osascript", "-e", script);
        }
        var shown = Run("notify-send", "--app-name=Arkko", $"--icon={Icon}", title, text);
        if (shown && sound) PlaySound();
        return shown;
#endif
    }

    /// <summary>Quita una notificación ya enviada, si el sistema lo permite (solo Windows).</summary>
    public static void Remove(string tag)
    {
#if WINDOWS
        ModernToasts.Remove(tag);
#endif
    }

    /// <summary>Suena el aviso del sistema, sin notificación.</summary>
    public static void PlaySound()
    {
        if (OperatingSystem.IsWindows()) MessageBeep(0x40);
        else if (OperatingSystem.IsMacOS()) Run("afplay", "/System/Library/Sounds/Glass.aiff");
        else Run("canberra-gtk-play", "--id=message-new-instant");
    }

    /// <summary>Lanza una herramienta del sistema sin esperar a que el aviso se cierre; false si no está o falla al arrancar.</summary>
    private static bool Run(string tool, params string[] args)
    {
        try
        {
            var info = new ProcessStartInfo(tool) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            foreach (var a in args) info.ArgumentList.Add(a);
            using var process = Process.Start(info);
            return process is not null;
        }
        catch (Win32Exception)
        {
            return false;
        }
        catch (Exception ex)
        {
            JsonStore.LogError("notify", ex);
            return false;
        }
    }

    /// <summary>Texto preparado para ir entre comillas en AppleScript.</summary>
    private static string AppleScript(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");

    [DllImport("user32.dll")]
    private static extern bool MessageBeep(uint type);
}
