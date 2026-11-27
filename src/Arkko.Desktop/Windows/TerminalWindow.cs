using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Localiza y trae al frente la ventana de terminal que aloja una sesión de Claude Code.</summary>
/// <remarks>
/// Se sube por los procesos antecesores de claude.exe (shell, terminal…) hasta el primero que tenga
/// una ventana visible: así vale para Warp, Windows Terminal o VS Code sin conocerlos. Si ese proceso
/// tiene varias ventanas, se prefiere la que lleva la carpeta de la sesión en el título. Las
/// pestañas no se pueden elegir desde fuera: se activa la ventana y la pestaña queda como estaba.
/// </remarks>
public static class TerminalWindow
{
    private const int SwRestore = 9;

    /// <summary>Trae al frente la terminal de la sesión; false si no se encuentra.</summary>
    public static bool Focus(SessionState session)
    {
        if (session.ClaudePid is not { } pid || !ClaudeProcess.IsAlive(pid, session.ClaudeStartUtc)) return false;

        foreach (var ancestor in ClaudeProcess.Ancestors(pid))
        {
            var windows = VisibleWindows(ancestor);
            if (windows.Count == 0) continue;

            var project = session.Project ?? "";
            var target = windows.FirstOrDefault(w => project.Length > 0 &&
                             w.Title.Contains(project, StringComparison.OrdinalIgnoreCase)).Handle;
            if (target == IntPtr.Zero) target = windows[0].Handle;
            return Activate(target);
        }
        return false;
    }

    /// <summary>Ventanas visibles con título de un proceso, en orden de la más cercana al frente.</summary>
    private static List<(IntPtr Handle, string Title)> VisibleWindows(int pid)
    {
        var result = new List<(IntPtr, string)>();
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var owner);
            if (owner != pid || !IsWindowVisible(hwnd) || GetWindow(hwnd, 4) != IntPtr.Zero) return true;

            var length = GetWindowTextLength(hwnd);
            if (length == 0) return true;
            var title = new StringBuilder(length + 1);
            GetWindowText(hwnd, title, title.Capacity);
            result.Add((hwnd, title.ToString()));
            return true;
        }, IntPtr.Zero);
        return result;
    }

    /// <summary>Restaura la ventana si está minimizada y la pone en primer plano.</summary>
    private static bool Activate(IntPtr hwnd)
    {
        if (IsIconic(hwnd)) ShowWindow(hwnd, SwRestore);
        return SetForegroundWindow(hwnd);
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
}
