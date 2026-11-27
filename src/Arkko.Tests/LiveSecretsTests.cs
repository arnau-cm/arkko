using System.Text.Json;
using Arkko.Core;

namespace Arkko.Tests;

/// <remarks>Los secretos de prueba se montan por partes, como en SecretDetectorTests.</remarks>
public sealed class LiveSecretsTests
{
    private static readonly string GitHub = "gh" + "p_" + "R4nd0mT0k3nV4lu3Abc9XyZ8wQ7eP6oL5kJ4";
    private static readonly string Aws = "AK" + "IA" + "Q3EGRT7XYZW5LMNP";

    /// <summary>El JSON de un PostToolUse con la entrada y la salida dadas.</summary>
    private static JsonElement Hook(string tool, object input, object? response) =>
        JsonSerializer.SerializeToElement(new { hook_event_name = "PostToolUse", tool_name = tool, tool_input = input, tool_response = response });

    [Fact]
    public void Avisa_de_un_secreto_en_la_salida_con_la_herramienta_y_el_fichero()
    {
        var root = Hook("Read", new { file_path = @"C:\repo\.env" }, new { type = "text", file = new { content = "GITHUB_TOKEN=" + GitHub } });
        Assert.Equal("Token de GitHub (salida de Read · .env)", LiveSecrets.ForTool(root));
    }

    [Fact]
    public void No_avisa_de_lo_que_parece_una_clave_dentro_de_una_imagen()
    {
        var google = "AI" + "za" + "SyD3f8kLmN0pQrStUvWxYz1234567890abc";
        var image = new { type = "image", file = new { base64 = "iVBORw0KGgo/" + google + "/AAAA", type = "image/png" } };
        Assert.Null(LiveSecrets.ForTool(Hook("Read", new { file_path = @"C:\tmp\captura.png" }, image)));

        // La misma clave en un texto sí avisa: la regla no se ha perdido.
        Assert.Equal("Clave de Google (salida de Read · notas.txt)",
            LiveSecrets.ForTool(Hook("Read", new { file_path = @"C:\tmp\notas.txt" }, new { type = "text", file = new { content = "key=" + google } })));
    }

    [Fact]
    public void En_una_salida_enorme_mira_el_principio_y_el_final()
    {
        var filler = new string('x', 300 * 1024);
        Assert.Equal("Token de GitHub (salida de Bash)", LiveSecrets.ForTool(Hook("Bash", new { command = "type log" }, filler + filler + " " + GitHub)));
        Assert.Equal("Token de GitHub (salida de Bash)", LiveSecrets.ForTool(Hook("Bash", new { command = "type log" }, GitHub + " " + filler + filler)));

        // Lo de en medio no avisa al momento: lo encuentra el análisis de los transcripts.
        Assert.Null(LiveSecrets.ForTool(Hook("Bash", new { command = "type log" }, filler + " " + GitHub + " " + filler)));
    }

    [Fact]
    public void No_avisa_del_fichero_entero_que_Claude_Code_guarda_al_editar()
    {
        // La respuesta de Edit lleva el fichero de antes y el diff, que no se mandan al modelo: lo enviado es la edición.
        var response = new { filePath = @"C:\repo\Tests.cs", oldString = "uno", newString = "dos", originalFile = "token " + GitHub,
            structuredPatch = new[] { new { lines = new[] { "+" + Aws } } } };
        Assert.Null(LiveSecrets.ForTool(Hook("Edit", new { file_path = @"C:\repo\Tests.cs", old_string = "uno", new_string = "dos" }, response)));

        // Lo que se escribe sí va en la llamada, y de eso sí avisa.
        Assert.Equal("Token de GitHub (entrada de Edit · Tests.cs)",
            LiveSecrets.ForTool(Hook("Edit", new { file_path = @"C:\repo\Tests.cs", old_string = "uno", new_string = GitHub }, response)));
    }

    [Fact]
    public void Avisa_de_la_entrada_y_de_la_salida_por_separado()
    {
        var root = Hook("Bash", new { command = "echo " + Aws }, "listo " + GitHub);
        Assert.Equal("Clave de AWS (entrada de Bash); Token de GitHub (salida de Bash)", LiveSecrets.ForTool(root));
    }

    [Fact]
    public void Sin_secretos_no_hay_aviso()
    {
        Assert.Null(LiveSecrets.ForTool(Hook("Read", new { file_path = @"C:\repo\README.md" }, "# Proyecto\nNada que ocultar.")));
        Assert.Null(LiveSecrets.ForTool(Hook("Bash", new { command = "dir" }, null)));
    }

    [Fact]
    public void No_avisa_de_lo_resuelto_en_seguridad_y_apunta_las_huellas_de_lo_demas()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cs-live-" + Guid.NewGuid().ToString("N"));
        try
        {
            var scanner = new SecretScanner(dir);
            scanner.Ignore(scanner.Fingerprint(GitHub));
            scanner.SetRotated(scanner.Fingerprint(Aws), true);

            var root = Hook("Bash", new { command = "echo " + Aws }, "listo " + GitHub);
            Assert.Null(LiveSecrets.ForTool(root, new SettledSecrets(dir)));

            var other = "gh" + "p_" + "Z9y8X7w6V5u4T3s2R1q0P9o8N7m6L5k4J3i2";
            var fingerprints = new List<string>();
            Assert.Equal("Token de GitHub (salida de Bash)",
                LiveSecrets.ForTool(Hook("Bash", new { command = "dir" }, $"{GitHub} y {other}"), new SettledSecrets(dir), fingerprints));
            Assert.Equal([scanner.Fingerprint(other)], fingerprints);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Un_prompt_con_secretos_da_sus_tipos_sin_repetir_y_el_motivo_del_bloqueo_no_lleva_el_valor()
    {
        var kinds = LiveSecrets.Kinds($"usa {GitHub} y luego {Aws}, otra vez {GitHub}");
        Assert.Equal(["Token de GitHub", "Clave de AWS"], kinds);
        Assert.Empty(LiveSecrets.Kinds("hola, ¿qué tal?"));

        var reason = LiveSecrets.BlockReason(kinds);
        Assert.Contains("Token de GitHub", reason);
        Assert.DoesNotContain(GitHub, reason);
        Assert.DoesNotContain(Aws, LiveSecrets.Message(LiveSecrets.ForPrompt(kinds)));
    }
}
