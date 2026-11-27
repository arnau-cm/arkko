using System.ComponentModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Sección Vulnerabilidades del panel: lo que tienes instalado con fallos conocidos y los avisos de seguridad, del
/// informe de arkko-stack, cada parte en su pestaña grande.</summary>
/// <remarks>
/// Lo instalado va en una tabla que se agrupa por dónde (el equipo y cada repo) o por tipo (programas, dependencias,
/// configuración); los avisos, como noticias con el protocolo en cuatro pasos. Cada fila se abre en su sitio.
/// </remarks>
public partial class VulnerabilitiesView : UserControl
{
    private readonly VulnerabilitiesModel _model = new();
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private StackSecurityMonitor? _monitor;

    /// <summary>Crea la sección vacía; se llena con Init.</summary>
    public VulnerabilitiesView()
    {
        InitializeComponent();
        DataContext = _model;
        _tick.Tick += (_, _) => Rebuild();
    }

    /// <summary>Se pide abrir Configuración → Seguridad, donde se deja a Arkko comprobar solo.</summary>
    public event Action? ConfigRequested;

    private Window? Owner => TopLevel.GetTopLevel(this) as Window;

    /// <summary>Conecta la sección con el vigilante del informe y con los ajustes, donde se recuerda cómo agrupar.</summary>
    public void Init(StackSecurityMonitor monitor, AppSettings settings, Action save)
    {
        _monitor = monitor;
        _model.UseSettings(settings, save);
        monitor.Changed += Rebuild;
        Rebuild();
    }

    /// <summary>Repinta al entrar en la sección: la antigüedad del informe cambia aunque el informe no.</summary>
    public void Activate() => Rebuild();

    /// <summary>Pone a la vista un paso del recorrido de imágenes: «machine», «news», «where», «kind», «ok», «open:N»
    /// (abre la fila N de la parte elegida), «run:ámbito» o «seen:id».</summary>
    public void ShowTourStep(string step)
    {
        Rebuild();
        if (step == "machine") _model.ShowMachine = true;
        else if (step == "news") _model.ShowNews = true;
        else if (step == "where") _model.ByWhere = true;
        else if (step == "kind") _model.ByKind = true;
        else if (step == "ok") _model.ShowOk = true;
        else if (step.StartsWith("search:", StringComparison.Ordinal)) _model.Query = step[7..];
        else if (step.StartsWith("open:", StringComparison.Ordinal) && int.TryParse(step[5..], out var n)) _model.OpenNth(n);
        else if (step.StartsWith("run:", StringComparison.Ordinal)) _ = _monitor?.RunAsync(step[4..]);
        else if (step.StartsWith("seen:", StringComparison.Ordinal)) _ = _monitor?.MarkSeenAsync([step[5..]]);
    }

    /// <summary>Vuelve a montar la sección con lo último del vigilante.</summary>
    /// <remarks>Mientras se comprueba, se repinta cada segundo para que corra el tiempo del botón; la tabla solo se rehace
    /// con un informe nuevo, así las filas abiertas y el desplazamiento no saltan.</remarks>
    private void Rebuild()
    {
        if (_monitor is null) return;
        _model.Load(_monitor);
        if (_monitor.Busy) _tick.Start();
        else _tick.Stop();
    }

    /// <summary>Comprueba la parte elegida: el equipo y los repos (1 o 2 minutos) o los avisos (segundos).</summary>
    private void Run_Click(object? sender, RoutedEventArgs e) =>
        _ = _monitor?.RunAsync(_model.ShowNews ? StackSecurity.ScopeNews : StackSecurity.ScopeExposure);

    /// <summary>Comprueba el equipo y los repos, el paso 2 del protocolo ante un aviso.</summary>
    private void CheckMachine_Click(object? sender, RoutedEventArgs e) => _ = _monitor?.RunAsync(StackSecurity.ScopeExposure);

    /// <summary>Abre Configuración → Seguridad.</summary>
    private void Config_Click(object? sender, RoutedEventArgs e) => ConfigRequested?.Invoke();

    /// <summary>Abre o cierra una fila de la tabla.</summary>
    private void Row_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is VulnRow row) _model.Toggle(row);
    }

    /// <summary>Abre o cierra un aviso.</summary>
    private void News_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is NewsRow row) _model.Toggle(row);
    }

    /// <summary>Enseña u oculta lo que está al día.</summary>
    private void ToggleOk_Click(object? sender, RoutedEventArgs e) => _model.ShowOk = !_model.ShowOk;

    /// <summary>Copia el arreglo de una comprobación.</summary>
    private async void CopyFix_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not VulnRow { Advice: { Length: > 0 } fix }) return;
        var text = StackSecurity.PlainFix(fix);
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
        }
        catch (Exception ex)
        {
            JsonStore.LogError("vulnerabilities-copy", ex);
        }
    }

    /// <summary>Abre en el navegador el enlace de un aviso, que el informe solo admite en https.</summary>
    private async void OpenLink_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not NewsRow { Link: { } link } || !Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return;
        try
        {
            if (TopLevel.GetTopLevel(this)?.Launcher is { } launcher) await launcher.LaunchUriAsync(uri);
        }
        catch (Exception ex)
        {
            JsonStore.LogError("vulnerabilities-link", ex);
        }
    }

    /// <summary>Marca un aviso como revisado, tras confirmar con el protocolo a la vista.</summary>
    /// <remarks>Lo hace <c>-Visto</c> de arkko-stack, y no se puede deshacer desde Arkko: por eso se confirma, y se recuerda
    /// que se marca al terminar el protocolo.</remarks>
    private async void MarkSeen_Click(object? sender, RoutedEventArgs e)
    {
        if (_monitor is null || (sender as Control)?.Tag is not NewsRow { Notice: var notice }) return;
        string[] steps =
        [
            "¿Es oficial? Que venga del fabricante, INCIBE-CERT, CISA o el aviso de GitHub del paquete.",
            "¿Te afecta? Que hayas comprobado si tienes una versión afectada.",
            "Si te afecta, que haya una tarea con qué se sube, dónde y con qué urgencia, con el aviso apuntado.",
            "Hecho: márcalo como revisado.",
        ];
        if (!await AppDialog.ConfirmAsync(Owner, "Marcar el aviso como revisado",
                $"{notice.Title} · {notice.Source}\n\nSe marca al terminar, no al leerlo. Una vez marcado deja de salir aquí y en Claude Code, y Arkko no lo puede desmarcar.",
                "Marcar como revisado", DialogTone.Warning, steps, note: $"check-security.ps1 -Visto {notice.Id}")) return;
        if (await _monitor.MarkSeenAsync([notice.Id]) is { } problem)
            await AppDialog.ShowAsync(Owner, "No se ha podido marcar el aviso", problem);
    }
}

/// <summary>Una línea del historial de una comprobación: el estado al que pasó y, debajo, cuándo y la frase de ese momento.</summary>
/// <param name="IsLast">La más antigua: no lleva línea hacia abajo.</param>
public sealed record VulnHistoryRow(string Title, string Sub, IBrush Brush, bool IsLast);

/// <summary>Un paso del protocolo ante un aviso.</summary>
public sealed record NoticeStep(string Number, string Title, string Text);

/// <summary>Una fuente de avisos al pie, con su estado.</summary>
public sealed record SourceDot(string Name, IBrush Brush, string Tip);

/// <summary>Base de las filas que se abren en su sitio.</summary>
public abstract class OpenableRow : INotifyPropertyChanged
{
    private bool _isOpen;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; init; } = "";

    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (_isOpen == value) return;
            _isOpen = value;
            Changed(nameof(IsOpen));
            Changed(nameof(Chevron));
            OpenChanged();
        }
    }

    /// <summary>La flecha de abrir o cerrar.</summary>
    public string Chevron => _isOpen ? "" : "";

    /// <summary>Avisa de lo que cambia al abrir o cerrar, además de la flecha.</summary>
    protected virtual void OpenChanged() { }

    protected void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Una fila de la tabla: la cabecera de un grupo o una comprobación, con lo que se ve al abrirla.</summary>
public sealed class VulnRow : OpenableRow
{
    public bool IsGroup { get; init; }
    public bool IsItem => !IsGroup;
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public IBrush PlaceBrush { get; init; } = Brushes.Gray;

    // CABECERA DE GRUPO
    public string GroupGlyph { get; init; } = "";
    public bool GroupGlyphVisible => GroupGlyph.Length > 0;
    public string GroupHelp { get; init; } = "";
    public bool GroupHelpVisible => GroupHelp.Length > 0;

    // COMPROBACIÓN
    public string KindText { get; init; } = "";
    public string Place { get; init; } = "";
    public string AvatarText { get; init; } = "";
    public IBrush AvatarBrush { get; init; } = Brushes.Gray;

    /// <summary>El logo de la pieza, si su dueño deja usarlo; si no, van las letras sobre su color.</summary>
    public VendorLogo? Logo { get; init; }
    public bool LogoVisible => Logo is not null;
    public bool LettersVisible => Logo is null;
    public IImage? LogoImage => Logo?.Image;
    public string? LogoCredit => Logo?.Credit;
    public string StateText { get; init; } = "";
    public IBrush DotBrush { get; init; } = Brushes.Gray;
    public IBrush StateFill { get; init; } = Brushes.Transparent;

    /// <summary>El icono de la pastilla en lugar del punto: el aviso de lo crítico.</summary>
    public string StateGlyph { get; init; } = "";
    public bool StateGlyphVisible => StateGlyph.Length > 0;
    public bool DotVisible => !StateGlyphVisible;

    /// <summary>El tinte de toda la fila y la franja a su izquierda: lo grave se ve sin mirar la pastilla.</summary>
    public IBrush RowTint { get; init; } = Brushes.Transparent;
    public bool StripeVisible { get; init; }
    public IBrush StripeBrush { get; init; } = Brushes.Transparent;

    /// <summary>La gravedad junto a «Al día»: lo que no corre prisa, pero existe.</summary>
    public string TagText { get; init; } = "";
    public IBrush TagBrush { get; init; } = Brushes.Gray;
    public bool TagVisible => TagText.Length > 0;
    public string When { get; init; } = "";
    public string Detail { get; init; } = "";

    /// <summary>Las versiones en QUÉ PASA, si dicen algo claro: la instalada, la unión, la otra versión o una nota.</summary>
    public string VersionFrom { get; init; } = "";
    public string VersionJoin { get; init; } = "";
    public string VersionTo { get; init; } = "";
    public string VersionRemark { get; init; } = "";
    public IBrush VersionBrush { get; init; } = Brushes.Gray;
    public bool VersionVisible => VersionFrom.Length > 0;
    public bool VersionToVisible => VersionTo.Length > 0;
    public bool VersionRemarkVisible => VersionRemark.Length > 0;
    public bool SentenceVisible => !VersionVisible;

    /// <summary>Si la frase del informe va en la fila abierta: cuando en la tabla salen las versiones y no es ya el texto de la tarjeta.</summary>
    public bool DetailLineVisible => VersionVisible && Detail.Length > 0 && AdviceText != Detail;
    public string SupportText { get; init; } = "";
    public bool SupportVisible => SupportText.Length > 0;
    public string? Advice { get; init; }
    public bool AdviceVisible => !string.IsNullOrWhiteSpace(Advice);

    /// <summary>El título de la tarjeta del arreglo, su icono y su texto: cómo se arregla, o por qué no hay nada que hacer.</summary>
    public string AdviceTitle { get; init; } = "";
    public string AdviceGlyph { get; init; } = "";
    public string AdviceText { get; init; } = "";

    /// <summary>El texto de la tarjeta en trozos: el código del arreglo se pinta aparte y se copia al pulsarlo.</summary>
    public IReadOnlyList<FixPart> AdviceParts { get; init; } = [];

    /// <summary>Cuándo se comprobó y su id, en una línea junto a «Copiar».</summary>
    public string MetaText { get; init; } = "";
    public IReadOnlyList<VulnHistoryRow> History { get; init; } = [];
    public bool HistoryVisible => History.Count > 0;

    /// <summary>El fondo de la fila: marcado mientras está abierta.</summary>
    public IBrush RowFill => IsOpen ? ThemeColors.Of("ButtonFill", "#14FFFFFF") : Brushes.Transparent;

    protected override void OpenChanged() => Changed(nameof(RowFill));
}

/// <summary>Un aviso de seguridad, como una noticia, con el protocolo al abrirlo.</summary>
public sealed class NewsRow : OpenableRow
{
    public required StackNotice Notice { get; init; }
    public string Day { get; init; } = "";
    public string Month { get; init; } = "";
    public string Source { get; init; } = "";
    public string SourceLetter { get; init; } = "";
    public IBrush SourceBrush { get; init; } = Brushes.Gray;
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public bool SubtitleVisible => Subtitle.Length > 0;
    public IReadOnlyList<string> Parts { get; init; } = [];
    public string StateText { get; init; } = "";
    public IBrush StateBrush { get; init; } = Brushes.Gray;
    public IBrush StateFill { get; init; } = Brushes.Transparent;
    public IReadOnlyList<NoticeStep> Steps { get; init; } = [];
    public bool StepsVisible { get; init; }
    public string ReviewedText { get; init; } = "";
    public string? Link { get; init; }
    public bool LinkVisible => Link is not null;
    public string LinkText { get; init; } = "Abrir el aviso";
    public bool CheckVisible { get; init; }
    public bool SeenVisible { get; init; }
}

/// <summary>Estado de la sección: las dos pestañas con su resumen, la tabla de lo instalado y los avisos.</summary>
public sealed class VulnerabilitiesModel : INotifyPropertyChanged
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    private const string GroupWhere = "where";
    private const string GroupKind = "kind";

    private StackSecurityMonitor? _monitor;
    private AppSettings? _settings;
    private Action? _save;
    private StackReportRead? _builtFor;
    private ILookup<string, StackHistoryEntry> _history = Array.Empty<StackHistoryEntry>().ToLookup(e => e.Id);
    private readonly HashSet<string> _open = [];
    private bool _newsOpened;

    private bool _showNews;
    private bool _showReviewed;
    private bool _showOk;
    private string _query = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Toma los ajustes, donde se recuerda cómo agrupar, y la forma de guardarlos.</summary>
    public void UseSettings(AppSettings settings, Action save) => (_settings, _save) = (settings, save);

    // PESTAÑAS
    public bool ShowMachine { get => !_showNews; set { if (value) SetTab(news: false); } }
    public bool ShowNews { get => _showNews; set { if (value) SetTab(news: true); } }
    public string MachineCount { get; private set; } = "–";
    public IBrush MachineCountBrush { get; private set; } = Grey;
    public string MachineCountLabel { get; private set; } = "";
    public string NewsCount { get; private set; } = "–";
    public IBrush NewsCountBrush { get; private set; } = Grey;
    public string NewsCountLabel { get; private set; } = "";

    // AVISO DE ARRIBA
    public string Notice { get; private set; } = "";
    public bool NoticeVisible => Notice.Length > 0;
    public IBrush NoticeBrush { get; private set; } = Brushes.Gray;
    public IBrush NoticeFill { get; private set; } = Brushes.Transparent;
    public bool NoticeConfigVisible { get; private set; }

    // ESTADO DE LA PARTE ELEGIDA
    public string AgeText { get; private set; } = "";
    public string AgeHint { get; private set; } = "";
    public IBrush AgeBrush { get; private set; } = Grey;
    public string OriginTip { get; private set; } = "";
    public bool RunEnabled { get; private set; }
    public string RunText { get; private set; } = "";
    public string RunTip { get; private set; } = "";

    // EQUIPO Y REPOS
    public bool ByWhere { get => Group == GroupWhere; set { if (value) SetGroup(GroupWhere); } }
    public bool ByKind { get => Group == GroupKind; set { if (value) SetGroup(GroupKind); } }
    private string Group => _settings?.VulnerabilitiesGroup == GroupKind ? GroupKind : GroupWhere;
    public string Query { get => _query; set { if (_query == value) return; _query = value; BuildRows(); } }
    public bool ShowOk { get => _showOk; set { if (_showOk == value) return; _showOk = value; BuildRows(); } }
    public IReadOnlyList<VulnRow> Rows { get; private set; } = [];
    public bool MachineTableVisible => !_showNews && _monitor?.Report is not null;
    public string TableEmptyText { get; private set; } = "";
    public bool TableEmptyVisible => TableEmptyText.Length > 0;
    public bool OkFooterVisible { get; private set; }
    public string OkText { get; private set; } = "";
    public IBrush OkBrush => Green;
    public string OkToggleText => _showOk ? "Ocultar" : "Ver";

    // AVISOS
    public bool ShowPending { get => !_showReviewed; set { if (value) SetReviewed(false); } }
    public bool ShowReviewed { get => _showReviewed; set { if (value) SetReviewed(true); } }
    public string PendingChip { get; private set; } = "Por revisar";
    public string ReviewedChip { get; private set; } = "Revisados";
    public IReadOnlyList<NewsRow> News { get; private set; } = [];
    public bool NewsListVisible => _showNews && _monitor?.Report is not null;
    public string NewsEmptyText { get; private set; } = "";
    public bool NewsEmptyVisible => NewsEmptyText.Length > 0;
    public IReadOnlyList<SourceDot> Sources { get; private set; } = [];
    public bool SourcesVisible => Sources.Count > 0;

    // SIN INFORME
    public bool EmptyVisible => _monitor is { Report: null };
    public string EmptyGlyph => "";
    public IBrush EmptyBrush => Amber;
    public string EmptyTitle { get; private set; } = "";
    public string EmptyText { get; private set; } = "";

    // COLORES
    private static IBrush Critical => ThemeColors.Of("DangerStrong", "#B0172B");
    private static IBrush CriticalFill => ThemeColors.Of("DangerStrong", "#B0172B", 0.16);
    private static IBrush CriticalTint => ThemeColors.Of("DangerStrong", "#B0172B", 0.14);
    private static IBrush HighTint => ThemeColors.Of("Danger", "#E03E4C", 0.07);
    private static IBrush Red => ThemeColors.Of("Danger", "#E03E4C");
    private static IBrush RedFill => ThemeColors.Of("Danger", "#E03E4C", 0.14);
    private static IBrush Amber => ThemeColors.Of("DataFiveHour", "#E5C07B");
    private static IBrush AmberFill => ThemeColors.Of("DataFiveHour", "#E5C07B", 0.14);
    private static IBrush Grey => ThemeColors.Of("FgFaint", "#A5A9B8");
    private static IBrush GreyFill => ThemeColors.Of("FgFaint", "#A5A9B8", 0.12);
    private static readonly IBrush Green = new ImmutableSolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x50));
    private static readonly IBrush GreenFill = new ImmutableSolidColorBrush(Color.FromArgb(0x24, 0x3F, 0xB9, 0x50));

    /// <summary>Monta la sección con lo último del vigilante; la tabla y los avisos solo se rehacen con un informe nuevo.</summary>
    public void Load(StackSecurityMonitor monitor)
    {
        _monitor = monitor;
        if (!ReferenceEquals(_builtFor, monitor.Last))
        {
            _builtFor = monitor.Last;
            _history = ReadHistory();
            BuildRows();
            BuildNews();
        }
        else if (News.Any(n => n.CheckVisible != CanCheck(n.Notice) || n.SeenVisible != CanMark(n.Notice)))
            BuildNews();
        LoadCounts(monitor);
        LoadStatus(monitor);
        LoadNotice(monitor);
        Notify(nameof(MachineCount), nameof(MachineCountBrush), nameof(MachineCountLabel), nameof(NewsCount), nameof(NewsCountBrush),
            nameof(NewsCountLabel), nameof(Notice), nameof(NoticeVisible), nameof(NoticeBrush), nameof(NoticeFill), nameof(NoticeConfigVisible),
            nameof(AgeText), nameof(AgeHint), nameof(AgeBrush), nameof(OriginTip), nameof(RunEnabled), nameof(RunText), nameof(RunTip),
            nameof(MachineTableVisible), nameof(NewsListVisible), nameof(EmptyVisible), nameof(EmptyTitle), nameof(EmptyText));
    }

    /// <summary>El número grande de cada pestaña: lo que hay que actualizar y los avisos por revisar.</summary>
    private void LoadCounts(StackSecurityMonitor monitor)
    {
        if (monitor.Report is null)
        {
            (MachineCount, MachineCountBrush, MachineCountLabel) = ("–", Grey, "sin informe");
            (NewsCount, NewsCountBrush, NewsCountLabel) = ("–", Grey, "sin informe");
            return;
        }
        var failures = monitor.Failures;
        var unseen = monitor.Unseen;
        (MachineCount, MachineCountBrush, MachineCountLabel) = failures > 0 ? (failures.ToString(Es), Red, "por actualizar") : ("0", Green, "todo al día");
        (NewsCount, NewsCountBrush, NewsCountLabel) = unseen > 0 ? (unseen.ToString(Es), Amber, "por revisar") : ("0", Green, "nada por revisar");
    }

    /// <summary>La línea de estado de la parte elegida: cuándo se comprobó, si está vieja y su botón.</summary>
    private void LoadStatus(StackSecurityMonitor monitor)
    {
        var report = monitor.Report;
        var now = DateTime.UtcNow;
        if (_showNews)
        {
            var date = report?.News?.Date;
            AgeText = date is null ? "Los avisos nunca se han buscado" : $"Avisos buscados {At(date)}";
            var stale = StackSecurity.NewsStale(report, now);
            AgeHint = date is null ? "" : stale ? "· tienen más de un día" : "· se buscan de nuevo cada día";
            AgeBrush = date is null ? Grey : stale ? Amber : Green;
        }
        else
        {
            var date = report?.Exposure?.Date;
            AgeText = date is null ? "Tu equipo nunca se ha comprobado" : $"Comprobado {At(date)}";
            var stale = StackSecurity.ExposureStale(report, now);
            AgeHint = date is null ? "" : stale ? "· tiene más de 7 días" : "· se da por viejo a los 7 días";
            AgeBrush = date is null ? Grey : stale ? Amber : Green;
        }
        var by = report?.Origin?.LaunchedBy switch
        {
            null or "" => "",
            "manual" => ", lanzado a mano",
            "arkko" => ", lanzado por Arkko",
            var other => $", lanzado por {other}",
        };
        OriginTip = (report is null ? "" : $"Informe de {report.Origin?.Machine ?? "este equipo"}{by} · arkko-stack, esquema {report.Schema}\n")
                    + (monitor.Last?.Path ?? StackSecurity.ReportFile());

        RunEnabled = StackSecurityMonitor.CanRun && !monitor.Busy;
        if (monitor.MarkingSeen) RunText = "Marcando como revisado…";
        else if (monitor.RunningScope is { } scope)
        {
            var elapsed = DateTime.UtcNow - (monitor.RunStartedUtc ?? DateTime.UtcNow);
            var what = scope == StackSecurity.ScopeNews ? "Buscando avisos" : "Comprobando";
            RunText = $"{what}… {(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";
        }
        else RunText = _showNews ? "Buscar avisos · segundos" : "Comprobar mi equipo · 1 o 2 min";
        RunTip = !StackSecurityMonitor.CanRun ? WhyNoScript()
            : _showNews ? "Lee los avisos de INCIBE-CERT, CISA y Node.js por HTTPS. Sale a internet; no envía nada tuyo."
            : "Mira lo instalado en el equipo y pasa npm audit y dotnet list package en tus repos, que mandan a npm y NuGet la lista de paquetes. Sale a internet; no envía código ni secretos.";
        EmptyTitle = monitor.Last?.Status is StackReportStatus.Unreadable or StackReportStatus.UnknownSchema ? "No se puede leer el informe" : "Sin informe de seguridad todavía";
        EmptyText = StackSecurityMonitor.CanRun ? "Pulsa «Comprobar mi equipo» o «Buscar avisos» para hacer el primero." : WhyNoScript();
    }

    /// <summary>El aviso de arriba: lo que impide leer o comprobar, la última comprobación que falló o el informe viejo.</summary>
    private void LoadNotice(StackSecurityMonitor monitor)
    {
        NoticeConfigVisible = false;
        var now = DateTime.UtcNow;
        var report = monitor.Report;
        var settings = _settings;
        if (monitor.RunProblem is { } problem) Set("La última comprobación no ha terminado. " + problem, red: true);
        else if (monitor.Last is { Status: StackReportStatus.Unreadable or StackReportStatus.UnknownSchema, Problem: { } why }) Set(why, red: true);
        else if (monitor.RunNote is { } note) Set(note, red: false);
        else if (report is not null && settings is { StackAutoCheck: false } && StackSecurityMonitor.CanRun
                 && (StackSecurity.NewsStale(report, now) || StackSecurity.ExposureStale(report, now)))
        {
            Set("Una parte del informe se ha quedado vieja y Arkko no la comprueba solo. Pulsa su botón, o deja que lo haga él en Configuración → Seguridad.", red: false);
            NoticeConfigVisible = true;
        }
        else if (!StackSecurityMonitor.CanRun && report is not null) Set(WhyNoScript() + " Se ve el último informe que dejó arkko-stack.", red: false);
        else Notice = "";

        void Set(string text, bool red)
        {
            Notice = text;
            NoticeBrush = red ? Red : Amber;
            NoticeFill = red ? RedFill : AmberFill;
        }
    }

    /// <summary>Por qué Arkko no puede lanzar la comprobación, en una línea.</summary>
    private static string WhyNoScript()
    {
        if (!OperatingSystem.IsWindows()) return "La comprobación de arkko-stack solo existe para Windows por ahora.";
        if (StackSecurity.StackHome() is null) return "Arkko no encuentra arkko-stack: falta la variable ARKKO_STACK_HOME.";
        return "Arkko no encuentra scripts\\check-security.ps1 en arkko-stack: actualízalo.";
    }

    // TABLA DE LO INSTALADO

    /// <summary>Monta la tabla: lo que no está al día (y, si se pide, lo que sí), filtrado por la búsqueda y agrupado.</summary>
    private void BuildRows()
    {
        var report = _monitor?.Report;
        var checks = report?.Checks ?? [];
        var query = _query.Trim();
        bool Matches(StackCheck c) => query.Length == 0 || new[] { c.Name, c.Object, c.Detail, c.Id, KindOf(c).Name, SeverityLook(c.SeverityRank)?.Name }
            .Any(s => s?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true);
        var shown = checks.Where(c => (_showOk || c.State != StackSecurity.Ok) && Matches(c)).ToList();

        var rows = new List<VulnRow>();
        if (report is not null)
        {
            var groups = Group == GroupKind
                ? shown.GroupBy(c => KindOf(c).Order).OrderBy(g => g.Key).Select(g => (Head: KindHead(KindOf(g.First())), Items: g.ToList()))
                : shown.GroupBy(c => c.OfMachine ? "" : c.Object!).OrderBy(g => g.Key.Length > 0).ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
                    .Select(g => (Head: WhereHead(g.Key), Items: g.ToList()));
            foreach (var (head, items) in groups)
            {
                rows.Add(head(items));
                foreach (var check in items.OrderBy(Urgency).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase))
                    rows.Add(CheckRow(report, check));
            }
        }
        Rows = rows;
        var ok = checks.Count(c => c.State == StackSecurity.Ok);
        OkFooterVisible = ok > 0 && query.Length == 0;
        OkText = ok == 1 ? "1 componente al día" : $"{ok} componentes al día";
        TableEmptyText = rows.Count > 0 ? ""
            : query.Length > 0 ? $"Nada coincide con «{query}»."
            : "Nada que actualizar: todo lo comprobado está al día.";
        Notify(nameof(Rows), nameof(OkFooterVisible), nameof(OkText), nameof(OkToggleText), nameof(TableEmptyText), nameof(TableEmptyVisible));
    }

    /// <summary>La cabecera de un grupo por dónde: el equipo, o un repo con su color.</summary>
    private static Func<List<StackCheck>, VulnRow> WhereHead(string repo) => items => new VulnRow
    {
        Key = "group:" + repo,
        IsGroup = true,
        Title = repo.Length == 0 ? "Este equipo" : repo,
        Subtitle = "· " + GroupCount(items),
        PlaceBrush = repo.Length == 0 ? Grey : RepoColors.Of(repo),
    };

    /// <summary>La cabecera de un grupo por tipo, con lo que suele hacer falta para arreglarlo.</summary>
    private static Func<List<StackCheck>, VulnRow> KindHead(CheckKind kind) => items => new VulnRow
    {
        Key = "group:" + kind.Name,
        IsGroup = true,
        Title = kind.Group,
        Subtitle = "· " + GroupCount(items),
        GroupGlyph = kind.Glyph,
        GroupHelp = kind.Help,
    };

    /// <summary>«3 por actualizar · 1 sin comprobar», o «al día».</summary>
    private static string GroupCount(List<StackCheck> items)
    {
        var parts = new List<string>();
        if (items.Count(c => c.Failing) is > 0 and var f) parts.Add($"{f} por actualizar");
        if (items.Count(c => c.Unchecked) is > 0 and var u) parts.Add($"{u} sin comprobar");
        if (items.Count(c => c.State == StackSecurity.Ok) is > 0 and var o) parts.Add($"{o} al día");
        return string.Join(" · ", parts);
    }

    /// <summary>La fila de una comprobación, con lo que pasa, cómo se arregla y su historial.</summary>
    private VulnRow CheckRow(StackReport report, StackCheck check)
    {
        var severity = SeverityLook(check.SeverityRank);
        var grave = check.Failing && check.SeverityRank is 0 or 1;
        var (dot, fill, state) = check.Failing ? severity is { } s ? grave ? (Brushes.White, s.Brush, s.Name) : (s.Brush, s.Fill, s.Name) : (Red, RedFill, "Actualizar")
            : check.Unchecked ? (Grey, GreyFill, "Sin comprobar")
            : (Green, GreenFill, "Al día");
        var tag = check.Failing || check.Unchecked ? null : severity;
        var versions = StackSecurity.Versions(check, DateOnly.FromDateTime(DateTime.Today));
        var (letters, avatar) = Avatar(check);
        var support = "";
        if (check.SupportEndsDate is { } end)
        {
            var days = end.DayNumber - DateOnly.FromDateTime(DateTime.Today).DayNumber;
            support = "Soporte hasta el " + end.ToString("d 'de' MMMM 'de' yyyy", Es) +
                      (days < 0 ? ": ya sin soporte" : days == 0 ? ": acaba hoy" : $": quedan {days} {(days == 1 ? "día" : "días")}");
        }
        var history = _history[check.Id].Where(e => e.Kind == StackSecurityLog.CheckKind).ToList();
        var key = "check:" + check.Id;
        var (adviceTitle, adviceGlyph, adviceText) = check.Failing && !string.IsNullOrWhiteSpace(check.Fix) ? ("Cómo se arregla", "\uE90F", StackSecurity.PlainFix(check.Fix))
            : check.Failing ? ("Cómo se arregla", "\uE90F", "El informe no dice cómo. " + (check.Detail ?? ""))
            : check.Unchecked ? ("No se pudo comprobar", "\uE9CE", check.Detail ?? "")
            : ("Está al día", "\uE73E", "No hay nada que hacer.");
        return new VulnRow
        {
            Key = key,
            Title = check.Name ?? check.Id,
            KindText = KindOf(check).Name,
            Place = check.OfMachine ? "Este equipo" : check.Object!,
            PlaceBrush = check.OfMachine ? Grey : RepoColors.Of(check.Object!),
            AvatarText = letters,
            AvatarBrush = avatar,
            Logo = VendorLogos.For(check),
            Subtitle = check.Detail ?? "",
            Detail = check.Detail ?? "",
            StateText = state,
            DotBrush = dot,
            StateFill = fill,
            StateGlyph = grave && check.SeverityRank == 0 ? "\uE7BA" : "",
            RowTint = !grave ? Brushes.Transparent : check.SeverityRank == 0 ? CriticalTint : HighTint,
            StripeVisible = grave && check.SeverityRank == 0,
            StripeBrush = Critical,
            TagText = tag?.Name ?? "",
            TagBrush = tag?.Brush ?? Grey,
            VersionFrom = versions?.Installed ?? "",
            VersionJoin = versions?.Note switch { VersionNote.Newer => "→", VersionNote.Published => "· última", null => "", _ => "·" },
            VersionTo = versions?.Other ?? "",
            VersionRemark = versions?.Note switch
            {
                VersionNote.Same => "la última",
                VersionNote.NoSupport => "sin soporte desde el " + check.SupportEndsDate!.Value.ToString("d 'de' MMMM", Es),
                _ => "",
            },
            VersionBrush = versions?.Note switch { VersionNote.Newer => Green, VersionNote.NoSupport => Red, _ => Grey },
            When = check.State == StackSecurity.Ok ? "" : Short(history.FirstOrDefault(e => e.To == check.State)?.Utc),
            SupportText = support,
            Advice = check.Failing && !string.IsNullOrWhiteSpace(check.Fix) ? check.Fix : null,
            AdviceTitle = adviceTitle,
            AdviceGlyph = adviceGlyph,
            AdviceText = adviceText,
            AdviceParts = check.Failing && !string.IsNullOrWhiteSpace(check.Fix) ? StackSecurity.FixParts(check.Fix) : [new FixPart(adviceText, false)],
            MetaText = $"Comprobado {At(report.Exposure?.Date)} · id {check.Id}",
            History = history.Select((e, i) => new VulnHistoryRow(HistoryTitle(e), HistorySub(e), StateBrush(e.To), i == history.Count - 1)).ToList(),
            IsOpen = _open.Contains(key),
        };
    }

    /// <summary>El nombre y los colores de una gravedad; null si no hay o no se conoce.</summary>
    private static (string Name, IBrush Brush, IBrush Fill)? SeverityLook(int? rank) => rank switch
    {
        0 => ("Crítica", Critical, CriticalFill),
        1 => ("Alta", Red, RedFill),
        2 => ("Media", Amber, AmberFill),
        3 => ("Baja", Grey, GreyFill),
        _ => null,
    };

    /// <summary>El orden dentro de un grupo: crítica, alta, lo que hay que actualizar sin gravedad, sin comprobar y al día.</summary>
    /// <remarks>Lo que está al día con gravedad media o baja va antes que lo que no tiene nada.</remarks>
    private static int Urgency(StackCheck c) => c.Failing ? c.SeverityRank switch { 0 => 0, 1 => 1, null => 2, var r => 1 + r.Value }
        : c.Unchecked ? 5
        : 6 + (c.SeverityRank ?? 4);

    /// <summary>El estado al que pasó en un cambio del historial, con las palabras de la sección.</summary>
    private static string HistoryTitle(StackHistoryEntry e) => e.To switch
    {
        null => "Ya no se comprueba",
        StackSecurity.Fail => "Hay que actualizar",
        _ => StateName(e.To),
    };

    /// <summary>Cuándo pasó y, si se apuntó, la frase del informe en ese momento.</summary>
    private static string HistorySub(StackHistoryEntry e) =>
        Long(e.Utc) + (e.From is null ? " · primera vez que se ve" : "") + (string.IsNullOrWhiteSpace(e.Detail) ? "" : " · " + e.Detail);

    private static string StateName(string? state) => state switch
    {
        StackSecurity.Fail => "Actualizar",
        StackSecurity.Ok => "Al día",
        StackSecurity.Unchecked => "No se pudo comprobar",
        StackSecurityLog.Unseen => "Por revisar",
        StackSecurityLog.Seen => "Revisado",
        _ => state ?? "",
    };

    private static IBrush StateBrush(string? state) => state switch
    {
        StackSecurity.Fail => Red,
        StackSecurity.Ok or StackSecurityLog.Seen => Green,
        StackSecurityLog.Unseen => Amber,
        _ => Grey,
    };

    /// <summary>Un tipo de comprobación: su nombre en la fila, el título y la ayuda de su grupo, su icono y su orden.</summary>
    private sealed record CheckKind(int Order, string Name, string Group, string Help, string Glyph);

    private static readonly CheckKind Programs = new(0, "Programa", "Programas del equipo",
        "Versiones con parches de seguridad pendientes, o sin soporte. Se arreglan actualizando el programa.", "");
    private static readonly CheckKind Dependencies = new(1, "Dependencias", "Dependencias de los repos",
        "Paquetes npm y NuGet con fallos conocidos. Se suben en una tarea del repo.", "");
    private static readonly CheckKind Settings = new(2, "Configuración", "Configuración de los repos",
        "Ficheros que se apartan de lo que fija arkko-stack, o un .NET sin soporte. Se corrigen en el repo.", "");
    private static readonly CheckKind Other = new(3, "Comprobación", "Otras comprobaciones",
        "Comprobaciones nuevas de arkko-stack que Arkko aún no sabe clasificar.", "");

    /// <summary>El tipo de una comprobación por el prefijo de su id, que el contrato deja usar para agrupar.</summary>
    private static CheckKind KindOf(StackCheck check)
    {
        var id = check.Id;
        var prefix = id.Split(':')[0];
        if (id.Contains(':') && prefix is "npm" or "dotnet") return Dependencies;
        if (prefix is "mcp" or "tfm") return Settings;
        if (prefix is "edge" or "node" or "dotnet-runtime" or "dotnet-sdk" or "dotnet" or "python" or "git" or "vscode" or "playwright-cache"
            or "vs" or "visualstudio") return Programs;
        return Other;
    }

    /// <summary>Las letras y el color del icono de una comprobación, por su id.</summary>
    /// <remarks>Las piezas que trae hoy el informe llevan el color de su marca. Una que Arkko no conoce lleva la inicial de
    /// su nombre y un color de la paleta de los proyectos, sacado de su id: distinto entre ellas y siempre el mismo.</remarks>
    private static (string Letters, IBrush Brush) Avatar(StackCheck check)
    {
        var id = check.Id;
        var prefix = id.Split(':')[0];
        string? hex = prefix == "dotnet" && id.Contains(':') ? "#6B4FBB" : null;
        var (letters, brand) = hex is not null ? ("Nu", hex) : prefix switch
        {
            "edge" => ("E", "#0F7BD4"),
            "node" => ("N", "#3C873A"),
            "dotnet-runtime" or "dotnet-sdk" or "dotnet" or "tfm" => (".N", "#512BD4"),
            "python" => ("Py", "#3572A5"),
            "git" => ("G", "#E44C30"),
            "vscode" => ("VS", "#2F80C8"),
            // Visual Studio, el IDE, aún no lo comprueba arkko-stack: se adelanta con los dos ids que puede tomar.
            "vs" or "visualstudio" => ("VS", "#68217A"),
            "npm" => ("npm", "#CB3837"),
            "playwright-cache" or "mcp" => ("Pw", "#2EAD33"),
            _ => ((check.Name ?? id)[..1].ToUpper(Es), null),
        };
        return (letters, brand is null ? ProjectColors.For(id) : new ImmutableSolidColorBrush(Color.Parse(brand)));
    }

    /// <summary>Abre o cierra una fila, y lo recuerda aunque la tabla se rehaga.</summary>
    public void Toggle(OpenableRow row)
    {
        row.IsOpen = !row.IsOpen;
        if (row.IsOpen) _open.Add(row.Key);
        else _open.Remove(row.Key);
    }

    /// <summary>Abre la fila N de la parte elegida, para el recorrido de imágenes.</summary>
    public void OpenNth(int n)
    {
        OpenableRow? row = _showNews ? News.ElementAtOrDefault(n) : Rows.Where(r => r.IsItem).ElementAtOrDefault(n);
        if (row is { IsOpen: false }) Toggle(row);
    }

    // AVISOS

    /// <summary>Monta los avisos por revisar o los revisados, del más reciente al más antiguo, y las fuentes del pie.</summary>
    /// <remarks>La primera vez, el primer aviso por revisar sale abierto: es lo que hay que hacer.</remarks>
    private void BuildNews()
    {
        var report = _monitor?.Report;
        var notices = report?.Notices ?? [];
        var pending = notices.Count(n => !n.Seen);
        PendingChip = $"Por revisar · {pending}";
        ReviewedChip = $"Revisados · {notices.Count - pending}";
        var list = notices.Where(n => n.Seen == _showReviewed).OrderByDescending(n => n.Date).ToList();
        if (!_newsOpened && !_showReviewed && list.Count > 0)
        {
            _newsOpened = true;
            _open.Add("notice:" + list[0].Id);
        }
        News = list.Select(NoticeRow).ToList();
        NewsEmptyText = report is null || News.Count > 0 ? "" : _showReviewed ? "Ningún aviso revisado en los últimos 30 días." : "Ningún aviso por revisar.";
        Sources = (report?.Sources ?? []).Select(s => new SourceDot(s.Name ?? s.Id,
            s.State == StackSecurity.Ok ? Green : s.State == StackSecurity.Unchecked ? Grey : Red,
            $"{StateName(s.State)}: {s.Detail}\nRespondió {At(s.Checked)}")).ToList();
        Notify(nameof(PendingChip), nameof(ReviewedChip), nameof(News), nameof(NewsEmptyText), nameof(NewsEmptyVisible), nameof(Sources), nameof(SourcesVisible));
    }

    private bool CanCheck(StackNotice n) => !n.Seen && !n.IsGap && StackSecurityMonitor.CanRun && _monitor is { Busy: false };
    private bool CanMark(StackNotice n) => !n.Seen && StackSecurityMonitor.CanRun && _monitor is { Busy: false };

    /// <summary>Un aviso como noticia, con el protocolo en cuatro pasos.</summary>
    private NewsRow NoticeRow(StackNotice notice)
    {
        var (letter, hex) = notice.Source switch
        {
            "INCIBE-CERT" => ("I", "#C8102E"),
            "CISA KEV" => ("C", "#1F4E8C"),
            "Node.js" => ("N", "#3C873A"),
            var other => ((other ?? "?")[..1].ToUpper(Es), "#6B7080"),
        };
        var local = notice.Date?.ToLocalTime();
        NoticeStep[] steps = notice.IsGap
            ?
            [
                new("1", "Abre su web", "Mira los avisos de esos días en INCIBE-CERT o en el boletín por correo."),
                new("2", "¿Alguno te toca?", "Busca si alguno habla de algo que usas: el equipo, los repos o los servidores."),
                new("3", "Si te toca", "Crea una tarea con qué se sube, dónde y con qué urgencia, y apunta el aviso."),
                new("4", "Hecho", "Márcalo como revisado: deja de salir aquí y en Claude Code."),
            ]
            :
            [
                new("1", "¿Es oficial?", "Ábrelo y mira que venga del fabricante, INCIBE-CERT, CISA o GitHub."),
                new("2", "¿Te afecta?", "Comprueba si tienes una versión afectada."),
                new("3", "Si te afecta", "Crea una tarea con qué se sube, dónde y con qué urgencia, y apunta el aviso."),
                new("4", "Hecho", "Márcalo como revisado: deja de salir aquí y en Claude Code."),
            ];
        var key = "notice:" + notice.Id;
        return new NewsRow
        {
            Key = key,
            Notice = notice,
            Day = local?.ToString("dd", Es) ?? "–",
            Month = local?.ToString("MMM", Es).Replace(".", "") ?? "",
            Source = notice.Source ?? "",
            SourceLetter = letter,
            SourceBrush = new ImmutableSolidColorBrush(Color.Parse(hex)),
            Title = notice.IsGap ? "Días que INCIBE-CERT no ha cubierto" : notice.Title ?? notice.Id,
            Subtitle = notice.IsGap ? notice.Title ?? "" : "",
            Parts = notice.IsGap ? [] : notice.Parts,
            StateText = notice.Seen ? "Revisado" : "Por revisar",
            StateBrush = notice.Seen ? Green : Amber,
            StateFill = notice.Seen ? GreenFill : AmberFill,
            Steps = steps,
            StepsVisible = !notice.Seen,
            ReviewedText = "Revisado: alguien terminó el protocolo con este aviso. Sigue aquí mientras esté en los 30 días del informe.",
            Link = notice.Link,
            LinkText = notice.IsGap ? "Abrir INCIBE-CERT" : "Abrir el aviso",
            CheckVisible = CanCheck(notice),
            SeenVisible = CanMark(notice),
            IsOpen = _open.Contains(key),
        };
    }

    // CAMBIOS DE VISTA

    /// <summary>Cambia de pestaña.</summary>
    private void SetTab(bool news)
    {
        if (_showNews == news) return;
        _showNews = news;
        if (_monitor is { } monitor) LoadStatus(monitor);
        Notify(nameof(ShowMachine), nameof(ShowNews), nameof(MachineTableVisible), nameof(NewsListVisible), nameof(AgeText), nameof(AgeHint),
            nameof(AgeBrush), nameof(RunText), nameof(RunTip));
    }

    /// <summary>Cambia cómo se agrupa la tabla y lo recuerda.</summary>
    private void SetGroup(string group)
    {
        if (_settings is null || Group == group) return;
        _settings.VulnerabilitiesGroup = group;
        _save?.Invoke();
        Notify(nameof(ByWhere), nameof(ByKind));
        BuildRows();
    }

    /// <summary>Pasa de los avisos por revisar a los revisados, o al revés.</summary>
    private void SetReviewed(bool reviewed)
    {
        if (_showReviewed == reviewed) return;
        _showReviewed = reviewed;
        Notify(nameof(ShowPending), nameof(ShowReviewed));
        BuildNews();
    }

    /// <summary>Lee todo el historial que se conserva, por id.</summary>
    private static ILookup<string, StackHistoryEntry> ReadHistory()
    {
        try
        {
            return StackSecurityLog.History().ToLookup(e => e.Id);
        }
        catch (Exception ex)
        {
            JsonStore.LogError("vulnerabilities-history", ex);
            return Array.Empty<StackHistoryEntry>().ToLookup(e => e.Id);
        }
    }

    /// <summary>«hoy a las 11:03», «ayer a las 15:29» o «el 5 nov a las 15:29».</summary>
    private static string At(DateTimeOffset? date)
    {
        if (date is not { } d) return "nunca";
        var local = d.ToLocalTime().DateTime;
        if (local.Date == DateTime.Today) return "hoy a las " + local.ToString("HH:mm", Es);
        if (local.Date == DateTime.Today.AddDays(-1)) return "ayer a las " + local.ToString("HH:mm", Es);
        return "el " + local.ToString("d MMM", Es).Replace(".", "") + " a las " + local.ToString("HH:mm", Es);
    }

    /// <summary>«hoy, 11:03», «ayer, 15:29» o «5 nov, 15:29», para el historial.</summary>
    private static string Long(DateTime utc)
    {
        var local = utc.ToLocalTime();
        if (local.Date == DateTime.Today) return "hoy, " + local.ToString("HH:mm", Es);
        if (local.Date == DateTime.Today.AddDays(-1)) return "ayer, " + local.ToString("HH:mm", Es);
        return local.ToString("d MMM, HH:mm", Es).Replace(".", "");
    }

    /// <summary>«hoy», «ayer» o «5 nov», para la columna «desde».</summary>
    private static string Short(DateTime? utc)
    {
        if (utc is not { } u) return "";
        var local = u.ToLocalTime();
        if (local.Date == DateTime.Today) return "hoy";
        if (local.Date == DateTime.Today.AddDays(-1)) return "ayer";
        return local.ToString("d MMM", Es).Replace(".", "");
    }

    /// <summary>Avisa a la vista de varios cambios de propiedad.</summary>
    private void Notify(params string[] names)
    {
        foreach (var name in names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
