using System.Globalization;

namespace Arkko.Core;

/// <summary>Lee lo que necesita el Diario de un día (prompts, estados, cambios de columna, límites y git) y guarda sus resúmenes.</summary>
/// <remarks>
/// Los commits se leen en la carpeta de cada sesión, que deja de existir al cerrar su worktree: por eso el resumen se
/// guarda en data/diary/&lt;día&gt;.json mientras se puede calcular, y el de un día ya terminado no se vuelve a calcular.
/// Los resúmenes no caducan: son pequeños, y los prompts y los transcripts de los que salen sí desaparecen.
/// </remarks>
public static class DiaryStore
{
    // Lo que se espera tras la medianoche para dar un día por cerrado: un prompt largo puede acabar pasadas las doce.
    private static readonly TimeSpan CloseMargin = TimeSpan.FromHours(1);

    /// <summary>El fichero del resumen de un día local.</summary>
    public static string PathOf(DateTime localDay, string? dataDir = null) =>
        Path.Combine(dataDir ?? AppPaths.Data, "diary", localDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".json");

    /// <summary>El resumen guardado de un día; null si no hay.</summary>
    public static DiaryDay? Load(DateTime localDay, string? dataDir = null) => JsonStore.Read<DiaryDay>(PathOf(localDay, dataDir));

    /// <summary>Guarda el resumen de un día.</summary>
    /// <remarks>
    /// Con un candado por día: la ventana y el guardado de cada hora pueden calcular el mismo día a la vez, y la
    /// escritura usa un temporal por proceso, no por hilo.
    /// </remarks>
    public static void Save(DiaryDay day, string? dataDir = null)
    {
        using var _ = JsonStore.Lock("diary-" + day.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        JsonStore.Write(PathOf(day.Day, dataDir), day);
    }

    /// <summary>Indica si un resumen ya no cambia: se calculó cuando su día había terminado, y con el formato de ahora.</summary>
    public static bool Final(DiaryDay day) =>
        day.Version == Diary.Version
        && day.BuiltUtc > DateTime.SpecifyKind(day.Day.Date.AddDays(1), DateTimeKind.Local).ToUniversalTime() + CloseMargin;

    /// <summary>El resumen de un día: el guardado si ya no cambia; si no, lo calcula y lo guarda si tiene algo.</summary>
    /// <param name="title">El título de una tarea por su clave, de los tableros leídos; null si no se conoce.</param>
    /// <param name="pullRequests">Si se pregunta al remoto por las PR de las ramas con commits, que puede tardar.</param>
    public static DiaryDay Get(DateTime localDay, BoardSettings settings, IReadOnlyDictionary<string, SessionLink>? links,
        Func<string, string?> title, bool pullRequests, DateTime nowUtc)
    {
        if (Load(localDay) is { } saved && Final(saved)) return saved;
        var day = Compute(localDay, settings, links, title, pullRequests, nowUtc);
        // Si no se puede guardar, el día calculado vale igual para enseñarlo: se guardará en el siguiente cálculo.
        try
        {
            if (!day.Empty) Save(day);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            JsonStore.LogError("diary", ex);
        }
        return day;
    }

    /// <summary>Calcula el resumen de un día leyendo lo que hay en disco y en git.</summary>
    public static DiaryDay Compute(DateTime localDay, BoardSettings settings, IReadOnlyDictionary<string, SessionLink>? links,
        Func<string, string?> title, bool pullRequests, DateTime nowUtc)
    {
        var day = localDay.Date;
        var prompts = JsonStore.ReadLines<PromptRecord>(AppPaths.PromptsFile(day));
        FillRepos(prompts);
        var sessions = TaskLinks.Sessions(prompts, settings, links);
        var (commits, prs) = Git(day, prompts, sessions, settings, pullRequests);
        return Diary.Build(new DiaryInputs(day, prompts, StateLog.Read(day), TaskHistory.Read(day), commits, prs, sessions, title,
            PeakFiveHour(day)), nowUtc);
    }

    /// <summary>Pone el repositorio a los prompts que no lo guardaron, por la carpeta de su sesión.</summary>
    /// <remarks>
    /// Los registros anteriores a guardar el repositorio solo tienen la carpeta (DEVLAB-6, develop), que no dice de qué
    /// repositorio es. Se saca de su transcript: la carpeta, si sigue siendo un worktree, o la de encima, si es la raíz de
    /// un repositorio con un worktree por rama (lleva .bare): esa sigue aunque el worktree ya se haya borrado.
    /// </remarks>
    private static void FillRepos(List<PromptRecord> prompts)
    {
        var bySession = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prompt in prompts.Where(p => p.Repo is null && p.TranscriptPath is not null))
        {
            if (!bySession.TryGetValue(prompt.SessionId, out var repo))
            {
                try
                {
                    repo = RepoOfFolder(TranscriptIndex.Of(prompt.TranscriptPath!).Cwd);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    repo = null;
                }
                bySession[prompt.SessionId] = repo;
            }
            prompt.Repo = repo;
        }
    }

    /// <summary>El repositorio de una carpeta de sesión: el del worktree, o la carpeta de encima si es la raíz de un repositorio con .bare.</summary>
    internal static string? RepoOfFolder(string? cwd)
    {
        if (Worktree.RepoOf(cwd) is { } repo) return repo;
        if (string.IsNullOrWhiteSpace(cwd)) return null;
        var parent = Path.GetDirectoryName(cwd.TrimEnd('/', '\\'));
        return parent is not null && Directory.Exists(Path.Combine(parent, ".bare")) ? Path.GetFileName(parent) : null;
    }

    /// <summary>El uso más alto del límite de 5 horas en un día; null sin datos.</summary>
    private static double? PeakFiveHour(DateTime day)
    {
        var peaks = JsonStore.ReadLines<LimitsSample>(AppPaths.LimitsHistoryFile(day))
            .SelectMany(s => s.Limits)
            .Where(l => l.Key.Equals("five_hour", StringComparison.OrdinalIgnoreCase))
            .Select(l => l.UsedPct)
            .ToList();
        return peaks.Count == 0 ? null : peaks.Max();
    }

    /// <summary>Los commits de la persona en el día, de las carpetas en que trabajaron sus sesiones, y las PR de esas ramas.</summary>
    /// <remarks>
    /// Una carpeta que ya no existe se salta. Un commit visto desde dos carpetas cuenta una vez, en la primera, y las
    /// carpetas con tarea van antes, para que se lo lleve la tarea.
    /// </remarks>
    private static (List<DiaryCommit> Commits, List<DiaryPullRequest> PullRequests) Git(DateTime day, List<PromptRecord> prompts,
        Dictionary<string, SessionTasks> sessions, BoardSettings settings, bool pullRequests)
    {
        var folders = new Dictionary<string, (string? Task, string Project, string? Folder)>(StringComparer.OrdinalIgnoreCase);
        foreach (var prompt in prompts.Where(p => p.TranscriptPath is not null))
        {
            string? cwd;
            try
            {
                cwd = TranscriptIndex.Of(prompt.TranscriptPath!).Cwd;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            if (cwd is null || folders.ContainsKey(cwd) || !Directory.Exists(cwd)) continue;
            var task = sessions.TryGetValue(prompt.SessionId, out var s) ? s.Principal : null;
            var name = Path.GetFileName(cwd.TrimEnd('/', '\\'));
            var repo = RepoOfFolder(cwd) ?? prompt.Repo ?? name;
            folders[cwd] = (task ?? TaskLinks.KeyOf(settings, name), repo, Diary.FolderOf(repo, name));
        }

        var commits = new List<DiaryCommit>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var prs = new List<DiaryPullRequest>();
        foreach (var (cwd, (task, project, folder)) in folders.OrderBy(f => f.Value.Task is null))
        {
            var found = CommitsIn(cwd, day, task, project, folder).Where(c => seen.Add(c.Hash)).ToList();
            commits.AddRange(found);
            if (pullRequests && found.Count > 0 && task is not null && PullRequestOf(cwd) is { } number)
                prs.Add(new DiaryPullRequest(task, project, number));
        }
        return (commits, prs);
    }

    // Lo que dijo el remoto de cada carpeta, y cuándo: el día en curso se recalcula a menudo y la PR casi nunca cambia.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime AtUtc, int? Number)> Prs =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan PrFresh = TimeSpan.FromMinutes(30);

    /// <summary>La PR de la rama de una carpeta; la misma respuesta durante media hora.</summary>
    private static int? PullRequestOf(string cwd)
    {
        if (Prs.TryGetValue(cwd, out var known) && DateTime.UtcNow - known.AtUtc < PrFresh) return known.Number;
        var number = GitReview.FindPr(cwd).Number;
        Prs[cwd] = (DateTime.UtcNow, number);
        return number;
    }

    /// <summary>Los commits de la persona en un día en la rama de una carpeta, sin los de merge; vacío si git no contesta.</summary>
    /// <remarks>La persona es el <c>user.email</c> de ese repositorio, que es quien firma sus commits.</remarks>
    internal static List<DiaryCommit> CommitsIn(string cwd, DateTime localDay, string? task, string project, string? folder = null)
    {
        if (GitChanges.RootOf(cwd, out _) is not { } root) return [];
        var email = GitChanges.Run(root, "config", "user.email");
        if (!email.Ok || email.Out.Trim().Length == 0) return [];
        var since = new DateTimeOffset(DateTime.SpecifyKind(localDay.Date, DateTimeKind.Local));
        var log = GitChanges.Run(root, "log", "HEAD", "--no-merges", "--fixed-strings", "--author=" + email.Out.Trim(),
            "--since=" + since.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
            "--until=" + since.AddDays(1).ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
            "--format=%H%x1f%aI%x1f%s");
        if (!log.Ok) return [];
        var result = new List<DiaryCommit>();
        foreach (var line in log.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.TrimEnd('\r').Split('\x1f');
            if (parts.Length < 3 || !DateTimeOffset.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.None, out var when)) continue;
            result.Add(new DiaryCommit { Hash = parts[0], WhenUtc = when.UtcDateTime, Subject = parts[2], Task = task, Project = project, Folder = folder });
        }
        return result;
    }
}
