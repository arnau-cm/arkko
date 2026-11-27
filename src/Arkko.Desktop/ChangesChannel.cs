using System.Text.Json;
using Avalonia.Threading;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Lo de git y la revisión de una página de Sesiones: atiende lo que pide sobre la carpeta de su sesión (lo
/// cambiado, el diff o el fichero de uno, y anotar o marcar en la revisión) y le manda las respuestas.</summary>
/// <remarks>
/// Cada página lleva el suyo, sea la de la sección o la de una ventana aparte, y todos se conocen: cuando uno cambia una
/// revisión, la reciben todas las páginas que enseñan esa misma revisión, para que lo anotado en una se vea en las demás.
/// Las escrituras ya van de una en una en <see cref="GitReview"/>, así que aquí solo se reparte el resultado. Lo cambiado
/// se pone al día solo: la carpeta de cada sesión que se enseña se vigila (<see cref="GitWatcher"/>, uno por carpeta para
/// todas las páginas que la enseñan) y, al cambiar algo, cada página vuelve a recibir lo que había pedido. Una página que
/// no se ve (<see cref="Paused"/>) lo recibe al volver a verse. Todo se llama desde el hilo de la ventana; git se lee
/// fuera de él.
/// </remarks>
internal sealed class ChangesChannel : IDisposable
{
    // Los canales de las páginas abiertas, para repartir entre ellos lo que cambia.
    private static readonly List<ChangesChannel> Open = [];

    // Un vigilante por carpeta de sesión, compartido por los canales que la enseñan; sin vigilante mientras se monta.
    private static readonly Dictionary<string, (GitWatcher? Watcher, int Users)> Watching = new(PathComparer);

    private readonly Action<object> _post;
    private readonly Func<(string Id, string? Cwd)?> _target;

    // El fichero de revisión de la carpeta que enseña la página, por el que se reconoce qué revisiones le tocan.
    private string? _reviewPath;

    // Lo cambiado de cada alcance se lee de uno en uno: lo que se pida mientras, se lee al acabar, una vez. Con cada uno,
    // si lo pidió un aviso de la carpeta.
    private readonly HashSet<string> _gitBusy = [];
    private readonly Dictionary<string, bool> _gitPending = [];

    // Las lecturas de lo cambiado en marcha, por alcance y carpeta, para que las páginas que piden lo mismo esperen a una.
    private static readonly Dictionary<string, Task<(GitChangeSet Set, ReviewState? Review)>> Reading = new(PathComparer);

    // La carpeta vigilada para esta página, los alcances que ha pedido de su sesión y si hay un aviso sin atender por no verse.
    private string? _watched;
    private string? _askedId;
    private readonly HashSet<string> _asked = [];
    private bool _paused;
    private bool _dirty;

    /// <summary>Crea el canal de una página y lo da a conocer a los demás.</summary>
    /// <param name="post">Cómo se le manda un mensaje a la página.</param>
    /// <param name="target">La sesión que enseña la página ahora, con su carpeta; null si ninguna.</param>
    public ChangesChannel(Action<object> post, Func<(string Id, string? Cwd)?> target)
    {
        _post = post;
        _target = target;
        Open.Add(this);
    }

    /// <summary>Si la página no se ve (su ventana minimizada, o la sección escondida): mientras, los avisos de la carpeta se
    /// guardan, y al volver a verse se atienden una vez.</summary>
    public bool Paused
    {
        get => _paused;
        set
        {
            _paused = value;
            if (value || !_dirty) return;
            _dirty = false;
            Refresh();
        }
    }

    /// <summary>Deja de recibir lo que cambian los demás y de vigilar su carpeta, al cerrarse su página.</summary>
    public void Dispose()
    {
        Open.Remove(this);
        Unwatch();
    }

    /// <summary>Atiende un mensaje de la página si es de git o de la revisión.</summary>
    /// <returns>true si era suyo; false si lo tiene que atender otro.</returns>
    public bool Handle(string? type, JsonElement m)
    {
        switch (type)
        {
            case "changes":
                var scope = Str(m, "scope") ?? "pending";
                Follow(scope);
                SendChanges(scope, fresh: false);
                return true;
            case "reviewPr":
                SendReview(cwd => (GitReview.Load(cwd), GitReview.FindPr(cwd)));
                return true;
            case "reviewLoad":
                SendReview(cwd => (GitReview.Load(cwd), null));
                return true;
            case "reviewStart":
                SendReview(cwd =>
                {
                    var set = GitChanges.List(cwd, GitScope.Branch);
                    if (set.Error is { } failed) return (new ReviewState(null, null, failed), null);
                    var pr = GitReview.FindPr(cwd);
                    return (GitReview.Start(cwd, set.Branch ?? "", set.BaseLabel, pr.Number, set.Files.Select(f => f.Path)), pr);
                });
                return true;
            case "reviewMark" when m.TryGetProperty("paths", out var markPaths) && markPaths.ValueKind == JsonValueKind.Array:
                var marked = markPaths.EnumerateArray().Select(p => p.GetString()).OfType<string>().ToList();
                var markReviewed = m.TryGetProperty("reviewed", out var r) && r.ValueKind == JsonValueKind.True;
                SendReview(cwd => (GitReview.Mark(cwd, marked, markReviewed), null));
                return true;
            case "reviewNote" when Str(m, "kind") is { } noteKind && Str(m, "text") is { } noteText:
                var noteFile = Str(m, "file");
                // Sin línea (una nota general o al fichero) llega null, que no es un número que leer.
                var noteLine = Int(m, "line");
                var noteLineText = Str(m, "lineText");
                var noteEnd = Int(m, "endLine");
                // Lo quitado que abarca, en el diff: sus líneas en la base, su texto y si la nota es solo de eso.
                (int, int?, string, bool)? noteRemoved = Int(m, "oldLine") is { } oldNo && Str(m, "removed") is { } removedText
                    ? (oldNo, Int(m, "oldEndLine"), removedText, m.TryGetProperty("anchor", out var an) && an.ValueKind == JsonValueKind.True)
                    : null;
                SendReview(cwd => (GitReview.AddNote(cwd, noteKind, noteFile, noteLine, noteLineText, noteText, noteEnd, noteRemoved), null));
                return true;
            case "reviewFile":
                SendChangeText("reviewFile", "", null, GitReview.ReadText);
                return true;
            case "reviewNoteRemove" when Str(m, "id") is { } noteId:
                SendReview(cwd => (GitReview.RemoveNote(cwd, noteId), null));
                return true;
            case "reviewRead" when m.TryGetProperty("ids", out var readIds) && readIds.ValueKind == JsonValueKind.Array:
                var ids = readIds.EnumerateArray().Select(p => p.GetString()).OfType<string>().ToList();
                var read = m.TryGetProperty("read", out var rd) && rd.ValueKind == JsonValueKind.True;
                SendReview(cwd => (GitReview.MarkRead(cwd, ids, read), null));
                return true;
            case "reviewShow":
                Share(m);
                return true;
            case "changeDiff" when Str(m, "path") is { } diffPath:
                var diffBase = Str(m, "base");
                var oldPath = Str(m, "oldPath");
                SendChangeText("changeDiff", diffPath, diffBase, cwd => GitChanges.Diff(cwd, diffBase, diffPath, oldPath));
                return true;
            case "changeFile" when Str(m, "path") is { } filePath:
                SendChangeText("changeFile", filePath, null, cwd => GitChanges.ReadFile(cwd, filePath));
                return true;
            case "repoGrep" when Str(m, "query") is { } grepQuery:
                _grepNext = new(grepQuery, Flag(m, "matchCase"), Flag(m, "wholeWord"), Flag(m, "regex"), Int(m, "seq") ?? 0);
                if (!_grepBusy) SendGrep();
                return true;
            default:
                return false;
        }
    }

    /// <summary>Manda los ficheros cambiados en la carpeta de la sesión: lo pendiente o todo lo de su rama.</summary>
    /// <param name="fresh">Si se manda por un aviso de la carpeta: la página vuelve a leer el fichero que enseña, que puede
    /// haber cambiado aunque cuente las mismas líneas.</param>
    /// <remarks>Con toda la rama, la revisión se pone al día de paso, y se reparte como cualquier otro cambio suyo.</remarks>
    private async void SendChanges(string scope, bool fresh)
    {
        if (!_gitBusy.Add(scope))
        {
            _gitPending[scope] = fresh || _gitPending.GetValueOrDefault(scope);
            return;
        }
        try
        {
            if (_target() is not ({ } id, var cwd)) return;
            var (set, review) = await ReadShared(cwd, scope);
            if (_target()?.Id != id) return;
            _post(new
            {
                type = "changes", id, scope, root = set.Root, branch = set.Branch, @base = set.Base, baseLabel = set.BaseLabel,
                note = set.Note, files = set.Files, truncated = set.Truncated, error = set.Error, upstream = set.Upstream, ahead = set.Ahead,
                fresh = fresh ? true : (bool?)null,
            });
            // Las demás páginas de esta revisión vigilan la misma carpeta y la reciben con su propia lectura.
            if (review is not null) PostReview(review, null);
        }
        catch (Exception ex)
        {
            JsonStore.LogError("sessions-git", ex);
        }
        finally
        {
            _gitBusy.Remove(scope);
            if (_gitPending.Remove(scope, out var again)) SendChanges(scope, again);
        }
    }

    /// <summary>Lee lo cambiado de un alcance en una carpeta y, si es toda la rama, pone al día su revisión; si ya se está
    /// leyendo para otra página, espera a esa lectura en lugar de lanzar otra.</summary>
    /// <remarks>Tras un aviso de la carpeta la piden a la vez todas las páginas que la enseñan: así git se lanza una vez.</remarks>
    private static Task<(GitChangeSet Set, ReviewState? Review)> ReadShared(string? cwd, string scope)
    {
        var since = SinceOf(scope);
        var whole = scope == RepoScope;
        var gitScope = GitChanges.ScopeOf(scope);
        var key = $"{(since is null && !whole ? gitScope.ToString() : scope)}|{(cwd is null ? "" : Path.GetFullPath(cwd))}";
        if (Reading.TryGetValue(key, out var running)) return running;
        var reading = Task.Run(() =>
        {
            if (since is not null) return (GitChanges.Since(cwd, since), (ReviewState?)null);
            if (whole) return (GitChanges.ListAll(cwd), (ReviewState?)null);
            var found = GitChanges.List(cwd, gitScope);
            // Con toda la rama a la vista, la revisión se pone al día: lo revisado que ha cambiado vuelve a «sin revisar».
            var synced = gitScope == GitScope.Branch && found.Error is null
                ? GitReview.WithRounds(GitReview.WithApplied(GitReview.Sync(cwd, found.Files.Select(f => f.Path))), cwd)
                : null;
            return (found, synced);
        });
        Reading[key] = reading;
        _ = reading.ContinueWith(_ => Dispatcher.UIThread.Post(() =>
        {
            if (Reading.TryGetValue(key, out var current) && current == reading) Reading.Remove(key);
        }));
        return reading;
    }

    /// <summary>Hace algo con la revisión de la carpeta de la sesión, fuera del hilo de la ventana, y reparte cómo queda;
    /// con su PR, si se ha buscado.</summary>
    private async void SendReview(Func<string?, (ReviewState State, PrLookup? Pr)> act)
    {
        if (_target() is not ({ } id, var cwd)) return;
        try
        {
            // Con cada cambio de la revisión se vuelve a mirar lo aplicado: si no, la página perdería las marcas al anotar.
            var (review, pr) = await Task.Run(() =>
            {
                var (state, found) = act(cwd);
                return (GitReview.WithRounds(GitReview.WithApplied(state), cwd), found);
            });
            if (_target()?.Id == id) Publish(review, pr);
        }
        catch (Exception ex)
        {
            JsonStore.LogError("sessions-review", ex);
        }
    }

    /// <summary>Pasa a las demás páginas de la misma revisión lo que ha elegido ver esta: la ronda, o las notas o el fichero
    /// de la revisión, para que Cambios y las notas abiertas aparte enseñen lo mismo.</summary>
    private void Share(JsonElement m)
    {
        if (_target() is not ({ } id, _) || _reviewPath is not { } path) return;
        var round = Pieces.RoundOf(m);
        var view = Str(m, "view");
        var hasRound = m.TryGetProperty("round", out _);
        foreach (var other in Open.ToList())
            if (other != this && PathsEqual(other._reviewPath, path))
                other._post(new { type = "reviewShow", id = other._target()?.Id ?? id, round, hasRound, view });
    }

    /// <summary>Manda la revisión a esta página y a todas las que enseñan la misma.</summary>
    private void Publish(ReviewState state, PrLookup? pr)
    {
        PostReview(state, pr);
        if (state.FilePath is not { } path) return;
        foreach (var other in Open.ToList())
            if (other != this && PathsEqual(other._reviewPath, path)) other.PostReview(state, pr);
    }

    /// <summary>Manda a la página la revisión de la sesión que enseña: la revisión (o ninguna), su fichero y, si se ha
    /// buscado, su PR (sin número si no la hay) o por qué no se ha podido mirar.</summary>
    private void PostReview(ReviewState state, PrLookup? pr)
    {
        if (_target() is not ({ } id, _)) return;
        _reviewPath = state.FilePath ?? _reviewPath;
        _post(new
        {
            type = "review", id, review = state.Review, path = state.FilePath, error = state.Error, pr = pr?.Number, prError = pr?.Error, prChecked = pr is not null,
            applied = state.Applied, appliedWarning = state.AppliedWarning, rounds = state.Rounds, roundsWarning = state.RoundsWarning,
        });
    }

    /// <summary>El alcance con todos los ficheros del repositorio, no solo los cambiados.</summary>
    public const string RepoScope = "repo";

    /// <summary>El commit de un alcance «desde un commit» (<c>since:&lt;commit&gt;</c>), con que se ve una ronda de la revisión;
    /// null si es otro alcance.</summary>
    private static string? SinceOf(string scope) => scope.StartsWith("since:", StringComparison.Ordinal) ? scope["since:".Length..] : null;

    /// <summary>Manda el diff de un fichero cambiado o el fichero entero, leídos fuera del hilo de la ventana.</summary>
    /// <param name="baseCommit">Con qué se comparó el diff, para que la página descarte uno que llegue de otra base.</param>
    private async void SendChangeText(string type, string path, string? baseCommit, Func<string?, GitText> read)
    {
        if (_target() is not ({ } id, var cwd)) return;
        try
        {
            var text = await Task.Run(() => read(cwd));
            if (_target()?.Id == id) _post(new { type, id, path, @base = baseCommit, text = text.Text, truncated = text.Truncated, error = text.Error });
        }
        catch (Exception ex)
        {
            JsonStore.LogError("sessions-git", ex);
        }
    }

    // Una búsqueda de texto que ha pedido la página: lo buscado, sus opciones y su número, que la página usa para quedarse
    // con la última.
    private sealed record GrepAsk(string Query, bool MatchCase, bool WholeWord, bool Regex, int Seq);

    // La última búsqueda pedida que aún no se ha lanzado, y si hay una en marcha.
    private GrepAsk? _grepNext;
    private bool _grepBusy;

    /// <summary>Busca texto en el repositorio de la sesión y manda lo encontrado a la página; mientras busca, se queda solo
    /// con la última búsqueda que llegue y la lanza al acabar.</summary>
    /// <remarks>Al escribir llegan varias seguidas: así hay como mucho un git grep en marcha y las intermedias no se lanzan.</remarks>
    private async void SendGrep()
    {
        _grepBusy = true;
        try
        {
            while (_grepNext is { } ask)
            {
                _grepNext = null;
                if (_target() is not ({ } id, var cwd)) break;
                var found = await Task.Run(() => GitChanges.Grep(cwd, ask.Query, ask.MatchCase, ask.WholeWord, ask.Regex));
                if (_target()?.Id != id || _grepNext is not null) continue;
                _post(new { type = "repoGrep", id, seq = ask.Seq, query = ask.Query, hits = found.Hits, truncated = found.Truncated, error = found.Error });
            }
        }
        catch (Exception ex)
        {
            JsonStore.LogError("sessions-git", ex);
        }
        finally
        {
            _grepBusy = false;
        }
    }

    /// <summary>Apunta un alcance que la página ha pedido y vigila la carpeta de su sesión; si la sesión ha cambiado,
    /// olvida lo que pedía la anterior y deja de vigilar su carpeta.</summary>
    private void Follow(string scope)
    {
        if (_target() is not ({ } id, var cwd)) return;
        if (id != _askedId)
        {
            _asked.Clear();
            _askedId = id;
        }
        // Se ve una ronda a la vez: la que se veía antes deja de leerse con cada aviso de la carpeta.
        if (SinceOf(scope) is not null) _asked.RemoveWhere(asked => SinceOf(asked) is not null);
        _asked.Add(scope);
        var folder = cwd is null ? null : Path.GetFullPath(cwd);
        if (folder is not null && _watched is not null && PathComparer.Equals(folder, _watched)) return;
        Unwatch();
        if (folder is not null) Watch(folder);
    }

    /// <summary>Vigila una carpeta para esta página: con el vigilante que ya tenga otra página o con uno nuevo, que se monta
    /// fuera del hilo de la ventana porque lanza git.</summary>
    private void Watch(string folder)
    {
        _watched = folder;
        if (Watching.TryGetValue(folder, out var shared))
        {
            Watching[folder] = shared with { Users = shared.Users + 1 };
            return;
        }
        Watching[folder] = (null, 1);
        _ = Task.Run(() => new GitWatcher(folder)).ContinueWith(made => Dispatcher.UIThread.Post(() =>
        {
            if (made.IsFaulted)
            {
                JsonStore.LogError("git-watch", made.Exception!);
                return;
            }
            var watcher = made.Result;
            // Si mientras se montaba ya nadie enseña la carpeta, se suelta.
            if (!Watching.TryGetValue(folder, out var now) || now.Watcher is not null)
            {
                watcher.Dispose();
                return;
            }
            watcher.Changed += () => Dispatcher.UIThread.Post(() => FolderChanged(folder));
            Watching[folder] = now with { Watcher = watcher };
        }));
    }

    /// <summary>Deja de vigilar la carpeta de esta página; el vigilante se suelta cuando ya no lo usa ninguna.</summary>
    private void Unwatch()
    {
        if (_watched is not { } folder) return;
        _watched = null;
        if (!Watching.TryGetValue(folder, out var shared)) return;
        if (shared.Users > 1)
        {
            Watching[folder] = shared with { Users = shared.Users - 1 };
            return;
        }
        Watching.Remove(folder);
        shared.Watcher?.Dispose();
    }

    /// <summary>Ha cambiado algo en una carpeta vigilada: lo vuelven a recibir todas las páginas que la enseñan.</summary>
    private static void FolderChanged(string folder)
    {
        foreach (var channel in Open.ToList())
            if (channel._watched is { } watched && PathComparer.Equals(watched, folder)) channel.Refresh();
    }

    /// <summary>Vuelve a mandar lo cambiado de cada alcance que ha pedido la página; si no se ve, lo deja para cuando se vea.</summary>
    private void Refresh()
    {
        if (_paused)
        {
            _dirty = true;
            return;
        }
        foreach (var scope in _asked.ToList()) SendChanges(scope, fresh: true);
    }

    /// <summary>Compara rutas como el sistema: sin distinguir mayúsculas en Windows y macOS, que no las distinguen por
    /// defecto, y distinguiéndolas en Linux.</summary>
    private static StringComparer PathComparer => OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    /// <summary>Si dos rutas son el mismo fichero.</summary>
    private static bool PathsEqual(string? a, string? b) =>
        a is not null && b is not null && PathComparer.Equals(Path.GetFullPath(a), Path.GetFullPath(b));

    /// <summary>Valor de texto de una propiedad del mensaje, o null.</summary>
    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Valor entero de una propiedad del mensaje, o null si falta o no es un número.</summary>
    private static int? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;

    /// <summary>Si una propiedad del mensaje es true; false si falta o es otra cosa.</summary>
    private static bool Flag(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
