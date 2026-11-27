using Avalonia.Media;
using Avalonia.Media.Immutable;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>El color de cada repositorio y de su subtablero del panel, el mismo en Sesiones, Tablero, Diario, Estadísticas y Worktrees.</summary>
/// <remarks>
/// Un repositorio se une a su subtablero por el <c>board</c> de su <c>.arkko.json</c> o, si no lo declara, por
/// llamarse igual. El color de la pareja es, por orden: el que eligió la persona (en Sesiones o en Configuración →
/// Tableros, que escriben el mismo), el que da el panel al subtablero y, si no hay ninguno, uno fijo por el nombre del
/// repositorio. Un repositorio sin panel lleva el elegido o el fijo; un subtablero sin repositorio, como hasta ahora.
/// Todos se pintan con la luminosidad ajustada al tema, para que un color oscuro se vea sobre fondo oscuro y uno claro
/// sobre fondo claro.
/// </remarks>
internal static class RepoColors
{
    /// <summary>Un subtablero de los tableros elegidos: cómo se llama, el color que le da el panel y el elegido para él.</summary>
    private sealed record SubBoard(string Id, string Name, string? PanelColor, string? Chosen);

    private static AppSettings? _settings;
    private static Action? _save;
    private static Dictionary<string, string?> _repos = new(StringComparer.OrdinalIgnoreCase);
    private static List<SubBoard> _boards = [];

    /// <summary>Se dispara en el hilo de la ventana cuando cambia algún color o qué repositorio va con qué subtablero.</summary>
    public static event Action? Changed;

    /// <summary>Recibe la configuración, donde van los colores elegidos de cada repositorio, y cómo guardarla.</summary>
    public static void Init(AppSettings settings, Action save)
    {
        _settings = settings;
        _save = save;
        TaskDirectory.Instance.Changed += () =>
        {
            RebuildBoards();
            Changed?.Invoke();
        };
        App.ThemeChanged += () => Changed?.Invoke();
        RebuildBoards();
    }

    /// <summary>Pone los repositorios conocidos con el subtablero que declara cada uno, y repinta si algo ha cambiado.</summary>
    public static void SetRepos(Dictionary<string, string?> repos)
    {
        var same = repos.Count == _repos.Count &&
                   repos.All(r => _repos.TryGetValue(r.Key, out var b) && string.Equals(b, r.Value, StringComparison.OrdinalIgnoreCase));
        if (same) return;
        _repos = new Dictionary<string, string?>(repos, StringComparer.OrdinalIgnoreCase);
        Remember();
        Changed?.Invoke();
    }

    /// <summary>Avisa de que ha cambiado un color elegido, para que todo lo que lo enseña se repinte.</summary>
    public static void NotifyChanged()
    {
        RebuildBoards();
        Changed?.Invoke();
    }

    /// <summary>El color de un repositorio: el elegido, el de su subtablero en el panel o uno fijo por su nombre.</summary>
    /// <remarks>Sale ya ajustado al tema con <see cref="Readable"/>, como todos los de esta clase.</remarks>
    public static IBrush Of(string repo, double opacity = 1) =>
        Hex(repo) is { } hex && HexColor.ToRgb(hex) is { } c
            ? Readable(new ImmutableSolidColorBrush(Color.FromRgb(c.R, c.G, c.B), opacity))
            : Readable(ProjectColors.For(repo, opacity));

    /// <summary>El color de un subtablero en el Tablero: el de su repositorio si tiene, y si no, el elegido, el del panel o uno fijo.</summary>
    public static IBrush OfSubBoard(BoardConnection connection, BoardRef board)
    {
        var repo = RepoOf(board.Id, board.Name);
        if (repo is not null && Chosen(repo) is { } mine) return Readable(BoardColors.BrushOf(mine));
        if (connection.SubBoardColors.TryGetValue(board.Id, out var picked) && HexColor.ToRgb(picked) is not null) return Readable(BoardColors.BrushOf(picked));
        if (board.Color is { } panel && HexColor.ToRgb(panel) is not null) return Readable(BoardColors.BrushOf(panel));
        return Readable(ProjectColors.For(repo ?? board.Name));
    }

    /// <summary>El color de una franja con el nombre en blanco encima: el mismo, oscurecido lo justo si es claro.</summary>
    public static IBrush Band(IBrush brush)
    {
        if (brush is not ISolidColorBrush { Color: var c } solid) return brush;
        var (r, g, b) = HexColor.UnderWhite((c.R, c.G, c.B));
        return r == c.R && g == c.G && b == c.B ? brush : new ImmutableSolidColorBrush(Color.FromArgb(c.A, r, g, b), solid.Opacity);
    }

    /// <summary>El mismo color con la luminosidad justa para verse en el tema activo: un azul marino se aclara en los oscuros.</summary>
    /// <remarks>
    /// El color guardado no cambia: solo cómo se pinta. La página de Sesiones aplica la misma regla en CSS, así que un
    /// repositorio se ve igual en las dos.
    /// </remarks>
    private static IBrush Readable(IBrush brush)
    {
        if (brush is not ISolidColorBrush { Color: var c } solid) return brush;
        var (r, g, b) = HexColor.Readable((c.R, c.G, c.B), dark: !App.Light);
        return r == c.R && g == c.G && b == c.B ? brush : new ImmutableSolidColorBrush(Color.FromArgb(c.A, r, g, b), solid.Opacity);
    }

    /// <summary>El repositorio que va con un subtablero, si alguno lo declara o se llama igual; null si ninguno.</summary>
    public static string? RepoOf(string boardId, string boardName)
    {
        foreach (var (repo, declared) in _repos)
            if (declared is not null && (Same(declared, boardId) || Same(declared, boardName))) return repo;
        foreach (var (repo, declared) in _repos)
            if (declared is null && Same(repo, boardName)) return repo;
        return null;
    }

    /// <summary>Pone el color de un subtablero desde Configuración → Tableros, o lo quita con null.</summary>
    /// <returns>true si el subtablero va con un repositorio y el color se ha guardado como el suyo; false si es solo del subtablero.</returns>
    /// <remarks>Con repositorio, el color es de la pareja: se guarda donde lo guarda Sesiones y se quita el que tuviera el subtablero solo.</remarks>
    public static bool SetSubBoard(BoardConnection connection, string boardId, string boardName, string? hex)
    {
        if (_settings is not { } settings || RepoOf(boardId, boardName) is not { } repo) return false;
        if (hex is null) settings.GroupColors.Remove(repo);
        else settings.GroupColors[repo] = hex.ToLowerInvariant();
        connection.SubBoardColors.Remove(boardId);
        _save?.Invoke();
        NotifyChanged();
        return true;
    }

    /// <summary>Los colores que enseña la página de Sesiones: los elegidos y, en los repositorios con subtablero, el que le toca.</summary>
    /// <remarks>Un repositorio sin color elegido ni subtablero no lleva ninguno: la página lo pinta neutro, como siempre.</remarks>
    public static Dictionary<string, string> ForPage()
    {
        var colors = new Dictionary<string, string>(_settings?.GroupColors ?? [], StringComparer.OrdinalIgnoreCase);
        foreach (var repo in _repos.Keys)
            if (!colors.ContainsKey(repo) && LinkedColor(repo) is { } hex) colors[repo] = hex.ToLowerInvariant();
        foreach (var (repo, hex) in _settings?.RepoPanelColors ?? [])
            if (!colors.ContainsKey(repo) && HexColor.ToRgb(hex) is not null) colors[repo] = hex.ToLowerInvariant();
        return colors;
    }

    /// <summary>El color de un repositorio sin el fijo por nombre: el elegido, el de su subtablero o el último que dio
    /// este; null si no tiene.</summary>
    private static string? Hex(string repo) => Chosen(repo) ?? LinkedColor(repo) ?? Remembered(repo);

    /// <summary>El último color que dio el subtablero de un repositorio, guardado para pintarlo al arrancar.</summary>
    private static string? Remembered(string repo) =>
        _settings?.RepoPanelColors.TryGetValue(repo, out var hex) == true && HexColor.ToRgb(hex) is not null ? hex : null;

    /// <summary>Guarda el color del subtablero de cada repositorio, para el próximo arranque; y olvida el de los que lo
    /// han perdido.</summary>
    /// <remarks>
    /// El color se lee del panel por la red unos segundos después de arrancar: hasta entonces, sin lo guardado, un
    /// repositorio sin color elegido salía en gris. Solo cuenta un subtablero ya encontrado: los de un proyecto entero
    /// llegan con esa lectura, y antes de ella un repositorio parece no tener subtablero aunque lo tenga; olvidarlo
    /// entonces lo dejaba en gris en cada arranque.
    /// </remarks>
    private static void Remember()
    {
        if (_settings is not { } settings) return;
        var changed = false;
        foreach (var repo in _repos.Keys)
        {
            if (BoardOf(repo) is not { } board) continue;
            var live = (board.Chosen ?? board.PanelColor)?.ToLowerInvariant();
            var known = settings.RepoPanelColors.TryGetValue(repo, out var k) ? k : null;
            if (live == known) continue;
            if (live is null) settings.RepoPanelColors.Remove(repo);
            else settings.RepoPanelColors[repo] = live;
            changed = true;
        }
        if (changed) _save?.Invoke();
    }

    /// <summary>El color elegido para un repositorio en Sesiones o, desde la 87, en Configuración → Tableros.</summary>
    private static string? Chosen(string repo) =>
        _settings?.GroupColors.TryGetValue(repo, out var hex) == true && HexColor.ToRgb(hex) is not null ? hex : null;

    /// <summary>El color del subtablero de un repositorio: el elegido para el subtablero o el del panel; null sin subtablero.</summary>
    private static string? LinkedColor(string repo) => BoardOf(repo) is { } board ? board.Chosen ?? board.PanelColor : null;

    /// <summary>El subtablero de un repositorio, el que declara o el que se llama igual; null si no está entre los leídos.</summary>
    private static SubBoard? BoardOf(string repo)
    {
        var declared = _repos.TryGetValue(repo, out var d) ? d : null;
        return _boards.FirstOrDefault(b => declared is not null ? Same(declared, b.Id) || Same(declared, b.Name) : Same(repo, b.Name));
    }

    /// <summary>Rehace la lista de subtableros de los tableros elegidos, con sus colores, para no leerla en cada pintado.</summary>
    private static void RebuildBoards()
    {
        var directory = TaskDirectory.Instance;
        var boards = new List<SubBoard>();
        foreach (var option in directory.Boards())
        {
            var connection = option.Connection;
            string? Picked(string id) => connection.SubBoardColors.TryGetValue(id, out var hex) && HexColor.ToRgb(hex) is not null ? hex : null;
            if (option.Selection is { BoardId: { } id, BoardName: var name })
                boards.Add(new SubBoard(id, name ?? id, null, Picked(id)));
            else
                boards.AddRange(directory.SubBoardsOf(option.Id).Select(b =>
                    new SubBoard(b.Id, b.Name, b.Color is { } c && HexColor.ToRgb(c) is not null ? c : null, Picked(b.Id))));
        }
        _boards = boards;
        Remember();
    }

    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
