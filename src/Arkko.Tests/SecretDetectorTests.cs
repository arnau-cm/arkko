using Arkko.Core;

namespace Arkko.Tests;

/// <remarks>
/// Los secretos de prueba se montan por partes: escritos enteros, el escáner de secretos de GitHub
/// podría tomarlos por reales y rechazar el push.
/// </remarks>
public sealed class SecretDetectorTests : IDisposable
{
    private static readonly string GitHub = "gh" + "p_" + "R4nd0mT0k3nV4lu3Abc9XyZ8wQ7eP6oL5kJ4";
    private static readonly string Aws = "AK" + "IA" + "Q3EGRT7XYZW5LMNP";
    private static readonly string Anthropic = "sk" + "-ant-" + "api03-Zx9Qw8Er7Ty6Ui5Op4As3Df2Gh1Jk";
    private static readonly string Jwt = "ey" + "JhbGciOiJIUzI1NiJ9" + ".ey" + "JzdWIiOiIxMjM0NTY3ODkwIn0" + "." + "dBjftJeZ4CVPmB92K27uhbUJU1p1r";
    // Payload minimo (un {} codificado): formato valido de JWT que la regla anterior se dejaba.
    private static readonly string JwtPayloadCorto = "ey" + "JhbGciOiJFUzI1NiJ9" + "." + "e30" + "." + "Zm9yZ2VkU2lnbmF0dXJlRm9yVGVzdHNPbmx5X3h4";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccs-secrets-" + Guid.NewGuid().ToString("N"));

    public SecretDetectorTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        // Solo la carpeta temporal que ha creado este test.
        if (_dir.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)) Directory.Delete(_dir, recursive: true);
    }

    private static List<string> Kinds(string text) => SecretDetector.Scan(text).Select(m => m.Kind).ToList();

    [Fact]
    public void Encuentra_claves_con_formato_conocido()
    {
        Assert.Equal(["Token de GitHub"], Kinds($"git remote set-url origin https://x:{GitHub}@github.com/repo".Replace("x:", "")));
        Assert.Contains("Clave de AWS", Kinds($"aws_access_key_id = {Aws}"));
        Assert.Equal(["Clave de Anthropic"], Kinds($"ANTHROPIC_KEY {Anthropic}"));
        Assert.Equal(["Token JWT"], Kinds($"token: {Jwt}"));
        Assert.Equal(["Token JWT"], Kinds($"token: {JwtPayloadCorto}"));
    }

    [Fact]
    public void Encuentra_variables_de_entorno_con_valor()
    {
        Assert.Equal(["Variable con secreto"], Kinds("export MY_API_KEY=Qw8eR7tY6uI5oP4aS3dF"));
        Assert.Equal(["Variable con secreto"], Kinds("MY_SERVICE_TOKEN               k9Jh2Lm4Np6Qr8St0Vw"));
        Assert.Equal(["Secreto en configuración"], Kinds("{ \"client_secret\": \"a8Fj3kLq9Zx2Vb7Nm4\" }"));
    }

    [Fact]
    public void Encuentra_credenciales_en_cadenas_y_urls()
    {
        Assert.Equal(["Contraseña en cadena de conexión"], Kinds("Server=sql01;Database=App;User Id=app;Password=Tr4b4j0!Seg;"));
        Assert.Equal(["URL con credenciales"], Kinds("https://deploy:Sup3rS3cr3tP4ss@git.empresa.local/repo.git"));
        Assert.Equal(["Clave privada"], Kinds("-----BEGIN RSA PRIVATE KEY-----\nMIIEowIBAAKCAQEAx4Kj9f2\n-----END RSA PRIVATE KEY-----"));
    }

    [Fact]
    public void Descarta_ejemplos_plantillas_y_referencias()
    {
        Assert.Empty(Kinds("export API_KEY=your_api_key_here"));
        Assert.Empty(Kinds("MY_API_KEY=${MY_API_KEY}"));
        Assert.Empty(Kinds("$env:MY_API_KEY = $env:OTRA"));
        Assert.Empty(Kinds("DB_PASSWORD=changeme"));
        Assert.Empty(Kinds("SECRET_TOKEN=xxxxxxxxxxxxxxxx"));
        Assert.Empty(Kinds("ACCESS_TOKEN=aaaaaaaaaaaaaaaa"));
        Assert.Empty(Kinds("Password=<contraseña>;Server=sql01;"));
        Assert.Empty(Kinds("Un texto normal sin nada que esconder, con la palabra token y password."));
    }

    [Fact]
    public void Enmascara_sin_dejar_ver_el_valor()
    {
        Assert.Equal("ghp_" + "…" + GitHub[^4..], SecretDetector.Mask(GitHub));
        Assert.Equal("••••••", SecretDetector.Mask("corta123"));
    }

    [Fact]
    public void El_contexto_tapa_el_valor_y_los_secretos_vecinos()
    {
        var text = new string('x', 9000) + "\nrun: git push https://x@github.com " + GitHub + " && aws_access_key_id = " + Aws + "\n" + new string('y', 9000);

        var around = SecretDetector.Around(text, GitHub)!;

        Assert.Contains("[" + SecretDetector.Mask(GitHub) + "]", around);
        Assert.Contains("git push", around);
        Assert.DoesNotContain(GitHub, around);
        Assert.DoesNotContain(Aws, around);
        Assert.StartsWith("…", around);
        Assert.EndsWith("…", around);
        Assert.DoesNotContain("\n", around);
        Assert.Null(SecretDetector.Around(text, "no-esta"));
    }

    [Fact]
    public void El_analisis_guarda_la_llamada_y_donde_aparece()
    {
        var transcript = Path.Combine(_dir, "agent-a1.jsonl");
        var output = new string('-', 12000) + " token=" + GitHub;
        File.WriteAllText(transcript,
            "{\"type\":\"assistant\",\"sessionId\":\"s1\",\"message\":{\"content\":[{\"type\":\"tool_use\",\"id\":\"toolu_1\",\"name\":\"Bash\",\"input\":{\"command\":\"gh auth status\",\"description\":\"Ver la sesión de gh\"}}]}}\n" +
            "{\"type\":\"user\",\"sessionId\":\"s1\",\"message\":{\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"toolu_1\",\"content\":\"" + output + "\"}]}}\n");

        var scanner = new SecretScanner(_dir);
        var finding = scanner.Scan([transcript]).Single();

        Assert.Equal("Salida de Bash", finding.Source);
        Assert.Equal("toolu_1", finding.ToolUseId);
        Assert.Equal(output.IndexOf(GitHub, StringComparison.Ordinal), finding.Position);
        Assert.Equal(output.Length, finding.TextLength);
        Assert.True(finding.Located);

        // El texto de alrededor y la llamada se leen del transcript al pedirlos, tapados; no se guardan.
        var spot = scanner.Spot(finding)!;
        Assert.Contains("token=[" + finding.Masked + "]", spot.Context);
        Assert.DoesNotContain(GitHub, spot.Context);
        Assert.Equal("Bash: gh auth status · Ver la sesión de gh", spot.Call);
        Assert.False(Contains(File.ReadAllBytes(Path.Combine(_dir, "security", "findings.bin")), "token="));
        // El valor real solo sale cuando se pide.
        Assert.Equal(GitHub, scanner.Reveal(finding));
    }

    [Fact]
    public void Sin_su_transcript_no_hay_contexto_ni_valor()
    {
        var transcript = Path.Combine(_dir, "sesion.jsonl");
        File.WriteAllText(transcript, Line("s1", "usa " + Aws));
        var scanner = new SecretScanner(_dir);
        var finding = scanner.Scan([transcript]).Single();
        File.Delete(transcript);

        Assert.Null(scanner.Spot(finding));
        Assert.Null(scanner.Reveal(finding));
    }

    [Fact]
    public void Una_url_con_codigo_de_prueba_no_es_una_credencial()
    {
        Assert.Empty(Kinds("new URL('https://javascript:alert(1)@localhost')"));
        Assert.Empty(Kinds("fetch('http://data:x(1)y@host.local/a')"));
        Assert.Equal(["URL con credenciales"], Kinds("https://deploy:Sup3rS3cr3tP4ss@git.empresa.local/repo.git"));
    }

    [Fact]
    public void Los_hallazgos_antiguos_se_completan_una_vez_desde_su_transcript()
    {
        var transcript = Path.Combine(_dir, "sesion.jsonl");
        File.WriteAllText(transcript,
            "{\"type\":\"assistant\",\"sessionId\":\"s1\",\"message\":{\"content\":[{\"type\":\"tool_use\",\"id\":\"toolu_9\",\"name\":\"Bash\",\"input\":{\"command\":\"echo " + Aws + "\"}}]}}\n");
        var print = new SecretScanner(_dir).Fingerprint(Aws);
        // Un hallazgo como los de antes, sin dónde: llega por el formato en claro de la primera versión, que se cifra al abrir.
        File.WriteAllText(Path.Combine(_dir, "security", "findings.jsonl"),
            "{\"when_utc\":\"2026-10-01T09:00:00Z\",\"session_id\":\"s1\",\"transcript\":" + System.Text.Json.JsonSerializer.Serialize(transcript)
            + ",\"kind\":\"Clave de AWS\",\"masked\":\"" + SecretDetector.Mask(Aws) + "\",\"fingerprint\":\"" + print + "\",\"source\":\"Entrada de Bash\"}\n");
        var scanner = new SecretScanner(_dir);
        Assert.Null(scanner.Findings().Single().ToolUseId);

        Assert.Equal(1, scanner.Locate());

        var located = new SecretScanner(_dir).Findings().Single();
        Assert.Equal("s1", located.SessionId);
        Assert.Equal("toolu_9", located.ToolUseId);
        Assert.True(located.Located);
        Assert.Equal(0, scanner.Locate());
    }

    [Fact]
    public void El_analisis_guarda_mascara_y_huella_nunca_el_valor()
    {
        var transcript = Path.Combine(_dir, "sesion.jsonl");
        File.WriteAllText(transcript,
            "{\"type\":\"user\",\"sessionId\":\"s1\",\"cwd\":\"C:\\\\repos\\\\demo\",\"timestamp\":\"2026-10-02T09:00:00Z\",\"message\":{\"role\":\"user\",\"content\":\"usa este token " + GitHub + "\"}}\n" +
            "{\"type\":\"assistant\",\"sessionId\":\"s1\",\"message\":{\"content\":[{\"type\":\"tool_use\",\"id\":\"t1\",\"name\":\"Read\",\"input\":{\"file_path\":\"C:\\\\demo\\\\.env\"}}]}}\n" +
            "{\"type\":\"user\",\"sessionId\":\"s1\",\"message\":{\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"t1\",\"content\":\"DB_PASSWORD=Kq8Zr3Lw9Xv2Nb7\"}]}}\n");
        var scanner = new SecretScanner(_dir);
        var found = scanner.Scan([transcript]);

        Assert.Equal(2, found.Count);
        Assert.Equal("Tu mensaje", found[0].Source);
        Assert.Equal("demo", found[0].Project);
        Assert.Equal("Salida de Read", found[1].Source);
        Assert.Equal(@"C:\demo\.env", found[1].File);
        // Cifrado: en disco no se lee ni el valor, ni la máscara, ni de dónde salió.
        var stored = File.ReadAllBytes(Path.Combine(_dir, "security", "findings.bin"));
        foreach (var text in new[] { GitHub, "Kq8Zr3Lw9Xv2Nb7", found[0].Masked, "Salida de Read", "demo" })
            Assert.False(Contains(stored, text), text);
        Assert.Equal(2, new SecretScanner(_dir).Findings().Count);

        // Incremental: lo ya leído no se vuelve a contar.
        Assert.Empty(scanner.Scan([transcript]));
    }

    [Fact]
    public void Analiza_los_transcripts_de_Cowork_con_rutas_largas_y_sin_su_registro()
    {
        // Como los guarda Claude Desktop: cada sesión con su propia carpeta .claude\projects, y la ruta pasa de 260 caracteres.
        var cowork = Path.Combine(_dir, "local-agent-mode-sessions");
        var session = Path.Combine(cowork, Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "local_" + Guid.NewGuid());
        var folder = Path.Combine(session, ".claude", "projects", new string('x', 150) + "-outputs");
        Directory.CreateDirectory(folder);
        var transcript = Path.Combine(folder, Guid.NewGuid() + ".jsonl");
        Assert.True(transcript.Length > 260);
        File.WriteAllText(transcript, "{\"type\":\"user\",\"sessionId\":\"c1\",\"cwd\":\"C:/sesion/outputs\",\"message\":{\"content\":\"" + Aws + "\"}}\n");
        // Su registro repite la misma conversación con otro formato: no se mira, o cada secreto saldría dos veces.
        File.WriteAllText(Path.Combine(session, "audit.jsonl"), "{\"type\":\"user\",\"session_id\":\"c1\",\"message\":{\"content\":\"" + GitHub + "\"}}\n");

        var transcripts = SecretScanner.CoworkTranscripts(cowork).ToList();
        Assert.Equal([transcript], transcripts);
        var finding = new SecretScanner(_dir).Scan(transcripts).Single();
        Assert.Equal(("Cowork", "c1"), (finding.Project, finding.SessionId));
    }

    [Fact]
    public void Un_falso_positivo_marcado_no_vuelve_a_salir()
    {
        var transcript = Path.Combine(_dir, "sesion.jsonl");
        File.WriteAllText(transcript, "{\"type\":\"user\",\"sessionId\":\"s1\",\"message\":{\"content\":\"" + Aws + "\"}}\n");
        var scanner = new SecretScanner(_dir);
        var finding = scanner.Scan([transcript]).Single();
        scanner.Ignore(finding.Fingerprint);
        Assert.Empty(scanner.Findings());
    }

    [Fact]
    public void Descarta_palabras_usadas_como_valor_de_ejemplo()
    {
        Assert.Empty(Kinds("git clone https://usuario:contraseña@servidor.local/repo.git"));
        Assert.Empty(Kinds("https://user:pass@host.example/ruta"));
        Assert.Empty(Kinds("DB_PASSWORD=contraseña"));
    }

    [Fact]
    public void Un_secreto_rotado_se_marca_y_se_desmarca()
    {
        var transcript = Path.Combine(_dir, "sesion.jsonl");
        File.WriteAllText(transcript, "{\"type\":\"user\",\"sessionId\":\"s1\",\"message\":{\"content\":\"" + Aws + "\"}}\n");
        var scanner = new SecretScanner(_dir);
        var finding = scanner.Scan([transcript]).Single();
        scanner.SetRotated(finding.Fingerprint, true);
        Assert.Contains(finding.Fingerprint, new SecretScanner(_dir).Rotated());
        Assert.Single(scanner.Findings());
        scanner.SetRotated(finding.Fingerprint, false);
        Assert.Empty(scanner.Rotated());
    }

    [Fact]
    public void Un_falso_positivo_se_puede_desmarcar_solo()
    {
        var transcript = Path.Combine(_dir, "sesion.jsonl");
        File.WriteAllText(transcript, "{\"type\":\"user\",\"sessionId\":\"s1\",\"message\":{\"content\":\"" + Aws + " " + GitHub + "\"}}\n");
        var scanner = new SecretScanner(_dir);
        var found = scanner.Scan([transcript]);
        foreach (var f in found) scanner.Ignore(f.Fingerprint);
        Assert.Equal(2, scanner.IgnoredFindings().Count);

        scanner.Unignore(found[0].Fingerprint);
        Assert.Equal(found[0].Fingerprint, scanner.Findings().Single().Fingerprint);
        Assert.Equal(found[1].Fingerprint, scanner.IgnoredFindings().Single().Fingerprint);
        Assert.Equal(2, scanner.AllFindings().Count);
    }

    [Fact]
    public void Eliminar_borra_todas_las_apariciones_del_secreto_y_conserva_las_marcas()
    {
        var uno = Path.Combine(_dir, "uno.jsonl");
        var dos = Path.Combine(_dir, "dos.jsonl");
        File.WriteAllText(uno, Line("s1", Aws) + Line("s1", GitHub));
        File.WriteAllText(dos, Line("s2", Aws));
        var scanner = new SecretScanner(_dir);
        scanner.Scan([uno, dos]);
        var aws = scanner.Findings().First(f => f.SessionId == "s2").Fingerprint;
        scanner.Ignore(aws);

        Assert.Equal(2, scanner.Delete(new HashSet<string> { aws }));
        Assert.DoesNotContain(new SecretScanner(_dir).AllFindings(), f => f.Fingerprint == aws);
        Assert.Single(scanner.Findings());
        Assert.Equal(1, scanner.IgnoredCount());

        // Lo ya leído no se relee: lo eliminado no vuelve al analizar otra vez.
        Assert.Empty(scanner.Scan([uno, dos]));
        Assert.Equal(0, scanner.Delete(new HashSet<string> { aws }));
    }

    [Fact]
    public void Los_secretos_afectados_son_los_que_comparten_algun_fichero()
    {
        SecretFinding In(string fingerprint, string? file) => new() { Fingerprint = fingerprint, File = file };
        var findings = new[]
        {
            In("a", @"C:\Pruebas\secretos.txt"), In("a", null),
            In("b", @"c:\pruebas\SECRETOS.TXT"),
            In("c", @"C:\Pruebas\otro.txt"),
            In("d", null),
        };
        Assert.Equal(new[] { "b" }, SecretReport.SharingFiles(findings, "a"));
        Assert.Equal(new[] { "a" }, SecretReport.SharingFiles(findings, "b"));
        Assert.Empty(SecretReport.SharingFiles(findings, "c"));
        // Sin fichero no hay nada que compartir, aunque otro tampoco tenga.
        Assert.Empty(SecretReport.SharingFiles(findings, "d"));
    }

    [Fact]
    public void El_analisis_no_busca_claves_dentro_de_una_imagen()
    {
        var google = "AI" + "za" + "SyD3f8kLmN0pQrStUvWxYz1234567890abc";
        // Otra en la imagen: si la de la imagen contara, no se juntaría con la del texto y saldrían dos.
        var noise = "AI" + "za" + "SyQ9w8e7r6t5y4u3i2o1pAsDfGhJkLzXcVb";
        string Result(string block) =>
            "{\"type\":\"user\",\"sessionId\":\"s1\",\"message\":{\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"t1\",\"content\":[" + block + "]}]}}\n";
        var transcript = Path.Combine(_dir, "imagen.jsonl");
        File.WriteAllText(transcript,
            Result("{\"type\":\"image\",\"source\":{\"type\":\"base64\",\"media_type\":\"image/png\",\"data\":\"iVBORw0KGgo/" + noise + "/AAAA\"}}")
            + Result("{\"type\":\"text\",\"text\":\"key=" + google + "\"}"));

        // Solo la del texto: la de la imagen es ruido del base64.
        Assert.Single(new SecretScanner(_dir).Scan([transcript]));
    }

    /// <summary>Una línea de transcript con un mensaje de la persona en una sesión.</summary>
    private static string Line(string session, string text) =>
        "{\"type\":\"user\",\"sessionId\":\"" + session + "\",\"message\":{\"content\":\"" + text + "\"}}\n";

    [Fact]
    public void Los_ficheros_en_claro_de_la_primera_version_se_cifran_y_se_borran()
    {
        var security = Path.Combine(_dir, "security");
        Directory.CreateDirectory(security);
        File.WriteAllText(Path.Combine(security, "findings.jsonl"),
            "{\"session_id\":\"s1\",\"kind\":\"Clave de AWS\",\"masked\":\"AKIA…LMNP\",\"fingerprint\":\"F1\"}\n" +
            "{\"session_id\":\"s2\",\"kind\":\"Token JWT\",\"masked\":\"eyJh…UJU1\",\"fingerprint\":\"F2\"}\n");
        File.WriteAllText(Path.Combine(security, "ignored.json"), "[\"F2\"]");

        var scanner = new SecretScanner(_dir);

        Assert.False(File.Exists(Path.Combine(security, "findings.jsonl")));
        Assert.False(File.Exists(Path.Combine(security, "ignored.json")));
        Assert.Equal("F1", scanner.Findings().Single().Fingerprint);
        Assert.Equal(1, scanner.IgnoredCount());
        scanner.ClearIgnored();
        Assert.Equal(2, scanner.Findings().Count);
    }

    [Fact]
    public void El_resumen_agrupa_por_secreto_y_cuenta_proyectos()
    {
        var at = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
        var findings = new[]
        {
            new SecretFinding { Fingerprint = "A", Kind = "Token JWT", SessionId = "s1", Project = "taller", WhenUtc = at },
            new SecretFinding { Fingerprint = "A", Kind = "Token JWT", SessionId = "s2", Project = "taller", WhenUtc = at.AddHours(2) },
            new SecretFinding { Fingerprint = "A", Kind = "Token JWT", SessionId = "s3", Project = "tienda", WhenUtc = at.AddHours(1) },
            new SecretFinding { Fingerprint = "B", Kind = "Variable con secreto", SessionId = "s3", Project = "tienda", WhenUtc = at },
        };

        var groups = SecretReport.Group(findings);

        Assert.Equal(["A", "B"], groups.Select(g => g.Fingerprint));
        Assert.Equal(3, groups[0].Sessions);
        Assert.Equal(at.AddHours(2), groups[0].LastUtc);
        Assert.Equal(["taller", "tienda"], groups[0].Projects);
        Assert.Equal([("tienda", 2), ("taller", 1)], SecretReport.ByProject(groups));
        Assert.Equal([("Token JWT", 1), ("Variable con secreto", 1)], SecretReport.ByKind(groups));
    }

    [Fact]
    public void En_un_worktree_el_proyecto_es_el_repositorio_y_la_carpeta_va_aparte()
    {
        var at = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
        var findings = new[]
        {
            new SecretFinding { Fingerprint = "A", Kind = "Token JWT", SessionId = "s1", Project = "develop", Repo = "Tienda", WhenUtc = at },
            new SecretFinding { Fingerprint = "A", Kind = "Token JWT", SessionId = "s2", Project = "DEVLAB-86", Repo = "Tienda", WhenUtc = at },
            new SecretFinding { Fingerprint = "A", Kind = "Token JWT", SessionId = "s3", Project = "claude-stats", WhenUtc = at },
        };

        var group = SecretReport.Group(findings).Single();

        Assert.Equal(["Tienda", "claude-stats"], group.Projects);
        Assert.Equal(["develop", "DEVLAB-86"], group.Worktrees);
        Assert.Equal("Tienda · develop", SecretReport.PlaceOf(findings[0]));
        Assert.Equal("claude-stats", SecretReport.PlaceOf(findings[2]));
    }

    [Fact]
    public void El_historial_de_prompts_recoge_lo_escrito_y_lo_pegado()
    {
        var history = Path.Combine(_dir, "history.jsonl");
        var pastes = Path.Combine(_dir, "paste-cache");
        File.WriteAllText(history,
            Prompt("s1", "usa este token " + GitHub, 1759395600000) +
            Prompt("s1", "[Pasted text #1 +2 lines]", 1759395660000, "\"1\":{\"id\":1,\"type\":\"text\",\"content\":\"aws_access_key_id = " + Aws + "\"}"));
        var scanner = new SecretScanner(_dir);
        var found = scanner.ScanHistory(history, pastes);

        Assert.Equal(["Historial de prompts", "Texto pegado"], found.Select(f => f.Source));
        Assert.All(found, f => Assert.Equal("s1", f.SessionId));
        Assert.Equal("demo", found[0].Project);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1759395600000).UtcDateTime, found[0].WhenUtc);

        // Incremental, como un transcript.
        Assert.Empty(scanner.ScanHistory(history, pastes));

        // El prompt se puede volver a ver, con la clave tapada; también tras guardarlo y leerlo cifrado.
        var stored = new SecretScanner(_dir).Findings();
        Assert.All(stored, f => Assert.True(SecretScanner.IsPrompt(f)));
        var prompt = SecretScanner.PromptText(stored.Single(f => f.Source == "Historial de prompts"));
        Assert.Equal("usa este token " + SecretDetector.Mask(GitHub), prompt);
        var pasted = SecretScanner.PromptText(stored.Single(f => f.Source == "Texto pegado"))!;
        Assert.Contains(SecretDetector.Mask(Aws), pasted);
        Assert.DoesNotContain(Aws, pasted);
    }

    [Fact]
    public void Un_texto_pegado_toma_la_sesion_del_prompt_que_lo_cita()
    {
        var history = Path.Combine(_dir, "history.jsonl");
        var pastes = Path.Combine(_dir, "paste-cache");
        Directory.CreateDirectory(pastes);
        File.WriteAllText(Path.Combine(pastes, "a1b2c3d4e5f6a7b8.txt"), "ANTHROPIC_KEY " + Anthropic);
        var scanner = new SecretScanner(_dir);

        // Pegado, pero ningún prompt lo cita todavía: no se ha enviado.
        Assert.Empty(scanner.ScanHistory(history, pastes));

        File.WriteAllText(history, Prompt("s2", "[Pasted text #1 +40 lines]", 1759395600000,
            "\"1\":{\"id\":1,\"type\":\"text\",\"contentHash\":\"a1b2c3d4e5f6a7b8\"}"));
        var finding = scanner.ScanHistory(history, pastes).Single();

        Assert.Equal("Texto pegado", finding.Source);
        Assert.Equal("s2", finding.SessionId);
        Assert.Equal("Clave de Anthropic", finding.Kind);
        Assert.Empty(scanner.ScanHistory(history, pastes));
        Assert.Equal("ANTHROPIC_KEY " + SecretDetector.Mask(Anthropic), SecretScanner.PromptText(finding));

        // Si Claude Code ya lo ha borrado, no hay nada que enseñar.
        File.Delete(Path.Combine(pastes, "a1b2c3d4e5f6a7b8.txt"));
        Assert.Null(SecretScanner.PromptText(finding));
    }

    [Fact]
    public void El_historial_no_repite_lo_que_ya_salio_en_el_transcript_de_la_sesion()
    {
        var transcript = Path.Combine(_dir, "sesion.jsonl");
        File.WriteAllText(transcript, "{\"type\":\"user\",\"sessionId\":\"s1\",\"message\":{\"content\":\"usa este token " + GitHub + "\"}}\n");
        var history = Path.Combine(_dir, "history.jsonl");
        // La misma clave en otra sesión cuyo transcript ya no existe: esa sí es nueva.
        File.WriteAllText(history, Prompt("s1", "usa este token " + GitHub, 1759395600000) + Prompt("s9", "y aquí " + GitHub, 1759395660000));
        var scanner = new SecretScanner(_dir);

        Assert.Single(scanner.Scan([transcript]));
        Assert.Equal("s9", scanner.ScanHistory(history, Path.Combine(_dir, "paste-cache")).Single().SessionId);
    }

    [Fact]
    public void Encuentra_claves_escritas_en_la_configuracion_y_no_las_referencias()
    {
        var project = Path.Combine(_dir, "demo");
        Directory.CreateDirectory(project);
        var settings = Path.Combine(_dir, "settings.json");
        File.WriteAllText(settings, "{ \"env\": { \"GITHUB_TOKEN\": \"" + GitHub + "\", \"OTRO_TOKEN\": \"${OTRO_TOKEN}\", \"IDIOMA\": \"es\" } }");
        var mcp = Path.Combine(project, ".mcp.json");
        File.WriteAllText(mcp,
            "{ \"mcpServers\": { \"jira\": { \"command\": \"npx\", \"env\": { \"JIRA_API_TOKEN\": \"Qw8eR7tY6uI5oP4aS3dF\" } }," +
            " \"github\": { \"env\": { \"GITHUB_TOKEN\": \"${GITHUB_TOKEN}\" } } } }");
        var state = Path.Combine(_dir, ".claude.json");
        File.WriteAllText(state, "{ \"projects\": { \"" + project.Replace("\\", "/") + "\": { \"mcpServers\": { \"api\": { \"headers\": { \"Authorization\": \"Bearer k9Jh2Lm4Np6Qr8St0Vw1Xy\" } } } } } }");
        var scanner = new SecretScanner(_dir);

        var written = scanner.ScanConfig([
            new ConfigFile(settings, ConfigKind.Settings),
            new ConfigFile(mcp, ConfigKind.Mcp, project),
            new ConfigFile(state, ConfigKind.ClaudeState),
            new ConfigFile(Path.Combine(_dir, "no-existe.json"), ConfigKind.Settings),
        ]);

        Assert.Equal(["Variable GITHUB_TOKEN en env", "Servidor MCP «jira»", "Servidor MCP «api»"], written.Select(f => f.Source));
        Assert.All(written, f => Assert.True(f.Written));
        Assert.Equal([settings, mcp, state], written.Select(f => f.File));
        Assert.Equal("demo", written[1].Project);
        Assert.Equal("demo", written[2].Project);
        Assert.Equal(3, new SecretScanner(_dir).Findings().Count);
        // Cifrado, como los hallazgos.
        Assert.False(Contains(File.ReadAllBytes(Path.Combine(_dir, "security", "written.bin")), GitHub));
    }

    [Fact]
    public void Una_clave_quitada_del_fichero_deja_de_salir()
    {
        var settings = Path.Combine(_dir, "settings.json");
        File.WriteAllText(settings, "{ \"env\": { \"GITHUB_TOKEN\": \"" + GitHub + "\" } }");
        var scanner = new SecretScanner(_dir);
        var file = new ConfigFile(settings, ConfigKind.Settings);
        Assert.Single(scanner.ScanConfig([file]));

        File.WriteAllText(settings, "{ \"env\": { \"GITHUB_TOKEN\": \"${GITHUB_TOKEN}\" } }");
        scanner.ScanConfig([file]);

        Assert.Empty(scanner.Findings());
    }

    [Fact]
    public void Una_clave_escrita_se_puede_marcar_pero_no_cuenta_como_sesion()
    {
        var settings = Path.Combine(_dir, "settings.json");
        File.WriteAllText(settings, "{ \"env\": { \"GITHUB_TOKEN\": \"" + GitHub + "\" } }");
        var transcript = Path.Combine(_dir, "sesion.jsonl");
        File.WriteAllText(transcript, "{\"type\":\"user\",\"sessionId\":\"s1\",\"message\":{\"content\":\"usa este token " + GitHub + "\"}}\n");
        var scanner = new SecretScanner(_dir);
        scanner.Scan([transcript]);
        scanner.ScanConfig([new ConfigFile(settings, ConfigKind.Settings)]);

        var group = SecretReport.Group(scanner.Findings()).Single();
        Assert.True(group.Sent);
        Assert.True(group.InFile);
        Assert.Equal(1, group.Sessions);

        // La marca va con el valor: resuelve lo enviado y lo escrito a la vez.
        scanner.Ignore(group.Fingerprint);
        Assert.Empty(scanner.Findings());
        Assert.Equal(2, scanner.IgnoredFindings().Count);
        // Eliminar solo borra lo enviado.
        Assert.DoesNotContain(scanner.AllFindings(), f => f.Written);
    }

    /// <summary>Una línea del historial de prompts, en el proyecto «demo», con lo pegado que se le pase.</summary>
    private static string Prompt(string session, string display, long timestampMs, string pasted = "") =>
        "{\"display\":\"" + display + "\",\"pastedContents\":{" + pasted + "},\"timestamp\":" + timestampMs +
        ",\"project\":\"C:\\\\repos\\\\demo\",\"sessionId\":\"" + session + "\"}\n";

    /// <summary>Si unos bytes contienen un texto, en UTF-8.</summary>
    private static bool Contains(byte[] bytes, string text)
    {
        var needle = System.Text.Encoding.UTF8.GetBytes(text);
        for (var i = 0; i + needle.Length <= bytes.Length; i++)
            if (bytes.AsSpan(i, needle.Length).SequenceEqual(needle)) return true;
        return false;
    }

    [Fact]
    public void La_huella_es_la_misma_para_el_mismo_secreto_y_no_es_un_hash_simple()
    {
        var scanner = new SecretScanner(_dir);
        Assert.Equal(scanner.Fingerprint(GitHub), new SecretScanner(_dir).Fingerprint(GitHub));
        var plain = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(GitHub)))[..24];
        Assert.NotEqual(plain, scanner.Fingerprint(GitHub));
    }
}
