using System.Globalization;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Textos de la limpieza de la carpeta de datos, los mismos que la ventana de WPF.</summary>
public static class CleanupNotes
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    /// <summary>Tamaño legible: «512 B», «38 KB», «1,4 MB».</summary>
    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{Math.Max(1, bytes / 1024)} KB",
        _ => (bytes / (1024.0 * 1024)).ToString("0.#", Es) + " MB",
    };

    /// <summary>Texto de una limpieza: qué hizo y cuánto liberó.</summary>
    public static string CleanupText(CleanupResult r) =>
        !r.Any ? "no había nada que borrar"
        : string.Join(", ", new[]
        {
            r.FilesDeleted > 0 ? $"{r.FilesDeleted} fichero(s) borrado(s)" : null,
            r.TextStripped > 0 ? $"texto quitado de {r.TextStripped} día(s)" : null,
        }.Where(t => t is not null)) + $" · {Size(r.BytesFreed)} liberados";
}
