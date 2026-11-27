using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Arkko.Core;

/// <summary>Almacén de ficheros JSON con escritura atómica y bloqueo entre procesos.</summary>
/// <remarks>
/// Varios procesos escriben a la vez (la statusline y cada hook de cada sesión), así que cada
/// lectura-modificación-escritura va bajo un mutex con nombre y cada escritura se hace sobre un
/// temporal que luego reemplaza al original: un lector nunca ve un fichero a medias.
/// </remarks>
public static class JsonStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private static readonly JsonSerializerOptions CompactOptions = new(Options) { WriteIndented = false };
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    // Lo último leído de cada fichero, con la fecha y el tamaño que tenía: si no han cambiado, no se vuelve a abrir.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (long Ticks, long Length, byte[] Bytes)> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    // Lo que no se guarda en memoria: un fichero de estado nunca llega a esto, y uno que sí no compensa tenerlo.
    private const long MaxCached = 1024 * 1024;

    /// <summary>Lee y deserializa un fichero; devuelve null si no existe o no se puede leer.</summary>
    /// <remarks>
    /// Cada llamada devuelve un objeto nuevo, pero el fichero solo se vuelve a abrir si ha cambiado su fecha o
    /// su tamaño. La ventana lee los mismos ficheros de configuración cada segundo y por cada sesión, y abrir
    /// un fichero cuesta mucho más que mirar su fecha: el antivirus revisa cada apertura.
    /// </remarks>
    public static T? Read<T>(string path) where T : class
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return null;
            if (Cache.TryGetValue(path, out var cached) && cached.Ticks == info.LastWriteTimeUtc.Ticks && cached.Length == info.Length)
                return Deserialize<T>(cached.Bytes);
            byte[] bytes;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                bytes = new byte[fs.Length];
                fs.ReadExactly(bytes);
            }
            if (bytes.Length <= MaxCached) Cache[path] = (info.LastWriteTimeUtc.Ticks, bytes.Length, bytes);
            return Deserialize<T>(bytes);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Deserializa los bytes de un fichero, sin su BOM si lo lleva: desde un array, el lector no se lo salta.</summary>
    private static T? Deserialize<T>(byte[] bytes) =>
        JsonSerializer.Deserialize<T>(bytes.AsSpan(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0), Options);

    /// <summary>Escribe un objeto como JSON, sustituyendo el fichero de forma atómica.</summary>
    public static void Write<T>(string path, T value)
    {
        WriteText(path, JsonSerializer.Serialize(value, Options));
    }

    /// <summary>Escribe texto UTF-8 sin BOM, sustituyendo el fichero de forma atómica.</summary>
    public static void WriteText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = $"{path}.{Environment.ProcessId}.tmp";
        File.WriteAllText(tmp, text, Utf8NoBom);
        Retry(() => File.Move(tmp, path, overwrite: true));
    }

    /// <summary>Escribe bytes, sustituyendo el fichero de forma atómica.</summary>
    public static void WriteBytes(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = $"{path}.{Environment.ProcessId}.tmp";
        File.WriteAllBytes(tmp, bytes);
        Retry(() => File.Move(tmp, path, overwrite: true));
    }

    /// <summary>Añade una línea JSON compacta al final de un fichero .jsonl.</summary>
    public static void AppendLine<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var line = JsonSerializer.Serialize(value, CompactOptions) + "\n";
        using var _ = Lock("append-" + Path.GetFileName(path));
        Retry(() =>
        {
            using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            var bytes = Utf8NoBom.GetBytes(line);
            fs.Write(bytes, 0, bytes.Length);
        });
    }

    /// <summary>Lee todas las líneas válidas de un fichero .jsonl.</summary>
    public static List<T> ReadLines<T>(string path)
    {
        var result = new List<T>();
        if (!File.Exists(path)) return result;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            while (reader.ReadLine() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var item = JsonSerializer.Deserialize<T>(line, Options);
                    if (item is not null) result.Add(item);
                }
                catch (JsonException) { }
            }
        }
        catch (IOException) { }
        return result;
    }

    /// <summary>Toma un mutex con nombre para serializar el acceso entre procesos.</summary>
    public static IDisposable Lock(string name)
    {
        var mutex = new Mutex(false, @"Local\ClaudeStats-" + name.Replace('\\', '_'));
        var owned = false;
        try
        {
            owned = mutex.WaitOne(TimeSpan.FromSeconds(3));
        }
        catch (AbandonedMutexException)
        {
            owned = true;
        }
        return new MutexHandle(mutex, owned);
    }

    /// <summary>Anota un error en el log de la app sin propagar ningún fallo.</summary>
    public static void LogError(string context, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Data);
            File.AppendAllText(AppPaths.ErrorLog,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{context}] {ex.GetType().Name}: {ex.Message}\n", Utf8NoBom);
        }
        catch { }
    }

    /// <summary>Reintenta una operación de fichero que puede chocar con otro proceso.</summary>
    private static void Retry(Action action)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (attempt < 6 && ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(40 * attempt);
            }
        }
    }

    private sealed class MutexHandle(Mutex mutex, bool owned) : IDisposable
    {
        /// <summary>Libera el mutex si se llegó a tomar.</summary>
        public void Dispose()
        {
            if (owned) mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }
}
