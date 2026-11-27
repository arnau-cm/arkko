using Arkko.Core;

namespace Arkko.Tests;

public sealed class DataRetentionTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Local);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccs-retention-" + Guid.NewGuid().ToString("N"));

    public DataRetentionTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        // Solo la carpeta temporal que ha creado este test.
        if (_dir.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)) Directory.Delete(_dir, recursive: true);
    }

    private string Prompts(DateTime day, string? text = "texto del prompt")
    {
        var path = Path.Combine(_dir, "prompts", day.ToString("yyyy-MM-dd") + ".jsonl");
        var usage = new TurnUsage();
        usage.ByModel["claude-opus-5-5"] = new ModelTokens { Output = 10, ApiCalls = 1 };
        JsonStore.AppendLine(path, new PromptRecord { WhenUtc = day.ToUniversalTime(), SessionId = "s", Prompt = text, Usage = usage });
        return path;
    }

    [Fact]
    public void Borra_el_historico_pasado_su_plazo_y_conserva_hoy()
    {
        var old = Prompts(Now.Date.AddDays(-40));
        var recent = Prompts(Now.Date.AddDays(-5));
        var today = Prompts(Now.Date);
        var result = new DataRetention(_dir).Apply(new RetentionSettings { PromptsDays = 30, PromptTextDays = null }, Now);
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
        Assert.True(File.Exists(today));
        Assert.Equal(1, result.FilesDeleted);
    }

    [Fact]
    public void Los_estados_y_los_cambios_de_columna_caducan_con_los_prompts()
    {
        var old = Now.Date.AddDays(-40).ToUniversalTime();
        StateLog.Append("s", SessionStatus.Waiting, old, _dir);
        TaskHistory.Record([new BoardTask("DEVLAB-1", "t") { State = TaskState.Todo }], old, _dir);
        TaskHistory.Record([new BoardTask("DEVLAB-1", "t") { State = TaskState.Doing }], old.AddMinutes(15), _dir);
        StateLog.Append("s", SessionStatus.Working, Now.ToUniversalTime(), _dir);

        new DataRetention(_dir).Apply(new RetentionSettings { PromptsDays = 30, PromptTextDays = null }, Now);

        Assert.False(File.Exists(StateLog.PathOf(Now.Date.AddDays(-40), _dir)));
        Assert.False(File.Exists(TaskHistory.PathOf(Now.Date.AddDays(-40), _dir)));
        Assert.True(File.Exists(StateLog.PathOf(Now.Date, _dir)));
        // La última columna conocida no caduca: sin ella, el siguiente cambio no se vería.
        Assert.True(File.Exists(Path.Combine(_dir, "task-states.json")));
    }

    /// <summary>Guarda el resumen del Diario de un día con un bloque por sesión, cada uno con el principio de un prompt.</summary>
    private void Diary(DateTime day, params string[] sessions) =>
        DiaryStore.Save(new DiaryDay
        {
            Day = day,
            Blocks = sessions.Select(s => new DiaryBlock { SessionId = s, Prompts = 1, Turns = [new DiaryTurn { Text = "texto del prompt" }] }).ToList(),
        }, _dir);

    [Fact]
    public void El_Diario_pierde_el_texto_de_los_prompts_con_su_plazo_y_conserva_el_resto()
    {
        Diary(Now.Date.AddDays(-40), "s");
        Diary(Now.Date.AddDays(-5), "s");
        new DataRetention(_dir).Apply(new RetentionSettings { PromptsDays = null, PromptTextDays = 30 }, Now);

        var old = DiaryStore.Load(Now.Date.AddDays(-40), _dir)!;
        Assert.Null(old.Blocks.Single().Turns.Single().Text);
        Assert.Equal(1, old.Blocks.Single().Prompts);
        Assert.NotNull(DiaryStore.Load(Now.Date.AddDays(-5), _dir)!.Blocks.Single().Turns.Single().Text);
    }

    [Fact]
    public void Olvidar_una_sesion_quita_su_texto_tambien_del_Diario()
    {
        Diary(Now.Date.AddDays(-2), "s", "otra");
        new DataRetention(_dir).ForgetSession("s");

        var blocks = DiaryStore.Load(Now.Date.AddDays(-2), _dir)!.Blocks;
        Assert.Null(blocks.Single(b => b.SessionId == "s").Turns.Single().Text);
        Assert.NotNull(blocks.Single(b => b.SessionId == "otra").Turns.Single().Text);
    }

    [Fact]
    public void Pasado_su_plazo_quita_el_texto_pero_conserva_las_cifras()
    {
        var old = Prompts(Now.Date.AddDays(-40));
        var recent = Prompts(Now.Date.AddDays(-5));
        var result = new DataRetention(_dir).Apply(new RetentionSettings { PromptsDays = null, PromptTextDays = 30 }, Now);
        var kept = JsonStore.ReadLines<PromptRecord>(old).Single();
        Assert.Null(kept.Prompt);
        Assert.Equal(10, kept.Usage.Totals().Output);
        Assert.Equal("texto del prompt", JsonStore.ReadLines<PromptRecord>(recent).Single().Prompt);
        Assert.Equal(1, result.TextStripped);
    }

    [Fact]
    public void Siempre_no_borra_nada_del_historico()
    {
        var old = Prompts(Now.Date.AddDays(-900));
        var result = new DataRetention(_dir).Apply(new RetentionSettings { PromptsDays = null, PromptTextDays = null, LimitsDays = null }, Now);
        Assert.True(File.Exists(old));
        Assert.Equal("texto del prompt", JsonStore.ReadLines<PromptRecord>(old).Single().Prompt);
        Assert.False(result.Any);
    }

    [Fact]
    public void Retira_las_sesiones_sin_proceso_que_llevan_un_dia_sin_cambios()
    {
        var sessions = Path.Combine(_dir, "sessions");
        JsonStore.Write(Path.Combine(sessions, "vieja.json"), new SessionState { SessionId = "vieja", UpdatedUtc = Now.ToUniversalTime().AddDays(-2) });
        JsonStore.Write(Path.Combine(sessions, "nueva.json"), new SessionState { SessionId = "nueva", UpdatedUtc = Now.ToUniversalTime().AddHours(-1) });
        JsonStore.Write(Path.Combine(sessions, "con-proceso.json"),
            new SessionState { SessionId = "con-proceso", ClaudePid = 4, UpdatedUtc = Now.ToUniversalTime().AddDays(-2) });
        new DataRetention(_dir).Apply(new RetentionSettings(), Now);
        Assert.False(File.Exists(Path.Combine(sessions, "vieja.json")));
        Assert.True(File.Exists(Path.Combine(sessions, "nueva.json")));
        Assert.True(File.Exists(Path.Combine(sessions, "con-proceso.json")));
    }

    [Fact]
    public void Borra_los_temporales_de_escrituras_cortadas_y_respeta_los_recientes()
    {
        var sessions = Path.Combine(_dir, "sessions");
        Directory.CreateDirectory(sessions);
        string Temp(string name, TimeSpan age)
        {
            var path = Path.Combine(sessions, name);
            File.WriteAllText(path, "{}");
            File.SetLastWriteTimeUtc(path, Now.ToUniversalTime() - age);
            return path;
        }
        var cut = Temp("abc.json.4321.tmp", TimeSpan.FromHours(3));
        var writing = Temp("def.json.4321.tmp", TimeSpan.FromMinutes(5));
        var other = Temp("ajeno.tmp", TimeSpan.FromDays(3));

        new DataRetention(_dir).Apply(new RetentionSettings(), Now);

        Assert.False(File.Exists(cut));
        Assert.True(File.Exists(writing));
        Assert.True(File.Exists(other));
    }

    [Fact]
    public void El_simulacro_cuenta_sin_borrar_nada()
    {
        var old = Prompts(Now.Date.AddDays(-40));
        var result = new DataRetention(_dir).Apply(new RetentionSettings { PromptsDays = 30 }, Now, dryRun: true);
        Assert.Equal(1, result.FilesDeleted);
        Assert.True(File.Exists(old));
        Assert.Equal("texto del prompt", JsonStore.ReadLines<PromptRecord>(old).Single().Prompt);
    }

    [Fact]
    public void El_inventario_dice_desde_cuando_hay_texto()
    {
        Prompts(Now.Date.AddDays(-10), text: null);
        Prompts(Now.Date.AddDays(-3));
        var inventory = new DataRetention(_dir).Inventory();
        Assert.Equal(Now.Date.AddDays(-10), inventory.Prompts.Since);
        Assert.Equal(Now.Date.AddDays(-3), inventory.PromptTextSince);
        Assert.True(inventory.TotalBytes > 0);
    }

    [Fact]
    public void Olvidar_una_sesion_quita_solo_su_texto_y_conserva_las_cifras()
    {
        var path = Prompts(Now.Date);
        var usage = new TurnUsage();
        usage.ByModel["claude-opus-5-5"] = new ModelTokens { Output = 20, ApiCalls = 1 };
        JsonStore.AppendLine(path, new PromptRecord { WhenUtc = Now.ToUniversalTime(), SessionId = "otra", Prompt = "de otra sesión", Usage = usage });
        var older = Prompts(Now.Date.AddDays(-3));

        Assert.Equal(2, new DataRetention(_dir).ForgetSession("s"));

        var records = JsonStore.ReadLines<PromptRecord>(path).ToList();
        Assert.Equal(2, records.Count);
        Assert.Null(records.Single(r => r.SessionId == "s").Prompt);
        Assert.Equal(10, records.Single(r => r.SessionId == "s").Usage.ByModel["claude-opus-5-5"].Output);
        Assert.Equal("de otra sesión", records.Single(r => r.SessionId == "otra").Prompt);
        Assert.Null(JsonStore.ReadLines<PromptRecord>(older).Single().Prompt);
    }

    [Fact]
    public void Olvidar_una_sesion_borra_su_statusline()
    {
        var statusline = Path.Combine(_dir, "statusline", "s.json");
        var other = Path.Combine(_dir, "statusline", "otra.json");
        Directory.CreateDirectory(Path.GetDirectoryName(statusline)!);
        File.WriteAllText(statusline, "{}");
        File.WriteAllText(other, "{}");

        Assert.Equal(0, new DataRetention(_dir).ForgetSession("s"));

        Assert.False(File.Exists(statusline));
        Assert.True(File.Exists(other));
    }
}
