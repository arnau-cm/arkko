using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Arkko.Core;

/// <summary>Un fichero de la rama en una revisión: si se ha dado por revisado, la huella de git que tenía entonces y si
/// ha cambiado después, que lo devuelve a «sin revisar».</summary>
public sealed class ReviewMark
{
    public string Path { get; set; } = "";
    public bool Reviewed { get; set; }
    public string? Blob { get; set; }
    public bool Changed { get; set; }
}

/// <summary>Una nota de la revisión: su tipo, dónde va (una línea de un fichero, un fichero o general) y su texto.</summary>
public sealed class ReviewNote
{
    public string Id { get; set; } = "";

    /// <summary>«correccion», «mejora», «duda» o «bien».</summary>
    public string Kind { get; set; } = "";

    public string? File { get; set; }
    public int? Line { get; set; }

    /// <summary>La última línea, si la nota abarca varias; null si es de una sola.</summary>
    public int? EndLine { get; set; }

    /// <summary>El texto de su (primera) línea al anotarla, para encontrarla si se mueve.</summary>
    public string? LineText { get; set; }

    /// <summary>Si su línea ya no está en el fichero: la nota queda en el fichero, sin línea.</summary>
    public bool Lost { get; set; }

    /// <summary>La primera línea quitada que abarca, con su número en la base de la rama; null si no abarca ninguna.</summary>
    public int? OldLine { get; set; }

    /// <summary>La última línea quitada, si abarca varias.</summary>
    public int? OldEndLine { get; set; }

    /// <summary>El texto de las líneas quitadas que abarca, como estaban en la base.</summary>
    public string? Removed { get; set; }

    /// <summary>Si la nota es solo de lo quitado: su línea es la que sigue a lo quitado, para situarla en el fichero de
    /// ahora, y no forma parte de la nota.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Anchor { get; set; }

    public string Text { get; set; } = "";
    public DateTime CreatedUtc { get; set; }

    /// <summary>La ronda de <c>/opsx:review</c> cuya respuesta ha leído la persona (0 si no tenía ronda); null si no la ha
    /// leído.</summary>
    /// <remarks>Si una ronda posterior vuelve a tratar la nota, su respuesta es otra y la nota vuelve a contar como sin leer.</remarks>
    public int? Read { get; set; }
}

/// <summary>La revisión de una rama: con qué se compara, su PR si la hay, cuándo empezó, sus ficheros y sus notas.</summary>
public sealed class Review
{
    public string Branch { get; set; } = "";
    public int? Pr { get; set; }
    public string? BaseLabel { get; set; }
    public DateTime StartedUtc { get; set; }
    public List<ReviewMark> Files { get; set; } = [];
    public List<ReviewNote> Notes { get; set; } = [];
}

/// <summary>Cómo está la revisión de la carpeta de una sesión: la revisión (null si no se ha empezado), dónde está su
/// fichero o por qué no se ha podido leer o escribir; y, si ya se ha mirado, lo que ha aplicado el agente.</summary>
/// <param name="Applied">Lo aplicado, por el id de la nota; solo las notas que lo tienen, las demás están pendientes.</param>
/// <param name="AppliedWarning">Por qué no se ha tenido en cuenta lo aplicado, o parte; null si no hay nada que decir.</param>
/// <param name="Rounds">Las rondas con código, de la primera a la última; null si no se han buscado.</param>
/// <param name="RoundsWarning">Por qué alguna ronda no se puede ver aparte; null si no hay nada que decir.</param>
public sealed record ReviewState(Review? Review, string? FilePath, string? Error,
    IReadOnlyDictionary<string, ReviewOutcome>? Applied = null, string? AppliedWarning = null,
    IReadOnlyList<ReviewRound>? Rounds = null, string? RoundsWarning = null);

/// <summary>Una ronda de <c>/opsx:review</c> que cambió código: el commit de la rama justo antes de ella y los ficheros que
/// tocó la rama hasta la siguiente ronda (o hasta ahora, si es la última), sin lo que entró con un merge de otra rama.</summary>
/// <param name="Merged">De sus ficheros, los que también cambió un merge de otra rama (develop); null si ninguno.</param>
/// <param name="MergedBase">La punta de lo último que se mergeó, con que se comparan los de <paramref name="Merged"/>.</param>
/// <param name="StartUtc">Cuándo empieza: la hora del commit de su base; null si no se ha podido leer.</param>
public sealed record ReviewRound(int Round, string Base, IReadOnlyList<string> Files, IReadOnlyList<string>? Merged = null, string? MergedBase = null,
    DateTime? StartUtc = null);

/// <summary>Lo que hizo el agente con una nota en una ronda de <c>/opsx:review</c>.</summary>
/// <param name="Outcome">«aplicada», «contestada», «tarea» o «vista».</param>
public sealed record ReviewOutcome(string Outcome, int? Round, string? Commit, string? Task, string? Summary);

/// <summary>La PR de una rama: su número (null si no hay ninguna) o, si no se ha podido preguntar al remoto, por qué.</summary>
public sealed record PrLookup(int? Number, string? Error);

/// <summary>La revisión de una rama, guardada en <c>docs/reviews/&lt;worktree&gt;-review.md</c> del repositorio.</summary>
/// <remarks>
/// El fichero es la única fuente: se lee y se reescribe entero en cada cambio, y lo que la app necesita y no se lee a la
/// vista (huellas, ids) va en comentarios HTML. Se deja fuera de git en <c>.git/info/exclude</c>, local y sin tocar el
/// <c>.gitignore</c> del repositorio. Un fichero dado por revisado guarda la huella de git de su contenido; si cambia,
/// vuelve a «sin revisar».
/// </remarks>
public static partial class GitReview
{
    /// <summary>Tipos de nota, con cómo se dicen.</summary>
    public static readonly IReadOnlyDictionary<string, string> Kinds = new Dictionary<string, string>
    {
        ["correccion"] = "Corrección", ["mejora"] = "Mejora", ["duda"] = "Duda", ["bien"] = "Bien",
    };

    /// <summary>Carpeta de las revisiones dentro del repositorio, como se excluye en git.</summary>
    public const string Folder = "docs/reviews/";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // Las escrituras del fichero, de una en una: marcar y volver a mirar los cambios pueden llegar a la vez.
    private static readonly object Gate = new();

    // Las últimas rondas calculadas de cada repositorio, con la historia y lo aplicado con que se calcularon.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string Key, IReadOnlyList<ReviewRound> Rounds, string? Warning)> KnownRounds =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>La PR de la rama, si la hay: la de número más alto que apunta al commit que tiene el remoto de la rama; o por
    /// qué no se ha podido saber.</summary>
    /// <remarks>
    /// Gitea y GitHub publican cada PR como <c>refs/pull/N/head</c>; no dice si sigue abierta. Preguntar al remoto puede
    /// fallar una vez y salir bien a la siguiente (se ha visto con Gitea por HTTP: un «Authentication failed» suelto), así
    /// que se reintenta una vez.
    /// </remarks>
    public static PrLookup FindPr(string? cwd)
    {
        if (GitChanges.RootOf(cwd, out var error) is not { } root) return new(null, error);
        var tip = GitChanges.Run(root, "rev-parse", "@{upstream}");
        if (!tip.Ok) tip = GitChanges.Run(root, "rev-parse", "HEAD");
        if (!tip.Ok) return new(null, null);
        var sha = tip.Out.Trim();
        var refs = GitChanges.RunRemote(root, "ls-remote", "origin", "refs/pull/*/head");
        if (!refs.Ok) refs = GitChanges.RunRemote(root, "ls-remote", "origin", "refs/pull/*/head");
        if (!refs.Ok) return new(null, refs.Error ?? "El remoto no ha contestado: puede que haga falta volver a iniciar sesión en git.");
        int? found = null;
        foreach (var line in refs.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var m = PullRef().Match(line.Trim());
            if (m.Success && m.Groups[1].Value == sha && int.TryParse(m.Groups[2].Value, out var n) && (found is null || n > found)) found = n;
        }
        return new(found, null);
    }

    /// <summary>La revisión de la carpeta de una sesión, tal como está en su fichero; sin revisión si no se ha empezado.</summary>
    public static ReviewState Load(string? cwd)
    {
        if (PathOf(cwd, out var error) is not { } path) return new(null, null, error);
        lock (Gate) return new(Read(path), path, null);
    }

    /// <summary>Empieza la revisión de la rama con sus ficheros, sin ninguno revisado; si ya había una, la deja como está.</summary>
    public static ReviewState Start(string? cwd, string branch, string? baseLabel, int? pr, IEnumerable<string> files)
    {
        if (PathOf(cwd, out var error) is not { } path) return new(null, null, error);
        var root = GitChanges.RootOf(cwd, out _)!;
        lock (Gate)
        {
            if (Read(path) is { } existing) return new(existing, path, null);
            var review = new Review
            {
                Branch = branch, BaseLabel = baseLabel, Pr = pr, StartedUtc = DateTime.UtcNow,
                Files = files.Select(f => new ReviewMark { Path = f }).ToList(),
            };
            if (Exclude(root) is { } failed) return new(null, path, failed);
            return Save(path, review);
        }
    }

    /// <summary>Pone la revisión al día con los ficheros que tiene ahora la rama: añade los nuevos, quita los que ya no
    /// cambian, devuelve a «sin revisar» los revisados que han cambiado y recoloca las notas cuya línea se ha movido.</summary>
    public static ReviewState Sync(string? cwd, IEnumerable<string> files, int? pr = null)
    {
        if (PathOf(cwd, out var error) is not { } path) return new(null, null, error);
        var root = GitChanges.RootOf(cwd, out _)!;
        lock (Gate)
        {
            if (Read(path) is not { } review) return new(null, path, null);
            var before = Write(review);
            var now = files.ToList();
            var known = review.Files.ToDictionary(f => f.Path, StringComparer.Ordinal);
            review.Files = now.Select(p => known.TryGetValue(p, out var mark) ? mark : new ReviewMark { Path = p }).ToList();
            var blobs = Blobs(root, review.Files.Where(f => f.Reviewed).Select(f => f.Path));
            foreach (var mark in review.Files.Where(f => f.Reviewed))
            {
                if (blobs.GetValueOrDefault(mark.Path) == mark.Blob) continue;
                (mark.Reviewed, mark.Changed, mark.Blob) = (false, true, null);
            }
            foreach (var group in review.Notes.Where(n => n.File is not null && (n.Line is not null || n.Lost)).GroupBy(n => n.File!))
                Relocate(root, group.Key, group);
            if (pr is not null) review.Pr = pr;
            return Write(review) == before ? new(review, path, null) : Save(path, review);
        }
    }

    /// <summary>Da un fichero por revisado, con la huella que tiene ahora, o lo devuelve a «sin revisar».</summary>
    public static ReviewState Mark(string? cwd, string file, bool reviewed) => Mark(cwd, [file], reviewed);

    /// <summary>Da unos ficheros por revisados, cada uno con la huella que tiene ahora, o los devuelve a «sin revisar»; los
    /// que no están en la revisión se saltan.</summary>
    /// <remarks>Dar un fichero por revisado da también por leídas las respuestas de sus notas ya resueltas; devolverlo a
    /// «sin revisar» no las toca, porque leer una respuesta y revisar el fichero son cosas distintas.</remarks>
    public static ReviewState Mark(string? cwd, IReadOnlyCollection<string> files, bool reviewed)
    {
        if (PathOf(cwd, out var error) is not { } path) return new(null, null, error);
        var root = GitChanges.RootOf(cwd, out _)!;
        lock (Gate)
        {
            if (Read(path) is not { } review) return new(null, path, "No hay ninguna revisión empezada.");
            var marks = review.Files.Where(f => files.Contains(f.Path)).ToList();
            if (marks.Count == 0) return new(review, path, "Ese fichero no está en la revisión.");
            var blobs = reviewed ? Blobs(root, marks.Select(m => m.Path)) : [];
            foreach (var mark in marks)
            {
                mark.Reviewed = reviewed;
                mark.Changed = false;
                mark.Blob = reviewed ? blobs.GetValueOrDefault(mark.Path) : null;
            }
            if (reviewed)
            {
                var (applied, _) = ReadApplied(AppliedPathOf(path), review);
                foreach (var note in review.Notes.Where(n => n.File is not null && files.Contains(n.File)))
                    if (applied.TryGetValue(note.Id, out var outcome) && !IsRead(note, outcome)) note.Read = outcome.Round ?? 0;
            }
            return Save(path, review);
        }
    }

    /// <summary>Da por leídas las respuestas de unas notas resueltas, o las devuelve a «sin leer»; las que no están o
    /// siguen pendientes se saltan.</summary>
    public static ReviewState MarkRead(string? cwd, IReadOnlyCollection<string> ids, bool read)
    {
        if (PathOf(cwd, out var error) is not { } path) return new(null, null, error);
        lock (Gate)
        {
            if (Read(path) is not { } review) return new(null, path, "No hay ninguna revisión empezada.");
            var (applied, _) = ReadApplied(AppliedPathOf(path), review);
            foreach (var note in review.Notes.Where(n => ids.Contains(n.Id)))
                note.Read = read && applied.TryGetValue(note.Id, out var outcome) ? outcome.Round ?? 0 : null;
            return Save(path, review);
        }
    }

    /// <summary>Si la persona ha leído lo que contestó <c>/opsx:review</c> a una nota en su última ronda.</summary>
    public static bool IsRead(ReviewNote note, ReviewOutcome outcome) => note.Read is { } read && read >= (outcome.Round ?? 0);

    /// <summary>Añade una nota: en una línea o unas líneas de un fichero, en lo quitado de él, en un fichero o general.</summary>
    /// <param name="endLine">La última línea, si abarca varias.</param>
    /// <param name="removed">Lo quitado que abarca, con sus líneas en la base; si la nota es solo de eso, <paramref name="line"/>
    /// es la línea que lo sigue y <c>anchor</c>, true.</param>
    public static ReviewState AddNote(string? cwd, string kind, string? file, int? line, string? lineText, string text, int? endLine = null,
        (int Line, int? EndLine, string Text, bool Anchor)? removed = null)
    {
        if (PathOf(cwd, out var error) is not { } path) return new(null, null, error);
        if (!Kinds.ContainsKey(kind)) return new(null, path, "Ese tipo de nota no existe.");
        if (string.IsNullOrWhiteSpace(text)) return new(null, path, "La nota está vacía.");
        lock (Gate)
        {
            if (Read(path) is not { } review) return new(null, path, "No hay ninguna revisión empezada.");
            var next = review.Notes.Select(n => int.TryParse(n.Id.TrimStart('n'), out var i) ? i : 0).DefaultIfEmpty(0).Max() + 1;
            var note = new ReviewNote
            {
                Id = "n" + next, Kind = kind, File = file, Line = file is null ? null : line, LineText = file is null || line is null ? null : lineText,
                EndLine = file is null || line is null || endLine is not { } end || end <= line ? null : end,
                Text = text.Trim(), CreatedUtc = DateTime.UtcNow,
            };
            if (file is not null && line is not null && removed is { } gone)
            {
                (note.OldLine, note.Removed, note.Anchor) = (gone.Line, gone.Text, gone.Anchor);
                note.OldEndLine = gone.EndLine is { } oldEnd && oldEnd > gone.Line ? oldEnd : null;
                // Una nota solo de lo quitado no abarca la línea que la sitúa: una sola, la de después.
                if (gone.Anchor) note.EndLine = null;
            }
            review.Notes.Add(note);
            return Save(path, review);
        }
    }

    /// <summary>El fichero de revisión tal como está en disco, para enseñarlo; o por qué no se puede.</summary>
    public static GitText ReadText(string? cwd)
    {
        if (PathOf(cwd, out var error) is not { } path) return new(null, false, error);
        lock (Gate)
        {
            try
            {
                return File.Exists(path) ? new(File.ReadAllText(path), false, null) : new(null, false, "Aún no hay ninguna revisión empezada.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new(null, false, "No se ha podido leer la revisión.");
            }
        }
    }

    /// <summary>Quita una nota por su id.</summary>
    public static ReviewState RemoveNote(string? cwd, string id)
    {
        if (PathOf(cwd, out var error) is not { } path) return new(null, null, error);
        lock (Gate)
        {
            if (Read(path) is not { } review) return new(null, path, "No hay ninguna revisión empezada.");
            review.Notes.RemoveAll(n => n.Id == id);
            return Save(path, review);
        }
    }

    /// <summary>Resultados con que <c>/opsx:review</c> marca una nota, con cómo se dicen.</summary>
    public static readonly IReadOnlyDictionary<string, string> Outcomes = new Dictionary<string, string>
    {
        ["aplicada"] = "Aplicada", ["contestada"] = "Contestada", ["tarea"] = "Tarea", ["vista"] = "Vista",
    };

    /// <summary>Añade a la revisión lo que ha aplicado el agente, leído de <c>&lt;worktree&gt;-applied.json</c>, junto a su
    /// fichero; sin revisión, la deja como está.</summary>
    /// <remarks>
    /// Ese fichero lo escribe solo <c>/opsx:review</c> al cerrar cada ronda; la app solo lo lee. Así cada fichero tiene un
    /// dueño: la revisión es de la persona y lo aplicado, del agente, y el estado nunca se escribe en el <c>.md</c>.
    /// </remarks>
    public static ReviewState WithApplied(ReviewState state)
    {
        if (state.Review is not { } review || state.FilePath is not { } path) return state;
        var (applied, warning) = ReadApplied(AppliedPathOf(path), review);
        return state with { Applied = applied, AppliedWarning = warning };
    }

    /// <summary>Añade a la revisión sus rondas con código: dónde empezó cada una en la rama y qué ficheros tocó. Sin nada
    /// aplicado con commit, la deja como está.</summary>
    /// <remarks>
    /// Cada ronda empieza justo antes del más antiguo de los commits de sus notas aplicadas, buscado en la historia de la
    /// rama por su primer padre, y acaba donde empieza la siguiente: lo que se commitee entre dos rondas cuenta como de la
    /// anterior. Una ronda que solo contestó o pasó notas a otra tarea no cambió código, y no sale. Si los commits de una
    /// ya no están en la rama (un rebase o un squash los ha reescrito), tampoco sale, y se avisa.
    /// </remarks>
    public static ReviewState WithRounds(ReviewState state, string? cwd)
    {
        if (state.Applied is not { Count: > 0 } applied || GitChanges.RootOf(cwd, out _) is not { } root) return state;
        var wanted = applied.Values
            .Where(o => o.Outcome == "aplicada" && o.Round is not null && o.Commit is { } c && ShortCommit().IsMatch(c))
            .GroupBy(o => o.Round!.Value)
            .ToList();
        if (wanted.Count == 0) return state;
        // Las rondas solo cambian con la historia de la rama o con lo aplicado: al marcar un fichero o anotar, se reutilizan.
        var head = GitChanges.Run(root, "rev-parse", "HEAD") is { Ok: true } h ? h.Out.Trim() : null;
        var key = $"{head}|{string.Join(';', wanted.OrderBy(g => g.Key).Select(g => $"{g.Key}:{string.Join(',', g.Select(o => o.Commit).Order(StringComparer.Ordinal))}"))}";
        if (head is not null && KnownRounds.TryGetValue(root, out var known) && known.Key == key)
            return state with { Rounds = known.Rounds, RoundsWarning = known.Warning };
        var computed = ComputeRounds(root, wanted);
        if (head is not null) KnownRounds[root] = (key, computed.Rounds, computed.Warning);
        return state with { Rounds = computed.Rounds, RoundsWarning = computed.Warning };
    }

    /// <summary>Las rondas de una rama: dónde empieza cada una en su historia y qué tocó; o, sin historia, por qué no.</summary>
    private static (IReadOnlyList<ReviewRound> Rounds, string? Warning) ComputeRounds(string root, List<IGrouping<int, ReviewOutcome>> wanted)
    {
        var start = GitChanges.BranchStart(root);
        var log = GitChanges.Run(root, "rev-list", "--first-parent", "--parents", "--reverse", "--max-count=2000", start is null ? "HEAD" : $"{start}..HEAD");
        if (!log.Ok) return ([], "No se ha podido leer la historia de la rama: las rondas no se pueden ver aparte.");
        // Cada commit con su primer padre, del más antiguo al más nuevo; el primero del repositorio no tiene padre.
        var history = log.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(' ')).Where(ids => ids.Length >= 2).ToList();
        var found = new List<(int Round, int At, string Base)>();
        var lost = new List<int>();
        foreach (var round in wanted)
        {
            var at = history.FindIndex(ids => round.Any(o => ids[0].StartsWith(o.Commit!, StringComparison.OrdinalIgnoreCase)));
            if (at < 0) lost.Add(round.Key);
            else found.Add((round.Key, at, history[at][1]));
        }
        found.Sort((a, b) => a.At.CompareTo(b.At));
        var rounds = new List<ReviewRound>();
        for (var i = 0; i < found.Count; i++)
            rounds.Add(RoundOf(root, found[i].Round, found[i].Base, i + 1 < found.Count ? found[i + 1].Base : "HEAD"));
        lost.Sort();
        var warning = lost.Count switch
        {
            0 => null,
            1 => $"Los commits de la ronda {lost[0]} ya no están en la rama (un rebase o un squash los ha reescrito): esa ronda no se puede ver aparte.",
            _ => $"Los commits de las rondas {string.Join(", ", lost)} ya no están en la rama (un rebase o un squash los ha reescrito): no se pueden ver aparte.",
        };
        return (rounds.OrderBy(r => r.Round).ToList(), warning);
    }

    /// <summary>Devuelve las rondas ya calculadas de la carpeta de una sesión, sin volver a mirar git; vacío si aún no se han
    /// calculado.</summary>
    public static IReadOnlyList<ReviewRound> KnownRoundsOf(string? cwd) =>
        GitChanges.RootOf(cwd, out _) is { } root && KnownRounds.TryGetValue(root, out var known) ? known.Rounds : [];

    /// <summary>Lo que cambió la rama en una ronda, entre su base y donde acaba: los ficheros de sus propios commits y los que
    /// un merge dejó con algo suyo (un conflicto resuelto a mano); de estos, los que también trajo un merge de otra rama, y
    /// la punta de lo último que se mergeó.</summary>
    /// <remarks>
    /// Lo que entra con un merge de develop no es de la ronda: contado como suyo, una ronda enseñaría el trabajo de las
    /// otras ramas. Un fichero que tocaron las dos se compara con lo mergeado, para no enseñar como de la ronda lo que hizo
    /// develop.
    /// </remarks>
    internal static ReviewRound RoundOf(string root, int round, string from, string end)
    {
        static IEnumerable<string> Names(GitChanges.Result run) => run.Ok ? run.Out.Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];
        var files = new SortedSet<string>(StringComparer.Ordinal);
        files.UnionWith(Names(GitChanges.Run(root, "log", "--first-parent", "--no-merges", "--name-only", "-z", "--format=", "-M", $"{from}..{end}")));
        var merges = GitChanges.Run(root, "rev-list", "--first-parent", "--merges", $"{from}..{end}");
        var brought = new HashSet<string>(StringComparer.Ordinal);
        string? mergedTip = null;
        foreach (var merge in merges.Ok ? merges.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [])
        {
            files.UnionWith(Names(GitChanges.Run(root, "show", "--cc", "--name-only", "-z", "--format=", merge)));
            brought.UnionWith(Names(GitChanges.Run(root, "diff", "--name-only", "-z", "-M", "--no-ext-diff", $"{merge}^1", merge)));
            // La lista va de la más nueva a la más vieja: la primera es lo último que se mergeó.
            mergedTip ??= GitChanges.Run(root, "rev-parse", $"{merge}^2") is { Ok: true } tip ? tip.Out.Trim() : null;
        }
        var merged = files.Where(brought.Contains).ToList();
        DateTime? start = GitChanges.Run(root, "show", "-s", "--format=%cI", from) is { Ok: true } when
                          && DateTimeOffset.TryParse(when.Out.Trim(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var at)
            ? at.UtcDateTime : null;
        return new(round, from, [.. files], merged.Count > 0 ? merged : null, merged.Count > 0 ? mergedTip : null, start);
    }

    /// <summary>Dónde deja <c>/opsx:review</c> lo aplicado de una revisión: el mismo nombre, acabado en <c>-applied.json</c>.</summary>
    internal static string AppliedPathOf(string reviewPath) =>
        Path.Combine(Path.GetDirectoryName(reviewPath)!, Path.GetFileName(reviewPath)[..^"-review.md".Length] + "-applied.json");

    /// <summary>Lo aplicado de cada nota de una revisión y, si hace falta, un aviso de por qué no se ha tenido en cuenta.</summary>
    /// <remarks>
    /// Sin fichero no hay nada aplicado ni aviso: es lo normal antes de la primera ronda. Un fichero roto o de otra rama se
    /// ignora entero, con aviso, sin romper la revisión. Cada entrada va con su nota por el id y la fecha de creación juntos,
    /// porque al borrar la última nota la siguiente reutiliza su id: la de otra fecha es otra nota, y sigue pendiente. La
    /// fecha se compara al segundo, por si el agente la copia sin sus fracciones. Las entradas de notas que ya no están se
    /// ignoran; las que no se entienden, también, y se cuentan en el aviso. Si una nota sale dos veces, gana la ronda más alta.
    /// </remarks>
    internal static (Dictionary<string, ReviewOutcome> Applied, string? Warning) ReadApplied(string path, Review review)
    {
        var applied = new Dictionary<string, ReviewOutcome>(StringComparer.Ordinal);
        var name = Path.GetFileName(path);
        try
        {
            if (!File.Exists(path)) return (applied, null);
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("notes", out var notes) || notes.ValueKind != JsonValueKind.Array)
                return (applied, $"{name} no tiene la forma esperada: todas las notas salen pendientes.");
            var branch = root.TryGetProperty("branch", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : null;
            if (!string.Equals(branch, review.Branch, StringComparison.Ordinal))
                return (applied, $"{name} es de la rama {branch ?? "(sin rama)"}, no de {review.Branch}: no se tiene en cuenta.");
            var odd = 0;
            foreach (var entry in notes.EnumerateArray())
            {
                if (Entry(entry) is not { } found)
                {
                    odd++;
                    continue;
                }
                var note = review.Notes.FirstOrDefault(n => n.Id == found.Id && Math.Abs((n.CreatedUtc.ToUniversalTime() - found.Created).TotalSeconds) < 1);
                if (note is null) continue;
                if (applied.TryGetValue(note.Id, out var known) && (known.Round ?? 0) > (found.Outcome.Round ?? 0)) continue;
                applied[note.Id] = found.Outcome;
            }
            return (applied, odd == 0 ? null : odd == 1 ? $"Una entrada de {name} no se entiende y se ignora." : $"{odd} entradas de {name} no se entienden y se ignoran.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return ([], $"No se ha podido leer {name}: todas las notas salen pendientes.");
        }
    }

    /// <summary>Una entrada de lo aplicado: el id y la fecha de su nota y lo que se hizo; null si le falta algo o no se entiende.</summary>
    private static (string Id, DateTime Created, ReviewOutcome Outcome)? Entry(JsonElement e)
    {
        string? Text(string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;
        if (e.ValueKind != JsonValueKind.Object || Text("id") is not { } id || Text("outcome") is not { } outcome || !Outcomes.ContainsKey(outcome)) return null;
        if (!e.TryGetProperty("createdUtc", out var c) || c.ValueKind != JsonValueKind.String || !c.TryGetDateTime(out var created)) return null;
        int? round = e.TryGetProperty("round", out var r) && r.ValueKind == JsonValueKind.Number && r.TryGetInt32(out var n) ? n : null;
        return (id, created.ToUniversalTime(), new ReviewOutcome(outcome, round, Text("commit"), Text("task"), Text("summary")));
    }

    /// <summary>Dónde va el fichero de revisión de la carpeta de una sesión: <c>docs/reviews/&lt;worktree&gt;-review.md</c>.</summary>
    private static string? PathOf(string? cwd, out string error)
    {
        if (GitChanges.RootOf(cwd, out error) is not { } root) return null;
        var name = string.Concat(Path.GetFileName(Path.TrimEndingDirectorySeparator(root)).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c));
        return Path.Combine(root, "docs", "reviews", $"{name}-review.md");
    }

    /// <summary>Deja <c>docs/reviews/</c> fuera de git en <c>info/exclude</c> del repositorio, compartido por sus worktrees;
    /// null si ya estaba o se ha añadido, o por qué no se ha podido.</summary>
    private static string? Exclude(string root)
    {
        var common = GitChanges.Run(root, "rev-parse", "--git-common-dir");
        if (!common.Ok) return "No se ha encontrado la carpeta de git del repositorio.";
        var file = Path.Combine(Path.GetFullPath(Path.Combine(root, common.Out.Trim())), "info", "exclude");
        try
        {
            var text = File.Exists(file) ? File.ReadAllText(file) : "";
            if (text.Split('\n').Any(l => l.Trim() == Folder)) return null;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.AppendAllText(file, (text.Length > 0 && !text.EndsWith('\n') ? "\n" : "") + Folder + "\n");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "No se ha podido dejar la revisión fuera de git.";
        }
    }

    /// <summary>La huella de git del contenido de unos ficheros, como está ahora en disco; «borrado» si ya no existe.</summary>
    private static Dictionary<string, string> Blobs(string root, IEnumerable<string> files)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var present = new List<string>();
        foreach (var file in files)
        {
            if (GitChanges.Inside(root, file) is { } full && File.Exists(full)) present.Add(file);
            else result[file] = "borrado";
        }
        if (present.Count == 0) return result;
        var hashed = GitChanges.Run(root, int.MaxValue, string.Join('\n', present) + "\n", TimeSpan.FromSeconds(5), "hash-object", "--stdin-paths");
        var lines = hashed.Ok ? hashed.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries) : [];
        for (var i = 0; i < present.Count && i < lines.Length; i++) result[present[i]] = lines[i].Trim();
        return result;
    }

    /// <summary>Vuelve a encontrar la línea de las notas de un fichero por su texto, la más cercana a donde estaba; si ya
    /// no está, la nota se queda en el fichero, sin línea.</summary>
    private static void Relocate(string root, string file, IEnumerable<ReviewNote> notes)
    {
        string[] lines;
        try
        {
            lines = GitChanges.Inside(root, file) is { } full && File.Exists(full) ? File.ReadAllLines(full) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }
        foreach (var note in notes)
        {
            if (note.LineText is not { } wanted) continue;
            var target = wanted.Trim();
            var at = note.Line ?? 1;
            if (at >= 1 && at <= lines.Length && lines[at - 1].Trim() == target)
            {
                note.Lost = false;
                continue;
            }
            var best = Enumerable.Range(1, lines.Length).Where(i => lines[i - 1].Trim() == target).OrderBy(i => Math.Abs(i - at)).FirstOrDefault();
            if (best == 0)
            {
                note.Lost = true;
                continue;
            }
            // Un rango se mueve entero: su final, lo mismo que su principio.
            if (note.EndLine is { } end) note.EndLine = end + best - at;
            (note.Line, note.Lost) = (best, false);
        }
    }

    /// <summary>Guarda la revisión en su fichero, entero y de una vez, y la devuelve; o por qué no se ha podido.</summary>
    private static ReviewState Save(string path, Review review)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, Write(review), new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
            return new(review, path, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(review, path, "No se ha podido guardar la revisión.");
        }
    }

    /// <summary>La revisión en Markdown, para leerla a la vista y para que la lea Claude, con lo de la app en comentarios.</summary>
    internal static string Write(Review review)
    {
        var text = new StringBuilder();
        var head = new { review.Branch, review.Pr, Base = review.BaseLabel, Started = review.StartedUtc };
        text.Append($"# Revisión de {review.Branch}\n\n");
        text.Append($"<!-- arkko-review {JsonSerializer.Serialize(head, Json)} -->\n\n");
        var line = new List<string> { $"Rama `{review.Branch}`" };
        if (review.Pr is { } pr) line.Add($"PR #{pr}");
        if (review.BaseLabel is { } label) line.Add($"contra `{label}`");
        line.Add($"empezada el {review.StartedUtc.ToLocalTime().ToString("dd/MM/yyyy 'a las' HH:mm", CultureInfo.InvariantCulture)}");
        text.Append(string.Join(" · ", line)).Append(". La lleva Arkko: se lee aquí, pero se cambia desde la app.\n\n");

        text.Append($"## Ficheros ({review.Files.Count(f => f.Reviewed)} de {review.Files.Count} revisados)\n\n");
        foreach (var f in review.Files)
        {
            var meta = f.Reviewed ? JsonSerializer.Serialize(new { blob = f.Blob }, Json) : f.Changed ? """{"changed":true}""" : null;
            text.Append($"- [{(f.Reviewed ? 'x' : ' ')}] `{f.Path}`");
            if (f.Changed) text.Append(" — ha cambiado desde que se revisó");
            if (meta is not null) text.Append($" <!-- {meta} -->");
            text.Append('\n');
        }

        text.Append($"\n## Notas ({review.Notes.Count})\n");
        foreach (var n in review.Notes)
        {
            var where = n.File is null ? "general"
                : n.Line is { } at && !n.Lost ? (n.EndLine is { } end ? $"`{n.File}:{at}-{end}`" : $"`{n.File}:{at}`") : $"`{n.File}`";
            if (n.Removed is not null) where += n.Anchor ? " · lo quitado justo antes" : " · y lo quitado";
            text.Append($"\n### {Kinds.GetValueOrDefault(n.Kind, n.Kind)} · {where}\n\n");
            text.Append($"<!-- {JsonSerializer.Serialize(n, Json)} -->\n\n");
            if (n.Lost) text.Append("_Su línea ya no está en el fichero._\n\n");
            if (n.LineText is { Length: > 0 } quoted && !n.Anchor) text.Append($"> `{quoted.Trim()}`\n\n");
            // Lo quitado va citado línea a línea: al leer la nota, lo citado se salta, porque sale de sus datos.
            if (n.Removed is { } removed)
            {
                var span = n.OldEndLine is { } oldEnd ? $"líneas {n.OldLine}-{oldEnd}" : $"línea {n.OldLine}";
                text.Append($"> Quitado ({span} de la base):\n");
                foreach (var gone in removed.Replace("\r\n", "\n").Split('\n')) text.Append($"> − `{gone.Trim()}`\n");
                text.Append('\n');
            }
            // Una línea de la nota que empiece como una cabecera de nota se escapa: si no, al leerla partiría la nota en dos.
            text.Append(string.Join('\n', n.Text.Replace("\r\n", "\n").Split('\n').Select(l => l.StartsWith("###", StringComparison.Ordinal) ? "\\" + l : l))).Append('\n');
        }
        return text.ToString();
    }

    /// <summary>Lee la revisión de su fichero; null si no existe o no es de Arkko.</summary>
    internal static Review? Read(string path)
    {
        string text;
        try
        {
            if (!File.Exists(path)) return null;
            text = File.ReadAllText(path).Replace("\r\n", "\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        if (Header().Match(text) is not { Success: true } header) return null;
        Review review;
        try
        {
            using var doc = JsonDocument.Parse(header.Groups[1].Value);
            var h = doc.RootElement;
            review = new Review
            {
                Branch = h.TryGetProperty("branch", out var b) ? b.GetString() ?? "" : "",
                Pr = h.TryGetProperty("pr", out var p) && p.TryGetInt32(out var pr) ? pr : null,
                BaseLabel = h.TryGetProperty("base", out var l) ? l.GetString() : null,
                StartedUtc = h.TryGetProperty("started", out var s) && s.TryGetDateTime(out var at) ? at.ToUniversalTime() : DateTime.UtcNow,
            };
        }
        catch (JsonException)
        {
            return null;
        }

        var lines = text.Split('\n');
        foreach (var line in lines)
        {
            if (FileLine().Match(line) is not { Success: true } m) continue;
            var mark = new ReviewMark { Path = m.Groups[2].Value, Reviewed = m.Groups[1].Value == "x" };
            if (m.Groups[3].Success)
            {
                try
                {
                    using var doc = JsonDocument.Parse(m.Groups[3].Value);
                    if (doc.RootElement.TryGetProperty("blob", out var blob)) mark.Blob = blob.GetString();
                    mark.Changed = doc.RootElement.TryGetProperty("changed", out var changed) && changed.ValueKind == JsonValueKind.True;
                }
                catch (JsonException)
                {
                    // Una marca ilegible deja el fichero sin huella: si estaba revisado, el próximo repaso lo desmarca.
                }
            }
            review.Files.Add(mark);
        }

        var start = Array.FindIndex(lines, l => l.StartsWith("## Notas", StringComparison.Ordinal));
        if (start < 0) return review;
        ReviewNote? note = null;
        var body = new List<string>();
        void Close()
        {
            if (note is null) return;
            note.Text = string.Join('\n', body).Trim();
            review.Notes.Add(note);
        }
        foreach (var line in lines.Skip(start + 1))
        {
            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                Close();
                (note, body) = (null, []);
                continue;
            }
            if (note is null && NoteMeta().Match(line) is { Success: true } meta)
            {
                try
                {
                    note = JsonSerializer.Deserialize<ReviewNote>(meta.Groups[1].Value, Json);
                }
                catch (JsonException)
                {
                    note = null;
                }
                continue;
            }
            // La cita de la línea y el aviso de línea perdida se escriben a partir de los datos de la nota: no son texto.
            if (note is not null && body.Count == 0 && (line.StartsWith("> ", StringComparison.Ordinal) || line.StartsWith("_Su línea", StringComparison.Ordinal) || line.Length == 0)) continue;
            if (note is not null) body.Add(line.StartsWith("\\###", StringComparison.Ordinal) ? line[1..] : line);
        }
        Close();
        return review;
    }

    [GeneratedRegex(@"^([0-9a-f]{7,64})\s+refs/pull/(\d+)/head$")]
    private static partial Regex PullRef();

    // Se escribe la de Arkko y se lee también la de cuando la app se llamaba Agent Pulse, que llevan las revisiones de
    // antes y pasan a la nueva al guardarlas.
    [GeneratedRegex(@"<!-- (?:agent-pulse|arkko)-review (\{.*\}) -->")]
    private static partial Regex Header();

    [GeneratedRegex(@"^- \[( |x)\] `(.+?)`.*?(?:<!-- (\{.*\}) -->)?$")]
    private static partial Regex FileLine();

    [GeneratedRegex(@"^<!-- (\{.*\}) -->$")]
    private static partial Regex NoteMeta();

    // El commit de una nota aplicada, corto o entero; más corto podría ser el principio de varios.
    [GeneratedRegex("^[0-9a-fA-F]{7,64}$")]
    private static partial Regex ShortCommit();
}
