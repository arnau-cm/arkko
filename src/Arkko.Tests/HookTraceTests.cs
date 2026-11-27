using System.Text.Json;
using System.Text.Json.Nodes;
using Arkko.Core;

namespace Arkko.Tests;

/// <remarks>Cada prueba escribe en una carpeta temporal un transcript con las entradas que Claude Code apunta de los
/// hooks, con la forma que tienen de verdad.</remarks>
public sealed class HookTraceTests : IDisposable
{
    private const string Ak = "\"C:/Users/x/AppData/Local/ClaudeStats/bin/ak.exe\" hook";
    private const string NoDotNet = "Failed with non-blocking status code: You must install .NET to run this application.\r\n\r\nApp: C:\\Users\\x\\AppData\\Local\\ClaudeStats\\bin\\ak.exe\r\n.NET location: Not found\r\nFailed to resolve hostfxr.dll [not found]. Error code: 0x80008083";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "cs-hooks-" + Guid.NewGuid().ToString("N"));
    private readonly string _transcript;

    public HookTraceTests()
    {
        Directory.CreateDirectory(_folder);
        _transcript = Path.Combine(_folder, "sesion.jsonl");
        File.WriteAllText(_transcript, "");
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    /// <summary>Añade al transcript una línea con el objeto dado, como hace Claude Code.</summary>
    private void Append(object entry) => File.AppendAllText(_transcript, JsonSerializer.Serialize(entry) + "\n");

    /// <summary>La entrada de un hook ejecutado: bien con 0 o mal con su código y su stderr.</summary>
    private void Run(string time, string evt, int exit, string command = Ak, string? stderr = null) => Append(new
    {
        type = "attachment",
        timestamp = time,
        attachment = new
        {
            type = exit == 0 ? "hook_success" : "hook_non_blocking_error",
            hookName = evt, hookEvent = evt, command, exitCode = exit, stderr = stderr ?? "", stdout = "",
        },
    });

    /// <summary>El resumen del Stop que Claude Code escribe al acabar el turno, con sus errores.</summary>
    private void Summary(string time, params string[] errors) => Append(new
    {
        type = "system", subtype = "stop_hook_summary", timestamp = time, hookCount = 1,
        hookInfos = new[] { new { command = Ak, durationMs = 479 } }, hookErrors = errors,
    });

    [Fact]
    public void Un_Stop_que_no_arranca_deja_el_motivo_y_el_turno_por_cerrar()
    {
        Run("2026-11-05T07:58:52.187Z", "PostToolUse", 0);
        Run("2026-11-05T07:59:04.509Z", "Stop", 131, stderr: NoDotNet);
        Summary("2026-11-05T07:59:04.516Z", NoDotNet);

        var trace = new HookTrace();
        Assert.True(trace.Read(_transcript));

        Assert.Equal(new HookRun(new DateTime(2026, 11, 5, 7, 59, 4, 509, DateTimeKind.Utc), "Stop", false, "No encuentra .NET"), trace.Last);
        Assert.Equal(new DateTime(2026, 11, 5, 7, 59, 4, 516, DateTimeKind.Utc), trace.MissedStopUtc);
    }

    [Fact]
    public void Un_hook_que_vuelve_a_funcionar_quita_el_fallo_pero_no_el_turno_que_falto()
    {
        Run("2026-11-05T07:59:04.509Z", "Stop", 131, stderr: NoDotNet);
        var trace = new HookTrace();
        trace.Read(_transcript);
        Assert.False(trace.Last!.Ok);

        Run("2026-11-05T08:10:00.000Z", "PostToolUse", 0);
        Assert.True(trace.Read(_transcript));
        Assert.True(trace.Last!.Ok);
        Assert.Equal(new DateTime(2026, 11, 5, 7, 59, 4, 509, DateTimeKind.Utc), trace.MissedStopUtc);
    }

    [Fact]
    public void Los_hooks_de_otros_no_cuentan()
    {
        Run("2026-11-05T08:00:00.000Z", "PreToolUse", 2, command: "node C:/arkko-stack/scripts/sql-guard.mjs", stderr: "Bloqueado: SQL");

        var trace = new HookTrace();
        Assert.False(trace.Read(_transcript));
        Assert.Null(trace.Last);
    }

    [Fact]
    public void Solo_lee_lo_nuevo_y_deja_para_luego_una_linea_a_medias()
    {
        Run("2026-11-05T08:00:00.000Z", "PostToolUse", 0);
        var trace = new HookTrace();
        trace.Read(_transcript);
        Assert.False(trace.Read(_transcript));

        var line = JsonSerializer.Serialize(new
        {
            type = "attachment", timestamp = "2026-11-05T08:01:00.000Z",
            attachment = new { type = "hook_non_blocking_error", hookEvent = "UserPromptSubmit", command = Ak, exitCode = 131, stderr = NoDotNet },
        });
        File.AppendAllText(_transcript, line[..40]);
        Assert.False(trace.Read(_transcript));
        Assert.True(trace.Last!.Ok);

        File.AppendAllText(_transcript, line[40..] + "\n");
        Assert.True(trace.Read(_transcript));
        Assert.Equal("UserPromptSubmit", trace.Last!.Event);
        Assert.Null(trace.MissedStopUtc);
    }

    [Fact]
    public void La_primera_lectura_empieza_cerca_del_final_en_una_linea_entera()
    {
        Run("2026-11-05T07:00:00.000Z", "Stop", 131, stderr: NoDotNet);
        var filler = JsonSerializer.Serialize(new { type = "assistant", text = new string('x', 2000) });
        File.AppendAllLines(_transcript, Enumerable.Repeat(filler, (int)(HookTrace.FirstWindow / 2000) + 10));
        Run("2026-11-05T08:00:00.000Z", "PostToolUse", 0);

        var trace = new HookTrace();
        trace.Read(_transcript);
        Assert.True(trace.Last!.Ok);
        Assert.Null(trace.MissedStopUtc);
    }

    [Fact]
    public void En_la_traza_el_fallo_va_en_su_llamada_o_en_el_ultimo_paso()
    {
        Append(new { type = "user", timestamp = "2026-11-05T07:58:00.000Z", message = new { role = "user", content = "Repasa el informe" } });
        Append(new
        {
            type = "assistant", timestamp = "2026-11-05T07:58:10.000Z",
            message = new { id = "m1", model = "claude-opus-5-5", content = new object[] { new { type = "tool_use", id = "tu1", name = "Read", input = new { file_path = "a.md" } } } },
        });
        Append(new { type = "user", timestamp = "2026-11-05T07:58:11.000Z", message = new { role = "user", content = new object[] { new { type = "tool_result", tool_use_id = "tu1", content = "ok" } } } });
        Append(new
        {
            type = "attachment", timestamp = "2026-11-05T07:58:11.500Z",
            attachment = new { type = "hook_non_blocking_error", hookEvent = "PostToolUse", toolUseID = "tu1", command = Ak, exitCode = 131, stderr = NoDotNet },
        });
        Append(new
        {
            type = "assistant", timestamp = "2026-11-05T07:59:00.000Z",
            message = new { id = "m2", model = "claude-opus-5-5", content = new object[] { new { type = "text", text = "Repasado." } } },
        });
        Run("2026-11-05T07:59:04.509Z", "Stop", 131, stderr: NoDotNet);

        var steps = SessionTraceReader.Read(_transcript).Steps;
        Assert.Equal("El hook de Arkko falló tras la herramienta: No encuentra .NET", steps.Single(s => s.Kind == TraceKind.Tool).HookError);
        Assert.Equal("El hook de Arkko falló al cerrar el turno: No encuentra .NET", steps.Single(s => s.Kind == TraceKind.Reply).HookError);
        Assert.Null(steps.Single(s => s.Kind == TraceKind.Prompt).HookError);
    }

    [Theory]
    [InlineData("An Application Control policy has blocked this file.", 1, "Windows lo ha bloqueado (Smart App Control)")]
    [InlineData("'C:/x/ak.exe' is not recognized as an internal or external command", 1, "No encuentra ak.exe")]
    [InlineData("Failed with non-blocking status code: algo raro\nmás", 3, "algo raro")]
    [InlineData("", 7, "Salió con el código 7")]
    public void El_motivo_se_dice_en_corto(string stderr, int exit, string reason) =>
        Assert.Equal(reason, HookTrace.Reason(stderr, exit));

    [Fact]
    public void Los_hooks_que_faltan_y_el_comando_del_Stop_salen_del_settings()
    {
        var (settings, _) = SettingsInstaller.Install(new JsonObject(), @"C:\Users\x\AppData\Local\ClaudeStats\bin\ak.exe");
        Assert.Empty(SettingsInstaller.MissingHooks(settings));
        Assert.Equal(Ak, SettingsInstaller.OwnStopCommand(settings));

        settings["hooks"]!.AsObject().Remove("Stop");
        Assert.Equal(["Stop"], SettingsInstaller.MissingHooks(settings));
        Assert.Null(SettingsInstaller.OwnStopCommand(settings));
        Assert.Equal(SettingsInstaller.Hooks.Length, SettingsInstaller.MissingHooks(new JsonObject()).Count);
    }
}
