using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Arkko.Core;

/// <summary>Lo que la app necesita de un transcript sin leerlo entero cada vez: carpeta, primer prompt, título, prompts, días con actividad, recap y modelos.</summary>
/// <remarks>
/// Cada transcript se lee una vez, a trozos y como bytes, y después solo lo que Claude Code le añade: un transcript
/// solo crece. Las líneas se miran sin convertirlas en texto, y solo se interpretan las que llevan algo que
/// interesa; las de herramientas y adjuntos, que son casi todas y pueden pesar megas por sus imágenes, se saltan.
/// Leerlos enteros como texto pedía cientos de MB de golpe, que .NET tardaba horas en devolver.
/// Si el fichero encoge o cambia su principio, se ha reescrito y se vuelve a leer desde el inicio.
/// </remarks>
internal sealed class TranscriptIndex
{
    // Lo que se lee de una vez; una línea más larga agranda el trozo lo que haga falta.
    private const int Chunk = 1 << 20;

    // Bytes del principio del fichero con que se reconoce que sigue siendo el mismo.
    private const int HeadCheck = 64;

    private static readonly ConcurrentDictionary<string, TranscriptIndex> Indexes = new(StringComparer.OrdinalIgnoreCase);

    private long _offset;
    private byte[] _head = [];
    private string? _cwd, _firstPrompt, _custom, _ai;
    private readonly List<string> _prompts = [];
    private readonly HashSet<DateTime> _days = [];
    private (string Text, DateTime Utc)? _recap;
    private readonly Dictionary<(string Model, string? Effort), int> _models = [];

    /// <summary>Lo que se sabe de un transcript, puesto al día con lo que tenga de nuevo.</summary>
    /// <exception cref="IOException">Si no se puede leer.</exception>
    public static Snapshot Of(string path)
    {
        var index = Indexes.GetOrAdd(path, _ => new TranscriptIndex());
        lock (index)
        {
            index.Update(path);
            return new Snapshot(index._cwd, index._firstPrompt, index._custom ?? index._ai, index._prompts.ToList(), index._days.ToHashSet(), index._recap,
                index._models.Select(m => new ModelUse(m.Key.Model, m.Key.Effort, m.Value)).ToList());
        }
    }

    /// <summary>Olvida lo leído de un transcript, como al borrar su sesión.</summary>
    public static void Forget(string path) => Indexes.TryRemove(path, out _);

    /// <summary>Lo que se sabe de un transcript en un momento.</summary>
    /// <param name="Title">El título que puso la persona, o si no el que puso Claude Code; null si ninguno.</param>
    /// <param name="Models">Cada modelo y esfuerzo con que respondió la sesión principal, con cuántas veces.</param>
    public sealed record Snapshot(string? Cwd, string? FirstPrompt, string? Title, List<string> Prompts, HashSet<DateTime> Days,
        (string Text, DateTime Utc)? Recap, List<ModelUse> Models);

    /// <summary>Lee lo nuevo del fichero desde la última vez, o todo si se ha reescrito.</summary>
    private void Update(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
        if (fs.Length < _offset || !SameHead(fs)) Reset();
        if (fs.Length <= _offset) return;
        if (_offset == 0) _head = ReadHead(fs);
        fs.Seek(_offset, SeekOrigin.Begin);

        var buffer = ArrayPool<byte>.Shared.Rent(Chunk);
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
                var from = 0;
                int newline;
                while ((newline = Array.IndexOf(buffer, (byte)'\n', from, filled - from)) >= 0)
                {
                    Take(buffer.AsMemory(from, newline - from));
                    from = newline + 1;
                }
                _offset += from;
                Buffer.BlockCopy(buffer, from, buffer, 0, filled - from);
                filled -= from;
            }
            // Una última línea sin salto se toma si ya es JSON entero: una a medio escribir no lo es, y se lee la próxima vez.
            if (filled > 0 && IsJson(buffer.AsMemory(0, filled)))
            {
                Take(buffer.AsMemory(0, filled));
                _offset += filled;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Si unos bytes son un JSON entero.</summary>
    private static bool IsJson(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Span.Trim(" \t\r"u8).IsEmpty) return false;
        try
        {
            using var _ = JsonDocument.Parse(bytes);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Si el principio del fichero es el mismo que al leerlo la primera vez.</summary>
    private bool SameHead(FileStream fs)
    {
        if (_offset == 0) return true;
        return ReadHead(fs, _head.Length).AsSpan().SequenceEqual(_head);
    }

    /// <summary>Los primeros bytes del fichero, con que se reconoce después; como mucho <paramref name="count"/>.</summary>
    private static byte[] ReadHead(FileStream fs, int count = HeadCheck)
    {
        fs.Seek(0, SeekOrigin.Begin);
        var head = new byte[Math.Min(count, fs.Length)];
        var read = 0;
        while (read < head.Length && fs.Read(head, read, head.Length - read) is var n && n > 0) read += n;
        return head;
    }

    /// <summary>Vuelve a empezar, para leer el fichero desde el principio.</summary>
    private void Reset()
    {
        _offset = 0;
        _head = [];
        _cwd = _firstPrompt = _custom = _ai = null;
        _prompts.Clear();
        _days.Clear();
        _recap = null;
        _models.Clear();
    }

    /// <summary>Saca de una línea lo que interesa; la mira como bytes y solo la interpreta si lleva algo que hace falta.</summary>
    private void Take(ReadOnlyMemory<byte> memory)
    {
        var line = memory.Span;
        TakeDay(line);
        if (line.IndexOf("\"type\":\"assistant\""u8) >= 0) TakeModel(line);

        var user = line.IndexOf("\"type\":\"user\""u8) >= 0;
        var tool = user && line.IndexOf("\"tool_result\""u8) >= 0;
        var needCwd = _cwd is null && line.IndexOf("\"cwd\""u8) >= 0;
        var title = line.IndexOf("-title\""u8) >= 0;
        var recap = line.IndexOf("\"away_summary\""u8) >= 0;
        // Un resultado de herramienta no es un prompt, y suele ser lo que pesa: solo se interpreta si aún falta la carpeta.
        if (!(user && !tool) && !needCwd && !title && !recap) return;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(memory);
        }
        catch (JsonException)
        {
            return;
        }
        using (doc)
        {
            var e = doc.RootElement;
            if (e.ValueKind != JsonValueKind.Object) return;
            _cwd ??= TranscriptReader.Str(e, "cwd");
            if (user && !tool) TakePrompt(e, line);
            switch (TranscriptReader.Str(e, "type"))
            {
                case "custom-title": _custom = TranscriptReader.Str(e, "customTitle") ?? _custom; break;
                case "ai-title": _ai = TranscriptReader.Str(e, "aiTitle") ?? _ai; break;
            }
            if (recap && TranscriptReader.Str(e, "subtype") == "away_summary" && TranscriptReader.Str(e, "content") is { Length: > 0 } text)
                _recap = (text.Trim(), TranscriptReader.Timestamp(e) ?? DateTime.MinValue);
        }
    }

    /// <summary>Apunta el prompt o el comando de una entrada de la persona: el primero, para la lista, y todos, para el buscador.</summary>
    /// <remarks>Para el buscador no cuentan los mensajes de sistema ni el resumen de una compactación, que no los escribió la persona.</remarks>
    private void TakePrompt(JsonElement e, ReadOnlySpan<byte> line)
    {
        if (!e.TryGetProperty("message", out var msg) || !msg.TryGetProperty("content", out var content)) return;
        if (SessionTraceReader.PromptText(content) is not { } text || SessionTraceReader.UserStep(text) is not { } step) return;
        if (step.Kind is not (TraceKind.Prompt or TraceKind.Command)) return;
        if (_firstPrompt is null && !TranscriptReader.IsSystem(e)) _firstPrompt = SessionTraceReader.Preview(step.Text ?? step.Title);
        if (line.IndexOf("\"isMeta\":true"u8) >= 0 || line.IndexOf("\"isCompactSummary\":true"u8) >= 0) return;
        _prompts.Add(step.Kind == TraceKind.Command ? step.Title : text);
    }

    /// <summary>Cuenta el modelo y el esfuerzo de una respuesta de Claude, sin interpretar la línea.</summary>
    /// <remarks>Las respuestas sintéticas («&lt;synthetic&gt;») no pasan por ningún modelo y no cuentan.</remarks>
    private void TakeModel(ReadOnlySpan<byte> line)
    {
        // El de la respuesta es el primero dentro de «message»; los que vienen después son de lo que se pide a un agente.
        var message = line.IndexOf("\"message\":{"u8);
        if (message < 0 || Value(line[message..], "\"model\":\""u8) is not { } model || model.StartsWith('<')) return;
        var key = (Format.ModelName(model), Value(line, "\"perTurnEffort\":\""u8));
        _models[key] = _models.GetValueOrDefault(key) + 1;
    }

    /// <summary>Texto que sigue a una clave en una línea, hasta las comillas que lo cierran; null si no está o es raro.</summary>
    private static string? Value(ReadOnlySpan<byte> line, ReadOnlySpan<byte> key)
    {
        var at = line.IndexOf(key);
        if (at < 0) return null;
        var rest = line[(at + key.Length)..];
        var end = rest.IndexOf((byte)'"');
        return end is > 0 and <= 80 ? Encoding.UTF8.GetString(rest[..end]) : null;
    }

    /// <summary>Apunta el día local de la hora de una línea, si la lleva; sin interpretar la línea.</summary>
    private void TakeDay(ReadOnlySpan<byte> line)
    {
        var key = "\"timestamp\":\""u8;
        var at = line.IndexOf(key);
        if (at < 0) return;
        var rest = line[(at + key.Length)..];
        var end = rest.IndexOf((byte)'"');
        if (end <= 0 || end > 40) return;
        Span<char> chars = stackalloc char[40];
        var count = Encoding.UTF8.GetChars(rest[..end], chars);
        if (DateTime.TryParse(chars[..count], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var utc))
            _days.Add(utc.ToLocalTime().Date);
    }
}
