using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Media;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>La galería web de un agente, donde está todo lo que ha publicado, también lo que no deja rastro en el equipo.</summary>
public sealed record ArtifactGallery(string Agent, string Name, string Url);

/// <summary>Cómo se dice, con qué icono y de qué color va lo publicado, igual en Artifacts, Sesiones, el Diario y Worktrees.</summary>
public static class ArtifactTexts
{
    /// <summary>El grupo de lo publicado desde carpetas que no son un repositorio de git.</summary>
    public const string NoRepo = "Sin repositorio";

    /// <summary>El grupo de lo que solo se sabe por la galería: sin sesión en este equipo, y por tanto sin repositorio.</summary>
    public const string FromGallery = "De claude.ai";

    /// <summary>Las galerías de los agentes conectados; cada agente nuevo añade la suya.</summary>
    public static readonly IReadOnlyList<ArtifactGallery> Galleries =
    [
        new("claude", "Claude", "https://claude.ai/code/artifacts"),
    ];

    // Si cada carpeta de trabajo está dentro de un repositorio de git, y cuál; se mira el disco una vez por carpeta.
    private static readonly Dictionary<string, string?> Roots = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>El nombre de una clase: «Página», «Documento»…</summary>
    public static string Kind(ArtifactKind kind) => kind switch
    {
        ArtifactKind.Page => "Página",
        ArtifactKind.Document => "Documento",
        ArtifactKind.Slides => "Presentación",
        ArtifactKind.Dashboard => "Dashboard",
        ArtifactKind.Design => "Diseño",
        ArtifactKind.Animation => "Animación",
        ArtifactKind.Unknown => "Artifact",
        _ => "Otro",
    };

    /// <summary>El nombre de una clase en plural, para los filtros: «Páginas», «Documentos»…</summary>
    public static string Plural(ArtifactKind kind) => kind switch
    {
        ArtifactKind.Page => "Páginas",
        ArtifactKind.Document => "Documentos",
        ArtifactKind.Slides => "Presentaciones",
        ArtifactKind.Dashboard => "Dashboards",
        ArtifactKind.Design => "Diseños",
        ArtifactKind.Animation => "Animaciones",
        ArtifactKind.Unknown => "Sin clase",
        _ => "Otros",
    };

    /// <summary>El icono de una clase, de la fuente de iconos de la app.</summary>
    public static string Icon(ArtifactKind kind) => kind switch
    {
        ArtifactKind.Document => "",
        ArtifactKind.Slides => "",
        ArtifactKind.Dashboard => "",
        ArtifactKind.Design => "",
        ArtifactKind.Animation => "",
        _ => "",
    };

    /// <summary>El color de una clase, de la paleta de datos de la app.</summary>
    /// <remarks>
    /// Es un color de dato, para reconocer la clase de un vistazo, no un acento: lo elegido y lo nuevo siguen en el verde del
    /// dossier. Lo de la galería sin clase va en el color de Claude, como su chispa: dice de dónde viene. Por eso los diseños
    /// van en rosa y no en el naranja de la paleta, que se confundiría con él.
    /// </remarks>
    public static IBrush Color(ArtifactKind kind, double opacity = 1) => kind switch
    {
        ArtifactKind.Page => ThemeColors.Of("DataSonnet", "#61AFEF", opacity),
        ArtifactKind.Document => ThemeColors.Of("DataFiveHour", "#E5C07B", opacity),
        ArtifactKind.Slides => ThemeColors.Of("DataFable", "#C678DD", opacity),
        ArtifactKind.Dashboard => ThemeColors.Of("DataHaiku", "#98C379", opacity),
        ArtifactKind.Design => new SolidColorBrush(Avalonia.Media.Color.Parse("#F472B6"), opacity),
        ArtifactKind.Animation => ThemeColors.Of("DataCache", "#56B6C2", opacity),
        ArtifactKind.Unknown => new SolidColorBrush(Avalonia.Media.Color.Parse("#D97757"), opacity),
        _ => ThemeColors.Of("DataOther", "#ABB2BF", opacity),
    };

    /// <summary>El color de un grupo: el de su repositorio, como en el resto de la app, o apagado fuera de uno.</summary>
    public static IBrush HomeBrush(string home) => home is NoRepo or FromGallery ? ThemeColors.Of("FgFaint", "#8A8D9C") : RepoColors.Of(home);
    /// <summary>El verde del dossier, el único acento: lo elegido, lo nuevo y la acción principal.</summary>
    public static IBrush Green(double opacity = 1) => new SolidColorBrush(Avalonia.Media.Color.Parse("#3FB950"), opacity);

    /// <summary>«1 artifact» o «3 artifacts».</summary>
    public static string Count(int n) => n == 1 ? "1 artifact" : $"{n} artifacts";

    /// <summary>El repositorio con que se agrupa: el del worktree, el de la carpeta si está dentro de uno, o «Sin repositorio».</summary>
    public static string Home(AgentArtifact item)
    {
        if (item.FromGallery) return item.Repo ?? FromGallery;
        if (item.Repo is { } repo) return repo;
        if (item.Cwd is not { } cwd) return NoRepo;
        if (!Roots.TryGetValue(cwd, out var root)) Roots[cwd] = root = GitRoot(cwd);
        return root ?? NoRepo;
    }

    /// <summary>Los repositorios de las carpetas de todas las sesiones de Claude Code, también las cerradas, y los que están
    /// junto a ellos.</summary>
    /// <remarks>
    /// Los vecinos cuentan porque un repositorio puede no tener ningún transcript: Claude Code los borra pasado su plazo.
    /// De las sesiones solo se lee el principio de cada transcript, y se acuerda de lo leído.
    /// </remarks>
    public static IEnumerable<string> SessionRepos()
    {
        var roots = SessionCatalog.Folders().Select(RootOf).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var nearby = roots.Select(Path.GetDirectoryName).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).SelectMany(ReposIn);
        return roots.Select(r => Path.GetFileName(r)).Concat(nearby);
    }

    /// <summary>La carpeta raíz del repositorio que contiene una carpeta: la de encima de un <c>.bare</c> o la del <c>.git</c>;
    /// null si no está en ninguno.</summary>
    private static string? RootOf(string folder)
    {
        try
        {
            for (var dir = new DirectoryInfo(folder.TrimEnd('\\', '/')); dir is not null; dir = dir.Parent)
                if (dir.Name.Equals(".bare", StringComparison.OrdinalIgnoreCase)) return dir.Parent?.FullName;
                else if (Directory.Exists(Path.Combine(dir.FullName, ".bare")) || Directory.Exists(Path.Combine(dir.FullName, ".git"))) return dir.FullName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            JsonStore.LogError("artifacts", ex);
        }
        return null;
    }

    /// <summary>Los repositorios que hay dentro de una carpeta, como los de <c>source\repos</c>.</summary>
    private static IEnumerable<string> ReposIn(string parent)
    {
        try
        {
            return Directory.EnumerateDirectories(parent)
                .Where(d => Directory.Exists(Path.Combine(d, ".bare")) || Directory.Exists(Path.Combine(d, ".git")))
                .Select(d => Path.GetFileName(d)).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            JsonStore.LogError("artifacts", ex);
            return [];
        }
    }

    /// <summary>El nombre de la carpeta del repositorio que contiene una carpeta; null si no está en ninguno o ya no existe.</summary>
    private static string? GitRoot(string folder)
    {
        try
        {
            for (var dir = new DirectoryInfo(folder); dir is not null; dir = dir.Parent)
                if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, ".git"))) return dir.Name;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            JsonStore.LogError("artifacts", ex);
        }
        return null;
    }

    /// <summary>La sesión que lo publicó, por su nombre si se le dio uno; si no, por cuándo: «Sesión del 29/10 a las 10:05».</summary>
    /// <remarks>Primero el de la sesión abierta, que puede haberse renombrado; después, el que guardó el índice.</remarks>
    public static string Session(AgentArtifact item)
    {
        if (item.FromGallery) return "Sin sesión en este equipo";
        var name = Pieces.Session(item.SessionId)?.SessionName ?? item.SessionName
                   ?? JsonStore.Read<SessionState>(AppPaths.SessionFile(item.SessionId))?.SessionName;
        return !string.IsNullOrWhiteSpace(name) ? name : $"Sesión del {When(item.CreatedUtc)}";
    }

    /// <summary>Una fecha en hora local: «29/10 a las 10:05».</summary>
    public static string When(DateTime utc) => utc.ToLocalTime().ToString("dd/MM 'a las' HH:mm", CultureInfo.InvariantCulture);

    /// <summary>La ficha de un artifact al pasar el ratón: su título, su clase y de dónde salió.</summary>
    public static Control Card(AgentArtifact item)
    {
        if (item.FromGallery)
            return DiaryInk.Card(item.Title ?? "Sin título", "De la galería de claude.ai", HomeBrush(Home(item)),
                rows: item.Repo is { } moved ? [("Repositorio", $"{moved}, puesto a mano"), ("Última versión", When(item.UpdatedUtc))] : [("Última versión", When(item.UpdatedUtc))],
                footer: "No está en los transcripts de este equipo: se hizo en el chat, en otro equipo o en una sesión cuyo transcript ya se borró. Por eso no se sabe su repositorio, su sesión ni su clase.");
        var home = Home(item);
        var rows = new List<(string, string)> { ("Repositorio", home) };
        if (item.Project is { } folder && folder != home) rows.Add(("Carpeta", folder));
        rows.Add(("Agente", (Galleries.FirstOrDefault(g => g.Agent == item.Agent)?.Name ?? item.Agent) + (item.Cowork ? " · Cowork" : " · Claude Code")));
        rows.Add(("Sesión", Session(item)));
        rows.Add(("Publicado", When(item.CreatedUtc)));
        if (item.Versions > 1) rows.Add(("Última versión", $"{When(item.UpdatedUtc)} · {item.Versions} versiones"));
        if (item.File is { } file) rows.Add(("Fichero", file));
        var footer = item.Deleted ? "Se borró de claude.ai desde una sesión: el enlace ya no abre nada."
            : item.Removed ? "Quitado de la lista de Arkko: en claude.ai sigue existiendo."
            : "Clic para abrirlo; clic derecho para más opciones.";
        return DiaryInk.Card(item.Title ?? "Sin título", Kind(item.Kind), HomeBrush(home), rows: rows, footer: footer);
    }
}

/// <summary>Una fila de la lista de Artifacts: lo publicado, con su clase, su carpeta, su sesión y cuándo.</summary>
public sealed class ArtifactRow(AgentArtifact item) : INotifyPropertyChanged
{
    private bool _selected;

    public event PropertyChangedEventHandler? PropertyChanged;

    public AgentArtifact Item => item;

    /// <summary>Si es lo que se ve al lado.</summary>
    public bool IsSelected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    /// <summary>Si se ha publicado o actualizado en el último día: lleva el punto verde.</summary>
    public bool IsNew => DateTime.UtcNow - item.UpdatedUtc < TimeSpan.FromDays(1);

    /// <summary>Dónde está, para la cabecera del detalle: «tienda / DEVLAB-112», o el grupo si no hay carpeta.</summary>
    public string Place => Folder is { } folder ? $"{ArtifactTexts.Home(item)} / {folder}" : ArtifactTexts.Home(item);

    /// <summary>El color de su repositorio, para la barrita de la cabecera del detalle.</summary>
    public IBrush HomeBrush => ArtifactTexts.HomeBrush(ArtifactTexts.Home(item));

    /// <summary>Si se puede ir a su sesión: lo de la galería no tiene.</summary>
    public bool HasSession => !item.FromGallery;
    public string Title => item.Title ?? "Sin título";
    public string KindText => ArtifactTexts.Kind(item.Kind);

    /// <summary>Sin clase conocida: su baldosa lleva la chispa de Claude en lugar del icono de una clase.</summary>
    public bool IsUnknown => item.Kind == ArtifactKind.Unknown;
    public bool HasIcon => !IsUnknown;
    public string Icon => ArtifactTexts.Icon(item.Kind);
    public IBrush KindBrush => ArtifactTexts.Color(item.Kind);
    public IBrush KindFill => ArtifactTexts.Color(item.Kind, 0.14);

    /// <summary>La carpeta, cuando dice algo que el grupo no dice: el worktree de un repositorio, o la carpeta suelta.</summary>
    public string? Folder => item.Project is { } folder && !string.Equals(folder, ArtifactTexts.Home(item), StringComparison.OrdinalIgnoreCase) ? folder : null;
    public bool FolderVisible => Folder is not null;

    public string SessionText => ArtifactTexts.Session(item);
    public string WhenText => TasksModel.Ago(item.UpdatedUtc);

    /// <summary>«v12»: cuántas veces se ha publicado, si más de una.</summary>
    public string VersionText => $"v{item.Versions}";
    public bool VersionVisible => item.Versions > 1;

    /// <summary>Por qué no está en la lista: «Borrado en claude.ai» o «Quitado».</summary>
    public string GoneText => item.Deleted ? "Borrado en claude.ai" : "Quitado";

    /// <summary>Si se puede devolver a la lista: lo borrado de claude.ai ya no abre nada.</summary>
    public bool CanRestore => item.Removed && !item.Deleted;

    /// <summary>Si su HTML de origen sigue en el equipo, para ir a su carpeta.</summary>
    public bool CanReveal => item.File is { } file && File.Exists(file);

    public Control Tip => ArtifactTexts.Card(item);
}

/// <summary>Un repositorio en la lista de Artifacts, o un grupo de lo publicado fuera de uno: su color, sus filas y su sitio.</summary>
public sealed record ArtifactGroup(string Name, int Count, List<ArtifactRow> Rows, bool Folded, bool CanUp, bool CanDown)
{
    /// <summary>La flecha de la cabecera: hacia abajo desplegado, hacia la derecha plegado.</summary>
    public string Chevron => Folded ? "" : "";

    public bool Open => !Folded;

    /// <summary>La barrita de la cabecera: el color del repositorio, como en el resto de la app, y apagada fuera de uno.</summary>
    public IBrush Brush => ArtifactTexts.HomeBrush(Name);

    public string CountText => Count.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Un filtro por clase, como chip con su icono: «Páginas 5».</summary>
public sealed class ArtifactKindChoice(ArtifactsModel model, ArtifactKind? kind, string text, int count) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public ArtifactKind? Kind => kind;
    public string Text => text;
    public string CountText => count.ToString(CultureInfo.InvariantCulture);
    public string Icon => kind is { } k ? ArtifactTexts.Icon(k) : "";
    public IBrush Brush => ArtifactTexts.Color(kind ?? ArtifactKind.Other);
    public bool IconVisible => kind is not null and not ArtifactKind.Unknown;

    /// <summary>La de sin clase lleva la chispa de Claude en lugar de un icono.</summary>
    public bool SparkVisible => kind == ArtifactKind.Unknown;

    public bool IsOn
    {
        get => model.Kind == kind;
        set
        {
            if (value) model.Kind = kind;
        }
    }

    /// <summary>Avisa de que puede haber cambiado si está elegido.</summary>
    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsOn)));
}

/// <summary>Cómo tiene la persona la sección: el orden de los repositorios y los plegados, en data/artifacts-view.json.</summary>
public sealed class ArtifactViewPrefs
{
    public List<string> Order { get; set; } = [];
    public List<string> Folded { get; set; } = [];
}

/// <summary>Estado de la sección Artifacts: la lista por repositorio, lo elegido para verlo al lado, los filtros, el orden y lo
/// quitado.</summary>
public sealed class ArtifactsModel : INotifyPropertyChanged
{
    private static string PrefsFile => Path.Combine(AppPaths.Data, "artifacts-view.json");

    private readonly ArtifactMonitor _monitor = ArtifactMonitor.Instance;
    private ArtifactKind? _kind;
    private string _query = "";
    private bool _showRemoved;
    private (string Group, string? Project)? _scope;
    private ArtifactRow? _selected;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Se dispara al elegir otro artifact, para abrirlo al lado.</summary>
    public event Action<ArtifactRow?>? SelectedChanged;

    public ObservableCollection<ArtifactGroup> Groups { get; } = [];
    public ObservableCollection<ArtifactKindChoice> Kinds { get; } = [];
    public ObservableCollection<ArtifactRow> RemovedRows { get; } = [];

    /// <summary>Lo elegido en la lista, que se ve al lado; null si nada.</summary>
    public ArtifactRow? Selected
    {
        get => _selected;
        private set
        {
            if (_selected == value) return;
            if (_selected is { } before) before.IsSelected = false;
            _selected = value;
            if (value is not null) value.IsSelected = true;
            OnChanged();
            OnChanged(nameof(HasSelected));
            OnChanged(nameof(GalleryVisible));
            OnChanged(nameof(SplitVisible));
        }
    }

    public bool HasSelected => _selected is not null;

    /// <summary>La galería, la entrada de la sección: se ve mientras no hay nada abierto.</summary>
    public bool GalleryVisible => ListVisible && _selected is null;

    /// <summary>La lista compacta con lo abierto al lado: se ve mientras hay algo abierto.</summary>
    public bool SplitVisible => ListVisible && _selected is not null;

    /// <summary>Abre un artifact, o lo cierra y vuelve a la galería con null; elegir el que ya está no lo vuelve a abrir.</summary>
    public void Select(ArtifactRow? row)
    {
        if (row?.Item.Url == _selected?.Item.Url) return;
        Selected = row;
        SelectedChanged?.Invoke(row);
    }

    /// <summary>La clase elegida; null, todas.</summary>
    public ArtifactKind? Kind
    {
        get => _kind;
        set
        {
            if (_kind == value) return;
            _kind = value;
            Arrange();
        }
    }

    /// <summary>Lo que se busca en el título, la carpeta, el repositorio y la sesión.</summary>
    public string Query
    {
        get => _query;
        set
        {
            if (_query == value) return;
            _query = value ?? "";
            Arrange();
        }
    }

    /// <summary>Si se ve lo quitado de la lista.</summary>
    public bool ShowRemoved
    {
        get => _showRemoved;
        set
        {
            if (_showRemoved == value) return;
            _showRemoved = value;
            OnChanged();
            OnChanged(nameof(RemovedListVisible));
        }
    }

    /// <summary>Pliega un repositorio, o lo despliega si ya lo estaba; se recuerda entre sesiones de la app.</summary>
    public void ToggleFold(string group)
    {
        var prefs = Prefs();
        if (prefs.Folded.RemoveAll(f => string.Equals(f, group, StringComparison.OrdinalIgnoreCase)) == 0) prefs.Folded.Add(group);
        Save(prefs);
        Arrange();
    }

    /// <summary>Sube o baja un repositorio en la lista; el orden que queda se recuerda entre sesiones de la app.</summary>
    public void Move(string group, int step)
    {
        var order = Groups.Select(g => g.Name).ToList();
        var at = order.FindIndex(n => string.Equals(n, group, StringComparison.OrdinalIgnoreCase));
        var to = at + step;
        if (at < 0 || to < 0 || to >= order.Count) return;
        (order[at], order[to]) = (order[to], order[at]);
        var prefs = Prefs();
        // Lo que no se ve ahora (por un filtro) conserva su sitio detrás de lo que sí.
        prefs.Order = order.Concat(prefs.Order.Where(n => !order.Contains(n, StringComparer.OrdinalIgnoreCase))).ToList();
        Save(prefs);
        Arrange();
    }

    /// <summary>Abre un artifact por su enlace; si los filtros lo dejan fuera, se quitan. Falso si no está en la lista.</summary>
    public bool Open(string url)
    {
        if (Find(url) is null)
        {
            _kind = null;
            _query = "";
            _scope = null;
            OnChanged(nameof(Query));
            Arrange();
        }
        if (Find(url) is not { } row) return false;
        Select(row);
        return true;
    }

    /// <summary>La fila de un artifact en la lista que se ve, por su enlace.</summary>
    private ArtifactRow? Find(string url) => Groups.SelectMany(g => g.Rows).FirstOrDefault(r => r.Item.Url == url);

    /// <summary>Deja ver solo lo de un repositorio o una carpeta, como al llegar desde Worktrees; null, todo.</summary>
    public void Only(string? group, string? project = null)
    {
        _scope = group is null ? null : (group, project);
        Arrange();
    }

    private int _shown;
    private DateTime? _latest;

    /// <summary>«17 artifacts · el último hace 1 h».</summary>
    public string Summary => _shown == 0 ? "" : string.Join(" · ", new[]
    {
        ArtifactTexts.Count(_shown),
        _latest is { } at ? $"el último {TasksModel.Ago(at)}" : null,
    }.OfType<string>());

    public bool ScopeVisible => _scope is not null;

    /// <summary>El chip del filtro de procedencia: «Solo arkko-stack · DEVLAB-125».</summary>
    public string ScopeText => _scope is { } s ? "Solo " + (s.Project is { } p && p != s.Group ? $"{s.Group} · {p}" : s.Group) : "";

    public string RemovedText => $"Quitados · {RemovedRows.Count}";
    public bool RemovedVisible => RemovedRows.Count > 0;
    public bool RemovedListVisible => _showRemoved && RemovedRows.Count > 0;

    /// <summary>La primera lectura de los transcripts aún no ha acabado.</summary>
    public bool LoadingVisible => !_monitor.Loaded;
    public bool ListVisible => !LoadingVisible && Groups.Count > 0;
    public bool EmptyVisible => !LoadingVisible && Groups.Count == 0;

    /// <summary>Si no hay nada porque los filtros lo dejan fuera, y no porque no se haya publicado nada.</summary>
    public bool Filtered => _monitor.Items.Any(a => !a.Removed && !a.Deleted);

    public string EmptyTitle => Filtered ? "Nada coincide" : "Nada publicado todavía";
    public string EmptyText => Filtered
        ? "Prueba con otra búsqueda u otra clase."
        : "Aquí sale lo que tus agentes publiquen desde las sesiones de este equipo: páginas, documentos, presentaciones…";

    /// <summary>Rehace la lista con lo último leído, los filtros y el orden de la persona, y conserva lo elegido.</summary>
    public void Arrange()
    {
        var all = _monitor.Items;
        var listed = all.Where(a => !a.Removed && !a.Deleted && InScope(a)).ToList();

        // Las clases que hay, de la que más tiene a la que menos, y lo sin clase siempre al final; si la elegida ya no tiene
        // ninguna, se vuelve a todas.
        var kinds = listed.GroupBy(a => a.Kind).OrderBy(g => g.Key == ArtifactKind.Unknown)
            .ThenByDescending(g => g.Count()).ThenBy(g => g.Key).ToList();
        if (_kind is { } chosen && kinds.All(g => g.Key != chosen)) _kind = null;
        Kinds.Clear();
        if (kinds.Count > 1)
        {
            Kinds.Add(new ArtifactKindChoice(this, null, "Todo", listed.Count));
            foreach (var g in kinds) Kinds.Add(new ArtifactKindChoice(this, g.Key, ArtifactTexts.Plural(g.Key), g.Count()));
        }

        var shown = listed.Where(a => (_kind is null || a.Kind == _kind) && Matches(a)).ToList();
        _shown = shown.Count;
        _latest = shown.Count == 0 ? null : shown.Max(a => a.UpdatedUtc);

        // Sin orden de la persona: los repositorios, del de lo más reciente al que menos; después, lo publicado fuera de uno, y
        // al final, lo que solo se sabe por la galería. Con orden, primero lo que ella ha colocado.
        var prefs = Prefs();
        var groups = shown.GroupBy(ArtifactTexts.Home, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key == ArtifactTexts.FromGallery ? 2 : g.Key == ArtifactTexts.NoRepo ? 1 : 0)
            .ThenByDescending(g => g.Max(a => a.UpdatedUtc))
            .Select((g, i) => (Group: g, Rank: prefs.Order.FindIndex(n => string.Equals(n, g.Key, StringComparison.OrdinalIgnoreCase)), Default: i))
            .OrderBy(x => x.Rank < 0 ? int.MaxValue : x.Rank).ThenBy(x => x.Default)
            .Select(x => x.Group).ToList();
        var keep = _selected?.Item.Url;
        ArtifactRow? again = null;
        Groups.Clear();
        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            var rows = group.OrderByDescending(a => a.UpdatedUtc).Select(a => new ArtifactRow(a)).ToList();
            again ??= rows.FirstOrDefault(r => r.Item.Url == keep);
            var folded = prefs.Folded.Contains(group.Key, StringComparer.OrdinalIgnoreCase);
            Groups.Add(new ArtifactGroup(group.Key, rows.Count, rows, folded, i > 0, i < groups.Count - 1));
        }

        RemovedRows.Clear();
        foreach (var a in all.Where(a => (a.Removed || a.Deleted) && InScope(a)).OrderByDescending(a => a.UpdatedUtc)) RemovedRows.Add(new ArtifactRow(a));
        if (RemovedRows.Count == 0) _showRemoved = false;

        // Lo elegido sigue elegido si sigue en la lista, ya con sus datos al día; si no, se suelta.
        _selected = null;
        Selected = again;
        if (again is null && keep is not null) SelectedChanged?.Invoke(null);

        foreach (var name in new[] { nameof(Kind), nameof(Summary), nameof(ScopeVisible), nameof(ScopeText), nameof(RemovedText), nameof(RemovedVisible),
                     nameof(RemovedListVisible), nameof(ShowRemoved), nameof(LoadingVisible), nameof(ListVisible), nameof(GalleryVisible), nameof(SplitVisible), nameof(EmptyVisible), nameof(Filtered),
                     nameof(EmptyTitle), nameof(EmptyText) })
            OnChanged(name);
        foreach (var choice in Kinds) choice.Refresh();
    }

    /// <summary>El orden y los plegados guardados; vacíos si no hay nada guardado.</summary>
    private static ArtifactViewPrefs Prefs() => JsonStore.Read<ArtifactViewPrefs>(PrefsFile) ?? new ArtifactViewPrefs();

    /// <summary>Guarda el orden y los plegados; si falla, se apunta y la vista sigue con lo de ahora.</summary>
    private static void Save(ArtifactViewPrefs prefs)
    {
        try
        {
            JsonStore.Write(PrefsFile, prefs);
        }
        catch (IOException ex)
        {
            JsonStore.LogError("artifacts", ex);
        }
    }

    /// <summary>Si entra en el filtro de procedencia.</summary>
    private bool InScope(AgentArtifact a) => _scope is not { } s ||
        (string.Equals(a.Group, s.Group, StringComparison.OrdinalIgnoreCase) &&
         (s.Project is null || string.Equals(a.Project, s.Project, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Si coincide con lo que se busca, sin mayúsculas.</summary>
    private bool Matches(AgentArtifact a)
    {
        var q = _query.Trim();
        if (q.Length == 0) return true;
        return new[] { a.Title, a.Project, ArtifactTexts.Home(a), ArtifactTexts.Session(a), ArtifactTexts.Kind(a.Kind) }
            .Any(t => t?.Contains(q, StringComparison.CurrentCultureIgnoreCase) == true);
    }

    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
