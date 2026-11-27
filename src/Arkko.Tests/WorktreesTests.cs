using System.Diagnostics;
using Arkko.Core;

namespace Arkko.Tests;

/// <remarks>Cada prueba monta su remoto y su repositorio con git, en una carpeta temporal.</remarks>
public sealed class WorktreesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cs-worktrees-" + Guid.NewGuid().ToString("N"));
    private readonly string _origin;
    private readonly string _app;

    public WorktreesTests()
    {
        _origin = Path.Combine(_root, "origin.git");
        _app = Path.Combine(_root, "app");
        Directory.CreateDirectory(_root);
        GitIn(_root, "init", "-q", "--bare", "-b", "develop", _origin);
        GitIn(_root, "clone", "-q", _origin, _app);
        Write(_app, "README.md", "hola\n");
        Commit(_app, "inicio");
        GitIn(_app, "push", "-q", "origin", "develop");
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        // Los objetos de git son de solo lectura: hay que quitárselo para poder borrarlos.
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, recursive: true);
    }

    private static string GitIn(string dir, params string[] args)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in (string[])["-c", "user.name=Prueba", "-c", "user.email=prueba@example.com", "-c", "commit.gpgsign=false", .. args]) info.ArgumentList.Add(arg);
        using var git = Process.Start(info)!;
        var output = git.StandardOutput.ReadToEnd();
        var error = git.StandardError.ReadToEnd();
        git.WaitForExit();
        Assert.True(git.ExitCode == 0, $"git {string.Join(' ', args)}: {error}");
        return output;
    }

    private static void Write(string dir, string relative, string text) => File.WriteAllText(Path.Combine(dir, relative), text);

    private static void Commit(string dir, string message)
    {
        GitIn(dir, "add", "-A");
        GitIn(dir, "commit", "-q", "-m", message);
    }

    /// <summary>Crea el worktree de una rama nueva junto al repositorio, con un commit subido y su PR publicada.</summary>
    private string Branch(string name, int pr)
    {
        var dir = Path.Combine(_root, name);
        GitIn(_app, "worktree", "add", "-q", "-b", name, dir);
        Write(dir, name + ".txt", name + "\n");
        Commit(dir, name);
        GitIn(dir, "push", "-q", "-u", "origin", name);
        GitIn(dir, "push", "-q", "origin", $"{name}:refs/pull/{pr}/head");
        return dir;
    }

    [Fact]
    public void Cada_worktree_dice_lo_que_tiene_sin_commit_y_sin_subir_y_si_su_PR_esta_abierta_o_mergeada()
    {
        Branch("DEVLAB-1", 1);
        var open = Branch("DEVLAB-2", 2);
        Write(open, "mas.txt", "más\n");
        Commit(open, "más");
        Write(open, "suelto.txt", "sin commit\n");
        var gone = Path.Combine(_root, "DEVLAB-3");
        GitIn(_app, "worktree", "add", "-q", "-b", "DEVLAB-3", gone);
        Directory.Delete(gone, recursive: true);

        // La PR se mergea desde otro clon: el repositorio aún no tiene ese commit de develop y tiene que traerlo.
        var other = Path.Combine(_root, "otro");
        GitIn(_root, "clone", "-q", _origin, other);
        GitIn(other, "merge", "-q", "--no-ff", "-m", "Merge DEVLAB-1", "origin/DEVLAB-1");
        GitIn(other, "push", "-q", "origin", "develop");

        var repo = Assert.Single(Worktrees.Read([open]));

        Assert.Equal("app", repo.Name);
        Assert.Equal("develop", repo.BaseBranch);
        Assert.Null(repo.RemoteError);
        Assert.Equal(["app", "DEVLAB-1", "DEVLAB-2", "DEVLAB-3"], repo.Worktrees.Select(w => w.Folder));

        var develop = repo.Worktrees[0];
        Assert.True(develop.IsBase);
        Assert.False(develop.Leftover(taskClosed: true));

        var merged = repo.Worktrees[1];
        Assert.Equal((1, PrState.Merged), (merged.Pr, merged.PrState));
        Assert.True(merged.Leftover(taskClosed: false));

        var working = repo.Worktrees[2];
        Assert.Equal((2, PrState.Open), (working.Pr, working.PrState));
        Assert.Equal((1, "origin/DEVLAB-2", 1), (working.Pending, working.Upstream, working.Ahead));
        Assert.False(working.Leftover(taskClosed: false));
        Assert.True(working.Leftover(taskClosed: true));

        var missing = repo.Worktrees[3];
        Assert.True(missing.Missing);
        Assert.True(missing.Leftover(taskClosed: false));
    }

    [Fact]
    public void Una_rama_sin_PR_ni_subir_no_tiene_PR_y_no_sobra()
    {
        var dir = Path.Combine(_root, "DEVLAB-4");
        GitIn(_app, "worktree", "add", "-q", "-b", "DEVLAB-4", dir);

        var item = Assert.Single(Worktrees.Read([dir])).Worktrees.Single(w => w.Folder == "DEVLAB-4");

        Assert.Equal((null, PrState.None, null, 0), (item.Pr, item.PrState, item.Upstream, item.Pending));
        Assert.False(item.Leftover(taskClosed: false));
    }

    [Fact]
    public void Su_carpeta_de_git_lleva_al_repositorio_como_cualquiera_de_sus_worktrees()
    {
        var dir = Path.Combine(_root, "DEVLAB-5");
        GitIn(_app, "worktree", "add", "-q", "-b", "DEVLAB-5", dir);

        var fromTree = Assert.Single(Worktrees.Read([dir], remote: false));
        var fromCommon = Assert.Single(Worktrees.Read([fromTree.CommonDir], remote: false));

        Assert.Equal(fromTree.CommonDir, fromCommon.CommonDir);
        Assert.Equal(fromTree.Worktrees.Select(w => w.Folder), fromCommon.Worktrees.Select(w => w.Folder));
    }

    [Fact]
    public void Cada_repositorio_dice_el_subtablero_de_su_arkko_stack_y_el_worktree_que_lo_declara_gana()
    {
        var dir = Path.Combine(_root, "DEVLAB-6");
        GitIn(_app, "worktree", "add", "-q", "-b", "DEVLAB-6", dir);
        Write(dir, ".arkko.json", "{ \"panel\": { \"projectKey\": \"DEVLAB\", \"board\": \"TIENDA\" } }");
        var plain = Path.Combine(_root, "suelta");
        Directory.CreateDirectory(plain);
        GitIn(plain, "init", "-q");

        var boards = RepoBoards.Read([_app, dir, Path.Combine(_root, "no-existe"), plain]);

        Assert.Equal("TIENDA", boards["app"]);
        Assert.True(boards.ContainsKey("suelta"));
        Assert.Null(boards["suelta"]);
        Assert.Equal(2, boards.Count);
    }

    [Fact]
    public void Un_arkko_stack_sin_board_o_roto_no_declara_subtablero()
    {
        Write(_app, "a.json", "{ \"panel\": { \"projectKey\": \"DEVLAB\" } }");
        Write(_app, "b.json", "{ roto");

        Assert.Null(RepoBoards.BoardIn(Path.Combine(_app, "a.json")));
        Assert.Null(RepoBoards.BoardIn(Path.Combine(_app, "b.json")));
        Assert.Null(RepoBoards.BoardIn(Path.Combine(_app, "no-existe.json")));
    }

    [Fact]
    public void Las_carpetas_de_las_sesiones_salen_del_principio_de_cada_transcript()
    {
        var projects = Path.Combine(_root, "projects");
        Directory.CreateDirectory(Path.Combine(projects, "p1"));
        Directory.CreateDirectory(Path.Combine(projects, "p2"));
        var cwd = _app.Replace("\\", "\\\\");
        File.WriteAllText(Path.Combine(projects, "p1", "a.jsonl"), "{\"type\":\"summary\"}\n{\"cwd\":\"" + cwd + "\",\"type\":\"user\"}\n");
        File.WriteAllText(Path.Combine(projects, "p1", "b.jsonl"), "{\"cwd\":\"" + cwd + "\"}\n");
        File.WriteAllText(Path.Combine(projects, "p2", "c.jsonl"), "{\"type\":\"summary\"}\n");

        Assert.Equal([_app], SessionCatalog.Folders(projects));
    }

    [Fact]
    public void Un_clon_con_un_solo_worktree_no_sale()
    {
        Assert.Empty(Worktrees.Read([_app, Path.Combine(_app, "no-existe"), null, ""]));
    }

    [Fact]
    public void Una_carpeta_borrada_vale_si_la_de_encima_es_un_repositorio_con_bare()
    {
        var bare = Path.Combine(_root, "tienda", ".bare");
        Directory.CreateDirectory(bare);

        Assert.Equal(Path.GetFullPath(bare), Worktrees.CommonDirOf(Path.Combine(_root, "tienda", "DEVLAB-9")));
        Assert.Null(Worktrees.CommonDirOf(Path.Combine(_root, "suelta", "DEVLAB-9")));
    }

    [Fact]
    public void La_lista_de_git_distingue_el_bare_las_ramas_los_commits_sueltos_y_las_carpetas_borradas()
    {
        var entries = Worktrees.ParseList(
            "worktree C:/r/tienda/.bare\nbare\n\n" +
            "worktree C:/r/tienda/develop\nHEAD aaa\nbranch refs/heads/develop\n\n" +
            "worktree C:/r/tienda/suelto\nHEAD bbb\ndetached\n\n" +
            "worktree C:/r/tienda/DEVLAB-3\nHEAD ccc\nbranch refs/heads/feature/DEVLAB-3\nprunable gitdir file points to non-existent location\n");

        Assert.Equal(
            [(true, null, false), (false, "develop", false), (false, null, false), (false, "feature/DEVLAB-3", true)],
            entries.Select(e => (e.Bare, e.Branch, e.Prunable)));
        Assert.Equal(Path.GetFullPath("C:/r/tienda/develop"), entries[1].Path);
    }

    [Fact]
    public void De_cada_commit_cuenta_la_PR_de_numero_mas_alto()
    {
        var refs = Worktrees.ParseRemote("aaa\trefs/heads/develop\naaa\trefs/pull/3/head\naaa\trefs/pull/12/head\nbbb\trefs/pull/4/merge\n");

        Assert.Equal(12, refs.PullBySha["aaa"]);
        Assert.False(refs.PullBySha.ContainsKey("bbb"));
        Assert.Equal("aaa", refs.Heads["develop"]);
    }

    [Theory]
    [InlineData("https://github.com/acme/arkko.git", "https://github.com/acme/arkko/pull/7")]
    [InlineData("https://usuario@github.com/acme/arkko", "https://github.com/acme/arkko/pull/7")]
    [InlineData("git@github.com:acme/arkko.git", "https://github.com/acme/arkko/pull/7")]
    [InlineData("ssh://git@github.com/acme/arkko.git", "https://github.com/acme/arkko/pull/7")]
    [InlineData("https://acme@dev.azure.com/acme/Tienda/_git/Tienda", "https://dev.azure.com/acme/Tienda/_git/Tienda/pullrequest/7")]
    [InlineData("git@ssh.dev.azure.com:v3/acme/Tienda/Tienda", "https://dev.azure.com/acme/Tienda/_git/Tienda/pullrequest/7")]
    [InlineData("https://dev.azure.com/acme/Mi%20proyecto/_git/app", "https://dev.azure.com/acme/Mi%20proyecto/_git/app/pullrequest/7")]
    [InlineData("http://git-interno:3000/ACME/Tienda.git", "http://git-interno:3000/ACME/Tienda/pulls/7")]
    [InlineData("https://usuario@git.ejemplo.local/ACME/Tienda", "https://git.ejemplo.local/ACME/Tienda/pulls/7")]
    public void La_pagina_de_una_PR_sale_de_la_direccion_del_remoto(string remote, string expected) =>
        Assert.Equal(expected, Worktrees.PullUrl(remote, 7));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("git@git-interno:ACME/Tienda.git")]
    [InlineData("C:/repos/origin.git")]
    [InlineData("https://github.com/acme")]
    [InlineData("https://dev.azure.com/acme/Tienda")]
    public void Sin_saber_como_es_la_web_del_remoto_no_hay_pagina_de_PR(string? remote) =>
        Assert.Null(Worktrees.PullUrl(remote, 7));
}
