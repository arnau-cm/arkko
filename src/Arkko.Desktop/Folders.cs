using System.Diagnostics;

namespace Arkko.Desktop;

/// <summary>Abre carpetas en el explorador de ficheros de cada sistema: el Explorador, Finder o el del escritorio de Linux.</summary>
/// <remarks>
/// Nunca abre un fichero: una ruta puede salir de un transcript, y abrirla lanzaría su programa asociado, que en un
/// script o un ejecutable es ejecutarlo. Las herramientas se lanzan sin pasar por un intérprete de órdenes.
/// </remarks>
public static class Folders
{
    /// <summary>Abre una carpeta.</summary>
    public static void Open(string folder)
    {
        if (OperatingSystem.IsWindows()) Process.Start("explorer.exe", $"\"{folder}\"")?.Dispose();
        else Start(OperatingSystem.IsMacOS() ? "open" : "xdg-open", folder);
    }

    /// <summary>Abre la carpeta de un fichero con él elegido; en Linux, que no tiene una forma común de elegirlo, solo la carpeta.</summary>
    public static void Reveal(string file)
    {
        if (OperatingSystem.IsWindows()) Process.Start("explorer.exe", $"/select,\"{file}\"")?.Dispose();
        else if (OperatingSystem.IsMacOS()) Start("open", "-R", file);
        else Open(Path.GetDirectoryName(file)!);
    }

    /// <summary>Lanza una herramienta del sistema con sus argumentos.</summary>
    private static void Start(string tool, params string[] args)
    {
        var info = new ProcessStartInfo(tool) { UseShellExecute = false };
        foreach (var a in args) info.ArgumentList.Add(a);
        Process.Start(info)?.Dispose();
    }
}
