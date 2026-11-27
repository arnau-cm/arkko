using Arkko.Core;

namespace Arkko.Tests;

public sealed class TranscriptReaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "arkko-tests-" + Guid.NewGuid().ToString("N"));

    public TranscriptReaderTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Cuenta_una_vez_cada_mensaje_repetido_y_separa_por_modelo()
    {
        var path = Write("s1.jsonl",
            User("2026-09-29T10:00:00Z", "prompt anterior"),
            Assistant("2026-09-29T10:00:01Z", "m0", "claude-opus-5-5", 999, 999),
            User("2026-09-29T10:01:00Z", "hola"),
            Assistant("2026-09-29T10:01:02Z", "m1", "claude-opus-5-5", 10, 100),
            Assistant("2026-09-29T10:01:02Z", "m1", "claude-opus-5-5", 10, 100),
            ToolResult("2026-09-29T10:01:03Z"),
            Assistant("2026-09-29T10:01:08Z", "m2", "claude-fable-5-1", 5, 50));

        var usage = TranscriptReader.LastTurn(path)!;

        Assert.Equal(100, usage.ByModel["claude-opus-5-5"].Output);
        Assert.Equal(1, usage.ByModel["claude-opus-5-5"].ApiCalls);
        Assert.Equal(50, usage.ByModel["claude-fable-5-1"].Output);
        Assert.Equal(8000, usage.DurationMs);
    }

    [Fact]
    public void Suma_los_subagentes_posteriores_al_prompt()
    {
        var path = Write("s2.jsonl",
            User("2026-09-29T10:00:00Z", "hola"),
            Assistant("2026-09-29T10:00:05Z", "m1", "claude-opus-5-5", 1, 10));
        var sub = Path.Combine(_dir, "s2", "subagents");
        Directory.CreateDirectory(sub);
        File.WriteAllLines(Path.Combine(sub, "agent-a.jsonl"),
        [
            Assistant("2026-09-29T09:00:00Z", "old", "claude-fable-5-1", 1, 777),
            Assistant("2026-09-29T10:00:03Z", "s1", "claude-fable-5-1", 1, 20),
        ]);

        var usage = TranscriptReader.LastTurn(path)!;

        Assert.Equal(20, usage.ByModel["claude-fable-5-1"].Output);
    }

    [Fact]
    public void Sin_prompt_humano_devuelve_null()
    {
        var path = Write("s3.jsonl", ToolResult("2026-09-29T10:00:00Z"));
        Assert.Null(TranscriptReader.LastTurn(path));
    }

    [Fact]
    public void Una_compactacion_a_mitad_de_turno_no_lo_parte()
    {
        var path = Write("s6.jsonl",
            User("2026-09-29T10:00:00Z", "hola"),
            Assistant("2026-09-29T10:00:02Z", "m1", "claude-opus-5-5", 1, 30),
            """{"type":"user","isCompactSummary":true,"timestamp":"2026-09-29T10:00:03Z","message":{"role":"user","content":"This session is being continued"}}""",
            Assistant("2026-09-29T10:00:05Z", "m2", "claude-opus-5-5", 1, 20));

        var usage = TranscriptReader.LastTurn(path)!;

        Assert.Equal(50, usage.ByModel["claude-opus-5-5"].Output);
    }

    [Fact]
    public void Un_turno_que_abre_un_comando_de_consola_dice_su_comando_y_no_el_prompt_anterior()
    {
        var path = Write("s7.jsonl",
            User("2026-09-29T10:00:00Z", "despliega"),
            Assistant("2026-09-29T10:00:02Z", "m1", "claude-opus-5-5", 1, 30),
            User("2026-09-29T10:01:00Z", "<bash-input> powershell -File ./scripts/deploy.ps1</bash-input>"),
            User("2026-09-29T10:01:20Z", "<bash-stdout>Instalada</bash-stdout><bash-stderr></bash-stderr>"),
            Assistant("2026-09-29T10:01:25Z", "m2", "claude-opus-5-5", 1, 20));

        var usage = TranscriptReader.LastTurn(path, out var opening)!;

        Assert.Equal(20, usage.ByModel["claude-opus-5-5"].Output);
        Assert.Equal((PromptKind.Shell, "powershell -File ./scripts/deploy.ps1"), opening);
    }

    [Fact]
    public void Un_turno_que_abre_un_aviso_se_reconoce_como_aviso()
    {
        var path = Write("s8.jsonl",
            User("2026-09-29T10:00:00Z", "<task-notification><task-id>b1</task-id><summary>Espera terminada</summary></task-notification>"),
            Assistant("2026-09-29T10:00:02Z", "m1", "claude-opus-5-5", 1, 30));

        TranscriptReader.LastTurn(path, out var opening);

        Assert.Equal(PromptKind.Notification, opening.Kind);
        Assert.Equal("Espera terminada", UserMessage.NotificationText(opening.Text!));
    }

    [Fact]
    public void Un_prompt_normal_abre_un_turno_de_prompt()
    {
        var path = Write("s9.jsonl", User("2026-09-29T10:00:00Z", "hola"), Assistant("2026-09-29T10:00:02Z", "m1", "claude-opus-5-5", 1, 30));

        TranscriptReader.LastTurn(path, out var opening);

        Assert.Equal((PromptKind.Prompt, "hola"), opening);
    }

    [Fact]
    public void El_registro_guarda_el_tipo_solo_si_no_es_un_prompt_y_enseña_cada_uno_a_su_manera()
    {
        var path = Path.Combine(_dir, "prompts.jsonl");
        JsonStore.AppendLine(path, new PromptRecord { SessionId = "a", Prompt = "hola" });
        JsonStore.AppendLine(path, new PromptRecord { SessionId = "b", Prompt = "deploy.ps1", Kind = PromptKind.Shell });
        JsonStore.AppendLine(path, new PromptRecord { SessionId = "c", Prompt = "<task-notification><summary>Hecho</summary></task-notification>" });

        var lines = File.ReadAllLines(path);
        Assert.DoesNotContain("\"kind\"", lines[0]);
        Assert.Contains("\"kind\":\"shell\"", lines[1]);
        var read = JsonStore.ReadLines<PromptRecord>(path);
        Assert.Equal(["hola", "! deploy.ps1", "Aviso: Hecho"], read.Select(r => r.Shown));
        Assert.Equal([true, false, false], read.Select(r => r.ByPerson));
    }

    [Fact]
    public void El_recap_es_el_ultimo_away_summary()
    {
        var path = Write("s4.jsonl",
            """{"type":"system","subtype":"away_summary","content":"Resumen viejo","timestamp":"2026-09-29T09:00:00Z"}""",
            User("2026-09-29T10:00:00Z", "hola"),
            """{"type":"system","subtype":"away_summary","content":"Hecho el commit; te toca revisar la PR.","timestamp":"2026-09-29T10:05:00Z"}""",
            """{"type":"system","subtype":"turn_duration","content":"x"}""");

        var recap = TranscriptReader.LatestRecap(path)!.Value;
        Assert.Equal("Hecho el commit; te toca revisar la PR.", recap.Text);
        Assert.Equal(new DateTime(2026, 9, 29, 10, 5, 0, DateTimeKind.Utc), recap.Utc);
    }

    [Fact]
    public void Sin_recap_devuelve_null()
    {
        var path = Write("s5.jsonl", User("2026-09-29T10:00:00Z", "hola"));
        Assert.Null(TranscriptReader.LatestRecap(path));
    }

    [Theory]
    [InlineData("claude-fable-5-1", "Fable 5.1")]
    [InlineData("claude-opus-5-5", "Opus 5.5")]
    [InlineData("claude-haiku-4-5-20251001", "Haiku 4.5")]
    public void Nombre_corto_de_modelo(string id, string expected) => Assert.Equal(expected, Format.ModelName(id));

    [Theory]
    [InlineData("five_hour", "5 h")]
    [InlineData("seven_day", "7 días")]
    [InlineData("seven_day_fable", "7 días Fable")]
    public void Etiqueta_de_limite(string key, string expected) => Assert.Equal(expected, LimitLabels.For(key));

    private string Write(string name, params string[] lines)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllLines(path, lines);
        return path;
    }

    private static string User(string ts, string text) =>
        $$$"""{"type":"user","timestamp":"{{{ts}}}","message":{"role":"user","content":"{{{text}}}"}}""";

    private static string ToolResult(string ts) =>
        $$$"""{"type":"user","timestamp":"{{{ts}}}","message":{"role":"user","content":[{"type":"tool_result","content":"ok"}]}}""";

    private static string Assistant(string ts, string id, string model, int input, int output) =>
        $$$$"""{"type":"assistant","timestamp":"{{{{ts}}}}","message":{"id":"{{{{id}}}}","model":"{{{{model}}}}","usage":{"input_tokens":{{{{input}}}},"output_tokens":{{{{output}}}},"cache_creation_input_tokens":0,"cache_read_input_tokens":0}}}""";
}
