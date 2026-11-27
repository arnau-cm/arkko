using System.Diagnostics;
using Arkko.Core;

namespace Arkko.Tests;

/// <remarks>Cada prueba monta su propio repositorio con git, en una carpeta temporal.</remarks>
public sealed class GitChangesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cs-git-" + Guid.NewGuid().ToString("N"));
    private readonly string _repo;

    public GitChangesTests()
    {
        _repo = Path.Combine(_root, "repo");
        Directory.CreateDirectory(_repo);
        Git("init", "-q", "-b", "main");
        Write("README.md", "hola\n");
        Write("src/a.cs", "uno\ndos\n");
        Commit("inicio");
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        // Los objetos de git son de solo lectura: hay que quitárselo para poder borrarlos.
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, recursive: true);
    }

    /// <summary>Lanza git en el repositorio de la prueba, o en otra carpeta, y devuelve lo que escribe.</summary>
    private string Git(params string[] args) => GitIn(_repo, args);

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

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_repo, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private void Commit(string message)
    {
        Git("add", "-A");
        Git("commit", "-q", "-m", message);
    }

    [Fact]
    public void Toda_la_rama_se_compara_con_develop_y_cuenta_lo_commiteado_lo_pendiente_y_lo_nuevo()
    {
        Git("branch", "develop");
        Git("switch", "-q", "-c", "DEVLAB-1");
        Write("src/a.cs", "uno\ndos\ntres\n");
        Commit("tres");
        Write("README.md", "adiós\n");
        Write("src/nuevo.cs", "a\nb\nc");

        var set = GitChanges.List(_repo, GitScope.Branch);

        Assert.Null(set.Error);
        Assert.Equal("DEVLAB-1", set.Branch);
        Assert.Equal("develop", set.BaseLabel);
        Assert.Equal(Git("rev-parse", "develop").Trim(), set.Base);
        Assert.Equal(Path.GetFullPath(_repo), set.Root);
        Assert.Equal(
            [("README.md", "modified", 1, 1), ("src/a.cs", "modified", 1, 0), ("src/nuevo.cs", "untracked", 3, 0)],
            set.Files.Select(f => (f.Path, f.Status, f.Added ?? -1, f.Deleted ?? -1)));
    }

    [Fact]
    public void Sin_develop_se_compara_con_main()
    {
        Git("switch", "-q", "-c", "feature");
        Write("src/a.cs", "uno\n");
        Commit("quita");

        var set = GitChanges.List(_repo, GitScope.Branch);

        Assert.Equal("main", set.BaseLabel);
        Assert.Equal([("src/a.cs", "modified")], set.Files.Select(f => (f.Path, f.Status)));
    }

    [Fact]
    public void Sin_rama_base_solo_cuenta_lo_no_commiteado()
    {
        Git("branch", "-m", "trunk");
        Write("src/a.cs", "otro\n");

        var set = GitChanges.List(_repo, GitScope.Branch);

        Assert.Null(set.Base);
        Assert.NotNull(set.Note);
        Assert.Equal(["src/a.cs"], set.Files.Select(f => f.Path));
    }

    [Fact]
    public void En_la_rama_base_sin_remoto_solo_cuenta_lo_no_commiteado()
    {
        Write("README.md", "otro\n");

        var set = GitChanges.List(_repo, GitScope.Branch);

        Assert.Equal("main", set.Branch);
        Assert.Null(set.Base);
        Assert.Contains("rama base", set.Note);
        Assert.Equal(["README.md"], set.Files.Select(f => f.Path));
    }

    [Fact]
    public void Lo_pendiente_es_solo_lo_no_commiteado()
    {
        Git("branch", "develop");
        Git("switch", "-q", "-c", "DEVLAB-2");
        Write("src/a.cs", "commiteado\n");
        Commit("commiteado");
        Write("README.md", "pendiente\n");
        Write("src/nuevo.cs", "x\n");

        var pending = GitChanges.List(_repo, GitScope.Pending);
        var branch = GitChanges.List(_repo, GitScope.Branch);

        Assert.Null(pending.Base);
        Assert.Equal(["README.md", "src/nuevo.cs"], pending.Files.Select(f => f.Path));
        Assert.Equal(["README.md", "src/a.cs", "src/nuevo.cs"], branch.Files.Select(f => f.Path));
    }

    [Fact]
    public void El_repositorio_entero_lleva_lo_versionado_y_lo_nuevo_sin_lo_ignorado_ni_lo_borrado()
    {
        Write(".gitignore", "bin/\n");
        Write("docs/guia.md", "guía\n");
        Commit("más ficheros");
        Write("bin/salida.dll", "x");
        Write("src/nuevo.cs", "nuevo\n");
        File.Delete(Path.Combine(_repo, "docs", "guia.md"));

        var set = GitChanges.ListAll(_repo);

        Assert.Null(set.Error);
        Assert.Equal("main", set.Branch);
        Assert.False(set.Truncated);
        Assert.Equal([".gitignore", "README.md", "src/a.cs", "src/nuevo.cs"], set.Files.Select(f => f.Path));
        Assert.All(set.Files, f => Assert.Equal("tracked", f.Status));
    }

    [Fact]
    public void El_repositorio_entero_fuera_de_un_repositorio_lo_dice_sin_fallar()
    {
        var outside = Path.Combine(_root, "suelta");
        Directory.CreateDirectory(outside);

        var set = GitChanges.ListAll(outside);

        Assert.NotNull(set.Error);
        Assert.Empty(set.Files);
    }

    [Fact]
    public void Buscar_texto_encuentra_en_versionados_y_nuevos_con_su_linea_y_sin_lo_ignorado()
    {
        Write(".gitignore", "bin/\n");
        Write("src/b.cs", "var Total = 1;\nvar subtotal = 2;\r\n");
        Commit("más");
        Write("src/nuevo.cs", "total nuevo\n");
        Write("bin/salida.txt", "total ignorado\n");

        var found = GitChanges.Grep(_repo, "total", matchCase: false, wholeWord: false, regex: false);

        Assert.Null(found.Error);
        Assert.False(found.Truncated);
        Assert.Equal(["src/b.cs:1:5:var Total = 1;", "src/b.cs:2:8:var subtotal = 2;", "src/nuevo.cs:1:1:total nuevo"],
            found.Hits.Select(h => $"{h.Path}:{h.Line}:{h.Column}:{h.Text}"));
    }

    [Fact]
    public void Buscar_texto_distingue_mayusculas_y_palabra_entera()
    {
        Write("src/b.cs", "Total\ntotal\nsubtotal\n");
        Commit("más");

        Assert.Equal([1], GitChanges.Grep(_repo, "Total", true, false, false).Hits.Select(h => h.Line));
        Assert.Equal([1, 2], GitChanges.Grep(_repo, "total", false, true, false).Hits.Select(h => h.Line));
    }

    [Fact]
    public void Buscar_texto_con_expresion_regular_y_tal_cual()
    {
        Write("src/b.cs", "a1\na.b\naxb\n");
        Commit("más");

        Assert.Equal([1], GitChanges.Grep(_repo, "a[0-9]", true, false, true).Hits.Select(h => h.Line));
        Assert.Equal([1], GitChanges.Grep(_repo, @"a(1|x)\b", false, false, true).Hits.Select(h => h.Line));
        Assert.Equal([2], GitChanges.Grep(_repo, "a.b", true, false, false).Hits.Select(h => h.Line));
        Assert.NotNull(GitChanges.Grep(_repo, "(", true, false, true).Error);
    }

    [Fact]
    public void Buscar_texto_que_empieza_por_guion_no_se_toma_por_una_opcion()
    {
        Write("src/b.cs", "x --version y\n");
        Commit("más");

        var found = GitChanges.Grep(_repo, "--version", true, false, false);

        Assert.Null(found.Error);
        Assert.Equal(["src/b.cs"], found.Hits.Select(h => h.Path));
    }

    [Fact]
    public void Buscar_texto_sin_coincidencias_no_es_un_fallo_y_se_corta_en_el_tope()
    {
        Assert.Empty(GitChanges.Grep(_repo, "nada-de-esto", false, false, false).Hits);
        Assert.Null(GitChanges.Grep(_repo, "nada-de-esto", false, false, false).Error);

        Write("src/mucho.txt", string.Concat(Enumerable.Repeat("eco\n", GitChanges.MaxGrepHits + 5)));
        var found = GitChanges.Grep(_repo, "eco", false, false, false);

        Assert.Equal(GitChanges.MaxGrepHits, found.Hits.Count);
        Assert.True(found.Truncated);
    }

    [Fact]
    public void Cuenta_los_commits_que_faltan_por_subir()
    {
        var remote = Path.Combine(_root, "remoto.git");
        GitIn(_root, "init", "-q", "--bare", remote);
        Git("remote", "add", "origin", remote);
        Git("switch", "-q", "-c", "DEVLAB-3");
        Git("push", "-q", "-u", "origin", "DEVLAB-3");
        Assert.Equal(0, GitChanges.List(_repo, GitScope.Pending).Ahead);

        Write("src/a.cs", "uno\n");
        Commit("uno");
        Write("src/a.cs", "dos\n");
        Commit("dos");

        var set = GitChanges.List(_repo, GitScope.Pending);
        Assert.Equal("origin/DEVLAB-3", set.Upstream);
        Assert.Equal(2, set.Ahead);

        // Una rama que aún no se ha publicado no sigue ninguna: no hay con qué contar.
        Git("switch", "-q", "-c", "DEVLAB-4");
        Assert.Null(GitChanges.List(_repo, GitScope.Pending).Upstream);
    }

    [Fact]
    public void Un_renombrado_y_un_borrado_dicen_lo_que_les_paso()
    {
        Git("branch", "develop");
        Git("switch", "-q", "-c", "DEVLAB-4");
        Git("mv", "src/a.cs", "src/renombrado.cs");
        Git("rm", "-q", "README.md");
        Commit("mueve");

        var set = GitChanges.List(_repo, GitScope.Branch);

        Assert.Equal(
            [("README.md", "deleted", (string?)null), ("src/renombrado.cs", "renamed", "src/a.cs")],
            set.Files.Select(f => (f.Path, f.Status, f.OldPath)));
    }

    [Fact]
    public void El_diff_de_un_fichero_va_de_la_base_a_lo_que_hay_en_disco()
    {
        var start = Git("rev-parse", "HEAD").Trim();
        Write("src/a.cs", "uno\nDOS\n");

        var diff = GitChanges.Diff(_repo, start, "src/a.cs");

        Assert.Null(diff.Error);
        Assert.Contains("-dos", diff.Text);
        Assert.Contains("+DOS", diff.Text);
        Assert.NotNull(GitChanges.Diff(_repo, "HEAD; rm -rf", "src/a.cs").Error);
        Assert.NotNull(GitChanges.Diff(_repo, start, "../fuera.txt").Error);
    }

    [Fact]
    public void Lee_un_fichero_del_repositorio_y_nada_de_fuera()
    {
        File.WriteAllText(Path.Combine(_root, "fuera.txt"), "secreto");
        Write("bin.dat", "a\0b");

        Assert.Equal("uno\ndos\n", GitChanges.ReadFile(_repo, "src/a.cs").Text);
        Assert.Equal("uno\ndos\n", GitChanges.ReadFile(_repo, @"src\a.cs").Text);
        Assert.Null(GitChanges.ReadFile(_repo, "../fuera.txt").Text);
        Assert.Null(GitChanges.ReadFile(_repo, Path.Combine(_root, "fuera.txt")).Text);
        Assert.Contains("binario", GitChanges.ReadFile(_repo, "bin.dat").Error);
        Assert.NotNull(GitChanges.ReadFile(_repo, "no-existe.cs").Error);
    }

    [Fact]
    public void Un_fichero_grande_se_corta_y_lo_dice()
    {
        Write("grande.txt", new string('x', GitChanges.MaxBytes + 10));

        var file = GitChanges.ReadFile(_repo, "grande.txt");

        Assert.True(file.Truncated);
        Assert.Equal(GitChanges.MaxBytes, file.Text!.Length);
    }

    [Fact]
    public void No_lee_a_traves_de_un_enlace_que_lleva_fuera()
    {
        var outside = Path.Combine(_root, "fuera");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secreto.txt"), "no");
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(_repo, "enlace"), outside);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Sin permiso para crear enlaces en este equipo, no hay nada que probar.
            return;
        }

        Assert.Null(GitChanges.ReadFile(_repo, "enlace/secreto.txt").Text);
    }

    [Fact]
    public void En_un_worktree_la_raiz_es_la_del_worktree()
    {
        Git("branch", "develop");
        var tree = Path.Combine(_root, "DEVLAB-5");
        Git("worktree", "add", "-q", "-b", "DEVLAB-5", tree, "develop");
        File.WriteAllText(Path.Combine(tree, "src", "a.cs"), "en el worktree\n");

        var set = GitChanges.List(Path.Combine(tree, "src"), GitScope.Branch);

        Assert.Null(set.Error);
        Assert.Equal(Path.GetFullPath(tree), set.Root);
        Assert.Equal("DEVLAB-5", set.Branch);
        Assert.Equal(["src/a.cs"], set.Files.Select(f => f.Path));
    }

    [Fact]
    public void Fuera_de_un_repositorio_lo_dice_sin_fallar()
    {
        var plain = Path.Combine(_root, "suelta");
        Directory.CreateDirectory(plain);

        Assert.NotNull(GitChanges.List(plain, GitScope.Branch).Error);
        Assert.NotNull(GitChanges.List(null, GitScope.Branch).Error);
        Assert.NotNull(GitChanges.List(Path.Combine(_root, "no-existe"), GitScope.Pending).Error);
    }
}
