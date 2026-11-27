using System.Buffers;
using System.Text.Json;

namespace Arkko.Core;

/// <summary>Lectura de ficheros JSON Lines que crecen, como los transcripts: desde un byte y solo las líneas enteras.</summary>
internal static class JsonLines
{
    /// <summary>Dice, por sus bytes, si una línea interesa.</summary>
    public delegate bool Filter(ReadOnlySpan<byte> line);

    /// <summary>Cada línea JSON entera de un fichero desde un byte, interpretada desde sus bytes; las que no se entienden se saltan.</summary>
    /// <remarks>
    /// La última línea se deja si no termina en salto: Claude Code puede estar escribiéndola. Si el fichero ha
    /// encogido, se lee desde el principio. Se lee a trozos y sin pasar a texto: lo nuevo de un transcript puede
    /// ser de decenas de MB, y leerlo entero, pasarlo a texto y partirlo en líneas pedía varias veces eso de golpe.
    /// Cada línea se entrega sin copiarla, así que solo vale hasta pedir la siguiente.
    /// </remarks>
    /// <param name="done">Recibe, al acabar, el byte desde el que seguir la próxima vez.</param>
    /// <param name="keep">Si se da, solo se interpretan las líneas cuyos bytes lo cumplen: mirar unos bytes cuesta mucho
    /// menos que interpretar el JSON de cada línea.</param>
    public static IEnumerable<JsonElement> Complete(string path, long from, Action<long>? done = null, Filter? keep = null)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
        if (from > fs.Length) from = 0;
        fs.Seek(from, SeekOrigin.Begin);
        var buffer = ArrayPool<byte>.Shared.Rent(1 << 20);
        var at = from;
        try
        {
            var filled = 0;
            while (true)
            {
                if (filled == buffer.Length)
                {
                    var bigger = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                    Buffer.BlockCopy(buffer, 0, bigger, 0, filled);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = bigger;
                }
                var read = fs.Read(buffer, filled, buffer.Length - filled);
                if (read == 0) break;
                filled += read;
                var start = 0;
                int newline;
                while ((newline = Array.IndexOf(buffer, (byte)'\n', start, filled - start)) >= 0)
                {
                    var line = buffer.AsMemory(start, newline - start);
                    start = newline + 1;
                    if (line.Length < 2) continue;
                    if (keep is not null && !keep(line.Span)) continue;
                    JsonDocument doc;
                    try
                    {
                        doc = JsonDocument.Parse(line);
                    }
                    catch (JsonException)
                    {
                        continue;
                    }
                    using (doc) yield return doc.RootElement;
                }
                at += start;
                Buffer.BlockCopy(buffer, start, buffer, 0, filled - start);
                filled -= start;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
        done?.Invoke(at);
    }
}
