using System.Windows.Input;

namespace Arkko.Desktop;

/// <summary>Lo que se puede hacer desde una tarjeta o fila de sesión, la misma en la flotante y en el Inicio del panel.</summary>
/// <remarks>
/// Las plantillas compartidas no tienen código propio: cada acción es un comando estático que publica un
/// evento, y lo atiende la ventana flotante, que sabe marcar las sesiones como revisadas y abrir el panel.
/// </remarks>
public static class SessionActions
{
    /// <summary>Se dispara al hacer clic en una tarjeta o fila de sesión.</summary>
    public static event Action<SessionRow>? CardClicked;

    /// <summary>Se dispara al pedir «Ir a la terminal» en el menú de una tarjeta o fila de sesión.</summary>
    public static event Action<SessionRow>? TerminalRequested;

    /// <summary>Se dispara al pulsar el escudo de secreto de una tarjeta o fila de sesión: hay que ir a Seguridad.</summary>
    public static event Action? SecurityRequested;

    /// <summary>Se dispara al pulsar la tarea de una tarjeta de sesión, con su clave: hay que ir a ella en el Tablero.</summary>
    public static event Action<string>? TaskRequested;

    /// <summary>Se dispara al pedir «Ver sesión» en el menú de una tarjeta o fila de sesión.</summary>
    public static event Action<SessionRow>? ViewSessionRequested;

    public static ICommand Card { get; } = new Relay(p => { if (p is SessionRow row) CardClicked?.Invoke(row); });
    public static ICommand Terminal { get; } = new Relay(p => { if (p is SessionRow row) TerminalRequested?.Invoke(row); });
    public static ICommand Shield { get; } = new Relay(_ => SecurityRequested?.Invoke());
    public static ICommand TaskChip { get; } = new Relay(p => { if (p is SessionTaskChip chip) TaskRequested?.Invoke(chip.Key); });
    public static ICommand ViewSession { get; } = new Relay(p => { if (p is SessionRow row) ViewSessionRequested?.Invoke(row); });

    /// <summary>Se dispara al pedir ver una sesión por su id desde una vista clavada en un corcho: hay que ir a ella en Sesiones.</summary>
    public static event Action<string>? SessionIdRequested;

    /// <summary>Se dispara al pedir ver lo publicado desde un repositorio o una carpeta: hay que ir a Artifacts, filtrada.</summary>
    public static event Action<string, string?>? ArtifactsRequested;

    /// <summary>Se dispara al pedir abrir un artifact por su enlace: hay que ir a Artifacts, con él abierto en el visor.</summary>
    public static event Action<string>? ArtifactRequested;

    /// <summary>Se dispara al pedir la configuración de los tableros desde un Tablero clavado en un corcho.</summary>
    public static event Action? BoardsConfigRequested;

    /// <summary>Publica la petición de ir a una tarea desde una vista que no es una tarjeta, como la cabecera de Sesiones.</summary>
    public static void RequestTask(string key) => TaskRequested?.Invoke(key);

    /// <summary>Publica la petición de ver una sesión por su id, desde una vista que no está en el panel.</summary>
    public static void RequestSession(string id) => SessionIdRequested?.Invoke(id);

    /// <summary>Publica la petición de ver lo publicado desde un repositorio, o desde una de sus carpetas.</summary>
    public static void RequestArtifacts(string group, string? project) => ArtifactsRequested?.Invoke(group, project);

    /// <summary>Publica la petición de abrir un artifact en Artifacts, por su enlace.</summary>
    public static void RequestArtifact(string url) => ArtifactRequested?.Invoke(url);

    /// <summary>Publica la petición de abrir la configuración de los tableros, desde una vista que no está en el panel.</summary>
    public static void RequestBoardsConfig() => BoardsConfigRequested?.Invoke();

    /// <summary>Se dispara al pedir abrir una sección del panel desde el widget de un corcho.</summary>
    public static event Action<PanelSection>? SectionRequested;

    /// <summary>Publica la petición de abrir una sección del panel, desde «Abrir en el panel» de un widget.</summary>
    public static void RequestSection(PanelSection section) => SectionRequested?.Invoke(section);
}

/// <summary>Comando que siempre se puede ejecutar y llama a una acción con su parámetro.</summary>
public sealed class Relay(Action<object?> run) : ICommand
{
    // Nunca cambia: siempre se puede ejecutar.
    public event EventHandler? CanExecuteChanged { add { } remove { } }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => run(parameter);
}
