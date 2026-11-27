using Arkko.Core;

namespace Arkko.Tests;

public sealed class WorktreeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cs-worktree-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    /// <summary>Crea la carpeta con un fichero .git que apunta a gitdir.</summary>
    private string Linked(string relative, string gitdir)
    {
        var dir = Path.Combine(_root, relative);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, ".git"), "gitdir: " + gitdir + "\n");
        return dir;
    }

    [Fact]
    public void Un_worktree_con_el_repositorio_en_bare_da_la_carpeta_padre()
    {
        var dir = Linked(@"tienda\develop", Path.Combine(_root, @"tienda\.bare\worktrees\develop").Replace('\\', '/'));
        Assert.Equal("tienda", Worktree.RepoOf(dir));
        Assert.Equal("tienda", Worktree.RepoOf(dir + "\\"));
    }

    [Fact]
    public void Un_worktree_junto_al_repositorio_da_el_nombre_del_repositorio_aunque_el_gitdir_sea_relativo()
    {
        var dir = Linked("tienda-DEVLAB-12", "../tienda/.git/worktrees/tienda-DEVLAB-12");
        Assert.Equal("tienda", Worktree.RepoOf(dir));
    }

    [Fact]
    public void Un_repositorio_bare_suelto_pierde_el_sufijo_git()
    {
        var dir = Linked("DEVLAB-7", Path.Combine(_root, @"taller.git\worktrees\DEVLAB-7"));
        Assert.Equal("taller", Worktree.RepoOf(dir));
    }

    [Fact]
    public void Una_carpeta_normal_un_submodulo_o_una_ruta_que_no_existe_no_dan_repositorio()
    {
        var normal = Path.Combine(_root, "normal");
        Directory.CreateDirectory(Path.Combine(normal, ".git"));
        Assert.Null(Worktree.RepoOf(normal));

        Assert.Null(Worktree.RepoOf(Linked(@"app\lib", "../.git/modules/lib")));
        Assert.Null(Worktree.RepoOf(Path.Combine(_root, "no-existe")));
        Assert.Null(Worktree.RepoOf(null));
    }

    [Fact]
    public void El_titulo_junta_repositorio_y_carpeta_solo_en_un_worktree()
    {
        Assert.Equal("tienda · develop",new SessionState { Project = "develop", Repo = "tienda" }.Title);
        Assert.Equal("claude-stats", new SessionState { Project = "claude-stats" }.Title);
        Assert.Null(new SessionState().Title);
    }
}
