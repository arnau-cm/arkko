using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Una fila de límite de uso, con la previsión al ritmo actual si la hay.</summary>
/// <param name="Projected">Porcentaje previsto al renovarse (100 si se agota antes); 0 sin previsión.</param>
/// <param name="ResetText">Renovación para la ficha: «en 3d 5h · dom 4 oct, 14:00».</param>
/// <param name="ForecastText">Previsión para la ficha, o el aviso de que aún no hay base para darla.</param>
public sealed record LimitRow(string Label, double Pct, string PctText, string Reset, IBrush Brush,
    double Projected, IBrush GhostBrush, string ResetText, string ForecastText, IBrush ForecastBrush)
{
    public string TipTitle => $"Límite de {Label}";

    /// <summary>Cuándo se renueva, para dibujar su ventana de tiempo; null sin dato.</summary>
    public DateTime? ResetsAtUtc { get; init; }
}

/// <summary>Una fila de consumo diario por modelo: tokens nuevos y, aparte, la lectura de caché.</summary>
public sealed record ModelRow(string Name, string Total, string Detail, FontWeight Weight, ModelTip? Tip)
{
    public bool HasTip => Tip is not null;

    /// <summary>Color del modelo, el de las gráficas: el punto, y el mismo color tenue de fondo a escala del primero.</summary>
    public IBrush Brush { get; init; } = Brushes.Transparent;
    public IBrush Fill { get; init; } = Brushes.Transparent;

    /// <summary>Tokens nuevos de este modelo y del que más gasta hoy, para el fondo de la fila.</summary>
    public double Value { get; init; }
    public double Max { get; init; }

    /// <summary>Parte del día de este modelo: «62 %».</summary>
    public string Pct { get; init; } = "";

    /// <summary>Parte del fondo de la fila que se rellena: este modelo frente al que más gasta, de 0 a 1.</summary>
    public double Fraction => Max > 0 ? Math.Clamp(Value / Max, 0, 1) : 0;
}

/// <summary>Desglose de los tokens de hoy de un modelo, ya formateado, para su ficha.</summary>
public sealed record ModelTip(string Title, string New, string Input, string Output, string CacheWrite, string CacheRead, string Calls);

/// <summary>Lista de chips de tarea que se compara por su contenido, no por referencia.</summary>
public sealed class ChipList(IList<SessionTaskChip> chips) : System.Collections.ObjectModel.ReadOnlyCollection<SessionTaskChip>(chips), IEquatable<ChipList>
{
    public bool Equals(ChipList? other) => other is not null && this.SequenceEqual(other);
    public override bool Equals(object? obj) => obj is ChipList other && Equals(other);
    public override int GetHashCode() => this.Aggregate(Count, (hash, chip) => HashCode.Combine(hash, chip));
}

/// <summary>Una tarea de una sesión como chip de su tarjeta: su clave, su título en el tooltip y si es la principal.</summary>
/// <remarks>En el Inicio solo se ven: las tareas de una sesión se añaden y quitan en Tareas y en Sesiones.</remarks>
public sealed record SessionTaskChip(string SessionId, string Key, string Tip, bool IsPrincipal)
{
    /// <summary>Los chips de las tareas de una sesión, con la principal delante.</summary>
    public static ChipList For(SessionTasks tasks)
    {
        var directory = TaskDirectory.Instance;
        return new ChipList(tasks.Tasks
            .Select(k => new SessionTaskChip(tasks.SessionId, k, TipOf(directory, k, k == tasks.Principal), k == tasks.Principal))
            .ToList());
    }

    private static string TipOf(TaskDirectory directory, string key, bool principal) =>
        $"{directory.LineOf(key)}\n{(principal ? "Tarea principal: se lleva el coste de la sesión" : "También trabaja en ella")}. Pulsa para verla en el Tablero.";
}

/// <summary>Una fila de sesión activa.</summary>
public sealed record SessionRow(
    string SessionId,
    string Title,
    string Subtitle,
    string Model,
    string Status,
    string? Detail,
    string Since,
    IBrush Brush,
    double? ContextPct,
    string ContextTip,
    IBrush ContextBrush,
    int EffortLevel,
    string EffortText,
    int WaitLevel,
    bool Working,
    IBrush StatusTint,
    bool Waiting,
    int ReviewLevel,
    string? Recap,
    string RecapAgo)
{
    public bool RecapVisible => !string.IsNullOrEmpty(Recap);

    /// <summary>Terminada y sin marcar como vista; nivel 2 si además lleva rato así.</summary>
    public bool NeedsReview => ReviewLevel > 0;
    public bool ReviewOverdue => ReviewLevel == 2;

    /// <summary>Desde cuándo está en su estado actual, para ordenar el tablero por lo más reciente.</summary>
    public DateTime SinceUtc { get; init; }

    /// <summary>Clave de la tarea principal de la sesión, la que se lleva su coste; null si no tiene ninguna.</summary>
    public string? TaskKey { get; init; }

    /// <summary>Sus tareas, como chips de la tarjeta.</summary>
    /// <remarks>
    /// Es una <see cref="ChipList"/>, que compara por contenido: la fila es un record y el Inicio solo repinta
    /// si cambia, así que una lista que comparase por referencia rehacía todas las tarjetas en cada refresco.
    /// </remarks>
    public ChipList TaskChips { get; init; } = new([]);

    public bool TaskLineVisible => TaskChips.Count > 0;

    /// <summary>Título de la tarea principal, bajo sus chips; null si no tiene o aún no se ha leído.</summary>
    public string? TaskTitle { get; init; }

    public bool TaskTitleVisible => !string.IsNullOrEmpty(TaskTitle);

    /// <summary>Carpeta del worktree cuando la sesión está en uno; el título es entonces el repositorio.</summary>
    public string? Worktree { get; init; }
    public string WorktreeSeparator => Worktree is null ? "" : "  ·  ";

    /// <summary>Secreto enviado en esta sesión que aún no se ha visto: tipo y origen; null si no hay o ya se vio.</summary>
    public string? SecretAlert { get; init; }
    public bool SecretAlertVisible => SecretAlert is not null;
    public string SecretAlertTip => $"Secreto sin revisar: {SecretAlert}. Pulsa el escudo para ir a Seguridad; se quita al rotarlo o descartarlo allí.";

    /// <summary>Por qué falló el último hook de Arkko de esta sesión, con la hora; null si funcionó.</summary>
    public string? HookError { get; init; }
    public bool HookErrorVisible => HookError is not null;
    public string HookErrorTip => $"El último hook de Arkko de esta sesión falló: {HookError}. Hasta que vuelva a funcionar, lo que pase en ella no llega a la app; los turnos que terminen se cierran al leerlos en el transcript.";

    /// <summary>Si su turno acabó y espera a sus agentes en segundo plano: no ha terminado y no te pide nada.</summary>
    public bool Agents { get; init; }

    /// <summary>Si su turno acabó y espera el resultado de un comando en marcha en segundo plano: no ha terminado.</summary>
    public bool Command { get; init; }

    /// <summary>Si la gota es la de siempre: ni espera a sus agentes ni a un comando, que llevan su propia marca.</summary>
    public bool PlainDot => !Agents && !Command;

    /// <summary>Los agentes que espera, y los que ya acabaron desde que terminó su turno.</summary>
    public AgentLineList AgentLines { get; init; } = new([]);
    public bool AgentLinesVisible => AgentLines.Count > 0;

    /// <summary>Lo que dejó en marcha en segundo plano, en una línea: «1 comando en marcha · npm run dev», o «Espera a 1 comando ·
    /// …» si espera su resultado; null si nada.</summary>
    public string? CommandText { get; init; }
    public bool CommandVisible => CommandText is not null;

    /// <summary>Todos los comandos en marcha, uno por línea, para la ficha.</summary>
    public string? CommandTip { get; init; }

    /// <summary>El punto de lo que sigue en marcha: el cian de la espera a un comando, o el verde de lo que funciona.</summary>
    public IBrush CommandDot => Command ? Brushes_.Command : Brushes_.Done;

    /// <summary>Columna del tablero de inicio: 0 en reposo, 1 trabajando o esperando a sus agentes o a un comando, 2 te espera
    /// (pregunta o sin revisar).</summary>
    public int BoardColumn => Waiting || NeedsReview ? 2 : Working || Agents || Command ? 1 : 0;

    /// <summary>Borde de la tarjeta según lo que pide: respuesta, revisión o nada (trabajando).</summary>
    /// <remarks>La que espera a sus agentes lleva un borde de rayas, que pinta la plantilla encima.</remarks>
    public IBrush? CardBorder =>
        Waiting ? (WaitLevel == 2 ? Brushes_.WaitHighEdge : Brushes_.WaitWarnEdge)
        : NeedsReview ? Brushes_.DoneEdge
        : Working ? Brushes_.WorkingEdge
        : Command ? Brushes_.CommandEdge
        : null;

    /// <summary>Fondo de la tarjeta: ámbar o rojo si espera respuesta, verde si falta revisarla, azul si trabaja, violeta si
    /// espera a sus agentes y cian si espera a un comando.</summary>
    public IBrush? CardBackground =>
        Waiting ? (WaitLevel == 2 ? Brushes_.HighCardFill : Brushes_.WaitingCardFill)
        : ReviewOverdue ? Brushes_.DoneCardFill
        : Working ? Brushes_.WorkingFill
        : Agents ? Brushes_.AgentsCardFill
        : Command ? Brushes_.CommandCardFill
        : null;

    /// <summary>El borde de rayas de la que espera a sus agentes.</summary>
    public IBrush AgentsEdge => Brushes_.AgentsEdge;

    public bool HasCardBackground => CardBackground is not null;
    public bool ReviewVisible => NeedsReview;

    /// <summary>Color del nivel de esfuerzo, de verde (bajo) a rojo (máximo).</summary>
    public IBrush EffortBrush => EffortLevel switch
    {
        1 => Brushes_.EffortLow,
        2 => Brushes_.EffortMedium,
        3 => Brushes_.EffortHigh,
        4 => Brushes_.EffortXHigh,
        5 => Brushes_.EffortMax,
        _ => Brushes_.EffortOff,
    };

    public IBrush? SinceBrush => WaitLevel switch { 2 => Brushes_.High, 1 => Brushes_.Waiting, _ => null };
    public FontWeight SinceWeight => WaitLevel > 0 ? FontWeight.Bold : FontWeight.Normal;
    public bool HasAccentBorder => CardBorder is not null;
    public bool Waited => WaitLevel > 0;
    public string DropletTip => $"{Title}: {Status}";

    /// <summary>Color de la gota: el del estado, o rojo si lleva rato terminada sin revisar.</summary>
    public IBrush DropletBrush => ReviewOverdue ? Brushes_.High : Brush;

    public bool SubtitleVisible => !string.IsNullOrEmpty(Subtitle);
    public bool DetailVisible => !string.IsNullOrEmpty(Detail);
    public bool ContextVisible => ContextPct is not null;
    public string ContextText => ContextPct is { } pct ? ((int)Math.Round(pct)).ToString() : "";
    public bool EffortVisible => EffortLevel != 0;

    /// <summary>Las cinco barras del indicador de esfuerzo, encendidas hasta el nivel de la sesión y cada una más alta.</summary>
    public IReadOnlyList<EffortBar> EffortBars =>
        Enumerable.Range(1, 5).Select(i => new EffortBar(i <= EffortLevel ? EffortBrush : Brushes_.EffortOff, 2 + 2 * i)).ToList();
}

/// <summary>Un agente en la tarjeta de una sesión que lo espera: qué hace, cuánto lleva o si ya acabó.</summary>
public sealed record AgentLine(string Description, string Time, bool Running)
{
    public bool Finished => !Running;
    public IBrush DotBrush => Running ? Brushes_.Agents : Brushes_.Done;
}

/// <summary>Lista de agentes que se compara por su contenido, no por referencia, para no repintar la tarjeta en cada refresco.</summary>
public sealed class AgentLineList(IList<AgentLine> lines) : System.Collections.ObjectModel.ReadOnlyCollection<AgentLine>(lines), IEquatable<AgentLineList>
{
    public bool Equals(AgentLineList? other) => other is not null && this.SequenceEqual(other);
    public override bool Equals(object? obj) => obj is AgentLineList other && Equals(other);
    public override int GetHashCode() => this.Aggregate(Count, (hash, line) => HashCode.Combine(hash, line));

    /// <summary>Las líneas de los agentes de una espera: los que trabajan con lo que llevan, y los que acabaron.</summary>
    public static AgentLineList Of(SessionState s) => new(s.Status == SessionStatus.Agents && s.Background is { } bg
        ? bg.Agents.Select(a => new AgentLine(a.Description, a.Running ? Format.Ago(a.StartUtc) : "acabó", a.Running)).ToList()
        : []);
}

/// <summary>Una barra del indicador de esfuerzo: su color y su alto.</summary>
public sealed record EffortBar(IBrush Brush, double Height);

/// <summary>Traducción de los niveles de esfuerzo de Claude Code.</summary>
public static class EffortLevels
{
    /// <summary>Nivel de 1 a 5 y nombre en castellano; 0 si no se conoce.</summary>
    public static (int Level, string Text) Of(string? effort) => effort?.ToLowerInvariant() switch
    {
        "low" => (1, "bajo"),
        "medium" => (2, "medio"),
        "high" => (3, "alto"),
        "xhigh" => (4, "muy alto"),
        "max" => (5, "máximo"),
        null or "" => (0, ""),
        var other => (0, other),
    };
}

/// <summary>Datos que pinta la ventana; se reemplazan enteros en cada refresco.</summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private IReadOnlyList<LimitRow> _limits = [];
    private IReadOnlyList<ModelRow> _models = [];
    private IReadOnlyList<SessionRow> _sessions = [];
    private string _limitsNote = "";
    private string? _hookProblem;
    private string _modelsTotal = "";
    private string _modelsDetail = "";
    private IReadOnlyList<BarSegment> _modelShares = [];
    private string _sessionsTitle = "SESIONES";
    private bool _sound = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<LimitRow> Limits
    {
        get => _limits;
        set
        {
            if (SetList(ref _limits, value)) OnPropertyChanged(nameof(MiniLimits));
        }
    }

    /// <summary>Límites que se ven en la pastilla del minimizado: solo el de 5 h.</summary>
    public IReadOnlyList<LimitRow> MiniLimits => _limits.Where(l => l.Label == "5 h").ToList();
    public IReadOnlyList<ModelRow> Models { get => _models; set => SetList(ref _models, value); }

    /// <summary>Tokens nuevos de hoy entre todos los modelos, la cifra grande de «Hoy por modelo».</summary>
    public string ModelsTotal
    {
        get => _modelsTotal;
        set
        {
            if (Set(ref _modelsTotal, value)) OnPropertyChanged(nameof(ModelsSummaryVisible));
        }
    }

    /// <summary>Caché y llamadas de hoy entre todos los modelos, junto a la cifra grande.</summary>
    public string ModelsDetail { get => _modelsDetail; set => Set(ref _modelsDetail, value); }

    /// <summary>Reparto de hoy entre los modelos, para la barra bajo la cifra grande.</summary>
    public IReadOnlyList<BarSegment> ModelShares { get => _modelShares; set => SetList(ref _modelShares, value); }

    public bool ModelsSummaryVisible => _modelsTotal.Length > 0;
    public IReadOnlyList<SessionRow> Sessions
    {
        get => _sessions;
        set
        {
            if (!SetList(ref _sessions, value)) return;
            OnPropertyChanged(nameof(NoSessionsVisible));
            OnPropertyChanged(nameof(RestingCount));
            OnPropertyChanged(nameof(WorkingCount));
            OnPropertyChanged(nameof(WorkingAgentsNote));
            OnPropertyChanged(nameof(WorkingAgentsVisible));
            OnPropertyChanged(nameof(WaitingCount));
            OnPropertyChanged(nameof(RestingSessions));
            OnPropertyChanged(nameof(WorkingSessions));
            OnPropertyChanged(nameof(WaitingSessions));
            SessionPage = _sessionPage;
        }
    }

    // Recuento de cada columna del tablero de inicio.
    public int RestingCount => _sessions.Count(s => s.BoardColumn == 0);
    public int WorkingCount => _sessions.Count(s => s.BoardColumn == 1);

    /// <summary>Cuántas de las que trabajan esperan en realidad a sus agentes, junto al título de su columna.</summary>
    public string WorkingAgentsNote => (_sessions.Count(s => s.Agents), _sessions.Count(s => s.Command)) switch
    {
        (0, 0) => "",
        (var a, 0) => $"· {a} esperando a sus agentes",
        (0, var c) => $"· {c} esperando a un comando",
        var (a, c) => $"· {a} esperando a sus agentes y {c} a un comando",
    };
    public bool WorkingAgentsVisible => WorkingAgentsNote.Length > 0;
    public int WaitingCount => _sessions.Count(s => s.BoardColumn == 2);

    private int _securityPending;

    /// <summary>Secretos pendientes de cambiar; con alguno, la barra enseña el escudo con su número.</summary>
    public int SecurityPending
    {
        get => _securityPending;
        set
        {
            if (!Set(ref _securityPending, value)) return;
            OnPropertyChanged(nameof(SecurityBadgeVisible));
            OnPropertyChanged(nameof(SecurityBadgeText));
            OnPropertyChanged(nameof(SecurityBadgeTip));
        }
    }

    public bool SecurityBadgeVisible => _securityPending > 0;

    // El contador es pequeño: de 10 en adelante, «9+».
    public string SecurityBadgeText => _securityPending > 9 ? "9+" : _securityPending.ToString();
    public string SecurityBadgeTip => (_securityPending == 1 ? "1 secreto pendiente de cambiar" : $"{_securityPending} secretos pendientes de cambiar") +
                                      ": abre Seguridad en el panel";

    private (int Failures, int Unseen) _vulnerabilities;

    /// <summary>Lo que hay que actualizar (comprobaciones en FALLA) y los avisos de seguridad por revisar; con alguno, la barra
    /// enseña su icono.</summary>
    public (int Failures, int Unseen) Vulnerabilities
    {
        get => _vulnerabilities;
        set
        {
            if (_vulnerabilities == value) return;
            _vulnerabilities = value;
            foreach (var name in new[] { nameof(VulnerabilityBadgeVisible), nameof(VulnerabilityFailuresVisible), nameof(VulnerabilityFailuresText),
                         nameof(VulnerabilityUnseenVisible), nameof(VulnerabilityUnseenText), nameof(VulnerabilityBadgeTip), nameof(VulnerabilityOnlyUnseen) })
                OnPropertyChanged(name);
        }
    }

    public bool VulnerabilityBadgeVisible => _vulnerabilities.Failures + _vulnerabilities.Unseen > 0;
    public bool VulnerabilityFailuresVisible => _vulnerabilities.Failures > 0;
    public bool VulnerabilityUnseenVisible => _vulnerabilities.Unseen > 0;
    public string VulnerabilityFailuresText => _vulnerabilities.Failures > 9 ? "9+" : _vulnerabilities.Failures.ToString();
    public string VulnerabilityUnseenText => _vulnerabilities.Unseen > 9 ? "9+" : _vulnerabilities.Unseen.ToString();

    /// <summary>Si solo hay avisos sin ver, sin nada que falle: el icono va en ámbar en lugar de rojo.</summary>
    public bool VulnerabilityOnlyUnseen => _vulnerabilities.Failures == 0 && _vulnerabilities.Unseen > 0;

    public string VulnerabilityBadgeTip => string.Join(" · ", new[]
    {
        _vulnerabilities.Failures switch { 0 => null, var n => $"{n} por actualizar" },
        _vulnerabilities.Unseen switch { 0 => null, 1 => "1 aviso de seguridad por revisar", var n => $"{n} avisos de seguridad por revisar" },
    }.Where(p => p is not null)) + ": abre Vulnerabilidades en el panel";

    // Punto de cada columna del tablero: el color de su estado en el tema activo, el mismo que la pastilla
    // de las tarjetas que contiene.
    public IBrush RestingDot => Brushes_.Idle;
    public IBrush WorkingDot => Brushes_.Working;
    public IBrush WaitingDot => Brushes_.Waiting;

    /// <summary>Repinta lo que toma su color de la paleta de estados; se llama después de cambiar de tema.</summary>
    public void ThemeApplied()
    {
        OnPropertyChanged(nameof(RestingDot));
        OnPropertyChanged(nameof(WorkingDot));
        OnPropertyChanged(nameof(WaitingDot));
    }

    // Sesiones de cada columna del tablero, la más reciente arriba. En «te esperan», primero las que preguntan
    // (o piden permiso o plan) y después las terminadas sin revisar, cada grupo por lo más reciente.
    public IReadOnlyList<SessionRow> RestingSessions => _sessions.Where(s => s.BoardColumn == 0).OrderByDescending(s => s.SinceUtc).ToList();
    public IReadOnlyList<SessionRow> WorkingSessions => _sessions.Where(s => s.BoardColumn == 1).OrderByDescending(s => s.SinceUtc).ToList();
    public IReadOnlyList<SessionRow> WaitingSessions => _sessions.Where(s => s.BoardColumn == 2)
        .OrderBy(s => s.Waiting ? 0 : 1).ThenByDescending(s => s.SinceUtc).ToList();

    public bool NoSessionsVisible => _sessions.Count == 0;

    private const int PageSize = 3;
    private int _sessionPage;

    /// <summary>Página visible de sesiones, de 3 en 3; la comparten la vista normal y la compacta.</summary>
    public int SessionPage
    {
        get => _sessionPage;
        set
        {
            _sessionPage = Math.Clamp(value, 0, PageCount - 1);
            OnPropertyChanged(nameof(SessionPage));
            OnPropertyChanged(nameof(PageSessions));
            OnPropertyChanged(nameof(PageText));
            OnPropertyChanged(nameof(PagerVisible));
        }
    }

    public int PageCount => Math.Max(1, (int)Math.Ceiling(_sessions.Count / (double)PageSize));
    public IReadOnlyList<SessionRow> PageSessions => _sessions.Skip(_sessionPage * PageSize).Take(PageSize).ToList();
    public string PageText => $"{_sessionPage + 1} / {PageCount}";
    public bool PagerVisible => _sessions.Count > PageSize;
    public string LimitsNote
    {
        get => _limitsNote;
        set
        {
            if (Set(ref _limitsNote, value)) OnPropertyChanged(nameof(LimitsNoteVisible));
        }
    }

    /// <summary>La nota de los límites no ocupa espacio cuando no hay nada que decir.</summary>
    public bool LimitsNoteVisible => !string.IsNullOrEmpty(_limitsNote);

    /// <summary>Qué falla con los hooks de Arkko y qué hacer, en una línea; null si funcionan.</summary>
    public string? HookProblem
    {
        get => _hookProblem;
        set
        {
            if (Set(ref _hookProblem, value)) OnPropertyChanged(nameof(HookProblemVisible));
        }
    }

    public bool HookProblemVisible => _hookProblem is not null;
    public string SessionsTitle { get => _sessionsTitle; set => Set(ref _sessionsTitle, value); }

    private bool _limitsOpen = true;
    private bool _modelsOpen = true;
    private bool _sessionsOpen = true;
    private bool _compact;

    public bool LimitsOpen
    {
        get => _limitsOpen;
        set
        {
            if (Set(ref _limitsOpen, value)) OnPropertyChanged(nameof(LimitsSummaryVisible));
        }
    }

    /// <summary>Con los límites plegados, su cabecera muestra las barras en pequeño.</summary>
    public bool LimitsSummaryVisible => !_limitsOpen;
    public bool ModelsOpen { get => _modelsOpen; set => Set(ref _modelsOpen, value); }
    public bool SessionsOpen { get => _sessionsOpen; set => Set(ref _sessionsOpen, value); }

    public bool Compact
    {
        get => _compact;
        set
        {
            if (!Set(ref _compact, value)) return;
            OnPropertyChanged(nameof(FullVisible));
            OnPropertyChanged(nameof(CompactVisible));
        }
    }

    private bool _mini;
    private int _attention;

    /// <summary>Modo minimizado: la ventana es una pastilla en la esquina con una gota por sesión.</summary>
    public bool Mini
    {
        get => _mini;
        set
        {
            if (!Set(ref _mini, value)) return;
            OnPropertyChanged(nameof(FullVisible));
            OnPropertyChanged(nameof(CompactVisible));
        }
    }

    /// <summary>Sesiones que requieren atención, para el contador de la pastilla.</summary>
    public int AttentionCount
    {
        get => _attention;
        set
        {
            if (!Set(ref _attention, value)) return;
            OnPropertyChanged(nameof(AttentionVisible));
            OnPropertyChanged(nameof(AttentionText));
        }
    }

    /// <summary>El contador de la pastilla.</summary>
    public string AttentionText => _attention.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public bool AttentionVisible => _attention > 0;

    private string _design = PillDesigns.DefaultKey;
    private IReadOnlyList<AppDesignOption>? _appDesignOptions;

    /// <summary>Diseño de toda la app, por su clave: uno de <see cref="PillDesigns.All"/>.</summary>
    /// <remarks>La pastilla lo sigue, salvo que tenga uno propio: ver <see cref="PillLook"/>.</remarks>
    public string Design
    {
        get => _design;
        set
        {
            if (!Set(ref _design, PillDesigns.Find(value).Key)) return;
            OnPropertyChanged(nameof(Light));
            foreach (var option in AppDesignOptions) option.Refresh();
            foreach (var option in PillDesignOptions) option.Refresh();
            OnPropertyChanged(nameof(PillLook));
            PillChanged?.Invoke();
        }
    }

    /// <summary>Si el diseño de la app es claro.</summary>
    public bool Light => _design == "glass-light";

    /// <summary>Los diseños de la app que se pueden elegir, con el elegido marcado.</summary>
    public IReadOnlyList<AppDesignOption> AppDesignOptions =>
        _appDesignOptions ??= PillDesigns.All.Select(d => new AppDesignOption(d, this)).ToList();

    /// <summary>Inverso de Sound, para el chip de avisos del menú.</summary>
    public bool Muted { get => !_sound; set => Sound = !value; }

    public string MuteText => _sound ? "Sonido y notificaciones" : "Silenciados";

    private DateTime? _quietUntil;

    /// <summary>Fin del «no molestar», en hora local; null si no está puesto.</summary>
    public DateTime? QuietUntil
    {
        get => _quietUntil;
        set
        {
            if (!Set(ref _quietUntil, value)) return;
            OnPropertyChanged(nameof(QuietText));
            OnPropertyChanged(nameof(QuietOnVisible));
            OnPropertyChanged(nameof(QuietOffVisible));
        }
    }

    /// <summary>Si ahora mismo hay que avisar: sonido activado y sin «no molestar» en curso.</summary>
    public bool AlertsOn => _sound && !(_quietUntil > DateTime.Now);

    /// <summary>Hasta cuándo dura el «no molestar»: «Sin avisos hasta las 15:30» o «hasta mañana a las 08:00».</summary>
    public string QuietText => _quietUntil is not { } until ? ""
        : until.Date == DateTime.Today ? $"Sin avisos hasta las {until:HH:mm}"
        : $"Sin avisos hasta mañana a las {until:HH:mm}";

    /// <summary>Tooltip de los botones de «no molestar»: por qué están desactivados, si lo están.</summary>
    public string? QuietTip => _sound ? null : "Los avisos ya están silenciados";

    public bool QuietOnVisible => _quietUntil is not null;
    public bool QuietOffVisible => _quietUntil is null;

    private bool _fadeWhenIdle;

    /// <summary>Semitransparente sin el ratón encima.</summary>
    public bool FadeWhenIdle { get => _fadeWhenIdle; set => Set(ref _fadeWhenIdle, value); }

    private double _pillX = 1;
    private double _pillY = 1;
    private string? _pillScreen;
    private IReadOnlyList<string> _screenKeys = [];

    /// <summary>Posición de la pastilla en proporción a la zona útil, con los valores de <see cref="AppSettings.PillX"/> y <see cref="AppSettings.PillY"/>.</summary>
    public (double X, double Y) PillSpot => (_pillX, _pillY);

    /// <summary>Monitor de la pastilla, con los valores de <see cref="AppSettings.PillScreen"/>: null, el de la ventana.</summary>
    public string? PillScreen => _pillScreen;

    /// <summary>Tamaño de la pastilla la última vez que se puso, en unidades de la interfaz, para dibujarla a escala.</summary>
    public Avalonia.Size PillSize { get; set; } = new(150, 38);

    /// <summary>Se dispara al cambiar el sitio de la pastilla: su posición o su monitor.</summary>
    public event Action? PillChanged;

    /// <summary>Pone la pastilla en un sitio: su posición en proporción y su monitor (null, el de la ventana).</summary>
    public void SetPill(double x, double y, string? screen)
    {
        if (_pillX == x && _pillY == y && _pillScreen == screen) return;
        (_pillX, _pillY, _pillScreen) = (x, y, screen);
        OnPillChanged();
    }

    /// <summary>Vuelve al sitio de serie: abajo a la derecha del monitor de la ventana.</summary>
    public void ResetPill() => SetPill(1, 1, null);

    private string? _pillDesignKey;
    private IReadOnlyList<PillDesignOption>? _pillDesignOptions;

    /// <summary>Diseño propio de la pastilla, por su clave; null si sigue al de la app.</summary>
    public string? PillDesignKey => _pillDesignKey;

    /// <summary>Diseño con el que se ve la pastilla: el suyo propio o, si no tiene, el de la app.</summary>
    public PillDesign PillLook => PillDesigns.Find(_pillDesignKey ?? _design);

    /// <summary>Las opciones de la pastilla: «Como la app» y cada diseño, con la elegida marcada.</summary>
    public IReadOnlyList<PillDesignOption> PillDesignOptions =>
        _pillDesignOptions ??= new[] { (string?)null }.Concat(PillDesigns.All.Select(d => (string?)d.Key))
            .Select(key => new PillDesignOption(key, this)).ToList();

    /// <summary>Elige el diseño propio de la pastilla por su clave; null, o una que no se conoce, la deja como la app.</summary>
    public void SetPillDesign(string? key)
    {
        key = PillDesigns.All.Any(d => d.Key == key) ? key : null;
        if (key == _pillDesignKey) return;
        _pillDesignKey = key;
        foreach (var option in PillDesignOptions) option.Refresh();
        OnPropertyChanged(nameof(PillLook));
        PillChanged?.Invoke();
    }

    /// <summary>Si la pastilla tiene un monitor elegido que ahora no está conectado.</summary>
    public bool PillScreenMissing => _pillScreen is not null && _screenKeys.Count > 0 && !_screenKeys.Contains(_pillScreen);

    /// <summary>Aviso de un monitor elegido que no está conectado.</summary>
    public string PillScreenNote => PillScreenMissing
        ? "El monitor donde pusiste la pastilla no está conectado: mientras tanto sale en el de la ventana. Al volver a conectarlo, irá a él."
        : "";

    /// <summary>Apunta las pantallas conectadas ahora, por su clave.</summary>
    public void SetScreens(IReadOnlyList<string> keys)
    {
        _screenKeys = keys;
        OnPillChanged(moved: false);
    }

    /// <summary>Avisa a la vista de que el sitio de la pastilla, o las pantallas, han cambiado; si se ha movido, también a quien la coloca.</summary>
    private void OnPillChanged(bool moved = true)
    {
        foreach (var name in new[] { nameof(PillSpot), nameof(PillScreen), nameof(PillScreenMissing), nameof(PillScreenNote) })
            OnPropertyChanged(name);
        if (moved) PillChanged?.Invoke();
    }

    /// <summary>Versión de producto, «v. 1.2.0»; la de los ficheros y la etiqueta de compilación van en el tooltip.</summary>
    /// <remarks>
    /// La etiqueta (BuildTag) va en un metadato propio del ensamblado: la versión informativa no sirve,
    /// porque el SDK le añade el commit de git y no se distingue de una etiqueta corta.
    /// </remarks>
    public string AppVersion { get; } = "v. " + (Metadata("DisplayVersion")
        ?? System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?");

    public string BuildTip { get; } = BuildTagText();

    /// <summary>Texto del tooltip de la versión: la etiqueta de compilación, si la tiene.</summary>
    private static string BuildTagText()
    {
        var file = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";
        var tag = Metadata("BuildTag");
        var text = string.IsNullOrEmpty(tag) ? $"Versión de la app · ficheros {file}" : $"Versión de la app · ficheros {file} · compilación {tag}";
        return text + " · ventana multiplataforma";
    }

    /// <summary>Valor de un metadato del ensamblado (AssemblyMetadata), o null si no está.</summary>
    private static string? Metadata(string key) =>
        System.Reflection.Assembly.GetExecutingAssembly()
            .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .OfType<System.Reflection.AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == key)?.Value;

    public bool FullVisible => !_compact && !_mini;
    public bool CompactVisible => _compact && !_mini;

    public bool Sound
    {
        get => _sound;
        set
        {
            if (!Set(ref _sound, value)) return;
            // Con los avisos silenciados del todo, un «no molestar» pendiente ya no tiene sentido.
            if (!value) QuietUntil = null;
            OnPropertyChanged(nameof(QuietTip));
            OnPropertyChanged(nameof(SoundIcon));
            OnPropertyChanged(nameof(Muted));
            OnPropertyChanged(nameof(MuteText));
        }
    }

    /// <summary>Glifo del botón de sonido: altavoz o altavoz tachado.</summary>
    public string SoundIcon => _sound ? "" : "";

    /// <summary>Color que identifica un estado de sesión.</summary>
    public static IBrush StatusBrush(SessionStatus status) => status switch
    {
        SessionStatus.Working => Brushes_.Working,
        SessionStatus.Waiting => Brushes_.Waiting,
        SessionStatus.Done => Brushes_.Done,
        SessionStatus.Agents => Brushes_.Agents,
        SessionStatus.Command => Brushes_.Command,
        _ => Brushes_.Idle,
    };

    /// <summary>Fondo translúcido de la pastilla de estado.</summary>
    public static IBrush StatusTint(SessionStatus status) => status switch
    {
        SessionStatus.Working => Brushes_.WorkingTint,
        SessionStatus.Waiting => Brushes_.WaitingTint,
        SessionStatus.Done => Brushes_.DoneTint,
        SessionStatus.Agents => Brushes_.AgentsTint,
        SessionStatus.Command => Brushes_.CommandTint,
        _ => Brushes_.IdleTint,
    };

    /// <summary>Estado en una palabra; en las esperas, el motivo: pregunta, permiso o plan.</summary>
    public static string StatusWord(SessionState s) => s.Status switch
    {
        SessionStatus.Working => "Trabajando",
        SessionStatus.Done => "Terminado",
        SessionStatus.Waiting when s.Detail?.StartsWith(PermissionPrefix, StringComparison.Ordinal) == true => "Permiso",
        SessionStatus.Waiting when s.Detail?.StartsWith("Plan", StringComparison.Ordinal) == true => "Plan",
        SessionStatus.Waiting => "Pregunta",
        SessionStatus.Agents => AgentsWord(s.Background?.RunningAgents ?? 0),
        SessionStatus.Command => CommandsWord(s.Background?.AwaitedCommands ?? 0),
        _ => "Lista",
    };

    /// <summary>Cuántos agentes espera, en dos palabras: «1 agente», «3 agentes».</summary>
    public static string AgentsWord(int running) => running == 1 ? "1 agente" : $"{running} agentes";

    /// <summary>Cuántos comandos espera, en dos palabras: «1 comando», «2 comandos».</summary>
    public static string CommandsWord(int awaited) => awaited == 1 ? "1 comando" : $"{awaited} comandos";

    /// <summary>Lo que dejó en marcha una sesión, en una línea: cuántos comandos y el último; null si nada.</summary>
    public static string? CommandsText(SessionState s) => s.Commands switch
    {
        [] => null,
        _ when s.Commands.Where(c => c.Awaited).ToList() is [var first, ..] awaited =>
            $"Espera a {CommandsWord(awaited.Count)} · {first.Description}" + (first.By is { } by ? $" · de «{by}»" : ""),
        [var one] => "1 comando en marcha · " + one.Description,
        var many => $"{many.Count} comandos en marcha · {many[^1].Description}",
    };

    /// <summary>Detalle que aporta algo más que el estado: el mensaje del permiso o de la espera.</summary>
    /// <remarks>Los textos los escribe HookHandler; aquí se quita la parte que ya dice la palabra de estado.</remarks>
    public static string? StatusDetail(SessionState s)
    {
        if (s.Detail is not { Length: > 0 } detail) return null;
        foreach (var prefix in new[] { PermissionPrefix, WaitPrefix })
        {
            if (detail.StartsWith(prefix, StringComparison.Ordinal))
            {
                var rest = detail[prefix.Length..].TrimStart(':', ' ');
                return rest.Length > 0 ? rest : null;
            }
        }
        return s.Status == SessionStatus.Waiting ? null : detail;
    }

    private const string PermissionPrefix = "Pide permiso";
    private const string WaitPrefix = "Espera tu respuesta";

    /// <summary>Color de una barra de límite según lo consumido.</summary>
    public static IBrush LimitBrush(double pct) => Brushes_.Scale(pct, ring: false);

    /// <summary>Color del anillo de contexto: se calienta según se acerca a la compactación.</summary>
    public static IBrush ContextBrush(double pct) => Brushes_.Scale(pct, ring: true);

    /// <summary>Asigna un campo y avisa a la vista si ha cambiado.</summary>
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    /// <summary>Sustituye una lista solo si su contenido ha cambiado; true si la ha sustituido.</summary>
    /// <remarks>El refresco es cada segundo: asignar listas iguales repintaría la ventana sin cambiar nada en pantalla.</remarks>
    private bool SetList<T>(ref IReadOnlyList<T> field, IReadOnlyList<T> value, [CallerMemberName] string? name = null)
    {
        if (field.SequenceEqual(value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    /// <summary>Notifica el cambio de una propiedad.</summary>
    private void OnPropertyChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
