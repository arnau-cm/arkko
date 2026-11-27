using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Ventana flotante, siempre encima, con límites, consumo del día y sesiones.</summary>
/// <remarks>
/// La misma que la de WPF: vista completa, compacta y pastilla en la esquina. Las posiciones se guardan en el
/// mismo app-settings.json, en unidades lógicas como las guarda WPF; Avalonia coloca en píxeles, así que se
/// convierten con la escala de la pantalla.
/// </remarks>
public partial class MainWindow : Window
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
    private const double DefaultWidth = 456;
    private static readonly TimeSpan WaitWarn = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan WaitHigh = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ReviewAfter = TimeSpan.FromMinutes(5);

    private readonly MainViewModel _vm = new();
    private readonly DataMonitor _monitor = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly AppSettings _settings;
    private readonly Notifier _notifier;
    private readonly ReviewStore _reviews = new();
    private readonly SecretAlertStore _secretAlerts = new();

    // Aviso de secreto ya notificado de cada sesión; la primera pasada solo toma nota.
    private readonly Dictionary<string, DateTime> _secretNotified = new();
    private bool _secretsPrimed;
    private readonly HashSet<string> _hooksNotified = [];
    private string? _hookProblemNotified;
    private int _badgeCount = -1;

    // Lo necesario para volver del minimizado tal como estaba: posición y si era compacta.
    private PixelPoint _miniRestore;
    private PixelRect _miniArea;
    private bool _miniFromCompact;
    private readonly DispatcherTimer _miniHover = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _fadeDelay = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private const double FadedOpacity = 0.55;

    // Finales ya recordados, por sesión e instante de fin: el aviso de «sin revisar» sale una sola vez.
    private readonly Dictionary<string, DateTime> _reminded = new();
    private bool _remindersPrimed;
    private readonly LimitThresholds _limitThresholds = new();

    /// <summary>Análisis de secretos en segundo plano; el panel lo usa para su sección de seguridad.</summary>
    public SecurityMonitor Security { get; }

    /// <summary>El informe de vulnerabilidades de arkko-stack, que comparten la barra, el panel y sus avisos.</summary>
    public StackSecurityMonitor Vulnerabilities { get; }

    private bool _begun;

    // Panel del modo maximizado: mientras está a la vista, esta ventana queda oculta.
    private PanelWindow? _panel;
    private bool _inPanel;
    private bool _miniFromPanel;
    private bool _cleaning;
    private bool _shuttingDown;

    /// <summary>Crea la ventana en la última posición guardada; el refresco arranca con <see cref="Start"/>.</summary>
    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        _settings = JsonStore.Read<AppSettings>(AppPaths.AppSettingsFile) ?? new AppSettings();
        _vm.Sound = _settings.Sound;
        _vm.QuietUntil = _settings.QuietUntilUtc is { } quiet && quiet > DateTime.UtcNow ? quiet.ToLocalTime() : null;
        _vm.FadeWhenIdle = _settings.FadeWhenIdle;
        // El diseño, antes de escuchar los cambios de la pastilla: cambiarlo la repinta y guarda, y aún no está todo cargado.
        _vm.Design = _settings.Design ?? (_settings.LightTheme ? "glass-light" : PillDesigns.DefaultKey);
        _vm.SetPill(_settings.PillX, _settings.PillY, _settings.PillScreen);
        _vm.SetPillDesign(_settings.PillDesign);
        ApplyPillLook();
        _vm.PillChanged += () =>
        {
            ApplyPillLook();
            MovePill();
            SavePlacement();
        };
        _vm.LimitsOpen = _settings.LimitsOpen;
        _vm.ModelsOpen = _settings.ModelsOpen;
        _vm.SessionsOpen = _settings.SessionsOpen;
        _vm.Compact = _settings.Compact;
        App.ApplyDesign(_vm.Design);
        _vm.PropertyChanged += OnViewModelChanged;

        Security = new SecurityMonitor(_settings, SavePlacement);
        Security.PendingChanged += () => _vm.SecurityPending = Security.PendingCount;
        Security.RecountPending();
        _notifier = new Notifier(BringToFront, Close);
        Vulnerabilities = new StackSecurityMonitor(_settings);
        Vulnerabilities.Changed += () => _vm.Vulnerabilities = (Vulnerabilities.Failures, Vulnerabilities.Unseen);
        Vulnerabilities.Alert += alert =>
        {
            _notifier.StackAlert(alert, _settings.Toasts && _vm.AlertsOn, _vm.AlertsOn);
            TourAlert(alert.Tag, alert.Title, alert.Text);
        };
        SessionActions.CardClicked += OnCardClicked;
        SessionActions.TerminalRequested += OnTerminalRequested;
        SessionActions.ViewSessionRequested += OnViewSession;
        SessionActions.TaskRequested += OnTaskRequested;
        SessionActions.SecurityRequested += OpenSecurity;
        SessionActions.SessionIdRequested += OnSessionRequested;
        SessionActions.BoardsConfigRequested += OnBoardsConfig;
        SessionActions.ArtifactsRequested += OnArtifactsRequested;
        SessionActions.ArtifactRequested += OnArtifactRequested;
        SessionActions.SectionRequested += OnSectionRequested;
        _timer.Tick += (_, _) =>
        {
            Refresh();
            SecretPlanService.Instance.CheckDay();
        };
        _miniHover.Tick += (_, _) => CloseMiniPanelIfAway();
        _fadeDelay.Tick += (_, _) => FadeIfIdle();
        PointerEntered += (_, _) => Unfade();
        PointerExited += (_, _) => ScheduleFade();
        SettingsPopup.Closed += (_, _) => ScheduleFade();
        Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(200) }];
        Opened += (_, _) => RestorePlacement();
        Screens.Changed += OnScreensChanged;
        Closing += (_, _) =>
        {
            SavePlacement();
            // Antes de que se cierren sus ventanas: cerrar la app no olvida los corchos.
            CorkWindow.SaveForShutdown();
            _shuttingDown = true;
            _panel?.CloseForShutdown();
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            _notifier.Dispose();
            Screens.Changed -= OnScreensChanged;
            SessionActions.CardClicked -= OnCardClicked;
            SessionActions.TerminalRequested -= OnTerminalRequested;
            SessionActions.ViewSessionRequested -= OnViewSession;
            SessionActions.TaskRequested -= OnTaskRequested;
            SessionActions.SecurityRequested -= OpenSecurity;
            SessionActions.SessionIdRequested -= OnSessionRequested;
            SessionActions.BoardsConfigRequested -= OnBoardsConfig;
            SessionActions.ArtifactsRequested -= OnArtifactsRequested;
            SessionActions.ArtifactRequested -= OnArtifactRequested;
        };
        Snapshot.WhenOpened(this, "flotante");
        if (Snapshot.Tour.Contains("mini")) Opened += async (_, _) => await SnapshotMini();
        if (Snapshot.Tour.Contains("click")) Opened += async (_, _) => await SnapshotClick();
        if (Snapshot.Tour.Contains("menu")) Opened += async (_, _) =>
        {
            // Para el recorrido de imágenes: abre el menú contextual de la primera tarjeta, para capturarlo desde fuera.
            await Task.Delay(3000);
            Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(this).OfType<Button>().FirstOrDefault(b => b.ContextMenu is not null)
                ?.ContextMenu?.Open();
        };
    }

    /// <summary>Para el recorrido de imágenes: pulsa cada tarjeta de la flotante y guarda cómo queda después.</summary>
    private async Task SnapshotClick()
    {
        await Task.Delay(3000);
        foreach (var row in _vm.Sessions.ToList()) SessionActions.Card.Execute(row);
        await Task.Delay(2500);
        Snapshot.Save(Content as Visual ?? this, "flotante-click");
    }

    /// <summary>Para el recorrido de imágenes: minimiza a la pastilla y guarda la pastilla y su panel abierto.</summary>
    private async Task SnapshotMini()
    {
        await Task.Delay(3000);
        EnterMini();
        await Task.Delay(1500);
        Snapshot.Save(MiniPill, "mini");
        MiniPanel.IsOpen = true;
        await Task.Delay(1500);
        Snapshot.Save(MiniPanelBody, "mini-panel");
    }

    /// <summary>Si la app se está cerrando: el panel no debe ocultarse en lugar de cerrarse.</summary>
    public bool ShuttingDown => _shuttingDown;

    /// <summary>Sesiones abiertas ahora mismo, para la sección Sesiones del panel.</summary>
    public IReadOnlyList<SessionState> OpenSessions => _monitor.Sessions;

    /// <summary>Los ajustes de la app, compartidos con el panel.</summary>
    public AppSettings Settings => _settings;

    /// <summary>Minimizar convierte la ventana en pastilla; con la pastilla puesta, minimizar de nuevo la devuelve.</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != WindowStateProperty || WindowState != WindowState.Minimized) return;
        WindowState = WindowState.Normal;
        if (_vm.Mini) ExitMini();
        else EnterMini();
    }

    /// <summary>Abre el panel en el Tablero, en la tarea pedida.</summary>
    private void OnTaskRequested(string key)
    {
        OpenPanel();
        _panel?.ShowTask(key);
    }

    /// <summary>Abre el panel en Artifacts con lo publicado desde un repositorio o una de sus carpetas.</summary>
    private void OnArtifactsRequested(string group, string? project)
    {
        OpenPanel();
        _panel?.ShowArtifacts(group, project);
    }

    /// <summary>Abre el panel en Artifacts con un artifact abierto en el visor.</summary>
    private void OnArtifactRequested(string url)
    {
        OpenPanel();
        _panel?.ShowArtifact(url);
    }

    /// <summary>Abre el panel en Sesiones con la sesión de la tarjeta, desde «Ver sesión».</summary>
    private void OnViewSession(SessionRow row) => OnSessionRequested(row.SessionId);

    /// <summary>Abre el panel en Sesiones con una sesión, por su id: desde una tarjeta o desde una vista clavada en un corcho.</summary>
    private void OnSessionRequested(string sessionId)
    {
        OpenPanel();
        _panel?.ShowSession(sessionId);
    }

    /// <summary>Abre el panel en una sección, desde «Abrir en el panel» del widget de un corcho.</summary>
    private void OnSectionRequested(PanelSection section)
    {
        OpenPanel();
        _panel?.ShowSection(section);
    }

    /// <summary>Abre el panel en la configuración de los tableros, desde un Tablero clavado en un corcho.</summary>
    private void OnBoardsConfig()
    {
        OpenPanel();
        _panel?.ShowBoardsConfig();
    }

    /// <summary>Abre la app en el último modo usado: el panel o la ventana flotante.</summary>
    public void Start()
    {
        Begin();
        RefreshPillScreens();
        if (_settings.Panel) OpenPanel();
        else Show();
        // Los paneles de los corchos necesitan los secretos y el nombre, se abra o no el panel: se les dan aquí.
        Pieces.Init(() => Security, () => string.IsNullOrWhiteSpace(_settings.UserName) ? "User" : _settings.UserName.Trim(), () => _monitor.Sessions,
            Attention, MarkReviewed);
        NativeViews.Init(() => _vm, () => _settings);
        CorkWindow.Restore();
    }

    /// <summary>Arranca el refresco de datos y avisos, una sola vez, se vea o no esta ventana.</summary>
    private void Begin()
    {
        if (_begun) return;
        _begun = true;
        Refresh();
        _timer.Start();
        // Las tareas de los tableros, en segundo plano: dan su título a la tarea de cada tarjeta.
        TaskDirectory.Instance.Changed += ScheduleRefresh;
        _monitor.Hooks.HealthChanged += () => Dispatcher.UIThread.Post(ScheduleRefresh);
        TaskDirectory.Instance.Start();
        DiaryKeeper.Instance.Start();
        WorktreeMonitor.Instance.Start();
        Vulnerabilities.Start();
        CleanupIfDue();
    }

    /// <summary>Lanza la limpieza automática de la carpeta de datos si hoy aún no se ha hecho.</summary>
    /// <remarks>Va en segundo plano: lee y reescribe ficheros, y la ventana no debe esperar por ello.</remarks>
    private void CleanupIfDue()
    {
        if (_cleaning || _settings.LastCleanupUtc?.ToLocalTime().Date == DateTime.Today) return;
        _cleaning = true;
        var retention = _settings.Retention;
        Task.Run(() => new DataRetention().Apply(retention, DateTime.Now)).ContinueWith(task =>
        {
            _cleaning = false;
            if (task.Exception is { } ex)
            {
                JsonStore.LogError("retention", ex.GetBaseException());
                return;
            }
            RecordCleanup(task.Result);
            if (task.Result.Any) _panel?.ShowNotice("Limpieza automática: " + CleanupNotes.CleanupText(task.Result));
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Apunta en los ajustes cuándo fue la última limpieza y qué hizo, y los guarda.</summary>
    private void RecordCleanup(CleanupResult? result)
    {
        _settings.LastCleanupUtc = DateTime.UtcNow;
        _settings.LastCleanupText = result is null ? "no había nada que borrar" : CleanupNotes.CleanupText(result);
        SavePlacement();
        _panel?.ForceReload();
    }

    /// <summary>Pasa al panel del modo maximizado y oculta la ventana flotante.</summary>
    private void OpenPanel()
    {
        SettingsPopup.IsOpen = false;
        if (_panel is null)
        {
            _panel = new PanelWindow(this, _vm, _settings);
            _panel.RestorePlacement();
        }
        _inPanel = true;
        _panel.Show();
        if (_settings.PanelMaximized && _panel.WindowState == WindowState.Normal) _panel.WindowState = WindowState.Maximized;
        _panel.Activate();
        Hide();
    }

    /// <summary>Minimiza el panel a la pastilla de la esquina; un clic en ella vuelve al panel.</summary>
    private void MiniFromPanel()
    {
        if (_panel is null) return;
        var area = PillPlacement.Area(_panel, _vm.PillScreen);
        _inPanel = false;
        _miniFromPanel = true;
        _panel.SavePlacement();
        _panel.Hide();
        ShowActivated = false;
        Show();
        ShowActivated = true;
        EnterMini(area);
    }

    /// <summary>Vuelve del panel a la ventana flotante.</summary>
    private void ShowFloating()
    {
        _inPanel = false;
        if (_panel is not null)
        {
            _panel.SavePlacement();
            _panel.Hide();
        }
        Show();
        Activate();
    }

    // ---------- lo que el panel le pide a la ventana flotante ----------

    /// <summary>Vuelve a la ventana flotante, desde el panel.</summary>
    public void PanelToFloating() => ShowFloating();

    /// <summary>Minimiza el panel a la pastilla.</summary>
    public void PanelToMini() => MiniFromPanel();

    /// <summary>Activa el «no molestar» durante una hora.</summary>
    public void QuietForAnHour() => SetQuiet(DateTime.Now.AddHours(1));

    /// <summary>Activa el «no molestar» hasta mañana a las 8:00.</summary>
    public void QuietUntilTomorrow() => SetQuiet(DateTime.Today.AddDays(1).AddHours(8));

    /// <summary>Quita el «no molestar».</summary>
    public void QuietOff() => SetQuiet(null);

    /// <summary>Pone o quita el modo compacto de la flotante, aunque esté oculta tras el panel.</summary>
    public void SetCompact(bool on)
    {
        if (on == _vm.Compact) return;
        _vm.Compact = on;
        SavePlacement();
    }

    /// <summary>Cambia los dos umbrales de aviso de los límites y los guarda.</summary>
    public void SetThresholds(int first, int second)
    {
        _settings.LimitThresholds = [Math.Min(first, second), Math.Max(first, second)];
        SavePlacement();
    }

    /// <summary>Guarda los plazos de los datos y apunta la limpieza que se haya hecho.</summary>
    public void RetentionSaved(RetentionSettings retention, CleanupResult? result)
    {
        _settings.Retention = retention;
        RecordCleanup(result);
    }

    /// <summary>Guarda los ajustes en el momento.</summary>
    public void SaveSettings() => SavePlacement();

    /// <summary>Abre el panel desde el botón de maximizar.</summary>
    private void Panel_Click(object? sender, RoutedEventArgs e) => OpenPanel();

    /// <summary>Abre el panel en Seguridad, desde el escudo de los secretos pendientes.</summary>
    private void SecurityBadge_Click(object? sender, RoutedEventArgs e) => OpenSecurity();

    /// <summary>Abre el panel en Vulnerabilidades, desde su icono de la barra.</summary>
    private void VulnerabilityBadge_Click(object? sender, RoutedEventArgs e) => OnSectionRequested(PanelSection.Vulnerabilities);

    /// <summary>Abre el panel en la sección Seguridad, donde un secreto se rota o se descarta.</summary>
    private void OpenSecurity()
    {
        OpenPanel();
        _panel?.ShowSection(PanelSection.Security);
    }

    /// <summary>Restaura la ventana si estaba minimizada y la pone delante.</summary>
    public void BringToFront()
    {
        if (_inPanel && _panel is not null)
        {
            if (_panel.WindowState == WindowState.Minimized) _panel.WindowState = WindowState.Normal;
            _panel.Show();
            _panel.Activate();
            return;
        }
        if (_vm.Mini) ExitMini();
        if (_inPanel) return;
        Show();
        Activate();
    }

    private bool _refreshQueued;

    /// <summary>Pide un repintado para cuando la ventana quede libre; varios seguidos se quedan en uno.</summary>
    /// <remarks>Al leer un tablero llegan varios avisos de cambio casi a la vez, y cada repintado relee las sesiones.</remarks>
    private void ScheduleRefresh()
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _refreshQueued = false;
            Refresh();
        }, DispatcherPriority.Background);
    }

    /// <summary>Relee los datos, actualiza la vista y lanza los avisos pendientes.</summary>
    private void Refresh()
    {
        List<SessionAlert> alerts;
        try
        {
            alerts = _monitor.Refresh();
        }
        catch (Exception ex)
        {
            JsonStore.LogError("refresh", ex);
            return;
        }

        if (_vm.QuietUntil <= DateTime.Now) _vm.QuietUntil = null;
        CleanupIfDue();
        Security.ScanIfDue();
        ArtifactMonitor.Instance.ScanIfDue();
        RenderLimits();
        RenderModels();
        var ordered = RenderSessions();

        _notifier.SetState(ordered.Count == 0 ? null : ordered[0].Status);
        UpdateBadge(ordered.Count(NeedsAttention));
        RemindOverdue(ordered);
        NotifySecrets(ordered);
        NotifyHooks(ordered);
        WarnLimits();
        // Silenciar corta sonido y notificaciones; el contador de la pastilla no depende de ello.
        foreach (var alert in alerts)
        {
            _notifier.Alert(alert.Session, _settings.Toasts && _vm.AlertsOn, _vm.AlertsOn);
            TourAlert("sesion", $"{alert.Session.Title}: {alert.Session.Status}", alert.Session.Commands.Count == 0 ? "" : $"{alert.Session.Commands.Count} en marcha");
        }
    }

    /// <summary>Apunta un aviso en el recorrido de imágenes, para revisarlo sin notificaciones en pantalla; fuera de él, nada.</summary>
    private static void TourAlert(string tag, string title, string text)
    {
        if (Snapshot.Folder is not { } folder) return;
        Directory.CreateDirectory(folder);
        File.AppendAllText(Path.Combine(folder, "tour-alerts.txt"), $"{DateTime.Now:HH:mm:ss} [{tag}] {title} | {text}\n");
    }

    /// <summary>Avisa de los límites que acaban de pasar uno de los umbrales configurados.</summary>
    private void WarnLimits()
    {
        if (_monitor.Limits is not { } snapshot) return;
        foreach (var crossing in _limitThresholds.Check(snapshot.Limits, _settings.LimitThresholds))
            _notifier.LimitAlert(crossing, _settings.Toasts && _vm.AlertsOn, _vm.AlertsOn);
    }

    /// <summary>Texto de la previsión: cuándo se agota o cuánto marcará al renovarse.</summary>
    private static string ForecastText(LimitProjection? projection, DateTime now) => projection switch
    {
        null => "Sin datos suficientes todavía",
        { ToFull: { } full } => $"Se agota en ~{Format.Until(now + full)} · {When(now + full)}",
        _ => $"~{(int)Math.Round(projection.AtReset)} % al renovarse",
    };

    /// <summary>Hora local legible de un instante: «hoy 13:00», «mañana 09:30» o «dom 4 oct, 14:00».</summary>
    private static string When(DateTime utc)
    {
        var local = utc.ToLocalTime();
        var days = (local.Date - DateTime.Now.Date).Days;
        return days switch
        {
            0 => "hoy " + local.ToString("HH:mm", Es),
            1 => "mañana " + local.ToString("HH:mm", Es),
            _ => local.ToString("ddd d MMM, HH:mm", Es).Replace(".", ""),
        };
    }

    /// <summary>Pinta los límites de uso de la cuenta.</summary>
    private void RenderLimits()
    {
        var snapshot = _monitor.Limits;
        if (snapshot is null || snapshot.Limits.Count == 0)
        {
            _vm.Limits = [];
            _vm.LimitsNote = "Sin datos todavía: llegan con la primera respuesta de cualquier sesión.";
            return;
        }

        var now = DateTime.UtcNow;
        var fg = ThemeColors.Of("Fg", "#F2F3F7");
        _vm.Limits = snapshot.Limits.Select(l =>
        {
            var projection = LimitForecast.Project(l, now);
            // Redondeado: con decimales, la fila cambiaría en cada refresco y la lista se reharía cada
            // segundo, cerrando el tooltip abierto.
            var projected = projection is null ? 0 : Math.Round(Math.Clamp(projection.AtReset, 0, 100));
            var reset = l.ResetsAtUtc is { } at ? $"en {Format.Until(at)} · {When(at)}" : "Sin dato";
            return new LimitRow(
                l.Label,
                Math.Clamp(l.UsedPct, 0, 100),
                ((int)Math.Round(l.UsedPct)).ToString(Es) + " %",
                Format.Until(l.ResetsAtUtc),
                MainViewModel.LimitBrush(l.UsedPct),
                projected,
                MainViewModel.LimitBrush(projected),
                reset,
                ForecastText(projection, now),
                projection?.ToFull is not null ? Brushes_.High : fg)
            {
                ResetsAtUtc = l.ResetsAtUtc,
            };
        }).ToList();

        var age = DateTime.UtcNow - snapshot.UpdatedUtc;
        var ago = Format.Ago(snapshot.UpdatedUtc);
        _vm.LimitsNote = age > TimeSpan.FromMinutes(10)
            ? $"Último dato hace {ago}: se actualiza cuando alguna sesión responde."
            : ago == "ahora" ? "Actualizado ahora" : $"Actualizado hace {ago}";
    }

    /// <summary>Pinta los tokens consumidos hoy, del modelo que más consume al que menos, con el total y su reparto.</summary>
    private void RenderModels()
    {
        var today = _monitor.Today.OrderByDescending(m => NewTokens(m.Value)).ToList();
        var total = today.Sum(m => NewTokens(m.Value));
        var max = today.Select(m => (double)NewTokens(m.Value)).DefaultIfEmpty(0).Max();
        _vm.Models = today
            .Select(m => new ModelRow(
                m.Key,
                Format.Tokens(NewTokens(m.Value)),
                $"+{Format.Tokens(m.Value.CacheRead)} de caché · {m.Value.ApiCalls} llamadas",
                m.Key.StartsWith("Fable", StringComparison.OrdinalIgnoreCase) ? FontWeight.SemiBold : FontWeight.Normal,
                Tip(m.Key, m.Value))
            {
                Brush = ModelColors.For(m.Key),
                Fill = ModelColors.For(m.Key, 0.18),
                Value = NewTokens(m.Value),
                Max = max,
                Pct = total > 0 ? Math.Round(100.0 * NewTokens(m.Value) / total) + " %" : "",
            })
            .DefaultIfEmpty(new ModelRow("Sin prompts terminados hoy", "", "", FontWeight.Normal, null))
            .ToList();
        _vm.ModelsTotal = today.Count > 0 ? Format.Tokens(total) : "";
        _vm.ModelsDetail = today.Count > 0
            ? $"+{Format.Tokens(today.Sum(m => m.Value.CacheRead))} de caché · {today.Sum(m => m.Value.ApiCalls)} llamadas"
            : "";
        _vm.ModelShares = today.Select(m => new BarSegment(NewTokens(m.Value), ModelColors.For(m.Key))).ToList();
    }

    /// <summary>Trabajo nuevo de un modelo: entrada, salida y escritura en caché, sin la relectura de caché.</summary>
    private static long NewTokens(ModelTokens t) => t.Input + t.Output + t.CacheWrite;

    /// <summary>Ficha de los tokens de hoy de un modelo, para el tooltip de su fila.</summary>
    private static ModelTip Tip(string model, ModelTokens t) => new(
        $"{model} · hoy",
        Format.Tokens(NewTokens(t)),
        Format.Tokens(t.Input),
        Format.Tokens(t.Output),
        Format.Tokens(t.CacheWrite),
        Format.Tokens(t.CacheRead),
        t.ApiCalls.ToString(Es));

    /// <summary>Pinta las sesiones abiertas, las que esperan a la persona primero.</summary>
    private List<SessionState> RenderSessions()
    {
        _reviews.Prune(_monitor.Sessions.Select(s => s.SessionId));
        _secretAlerts.Prune(_monitor.Sessions.Select(s => s.SessionId));

        // Primero lo que pide atención; después, la actividad más reciente arriba.
        var sessions = _monitor.Sessions
            .OrderBy(s => s.Status == SessionStatus.Waiting ? 0 : ReviewLevel(s) == 2 ? 1 : 2)
            .ThenByDescending(s => s.StatusSinceUtc)
            .ToList();
        _vm.SessionsTitle = sessions.Count == 0 ? "SESIONES" : $"SESIONES ({sessions.Count})";
        _vm.Sessions = sessions.Select(s =>
        {
            var project = s.Repo ?? s.Project ?? s.SessionId[..Math.Min(8, s.SessionId.Length)];
            var (effortLevel, effortText) = EffortLevels.Of(s.Effort);
            var recap = s.Status == SessionStatus.Working ? null : _monitor.RecapFor(s);
            var tasks = TaskDirectory.Instance.TasksOfSession(s.SessionId, s.Project);
            return new SessionRow(
                s.SessionId,
                project,
                s.SessionName?.Trim() ?? "",
                s.Model ?? "",
                MainViewModel.StatusWord(s),
                MainViewModel.StatusDetail(s),
                Format.Ago(s.StatusSinceUtc),
                MainViewModel.StatusBrush(s.Status),
                s.ContextPct,
                ContextTip(s),
                MainViewModel.ContextBrush(s.ContextPct ?? 0),
                effortLevel,
                effortText,
                WaitLevel(s),
                s.Status == SessionStatus.Working,
                MainViewModel.StatusTint(s.Status),
                s.Status == SessionStatus.Waiting,
                ReviewLevel(s),
                recap?.Text,
                recap is { } r ? "Recap · " + (Format.Ago(r.Utc) == "ahora" ? "ahora" : "hace " + Format.Ago(r.Utc)) : "")
            {
                SinceUtc = s.StatusSinceUtc,
                Worktree = s.Repo is null ? null : s.Project,
                TaskKey = tasks.Principal,
                TaskChips = SessionTaskChip.For(tasks),
                TaskTitle = tasks.Principal is { } principal ? TaskDirectory.Instance.Find(principal)?.Task.Title : null,
                SecretAlert = _settings.SecretAlerts ? Security.SecretLabel(s) : null,
                HookError = s.HookError is { } hooks ? $"{hooks} (a las {s.HookErrorUtc?.ToLocalTime():HH:mm})" : null,
                Agents = s.Status == SessionStatus.Agents,
                Command = s.Status == SessionStatus.Command,
                AgentLines = AgentLineList.Of(s),
                CommandText = MainViewModel.CommandsText(s),
                CommandTip = s.Commands.Count == 0 ? null : string.Join("\n", s.Commands.Select(c => c.Description)),
            };
        }).ToList();
        return sessions;
    }

    /// <summary>Revisión pendiente de una sesión: 0 vista o no terminada, 1 recién terminada, 2 sin mirar pasado el rato.</summary>
    private int ReviewLevel(SessionState s)
    {
        if (s.Status != SessionStatus.Done || _reviews.IsReviewed(s)) return 0;
        return DateTime.UtcNow - s.StatusSinceUtc >= ReviewAfter ? 2 : 1;
    }

    /// <summary>Da por revisada una sesión terminada que estaba pendiente, como al pulsar su tarjeta, y quita su aviso.</summary>
    public void MarkReviewed(string sessionId)
    {
        var session = _monitor.Sessions.FirstOrDefault(s => s.SessionId == sessionId);
        if (session is null || ReviewLevel(session) == 0) return;
        _reviews.MarkReviewed(session);
        SystemNotifications.Remove(session.SessionId);
        Refresh();
        Pieces.NotifyReviewed();
    }

    /// <summary>Lo que pide una sesión a la persona: revisión y espera de respuesta, de 0 a 2, y el secreto sin revisar, con la misma regla que el inicio.</summary>
    public (int Review, int Wait, string? Secret) Attention(SessionState s) =>
        (ReviewLevel(s), WaitLevel(s), _settings.SecretAlerts ? Security.SecretLabel(s) : null);

    /// <summary>Sesiones que cuentan para el contador: esperan respuesta o están terminadas sin revisar.</summary>
    private bool NeedsAttention(SessionState s) => s.Status == SessionStatus.Waiting || ReviewLevel(s) > 0;

    /// <summary>Avisa una vez de cada sesión que acaba de pasar a «sin revisar» con retraso.</summary>
    /// <remarks>La primera pasada solo toma nota de lo que ya estaba así al abrir la app: no se avisa de golpe de sesiones antiguas.</remarks>
    private void RemindOverdue(List<SessionState> sessions)
    {
        foreach (var s in sessions.Where(s => ReviewLevel(s) == 2))
        {
            if (_reminded.TryGetValue(s.SessionId, out var at) && at == s.StatusSinceUtc) continue;
            _reminded[s.SessionId] = s.StatusSinceUtc;
            if (_remindersPrimed) _notifier.RemindReview(s, ReviewAfter, _settings.Toasts && _vm.AlertsOn, _vm.AlertsOn);
        }
        _remindersPrimed = true;
    }

    /// <summary>Indica si el aviso de secreto de una sesión sigue abierto: sin dar por visto ni resuelto en Seguridad.</summary>
    private bool SecretAlertOpen(SessionState s) => _secretAlerts.IsPending(s) && !Security.IsSettled(s);

    /// <summary>Notifica una vez cada aviso de secreto enviado que el hook ha dejado en una sesión.</summary>
    /// <remarks>Como los recordatorios, la primera pasada solo toma nota: al abrir la app no se avisa de golpe de lo de antes.</remarks>
    private void NotifySecrets(List<SessionState> sessions)
    {
        if (!_settings.SecretAlerts) return;
        foreach (var s in sessions.Where(SecretAlertOpen))
        {
            if (_secretNotified.TryGetValue(s.SessionId, out var at) && at == s.SecretAlertUtc) continue;
            _secretNotified[s.SessionId] = s.SecretAlertUtc!.Value;
            if (!_secretsPrimed) continue;
            _notifier.SecretAlert(s, _settings.Toasts && _vm.AlertsOn, _vm.AlertsOn);
            // Que el secreto llegue a Seguridad en segundos, no al siguiente análisis automático.
            Security.ScanSoon();
        }
        _secretsPrimed = true;
    }

    /// <summary>Resume lo que falla ahora de los hooks de Arkko para las vistas y avisa una vez de cada fallo nuevo: uno
    /// del equipo o una sesión que empieza a fallar.</summary>
    /// <remarks>
    /// El aviso general es solo para lo que sigue fallando; lo que falló y ya arranca bien se queda en la marca de su
    /// sesión. Si fallan varias sesiones a la vez, como al actualizarse .NET, llega un solo aviso. Hasta la primera
    /// comprobación del equipo no se avisa de las sesiones: al abrir la app, un fallo de antes puede estar ya arreglado.
    /// </remarks>
    private void NotifyHooks(List<SessionState> sessions)
    {
        var health = _monitor.Hooks.Health;
        var failing = sessions.Where(s => s.HookError is not null && _monitor.Hooks.StillFailing(s.HookErrorUtc ?? DateTime.MinValue)).ToList();
        var machine = health is { Ok: false } bad ? bad.Problem + (bad.Fix is { } fix ? ". " + fix : "") : null;
        _vm.HookProblem = machine ?? (failing.Count == 0 ? null
            : $"Los hooks de Arkko fallan en {(failing.Count == 1 ? "una sesión" : $"{failing.Count} sesiones")}: {failing[0].HookError}");

        var marked = sessions.Where(s => s.HookError is not null).Select(s => s.SessionId).ToList();
        _hooksNotified.IntersectWith(marked);
        if (health is null) return;
        var fresh = failing.Where(s => _hooksNotified.Add(s.SessionId)).ToList();
        _hooksNotified.UnionWith(marked);
        var toast = _settings.Toasts && _vm.AlertsOn;
        if (machine is not null && machine != _hookProblemNotified)
            _notifier.HookAlert("Arkko no recibe datos de Claude Code", machine, toast, _vm.AlertsOn);
        else if (machine is null && fresh.Count > 0)
            _notifier.HookAlert("Los hooks de Arkko fallan",
                $"{fresh[0].HookError} · {string.Join(", ", fresh.Select(s => s.Title ?? s.SessionId[..Math.Min(8, s.SessionId.Length)]))}", toast, _vm.AlertsOn);
        _hookProblemNotified = machine;
    }

    /// <summary>Apunta cuántas sesiones piden atención, para el contador de la pastilla.</summary>
    private void UpdateBadge(int count)
    {
        if (count == _badgeCount) return;
        _badgeCount = count;
        _vm.AttentionCount = count;
        Title = count == 0 ? "Arkko" : $"Arkko: {count} sesión(es) te esperan";
    }

    /// <summary>Convierte la ventana en la pastilla de la esquina, recordando cómo estaba.</summary>
    private void EnterMini(PixelRect? area = null)
    {
        if (_vm.Mini) return;
        SettingsPopup.IsOpen = false;
        _miniRestore = Position;
        _miniArea = area ?? PillPlacement.Area(this, _vm.PillScreen);
        _miniFromCompact = _vm.Compact;
        _vm.Mini = true;
        MinWidth = 0;
        SizeToContent = SizeToContent.WidthAndHeight;
        // El tamaño nuevo se conoce tras medir: se coloca entonces, y cada vez que cambie.
        Dispatcher.UIThread.Post(PlaceMini, DispatcherPriority.Render);
    }

    /// <summary>Coloca la pastilla en su sitio de su monitor, con su panel desplegándose hacia dentro de la pantalla.</summary>
    private void PlaceMini()
    {
        var scale = DesktopScaling;
        var size = new PixelSize((int)Math.Ceiling(Bounds.Width * scale), (int)Math.Ceiling(Bounds.Height * scale));
        var (x, y) = _vm.PillSpot;
        Position = PillPlacement.Position(_miniArea, size, (int)Math.Round(PillPlacement.Margin * scale), x, y);
        _vm.PillSize = Bounds.Size;
        MiniPanel.Placement = PillPlacement.PanelPlacement(y);
        MiniPanel.VerticalOffset = MiniPanel.Placement == PlacementMode.Top ? -10 : 10;
    }

    /// <summary>Da a la pastilla y a su panel los colores de su diseño en lo que pintan sus plantillas compartidas: textos, límites y separadores.</summary>
    /// <remarks>Esas plantillas toman los colores del tema por recurso; uno con el mismo nombre en la pastilla manda sobre el del tema.</remarks>
    private void ApplyPillLook()
    {
        var look = _vm.PillLook;
        foreach (var host in new Control[] { MiniPill, MiniPanelBody })
        {
            host.Resources["Fg"] = look.Text;
            host.Resources["FgStrong"] = look.Strong ?? look.Text;
            host.Resources["FgTitle"] = look.Title ?? look.Text;
            host.Resources["FgLabel"] = look.Strong ?? look.Muted;
            // La pastilla puede llevar otro diseño que la app: el texto en varias intensidades lo decide el suyo.
            host.Classes.Set("terminal", look.Tiered);
            host.Resources["FgMuted"] = look.Muted;
            host.Resources["FgFaint"] = look.Muted;
            host.Resources["TrackFill"] = look.Track;
            host.Resources["TrackEdge"] = look.Track;
        }
    }

    /// <summary>Apunta las pantallas conectadas, para el dibujo de Configuración → Pastilla.</summary>
    private void RefreshPillScreens() =>
        _vm.SetScreens(PillPlacement.Monitors(Screens.All).Select(m => m.Key).ToList());

    /// <summary>Al conectar o desconectar un monitor, apunta las pantallas y, con la pastilla puesta, la vuelve a colocar.</summary>
    private void OnScreensChanged(object? sender, EventArgs e)
    {
        RefreshPillScreens();
        MovePill();
    }

    /// <summary>Con la pastilla puesta, la lleva a la esquina y el monitor elegidos ahora.</summary>
    private void MovePill()
    {
        if (!_vm.Mini) return;
        _miniArea = PillPlacement.Area(this, _vm.PillScreen);
        PlaceMini();
    }

    /// <summary>Si el tamaño cambia con la pastilla puesta, la mantiene en su esquina.</summary>
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_vm.Mini) PlaceMini();
    }

    /// <summary>Área de trabajo (sin la barra de tareas) del monitor donde está una ventana, en píxeles.</summary>
    private static PixelRect WorkArea(Window window) =>
        (window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary)?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);

    /// <summary>Devuelve la ventana a como estaba antes de minimizar: completa o compacta.</summary>
    private void ExitMini()
    {
        if (!_vm.Mini) return;
        MiniPanel.IsOpen = false;
        _miniHover.Stop();
        // Si vuelve al panel, se oculta antes de recuperar su forma: solo debe verse aparecer el panel.
        if (_miniFromPanel) Hide();
        _vm.Mini = false;
        MinWidth = 300;
        SizeToContent = SizeToContent.Height;
        Width = DefaultWidth;
        Position = _miniRestore;
        if (_miniFromPanel)
        {
            _miniFromPanel = false;
            OpenPanel();
            return;
        }
        Activate();
    }

    /// <summary>Página anterior de sesiones.</summary>
    private void PrevPage_Click(object? sender, RoutedEventArgs e) => _vm.SessionPage--;

    /// <summary>Página siguiente de sesiones.</summary>
    private void NextPage_Click(object? sender, RoutedEventArgs e) => _vm.SessionPage++;

    /// <summary>Un clic en la pastilla abre la ventana.</summary>
    private void Mini_PointerReleased(object? sender, PointerReleasedEventArgs e) => ExitMini();

    /// <summary>Al pasar el ratón por la pastilla, despliega encima el panel con límites y sesiones.</summary>
    private void Mini_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (!_vm.Mini) return;
        MiniPanel.IsOpen = true;
        _miniHover.Start();
    }

    /// <summary>Cierra el panel cuando el ratón ya no está ni en la pastilla ni en el panel.</summary>
    private void CloseMiniPanelIfAway()
    {
        if (_vm.Mini && (MiniPill.IsPointerOver || MiniPanelBody.IsPointerOver)) return;
        MiniPanel.IsOpen = false;
        _miniHover.Stop();
        ScheduleFade();
    }

    /// <summary>Al hacer clic en una tarjeta, la da por revisada y quita sus avisos, sin cambiar de ventana.</summary>
    private void OnCardClicked(SessionRow row)
    {
        var session = _monitor.Sessions.FirstOrDefault(s => s.SessionId == row.SessionId);
        if (session is not null) MarkSeen(row, session);
    }

    /// <summary>Da la sesión por revisada y trae al frente su ventana de terminal (solo en Windows).</summary>
    private void OnTerminalRequested(SessionRow row)
    {
        var session = _monitor.Sessions.FirstOrDefault(s => s.SessionId == row.SessionId);
        if (session is null) return;

        MarkSeen(row, session);
#if WINDOWS
        if (!TerminalWindow.Focus(session)) JsonStore.LogError("terminal", new InvalidOperationException(
            $"Sin ventana de terminal para la sesión {session.Title} (pid {session.ClaudePid})"));
#endif
    }

    /// <summary>Quita el aviso de revisión y el de secreto de una sesión, si los tiene.</summary>
    private void MarkSeen(SessionRow row, SessionState session)
    {
        if (!row.NeedsReview && row.SecretAlert is null) return;
        if (row.NeedsReview)
        {
            _reviews.MarkReviewed(session);
            SystemNotifications.Remove(session.SessionId);
            Pieces.NotifyReviewed();
        }
        if (row.SecretAlert is not null)
        {
            _secretAlerts.MarkSeen(session);
            SystemNotifications.Remove("secret-" + session.SessionId);
        }
        Refresh();
    }

    /// <summary>Nivel de alerta de una sesión que espera respuesta: 0 normal, 1 más de 5 min, 2 más de 15.</summary>
    private static int WaitLevel(SessionState s)
    {
        if (s.Status != SessionStatus.Waiting) return 0;
        var waiting = DateTime.UtcNow - s.StatusSinceUtc;
        return waiting >= WaitHigh ? 2 : waiting >= WaitWarn ? 1 : 0;
    }

    /// <summary>Texto de ayuda del anillo: uso del contexto y aviso si se acerca la compactación.</summary>
    private static string ContextTip(SessionState s)
    {
        if (s.ContextPct is not { } pct) return "";
        var tip = $"Contexto: {(int)Math.Round(pct)} %";
        if (s.ContextSize is { } size) tip += $" de {Format.Tokens(size)} tokens";
        if (pct >= 80) tip += "\nPronto compactará: valora /compact o /clear";
        return tip;
    }

    /// <summary>Coloca la ventana donde estaba, si esa posición sigue dentro de alguna pantalla; si no, arriba a la derecha.</summary>
    private void RestorePlacement()
    {
        Width = DefaultWidth;
        var scale = DesktopScaling;
        if (_settings.Left is { } left && _settings.Top is { } top)
        {
            var point = new PixelPoint((int)Math.Round(left * scale), (int)Math.Round(top * scale));
            if (Screens.All.Any(s => s.Bounds.Contains(point + new PixelPoint(50, 30))))
            {
                Position = point;
                return;
            }
        }
        var area = WorkArea(this);
        Position = new PixelPoint(area.Right - (int)Math.Round((DefaultWidth + 16) * scale), area.Y + (int)Math.Round(16 * scale));
    }

    /// <summary>Guarda posición, tamaño y preferencias para el próximo arranque.</summary>
    private void SavePlacement()
    {
        // En la pastilla se guarda la ventana de antes de minimizar: al abrir, la app arranca normal.
        var position = _vm.Mini ? _miniRestore : Position;
        var scale = DesktopScaling;
        _settings.Left = position.X / scale;
        _settings.Top = position.Y / scale;
        _settings.Width = Width;
        _settings.Sound = _vm.Sound;
        _settings.QuietUntilUtc = _vm.QuietUntil?.ToUniversalTime();
        _settings.FadeWhenIdle = _vm.FadeWhenIdle;
        (_settings.PillX, _settings.PillY) = _vm.PillSpot;
        _settings.PillScreen = _vm.PillScreen;
        _settings.PillDesign = _vm.PillDesignKey;
        _settings.Compact =_vm.Mini ? _miniFromCompact : _vm.Compact;
        _settings.Panel = _inPanel || _miniFromPanel;
        _panel?.SavePlacement();
        _settings.Design = _vm.Design;
        _settings.LightTheme = _vm.Light;
        _settings.LimitsOpen = _vm.LimitsOpen;
        _settings.ModelsOpen = _vm.ModelsOpen;
        _settings.SessionsOpen = _vm.SessionsOpen;
        try
        {
            JsonStore.Write(AppPaths.AppSettingsFile, _settings);
        }
        catch (Exception ex)
        {
            JsonStore.LogError("settings", ex);
        }
    }

    /// <summary>Alterna entre la vista completa y la compacta.</summary>
    private void Compact_Click(object? sender, RoutedEventArgs e) => _vm.Compact = !_vm.Compact;

    /// <summary>Arrastra la ventana desde la barra de título.</summary>
    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }

    /// <summary>Abre o cierra el menú de configuración.</summary>
    private void Settings_Click(object? sender, RoutedEventArgs e) => SettingsPopup.IsOpen = !SettingsPopup.IsOpen;

    /// <summary>Abre el editor de la barra de estado de la terminal.</summary>
    private async void TerminalLayout_Click(object? sender, RoutedEventArgs e)
    {
        SettingsPopup.IsOpen = false;
        await new TerminalLayoutWindow().ShowDialog(this);
    }

    /// <summary>Pasa al modo compacto desde el menú, cerrándolo antes.</summary>
    private void CompactFromMenu_Click(object? sender, RoutedEventArgs e)
    {
        SettingsPopup.IsOpen = false;
        _vm.Compact = true;
    }

    /// <summary>Aplica los cambios del menú: la transparencia, y el tema repintando las filas con la paleta nueva.</summary>
    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.FadeWhenIdle))
        {
            if (_vm.FadeWhenIdle) ScheduleFade();
            else Unfade();
            return;
        }
        if (e.PropertyName != nameof(MainViewModel.Design)) return;
        App.ApplyDesign(_vm.Design);
        _vm.ThemeApplied();
        Refresh();
        // Las gráficas del panel llevan el color de los datos, que cambia con el tema, y se pintan al recargar.
        _panel?.ForceReload();
    }

    /// <summary>Minimiza la ventana a la pastilla de la esquina, desde la vista completa o la compacta.</summary>
    private void Minimize_Click(object? sender, RoutedEventArgs e) => EnterMini();

    /// <summary>Activa el «no molestar» durante una hora.</summary>
    private void QuietHour_Click(object? sender, RoutedEventArgs e) => SetQuiet(DateTime.Now.AddHours(1));

    /// <summary>Activa el «no molestar» hasta mañana a las 8:00.</summary>
    private void QuietTomorrow_Click(object? sender, RoutedEventArgs e) => SetQuiet(DateTime.Today.AddDays(1).AddHours(8));

    /// <summary>Quita el «no molestar» antes de tiempo.</summary>
    private void QuietOff_Click(object? sender, RoutedEventArgs e) => SetQuiet(null);

    /// <summary>Pone o quita el «no molestar» y lo guarda en el momento, para que sobreviva a un cierre.</summary>
    private void SetQuiet(DateTime? until)
    {
        _vm.QuietUntil = until;
        SavePlacement();
    }

    /// <summary>Programa el paso a semitransparente si el ratón ya no está encima.</summary>
    private void ScheduleFade()
    {
        _fadeDelay.Stop();
        if (_vm.FadeWhenIdle) _fadeDelay.Start();
    }

    /// <summary>Vuelve semitransparente la ventana si sigue sin el ratón encima y sin menús abiertos.</summary>
    private void FadeIfIdle()
    {
        _fadeDelay.Stop();
        if (!_vm.FadeWhenIdle || IsPointerOver || SettingsPopup.IsOpen || MiniPanel.IsOpen) return;
        Opacity = FadedOpacity;
    }

    /// <summary>Devuelve la opacidad completa al pasar el ratón.</summary>
    private void Unfade()
    {
        _fadeDelay.Stop();
        if (Opacity < 1) Opacity = 1;
    }

    /// <summary>Cierra la app.</summary>
    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
