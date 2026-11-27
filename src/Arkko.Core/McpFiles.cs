using System.Text;
using System.Text.RegularExpressions;

namespace Arkko.Core;

/// <summary>Un fichero leído de la carpeta de una MCP de navegador: su texto, si se cortó por largo, o por qué no se leyó.</summary>
public sealed record McpFile(string? Text, bool Truncated, string? Error);

/// <summary>Los ficheros que deja una MCP de navegador en la carpeta «.playwright-mcp» de una sesión (el árbol de la
/// página, los mensajes de la consola), para enseñarlos en el detalle de su llamada.</summary>
/// <remarks>
/// La ruta llega de la página, así que solo se lee dentro de esa carpeta, ficheros de texto y hasta un tamaño; un
/// enlace que lleve fuera, tampoco.
/// </remarks>
public static partial class McpFiles
{
    /// <summary>Carpeta donde la MCP de Playwright guarda lo que devuelve, dentro de la de trabajo de la sesión.</summary>
    public const string Folder = ".playwright-mcp";

    /// <summary>Lo que se lee como mucho de un fichero; el resto se corta.</summary>
    public const int MaxBytes = 512 * 1024;

    private static readonly string[] TextExtensions = [".yml", ".yaml", ".log", ".md", ".txt", ".json"];

    /// <summary>Lee un fichero de la carpeta de la MCP de una sesión, dada su ruta relativa a la carpeta de trabajo.</summary>
    public static McpFile Read(string? cwd, string relative)
    {
        if (Root(cwd) is not { } root) return new(null, false, "No se sabe en qué carpeta trabajaba la sesión.");
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) return new(null, false, "Solo se abren ficheros de la carpeta .playwright-mcp.");
        var folder = Path.GetFullPath(Path.Combine(root, Folder));
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(folder + Path.DirectorySeparatorChar, comparison)) return new(null, false, "Solo se abren ficheros de la carpeta .playwright-mcp.");
        if (!TextExtensions.Contains(Path.GetExtension(full).ToLowerInvariant())) return new(null, false, "Ese tipo de fichero no se enseña aquí.");
        var info = new FileInfo(full);
        if (!info.Exists) return new(null, false, "El fichero ya no está: se limpió la carpeta de la MCP.");
        // Ni el fichero ni su carpeta pueden ser un enlace: llevaría fuera de la carpeta sin que la ruta lo dijera.
        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint) || new DirectoryInfo(folder).Attributes.HasFlag(FileAttributes.ReparsePoint))
            return new(null, false, "Solo se abren ficheros de la carpeta .playwright-mcp.");
        try
        {
            using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[Math.Min(info.Length, MaxBytes)];
            var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            return new(Encoding.UTF8.GetString(buffer, 0, read), info.Length > MaxBytes, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(null, false, "No se ha podido leer el fichero.");
        }
    }

    /// <summary>La carpeta de trabajo de una sesión con el formato del sistema; en Windows, la «/c/…» de Git Bash pasa a «C:\…».</summary>
    public static string? Root(string? cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd)) return null;
        if (OperatingSystem.IsWindows() && BashDrive().Match(cwd) is { Success: true } m)
            return $"{char.ToUpperInvariant(m.Groups[1].Value[0])}:\\{m.Groups[2].Value.Replace('/', '\\')}";
        return cwd;
    }

    [GeneratedRegex("^/([a-zA-Z])(?:/(.*))?$")]
    private static partial Regex BashDrive();
}
