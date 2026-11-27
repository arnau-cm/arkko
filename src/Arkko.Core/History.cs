using System.Globalization;

namespace Arkko.Core;

/// <summary>Periodo que se analiza en el panel.</summary>
public enum HistoryPeriod
{
    Today,
    Week,
    Month,
    All,
}

/// <summary>Cifras clave de un periodo: lo que se ha trabajado con Claude.</summary>
/// <param name="CachePct">Parte de la entrada que se leyó de caché, de 0 a 100; null sin datos.</param>
public sealed record HistorySummary(long NewTokens, int Prompts, int Sessions, TimeSpan Working, double? CachePct);

/// <summary>Tamaño de cada barra del histórico.</summary>
public enum BucketSize
{
    Hour,
    Day,
    Week,
}

/// <summary>Una barra del histórico: su inicio (hora local) y los tokens nuevos de cada modelo.</summary>
public sealed record UsageBucket(DateTime Start, IReadOnlyDictionary<string, long> ByModel)
{
    public long Total => ByModel.Values.Sum();
}

/// <summary>Una barra del aprovechamiento de caché: su inicio, el % leído de caché y de cuánto.</summary>
/// <param name="Pct">Lectura de caché sobre toda la entrada, de 0 a 100; null si no hubo uso.</param>
public sealed record CacheBucket(DateTime Start, double? Pct, long CacheRead, long Input);

/// <summary>Las cifras clave de una barra del histórico: su inicio (hora local) y lo hecho en ella.</summary>
public sealed record SummaryBucket(DateTime Start, HistorySummary Summary);

/// <summary>Consumo de un proyecto en el periodo.</summary>
/// <param name="Pct">Parte de los tokens nuevos del periodo, de 0 a 100.</param>
public sealed record ProjectUsage(string Project, long NewTokens, int Prompts, int Sessions, double Pct)
{
    /// <summary>Desglose por carpeta cuando el repositorio se ha usado desde worktrees; vacío si no.</summary>
    public IReadOnlyList<ProjectUsage> Worktrees { get; init; } = [];
}

/// <summary>Lee el histórico de prompts y calcula sus cifras; lo usa el panel del modo maximizado.</summary>
public static class History
{
    /// <summary>Primer día local del periodo; null es «desde el principio».</summary>
    public static DateTime? From(HistoryPeriod period, DateTime today) => period switch
    {
        HistoryPeriod.Today => today.Date,
        HistoryPeriod.Week => today.Date.AddDays(-6),
        HistoryPeriod.Month => today.Date.AddDays(-29),
        _ => null,
    };

    /// <summary>Prompts registrados desde un día local hasta hoy, o hasta otro día, del más antiguo al más reciente.</summary>
    public static List<PromptRecord> Load(DateTime? fromDay, DateTime? toDay = null)
    {
        var result = new List<PromptRecord>();
        if (!Directory.Exists(AppPaths.Prompts)) return result;
        foreach (var file in Directory.EnumerateFiles(AppPaths.Prompts, "*.jsonl").Order(StringComparer.Ordinal))
        {
            if (!DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var day)) continue;
            if (fromDay is { } from && day < from.Date) continue;
            if (toDay is { } to && day > to.Date) continue;
            result.AddRange(JsonStore.ReadLines<PromptRecord>(file));
        }
        return result;
    }

    /// <summary>Tramo con el que se compara un periodo: el anterior de la misma duración, hasta la misma hora; null en «Todo».</summary>
    /// <remarks>
    /// Se corta a la misma hora para que la comparación sea justa: a media mañana, «Hoy» se compara con
    /// ayer hasta esa hora, no con ayer entero.
    /// </remarks>
    public static (DateTime From, DateTime To)? PreviousWindow(HistoryPeriod period, DateTime now)
    {
        var days = period switch
        {
            HistoryPeriod.Today => 1,
            HistoryPeriod.Week => 7,
            HistoryPeriod.Month => 30,
            _ => 0,
        };
        if (days == 0) return null;
        var from = From(period, now.Date)!.Value;
        return (from.AddDays(-days), now.AddDays(-days));
    }

    /// <summary>Prompts hechos dentro de un tramo de hora local, sin incluir su final.</summary>
    public static List<PromptRecord> Between(IEnumerable<PromptRecord> records, DateTime from, DateTime to) =>
        records.Where(r => r.WhenUtc.ToLocalTime() is var local && local >= from && local < to).ToList();

    /// <summary>Las cifras clave de cada barra, en las mismas barras que el histórico: lo que dibujan las tarjetas del panel.</summary>
    public static (BucketSize Size, List<SummaryBucket> Buckets) Series(IReadOnlyCollection<PromptRecord> records, HistoryPeriod period, DateTime now,
        WorkSchedule? work = null)
    {
        var (size, starts) = Range(records, period, now, work);
        var groups = starts.Select(_ => new List<PromptRecord>()).ToList();
        foreach (var r in records)
        {
            var index = starts.IndexOf(BucketStart(r.WhenUtc.ToLocalTime(), size));
            if (index >= 0) groups[index].Add(r);
        }
        return (size, starts.Select((s, i) => new SummaryBucket(s, Summarize(groups[i]))).ToList());
    }

    /// <summary>Los prompts hechos en horario laboral.</summary>
    public static List<PromptRecord> InSchedule(IEnumerable<PromptRecord> records, WorkSchedule work) =>
        records.Where(r => work.Contains(r.WhenUtc.ToLocalTime())).ToList();

    // Con más días que esto, «Todo» se agrupa por semanas: barras diarias serían demasiado finas.
    private const int MaxDailyBars = 62;

    /// <summary>Tokens nuevos por modelo en barras: por horas para hoy, por días, o por semanas si el periodo es muy largo.</summary>
    /// <remarks>
    /// Incluye las barras vacías, para que los días sin uso se vean como huecos. Con horario laboral,
    /// solo salen las horas de la franja y los días laborables; las semanas, enteras.
    /// </remarks>
    public static (BucketSize Size, List<UsageBucket> Buckets) Buckets(IReadOnlyCollection<PromptRecord> records, HistoryPeriod period, DateTime now,
        WorkSchedule? work = null)
    {
        var (size, starts) = Range(records, period, now, work);
        var buckets = starts.Select(_ => new Dictionary<string, long>()).ToList();
        foreach (var r in records)
        {
            var index = starts.IndexOf(BucketStart(r.WhenUtc.ToLocalTime(), size));
            if (index < 0) continue;
            foreach (var (model, tokens) in r.Usage.ByModel)
            {
                var name = Format.ModelName(model);
                buckets[index][name] = buckets[index].GetValueOrDefault(name) + PromptSummary.NewTokens(tokens);
            }
        }
        return (size, starts.Select((s, i) => new UsageBucket(s, buckets[i])).ToList());
    }

    /// <summary>Aprovechamiento de la caché en las mismas barras que el histórico.</summary>
    public static (BucketSize Size, List<CacheBucket> Buckets) CacheBuckets(IReadOnlyCollection<PromptRecord> records, HistoryPeriod period, DateTime now,
        WorkSchedule? work = null)
    {
        var (size, starts) = Range(records, period, now, work);
        var totals = starts.Select(_ => new ModelTokens()).ToList();
        foreach (var r in records)
        {
            var index = starts.IndexOf(BucketStart(r.WhenUtc.ToLocalTime(), size));
            if (index >= 0) totals[index].Add(r.Usage.Totals());
        }
        return (size, starts.Select((s, i) =>
        {
            var input = totals[i].Input + totals[i].CacheWrite + totals[i].CacheRead;
            return new CacheBucket(s, input == 0 ? null : 100.0 * totals[i].CacheRead / input, totals[i].CacheRead, input);
        }).ToList());
    }

    /// <summary>Tamaño de las barras y el inicio de cada una para un periodo, incluidas las vacías.</summary>
    private static (BucketSize Size, List<DateTime> Starts) Range(IReadOnlyCollection<PromptRecord> records, HistoryPeriod period, DateTime now,
        WorkSchedule? work)
    {
        var today = now.Date;
        BucketSize size;
        DateTime first;
        if (period == HistoryPeriod.Today)
        {
            size = BucketSize.Hour;
            first = today;
        }
        else
        {
            first = From(period, today)
                ?? (records.Count == 0 ? today : records.Min(r => r.WhenUtc.ToLocalTime().Date));
            size = (today - first).Days + 1 > MaxDailyBars ? BucketSize.Week : BucketSize.Day;
            if (size == BucketSize.Week) first = StartOfWeek(first);
        }

        var last = size == BucketSize.Hour ? today.AddHours(23) : size == BucketSize.Week ? StartOfWeek(today) : today;
        if (work is not null && size == BucketSize.Hour)
            (first, last) = (today.AddHours(work.Hours.First), today.AddHours(work.Hours.Last));

        var starts = new List<DateTime>();
        for (var start = first; start <= last; start = Next(start, size))
            if (work is null || size != BucketSize.Day || work.IsWorkDay(start)) starts.Add(start);
        return (size, starts);
    }

    /// <summary>Los prompts que más tokens nuevos han usado, del más caro al más barato.</summary>
    public static List<PromptRecord> Costliest(IEnumerable<PromptRecord> records, int count) =>
        records
            .OrderByDescending(r => PromptSummary.NewTokens(r.Usage.Totals()))
            .ThenByDescending(r => r.WhenUtc)
            .Take(count)
            .ToList();

    /// <summary>Primera línea del prompt, sin espacios repetidos, para verlo de un vistazo; null si no se guardó.</summary>
    public static string? PromptPreview(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return null;
        var line = prompt.Trim().Split('\n')[0];
        return string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Fotos de los límites guardadas desde un día local hasta hoy, en orden.</summary>
    public static List<LimitsSample> LoadLimits(DateTime? fromDay)
    {
        var result = new List<LimitsSample>();
        var dir = Path.GetDirectoryName(AppPaths.LimitsHistoryFile(DateTime.Today))!;
        if (!Directory.Exists(dir)) return result;
        foreach (var file in Directory.EnumerateFiles(dir, "*.jsonl").Order(StringComparer.Ordinal))
        {
            if (!DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var day)) continue;
            if (fromDay is { } from && day < from.Date) continue;
            result.AddRange(JsonStore.ReadLines<LimitsSample>(file));
        }
        return result;
    }

    /// <summary>Tokens nuevos por proyecto, del que más gasta al que menos.</summary>
    /// <remarks>
    /// El proyecto es el repositorio, que junta todos sus worktrees; si alguno de sus prompts vino de un
    /// worktree, el proyecto lleva el desglose por carpeta. Los porcentajes del desglose son sobre todo
    /// el periodo, como los del proyecto, para que las barras se lean en la misma escala.
    /// </remarks>
    public static List<ProjectUsage> ByProject(IReadOnlyCollection<PromptRecord> records)
    {
        var total = records.Sum(r => PromptSummary.NewTokens(r.Usage.Totals()));

        ProjectUsage Usage(string name, IEnumerable<PromptRecord> group)
        {
            var list = group.ToList();
            var tokens = list.Sum(r => PromptSummary.NewTokens(r.Usage.Totals()));
            return new ProjectUsage(name, tokens, list.Count(r => r.ByPerson), list.Select(r => r.SessionId).Distinct().Count(),
                total == 0 ? 0 : 100.0 * tokens / total);
        }

        return records
            .GroupBy(ProjectOf, StringComparer.OrdinalIgnoreCase)
            .Select(g => Usage(g.Key, g) with
            {
                Worktrees = g.Any(r => r.Repo is not null)
                    ? g.GroupBy(FolderOf, StringComparer.OrdinalIgnoreCase).Select(w => Usage(w.Key, w)).OrderByDescending(w => w.NewTokens).ToList()
                    : [],
            })
            .OrderByDescending(p => p.NewTokens)
            .ToList();
    }

    /// <summary>Proyecto de un prompt: su repositorio si venía de un worktree, su carpeta si no.</summary>
    public static string ProjectOf(PromptRecord r) => r.Repo ?? FolderOf(r);

    /// <summary>Worktree de un prompt: su carpeta si venía de uno; null si no.</summary>
    public static string? WorktreeOf(PromptRecord r) => r.Repo is null ? null : FolderOf(r);

    /// <summary>Carpeta de la sesión del prompt, o «(sin proyecto)» si no se registró.</summary>
    private static string FolderOf(PromptRecord r) => string.IsNullOrWhiteSpace(r.Project) ? "(sin proyecto)" : r.Project!;

    /// <summary>Inicio de la barra a la que pertenece un instante local.</summary>
    private static DateTime BucketStart(DateTime local, BucketSize size) => size switch
    {
        BucketSize.Hour => local.Date.AddHours(local.Hour),
        BucketSize.Week => StartOfWeek(local.Date),
        _ => local.Date,
    };

    /// <summary>Inicio de la barra siguiente.</summary>
    private static DateTime Next(DateTime start, BucketSize size) => size switch
    {
        BucketSize.Hour => start.AddHours(1),
        BucketSize.Week => start.AddDays(7),
        _ => start.AddDays(1),
    };

    /// <summary>Lunes de la semana de un día.</summary>
    private static DateTime StartOfWeek(DateTime day) => day.Date.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    /// <summary>Cifras clave de un conjunto de prompts.</summary>
    /// <remarks>
    /// El % de caché es la lectura de caché sobre toda la entrada (sin caché, escrita y leída): lo que
    /// el modelo no tuvo que procesar de nuevo. Los prompts son solo los de la persona: los turnos de un comando «!» o de
    /// un aviso suman su tiempo y sus tokens, pero no cuentan como prompts.
    /// </remarks>
    public static HistorySummary Summarize(IReadOnlyCollection<PromptRecord> records)
    {
        var totals = new ModelTokens();
        long ms = 0;
        foreach (var r in records)
        {
            totals.Add(r.Usage.Totals());
            ms += r.Usage.DurationMs;
        }
        var input = totals.Input + totals.CacheWrite + totals.CacheRead;
        return new HistorySummary(
            PromptSummary.NewTokens(totals),
            records.Count(r => r.ByPerson),
            records.Select(r => r.SessionId).Distinct().Count(),
            TimeSpan.FromMilliseconds(ms),
            input == 0 ? null : 100.0 * totals.CacheRead / input);
    }
}
