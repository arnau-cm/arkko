using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Arkko.Core;

/// <summary>Localiza el proceso de Claude Code que lanzó un hook y comprueba si sigue vivo.</summary>
/// <remarks>
/// SessionEnd no llega si la terminal se cierra de golpe. Guardar el PID del proceso claude
/// (o node, en instalaciones por npm) permite a la ventana retirar esas sesiones huérfanas;
/// la hora de arranque evita confundirlo con otro proceso que reutilice el mismo PID.
/// </remarks>
public static class ClaudeProcess
{
    private static readonly string[] Names = ["claude", "node"];

    /// <summary>Busca entre los antecesores del proceso actual el de Claude Code.</summary>
    public static (int Pid, DateTime StartUtc)? FindAncestor()
    {
        try
        {
            var parents = ParentMap();
            var pid = Environment.ProcessId;
            for (var depth = 0; depth < 12 && parents.TryGetValue(pid, out var info); depth++)
            {
                pid = info.Parent;
                if (!parents.TryGetValue(pid, out var parent)) break;
                if (IsClaude(parent.Name))
                {
                    using var process = Process.GetProcessById(pid);
                    return (pid, process.StartTime.ToUniversalTime());
                }
            }
        }
        catch (Exception ex)
        {
            JsonStore.LogError("ClaudeProcess", ex);
        }
        return null;
    }

    /// <summary>Cadena de antecesores de un proceso, del padre hacia arriba.</summary>
    public static List<int> Ancestors(int pid, int maxDepth = 12)
    {
        var result = new List<int>();
        try
        {
            var parents = ParentMap();
            for (var depth = 0; depth < maxDepth && parents.TryGetValue(pid, out var info); depth++)
            {
                pid = info.Parent;
                if (pid <= 4 || !parents.ContainsKey(pid) || result.Contains(pid)) break;
                result.Add(pid);
            }
        }
        catch (Exception ex)
        {
            JsonStore.LogError("ClaudeProcess", ex);
        }
        return result;
    }

    /// <summary>Indica si el proceso guardado sigue siendo el mismo y está vivo.</summary>
    public static bool IsAlive(int pid, DateTime? startUtc)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited) return false;
            return startUtc is null || Math.Abs((process.StartTime.ToUniversalTime() - startUtc.Value).TotalSeconds) < 2;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Indica si un nombre de ejecutable es el de Claude Code: claude o node, con .exe en Windows.</summary>
    private static bool IsClaude(string name) => OperatingSystem.IsWindows()
        ? Names.Any(n => name.Equals(n + ".exe", StringComparison.OrdinalIgnoreCase))
        : Names.Contains(Path.GetFileName(name));

    /// <summary>Foto de todos los procesos: PID → (padre, nombre del ejecutable).</summary>
    private static Dictionary<int, (int Parent, string Name)> ParentMap() =>
        OperatingSystem.IsWindows() ? WindowsParentMap()
        : OperatingSystem.IsLinux() ? LinuxParentMap()
        : PsParentMap();

    /// <summary>Foto de los procesos de Linux, leída de /proc.</summary>
    private static Dictionary<int, (int Parent, string Name)> LinuxParentMap()
    {
        var map = new Dictionary<int, (int, string)>();
        foreach (var dir in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(dir), out var pid)) continue;
            try
            {
                // «pid (nombre) estado padre …»: el nombre puede llevar espacios y paréntesis, así que se corta por el último.
                var stat = File.ReadAllText(Path.Combine(dir, "stat"));
                var open = stat.IndexOf('(');
                var close = stat.LastIndexOf(')');
                var fields = stat[(close + 2)..].Split(' ');
                map[pid] = (int.Parse(fields[1]), stat[(open + 1)..close]);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentOutOfRangeException or IndexOutOfRangeException)
            {
                // El proceso terminó mientras se leía.
            }
        }
        return map;
    }

    /// <summary>Foto de los procesos de macOS, con ps.</summary>
    private static Dictionary<int, (int Parent, string Name)> PsParentMap()
    {
        var map = new Dictionary<int, (int, string)>();
        var info = new ProcessStartInfo("ps", "-A -o pid=,ppid=,comm=") { RedirectStandardOutput = true, UseShellExecute = false };
        using var ps = Process.Start(info);
        if (ps is null) return map;
        var output = ps.StandardOutput.ReadToEnd();
        ps.WaitForExit(5000);
        foreach (var line in output.Split('\n'))
        {
            var parts = line.Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 3 && int.TryParse(parts[0], out var pid) && int.TryParse(parts[1], out var parent))
                map[pid] = (parent, Path.GetFileName(parts[2].Trim()));
        }
        return map;
    }

    /// <summary>Foto de los procesos de Windows, con la API de ToolHelp.</summary>
    private static Dictionary<int, (int Parent, string Name)> WindowsParentMap()
    {
        var map = new Dictionary<int, (int, string)>();
        var snapshot = CreateToolhelp32Snapshot(0x2, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1)) return map;
        try
        {
            var entry = new ProcessEntry32 { dwSize = (uint)Marshal.SizeOf<ProcessEntry32>() };
            if (!Process32FirstW(snapshot, ref entry)) return map;
            do
            {
                map[(int)entry.th32ProcessID] = ((int)entry.th32ParentProcessID, entry.szExeFile);
            } while (Process32NextW(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }
        return map;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
