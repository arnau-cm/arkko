using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Panel del modo maximizado: el análisis del uso, en una ventana normal.</summary>
/// <remarks>
/// Es una ventana aparte porque la flotante es transparente y siempre encima. La flotante lo abre y lo oculta:
/// nunca se ven las dos a la vez. El tamaño y la posición se guardan en unidades lógicas, como los guarda la
/// ventana de WPF, que lee el mismo fichero de ajustes.
/// </remarks>
public partial class PanelWindow : Window
{
    private readonly PanelModel _model = new();
    private readonly DispatcherTimer _reload = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly DispatcherTimer _nameDelay = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly MainWindow _host;
    private readonly MainViewModel _home;
    private readonly AppSettings _settings;
    private WindowState _lastState = WindowState.Normal;
    private bool _closing;

    // El mismo objeto que los ajustes: lo que se cambia en la configuración se guarda con ellos.
    private readonly WorkSchedule _work;

    /// <summary>Constructor vacío para el cargador de XAML; el panel se crea desde la ventana flotante.</summary>
    public PanelWindow() : this(null!, new MainViewModel(), new AppSettings()) { }

    /// <summary>Crea el panel sobre la ventana flotante, que sigue llevando los datos, los avisos y los ajustes.</summary>
    /// <param name="home">Datos de la ventana flotante: la página de inicio y la configuración los comparten con ella.</param>
    public PanelWindow(MainWindow host, MainViewModel home, AppSettings settings)
    {
        InitializeComponent();
        _host = host;
        _home = home;
        _settings = settings;
        PinnableSections();
        _model.Period = (HistoryPeriod)Math.Clamp(settings.PanelPeriod, 0, 3);
        _model.MenuExpanded = settings.PanelMenuExpanded;
        _model.MenuHides = settings.PanelMenuHides;
        _model.IconLight = settings.AppIconLight;
        // Las dos teclas van sin texto: su ficha y su nombre dicen cuál es cuál.
        DiaryInk.Tip(IconDarkChoice, DiaryInk.Card("Tecla oscura", "El icono de serie de la app."));
        DiaryInk.Tip(IconLightChoice, DiaryInk.Card("Tecla clara", "La misma tecla en claro, como la de las notificaciones."));
        Avalonia.Automation.AutomationProperties.SetName(IconDarkChoice, "Tecla oscura");
        Avalonia.Automation.AutomationProperties.SetName(IconLightChoice, "Tecla clara");
        _model.ConfigCategory = Math.Clamp(settings.PanelConfigCategory, 0, 9);
        _model.AppVersion = home.AppVersion;
        _model.SetThresholds(settings.LimitThresholds);
        _work = settings.WorkHours;
        _model.SetWorkSchedule(_work);
        StatsPage.Init(_work);
        _model.WorkHoursOnly = settings.PanelWorkHoursOnly;
        DataContext = _model;
        HomePage.DataContext = home;
        ThemeHost.DataContext = home;
        AlertsHost.DataContext = home;
        FloatingHost.DataContext = home;
        PillHost.DataContext = home;
        PillMap.Model = home;
        UserNameBox.Text = settings.UserName ?? "";
        HomePage.IsVisible = _model.Section == PanelSection.Home;
        if (host is null) return;

        _model.ThresholdsChanged += (first, second) => _host.SetThresholds(first, second);
        _model.MenuHidesChanged += () => _host.SaveSettings();
        _model.IconChanged += () =>
        {
            _settings.AppIconLight = _model.IconLight;
            AppIcons.Apply(_model.IconLight);
            _host.SaveSettings();
        };
        _model.WorkHoursChanged += () =>
        {
            StatsPage.Reload();
            _host.SaveSettings();
        };
        DataSettings.Init(() => settings, host.RetentionSaved);
        SecurityPage.Init(host.Security);
        SecurityPage.SessionRequested += ShowSession;
        SecurityPage.SecretStepRequested += (id, secret) => ShowSession(id, secret);
        DenyRules.Init(settings, host.SaveSettings);
        DenyRules.StackAutoChanged += () =>
        {
            host.Vulnerabilities.RunIfDue();
            VulnerabilitiesPage.Activate();
        };
        SessionsPage.Init(() => host.OpenSessions, host.Attention, host.MarkReviewed, host.Security, settings, host.SaveSettings);
        SessionsPage.SecurityRequested += () => ShowSection(PanelSection.Security);
        ClocksPage.Init(home, () => host.OpenSessions);
        ClocksPage.SessionRequested += ShowSession;
        SessionsPage.DimRequested += on => Dim.IsVisible = on;
        Dim.PointerPressed += (_, _) =>
        {
            Dim.IsVisible = false;
            SessionsPage.CloseLightbox();
        };
        GroupColors.Init(settings, host.SaveSettings);
        GroupColors.Changed += SessionsPage.SendGroups;
        CodeThemes.Init(settings, host.SaveSettings);
        CodeThemes.Changed += SessionsPage.SendCode;
        FinalReplyFormat.Init(settings, host.SaveSettings);
        FinalReplyFormat.Changed += SessionsPage.SendFinalFormat;
        RepoColors.Init(settings, host.SaveSettings);
        RepoColors.Changed += SessionsPage.SendGroups;
        _nameDelay.Tick += (_, _) =>
        {
            _nameDelay.Stop();
            var name = UserNameBox.Text ?? "";
            settings.UserName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            _host.SaveSettings();
            SessionsPage.SendUser();
        };
        UserNameBox.TextChanged += (_, _) =>
        {
            _nameDelay.Stop();
            _nameDelay.Start();
        };
        _model.DefaultView = AppSettings.SessionsViewOf(settings.SessionsTab);
        _model.DefaultViewChanged += () =>
        {
            settings.SessionsTab = _model.DefaultView;
            _host.SaveSettings();
            SessionsPage.SendTab(fromSettings: true);
        };
        _model.SessionsAttention = home.AttentionCount;
        home.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.AttentionCount)) _model.SessionsAttention = home.AttentionCount;
            if (e.PropertyName == nameof(MainViewModel.Compact)) _host.SaveSettings();
        };
        _model.SecurityPending = host.Security.PendingCount;
        host.Security.PendingChanged += () => _model.SecurityPending = host.Security.PendingCount;
        var tokens = SecretPlanService.Instance;
        _model.TokensPending = (tokens.DueCount, tokens.SoonCount);
        tokens.Changed += () => _model.TokensPending = (tokens.DueCount, tokens.SoonCount);
        // Un análisis nuevo de Seguridad, o un secreto marcado allí, puede poner un token en «Toca cambiarlo ya» o quitarlo.
        host.Security.Changed += () =>
        {
            if (!host.Security.Running) tokens.CheckLeaks();
        };
        host.Security.PendingChanged += tokens.CheckLeaks;
        TokensPage.Init(host.Security);
        TokensPage.FindingRequested += fingerprint =>
        {
            ShowSection(PanelSection.Security);
            SecurityPage.ShowFinding(fingerprint);
        };
        SecurityPage.TokenRequested += id =>
        {
            ShowSection(PanelSection.Tokens);
            TokensPage.ShowToken(id);
        };
        _model.Vulnerabilities = (host.Vulnerabilities.Failures, host.Vulnerabilities.Unseen);
        host.Vulnerabilities.Changed += () => _model.Vulnerabilities = (host.Vulnerabilities.Failures, host.Vulnerabilities.Unseen);
        VulnerabilitiesPage.Init(host.Vulnerabilities, settings, host.SaveSettings);
        VulnerabilitiesPage.ConfigRequested += ShowSecurityConfig;
        TasksPage.Init(() => host.OpenSessions);
        TasksPage.SessionRequested += ShowSession;
        DiaryPage.SessionRequested += ShowSession;
        DiaryPage.TaskRequested += ShowTask;
        WorktreesPage.TaskRequested += ShowTask;
        SessionsPage.TaskRequested += ShowTask;
        TasksPage.ConfigureRequested += ShowBoardsConfig;
        _model.SectionChanged += OnSectionChanged;
        _model.PeriodChanged += () => StatsPage.Reload();
        _model.BreakdownChanged += () => StatsPage.ForceReload();
        _reload.Tick += (_, _) => StatsPage.Reload();
        // Cerrar el panel cierra la app, como la cruz de la ventana flotante. El cierre de la app va después:
        // hacerlo aquí mismo cerraría el panel mientras aún se está cerrando.
        Closing += (_, e) =>
        {
            if (_closing || _host.ShuttingDown) return;
            e.Cancel = true;
            Dispatcher.UIThread.Post(_host.Close);
        };
        Snapshot.WhenOpened(this, "panel");
        Snapshot.RunTour(this, ShowTourStep);
    }

    /// <summary>Pone a la vista un paso del recorrido de imágenes: una sección, o «config:N» para una categoría de la configuración.</summary>
    private bool ShowTourStep(string step)
    {
        if (step.StartsWith("config:", StringComparison.Ordinal) && int.TryParse(step[7..], out var category))
        {
            _model.ConfigCategory = category;
            _model.Section = PanelSection.Config;
            return true;
        }
        if (step == "codemenu")
        {
            _model.ConfigCategory = 9;
            _model.Section = PanelSection.Config;
            Dispatcher.UIThread.Post(async () =>
            {
                await Task.Delay(600);
                await CodeThemesSettings.SnapshotMenuAsync();
            });
            return true;
        }
        if (step == "security:ignored")
        {
            _model.Section = PanelSection.Security;
            SecurityPage.ShowIgnoredTab();
            return true;
        }
        if (step is "security:where" or "security:reveal")
        {
            _model.Section = PanelSection.Security;
            if (step == "security:where") SecurityPage.ShowWhereForTour();
            else SecurityPage.RevealForTour();
            return true;
        }
        if (step.StartsWith("live:", StringComparison.Ordinal) && Snapshot.Folder is { } liveFolder)
        {
            // Sigue una sesión en Sesiones y anota cada 3 s lo que tienen la app y la página, durante quince minutos.
            SessionsPage.Select(step[5..]);
            _model.Section = PanelSection.Sessions;
            Dispatcher.UIThread.Post(async () =>
            {
                for (var i = 0; i < 300; i++)
                {
                    await Task.Delay(3000);
                    File.AppendAllText(Path.Combine(liveFolder, "tour-live.txt"), $"{DateTime.Now:HH:mm:ss} {await SessionsPage.LiveProbeAsync()}\n");
                }
            });
            return true;
        }
        if (step == "unload")
        {
            SessionsPage.UnloadNow();
            return true;
        }
        if (step == "probe" && Snapshot.Folder is { } probeFolder)
        {
            Dispatcher.UIThread.Post(async () =>
                File.AppendAllText(Path.Combine(probeFolder, "tour-probe.txt"), $"{DateTime.Now:HH:mm:ss.f} {await SessionsPage.ProbeAsync()}\n"));
            return true;
        }
        if (step.StartsWith("diary:mode:", StringComparison.Ordinal) && Enum.TryParse<DiaryMode>(step[11..], ignoreCase: true, out var diaryMode))
        {
            _model.Section = PanelSection.Diary;
            DiaryPage.SetMode(diaryMode);
            return true;
        }
        if (step == "diary:cards")
        {
            _model.Section = PanelSection.Diary;
            DiaryPage.ShowSampleCards();
            return true;
        }
        if (step == "diary:picker")
        {
            _model.Section = PanelSection.Diary;
            var picker = DiaryPage.OpenPicker();
            Dispatcher.UIThread.Post(async () =>
            {
                await Task.Delay(800);
                Snapshot.Save(picker, "tour-diary-picker-popup");
            });
            return true;
        }
        if (step == "diary:prev")
        {
            _model.Section = PanelSection.Diary;
            DiaryPage.Previous();
            return true;
        }
        if (step is "diary:detail" or "diary:detail:folder" or "diary:detail:move")
        {
            _model.Section = PanelSection.Diary;
            DiaryPage.SelectFirstToday(withFolder: step.EndsWith(":folder", StringComparison.Ordinal), withMove: step.EndsWith(":move", StringComparison.Ordinal));
            return true;
        }
        if (step == "artifacts:first")
        {
            _model.Section = PanelSection.Artifacts;
            ArtifactsPage.SelectFirst();
            return true;
        }
        if (step.StartsWith("vulnerabilities:", StringComparison.Ordinal))
        {
            _model.Section = PanelSection.Vulnerabilities;
            VulnerabilitiesPage.ShowTourStep(step[16..]);
            return true;
        }
        if (step == "tokens:cards")
        {
            _model.Section = PanelSection.Tokens;
            TokensPage.ShowCardsTab();
            return true;
        }
        if (!Enum.TryParse<PanelSection>(step, ignoreCase: true, out var section)) return false;
        _model.Section = section;
        if (section == PanelSection.Sessions && Snapshot.Folder is { } folder)
            Dispatcher.UIThread.Post(async () =>
            {
                await Task.Delay(6000);
                File.WriteAllText(Path.Combine(folder, "tour-sessions.txt"), await SessionsPage.ProbeAsync());
            });
        return true;
    }

    /// <summary>Activa o detiene lo que depende de la sección que se ve.</summary>
    private void OnSectionChanged()
    {
        HomePage.IsVisible = _model.Section == PanelSection.Home;
        if (_model.Section != PanelSection.Home) SessionColumnPanel.Resync();
        if (_model.Section == PanelSection.Sessions) SessionsPage.Activate();
        else SessionsPage.Deactivate();
        if (_model.Section == PanelSection.Clocks) ClocksPage.Activate();
        else ClocksPage.Deactivate();
        if (_model.Section == PanelSection.Tasks) TasksPage.Activate();
        else TasksPage.Deactivate();
        if (_model.Section == PanelSection.Security) SecurityPage.Reload();
        if (_model.Section == PanelSection.Tokens) TokensPage.Activate();
        if (_model.Section == PanelSection.Vulnerabilities) VulnerabilitiesPage.Activate();
        if (_model.Section == PanelSection.Diary) DiaryPage.Activate();
        else DiaryPage.Deactivate();
        if (_model.Section == PanelSection.Worktrees) WorktreesPage.Activate();
        else WorktreesPage.Deactivate();
        if (_model.Section == PanelSection.Artifacts) ArtifactsPage.Activate();
        else ArtifactsPage.Deactivate();
        // Al entrar se relee y se repinta siempre: eliminar una sesión quita el texto de sus prompts sin cambiar
        // cuántos hay, y la recarga normal no lo vería.
        if (_model.Section == PanelSection.Stats)
        {
            StatsPage.ForceReload();
            _reload.Start();
        }
        else _reload.Stop();
        if (_model.Section != PanelSection.Config) return;
        DataSettings.Reload();
        TerminalEditor.Reload();
        DenyRules.Reload();
        BoardsConfig.Reload();
    }

    /// <summary>Atiende a mostrarse u ocultarse, y a minimizarse, que lo convierte en la pastilla como la flotante.</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_host is null) return;
        if (change.Property == IsVisibleProperty)
        {
            if (IsVisible)
            {
                // El histórico solo lo enseña Estadísticas: con otra sección a la vista no se relee.
                if (_model.Section == PanelSection.Stats)
                {
                    StatsPage.Reload();
                    _reload.Start();
                }
                if (_model.Section == PanelSection.Tasks) TasksPage.Activate();
                if (_model.Section == PanelSection.Worktrees) WorktreesPage.Activate();
                if (_model.Section == PanelSection.Artifacts) ArtifactsPage.Activate();
                if (_model.Section == PanelSection.Clocks) ClocksPage.Activate();
                if (_model.Section == PanelSection.Sessions) SessionsPage.Activate();
                // Sesiones se carga en segundo plano en cuanto el panel queda libre, no al entrar en ella.
                else Dispatcher.UIThread.Post(SessionsPage.Preload, DispatcherPriority.ApplicationIdle);
            }
            else
            {
                _reload.Stop();
                SessionsPage.Deactivate();
                ClocksPage.Deactivate();
                TasksPage.Deactivate();
                WorktreesPage.Deactivate();
                SessionColumnPanel.Resync();
            }
        }
        else if (change.Property == WindowStateProperty)
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = _lastState;
                _host.PanelToMini();
                return;
            }
            _lastState = WindowState;
            _model.Maximized = WindowState == WindowState.Maximized;
        }
    }

    /// <summary>Coloca el panel donde estaba, si esa posición sigue dentro de alguna pantalla; si no, centrado.</summary>
    public void RestorePlacement()
    {
        if (_settings.PanelWidth is { } w && _settings.PanelHeight is { } h)
        {
            Width = Math.Max(MinWidth, w);
            Height = Math.Max(MinHeight, h);
        }
        var scale = _host?.DesktopScaling ?? 1;
        if (_settings.PanelLeft is { } left && _settings.PanelTop is { } top)
        {
            var point = new PixelPoint((int)Math.Round(left * scale), (int)Math.Round(top * scale));
            if (Screens.All.Any(s => s.Bounds.Contains(point + new PixelPoint(50, 30))))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Position = point;
                return;
            }
        }
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    /// <summary>Recuerda posición, tamaño, si estaba maximizado, el periodo y cómo estaba el menú.</summary>
    public void SavePlacement()
    {
        if (WindowState == WindowState.Normal)
        {
            var scale = DesktopScaling;
            _settings.PanelLeft = Position.X / scale;
            _settings.PanelTop = Position.Y / scale;
            _settings.PanelWidth = Width;
            _settings.PanelHeight = Height;
        }
        _settings.PanelMaximized = WindowState == WindowState.Maximized;
        _settings.PanelPeriod = (int)_model.Period;
        _settings.PanelMenuExpanded = _model.MenuExpanded;
        _settings.PanelMenuHides = _model.MenuHides;
        _settings.PanelConfigCategory = _model.ConfigCategory;
        _settings.PanelWorkHoursOnly = _model.WorkHoursOnly;
    }

    // Las secciones que se clavan en un corcho, por el nombre de su elemento del menú lateral.
    private static readonly Dictionary<string, string> Pinnable = new()
    {
        ["Inicio"] = "home", ["Relojes"] = "clocks", ["Tablero"] = "board", ["Worktrees"] = "worktrees", ["Diario"] = "diary", ["Artifacts"] = "artifacts", ["Estadísticas"] = "stats",
    };

    /// <summary>Hace que las secciones que se pueden clavar (Inicio, Relojes, Tablero, Worktrees, Diario, Artifacts y Estadísticas) lo ofrezcan
    /// con el clic derecho en su elemento del menú lateral.</summary>
    private void PinnableSections()
    {
        foreach (var item in this.GetLogicalDescendants().OfType<RadioButton>())
            if (item.Content is string name && Pinnable.TryGetValue(name, out var kind))
                item.ContextRequested += (_, e) =>
                {
                    CorkWindow.PinMenu(NativeViews.Spec(kind)).Open(item);
                    e.Handled = true;
                };
    }

    /// <summary>Con el menú plegado, enseña la ficha de la sección al pasar por su icono; desplegado, el nombre ya se ve y no sale nada.</summary>
    /// <remarks>La ficha se monta al entrar el ratón, para que el dato de ahora (sesiones, secretos) esté al día.</remarks>
    private void NavItem_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is not RadioButton { Content: string name } item) return;
        if (_model.MenuExpanded || NavTipFor(name) is not { } tip)
        {
            ToolTip.SetTip(item, null);
            return;
        }
        ToolTip.SetTip(item, new ContentControl { Content = tip, ContentTemplate = this.FindResource("NavTipTemplate") as IDataTemplate });
    }

    /// <summary>Ficha de una sección del menú: qué hay en ella y, si lo hay, un dato de ahora; null si no es una sección conocida.</summary>
    private NavTip? NavTipFor(string name)
    {
        var danger = ThemeColors.Of("Danger", "#E03E4C");
        var faint = ThemeColors.Of("FgFaint", "#8A8D9C");
        var open = _host.OpenSessions;
        var waiting = open.Count(s => s.Status == SessionStatus.Waiting);
        var review = open.Count(s => _host.Attention(s).Review > 0);
        var agents = open.Count(s => s.Status == SessionStatus.Agents);
        var commands = open.Count(s => s.Status == SessionStatus.Command);
        var sessions = open.Count == 0 ? "Ninguna sesión abierta"
            : string.Join(" · ", new[]
            {
                open.Count == 1 ? "1 abierta" : $"{open.Count} abiertas",
                waiting == 0 ? null : waiting == 1 ? "1 te espera" : $"{waiting} te esperan",
                agents == 0 ? null : agents == 1 ? "1 espera a sus agentes" : $"{agents} esperan a sus agentes",
                commands == 0 ? null : commands == 1 ? "1 espera a un comando" : $"{commands} esperan a un comando",
                review == 0 ? null : $"{review} sin revisar",
            }.Where(p => p is not null));
        var sessionsBrush = waiting + review > 0 ? danger : faint;
        var pending = _model.SecurityPending;
        var (due, soon) = _model.TokensPending;

        return name switch
        {
            "Inicio" => new NavTip(name, "Las sesiones abiertas, lo que pide cada una y tus límites de uso.", sessions, sessionsBrush),
            "Sesiones" => new NavTip(name, "Cómo ha trabajado cada sesión: chat, secuencia y mapa de agentes, con repetición paso a paso.", sessions, sessionsBrush),
            "Tablero" => TaskDirectory.Instance.Boards().Count == 0
                ? new NavTip(name, "Las tareas de tu panel, unidas a las sesiones que las desarrollan y a lo que han costado.",
                    "Sin tablero: se conecta en Configuración → Tableros", faint)
                : new NavTip(name, "Las tareas de tu panel, unidas a las sesiones que las desarrollan y a lo que han costado."),
            "Worktrees" => WorktreesModel.LeftoverCount() is { } leftover
                ? new NavTip(name, "Las carpetas de cada rama de tus repositorios: lo que tienen sin subir, su PR y cuáles ya se pueden cerrar.",
                    leftover == 0 ? "Ninguno por cerrar" : leftover == 1 ? "1 worktree se puede cerrar" : $"{leftover} worktrees se pueden cerrar",
                    leftover == 0 ? faint : ThemeColors.Of("DataFiveHour", "#E5C07B"))
                : new NavTip(name, "Las carpetas de cada rama de tus repositorios: lo que tienen sin subir, su PR y cuáles ya se pueden cerrar."),
            "Diario" => new NavTip(name, "Lo que has hecho cada día con los agentes, como una agenda: sesiones, commits y tareas, con su parte para copiar."),
            "Artifacts" => new NavTip(name, "Lo que Claude ha publicado en claude.ai desde tus sesiones, por repositorio: páginas, documentos, presentaciones…",
                ArtifactMonitor.Instance.Loaded ? ArtifactTexts.Count(ArtifactMonitor.Instance.Items.Count(a => !a.Removed && !a.Deleted)) : null, faint),
            "Estadísticas" => new NavTip(name, "Consumo por día, proyecto, modelo y tarea, en el periodo que elijas."),
            "Seguridad" => new NavTip(name, "Secretos que han pasado por una conversación o están escritos en la configuración de Claude Code.",
                pending == 0 ? "Nada pendiente" : pending == 1 ? "1 secreto pendiente de cambiar" : $"{pending} secretos pendientes de cambiar",
                pending == 0 ? faint : danger),
            "Vulnerabilidades" => VulnerabilitiesTip(name, danger, faint),
            "Tokens" => new NavTip(name, "Tus tokens de Git, APIs y servicios: dónde se cambian, dónde se guardan y cuándo toca rotarlos.",
                due > 0 ? (due == 1 ? "1 token por cambiar hoy" : $"{due} tokens por cambiar hoy")
                : soon > 0 ? (soon == 1 ? "1 token por cambiar esta semana" : $"{soon} tokens por cambiar esta semana") : "Nada que cambiar",
                due > 0 ? danger : soon > 0 ? SecurityModel.WrittenBrush : faint),
            "Configuración" => new NavTip(name, "General, sesiones, tableros, avisos, vista flotante, terminal, datos y retención y seguridad."),
            _ => null,
        };
    }

    /// <summary>Ficha de «Vulnerabilidades» en el menú plegado: lo que falla y los avisos sin ver, o si el informe está viejo.</summary>
    private NavTip VulnerabilitiesTip(string name, IBrush danger, IBrush faint)
    {
        const string what = "Lo que tienes instalado con fallos de seguridad conocidos, en tu equipo y tus repos, y los avisos de seguridad que te tocan.";
        var (failures, unseen) = _model.Vulnerabilities;
        var amber = ThemeColors.Of("DataFiveHour", "#E5C07B");
        if (failures + unseen > 0)
            return new NavTip(name, what, string.Join(" · ", new[]
            {
                failures switch { 0 => null, _ => $"{failures} por actualizar" },
                unseen switch { 0 => null, 1 => "1 aviso por revisar", _ => $"{unseen} avisos por revisar" },
            }.Where(p => p is not null)), failures > 0 ? danger : amber);
        var report = _host.Vulnerabilities.Report;
        if (report is null) return new NavTip(name, what, "Sin informe de arkko-stack", faint);
        return StackSecurity.NewsStale(report, DateTime.UtcNow) || StackSecurity.ExposureStale(report, DateTime.UtcNow)
            ? new NavTip(name, what, "El informe está viejo", amber)
            : new NavTip(name, what, "Todo en orden", faint);
    }

    /// <summary>Abre la configuración de seguridad, donde están los ajustes de las vulnerabilidades.</summary>
    public void ShowSecurityConfig()
    {
        _model.ConfigCategory = 5;
        ShowSection(PanelSection.Config);
    }

    /// <summary>Abre la configuración de los tableros.</summary>
    public void ShowBoardsConfig()
    {
        _model.ConfigCategory = 7;
        ShowSection(PanelSection.Config);
    }

    /// <summary>Abre la sección Sesiones con una sesión concreta, como «Ver sesión» desde una tarjeta.</summary>
    public void ShowSession(string sessionId) => ShowSession(sessionId, null);

    /// <summary>Abre la sección Sesiones con una sesión y, desde Seguridad, en el paso donde apareció un secreto.</summary>
    /// <param name="secret">La referencia corta del secreto cuyo paso se enseña al abrirla; null para abrirla sin más.</param>
    public void ShowSession(string sessionId, string? secret)
    {
        if (secret is null) SessionsPage.Select(sessionId);
        else SessionsPage.SelectSecret(sessionId, secret);
        // Ya en Sesiones no hay cambio de sección que la active: se hace aquí.
        if (_model.Section == PanelSection.Sessions) SessionsPage.Activate();
        else _model.Section = PanelSection.Sessions;
    }

    /// <summary>Pasa a una sección del menú, como si se hubiera pulsado en él.</summary>
    /// <remarks>Si ya estaba en Seguridad, la relee: con el panel oculto, la sección no se actualiza.</remarks>
    public void ShowSection(PanelSection section)
    {
        if (_model.Section == section && section == PanelSection.Security) SecurityPage.Reload();
        _model.Section = section;
    }

    /// <summary>Abre el Tablero en una tarea, por su clave.</summary>
    public void ShowTask(string key)
    {
        _model.Section = PanelSection.Tasks;
        TasksPage.Focus(key);
    }

    /// <summary>Abre Artifacts con lo publicado desde un repositorio, o desde una de sus carpetas.</summary>
    public void ShowArtifacts(string group, string? project)
    {
        ArtifactsPage.Only(group, project);
        _model.Section = PanelSection.Artifacts;
    }

    /// <summary>Abre Artifacts con un artifact abierto en el visor, por su enlace.</summary>
    public void ShowArtifact(string url)
    {
        _model.Section = PanelSection.Artifacts;
        ArtifactsPage.Open(url);
    }

    /// <summary>Cierra el panel de verdad, al cerrar la app.</summary>
    public void CloseForShutdown()
    {
        _closing = true;
        Close();
    }

    /// <summary>Muestra un aviso en el pie del panel, como el resultado de una limpieza.</summary>
    public void ShowNotice(string text) => _model.Notice = text;

    /// <summary>Vuelve a leer los datos aunque no haya prompts nuevos: tras una limpieza pueden haber cambiado.</summary>
    public void ForceReload()
    {
        StatsPage.Forget();
        if (IsVisible) StatsPage.Reload();
    }

    /// <summary>Despliega o pliega el menú lateral a riel de iconos.</summary>
    private void ToggleMenu_Click(object? sender, RoutedEventArgs e) => _model.MenuExpanded = !_model.MenuExpanded;

    /// <summary>Mueve media hora el inicio o el fin del horario laboral; el Tag dice cuál y hacia dónde («start+», «end-»…).</summary>
    private void WorkShift_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not string tag) return;
        _model.ShiftWork(tag.StartsWith("start", StringComparison.Ordinal), tag.EndsWith('+') ? 1 : -1);
    }

    /// <summary>Exporta a CSV los prompts del periodo, en la carpeta que elija la persona.</summary>
    private async void Export_Click(object? sender, RoutedEventArgs e) => await StatsPage.ExportAsync();

    /// <summary>Vuelve a la ventana flotante.</summary>
    private void Floating_Click(object? sender, RoutedEventArgs e) => _host.PanelToFloating();

    /// <summary>Minimiza el panel a la pastilla de la esquina.</summary>
    private void Minimize_Click(object? sender, RoutedEventArgs e) => _host.PanelToMini();

    /// <summary>Minimiza el panel a la pastilla para ver dónde sale; un clic en ella vuelve aquí.</summary>
    private void PreviewPill_Click(object? sender, RoutedEventArgs e) => _host.PanelToMini();

    /// <summary>Devuelve la pastilla a su sitio de serie: abajo a la derecha del monitor de la ventana.</summary>
    private void ResetPill_Click(object? sender, RoutedEventArgs e) => _home.ResetPill();

    /// <summary>Alterna entre maximizado y su tamaño normal.</summary>
    private void Maximize_Click(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    /// <summary>Cierra la app entera.</summary>
    private void Close_Click(object? sender, RoutedEventArgs e) => _host.Close();

    /// <summary>Activa el «no molestar» durante una hora.</summary>
    private void QuietHour_Click(object? sender, RoutedEventArgs e) => _host.QuietForAnHour();

    /// <summary>Activa el «no molestar» hasta mañana a las 8:00.</summary>
    private void QuietTomorrow_Click(object? sender, RoutedEventArgs e) => _host.QuietUntilTomorrow();

    /// <summary>Quita el «no molestar».</summary>
    private void QuietOff_Click(object? sender, RoutedEventArgs e) => _host.QuietOff();
}

/// <summary>Secciones del panel.</summary>
public enum PanelSection
{
    Home,
    Stats,
    Config,
    Security,
    Sessions,
    Tasks,
    Tokens,
    Diary,
    Worktrees,
    Artifacts,
    Vulnerabilities,
    Clocks,
}

/// <summary>Aviso con número de un elemento del menú lateral: pastilla roja junto al nombre, o punto sobre el icono con el menú plegado.</summary>
/// <remarks>La plantilla del menú es la misma para todos los elementos; solo lleva aviso el que lo tenga puesto, y null lo oculta.</remarks>
public sealed class NavBadge : AvaloniaObject
{
    public static readonly AttachedProperty<string?> TextProperty = AvaloniaProperty.RegisterAttached<NavBadge, Control, string?>("Text");

    /// <summary>Un segundo aviso, en ámbar al lado del primero: lo que hay que mirar sin ser urgente.</summary>
    public static readonly AttachedProperty<string?> SecondProperty = AvaloniaProperty.RegisterAttached<NavBadge, Control, string?>("Second");

    /// <summary>Si lleva algún aviso, el primero o el segundo: con el menú plegado, enseña el punto.</summary>
    public static readonly AttachedProperty<bool> AnyProperty = AvaloniaProperty.RegisterAttached<NavBadge, Control, bool>("Any");

    /// <summary>Pone al día si hay algún aviso cada vez que cambia uno de los dos.</summary>
    static NavBadge()
    {
        TextProperty.Changed.AddClassHandler<Control>((c, _) => c.SetValue(AnyProperty, GetText(c) is not null || GetSecond(c) is not null));
        SecondProperty.Changed.AddClassHandler<Control>((c, _) => c.SetValue(AnyProperty, GetText(c) is not null || GetSecond(c) is not null));
    }

    public static string? GetText(Control c) => c.GetValue(TextProperty);
    public static void SetText(Control c, string? value) => c.SetValue(TextProperty, value);
    public static string? GetSecond(Control c) => c.GetValue(SecondProperty);
    public static void SetSecond(Control c, string? value) => c.SetValue(SecondProperty, value);
    public static bool GetAny(Control c) => c.GetValue(AnyProperty);
    public static void SetAny(Control c, bool value) => c.SetValue(AnyProperty, value);
}

/// <summary>Ficha de una sección del menú plegado: su nombre, qué hay en ella y, si lo hay, un dato de ahora.</summary>
/// <param name="Live">Lo que pasa ahora en la sección («2 abiertas · 1 te espera»); null si no hay nada que contar.</param>
/// <param name="LiveBrush">Color del punto del dato: rojo si pide atención, gris si es solo informativo.</param>
public sealed record NavTip(string Title, string Text, string? Live = null, IBrush? LiveBrush = null)
{
    public bool LiveVisible => Live is not null;
}

/// <summary>Cifras del tramo con el que se compara el periodo, y su inicio y final en hora local.</summary>
public sealed record PreviousSummary(HistorySummary Summary, DateTime From, DateTime To);

/// <summary>Una cifra clave del panel: su valor, su icono y color, y cómo ha ido en el periodo.</summary>
/// <param name="Trend">Valor de cada barra del histórico, para la mini gráfica; null es un hueco.</param>
public sealed record Kpi(string Value, string Label, string Glyph, IBrush Brush, IBrush Fill, IReadOnlyList<double?> Trend)
{
    /// <summary>Cambio frente al tramo anterior, con su flecha («▲ 23 %»); vacío si no hay con qué comparar.</summary>
    public string Delta { get; init; } = "";

    /// <summary>Con qué se compara, tras el cambio: «vs. ayer».</summary>
    public string DeltaNote { get; init; } = "";

    public IBrush? DeltaBrush { get; init; }
    public string? DeltaTip { get; init; }

    /// <summary>Sin comparación, la línea se oculta sin soltar su sitio.</summary>
    public double DeltaOpacity => Delta.Length > 0 ? 1 : 0;
}

/// <summary>Cursores de las filas que se pueden pulsar.</summary>
internal static class RowCursors
{
    public static readonly Cursor Hand = new(StandardCursorType.Hand);
    public static readonly Cursor Arrow = new(StandardCursorType.Arrow);
}

/// <summary>Una fila de «Prompts más caros»: cuándo, dónde, el inicio del texto, sus cifras y su ficha.</summary>
/// <param name="HasText">Si se guardó el texto del prompt; los anteriores a guardarlo no lo tienen.</param>
public sealed record CostlyRow(string When, string Project, string Preview, bool HasText, string Tokens, string Cache,
    string Duration, string Calls, PromptTip Tip)
{
    /// <summary>Carpeta del worktree, junto al proyecto; vacía si el prompt no venía de uno.</summary>
    public string Worktree { get; init; } = "";

    /// <summary>Puesto en la lista, del más caro (1) al más barato.</summary>
    public int Rank { get; init; }

    /// <summary>Sesión del prompt, que se abre en Sesiones al pulsar la fila; vacía si su transcript ya no existe.</summary>
    public string SessionId { get; init; } = "";

    public IBrush ProjectBrush { get; init; } = Brushes.Gray;
    public IBrush ProjectFill { get; init; } = Brushes.Transparent;

    /// <summary>Tokens nuevos de este prompt y del más caro, para la barra de calor de su columna.</summary>
    public double Heat { get; init; }
    public double HeatMax { get; init; }

    public double HeatFraction => HeatMax > 0 ? Math.Clamp(Heat / HeatMax, 0, 1) : 0;
    public bool CanOpen => SessionId.Length > 0;
    public Cursor RowCursor => CanOpen ? RowCursors.Hand : RowCursors.Arrow;
    public bool WorktreeVisible => Worktree.Length > 0;
    public string RankText => Rank.ToString(CultureInfo.InvariantCulture);
    public bool IsTop => Rank <= 3;
}

/// <summary>Ficha de un prompt: dónde y cuándo, el texto completo, los tokens por modelo y el resumen.</summary>
public sealed record PromptTip(string Title, string Where, string Text, IReadOnlyList<TipRow> Models, string Footer);

/// <summary>Chip de un día del horario laboral; si el cambio no se acepta, vuelve a como estaba.</summary>
public sealed class WorkDayOption(DayOfWeek day, bool isOn, Func<DayOfWeek, bool, bool> toggle) : INotifyPropertyChanged
{
    private bool _isOn = isOn;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Letter => WorkSchedule.Letter(day);
    public string Name => CultureInfo.GetCultureInfo("es-ES").DateTimeFormat.GetDayName(day);

    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (_isOn == value) return;
            if (toggle(day, value)) _isOn = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsOn)));
        }
    }
}

/// <summary>Una fila de «Por proyecto»: nombre, barra a escala del primero, cifras y su ficha.</summary>
public sealed record ProjectRow(string Name, double Value, double Max, string Tokens, string Pct, BarTip Tip)
{
    /// <summary>Color del proyecto: el punto, y el mismo color tenue en la barra que llena la fila.</summary>
    public IBrush Brush { get; init; } = Brushes.Gray;
    public IBrush Fill { get; init; } = Brushes.Transparent;

    /// <summary>Prompts y sesiones, en gris bajo el nombre.</summary>
    public string Detail { get; init; } = "";

    public double Fraction => Max > 0 ? Math.Clamp(Value / Max, 0, 1) : 0;

    /// <summary>Fila de un worktree, sangrada debajo de su repositorio.</summary>
    public bool IsWorktree { get; init; }

    /// <summary>Repositorio con desglose por worktree, que se abre y se cierra al pulsarlo.</summary>
    public bool Expandable { get; init; }
    public bool Expanded { get; init; }

    /// <summary>Clave de la tarea en «Por tarea», que el menú de la fila abre en el Tablero; null en «Por proyecto» y en «Otros».</summary>
    public string? TaskKey { get; init; }

    public bool IsTask => TaskKey != null;
    public Cursor RowCursor => Expandable ? RowCursors.Hand : RowCursors.Arrow;
    public string Chevron => Expanded ? "" : "";
    public Thickness Indent => IsWorktree ? new Thickness(20, 0, 0, 4) : new Thickness(0, 0, 0, 4);
}

/// <summary>Estado del panel: el periodo elegido y las cifras que se muestran.</summary>
public sealed class PanelModel : INotifyPropertyChanged
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
    private HistoryPeriod _period = HistoryPeriod.Week;
    private IReadOnlyList<Kpi> _kpis = [];
    private string _dataNote = "";
    private bool _maximized;
    private IReadOnlyList<BarModel> _bars = [];
    private IReadOnlyList<LegendItem> _legend = [];
    private IReadOnlyList<ProjectRow> _projects = [];
    private IReadOnlyList<ProjectUsage> _lastProjects = [];
    private IReadOnlyList<BarSegment> _projectShares = [];
    private readonly HashSet<string> _expandedProjects = new(StringComparer.OrdinalIgnoreCase);
    private string _axisTop = "";
    private string _axisMid = "";
    private IReadOnlyList<BarModel> _cacheBars = [];
    private IReadOnlyList<LineSeries> _limitSeries = [];
    private DateTime _limitsFrom;
    private DateTime _limitsTo;
    private string _notice = "";
    private IReadOnlyList<CostlyRow> _costliest = [];

    /// <summary>Cuántos prompts se listan en «Prompts más caros».</summary>
    public const int CostliestCount = 20;

    // Con más proyectos que estos, el resto se agrupa en «Otros»: la sección no tiene desplazamiento.
    private const int MaxProjects = 8;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Se dispara al cambiar de periodo, para recargar los datos.</summary>
    public event Action? PeriodChanged;

    /// <summary>Se dispara al cambiar entre «Proyectos» y «Tareas» en el reparto del consumo.</summary>
    public event Action? BreakdownChanged;

    private bool _byTask;

    /// <summary>El reparto del consumo va por tarea en lugar de por proyecto.</summary>
    public bool ByTask
    {
        get => _byTask;
        set
        {
            if (_byTask == value) return;
            _byTask = value;
            OnChanged();
            OnChanged(nameof(ByProject));
            OnChanged(nameof(BreakdownTitle));
            OnChanged(nameof(ProjectsEmptyText));
            BreakdownChanged?.Invoke();
        }
    }

    public bool ByProject
    {
        get => !_byTask;
        set => ByTask = !value;
    }

    public string BreakdownTitle => _byTask ? "POR TAREA" : "POR PROYECTO";
    public string ProjectsEmptyText => _byTask
        ? "Ningún prompt del periodo es de una carpeta con clave de tarea (como DEVLAB-99)"
        : "Sin prompts en este periodo";

    /// <summary>Se dispara al cambiar de sección.</summary>
    public event Action? SectionChanged;

    private PanelSection _section;
    private bool _menuExpanded = true;

    // Anchos del menú lateral: desplegado con nombres, o plegado a riel de iconos.
    private const double MenuWide = 210;
    private const double MenuRail = 60;

    public PanelSection Section
    {
        get => _section;
        set
        {
            if (_section == value) return;
            _section = value;
            foreach (var name in new[] { nameof(IsHome), nameof(IsStats), nameof(IsConfig), nameof(IsSecurity), nameof(IsSessions), nameof(IsTasks),
                         nameof(IsTokens), nameof(TokensVisible), nameof(TasksVisible), nameof(StatsVisible), nameof(ConfigVisible),
                         nameof(IsDiary), nameof(DiaryVisible), nameof(IsWorktrees), nameof(WorktreesVisible), nameof(IsArtifacts), nameof(ArtifactsVisible),
                         nameof(SecurityVisible), nameof(SessionsVisible), nameof(SectionTitle), nameof(TitleTextVisible),
                         nameof(IsVulnerabilities), nameof(VulnerabilitiesVisible), nameof(IsClocks), nameof(ClocksVisible) })
                OnChanged(name);
            SectionChanged?.Invoke();
        }
    }

    // Un booleano por elemento del menú: los RadioButton se enlazan a su sección.
    public bool IsHome { get => _section == PanelSection.Home; set { if (value) Section = PanelSection.Home; } }
    public bool IsStats { get => _section == PanelSection.Stats; set { if (value) Section = PanelSection.Stats; } }
    public bool IsConfig { get => _section == PanelSection.Config; set { if (value) Section = PanelSection.Config; } }
    public bool IsSecurity { get => _section == PanelSection.Security; set { if (value) Section = PanelSection.Security; } }
    public bool IsSessions { get => _section == PanelSection.Sessions; set { if (value) Section = PanelSection.Sessions; } }
    public bool IsClocks { get => _section == PanelSection.Clocks; set { if (value) Section = PanelSection.Clocks; } }
    public bool IsTasks { get => _section == PanelSection.Tasks; set { if (value) Section = PanelSection.Tasks; } }
    public bool IsTokens { get => _section == PanelSection.Tokens; set { if (value) Section = PanelSection.Tokens; } }
    public bool IsDiary { get => _section == PanelSection.Diary; set { if (value) Section = PanelSection.Diary; } }
    public bool IsWorktrees { get => _section == PanelSection.Worktrees; set { if (value) Section = PanelSection.Worktrees; } }
    public bool IsArtifacts { get => _section == PanelSection.Artifacts; set { if (value) Section = PanelSection.Artifacts; } }
    public bool IsVulnerabilities { get => _section == PanelSection.Vulnerabilities; set { if (value) Section = PanelSection.Vulnerabilities; } }

    public bool StatsVisible => _section == PanelSection.Stats;
    public bool ConfigVisible => _section == PanelSection.Config;
    public bool SecurityVisible => _section == PanelSection.Security;
    public bool SessionsVisible => _section == PanelSection.Sessions;
    public bool ClocksVisible => _section == PanelSection.Clocks;
    public bool TasksVisible => _section == PanelSection.Tasks;
    public bool TokensVisible => _section == PanelSection.Tokens;
    public bool DiaryVisible => _section == PanelSection.Diary;
    public bool WorktreesVisible => _section == PanelSection.Worktrees;
    public bool ArtifactsVisible => _section == PanelSection.Artifacts;
    public bool VulnerabilitiesVisible => _section == PanelSection.Vulnerabilities;

    private int _securityPending;

    /// <summary>Secretos pendientes de cambiar, para el aviso de «Seguridad» en el menú.</summary>
    public int SecurityPending
    {
        get => _securityPending;
        set
        {
            if (_securityPending == value) return;
            _securityPending = value;
            OnChanged(nameof(SecurityBadge));
        }
    }

    private int _sessionsAttention;

    /// <summary>Sesiones que esperan respuesta o están terminadas sin revisar, para el aviso de «Sesiones» en el menú.</summary>
    /// <remarks>Es el mismo número que el de la pastilla.</remarks>
    public int SessionsAttention
    {
        get => _sessionsAttention;
        set
        {
            if (_sessionsAttention == value) return;
            _sessionsAttention = value;
            OnChanged(nameof(SessionsBadge));
        }
    }

    /// <summary>Número del aviso de «Sesiones»; null si ninguna pide nada.</summary>
    public string? SessionsBadge => Badge(_sessionsAttention);

    /// <summary>Número del aviso de «Seguridad»; null sin pendientes, y la plantilla del menú lo oculta.</summary>
    public string? SecurityBadge => Badge(_securityPending);

    private int _tokensDue;
    private int _tokensSoon;

    /// <summary>Tokens que toca cambiar hoy y esta semana, para el aviso de «Tokens» en el menú.</summary>
    public (int Due, int Soon) TokensPending
    {
        get => (_tokensDue, _tokensSoon);
        set
        {
            if ((_tokensDue, _tokensSoon) == value) return;
            (_tokensDue, _tokensSoon) = value;
            OnChanged(nameof(TokensBadge));
            OnChanged(nameof(TokensBadgeSoon));
        }
    }

    /// <summary>Número del aviso de «Tokens»: lo de hoy y lo de esta semana; null si no toca nada.</summary>
    public string? TokensBadge => Badge(_tokensDue + _tokensSoon);

    /// <summary>Indica si el aviso de «Tokens» va en ámbar: solo toca algo esta semana, nada hoy.</summary>
    public bool TokensBadgeSoon => _tokensDue == 0 && _tokensSoon > 0;

    private (int Failures, int Unseen) _vulnerabilities;

    /// <summary>Comprobaciones del stack en FALLA y avisos sin ver, para los avisos de «Vulnerabilidades» en el menú.</summary>
    public (int Failures, int Unseen) Vulnerabilities
    {
        get => _vulnerabilities;
        set
        {
            if (_vulnerabilities == value) return;
            _vulnerabilities = value;
            OnChanged(nameof(VulnerabilitiesBadge));
            OnChanged(nameof(VulnerabilitiesUnseenBadge));
            OnChanged(nameof(VulnerabilitiesOnlyUnseen));
        }
    }

    /// <summary>El aviso rojo de «Vulnerabilidades»: lo que falla; null si nada.</summary>
    public string? VulnerabilitiesBadge => Badge(_vulnerabilities.Failures);

    /// <summary>El aviso ámbar de «Vulnerabilidades», al lado del rojo: los avisos sin ver; null si ninguno.</summary>
    public string? VulnerabilitiesUnseenBadge => Badge(_vulnerabilities.Unseen);

    /// <summary>Si solo hay avisos sin ver: el punto del menú plegado va en ámbar.</summary>
    public bool VulnerabilitiesOnlyUnseen => _vulnerabilities.Failures == 0 && _vulnerabilities.Unseen > 0;

    private static string? Badge(int count) => count switch
    {
        0 => null,
        > 99 => "99+",
        _ => count.ToString(),
    };

    public string SectionTitle => _section switch
    {
        PanelSection.Stats => "Estadísticas",
        PanelSection.Config => "Configuración",
        PanelSection.Security => "Seguridad",
        PanelSection.Sessions => "Sesiones",
        PanelSection.Clocks => "Relojes",
        PanelSection.Tasks => "Tablero",
        PanelSection.Tokens => "Tokens",
        PanelSection.Diary => "Diario",
        PanelSection.Worktrees => "Worktrees",
        PanelSection.Artifacts => "Artifacts",
        PanelSection.Vulnerabilities => "Vulnerabilidades",
        _ => "Inicio",
    };

    private string _defaultView = AppSettings.SessionsLive;

    /// <summary>Se dispara al cambiar la vista con la que se abre Sesiones.</summary>
    public event Action? DefaultViewChanged;

    /// <summary>Vista con la que se abre Sesiones: «live», el Flujo; «diagram», «now» o «parte».</summary>
    public string DefaultView
    {
        get => _defaultView;
        set
        {
            if (_defaultView == value) return;
            _defaultView = value;
            foreach (var name in new[] { nameof(ViewLive), nameof(ViewDiagram), nameof(ViewNow), nameof(ViewParte), nameof(ViewObra) }) OnChanged(name);
            DefaultViewChanged?.Invoke();
        }
    }

    public bool ViewLive { get => _defaultView == AppSettings.SessionsLive; set { if (value) DefaultView = AppSettings.SessionsLive; } }
    public bool ViewDiagram { get => _defaultView == AppSettings.SessionsDiagram; set { if (value) DefaultView = AppSettings.SessionsDiagram; } }
    public bool ViewNow { get => _defaultView == AppSettings.SessionsNow; set { if (value) DefaultView = AppSettings.SessionsNow; } }
    public bool ViewParte { get => _defaultView == AppSettings.SessionsParte; set { if (value) DefaultView = AppSettings.SessionsParte; } }
    public bool ViewObra { get => _defaultView == AppSettings.SessionsObra; set { if (value) DefaultView = AppSettings.SessionsObra; } }

    public bool MenuExpanded
    {
        get => _menuExpanded;
        set
        {
            _menuExpanded = value;
            OnChanged(nameof(MenuExpanded));
            OnChanged(nameof(MenuCollapsed));
            OnChanged(nameof(MenuWidth));
            OnChanged(nameof(MenuToggleTip));
            OnChanged(nameof(TitleMenuVisible));
        }
    }

    /// <summary>Inverso de MenuExpanded: con el menú plegado, los avisos van en un punto sobre el icono.</summary>
    public bool MenuCollapsed => !_menuExpanded;

    private bool _menuHides;
    private int _configCategory;

    /// <summary>Se dispara al cambiar cómo se pliega el menú, para guardarlo.</summary>
    public event Action? MenuHidesChanged;

    /// <summary>Al plegarlo, el menú desaparece del todo en lugar de quedarse en un riel de iconos.</summary>
    public bool MenuHides
    {
        get => _menuHides;
        set
        {
            _menuHides = value;
            OnChanged(nameof(MenuHides));
            OnChanged(nameof(MenuRailMode));
            OnChanged(nameof(MenuWidth));
            OnChanged(nameof(TitleMenuVisible));
            MenuHidesChanged?.Invoke();
        }
    }

    /// <summary>Inverso de MenuHides, para el chip «Riel de iconos».</summary>
    public bool MenuRailMode { get => !_menuHides; set { if (value) MenuHides = false; } }

    private bool _iconLight;

    /// <summary>Se dispara al cambiar el icono de la app, para aplicarlo y guardarlo.</summary>
    public event Action? IconChanged;

    /// <summary>Si el icono de la app es la tecla clara.</summary>
    public bool IconLight
    {
        get => _iconLight;
        set
        {
            if (_iconLight == value) return;
            _iconLight = value;
            OnChanged(nameof(IconLight));
            OnChanged(nameof(IconDark));
            IconChanged?.Invoke();
        }
    }

    /// <summary>Inverso de IconLight, para el chip «Tecla oscura».</summary>
    public bool IconDark { get => !_iconLight; set { if (value) IconLight = false; } }

    public double MenuWidth => _menuExpanded ? MenuWide : _menuHides ? 0 : MenuRail;

    /// <summary>Con el menú oculto del todo, su botón pasa a la barra de título para poder volver a abrirlo.</summary>
    public bool TitleMenuVisible => !_menuExpanded && _menuHides;

    /// <summary>El título de la sección en la barra; en la configuración va en su panel de categorías.</summary>
    public bool TitleTextVisible => _section != PanelSection.Config;

    /// <summary>Categoría abierta en la configuración: 0 general, 1 avisos, 2 vista flotante, 3 terminal, 4 datos, 5 seguridad, 6 sesiones, 7 tableros, 8 pastilla.</summary>
    public int ConfigCategory
    {
        get => _configCategory;
        set
        {
            _configCategory = value;
            foreach (var name in new[] { nameof(IsGeneral), nameof(IsAlerts), nameof(IsFloating), nameof(IsTerminal), nameof(IsData), nameof(IsSecurityConfig), nameof(IsSessionsConfig),
                         nameof(IsBoardsConfig), nameof(IsPillConfig), nameof(PillConfigVisible), nameof(GeneralVisible), nameof(AlertsVisible), nameof(FloatingVisible), nameof(TerminalVisible),
                         nameof(DataVisible), nameof(SecurityConfigVisible), nameof(SessionsConfigVisible), nameof(BoardsConfigVisible),
                         nameof(IsCodeConfig), nameof(CodeConfigVisible) })
                OnChanged(name);
        }
    }

    // Un booleano por categoría de la configuración, para sus botones.
    public bool IsGeneral { get => _configCategory == 0; set { if (value) ConfigCategory = 0; } }
    public bool IsAlerts { get => _configCategory == 1; set { if (value) ConfigCategory = 1; } }
    public bool IsFloating { get => _configCategory == 2; set { if (value) ConfigCategory = 2; } }
    public bool IsTerminal { get => _configCategory == 3; set { if (value) ConfigCategory = 3; } }
    public bool IsData { get => _configCategory == 4; set { if (value) ConfigCategory = 4; } }
    public bool IsSecurityConfig { get => _configCategory == 5; set { if (value) ConfigCategory = 5; } }
    public bool IsSessionsConfig { get => _configCategory == 6; set { if (value) ConfigCategory = 6; } }
    public bool IsBoardsConfig { get => _configCategory == 7; set { if (value) ConfigCategory = 7; } }
    public bool IsPillConfig { get => _configCategory == 8; set { if (value) ConfigCategory = 8; } }
    public bool IsCodeConfig { get => _configCategory == 9; set { if (value) ConfigCategory = 9; } }
    public bool GeneralVisible => _configCategory == 0;
    public bool AlertsVisible => _configCategory == 1;
    public bool FloatingVisible => _configCategory == 2;
    public bool TerminalVisible => _configCategory == 3;
    public bool DataVisible => _configCategory == 4;
    public bool SecurityConfigVisible => _configCategory == 5;
    public bool SessionsConfigVisible => _configCategory == 6;
    public bool BoardsConfigVisible => _configCategory == 7;
    public bool PillConfigVisible => _configCategory == 8;
    public bool CodeConfigVisible => _configCategory == 9;

    private bool _workHoursOnly;
    private WorkSchedule _work = new();

    /// <summary>Se dispara al poner o quitar el filtro de horario laboral o al cambiar el horario, para repintar y guardar.</summary>
    public event Action? WorkHoursChanged;

    /// <summary>Las estadísticas cuentan solo lo hecho en horario laboral.</summary>
    public bool WorkHoursOnly
    {
        get => _workHoursOnly;
        set
        {
            if (_workHoursOnly == value) return;
            _workHoursOnly = value;
            OnChanged();
            WorkHoursChanged?.Invoke();
        }
    }

    /// <summary>Si el filtro cuenta ahora: por ahora solo se ofrece en «Hoy», aunque el resto de periodos ya sabría aplicarlo.</summary>
    public bool WorkHoursActive => _workHoursOnly && _period == HistoryPeriod.Today;

    /// <summary>Fuera de «Hoy», el filtro se oculta sin soltar su sitio.</summary>
    public double WorkHoursOpacity => _period == HistoryPeriod.Today ? 1 : 0;

    public string WorkHoursTip => $"Cuenta solo lo hecho en horario laboral: {_work.Describe()}. El horario se cambia en Configuración → General.";

    /// <summary>Un chip por día de la semana, de lunes a domingo.</summary>
    public IReadOnlyList<WorkDayOption> WorkDays { get; private set; } = [];
    public string WorkStart => WorkSchedule.Time(_work.StartMinutes);
    public string WorkEnd => WorkSchedule.Time(_work.EndMinutes);

    /// <summary>Toma el horario guardado, que se edita en el sitio.</summary>
    public void SetWorkSchedule(WorkSchedule work)
    {
        _work = work;
        WorkDays = WorkSchedule.Order.Select(d => new WorkDayOption(d, work.Days.Contains(d), ToggleWorkDay)).ToList();
        OnChanged(nameof(WorkDays));
        OnWorkChanged();
    }

    /// <summary>Mueve el inicio o el fin media hora, sin que la franja quede vacía ni salga del día.</summary>
    public void ShiftWork(bool start, int steps)
    {
        var delta = steps * WorkSchedule.Step;
        if (start) _work.StartMinutes = Math.Clamp(_work.StartMinutes + delta, 0, _work.EndMinutes - WorkSchedule.Step);
        else _work.EndMinutes = Math.Clamp(_work.EndMinutes + delta, _work.StartMinutes + WorkSchedule.Step, 24 * 60);
        OnWorkChanged();
    }

    /// <summary>Pone o quita un día; el último que queda no se puede quitar, para que el filtro siempre tenga algo.</summary>
    private bool ToggleWorkDay(DayOfWeek day, bool on)
    {
        if (!on && _work.Days.Count == 1 && _work.Days.Contains(day)) return false;
        _work.Days.Remove(day);
        if (on) _work.Days.Add(day);
        OnWorkChanged();
        return true;
    }

    /// <summary>Avisa de un cambio del horario a la vista y al panel, que lo guarda y repinta.</summary>
    private void OnWorkChanged()
    {
        OnChanged(nameof(WorkStart));
        OnChanged(nameof(WorkEnd));
        OnChanged(nameof(WorkHoursTip));
        WorkHoursChanged?.Invoke();
    }

    /// <summary>Opciones de los dos umbrales de aviso de los límites.</summary>
    public IReadOnlyList<RetentionOption> FirstThresholds { get; private set; } = [];
    public IReadOnlyList<RetentionOption> SecondThresholds { get; private set; } = [];

    /// <summary>Se dispara al elegir otro umbral, con los dos valores.</summary>
    public event Action<int, int>? ThresholdsChanged;

    /// <summary>Monta las opciones de los umbrales con los guardados marcados.</summary>
    public void SetThresholds(IReadOnlyList<int> saved)
    {
        var first = saved.Count > 0 ? saved[0] : 80;
        var second = saved.Count > 1 ? saved[1] : 95;
        FirstThresholds = Options("first", [70, 75, 80, 85, 90], first);
        SecondThresholds = Options("second", [90, 95, 98], second);
        OnChanged(nameof(FirstThresholds));
        OnChanged(nameof(SecondThresholds));
    }

    /// <summary>Opciones de un umbral; si el guardado no es una de ellas, se añade.</summary>
    private IReadOnlyList<RetentionOption> Options(string group, int[] values, int selected)
    {
        var all = values.Contains(selected) ? values : [.. values, selected];
        var options = all.Order().Select(v => new RetentionOption(group, v + " %", v, v == selected)).ToList();
        foreach (var o in options)
            o.PropertyChanged += (_, _) =>
            {
                if (!o.IsSelected) return;
                var a = FirstThresholds.FirstOrDefault(x => x.IsSelected)?.Days ?? 80;
                var b = SecondThresholds.FirstOrDefault(x => x.IsSelected)?.Days ?? 95;
                ThresholdsChanged?.Invoke(a, b);
            };
        return options;
    }

    public string MenuToggleTip => _menuExpanded ? "Plegar el menú" : "Desplegar el menú";

    /// <summary>Versión de producto, al pie del menú.</summary>
    public string AppVersion { get; set; } = "";

    public HistoryPeriod Period
    {
        get => _period;
        set
        {
            if (_period == value) return;
            _period = value;
            OnChanged(nameof(IsToday));
            OnChanged(nameof(IsWeek));
            OnChanged(nameof(IsMonth));
            OnChanged(nameof(IsAll));
            OnChanged(nameof(WorkHoursOpacity));
            PeriodChanged?.Invoke();
        }
    }

    // Un booleano por botón de periodo: los RadioButton se enlazan a su opción.
    public bool IsToday { get => _period == HistoryPeriod.Today; set { if (value) Period = HistoryPeriod.Today; } }
    public bool IsWeek { get => _period == HistoryPeriod.Week; set { if (value) Period = HistoryPeriod.Week; } }
    public bool IsMonth { get => _period == HistoryPeriod.Month; set { if (value) Period = HistoryPeriod.Month; } }
    public bool IsAll { get => _period == HistoryPeriod.All; set { if (value) Period = HistoryPeriod.All; } }

    public IReadOnlyList<Kpi> Kpis { get => _kpis; private set { _kpis = value; OnChanged(); } }
    public IReadOnlyList<BarModel> Bars { get => _bars; private set { _bars = value; OnChanged(); OnChanged(nameof(HistoryEmptyVisible)); } }
    public IReadOnlyList<LegendItem> Legend { get => _legend; private set { _legend = value; OnChanged(); } }
    public IReadOnlyList<ProjectRow> Projects { get => _projects; private set { _projects = value; OnChanged(); OnChanged(nameof(ProjectsEmptyVisible)); } }

    /// <summary>Reparto del periodo entre los proyectos, para la barra de arriba de «Por proyecto».</summary>
    public IReadOnlyList<BarSegment> ProjectShares { get => _projectShares; private set { _projectShares = value; OnChanged(); } }
    public string AxisTop { get => _axisTop; private set { _axisTop = value; OnChanged(); } }
    public string AxisMid { get => _axisMid; private set { _axisMid = value; OnChanged(); } }
    public IReadOnlyList<BarModel> CacheBars { get => _cacheBars; private set { _cacheBars = value; OnChanged(); OnChanged(nameof(CacheEmptyVisible)); } }
    public IReadOnlyList<LineSeries> LimitSeries { get => _limitSeries; private set { _limitSeries = value; OnChanged(); OnChanged(nameof(LimitsEmptyVisible)); } }
    public IReadOnlyList<LegendItem> LimitLegend => _limitSeries.Select(s => new LegendItem(s.Name, s.Brush)).ToList();
    public DateTime LimitsFrom { get => _limitsFrom; private set { _limitsFrom = value; OnChanged(); } }
    public DateTime LimitsTo { get => _limitsTo; private set { _limitsTo = value; OnChanged(); } }
    public IReadOnlyList<CostlyRow> Costliest { get => _costliest; private set { _costliest = value; OnChanged(); OnChanged(nameof(CostliestEmptyVisible)); } }
    public bool CostliestEmptyVisible => _costliest.Count == 0;
    public bool CacheEmptyVisible => !_cacheBars.Any(b => b.Segments.Count > 0);
    public bool LimitsEmptyVisible => _limitSeries.Count == 0;

    /// <summary>Aviso del pie tras exportar; vacío, el pie muestra el origen de los datos.</summary>
    public string Notice
    {
        get => _notice;
        set
        {
            _notice = value;
            OnChanged();
            OnChanged(nameof(Footer));
        }
    }

    public string Footer => _notice.Length > 0 ? _notice : _dataNote;

    public bool HistoryEmptyVisible => !_bars.Any(b => b.Segments.Count > 0);
    public bool ProjectsEmptyVisible => _projects.Count == 0;
    public string DataNote { get => _dataNote; private set { _dataNote = value; OnChanged(); OnChanged(nameof(Footer)); } }

    public bool Maximized
    {
        get => _maximized;
        set
        {
            _maximized = value;
            OnChanged(nameof(MaximizeGlyph));
            OnChanged(nameof(MaximizeTip));
        }
    }

    /// <summary>Glifo del botón: maximizar o restaurar.</summary>
    public string MaximizeGlyph => _maximized ? "" : "";
    public string MaximizeTip => _maximized ? "Restaurar" : "Maximizar";

    /// <summary>Pinta las cifras clave de un periodo, con su evolución y la comparación con el tramo anterior, y el pie con el origen de los datos.</summary>
    /// <param name="series">Cifras de cada barra del histórico hasta ahora, para las mini gráficas.</param>
    /// <param name="previous">Cifras del tramo anterior y sus límites; null en «Todo».</param>
    public void Show(HistorySummary s, IReadOnlyList<SummaryBucket> series, PreviousSummary? previous, DateTime? firstDay)
    {
        Kpi Card(string value, string label, string glyph, string key, string fallback, Func<HistorySummary, double?> pick) =>
            new(value, label, glyph, ThemeColors.Of(key, fallback), ThemeColors.Of(key, fallback, 0.16), series.Select(b => pick(b.Summary)).ToList());

        var tokens = Card(Format.Tokens(s.NewTokens), "tokens nuevos", "", "Accent", "#F09A76", b => b.NewTokens);
        var prompts = Card(s.Prompts.ToString("N0", Es), s.Prompts == 1 ? "prompt" : "prompts", "", "DataSonnet", "#61AFEF", b => b.Prompts);
        var sessions = Card(s.Sessions.ToString("N0", Es), s.Sessions == 1 ? "sesión" : "sesiones", "", "DataFable", "#C678DD", b => b.Sessions);
        var working = Card(s.Working > TimeSpan.Zero ? Clock(s.Working) : "—", "Claude trabajando", "", "DataHaiku", "#98C379",
            b => b.Working.TotalMinutes);
        var cache = Card(s.CachePct is { } c ? Math.Round(c).ToString(Es) + " %" : "—", "de la entrada, leída de caché", "", "DataCache", "#56B6C2",
            b => b.CachePct);

        if (previous is { } p)
        {
            var before = p.Summary;
            tokens = Compare(tokens, s.NewTokens, before.NewTokens, Format.Tokens(before.NewTokens), p);
            prompts = Compare(prompts, s.Prompts, before.Prompts, before.Prompts.ToString("N0", Es), p);
            sessions = Compare(sessions, s.Sessions, before.Sessions, before.Sessions.ToString("N0", Es), p);
            working = Compare(working, s.Working.TotalMinutes, before.Working.TotalMinutes,
                before.Working > TimeSpan.Zero ? Clock(before.Working) : "nada", p);
            cache = Compare(cache, s.CachePct, before.CachePct, before.CachePct is { } bc ? Math.Round(bc).ToString(Es) + " %" : "sin datos", p,
                points: true);
        }
        Kpis = [tokens, prompts, sessions, working, cache];
        DataNote = firstDay is { } d
            ? $"Datos de los prompts terminados desde el {d.ToString("d 'de' MMMM", Es)}, guardados en este equipo."
            : "Todavía no hay prompts registrados.";
    }

    /// <summary>Pone en una cifra su cambio frente al tramo anterior: en % de lo que había, o en puntos si la cifra ya es un %.</summary>
    /// <remarks>
    /// Más o menos uso no es mejor ni peor, así que la flecha va en el color de la cifra; solo en la caché, donde
    /// subir es bueno, sube en verde y baja en ámbar. Un cambio pequeño se da por igual.
    /// </remarks>
    private Kpi Compare(Kpi kpi, double? now, double? before, string beforeText, PreviousSummary window, bool points = false)
    {
        if (now is not { } n || before is not { } b) return kpi;
        int sign;
        string text;
        if (points)
        {
            var diff = n - b;
            sign = Math.Abs(diff) < 1 ? 0 : Math.Sign(diff);
            text = sign == 0 ? "= igual" : $"{(sign > 0 ? "▲" : "▼")} {Math.Round(Math.Abs(diff)).ToString(Es)} pts";
        }
        else if (b <= 0)
        {
            if (n <= 0) return kpi;
            sign = 1;
            text = "▲ nuevo";
        }
        else
        {
            var pct = (n - b) / b * 100;
            sign = Math.Abs(pct) < 3 ? 0 : Math.Sign(pct);
            text = sign == 0 ? "= igual"
                : pct >= 200 ? $"▲ ×{(n / b).ToString("0.#", Es)}"
                : $"{(sign > 0 ? "▲" : "▼")} {Math.Round(Math.Abs(pct)).ToString(Es)} %";
        }
        var brush = sign == 0 ? ThemeColors.Of("FgFaint", "#8A8D9C")
            : !points ? kpi.Brush
            : sign > 0 ? ThemeColors.Of("DataHaiku", "#98C379") : ThemeColors.Of("DataFiveHour", "#E5C07B");
        var note = _period switch
        {
            HistoryPeriod.Today => "vs. ayer",
            HistoryPeriod.Week => "vs. 7 días antes",
            _ => "vs. 30 días antes",
        };
        var span = _period == HistoryPeriod.Today
            ? $"ayer hasta las {window.To:HH:mm}"
            : $"del {window.From.ToString("d 'de' MMMM", Es)} al {window.To.ToString("d 'de' MMMM', a las 'HH:mm", Es)}";
        return kpi with { Delta = text, DeltaNote = note, DeltaBrush = brush, DeltaTip = $"Antes: {beforeText}, {span}" };
    }

    /// <summary>Pinta el histórico: una barra por hora, día o semana, apilada por modelo, con su eje y su leyenda.</summary>
    public void ShowHistory(BucketSize size, IReadOnlyList<UsageBucket> buckets)
    {
        var max = NiceCeiling(buckets.Select(b => (double)b.Total).DefaultIfEmpty(0).Max());
        // Orden fijo de las series: el modelo que más pesa en el periodo, abajo.
        var models = buckets.SelectMany(b => b.ByModel)
            .GroupBy(m => m.Key)
            .OrderByDescending(g => g.Sum(m => m.Value))
            .Select(g => g.Key)
            .ToList();
        var step = size == BucketSize.Hour ? 3 : buckets.Count <= 10 ? 1 : (int)Math.Ceiling(buckets.Count / 8.0);

        Bars = buckets.Select((b, i) => new BarModel(
            i % step == 0 ? AxisLabel(b.Start, size) : "",
            models.Where(b.ByModel.ContainsKey).Select(m => new BarSegment(b.ByModel[m], ModelColors.For(m))).ToList(),
            max,
            new BarTip(
                TipTitle(b.Start, size),
                models.Where(m => b.ByModel.GetValueOrDefault(m) > 0)
                    .Select(m => new TipRow(m, Format.Tokens(b.ByModel[m]), ModelColors.For(m))).ToList(),
                b.Total > 0 ? Format.Tokens(b.Total) + " tokens nuevos" : "Sin uso"))).ToList();
        Legend = models.Select(m => new LegendItem(m, ModelColors.For(m))).ToList();
        AxisTop = max > 0 ? Format.Tokens((long)max) : "";
        AxisMid = max > 0 ? Format.Tokens((long)(max / 2)) : "";
    }

    /// <summary>Pinta «Por proyecto»: los que más gastan, con el resto agrupado en «Otros» y los worktrees de los repositorios abiertos.</summary>
    public void ShowProjects(IReadOnlyList<ProjectUsage> projects)
    {
        _lastProjects = projects;
        var shown = projects.Take(MaxProjects).ToList();
        var rest = projects.Skip(MaxProjects).ToList();
        if (rest.Count > 0)
        {
            shown.Add(new ProjectUsage($"Otros ({rest.Count})", rest.Sum(p => p.NewTokens), rest.Sum(p => p.Prompts),
                rest.Sum(p => p.Sessions), rest.Sum(p => p.Pct)));
        }
        var max = shown.Select(p => (double)p.NewTokens).DefaultIfEmpty(0).Max();
        var others = rest.Count > 0 ? shown[^1] : null;

        // En «Por tarea», la fila lleva la clave y, si se ha leído, el título de la tarea.
        var directory = ByTask ? TaskDirectory.Instance : null;
        string NameOf(ProjectUsage p) =>
            directory != null && p != others && directory.Find(p.Project) is { } found ? $"{p.Project} · {found.Task.Title}" : p.Project;

        ProjectRow Row(ProjectUsage p, string title, string colorOf, double fill) => new(
            NameOf(p),
            p.NewTokens,
            max,
            Format.Tokens(p.NewTokens),
            Math.Round(p.Pct).ToString(Es) + " %",
            new BarTip(title,
            [
                new("Tokens nuevos", Format.Tokens(p.NewTokens), Brushes.Transparent),
                new("Prompts", p.Prompts.ToString("N0", Es), Brushes.Transparent),
                new("Sesiones", p.Sessions.ToString("N0", Es), Brushes.Transparent),
            ], $"{p.Pct.ToString("0.#", Es)} % del periodo"))
        {
            TaskKey = directory != null && p != others ? p.Project : null,
            Brush = p == others ? ProjectColors.Others() : RepoColors.Of(colorOf),
            Fill = p == others ? ProjectColors.Others(fill) : RepoColors.Of(colorOf, fill),
            Detail = $"{p.Prompts.ToString("N0", Es)} {(p.Prompts == 1 ? "prompt" : "prompts")} · " +
                     $"{p.Sessions.ToString("N0", Es)} {(p.Sessions == 1 ? "sesión" : "sesiones")}",
        };

        var rows = new List<ProjectRow>();
        foreach (var p in shown)
        {
            var expanded = p.Worktrees.Count > 0 && _expandedProjects.Contains(p.Project);
            rows.Add(Row(p, NameOf(p), p.Project, 0.2) with { Expandable = p.Worktrees.Count > 0, Expanded = expanded });
            if (expanded) rows.AddRange(p.Worktrees.Select(w => Row(w, $"{p.Project} · {w.Project}", p.Project, 0.1) with { IsWorktree = true }));
        }
        Projects = rows;
        ProjectShares = rows.Where(r => !r.IsWorktree).Select(r => new BarSegment(r.Value, r.Brush)).ToList();
    }

    /// <summary>Abre o cierra el desglose por worktree de un repositorio.</summary>
    public void ToggleProject(string name)
    {
        if (!_expandedProjects.Remove(name)) _expandedProjects.Add(name);
        ShowProjects(_lastProjects);
    }

    /// <summary>Pinta la caché: el % de la entrada leído de caché en cada barra, de 0 a 100.</summary>
    public void ShowCache(BucketSize size, IReadOnlyList<CacheBucket> buckets)
    {
        var step = size == BucketSize.Hour ? 3 : buckets.Count <= 10 ? 1 : (int)Math.Ceiling(buckets.Count / 8.0);
        CacheBars = buckets.Select((b, i) => new BarModel(
            i % step == 0 ? AxisLabel(b.Start, size) : "",
            b.Pct is { } pct ? [new BarSegment(pct, CacheBrush)] : [],
            100,
            new BarTip(
                TipTitle(b.Start, size),
                b.Pct is { } p
                    ?
                    [
                        new TipRow("Leída de caché", Math.Round(p).ToString(Es) + " %", CacheBrush),
                        new TipRow("Lectura de caché", Format.Tokens(b.CacheRead), Brushes.Transparent),
                        new TipRow("Entrada total", Format.Tokens(b.Input), Brushes.Transparent),
                    ]
                    : [],
                b.Pct is null ? "Sin uso" : "Si baja de lo habitual, algo está invalidando la caché"))).ToList();
    }

    /// <summary>Pinta la evolución de cada límite en el periodo, desde su primera foto.</summary>
    /// <param name="work">Horario laboral para «Hoy»: la gráfica va solo por la franja, y vacía en un día no laborable.</param>
    public void ShowLimits(IReadOnlyList<LimitsSample> samples, DateTime? from, DateTime now, WorkSchedule? work = null)
    {
        var to = now;
        if (work is not null && from is { } day)
        {
            from = day.AddMinutes(work.StartMinutes);
            to = now < day.AddMinutes(work.EndMinutes) ? now : day.AddMinutes(work.EndMinutes);
            // La última foto de antes de la franja da el valor con que empieza la línea.
            var before = samples.LastOrDefault(s => s.WhenUtc.ToLocalTime() < from);
            samples = !work.IsWorkDay(day) || to <= from
                ? []
                : [.. before is null ? [] : new[] { before }, .. samples.Where(s => s.WhenUtc.ToLocalTime() >= from && s.WhenUtc.ToLocalTime() <= to)];
        }

        var series = samples
            .SelectMany(s => s.Limits.Select(l => (s.WhenUtc, l)))
            .GroupBy(x => x.l.Key)
            .OrderBy(g => LimitLabels.Order(g.Key))
            .Select(g => new LineSeries(
                g.First().l.Label,
                LimitBrush(g.Key),
                g.Select(x => (x.WhenUtc.ToLocalTime(), x.l.UsedPct)).OrderBy(p => p.Item1).ToList()))
            .ToList();
        LimitsFrom = from ?? (samples.Count > 0 ? samples.Min(s => s.WhenUtc).ToLocalTime() : now.Date);
        LimitsTo = to;
        LimitSeries = series;
        OnChanged(nameof(LimitLegend));
    }

    /// <summary>Pinta «Prompts más caros»: una fila por prompt, con su ficha.</summary>
    public void ShowCostliest(IReadOnlyList<PromptRecord> records)
    {
        var top = records.Select(r => (double)PromptSummary.NewTokens(r.Usage.Totals())).DefaultIfEmpty(0).Max();
        Costliest = records.Select((r, i) =>
        {
            var t = r.Usage.Totals();
            var local = r.WhenUtc.ToLocalTime();
            var preview = History.PromptPreview(r.Shown);
            var project = History.ProjectOf(r);
            var worktree = History.WorktreeOf(r);
            var where = worktree is null ? project : $"{project} · {worktree}";
            var duration = Clock(TimeSpan.FromMilliseconds(r.Usage.DurationMs));
            var calls = t.ApiCalls.ToString("N0", Es);
            return new CostlyRow(
                When(local),
                project,
                preview ?? "(texto no guardado: prompt anterior a guardarlo)",
                preview is not null,
                Format.Tokens(PromptSummary.NewTokens(t)),
                "+" + Format.Tokens(t.CacheRead),
                duration,
                calls,
                new PromptTip(
                    Capitalize(local.ToString("dddd d 'de' MMMM, HH:mm", Es)),
                    string.IsNullOrWhiteSpace(r.SessionName) ? where : $"{where} · {r.SessionName}",
                    r.Shown?.Trim() ?? "El texto de los prompts se guarda desde la versión que lo añadió; este es anterior.",
                    r.Usage.ByModel
                        .OrderByDescending(m => PromptSummary.NewTokens(m.Value))
                        .Select(m => new TipRow(Format.ModelName(m.Key), Format.Tokens(PromptSummary.NewTokens(m.Value)) + " tokens nuevos",
                            ModelColors.For(Format.ModelName(m.Key))))
                        .ToList(),
                    $"{duration} · {calls} llamadas a la API · +{Format.Tokens(t.CacheRead)} de caché"))
            {
                Worktree = worktree ?? "",
                Rank = i + 1,
                SessionId = r.TranscriptPath is { } path && File.Exists(path) ? r.SessionId : "",
                ProjectBrush = RepoColors.Of(project),
                ProjectFill = RepoColors.Of(project, 0.16),
                Heat = PromptSummary.NewTokens(t),
                HeatMax = top,
            };
        }).ToList();
    }

    /// <summary>Duración con espacio entre unidades y la segunda a dos cifras: «45s», «2m 05s», «1h 07m» o «2d 3h».</summary>
    /// <remarks>La de la statusline va sin espacios para ocupar poco; en el panel hay sitio y se lee mejor separada.</remarks>
    private static string Clock(TimeSpan span)
    {
        if (span.TotalMinutes < 1) return $"{(int)span.TotalSeconds}s";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}m {span.Seconds:00}s";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours}h {span.Minutes:00}m";
        return $"{(int)span.TotalDays}d {span.Hours}h";
    }

    /// <summary>Cuándo fue un prompt, corto: «hoy 14:05», «ayer 09:12» o «29 sep 18:40».</summary>
    private static string When(DateTime local)
    {
        var days = (DateTime.Today - local.Date).Days;
        var time = local.ToString("HH:mm", Es);
        return days switch
        {
            0 => "hoy " + time,
            1 => "ayer " + time,
            _ => Clean(local.ToString("d MMM", Es)) + " " + time,
        };
    }

    /// <summary>Nombre corto del periodo para el nombre del fichero exportado.</summary>
    public static string PeriodSlug(HistoryPeriod period) => period switch
    {
        HistoryPeriod.Today => "hoy",
        HistoryPeriod.Week => "7-dias",
        HistoryPeriod.Month => "30-dias",
        _ => "todo",
    };

    private static IBrush CacheBrush => ThemeColors.Of("DataCache", "#56B6C2");

    /// <summary>Color de cada límite en la gráfica: ámbar el de 5 h, turquesa el semanal, morado los demás.</summary>
    private static IBrush LimitBrush(string key) => key switch
    {
        "five_hour" => ThemeColors.Of("DataFiveHour", "#E5C07B"),
        "seven_day" => ThemeColors.Of("DataSevenDay", "#56B6C2"),
        _ => ThemeColors.Of("DataOtherLimit", "#C678DD"),
    };

    /// <summary>Etiqueta del eje de una barra: «14h», «lun 29» o «29 sep».</summary>
    private static string AxisLabel(DateTime start, BucketSize size) => size switch
    {
        BucketSize.Hour => $"{start.Hour}h",
        BucketSize.Week => Clean(start.ToString("d MMM", Es)),
        _ => Clean(start.ToString("ddd d", Es)),
    };

    /// <summary>Título de la ficha de una barra: la hora, el día o la semana completos.</summary>
    private static string TipTitle(DateTime start, BucketSize size) => size switch
    {
        BucketSize.Hour => $"{start:HH}:00 – {start.AddHours(1):HH}:00",
        BucketSize.Week => "Semana del " + start.ToString("d 'de' MMMM", Es),
        _ => Capitalize(start.ToString("dddd d 'de' MMMM", Es)),
    };

    /// <summary>Quita los puntos de las abreviaturas del castellano: «lun.» → «lun».</summary>
    private static string Clean(string text) => text.Replace(".", "");

    /// <summary>Pone en mayúscula la primera letra.</summary>
    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpper(text[0], Es) + text[1..];

    /// <summary>Redondea hacia arriba a un valor redondo para el eje: 1, 2, 2,5 o 5 por potencia de diez.</summary>
    private static double NiceCeiling(double value)
    {
        if (value <= 0) return 0;
        var power = Math.Pow(10, Math.Floor(Math.Log10(value)));
        foreach (var m in new[] { 1, 2, 2.5, 5, 10 })
            if (m * power >= value) return m * power;
        return 10 * power;
    }

    /// <summary>Avisa a la vista de un cambio de propiedad.</summary>
    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
