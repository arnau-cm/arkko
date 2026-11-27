using System.Text.Json;
using Arkko.Core;

namespace Arkko.Tests;

/// <remarks>Los informes tienen la forma del contrato de arkko-stack (su docs/seguridad.md), con datos inventados.</remarks>
public sealed class StackSecurityTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 6, 9, 0, 0, DateTimeKind.Utc);
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "cs-stack-" + Guid.NewGuid().ToString("N"));

    public StackSecurityTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (_folder.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)) Directory.Delete(_folder, recursive: true);
    }

    /// <summary>Un informe con las fechas, comprobaciones y avisos dados.</summary>
    private static string Report(string? exposure = "2026-11-05T14:29:38.92+00:00", string? news = "2026-11-05T14:29:38.92+00:00",
        object[]? checks = null, object[]? notices = null, int schema = 1) => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["esquema"] = schema,
        ["generado"] = "2026-11-05T14:29:38.92+00:00",
        ["origen"] = new { equipo = "PC-PRUEBA", ambito = "todo", lanzadoPor = "manual" },
        ["exposicion"] = new { fecha = exposure, comprobaciones = checks ?? [] },
        ["noticias"] = new
        {
            fecha = news,
            fuentes = new[] { new { id = "kev", nombre = "CISA KEV", estado = "OK", detalle = "sin cambios", consultada = "2026-11-05T14:29:38.92+00:00" } },
            avisos = notices ?? [],
        },
    });

    private static object Check(string id, string state, string obj = "equipo", string? end = null) =>
        new { id, objeto = obj, nombre = id, estado = state, detalle = "detalle de " + id, arreglo = state == "OK" ? "" : "arréglalo", finSoporte = end };

    private static object Notice(string id, bool seen = false, string link = "https://example.test/aviso") =>
        new { fuente = "Node.js", id, titulo = "Aviso " + id, fecha = "2026-11-03T23:00:00+00:00", enlace = link, piezas = new[] { "Node.js y npm" }, visto = seen };

    private static StackReport Parse(string json)
    {
        var read = StackSecurity.Parse(json);
        Assert.Equal(StackReportStatus.Ok, read.Status);
        return read.Report!;
    }

    [Fact]
    public void Lee_el_informe_del_contrato()
    {
        var report = Parse(Report(checks: [Check("git", "FALLA"), Check("tfm:Taller", "OK", "Taller", "2026-11-10")], notices: [Notice("NODE-x")]));

        Assert.Equal("manual", report.Origin!.LaunchedBy);
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 14, 29, 38, 920, TimeSpan.Zero), report.Exposure!.Date);
        Assert.True(report.Checks[0].Failing);
        Assert.True(report.Checks[0].OfMachine);
        Assert.False(report.Checks[1].OfMachine);
        Assert.Equal(new DateOnly(2026, 11, 10), report.Checks[1].SupportEndsDate);
        Assert.Null(report.Checks[0].SupportEndsDate);
        Assert.Equal("Node.js y npm", report.Notices.Single().Parts.Single());
        Assert.Equal("CISA KEV", report.Sources.Single().Name);
    }

    [Fact]
    public void Lee_versiones_y_gravedad_y_un_campo_vacio_cuenta_como_ausente()
    {
        var report = Parse(Report(checks:
        [
            new { id = "edge", objeto = "equipo", nombre = "Edge", estado = "FALLA", detalle = "d", instalada = "150.0.4100.12", ultima = "156.0.4314.58", arreglo = "a" },
            new { id = "npm:Taller:package-lock.json", objeto = "Taller", nombre = "npm audit", estado = "OK", detalle = "d", gravedad = "media", arreglo = "" },
            new { id = "git", objeto = "equipo", nombre = "Git", estado = "OK", detalle = "d", instalada = " ", ultima = "", gravedad = "rara", arreglo = "" },
        ]));

        Assert.Equal(("150.0.4100.12", "156.0.4314.58"), (report.Checks[0].Installed, report.Checks[0].Latest));
        Assert.Null(report.Checks[0].SeverityRank);
        Assert.Equal(2, report.Checks[1].SeverityRank);
        Assert.Null(report.Checks[2].Installed);
        Assert.Null(report.Checks[2].Latest);
        Assert.Null(report.Checks[2].SeverityRank);
    }

    [Theory]
    [InlineData("FALLA", "150.0", "156.0", null, VersionNote.Newer, "156.0")]
    [InlineData("OK", "22.21.0", "22.21.0", null, VersionNote.Same, null)]
    [InlineData("OK", "1.107.0-insider", "1.106.0", null, VersionNote.Published, "1.106.0")]
    [InlineData("SIN COMPROBAR", "2.54.0.1", "2.56.0.4", null, VersionNote.Published, "2.56.0.4")]
    [InlineData("FALLA", "8.0.33", null, "2026-11-10", VersionNote.NoSupport, null)]
    public void Las_versiones_dicen_que_pasa(string state, string installed, string? latest, string? end, VersionNote note, string? other)
    {
        var check = new StackCheck { Id = "x", State = state, Installed = installed, Latest = latest, SupportEnds = end };
        Assert.Equal(new StackVersions(installed, other, note), StackSecurity.Versions(check, new DateOnly(2026, 11, 10)));
    }

    [Theory]
    [InlineData("FALLA", null, "156.0", null)]
    [InlineData("FALLA", "3.12.10", null, null)]
    [InlineData("FALLA", "3.12.10", null, "2028-11-01")]
    [InlineData("FALLA", "2.56.0.4", "2.56.0.4", null)]
    [InlineData("SIN COMPROBAR", "2.54.0.1", null, null)]
    public void Sin_versiones_claras_se_queda_la_frase(string state, string? installed, string? latest, string? end) =>
        Assert.Null(StackSecurity.Versions(new StackCheck { Id = "x", State = state, Installed = installed, Latest = latest, SupportEnds = end },
            new DateOnly(2026, 11, 10)));

    [Fact]
    public void El_arreglo_se_parte_en_texto_y_codigo()
    {
        var parts = StackSecurity.FixParts("Actualiza Git con `git update-git-for-windows`, o con el instalador de `gitforwindows.org`.");
        Assert.Equal(
        [
            new FixPart("Actualiza Git con ", false), new FixPart("git update-git-for-windows", true),
            new FixPart(", o con el instalador de ", false), new FixPart("gitforwindows.org", true), new FixPart(".", false),
        ], parts);
        Assert.Equal("Actualiza Git con git update-git-for-windows, o con el instalador de gitforwindows.org.",
            StackSecurity.PlainFix("Actualiza Git con `git update-git-for-windows`, o con el instalador de `gitforwindows.org`."));
    }

    [Theory]
    [InlineData("Sin marcas.", "Sin marcas.")]
    [InlineData("Una `sola comilla", "Una `sola comilla")]
    [InlineData("`npm update -g <paquete>`", "npm update -g <paquete>")]
    [InlineData("Vacío `` aquí", "Vacío  aquí")]
    [InlineData("", "")]
    public void Un_arreglo_raro_no_se_rompe(string fix, string plain) => Assert.Equal(plain, StackSecurity.PlainFix(fix));

    [Fact]
    public void Con_otro_esquema_no_interpreta_nada()
    {
        var read = StackSecurity.Parse(Report(schema: 2));
        Assert.Equal(StackReportStatus.UnknownSchema, read.Status);
        Assert.Null(read.Report);
        Assert.Contains("(2)", read.Problem);

        Assert.Equal(StackReportStatus.UnknownSchema, StackSecurity.Parse("""{ "generado": "x" }""").Status);
        Assert.Equal(StackReportStatus.Unreadable, StackSecurity.Parse("{ esto no es json").Status);
        Assert.Equal(StackReportStatus.Missing, StackSecurity.Read(Path.Combine(_folder, "no-esta.json")).Status);
    }

    [Fact]
    public void Un_enlace_que_no_es_https_no_se_ofrece()
    {
        var report = Parse(Report(notices: [Notice("A", link: "http://example.test"), Notice("B", link: "javascript:alert(1)")]));
        Assert.All(report.Notices, n => Assert.Null(n.Link));
    }

    [Theory]
    [InlineData("2026-11-05T14:00:00+00:00", "2026-11-05T14:00:00+00:00", null)]
    [InlineData("2026-11-05T14:00:00+00:00", "2026-11-04T14:00:00+00:00", "noticias")]
    [InlineData("2026-10-29T08:00:00+00:00", "2026-11-05T14:00:00+00:00", "exposicion")]
    [InlineData(null, "2026-11-04T14:00:00+00:00", "todo")]
    public void Toca_lanzar_lo_que_se_ha_quedado_viejo(string? exposure, string? news, string? due) =>
        Assert.Equal(due, StackSecurity.DueScope(Parse(Report(exposure, news)), Now));

    [Fact]
    public void Sin_informe_toca_todo() => Assert.Equal("todo", StackSecurity.DueScope(null, Now));

    [Fact]
    public void La_primera_lectura_solo_apunta()
    {
        var report = Parse(Report(checks: [Check("git", "FALLA"), Check("edge", "OK")], notices: [Notice("NODE-x")]));
        var (state, changes) = StackSecurityLog.Compare(null, report, Now);

        Assert.True(changes.First);
        Assert.Empty(changes.NewFailures);
        Assert.Empty(changes.NewNotices);
        Assert.Equal(3, changes.History.Count);
        Assert.Equal("FALLA", state.Checks["git"]);
        Assert.False(state.Notices["NODE-x"]);
    }

    [Fact]
    public void Avisa_de_lo_que_pasa_a_falla_y_de_los_avisos_nuevos_sin_ver()
    {
        var (before, _) = StackSecurityLog.Compare(null, Parse(Report(checks: [Check("git", "OK"), Check("edge", "FALLA")], notices: [Notice("A")])), Now);
        var report = Parse(Report(checks: [Check("git", "FALLA"), Check("edge", "FALLA"), Check("vscode", "FALLA"), Check("node", "OK")],
            notices: [Notice("A"), Notice("B"), Notice("C", seen: true)]));

        var (_, changes) = StackSecurityLog.Compare(before, report, Now);

        Assert.False(changes.First);
        Assert.Equal(["git", "vscode"], changes.NewFailures.Select(c => c.Id));
        Assert.Equal(["B"], changes.NewNotices.Select(n => n.Id));
        Assert.Contains(changes.History, e => e is { Id: "git", From: "OK", To: "FALLA", Detail: "detalle de git" });
        Assert.DoesNotContain(changes.History, e => e.Id == "edge");
    }

    [Fact]
    public void Lo_que_se_arregla_se_ve_o_se_va_queda_en_el_historial()
    {
        var (before, _) = StackSecurityLog.Compare(null, Parse(Report(checks: [Check("git", "FALLA"), Check("mcp:Viejo", "OK", "Viejo")], notices: [Notice("A")])), Now);
        var (after, changes) = StackSecurityLog.Compare(before, Parse(Report(checks: [Check("git", "OK")], notices: [Notice("A", seen: true)])), Now);

        Assert.Empty(changes.NewFailures);
        Assert.Contains(changes.History, e => e is { Kind: "check", Id: "git", From: "FALLA", To: "OK" });
        Assert.Contains(changes.History, e => e is { Kind: "check", Id: "mcp:Viejo", From: "OK", To: null });
        Assert.Contains(changes.History, e => e is { Kind: "notice", Id: "A", From: "sin ver", To: "visto" });
        Assert.False(after.Checks.ContainsKey("mcp:Viejo"));
    }

    [Fact]
    public void Una_seccion_que_no_se_ejecuto_no_cambia_nada()
    {
        var (before, _) = StackSecurityLog.Compare(null, Parse(Report(checks: [Check("git", "FALLA")])), Now);
        var (after, changes) = StackSecurityLog.Compare(before, Parse(Report(exposure: null)), Now);
        Assert.Empty(changes.History);
        Assert.Equal("FALLA", after.Checks["git"]);
    }

    [Fact]
    public void Guarda_el_estado_y_el_historial_y_no_repite_avisos()
    {
        StackSecurityLog.Record(Parse(Report(checks: [Check("git", "OK")])), Now, _folder);
        var report = Parse(Report(checks: [Check("git", "FALLA")]));

        Assert.Equal(["git"], StackSecurityLog.Record(report, Now.AddHours(1), _folder).NewFailures.Select(c => c.Id));
        Assert.Empty(StackSecurityLog.Record(report, Now.AddHours(2), _folder).NewFailures);

        var history = StackSecurityLog.History("check", "git", _folder);
        Assert.Equal([("OK", "FALLA"), (null, "OK")], history.Select(e => (e.From, e.To)));
    }

    [Fact]
    public void Avisa_una_sola_vez_de_cada_caducidad_sin_estropear_la_primera_lectura()
    {
        var date = new DateTimeOffset(2026, 11, 4, 14, 0, 0, TimeSpan.Zero);
        Assert.True(StackSecurityLog.TakeStaleAlert(news: true, date, _folder));
        Assert.False(StackSecurityLog.TakeStaleAlert(news: true, date, _folder));
        Assert.True(StackSecurityLog.TakeStaleAlert(news: false, date, _folder));
        Assert.True(StackSecurityLog.TakeStaleAlert(news: true, date.AddDays(1), _folder));

        // Haber avisado no cuenta como haber leído un informe: la primera lectura sigue sin avisar.
        Assert.True(StackSecurityLog.Record(Parse(Report(checks: [Check("git", "FALLA")])), Now, _folder).First);
    }

    [Fact]
    public void El_plazo_siempre_se_guarda_y_de_serie_son_90_dias()
    {
        var settings = new RetentionSettings { VulnerabilitiesDays = null };
        var back = JsonSerializer.Deserialize<RetentionSettings>(JsonSerializer.Serialize(settings, JsonStore.Options), JsonStore.Options)!;
        Assert.Null(back.VulnerabilitiesDays);
        Assert.Equal(90, new RetentionSettings().VulnerabilitiesDays);
    }

    [Fact]
    public void La_retencion_borra_el_historial_viejo()
    {
        var dir = Path.Combine(_folder, "vulnerabilities", "history");
        Directory.CreateDirectory(dir);
        var old = Path.Combine(dir, "2026-07-01.jsonl");
        var recent = Path.Combine(dir, "2026-10-30.jsonl");
        File.WriteAllText(old, "{}\n");
        File.WriteAllText(recent, "{}\n");

        new DataRetention(_folder).Apply(new RetentionSettings { VulnerabilitiesDays = 90 }, new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Local));

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
    }
}
