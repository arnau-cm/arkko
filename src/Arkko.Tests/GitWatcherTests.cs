using System.Diagnostics;
using Arkko.Core;

namespace Arkko.Tests;

/// <remarks>Cada prueba monta su propio repositorio con git, en una carpeta temporal, y lo vigila con plazos cortos.</remarks>
public sealed class GitWatcherTests : IDisposable
{
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan Longest = TimeSpan.FromMilliseconds(800);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "cs-watch-" + Guid.NewGuid().ToString("N"));
    private readonly string _repo;
    private readonly List<GitWatcher> _watchers = [];
    private int _changes;

    public GitWatcherTests()
    {
        _repo = Path.Combine(_root, "repo");
        Directory.CreateDirectory(_repo);
        Git("init", "-q", "-b", "main");
        Write("src/a.cs", "uno\n");
        Git("add", "-A");
        Git("commit", "-q", "-m", "inicio");
    }

    public void Dispose()
    {
        _watchers.ForEach(w => w.Dispose());
        if (!Directory.Exists(_root)) return;
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, recursive: true);
    }

    private void Git(params string[] args)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = _repo, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in (string[])["-c", "user.name=Prueba", "-c", "user.email=prueba@example.com", "-c", "commit.gpgsign=false", .. args]) info.ArgumentList.Add(arg);
        using var git = Process.Start(info)!;
        var error = git.StandardError.ReadToEnd();
        git.StandardOutput.ReadToEnd();
        git.WaitForExit();
        Assert.True(git.ExitCode == 0, $"git {string.Join(' ', args)}: {error}");
    }

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_repo, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    /// <summary>Vigila una carpeta, contando los avisos, con los plazos cortos de las pruebas.</summary>
    private GitWatcher Watch(string folder, TimeSpan? pollEvery = null)
    {
        var watcher = new GitWatcher(folder, Quiet, Longest, pollEvery ?? TimeSpan.FromMinutes(1));
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
        while (Volatile.Read(ref _changes) == 0 && DateTime.UtcNow < until) Thread.Sleep(50);
        return Volatile.Read(ref _changes);
    }

    [Fact]
    public void Avisa_al_cambiar_un_fichero_del_repositorio()
    {
        Watch(_repo);
        Write("src/a.cs", "uno\ndos\n");
        Assert.True(WaitForChanges() > 0);
    }

    [Fact]
    public void Avisa_al_hacer_un_commit()
    {
        Write("src/b.cs", "b\n");
        Git("add", "-A");
        Watch(_repo);
        Git("commit", "-q", "-m", "otro");
        Assert.True(WaitForChanges() > 0);
    }

    [Fact]
    public void Vigila_tambien_desde_una_subcarpeta()
    {
        var watcher = Watch(Path.Combine(_repo, "src"));
        Assert.Equal(Path.GetFullPath(_repo).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(watcher.Root!).TrimEnd(Path.DirectorySeparatorChar));
        Write("raiz.txt", "x\n");
        Assert.True(WaitForChanges() > 0);
    }

    [Fact]
    public void No_avisa_por_lo_interno_de_git_ni_por_node_modules()
    {
        Watch(_repo);
        Write(".git/objects/prueba", "x");
        Write("node_modules/paquete/index.js", "x");
        Write(".vs/estado", "x");
        // Mirar lo cambiado, como hace la app, toca lo interno de git: no debe contar como cambio.
        Git("status", "--porcelain");
        Thread.Sleep(1000);
        Assert.Equal(0, Volatile.Read(ref _changes));
    }

    [Fact]
    public void Junta_en_un_aviso_los_cambios_seguidos()
    {
        Watch(_repo);
        for (var i = 0; i < 20; i++)
        {
            Write($"src/f{i}.cs", "x\n");
            Thread.Sleep(10);
        }
        WaitForChanges();
        Thread.Sleep(Quiet * 4);
        Assert.Equal(1, Volatile.Read(ref _changes));
    }

    [Fact]
    public void Una_tanda_larga_avisa_sin_esperar_a_que_acabe()
    {
        Watch(_repo);
        var stop = DateTime.UtcNow + Longest * 3;
        var i = 0;
        while (DateTime.UtcNow < stop)
        {
            Write($"src/g{i++ % 5}.cs", $"{i}\n");
            Thread.Sleep(50);
        }
        // Siguiendo los cambios sin parar, ha avisado más de una vez: cada vez que se cumplía el máximo de la tanda.
        Assert.True(Volatile.Read(ref _changes) >= 2);
    }

    [Fact]
    public void Fuera_de_un_repositorio_no_vigila_ni_falla()
    {
        var outside = Path.Combine(_root, "suelta");
        Directory.CreateDirectory(outside);
        using var watcher = new GitWatcher(outside);
        Assert.Null(watcher.Root);
        Assert.False(watcher.Polling);
    }

    [Fact]
    public void Si_no_puede_vigilar_avisa_cada_poco()
    {
        var watcher = Watch(_repo, TimeSpan.FromMilliseconds(200));
        watcher.ForcePolling();
        Assert.True(watcher.Polling);
        Assert.True(WaitForChanges(3) > 0);
    }
}
