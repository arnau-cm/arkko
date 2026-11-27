using Arkko.Core;

namespace Arkko.Tests;

/// <remarks>Cada prueba monta la carpeta de un proyecto con el transcript de una sesión, en una carpeta temporal, y lo
/// vigila con plazos cortos.</remarks>
public sealed class TranscriptWatcherTests : IDisposable
{
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan Longest = TimeSpan.FromMilliseconds(500);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "cs-transcript-" + Guid.NewGuid().ToString("N"));
    private readonly string _transcript;
    private readonly List<TranscriptWatcher> _watchers = [];
    private int _changes;

    public TranscriptWatcherTests()
    {
        Directory.CreateDirectory(_folder);
        _transcript = Path.Combine(_folder, "sesion.jsonl");
        File.WriteAllText(_transcript, "{}\n");
    }

    public void Dispose()
    {
        _watchers.ForEach(w => w.Dispose());
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    /// <summary>Añade una línea a un fichero de la carpeta del proyecto, creándolo si hace falta.</summary>
    private void Append(string relative)
    {
        var path = Path.Combine(_folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.AppendAllText(path, "{\"type\":\"assistant\"}\n");
    }

    /// <summary>Vigila el transcript, contando los avisos, con los plazos cortos de las pruebas.</summary>
    private TranscriptWatcher Watch(TimeSpan? pollEvery = null)
    {
        var watcher = new TranscriptWatcher(_transcript, Quiet, Longest, pollEvery ?? TimeSpan.FromMinutes(1));
        watcher.Changed += () => Interlocked.Increment(ref _changes);
        _watchers.Add(watcher);
        // El sistema tarda un poco en empezar a mandar avisos de una carpeta recién vigilada.
        Thread.Sleep(200);
        return watcher;
    }

    /// <summary>Espera a que haya al menos un aviso, como mucho unos segundos; devuelve cuántos ha habido.</summary>
    private int WaitForChanges(int seconds = 5)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (Volatile.Read(ref _changes) == 0 && DateTime.UtcNow < until) Thread.Sleep(20);
        return Volatile.Read(ref _changes);
    }

    [Fact]
    public void Avisa_al_escribir_en_el_transcript()
    {
        Watch();
        Append("sesion.jsonl");
        Assert.True(WaitForChanges() > 0);
    }

    [Fact]
    public void Avisa_al_escribir_un_subagente_de_la_sesion()
    {
        Watch();
        Append("sesion/subagents/agent-1.jsonl");
        Assert.True(WaitForChanges() > 0);
    }

    [Fact]
    public void No_avisa_por_otra_sesion_de_la_misma_carpeta()
    {
        Watch();
        Append("otra.jsonl");
        Append("otra/subagents/agent-1.jsonl");
        Thread.Sleep(800);
        Assert.Equal(0, Volatile.Read(ref _changes));
    }

    [Fact]
    public void Junta_en_un_aviso_las_escrituras_seguidas()
    {
        Watch();
        for (var i = 0; i < 10; i++) Append("sesion.jsonl");
        WaitForChanges();
        Thread.Sleep(600);
        Assert.InRange(Volatile.Read(ref _changes), 1, 2);
    }

    [Fact]
    public void Avisa_a_tiempo_aunque_no_paren_de_llegar_escrituras()
    {
        Watch();
        var started = DateTime.UtcNow;
        while (Volatile.Read(ref _changes) == 0 && DateTime.UtcNow - started < TimeSpan.FromSeconds(3))
        {
            Append("sesion.jsonl");
            Thread.Sleep(40);
        }
        Assert.True(Volatile.Read(ref _changes) > 0);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(1.5));
    }

    [Fact]
    public void Sin_poder_vigilar_avisa_cada_poco()
    {
        var watcher = Watch(TimeSpan.FromMilliseconds(200));
        watcher.ForcePolling();
        Assert.True(watcher.Polling);
        Assert.True(WaitForChanges() > 0);
    }
}
