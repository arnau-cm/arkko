using Arkko.Core;

namespace Arkko.Tests;

public class HistoryTests
{
    private static PromptRecord Prompt(string session, long input, long output, long cacheWrite, long cacheRead, long ms)
    {
        var usage = new TurnUsage { DurationMs = ms };
        usage.ByModel["claude-opus-5-5"] = new ModelTokens { Input = input, Output = output, CacheWrite = cacheWrite, CacheRead = cacheRead, ApiCalls = 1 };
        return new PromptRecord { SessionId = session, Usage = usage };
    }

    [Fact]
    public void Calcula_las_cifras_clave()
    {
        var s = History.Summarize(
        [
            Prompt("a", 10, 100, 890, 9_000, 60_000),
            Prompt("a", 0, 50, 0, 1_000, 30_000),
            Prompt("b", 0, 50, 100, 900, 30_000),
        ]);
        Assert.Equal(1_200, s.NewTokens);
        Assert.Equal(3, s.Prompts);
        Assert.Equal(2, s.Sessions);
        Assert.Equal(TimeSpan.FromMinutes(2), s.Working);
        // Caché leída 10 900 de una entrada total de 10 900 + 10 + 990.
        Assert.Equal(100.0 * 10_900 / 11_900, s.CachePct!.Value, 6);
    }

    [Fact]
    public void Sin_prompts_no_hay_porcentaje_de_cache()
    {
        var s = History.Summarize([]);
        Assert.Equal(0, s.Prompts);
        Assert.Null(s.CachePct);
    }

    private static PromptRecord At(DateTime local, string project, string model, long output)
    {
        var usage = new TurnUsage();
        usage.ByModel[model] = new ModelTokens { Output = output, ApiCalls = 1 };
        return new PromptRecord { WhenUtc = local.ToUniversalTime(), SessionId = project, Project = project, Usage = usage };
    }

    [Fact]
    public void Reparte_por_dia_y_modelo_con_los_dias_vacios()
    {
        var now = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Local);
        var (size, buckets) = History.Buckets(
        [
            At(new DateTime(2026, 9, 30, 10, 0, 0, DateTimeKind.Local), "a", "claude-opus-5-5", 100),
            At(new DateTime(2026, 9, 30, 11, 0, 0, DateTimeKind.Local), "a", "claude-sonnet-5-5", 40),
            At(new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Local), "b", "claude-opus-5-5", 7),
        ], HistoryPeriod.Week, now);
        Assert.Equal(BucketSize.Day, size);
        Assert.Equal(7, buckets.Count);
        Assert.Equal(0, buckets[0].Total);
        Assert.Equal(100, buckets[5].ByModel["Opus 5.5"]);
        Assert.Equal(40, buckets[5].ByModel["Sonnet 5.5"]);
        Assert.Equal(7, buckets[6].Total);
    }

    [Fact]
    public void Hoy_va_por_horas()
    {
        var now = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Local);
        var (size, buckets) = History.Buckets([At(new DateTime(2026, 10, 1, 9, 30, 0, DateTimeKind.Local), "a", "claude-opus-5-5", 5)],
            HistoryPeriod.Today, now);
        Assert.Equal(BucketSize.Hour, size);
        Assert.Equal(24, buckets.Count);
        Assert.Equal(5, buckets[9].Total);
    }

    [Fact]
    public void Todo_con_muchos_dias_va_por_semanas()
    {
        var now = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Local);
        var (size, buckets) = History.Buckets([At(new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Local), "a", "claude-opus-5-5", 5)],
            HistoryPeriod.All, now);
        Assert.Equal(BucketSize.Week, size);
        Assert.Equal(DayOfWeek.Monday, buckets[0].Start.DayOfWeek);
        Assert.Equal(5, buckets[0].Total);
    }

    [Fact]
    public void Ordena_los_proyectos_por_consumo()
    {
        var day = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Local);
        var projects = History.ByProject(
        [
            At(day, "a", "claude-opus-5-5", 25),
            At(day, "b", "claude-opus-5-5", 50),
            At(day, "b", "claude-opus-5-5", 25),
        ]);
        Assert.Equal(["b", "a"], projects.Select(p => p.Project));
        Assert.Equal(75, projects[0].Pct, 6);
        Assert.Equal(2, projects[0].Prompts);
    }

    [Fact]
    public void Los_worktrees_se_juntan_en_su_repositorio_con_el_desglose_por_carpeta()
    {
        var day = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Local);
        PromptRecord InRepo(string repo, string folder, long output)
        {
            var r = At(day, folder, "claude-opus-5-5", output);
            r.Repo = repo;
            return r;
        }
        var projects = History.ByProject(
        [
            InRepo("Tienda", "develop", 20),
            InRepo("Tienda", "DEVLAB-86", 50),
            InRepo("Taller", "develop", 10),
            At(day, "claude-stats", "claude-opus-5-5", 20),
        ]);

        Assert.Equal(["Tienda", "claude-stats", "Taller"], projects.Select(p => p.Project));
        Assert.Equal(70, projects[0].NewTokens);
        Assert.Equal(["DEVLAB-86", "develop"], projects[0].Worktrees.Select(w => w.Project));
        Assert.Equal(50, projects[0].Worktrees[0].Pct, 6);
        Assert.Empty(projects[1].Worktrees);
    }

    [Fact]
    public void Calcula_la_cache_por_barra()
    {
        var now = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Local);
        var usage = new TurnUsage();
        usage.ByModel["claude-opus-5-5"] = new ModelTokens { Input = 10, CacheWrite = 90, CacheRead = 900, ApiCalls = 1 };
        var record = new PromptRecord { WhenUtc = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Local).ToUniversalTime(), Usage = usage };
        var (_, buckets) = History.CacheBuckets([record], HistoryPeriod.Today, now);
        Assert.Equal(90, buckets[9].Pct!.Value, 6);
        Assert.Null(buckets[10].Pct);
    }

    [Fact]
    public void El_csv_separa_con_punto_y_coma_y_entrecomilla_lo_necesario()
    {
        var usage = new TurnUsage { DurationMs = 2_500 };
        usage.ByModel["claude-opus-5-5"] = new ModelTokens { Input = 1, Output = 2, CacheWrite = 3, CacheRead = 4, ApiCalls = 1 };
        var csv = HistoryCsv.Build(
        [
            new PromptRecord { WhenUtc = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Local).ToUniversalTime(),
                SessionId = "s1", Project = "mi;proyecto", SessionName = "Dice \"hola\"", Prompt = "secreto", Usage = usage },
        ]);
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("Fecha;Hora;Proyecto;Worktree;", lines[0]);
        Assert.Equal("2026-10-01;09:00:00;\"mi;proyecto\";;s1;\"Dice \"\"hola\"\"\";Opus 5.5;6;1;2;3;4;1;2,5", lines[1]);
        Assert.DoesNotContain("secreto", csv);
    }

    [Fact]
    public void Ordena_los_prompts_por_tokens_nuevos()
    {
        var day = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Local);
        var costliest = History.Costliest(
        [
            At(day, "a", "claude-opus-5-5", 10),
            At(day.AddMinutes(1), "b", "claude-opus-5-5", 300),
            At(day.AddMinutes(2), "c", "claude-opus-5-5", 20),
        ], 2);
        Assert.Equal(["b", "c"], costliest.Select(p => p.Project));
    }

    [Fact]
    public void El_inicio_del_prompt_es_su_primera_linea_sin_espacios_de_mas()
    {
        Assert.Equal("Haz la fase 4 ya", History.PromptPreview("  Haz   la fase\t4 ya\nY luego más cosas"));
        Assert.Null(History.PromptPreview("   "));
    }

    [Fact]
    public void El_periodo_incluye_hoy()
    {
        var today = new DateTime(2026, 10, 1);
        Assert.Equal(today, History.From(HistoryPeriod.Today, today));
        Assert.Equal(new DateTime(2026, 9, 25), History.From(HistoryPeriod.Week, today));
        Assert.Equal(new DateTime(2026, 9, 2), History.From(HistoryPeriod.Month, today));
        Assert.Null(History.From(HistoryPeriod.All, today));
    }

    [Fact]
    public void El_tramo_anterior_tiene_la_misma_duracion_y_acaba_a_la_misma_hora()
    {
        var now = new DateTime(2026, 10, 13, 10, 30, 0);
        Assert.Equal((new DateTime(2026, 10, 12), new DateTime(2026, 10, 12, 10, 30, 0)), History.PreviousWindow(HistoryPeriod.Today, now));
        Assert.Equal((new DateTime(2026, 9, 30), new DateTime(2026, 10, 6, 10, 30, 0)), History.PreviousWindow(HistoryPeriod.Week, now));
        Assert.Equal((new DateTime(2026, 8, 15), new DateTime(2026, 9, 13, 10, 30, 0)), History.PreviousWindow(HistoryPeriod.Month, now));
        Assert.Null(History.PreviousWindow(HistoryPeriod.All, now));
    }

    [Fact]
    public void Entre_dos_horas_cuenta_el_inicio_y_no_el_final()
    {
        var records = new[]
        {
            At(new DateTime(2026, 10, 12, 9, 0, 0, DateTimeKind.Local), "a", "claude-opus-5-5", 1),
            At(new DateTime(2026, 10, 12, 10, 30, 0, DateTimeKind.Local), "a", "claude-opus-5-5", 2),
        };
        var inside = History.Between(records, new DateTime(2026, 10, 12, 9, 0, 0), new DateTime(2026, 10, 12, 10, 30, 0));
        Assert.Equal(1, Assert.Single(inside).Usage.Totals().Output);
    }

    [Fact]
    public void Las_cifras_por_barra_van_en_las_mismas_barras_que_el_historico()
    {
        var now = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Local);
        var (size, buckets) = History.Series(
        [
            At(new DateTime(2026, 9, 30, 10, 0, 0, DateTimeKind.Local), "a", "claude-opus-5-5", 100),
            At(new DateTime(2026, 9, 30, 11, 0, 0, DateTimeKind.Local), "b", "claude-opus-5-5", 40),
            At(new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Local), "b", "claude-opus-5-5", 7),
        ], HistoryPeriod.Week, now);
        Assert.Equal(BucketSize.Day, size);
        Assert.Equal(7, buckets.Count);
        Assert.Equal(0, buckets[0].Summary.Prompts);
        Assert.Equal(2, buckets[5].Summary.Prompts);
        Assert.Equal(2, buckets[5].Summary.Sessions);
        Assert.Equal(1, buckets[6].Summary.Prompts);
    }
}
