using Avalonia.Controls;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Las vistas de la app que se clavan en un corcho como un panel más: Inicio, Relojes, el Tablero, el Diario,
/// Worktrees, Artifacts y Estadísticas; y el Navegador y los widgets en directo, que solo viven en el corcho.</summary>
/// <remarks>
/// Clavada, cada una es su widget (<see cref="CorkWidget"/>): una versión pensada para un recuadro, con los mismos datos que
/// la sección del panel y lo elegido en ella guardado aparte, con el corcho. Lo que piden («ver sesión», «ver tarea»,
/// «Abrir en el panel») lo atiende la ventana principal, que abre el panel en esa sección.
/// </remarks>
internal static class NativeViews
{
    private static Func<object?> _home = () => null;
    private static Func<AppSettings> _settings = () => new AppSettings();

    /// <summary>Recibe el modelo de la ventana flotante, que es el de Inicio clavado, y los ajustes de la app, con los que
    /// empiezan los widgets (el periodo y el horario de Estadísticas).</summary>
    public static void Init(Func<object?> home, Func<AppSettings> settings) => (_home, _settings) = (home, settings);

    /// <summary>El modelo de Inicio, de donde salen las filas de las sesiones; null antes de arrancar.</summary>
    public static object? Home() => _home();

    /// <summary>Los ajustes de la app.</summary>
    public static AppSettings Settings() => _settings();

    /// <summary>Si una clase de panel es una vista de la app y no una parte de una sesión.</summary>
    public static bool Is(string kind) => kind is "home" or "board" or "diary" or "worktrees" or "artifacts" or "stats" or "browser" or "clocks" or "tetoca" or "pulso" or "todas" or "avisos" or "carril";

    /// <summary>Cómo se llama cada vista.</summary>
    public static string NameOf(string kind) => kind switch
    {
        "home" => "Inicio",
        "board" => "Tablero",
        "diary" => "Diario",
        "worktrees" => "Worktrees",
        "artifacts" => "Artifacts",
        "stats" => "Estadísticas",
        "browser" => "Navegador",
        "clocks" => "Relojes",
        "tetoca" => "Te toca",
        "pulso" => "Pulso",
        "todas" => "Ahora, de todas",
        "avisos" => "Avisos",
        "carril" => "Carril de sesión",
        _ => kind,
    };

    /// <summary>Lo que hay que pasar para abrir una vista: su clase y su nombre, sin sesión.</summary>
    public static PieceSpec Spec(string kind) => new(kind, "", NameOf(kind));

    /// <summary>Crea el widget de una vista clavada.</summary>
    public static Control Make(string kind) => kind switch
    {
        "home" => new HomeWidget(_home()),
        "board" => new BoardWidget(),
        "diary" => new DiaryWidget(),
        "worktrees" => new WorktreesWidget(),
        "artifacts" => new ArtifactsWidget(),
        "stats" => new StatsWidget(),
        "browser" => new BrowserWidget(),
        "clocks" => new ClocksWidget(_home()),
        "tetoca" or "pulso" or "todas" or "avisos" or "carril" => new LiveWidget(kind, _home()),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No es una vista de la app."),
    };

    /// <summary>Al verse: el widget empieza a leer y a ponerse al día, como al entrar en su sección del panel.</summary>
    public static void Activate(Control view)
    {
        if (view is CorkWidget widget) widget.Activate();
    }

    /// <summary>Al dejar de verse: el widget deja de releer.</summary>
    public static void Deactivate(Control view)
    {
        if (view is CorkWidget widget) widget.Deactivate();
    }
}
