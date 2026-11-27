using System.Text.Json;
using Arkko.Core;

namespace Arkko.Tests;

public sealed class ArtifactIndexTests : IDisposable
{
    private const string Page = "https://claude.ai/artifact/AbCdEf123";
    private const string DocsType = "https://claude.ai/artifact/Rp9naXUCj2xozpUkyQy19W";

    // Las líneas llevan la fecha de hoy: el índice olvida las llamadas sin respuesta de más de un día,
    // y con una fecha fija la prueba deja de pasar al día siguiente.
    private static readonly DateTime Today = DateTime.UtcNow.Date;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "arkko-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _cwd;

    public ArtifactIndexTests()
    {
        _cwd = Path.Combine(_dir, "proyecto");
        Directory.CreateDirectory(_cwd);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Una_publicacion_y_su_actualizacion_son_un_artifact_con_dos_versiones()
    {
        File.WriteAllText(Path.Combine(_cwd, "pagina.html"), "<title>Comparador de iconos</title><p>hola</p>");
        Directory.CreateDirectory(Path.Combine(_dir, "sessions"));
        File.WriteAllText(Path.Combine(_dir, "sessions", "s1.json"), """{ "session_id": "s1", "session_name": "Iconos nuevos" }""");
        var transcript = Write("s1.jsonl",
            Use("10:00:00", "t1", "Artifact", new { file_path = "pagina.html", icon = "bell" }),
            Result("10:00:02", "t1", $"Published C:\\x\\pagina.html at {Page} (Version 1)"),
            Use("10:05:00", "t2", "Artifact", new { file_path = "pagina.html" }),
            Result("10:05:01", "t2", $"Updated the Artifact at {Page}"));

        var index = new ArtifactIndex(_dir);
        Assert.True(index.Scan([transcript]));

        var item = Assert.Single(index.All());
        Assert.Equal(Page, item.Url);
        Assert.Equal(ArtifactKind.Page, item.Kind);
        Assert.Equal("Comparador de iconos", item.Title);
        Assert.Equal(2, item.Versions);
        Assert.Equal("s1", item.SessionId);
        Assert.Equal("Iconos nuevos", item.SessionName);
        Assert.Equal("proyecto", item.Group);
        Assert.Equal(Today.AddHours(10), item.CreatedUtc);
        Assert.Equal(Today.AddHours(10).AddMinutes(5), item.UpdatedUtc);
    }

    [Fact]
    public void Lo_que_no_publica_no_cuenta()
    {
        var transcript = Write("s1.jsonl",
            Use("10:00:00", "t1", "Artifact", new { action = "quickstart", intent = "other" }),
            Result("10:00:01", "t1", $"Quickstart. Start from the Dashboard type: {DocsType}"),
            Use("10:01:00", "t2", "Artifact", new { action = "read", url = Page }),
            Result("10:01:01", "t2", $"[Artifact {Page} (version 3)]"),
            Use("10:02:00", "t3", "Artifact", new { file_path = "x.html" }),
            Result("10:02:01", "t3", $"Publish refused: a newer version of {Page} exists", error: true),
            Text("10:03:00", $"Te paso el enlace: {Page}"));

        var index = new ArtifactIndex(_dir);

        Assert.False(index.Scan([transcript]));
        Assert.Empty(index.All());
    }

    [Fact]
    public void Un_artifact_de_un_tipo_se_guarda_con_su_clase_y_su_titulo()
    {
        var transcript = Write("s1.jsonl",
            Use("10:00:00", "t1", "Artifact", new { action = "publish", type_url = DocsType, title = "Informe de octubre" }),
            Result("10:00:02", "t1", "Created a new Artifact at https://claude.ai/artifact/Doc999 from the Docs type."));

        var index = new ArtifactIndex(_dir);
        index.Scan([transcript]);

        var item = Assert.Single(index.All());
        Assert.Equal(ArtifactKind.Document, item.Kind);
        Assert.Equal("Informe de octubre", item.Title);
    }

    [Fact]
    public void Un_documento_del_conector_de_Docs_cuenta_y_sus_cambios_suben_la_version()
    {
        var link = "https://claude.ai/artifact/DocDeDocs1";
        var transcript = Write("s1.jsonl",
            Use("10:00:00", "t1", "mcp__claude_ai_Claude_Docs__batch", new { container = new { kind = "project", create = new { name = "Acta" } } }),
            Result("10:00:03", "t1", $"Created. Open it at {link}"),
            Use("10:10:00", "t2", "mcp__claude_ai_Claude_Docs__update", new { payload = "texto" }),
            Result("10:10:01", "t2", $"Updated {link}"),
            Use("10:20:00", "t3", "mcp__claude_ai_Claude_Docs__update", new { payload = "otro" }),
            Result("10:20:01", "t3", "Updated https://claude.ai/artifact/OtroDoc"));

        var index = new ArtifactIndex(_dir);
        index.Scan([transcript]);

        var item = Assert.Single(index.All());
        Assert.Equal(ArtifactKind.Document, item.Kind);
        Assert.Equal("Acta", item.Title);
        Assert.Equal(2, item.Versions);
    }

    [Fact]
    public void La_respuesta_que_llega_en_otra_lectura_se_empareja_con_su_llamada()
    {
        var transcript = Write("s1.jsonl", Use("10:00:00", "t1", "Artifact", new { file_path = "a.html" }));
        var index = new ArtifactIndex(_dir);
        Assert.False(index.Scan([transcript]));

        File.AppendAllText(transcript, Result("10:00:02", "t1", $"Published a.html at {Page}") + "\n");

        Assert.True(index.Scan([transcript]));
        Assert.Single(index.All());
    }

    [Fact]
    public void Releer_no_suma_versiones_y_lo_quitado_no_vuelve()
    {
        var transcript = Write("s1.jsonl",
            Use("10:00:00", "t1", "Artifact", new { file_path = "a.html" }),
            Result("10:00:02", "t1", $"Published a.html at {Page}"));
        var index = new ArtifactIndex(_dir);
        index.Scan([transcript]);
        index.SetRemoved(index.All().Single(), true);

        // Como si fuera la primera vez: sin estado, se relee el transcript entero.
        File.Delete(Path.Combine(_dir, "artifacts-scan.json"));
        index.Scan([transcript]);

        var item = Assert.Single(index.All());
        Assert.Equal(1, item.Versions);
        Assert.True(item.Removed);
    }

    [Fact]
    public void Un_borrado_desde_una_sesion_lo_marca_como_borrado()
    {
        var transcript = Write("s1.jsonl",
            Use("10:00:00", "t1", "Artifact", new { file_path = "a.html" }),
            Result("10:00:02", "t1", $"Published a.html at {Page}"),
            Use("11:00:00", "t2", "Artifact", new { action = "delete", url = Page }),
            Result("11:00:01", "t2", $"Deleted the Artifact {Page}"));

        var index = new ArtifactIndex(_dir);
        index.Scan([transcript]);

        Assert.True(Assert.Single(index.All()).Deleted);
    }

    [Fact]
    public void La_galeria_añade_lo_que_el_indice_no_conoce_y_lo_quitado_se_recuerda()
    {
        var transcript = Write("s1.jsonl",
            Use("10:00:00", "t1", "Artifact", new { file_path = "a.html" }),
            Result("10:00:02", "t1", $"Published a.html at {Page}"));
        var index = new ArtifactIndex(_dir);
        index.Scan([transcript]);
        File.WriteAllText(index.GalleryFile, $$"""
            { "fetched_utc": "{{Today:yyyy-MM-dd}}T12:00:00Z", "items": [
              { "url": "{{Page}}", "title": "Repetido", "updated_utc": "{{Today:yyyy-MM-dd}}T10:00:00Z" },
              { "url": "https://claude.ai/artifact/DelChat1", "title": "Guía hecha en el chat", "updated_utc": "2026-10-26T12:00:00Z" } ] }
            """);

        var all = index.AllWithGallery();

        Assert.Equal(2, all.Count);
        Assert.False(all.Single(a => a.Url == Page).FromGallery);
        Assert.Equal("Repetido", all.Single(a => a.Url == Page).Title);
        var chat = all.Single(a => a.FromGallery);
        Assert.Equal("Guía hecha en el chat", chat.Title);
        Assert.Equal(ArtifactKind.Unknown, chat.Kind);

        index.Change(chat, a => (a.Repo, a.Kind) = ("tienda", ArtifactKind.Document));
        index.SetRemoved(index.AllWithGallery().Single(a => a.FromGallery), true);

        var kept = index.AllWithGallery().Single(a => a.FromGallery);
        Assert.Equal(("tienda", ArtifactKind.Document, true), (kept.Repo, kept.Kind, kept.Removed));
        Assert.Equal(2, index.AllWithGallery().Count);
    }

    /// <summary>Escribe un transcript con unas líneas y devuelve su ruta.</summary>
    private string Write(string name, params string[] lines)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, string.Join("\n", lines) + "\n");
        return path;
    }

    /// <summary>Una línea de Claude que llama a una herramienta.</summary>
    private string Use(string time, string id, string tool, object input) => Line("assistant", time,
        new { role = "assistant", content = new object[] { new { type = "tool_use", id, name = tool, input } } });

    /// <summary>Una línea con la respuesta de una herramienta.</summary>
    private string Result(string time, string id, string text, bool error = false) => Line("user", time,
        new { role = "user", content = new object[] { new { type = "tool_result", tool_use_id = id, content = text, is_error = error } } });

    /// <summary>Una línea de Claude con texto.</summary>
    private string Text(string time, string text) => Line("assistant", time,
        new { role = "assistant", content = new object[] { new { type = "text", text } } });

    private string Line(string type, string time, object message) => JsonSerializer.Serialize(new
    {
        type, sessionId = "s1", cwd = _cwd, timestamp = $"{Today:yyyy-MM-dd}T{time}Z", message,
    });
}
