using System.Text.Json;

namespace Arkko.Core;

/// <summary>Los repositorios en que han trabajado las sesiones y el subtablero del panel que declara cada uno.</summary>
/// <remarks>
/// Un repositorio que registra trabajo en el panel lo dice en el <c>.arkko.json</c> de su raíz, versionado con
/// él: la sección de su proveedor lleva el subtablero en <c>board</c>. Con eso se une el repositorio a su subtablero,
/// para que los dos se vean del mismo color.
/// </remarks>
public static class RepoBoards
{
    /// <summary>El subtablero declarado por cada repositorio, por su nombre; null en los que no declaran ninguno.</summary>
    /// <remarks>El nombre es el que agrupa las sesiones: el del repositorio en un worktree, el de la carpeta si no.</remarks>
    public static Dictionary<string, string?> Read(IEnumerable<string?> folders)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f!.TrimEnd('/', '\\'))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (RootOf(folder) is not { } root) continue;
            var name = Worktree.RepoOf(folder) ?? Path.GetFileName(folder);
            if (string.IsNullOrEmpty(name)) continue;
            var board = BoardIn(Path.Combine(root, ".arkko.json"));
            // Si un worktree lo declara y otro no (aún no tiene el fichero), cuenta el que lo declara.
            if (!result.TryGetValue(name, out var known) || known is null) result[name] = board;
        }
        return result;
    }

    /// <summary>El subtablero del <c>.arkko.json</c> de un repositorio: el <c>board</c> de la sección de su proveedor.</summary>
    internal static string? BoardIn(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var section in doc.RootElement.EnumerateObject())
                if (section.Value.ValueKind == JsonValueKind.Object && section.Value.TryGetProperty("board", out var board)
                    && board.ValueKind == JsonValueKind.String && board.GetString() is { Length: > 0 } name)
                    return name.Trim();
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>La raíz del árbol de trabajo de una carpeta: la primera, hacia arriba, que tiene .git; null si no hay.</summary>
    private static string? RootOf(string folder)
    {
        try
        {
            for (var dir = new DirectoryInfo(folder); dir is { Exists: true }; dir = dir.Parent)
                if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, ".git"))) return dir.FullName;
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
