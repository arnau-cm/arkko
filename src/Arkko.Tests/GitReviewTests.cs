using System.Diagnostics;
using Arkko.Core;

namespace Arkko.Tests;

/// <remarks>Cada prueba monta su propio repositorio con git, en una carpeta temporal.</remarks>
public sealed class GitReviewTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cs-review-" + Guid.NewGuid().ToString("N"));
    private readonly string _repo;

    public GitReviewTests()
    {
        _repo = Path.Combine(_root, "DEVLAB-7");
        Directory.CreateDirectory(_repo);
        Git("init", "-q", "-b", "main");
        Write("src/a.cs", "uno\ndos\ntres\n");
        Write("src/b.cs", "b\n");
        Git("add", "-A");
        Git("commit", "-q", "-m", "inicio");
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, recursive: true);
    }

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

    private ReviewState Start() => GitReview.Start(_repo, "DEVLAB-7", "develop", 12, ["src/a.cs", "src/b.cs"]);

    /// <summary>Escribe a mano lo aplicado, como lo dejaría <c>/opsx:review</c>.</summary>
    private void Applied(string json) => Write("docs/reviews/DEVLAB-7-applied.json", json);

    /// <summary>Una entrada de lo aplicado para una nota, con su id y su fecha de creación tal como están en la revisión.</summary>
    private static string EntryOf(ReviewNote n, string outcome, int round, string extra = "") =>
        $$"""{ "id": "{{n.Id}}", "createdUtc": "{{n.CreatedUtc:yyyy-MM-ddTHH:mm:ss}}Z", "round": {{round}}, "outcome": "{{outcome}}"{{extra}} }""";

    /// <summary>Cuatro notas, una de cada tipo, como al empezar a revisar.</summary>
    private List<ReviewNote> FourNotes()
    {
        Start();
        GitReview.AddNote(_repo, "correccion", "src/a.cs", 2, "dos", "Falta un caso.");
        GitReview.AddNote(_repo, "duda", "src/a.cs", 3, "tres", "¿Por qué dos veces?");
        GitReview.AddNote(_repo, "mejora", null, null, null, "Exportar a Excel.");
        return GitReview.AddNote(_repo, "bien", "src/b.cs", null, null, "Bien resuelto.").Review!.Notes;
    }

    [Fact]
    public void Lo_aplicado_marca_sus_notas_y_las_demas_quedan_pendientes()
    {
        var notes = FourNotes();
        Applied($$"""
            { "branch": "DEVLAB-7", "notes": [
              {{EntryOf(notes[0], "aplicada", 1, ", \"commit\": \"a1b2c3d\", \"summary\": \"Se añade el caso.\"")}},
              {{EntryOf(notes[1], "contestada", 1, ", \"summary\": \"El segundo orden desempata.\"")}}
            ] }
            """);

        var state = GitReview.WithApplied(GitReview.Load(_repo));

        Assert.Null(state.AppliedWarning);
        Assert.Equal(2, state.Applied!.Count);
        Assert.Equal(new ReviewOutcome("aplicada", 1, "a1b2c3d", null, "Se añade el caso."), state.Applied[notes[0].Id]);
        Assert.Equal("contestada", state.Applied[notes[1].Id].Outcome);
        Assert.False(state.Applied.ContainsKey(notes[2].Id));
        Assert.False(state.Applied.ContainsKey(notes[3].Id));
        // El estado vive solo en el .json: el .md sigue llevando solo lo que anota la persona.
        Assert.DoesNotContain("aplicada", File.ReadAllText(state.FilePath!));
    }

    [Fact]
    public void Leer_una_respuesta_la_aparta_hasta_que_otra_ronda_vuelve_a_tratar_la_nota()
    {
        var notes = FourNotes();
        Applied($$"""{ "branch": "DEVLAB-7", "notes": [ {{EntryOf(notes[1], "contestada", 1)}} ] }""");

        // Una pendiente no tiene respuesta que leer: se salta.
        var review = GitReview.MarkRead(_repo, [notes[1].Id, notes[2].Id], true).Review!;
        Assert.Equal(1, review.Notes[1].Read);
        Assert.Null(review.Notes[2].Read);
        Assert.Equal(1, GitReview.Load(_repo).Review!.Notes[1].Read);

        // Otra ronda contesta otra cosa: vuelve a estar sin leer.
        Applied($$"""{ "branch": "DEVLAB-7", "notes": [ {{EntryOf(notes[1], "contestada", 2)}} ] }""");
        var state = GitReview.WithApplied(GitReview.Load(_repo));
        Assert.False(GitReview.IsRead(state.Review!.Notes[1], state.Applied![notes[1].Id]));

        review = GitReview.MarkRead(_repo, [notes[1].Id], false).Review!;
        Assert.Null(review.Notes[1].Read);
    }

    [Fact]
    public void Revisar_un_fichero_da_por_leidas_sus_notas_resueltas_y_desmarcarlo_no_las_toca()
    {
        var notes = FourNotes();
        Applied($$"""
            { "branch": "DEVLAB-7", "notes": [
              {{EntryOf(notes[0], "aplicada", 1, ", \"commit\": \"a1b2c3d\"")}},
              {{EntryOf(notes[3], "vista", 2)}}
            ] }
            """);

        var review = GitReview.Mark(_repo, ["src/a.cs"], true).Review!;
        Assert.Equal(1, review.Notes[0].Read);
        Assert.Null(review.Notes[1].Read);
        Assert.Null(review.Notes[3].Read);

        review = GitReview.Mark(_repo, ["src/a.cs"], false).Review!;
        Assert.Equal(1, review.Notes[0].Read);
    }

    [Fact]
    public void Se_lee_la_marca_de_Agent_Pulse_y_al_guardar_pasa_a_la_de_Arkko()
    {
        var path = Start().FilePath!;
        Assert.Contains("<!-- arkko-review {", File.ReadAllText(path));
        File.WriteAllText(path, File.ReadAllText(path).Replace("<!-- arkko-review {", "<!-- agent-pulse-review {"));

        var state = GitReview.Load(_repo);

        Assert.Equal("DEVLAB-7", state.Review!.Branch);
        GitReview.AddNote(_repo, "duda", null, null, null, "Una nota.");
        Assert.Contains("<!-- arkko-review {", File.ReadAllText(path));
        Assert.DoesNotContain("agent-pulse-review", File.ReadAllText(path));
    }

    [Fact]
    public void Una_nota_nueva_que_reutiliza_el_id_de_una_borrada_sigue_pendiente()
    {
        var notes = FourNotes();
        var last = notes[^1];
        Applied($$"""{ "branch": "DEVLAB-7", "notes": [ {{EntryOf(last, "vista", 1)}} ] }""");
        Assert.True(GitReview.WithApplied(GitReview.Load(_repo)).Applied!.ContainsKey(last.Id));

        GitReview.RemoveNote(_repo, last.Id);
        // La nota nueva se crea en otro segundo que la borrada, como pasa al usar la app.
        Thread.Sleep(1100);
        var again = GitReview.AddNote(_repo, "duda", null, null, null, "Otra cosa.").Review!.Notes[^1];

        Assert.Equal(last.Id, again.Id);
        Assert.Empty(GitReview.WithApplied(GitReview.Load(_repo)).Applied!);
    }

    [Fact]
    public void Sin_fichero_de_lo_aplicado_todo_esta_pendiente_y_sin_aviso()
    {
        FourNotes();

        var state = GitReview.WithApplied(GitReview.Load(_repo));

        Assert.Empty(state.Applied!);
        Assert.Null(state.AppliedWarning);
    }

    [Theory]
    [InlineData("{ roto")]
    [InlineData("""{ "branch": "DEVLAB-99", "notes": [] }""")]
    [InlineData("""[ 1, 2 ]""")]
    public void Un_fichero_de_lo_aplicado_roto_o_de_otra_rama_se_ignora_con_aviso(string json)
    {
        var notes = FourNotes();
        Applied(json);

        var state = GitReview.WithApplied(GitReview.Load(_repo));

        Assert.Null(state.Error);
        Assert.Equal(notes.Count, state.Review!.Notes.Count);
        Assert.Empty(state.Applied!);
        Assert.NotNull(state.AppliedWarning);
    }

    [Fact]
    public void Las_entradas_de_notas_borradas_se_ignoran_y_las_que_no_se_entienden_se_cuentan()
    {
        var notes = FourNotes();
        Applied($$"""
            { "branch": "DEVLAB-7", "notes": [
              { "id": "n40", "createdUtc": "2026-10-28T09:12:00Z", "round": 1, "outcome": "aplicada" },
              {{EntryOf(notes[0], "rechazada", 1)}},
              { "id": "n2", "outcome": "vista" },
              {{EntryOf(notes[1], "tarea", 2, ", \"task\": \"DEVLAB-130\"")}}
            ] }
            """);

        var state = GitReview.WithApplied(GitReview.Load(_repo));

        Assert.Equal([notes[1].Id], state.Applied!.Keys);
        Assert.Equal(new ReviewOutcome("tarea", 2, null, "DEVLAB-130", null), state.Applied[notes[1].Id]);
        Assert.Contains("2 entradas", state.AppliedWarning);
    }

    [Fact]
    public void Si_una_nota_sale_dos_veces_gana_la_ronda_mas_alta_y_la_fecha_se_compara_al_segundo()
    {
        var notes = FourNotes();
        var exact = notes[0].CreatedUtc.ToString("O");
        Applied($$"""
            { "branch": "DEVLAB-7", "notes": [
              {{EntryOf(notes[0], "aplicada", 2, ", \"commit\": \"b2\"")}},
              { "id": "{{notes[0].Id}}", "createdUtc": "{{exact}}", "round": 1, "outcome": "contestada" }
            ] }
            """);

        var outcome = GitReview.WithApplied(GitReview.Load(_repo)).Applied![notes[0].Id];

        Assert.Equal(("aplicada", 2, "b2"), (outcome.Outcome, outcome.Round, outcome.Commit));
    }

    /// <summary>Commitea lo que haya en la carpeta y devuelve el commit.</summary>
    private string Commit(string message)
    {
        Git("add", "-A");
        Git("commit", "-q", "-m", message);
        return Git("rev-parse", "HEAD").Trim();
    }

    [Fact]
    public void Cada_ronda_empieza_antes_de_su_primer_commit_y_lleva_lo_que_toco_hasta_la_siguiente()
    {
        Git("switch", "-q", "-c", "DEVLAB-7");
        Write("src/a.cs", "uno\ndos\ntres\ncuatro\n");
        var work = Commit("trabajo");
        var notes = FourNotes();
        Write("src/a.cs", "uno\ndos bien\ntres\ncuatro\n");
        Write("src/c.cs", "c\n");
        var fix = Commit("ronda 1: el caso");
        Write("docs/d.md", "doc\n");
        var docs = Commit("ronda 1: documentación");
        Write("src/b.cs", "b2\n");
        var second = Commit("ronda 2");
        Applied($$"""
            { "branch": "DEVLAB-7", "notes": [
              {{EntryOf(notes[0], "aplicada", 1, $", \"commit\": \"{fix[..7]}\"")}},
              {{EntryOf(notes[1], "contestada", 1)}},
              {{EntryOf(notes[3], "aplicada", 2, $", \"commit\": \"{second[..7]}\"")}}
            ] }
            """);

        var state = GitReview.WithRounds(GitReview.WithApplied(GitReview.Load(_repo)), _repo);

        Assert.Null(state.RoundsWarning);
        Assert.Equal(2, state.Rounds!.Count);
        Assert.Equal((1, work), (state.Rounds[0].Round, state.Rounds[0].Base));
        Assert.Equal(["docs/d.md", "src/a.cs", "src/c.cs"], state.Rounds[0].Files.Order());
        Assert.Equal((2, docs), (state.Rounds[1].Round, state.Rounds[1].Base));
        Assert.Equal(["src/b.cs"], state.Rounds[1].Files);
    }

    [Fact]
    public void Lo_que_entra_con_un_merge_de_develop_no_es_de_la_ronda_salvo_lo_resuelto_a_mano()
    {
        Git("branch", "develop");
        Git("switch", "-q", "-c", "DEVLAB-7", "develop");
        Write("src/a.cs", "uno\ndos\ntres\ncuatro\n");
        Write("src/b.cs", "b de la rama\n");
        Commit("trabajo");
        var work = Git("rev-parse", "HEAD").Trim();
        var notes = FourNotes();
        Write("src/c.cs", "c\n");
        var fix = Commit("ronda 1");
        // Otra rama llega a develop: un fichero nuevo y otro que también tocó esta, con conflicto.
        Git("switch", "-q", "develop");
        Write("src/d.cs", "d\n");
        Write("src/b.cs", "b de develop\n");
        var developed = Commit("otra tarea");
        Git("switch", "-q", "DEVLAB-7");
        GitTry("merge", "-q", "develop");
        Write("src/b.cs", "b de los dos\n");
        Git("add", "-A");
        Git("commit", "-q", "--no-edit");
        Applied($$"""
            { "branch": "DEVLAB-7", "notes": [ {{EntryOf(notes[0], "aplicada", 1, $", \"commit\": \"{fix[..7]}\"")}} ] }
            """);

        var round = Assert.Single(GitReview.WithRounds(GitReview.WithApplied(GitReview.Load(_repo)), _repo).Rounds!);

        Assert.Equal(work, round.Base);
        Assert.Equal(["src/b.cs", "src/c.cs"], round.Files.Order());
        Assert.Equal(["src/b.cs"], round.Merged);
        Assert.Equal(developed, round.MergedBase);
    }

    /// <summary>Lanza git sin exigir que salga bien: un merge con conflicto termina con error, y se resuelve después.</summary>
    private void GitTry(params string[] args)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = _repo, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in (string[])["-c", "user.name=Prueba", "-c", "user.email=prueba@example.com", "-c", "commit.gpgsign=false", .. args]) info.ArgumentList.Add(arg);
        using var git = Process.Start(info)!;
        git.StandardOutput.ReadToEnd();
        git.StandardError.ReadToEnd();
        git.WaitForExit();
    }

    [Fact]
    public void Una_ronda_sin_codigo_no_sale_y_una_reescrita_se_avisa()
    {
        Git("switch", "-q", "-c", "DEVLAB-7");
        Write("src/a.cs", "uno\n");
        Commit("trabajo");
        var notes = FourNotes();
        Applied($$"""
            { "branch": "DEVLAB-7", "notes": [
              {{EntryOf(notes[0], "aplicada", 1, ", \"commit\": \"0123abcd\"")}},
              {{EntryOf(notes[1], "contestada", 2)}}
            ] }
            """);

        var state = GitReview.WithRounds(GitReview.WithApplied(GitReview.Load(_repo)), _repo);

        Assert.Empty(state.Rounds!);
        Assert.Contains("ronda 1", state.RoundsWarning);
    }

    [Fact]
    public void Sin_nada_aplicado_no_se_buscan_rondas()
    {
        FourNotes();

        var state = GitReview.WithRounds(GitReview.WithApplied(GitReview.Load(_repo)), _repo);

        Assert.Null(state.Rounds);
        Assert.Null(state.RoundsWarning);
    }

    [Fact]
    public void Lo_cambiado_desde_un_commit_cuenta_lo_commiteado_despues_y_lo_que_hay_en_disco()
    {
        Git("switch", "-q", "-c", "DEVLAB-7");
        Write("src/a.cs", "uno\n");
        var from = Commit("trabajo");
        Write("src/b.cs", "b\nb2\n");
        Commit("después");
        Write("src/c.cs", "c\n");

        var set = GitChanges.Since(_repo, from);

        Assert.Null(set.Error);
        Assert.Equal(from, set.Base);
        Assert.Equal(["src/b.cs", "src/c.cs"], set.Files.Select(f => f.Path));
        Assert.Equal((1, 0), (set.Files[0].Added, set.Files[0].Deleted));
        Assert.NotNull(GitChanges.Since(_repo, "HEAD~1").Error);
    }

    [Fact]
    public void Empezar_crea_el_fichero_en_docs_reviews_y_lo_deja_fuera_de_git()
    {
        var state = Start();

        Assert.Null(state.Error);
        Assert.Equal(Path.Combine(_repo, "docs", "reviews", "DEVLAB-7-review.md"), state.FilePath);
        Assert.True(File.Exists(state.FilePath));
        Assert.Equal("", Git("status", "--porcelain").Trim());
        Assert.Contains("docs/reviews/", Git("check-ignore", "docs/reviews/DEVLAB-7-review.md"));
        Assert.All(state.Review!.Files, f => Assert.False(f.Reviewed));

        // Empezar otra vez no la reinicia, ni repite la exclusión.
        GitReview.Mark(_repo, "src/a.cs", true);
        Assert.True(Start().Review!.Files[0].Reviewed);
        var exclude = File.ReadAllText(Path.Combine(_repo, ".git", "info", "exclude"));
        Assert.Single(exclude.Split('\n'), l => l.Trim() == GitReview.Folder);
    }

    [Fact]
    public void Lo_que_se_lee_del_fichero_es_lo_que_se_escribio()
    {
        Start();
        GitReview.Mark(_repo, "src/b.cs", true);
        GitReview.AddNote(_repo, "correccion", "src/a.cs", 2, "dos", "Esto está mal.\n### y esta línea no es una nota");
        GitReview.AddNote(_repo, "duda", null, null, null, "¿Por qué así?");

        var review = GitReview.Load(_repo).Review!;

        Assert.Equal(("DEVLAB-7", 12, "develop"), (review.Branch, review.Pr, review.BaseLabel));
        Assert.Equal([false, true], review.Files.Select(f => f.Reviewed));
        Assert.NotNull(review.Files[1].Blob);
        Assert.Equal(2, review.Notes.Count);
        Assert.Equal(("n1", "correccion", "src/a.cs", 2, "dos"), (review.Notes[0].Id, review.Notes[0].Kind, review.Notes[0].File, review.Notes[0].Line, review.Notes[0].LineText));
        Assert.Equal("Esto está mal.\n### y esta línea no es una nota", review.Notes[0].Text);
        Assert.Equal(("n2", "duda", null, "¿Por qué así?"), (review.Notes[1].Id, review.Notes[1].Kind, review.Notes[1].File, review.Notes[1].Text));

        GitReview.RemoveNote(_repo, "n1");
        Assert.Equal(["n2"], GitReview.Load(_repo).Review!.Notes.Select(n => n.Id));
    }

    [Fact]
    public void Un_fichero_revisado_que_cambia_vuelve_a_sin_revisar()
    {
        Start();
        GitReview.Mark(_repo, "src/a.cs", true);
        GitReview.Mark(_repo, "src/b.cs", true);

        Assert.All(GitReview.Sync(_repo, ["src/a.cs", "src/b.cs"]).Review!.Files, f => Assert.True(f.Reviewed));

        Write("src/a.cs", "uno\ndos\ntres\ncuatro\n");
        var review = GitReview.Sync(_repo, ["src/a.cs", "src/b.cs"]).Review!;

        Assert.Equal([(false, true), (true, false)], review.Files.Select(f => (f.Reviewed, f.Changed)));
        Assert.Contains("ha cambiado desde que se revisó", File.ReadAllText(GitReview.Load(_repo).FilePath!));
    }

    [Fact]
    public void Se_marcan_varios_de_una_vez_y_se_salta_lo_que_no_esta()
    {
        Start();

        var review = GitReview.Mark(_repo, ["src/a.cs", "src/b.cs", "no/esta.cs"], true).Review!;
        Assert.All(review.Files, f => Assert.True(f.Reviewed && f.Blob is not null));

        review = GitReview.Mark(_repo, ["src/a.cs", "src/b.cs"], false).Review!;
        Assert.All(review.Files, f => Assert.False(f.Reviewed));
    }

    [Fact]
    public void Sigue_a_los_ficheros_de_la_rama()
    {
        Start();
        GitReview.Mark(_repo, "src/a.cs", true);

        var review = GitReview.Sync(_repo, ["src/a.cs", "src/c.cs"]).Review!;

        Assert.Equal([("src/a.cs", true), ("src/c.cs", false)], review.Files.Select(f => (f.Path, f.Reviewed)));
    }

    [Fact]
    public void Una_nota_sigue_a_su_linea_y_avisa_si_desaparece()
    {
        Start();
        GitReview.AddNote(_repo, "mejora", "src/a.cs", 2, "dos", "Mejorable.");

        Write("src/a.cs", "cero\nuno\ndos\ntres\n");
        Assert.Equal((3, false), (GitReview.Sync(_repo, ["src/a.cs", "src/b.cs"]).Review!.Notes[0].Line, false));

        Write("src/a.cs", "cero\nuno\ntres\n");
        Assert.True(GitReview.Sync(_repo, ["src/a.cs", "src/b.cs"]).Review!.Notes[0].Lost);
    }

    [Fact]
    public void Una_nota_de_varias_lineas_se_mueve_entera_y_se_lee_igual()
    {
        Start();
        GitReview.AddNote(_repo, "correccion", "src/a.cs", 1, "uno", "Todo el bloque.", endLine: 3);
        Assert.Contains("`src/a.cs:1-3`", GitReview.ReadText(_repo).Text);

        Write("src/a.cs", "nueva\notra\nuno\ndos\ntres\n");
        var note = GitReview.Sync(_repo, ["src/a.cs", "src/b.cs"]).Review!.Notes[0];

        Assert.Equal((3, 5), (note.Line, note.EndLine));
        Assert.Equal((3, 5), (GitReview.Load(_repo).Review!.Notes[0].Line, GitReview.Load(_repo).Review!.Notes[0].EndLine));
    }

    [Fact]
    public void Una_nota_de_lo_quitado_lo_cita_y_se_lee_igual()
    {
        Start();
        GitReview.AddNote(_repo, "duda", "src/a.cs", 2, "dos", "¿Por qué se quita?", endLine: 3, removed: (4, 5, "viejo\nantiguo", true));

        var text = GitReview.ReadText(_repo).Text!;
        Assert.Contains("`src/a.cs:2` · lo quitado justo antes", text);
        Assert.Contains("> − `viejo`\n> − `antiguo`", text);
        var note = GitReview.Load(_repo).Review!.Notes[0];
        // Solo de lo quitado: la línea que la sitúa es una, y no se cita como si fuera de la nota.
        Assert.Equal((2, (int?)null, 4, (int?)5, "viejo\nantiguo", true), (note.Line!.Value, note.EndLine, note.OldLine!.Value, note.OldEndLine, note.Removed, note.Anchor));
        Assert.DoesNotContain("> `dos`", text);
        Assert.Equal("¿Por qué se quita?", note.Text);
    }

    [Fact]
    public void Encuentra_la_PR_que_apunta_a_la_rama()
    {
        var remote = Path.Combine(_root, "remoto.git");
        GitIn(_root, "init", "-q", "--bare", remote);
        Git("remote", "add", "origin", remote);
        Git("push", "-q", "-u", "origin", "main");
        Assert.Equal(new PrLookup(null, null), GitReview.FindPr(_repo));

        var sha = Git("rev-parse", "HEAD").Trim();
        GitIn(remote, "update-ref", "refs/pull/7/head", sha);
        GitIn(remote, "update-ref", "refs/pull/3/head", sha);
        Assert.Equal(new PrLookup(7, null), GitReview.FindPr(_repo));

        // Sin remoto al que preguntar, lo dice: no es lo mismo que no tener PR.
        Git("remote", "set-url", "origin", Path.Combine(_root, "no-existe.git"));
        Assert.NotNull(GitReview.FindPr(_repo).Error);
    }

    [Fact]
    public void Sin_revision_empezada_no_hay_nada_que_cambiar()
    {
        Assert.Null(GitReview.Load(_repo).Review);
        Assert.Null(GitReview.Sync(_repo, ["src/a.cs"]).Review);
        Assert.NotNull(GitReview.Mark(_repo, "src/a.cs", true).Error);
        Assert.NotNull(GitReview.AddNote(_repo, "bien", null, null, null, "Bien.").Error);
        Assert.False(Directory.Exists(Path.Combine(_repo, "docs")));
    }
}
