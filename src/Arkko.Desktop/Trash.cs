using System.Globalization;

namespace Arkko.Desktop;

/// <summary>Lleva ficheros y carpetas a la papelera del sistema, para que un borrado tenga arreglo.</summary>
/// <remarks>
/// En Windows, la papelera de siempre; en Linux, la de freedesktop.org (~/.local/share/Trash), con su ficha de
/// dónde estaba cada cosa para poder restaurarla; en macOS, ~/.Trash.
/// </remarks>
public static class Trash
{
    /// <summary>Mueve a la papelera un fichero o una carpeta; lanza IOException si no se puede.</summary>
    public static void Move(string path)
    {
        var isDirectory = Directory.Exists(path);
        if (OperatingSystem.IsWindows())
        {
            if (isDirectory)
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            else
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            return;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string target;
        if (OperatingSystem.IsMacOS())
        {
            target = Free(Path.Combine(home, ".Trash"), Path.GetFileName(path));
        }
        else
        {
            var data = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } x ? x : Path.Combine(home, ".local", "share");
            var trash = Path.Combine(data, "Trash");
            Directory.CreateDirectory(Path.Combine(trash, "files"));
            Directory.CreateDirectory(Path.Combine(trash, "info"));
            target = Free(Path.Combine(trash, "files"), Path.GetFileName(path));
            var info = string.Join("\n",
                "[Trash Info]",
                "Path=" + Uri.EscapeDataString(Path.GetFullPath(path)).Replace("%2F", "/"),
                "DeletionDate=" + DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)) + "\n";
            File.WriteAllText(Path.Combine(trash, "info", Path.GetFileName(target) + ".trashinfo"), info);
        }
        if (isDirectory) Directory.Move(path, target);
        else File.Move(path, target);
    }

    /// <summary>Ruta libre dentro de una carpeta para un nombre: el mismo o, si ya existe, con un número detrás.</summary>
    private static string Free(string folder, string name)
    {
        Directory.CreateDirectory(folder);
        var candidate = Path.Combine(folder, name);
        for (var i = 2; File.Exists(candidate) || Directory.Exists(candidate); i++)
            candidate = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(name)} {i}{Path.GetExtension(name)}");
        return candidate;
    }
}
