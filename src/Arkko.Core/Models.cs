using System.Text.Json.Serialization;

namespace Arkko.Core;

/// <summary>Estado de una sesión de Claude Code, visto desde fuera.</summary>
public enum SessionStatus
{
    /// <summary>Abierta, sin prompt en curso.</summary>
    Idle,
    /// <summary>Procesando un prompt.</summary>
    Working,
    /// <summary>Espera una respuesta de la persona: permiso, pregunta o diálogo.</summary>
    Waiting,
    /// <summary>Ha terminado el prompt y el control vuelve a la persona.</summary>
    Done,
    /// <summary>Su turno ha acabado, pero le quedan agentes trabajando en segundo plano y retomará sola cuando acaben.</summary>
    /// <remarks>No lo escribe el hook: lo pone la app al leer el transcript, en lugar del Done o el Working que dejó el hook.</remarks>
    Agents,
    /// <summary>Su turno ha acabado, pero espera el resultado de un comando que sigue en marcha en segundo plano: uno que tardó
    /// tanto que Claude Code lo pasó a segundo plano, o uno que dejó así uno de sus agentes.</summary>
    /// <remarks>Como <see cref="Agents"/>, lo pone la app al leer el transcript; si además le quedan agentes, manda Agents.</remarks>
    Command,
}

/// <summary>Estado actual de una sesión, guardado en data/sessions/&lt;id&gt;.json.</summary>
public sealed class SessionState
{
    public string SessionId { get; set; } = "";
    public string? Cwd { get; set; }
    public string? Project { get; set; }

    /// <summary>Repositorio de la carpeta cuando es un worktree; null en una carpeta normal.</summary>
    public string? Repo { get; set; }

    /// <summary>Nombre de la sesión en texto plano: «repo · carpeta» en un worktree, la carpeta si no.</summary>
    [JsonIgnore]
    public string? Title => Repo is not null && Project is not null ? $"{Repo} · {Project}" : Project;

    public string? TranscriptPath { get; set; }
    public string? Model { get; set; }
    public SessionStatus Status { get; set; }
    public string? Detail { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public DateTime StatusSinceUtc { get; set; }
    public int? ClaudePid { get; set; }
    public DateTime? ClaudeStartUtc { get; set; }
    public string? SessionName { get; set; }
    public string? Effort { get; set; }
    public double? ContextPct { get; set; }
    public long? ContextSize { get; set; }
    public double? CostUsd { get; set; }
    public TurnUsage? LastTurn { get; set; }

    /// <summary>Texto del prompt en curso, recortado; se copia al registro del prompt al terminar.</summary>
    public string? LastPrompt { get; set; }

    /// <summary>Último secreto enviado que ha visto el hook en el momento: tipo y origen, nunca el valor.</summary>
    public string? SecretAlert { get; set; }

    /// <summary>Cuándo lo vio; la app lo compara con cuándo se dio por visto en la tarjeta.</summary>
    public DateTime? SecretAlertUtc { get; set; }

    /// <summary>Huellas de los secretos del aviso: si todos acaban resueltos en Seguridad, la tarjeta lo retira.</summary>
    public List<string>? SecretAlertFingerprints { get; set; }

    /// <summary>Por qué fallan los hooks de Arkko en esta sesión, en pocas palabras; null si funcionan.</summary>
    /// <remarks>Lo pone la app al leerlo en el transcript; lo quita el primer hook que vuelve a funcionar.</remarks>
    public string? HookError { get; set; }

    /// <summary>Cuándo falló el último.</summary>
    public DateTime? HookErrorUtc { get; set; }

    /// <summary>Su trabajo en segundo plano, leído del transcript por la app; null si no se ha mirado.</summary>
    [JsonIgnore]
    public SessionBackground? Background { get; set; }

    /// <summary>Los comandos que dejó en marcha en segundo plano.</summary>
    [JsonIgnore]
    public IReadOnlyList<BackgroundCommand> Commands => Background?.Commands ?? [];
}

/// <summary>Tokens consumidos por un modelo.</summary>
public sealed class ModelTokens
{
    public long Input { get; set; }
    public long Output { get; set; }
    public long CacheWrite { get; set; }
    public long CacheRead { get; set; }
    public int ApiCalls { get; set; }

    public long Total => Input + Output + CacheWrite + CacheRead;

    /// <summary>Suma a este acumulado los tokens de otro.</summary>
    public void Add(ModelTokens other)
    {
        Input += other.Input;
        Output += other.Output;
        CacheWrite += other.CacheWrite;
        CacheRead += other.CacheRead;
        ApiCalls += other.ApiCalls;
    }
}

/// <summary>Consumo de un prompt completo, desglosado por modelo.</summary>
public sealed class TurnUsage
{
    public long DurationMs { get; set; }
    public Dictionary<string, ModelTokens> ByModel { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Devuelve la suma de todos los modelos.</summary>
    public ModelTokens Totals()
    {
        var sum = new ModelTokens();
        foreach (var tokens in ByModel.Values) sum.Add(tokens);
        return sum;
    }
}

/// <summary>Un prompt terminado, una línea en data/prompts/&lt;día&gt;.jsonl.</summary>
public sealed class PromptRecord
{
    public DateTime WhenUtc { get; set; }
    public string SessionId { get; set; } = "";
    public string? Project { get; set; }

    /// <summary>Repositorio cuando la carpeta era un worktree; null en una carpeta normal y en los registros anteriores a guardarlo.</summary>
    public string? Repo { get; set; }

    public TurnUsage Usage { get; set; } = new();

    /// <summary>Nombre de la sesión en ese momento, si tenía.</summary>
    public string? SessionName { get; set; }

    /// <summary>Texto del prompt, recortado; null en los registros anteriores a guardarlo.</summary>
    /// <remarks>En un comando «!», el comando; en un aviso, el aviso tal como llegó.</remarks>
    public string? Prompt { get; set; }

    /// <summary>Con qué empezó el turno; Prompt en los registros anteriores a guardarlo.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public PromptKind Kind { get; set; }

    /// <summary>Transcript de la sesión, para ir al detalle del prompt.</summary>
    public string? TranscriptPath { get; set; }

    /// <summary>Con qué empezó el turno, también en los registros de antes de guardarlo: un aviso se reconoce por su texto.</summary>
    /// <remarks>Un comando «!» antiguo no se puede reconocer: se apuntó con el texto del prompt anterior.</remarks>
    [JsonIgnore]
    public PromptKind KindOf => Kind == PromptKind.Prompt ? UserMessage.KindOf(Prompt) : Kind;

    /// <summary>Si el turno lo empezó un prompt de la persona, que es lo que cuentan los recuentos de prompts.</summary>
    [JsonIgnore]
    public bool ByPerson => KindOf == PromptKind.Prompt;

    /// <summary>El texto que se enseña: el prompt, «! comando» o lo que dice el aviso; null si no se guardó.</summary>
    [JsonIgnore]
    public string? Shown => Prompt is null ? null : KindOf switch
    {
        PromptKind.Shell => "! " + Prompt,
        PromptKind.Notification => "Aviso: " + UserMessage.NotificationText(Prompt),
        _ => Prompt,
    };
}

/// <summary>Una foto de los límites cuando cambian, una línea en data/limits-history/&lt;día&gt;.jsonl.</summary>
public sealed class LimitsSample
{
    public DateTime WhenUtc { get; set; }
    public List<LimitInfo> Limits { get; set; } = [];

    /// <summary>Indica si los límites han cambiado respecto a la foto anterior: porcentaje entero o renovación.</summary>
    /// <remarks>
    /// Al porcentaje entero, porque la statusline se pinta muy a menudo y un decimal que baila no es
    /// un cambio que merezca una línea del histórico.
    /// </remarks>
    public static bool Changed(IReadOnlyList<LimitInfo>? before, IReadOnlyList<LimitInfo> now)
    {
        if (before is null || before.Count != now.Count) return true;
        var old = before.ToDictionary(l => l.Key);
        return now.Any(l => !old.TryGetValue(l.Key, out var o)
            || (int)Math.Round(o.UsedPct) != (int)Math.Round(l.UsedPct)
            || o.ResetsAtUtc != l.ResetsAtUtc);
    }
}

/// <summary>Un límite de uso de la cuenta, tal como llega a la statusline.</summary>
public sealed class LimitInfo
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public double UsedPct { get; set; }
    public DateTime? ResetsAtUtc { get; set; }
}

/// <summary>Última foto de los límites de uso, en data/limits.json.</summary>
public sealed class LimitsSnapshot
{
    public DateTime UpdatedUtc { get; set; }
    public List<LimitInfo> Limits { get; set; } = [];
}

/// <summary>Preferencias de la ventana, en data/app-settings.json.</summary>
public sealed class AppSettings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public bool Sound { get; set; } = true;
    public bool Toasts { get; set; } = true;
    public bool Compact { get; set; }
    public bool LightTheme { get; set; }
    public bool LimitsOpen { get; set; } = true;
    public bool ModelsOpen { get; set; } = true;
    public bool SessionsOpen { get; set; } = true;

    /// <summary>«No molestar»: hasta cuándo no hay sonido ni notificaciones; null si no está puesto.</summary>
    public DateTime? QuietUntilUtc { get; set; }

    /// <summary>La ventana se vuelve semitransparente cuando el ratón no está encima.</summary>
    public bool FadeWhenIdle { get; set; }

    /// <summary>Posición de la pastilla de izquierda a derecha en la zona útil de su monitor: 0 pegada a la izquierda, 1 a la derecha.</summary>
    /// <remarks>En proporción y no en píxeles, para que siga en su sitio si cambian la resolución, la escala o el monitor.</remarks>
    public double PillX { get; set; } = 1;

    /// <summary>Posición de la pastilla de arriba abajo en la zona útil de su monitor: 0 pegada arriba, 1 abajo.</summary>
    public double PillY { get; set; } = 1;

    /// <summary>Monitor de la pastilla: null, el de la ventana al minimizar; o la clave de una pantalla concreta.</summary>
    public string? PillScreen { get; set; }

    /// <summary>Diseño propio de la pastilla, por su clave; null, o uno que no se conoce, sigue al de la app.</summary>
    public string? PillDesign { get; set; }

    /// <summary>Diseño de la ventana multiplataforma, por su clave; null, el de cristal que diga <see cref="LightTheme"/>.</summary>
    /// <remarks>La ventana de WPF solo sabe de claro y oscuro: sigue leyendo <see cref="LightTheme"/>, que se guarda a la vez.</remarks>
    public string? Design { get; set; }

    /// <summary>Si la app estaba en el panel del modo maximizado al cerrarse: arranca igual.</summary>
    public bool Panel { get; set; }

    /// <summary>Nombre de quien usa la app, para sus mensajes en la vista de sesión; vacío, sale «User».</summary>
    public string? UserName { get; set; }

    public const string SessionsLive = "live";
    public const string SessionsDiagram = "diagram";
    public const string SessionsNow = "now";
    public const string SessionsParte = "parte";
    public const string SessionsObra = "obra";

    /// <summary>Vista con la que se abre Sesiones: «live», el Flujo (por defecto), «diagram», «now», «parte» u «obra».</summary>
    public string SessionsTab { get; set; } = SessionsLive;

    /// <summary>Vista que toca a un valor guardado: una que ya no existe o no se conoce abre el Flujo.</summary>
    public static string SessionsViewOf(string? value) =>
        value is SessionsDiagram or SessionsNow or SessionsParte or SessionsObra ? value : SessionsLive;

    /// <summary>Si la respuesta final del directo de Sesiones se enseña con el formato de Arkko en lugar de tal cual.</summary>
    public bool FinalReplyFormat { get; set; }

    /// <summary>Color de cada grupo del panel de activas en Sesiones, por nombre de repositorio o carpeta («#rrggbb»).</summary>
    public Dictionary<string, string> GroupColors { get; set; } = [];

    /// <summary>Colores añadidos por la persona al menú de colores de los grupos, detrás de los de serie.</summary>
    public List<string> GroupPalette { get; set; } = [];

    /// <summary>El último color que dio el subtablero de cada repositorio, por su nombre, para pintarlo al arrancar sin
    /// esperar a leer el panel. No es un color elegido: se pone al día con cada lectura.</summary>
    public Dictionary<string, string> RepoPanelColors { get; set; } = [];

    /// <summary>El tema del código elegido para cada lenguaje, por su id; los que no están llevan el de serie.</summary>
    public Dictionary<string, string> CodeThemes { get; set; } = [];

    /// <summary>Colores de serie del menú de los grupos: rojo, naranja, amarillo, verde, azul y morado.</summary>
    public static readonly IReadOnlyList<string> DefaultGroupPalette = ["#e5534b", "#f0883e", "#d4a72c", "#3fb950", "#4493f8", "#a371f7"];

    /// <summary>Días y franja de trabajo, para el filtro de horario laboral de las estadísticas.</summary>
    public WorkSchedule WorkHours { get; set; } = new();

    /// <summary>Si las estadísticas cuentan solo lo hecho en horario laboral.</summary>
    public bool PanelWorkHoursOnly { get; set; }

    public double? PanelLeft { get; set; }
    public double? PanelTop { get; set; }
    public double? PanelWidth { get; set; }
    public double? PanelHeight { get; set; }
    public bool PanelMaximized { get; set; }

    /// <summary>Periodo analizado en el panel: 0 hoy, 1 siete días, 2 treinta días, 3 todo.</summary>
    public int PanelPeriod { get; set; } = 1;

    /// <summary>Si el menú lateral del panel está desplegado o plegado.</summary>
    public bool PanelMenuExpanded { get; set; } = true;

    /// <summary>Al plegarlo, el menú desaparece del todo en lugar de quedarse en un riel de iconos.</summary>
    public bool PanelMenuHides { get; set; }

    /// <summary>Si el icono de la app es la tecla clara; si no, la oscura.</summary>
    public bool AppIconLight { get; set; }

    /// <summary>Categoría abierta en la configuración del panel.</summary>
    public int PanelConfigCategory { get; set; }

    /// <summary>Plazos de conservación del histórico, de su texto y de los límites.</summary>
    public RetentionSettings Retention { get; set; } = new();

    /// <summary>Última limpieza de la carpeta de datos y lo que hizo, para la ficha de datos.</summary>
    public DateTime? LastCleanupUtc { get; set; }
    public string? LastCleanupText { get; set; }

    /// <summary>Porcentajes de uso de los límites a partir de los que se avisa.</summary>
    public List<int> LimitThresholds { get; set; } = [80, 95];

    /// <summary>Inicio del último análisis de secretos: el siguiente solo mira los transcripts escritos desde entonces.</summary>
    public DateTime? SecurityScanUtc { get; set; }

    /// <summary>Inicio del último análisis de todo el historial; null si nunca se ha hecho.</summary>
    public DateTime? SecurityFullScanUtc { get; set; }

    /// <summary>Avisar en el momento de un secreto enviado: línea en la sesión, notificación y tarjeta marcada.</summary>
    public bool SecretAlerts { get; set; } = true;

    /// <summary>Frenar un prompt que lleva un secreto antes de que salga. Desactivado por defecto.</summary>
    public bool BlockSecretPrompts { get; set; }

    /// <summary>Dejar que Arkko lance solo la comprobación de vulnerabilidades de arkko-stack cuando el informe se queda viejo.</summary>
    /// <remarks>Apagado de serie: esa comprobación sale a internet, y en un PC de empresa eso se decide a conciencia.
    /// Los botones de la sección funcionan igual con esto apagado, porque los pulsa la persona.</remarks>
    public bool StackAutoCheck { get; set; }

    /// <summary>Avisar con una notificación de lo nuevo en las vulnerabilidades: una FALLA, un aviso, el informe viejo o una
    /// comprobación que no termina.</summary>
    public bool StackAlerts { get; set; } = true;

    /// <summary>Cómo se agrupa en Vulnerabilidades lo que hay que actualizar: «where» (equipo y cada repo) o «kind» (por tipo).</summary>
    public string VulnerabilitiesGroup { get; set; } = "where";
}
