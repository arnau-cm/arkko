using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Sección de seguridad del panel: la lista de secretos a la izquierda y el detalle del elegido a la derecha.</summary>
/// <remarks>
/// Como Sesiones, la página no se desplaza: solo la lista y el detalle, cada uno por su cuenta. Los hallazgos se leen
/// descifrados solo en memoria y solo se muestran enmascarados. Al marcar uno se relee todo: son pocos y así la lista
/// no se desincroniza del almacén.
/// </remarks>
public partial class SecurityView : UserControl
{
    private readonly SecurityModel _model = new();
    private SecurityMonitor? _monitor;

    /// <summary>Crea la sección vacía; se llena con Init.</summary>
    public SecurityView()
    {
        InitializeComponent();
        DataContext = _model;
        _model.SelectionChanged += () => DetailScroll.ScrollToHome();
        // Un token nuevo, una variable o un enlace cambian de qué token es cada secreto.
        SecretPlanService.Instance.Changed += () =>
        {
            if (IsEffectivelyVisible) Reload();
        };
    }

    private Window? Owner => TopLevel.GetTopLevel(this) as Window;

    /// <summary>Se dispara al pedir ver en Sesiones la sesión de una aparición, con su id.</summary>
    public event Action<string>? SessionRequested;

    /// <summary>Se dispara al pedir ver en Sesiones el paso donde apareció un secreto: el id de la sesión y la referencia corta del secreto.</summary>
    public event Action<string, string>? SecretStepRequested;

    /// <summary>Se dispara al pedir ver en Tokens el token de un secreto, con su id.</summary>
    public event Action<string>? TokenRequested;

    /// <summary>Enseña un secreto, desde Tokens: en su pestaña, sin filtros, y elegido.</summary>
    public void ShowFinding(string fingerprint) => _model.ShowFinding(fingerprint);

    /// <summary>Pasa a la pestaña de falsos positivos.</summary>
    public void ShowIgnoredTab() => _model.ShowIgnored = true;

    /// <summary>Para el recorrido de capturas: elige el primer secreto y despliega dónde salió su primera aparición.</summary>
    public void ShowWhereForTour()
    {
        if (_model.Selected is { Appearances: [var first, ..] }) first.ToggleWhere(Avalonia.Threading.Dispatcher.UIThread);
    }

    /// <summary>Para el recorrido de capturas: enseña el valor de la primera aparición como si se mantuviera pulsado.</summary>
    public void RevealForTour()
    {
        if (_model.Selected is { Appearances: [var first, ..] }) first.BeginReveal(Avalonia.Threading.Dispatcher.UIThread);
    }

    /// <summary>Abre bajo el botón la lista de tokens, para apuntar que este secreto es uno de ellos.</summary>
    private void LinkToken_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SecretRow row } anchor) return;
        var menu = new ContextMenu { Placement = PlacementMode.Bottom };
        foreach (var card in SecretPlanService.Instance.Plan.Cards.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var entry = new MenuItem { Header = card.Name.Trim().Length > 0 ? card.Name.Trim() : "Token sin nombre" };
            entry.Click += (_, _) => Safely(() => SecretPlanService.Instance.Link(card.Id, row.Fingerprint));
            menu.Items.Add(entry);
        }
        menu.Open(anchor);
    }

    /// <summary>Quita el enlace hecho a mano entre este secreto y su token.</summary>
    private void UnlinkToken_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is SecretRow row) Safely(() => SecretPlanService.Instance.Unlink(row.Fingerprint));
    }

    /// <summary>Pide ver en Tokens el token de este secreto.</summary>
    private void ShowToken_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is SecretRow { TokenId: { } id }) TokenRequested?.Invoke(id);
    }

    /// <summary>Conecta la sección con el análisis en segundo plano.</summary>
    public void Init(SecurityMonitor monitor)
    {
        _monitor = monitor;
        monitor.Changed += () =>
        {
            if (IsEffectivelyVisible) Reload();
            else _model.ShowScan(monitor);
        };
    }

    /// <summary>Relee los hallazgos y sus marcas.</summary>
    public void Reload()
    {
        if (_monitor is null) return;
        try
        {
            var scanner = new SecretScanner();
            _model.Show(SecretReport.Group(scanner.Findings()), scanner.Rotated(), SecretReport.Group(scanner.IgnoredFindings()));
        }
        catch (Exception ex)
        {
            JsonStore.LogError("security-view", ex);
            _model.ShowError();
        }
        _model.ShowScan(_monitor);
    }

    /// <summary>Analiza todo el historial en segundo plano.</summary>
    private void ScanAll_Click(object? sender, RoutedEventArgs e) => _monitor?.ScanAll();

    /// <summary>Marca un secreto como falso positivo.</summary>
    private void Ignore_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not string fingerprint) return;
        Safely(() => new SecretScanner().Ignore(fingerprint));
    }

    /// <summary>Marca un secreto como ya rotado.</summary>
    private void Rotated_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not string fingerprint) return;
        Safely(() => new SecretScanner().SetRotated(fingerprint, true));
    }

    /// <summary>Quita la marca de rotado o de falso positivo a un secreto: vuelve a pendientes.</summary>
    private void Unmark_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not SecretRow row) return;
        Safely(() =>
        {
            var scanner = new SecretScanner();
            if (row.State == SecretState.Ignored) scanner.Unignore(row.Fingerprint);
            else scanner.SetRotated(row.Fingerprint, false);
        });
    }

    /// <summary>Vuelve a mostrar los marcados como falso positivo, tras confirmarlo.</summary>
    private async void ShowIgnored_Click(object? sender, RoutedEventArgs e)
    {
        var count = _model.IgnoredCount;
        if (!await AppDialog.ConfirmAsync(Owner, "Volver a mostrar los falsos positivos",
                (count == 1 ? "El secreto marcado como falso positivo vuelve" : $"Los {count} secretos marcados como falso positivo vuelven") +
                " a pendientes. Para quitar solo uno, usa su botón «Volver a pendiente».",
                "Volver a mostrarlos todos")) return;
        Safely(() => new SecretScanner().ClearIgnored());
    }

    /// <summary>Muestra en su carpeta el fichero de una aparición.</summary>
    private void ShowFile_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is string path) ShowInFolder(path);
    }

    /// <summary>Copia la ruta del fichero de una aparición.</summary>
    private async void CopyPath_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not string path) return;
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(path);
        }
        catch (Exception ex)
        {
            JsonStore.LogError("security-copy", ex);
        }
    }

    /// <summary>Despliega o pliega, en su sitio, el prompt donde salió una aparición.</summary>
    private void Prompt_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is AppearanceRow appearance) appearance.TogglePrompt();
    }

    /// <summary>Pide abrir en Sesiones la sesión de una aparición, en el paso donde salió si lo tiene.</summary>
    private void OpenSession_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not string id || id.Length == 0) return;
        if ((sender as Control)?.DataContext is AppearanceRow { StepRef: { } secret }) SecretStepRequested?.Invoke(id, secret);
        else SessionRequested?.Invoke(id);
    }

    /// <summary>Despliega o pliega, en su sitio, dónde salió una aparición.</summary>
    private void Where_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is AppearanceRow appearance) appearance.ToggleWhere(Avalonia.Threading.Dispatcher.UIThread);
    }

    /// <summary>Al pulsar «Mantener pulsado para ver» (el botón actúa al bajar), enseña el valor real hasta que se suelta.</summary>
    private void Reveal_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is AppearanceRow appearance) appearance.BeginReveal(Avalonia.Threading.Dispatcher.UIThread);
    }

    /// <summary>Al soltar, el botón suelta el puntero: vuelve a tapar el valor.</summary>
    private void Reveal_Released(object? sender, PointerCaptureLostEventArgs e) => Hide(sender);

    /// <summary>Si el foco se va a otro sitio, también se tapa.</summary>
    private void Reveal_LostFocus(object? sender, RoutedEventArgs e) => Hide(sender);

    private static void Hide(object? sender)
    {
        if ((sender as Control)?.DataContext is AppearanceRow appearance) appearance.EndReveal();
    }

    /// <summary>Abre el menú del filtro por tipo.</summary>
    private void KindFilter_Click(object? sender, RoutedEventArgs e) =>
        OpenFilter((Button)sender!, "Todos los tipos", _model.KindCounts(), _model.KindFilter, kind => _model.KindFilter = kind);

    /// <summary>Abre el menú del filtro por proyecto.</summary>
    private void ProjectFilter_Click(object? sender, RoutedEventArgs e) =>
        OpenFilter((Button)sender!, "Todos los proyectos", _model.ProjectCounts(), _model.ProjectFilter, project => _model.ProjectFilter = project);

    /// <summary>Abre bajo un botón el menú de un filtro: todos y cada valor con cuántos secretos tiene, marcado el elegido.</summary>
    private static void OpenFilter(Button anchor, string all, IReadOnlyList<CountRow> counts, string? current, Action<string?> choose)
    {
        var menu = new ContextMenu { Placement = PlacementMode.Bottom };

        void Add(string header, string? value)
        {
            var entry = new MenuItem { Header = (value == current ? "✓  " : "     ") + header };
            entry.Click += (_, _) => choose(value);
            menu.Items.Add(entry);
        }

        Add(all, null);
        if (counts.Count > 0) menu.Items.Add(new Separator());
        foreach (var count in counts) Add($"{count.Name} ({count.Count})", count.Name);
        menu.Open(anchor);
    }

    /// <summary>Lo que supone eliminar un secreto, para el aviso que lo confirma.</summary>
    private const string DeleteNote = "Deja de salir en Seguridad y no vuelve aunque se analice de nuevo el historial; solo saldría otra vez si aparece en algo escrito después.";

    /// <summary>Elimina un secreto marcado tras confirmarlo; si comparte fichero con otros, los enumera y los elimina todos.</summary>
    private async void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not SecretRow row) return;
        HashSet<string> others;
        try
        {
            others = SecretReport.SharingFiles(new SecretScanner().AllFindings(), row.Fingerprint);
        }
        catch (Exception ex)
        {
            JsonStore.LogError("security-delete", ex);
            await AppDialog.ShowAsync(Owner, "No se han podido leer los hallazgos", ex.Message, note: "El detalle está en errors.log.");
            return;
        }
        if (others.Count > 0)
        {
            // Primero el pulsado, y después los demás: pendientes, rotados y falsos positivos, cada grupo por tipo.
            var listed = others.Select(_model.Find).OfType<SecretRow>().OrderBy(r => r.State).ThenBy(r => r.Kind).Prepend(row)
                .Select(r => new DialogRow(r.Status, r.StatusBrush, r.StatusFill, r.Kind, r.Masked, r.Projects))
                .ToList();
            var names = row.Files.Select(Path.GetFileName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var files = names.Count == 1 ? names[0] : string.Join(", ", names[..^1]) + " y " + names[^1];
            var total = others.Count + 1;
            if (!await AppDialog.ConfirmRowsAsync(Owner, $"Eliminar {total} secretos",
                    (others.Count == 1 ? $"Hay 1 secreto más en {files}." : $"Hay {others.Count} secretos más en {files}.") + " Se eliminarán todos.",
                    $"Eliminar los {total}", listed)) return;
        }
        else if (!await AppDialog.ConfirmRowsAsync(Owner, "Eliminar el secreto", DeleteNote, "Eliminar",
                     [new DialogRow(row.Status, row.StatusBrush, row.StatusFill, row.Kind, row.Masked, row.Projects)])) return;
        others.Add(row.Fingerprint);
        Safely(() => new SecretScanner().Delete(others));
    }

    /// <summary>Enseña el fichero en su carpeta; si ya no existe, lo dice y ofrece su carpeta.</summary>
    /// <remarks>Nunca abre el fichero, solo su carpeta: lo explica <see cref="Folders"/>.</remarks>
    private async void ShowInFolder(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            if (File.Exists(full))
            {
                Folders.Reveal(full);
                return;
            }
            var folder = Path.GetDirectoryName(full);
            if (folder is not null && Directory.Exists(folder))
            {
                if (await AppDialog.ConfirmAsync(Owner, "El fichero ya no existe",
                        "Se ha movido, borrado o renombrado desde que apareció el secreto. Su carpeta sigue existiendo.",
                        "Abrir la carpeta", DialogTone.Warning, note: full, cancel: "Cerrar"))
                    Folders.Open(folder);
                return;
            }
            await AppDialog.ShowAsync(Owner, "El fichero ya no existe", "Ni el fichero ni su carpeta están en este equipo.", DialogTone.Warning, full);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or Win32Exception)
        {
            JsonStore.LogError("security-file", ex);
            await AppDialog.ShowAsync(Owner, "No se ha podido mostrar el fichero", ex.Message, note: path);
        }
    }

    /// <summary>Aplica un cambio en el almacén (una marca, un borrado) y relee; un fallo se anota y se avisa.</summary>
    private void Safely(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            JsonStore.LogError("security-mark", ex);
            _ = AppDialog.ShowAsync(Owner, "No se ha podido guardar el cambio", ex.Message, note: "El detalle está en errors.log.");
        }
        Reload();
        _monitor?.RecountPending();
    }
}

/// <summary>En qué lista está un secreto: pendiente de cambiar, ya rotado o marcado como falso positivo.</summary>
public enum SecretState { Pending, Rotated, Ignored }

/// <summary>Un secreto en la lista y en el detalle: su estado, dónde salió y qué hacer con él.</summary>
public sealed class SecretRow
{
    private readonly SecretGroup _group;
    private IReadOnlyList<AppearanceRow>? _appearances;

    /// <summary>Monta la fila de un secreto en una de las tres listas.</summary>
    /// <param name="when">Cuándo salió por última vez, ya en texto.</param>
    public SecretRow(SecretGroup group, SecretState state, string when)
    {
        _group = group;
        State = state;
        When = when;
        Projects = string.Join(", ", group.Projects);
        Files = group.Appearances.Where(a => !string.IsNullOrWhiteSpace(a.File)).Select(a => a.File!)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var tokens = SecretPlanService.Instance;
        if (tokens.TokenOf(group.Fingerprint) is { } token)
        {
            TokenId = token.Id;
            TokenName = token.Name.Trim().Length > 0 ? token.Name.Trim() : "Token sin nombre";
            LinkedByHand = token.Fingerprints.Contains(group.Fingerprint);
        }
        HasTokens = tokens.Plan.Cards.Count > 0;
    }

    // TOKEN: si el secreto es uno de los de la sección Tokens, reconocido por su variable o enlazado a mano.
    public string? TokenId { get; }
    public string? TokenName { get; }
    public bool LinkedByHand { get; }
    private bool HasTokens { get; }
    public string TokenLine => TokenName is null ? "" : $"Es el token {TokenName}" + (LinkedByHand ? ", enlazado a mano." : ", reconocido por su variable.");
    public bool TokenVisible => TokenName is not null;
    public bool UnlinkTokenVisible => LinkedByHand;
    public bool LinkTokenVisible => TokenName is null && HasTokens;

    public string Fingerprint => _group.Fingerprint;
    public string Kind => _group.Kind;
    public string Masked => _group.Masked;
    public string Projects { get; }
    public string When { get; }
    public SecretState State { get; }

    /// <summary>Ficheros donde apareció, del más reciente al más antiguo y sin repetir.</summary>
    public IReadOnlyList<string> Files { get; }

    public bool Sent => _group.Sent;
    public bool InFile => _group.InFile;

    // Los colores de las etiquetas: rojo lo enviado, ámbar lo escrito en un fichero.
    public IBrush SentBrush => SecurityModel.PendingBrush;
    public IBrush SentFill => SecurityModel.PendingFill;
    public IBrush WrittenBrush => SecurityModel.WrittenBrush;
    public IBrush WrittenFill => SecurityModel.WrittenFill;

    /// <summary>Color de lo que hay que hacer: rojo si se envió y está pendiente, ámbar si sigue escrito en un fichero, verde rotado y gris descartado.</summary>
    public IBrush ToneBrush => State switch
    {
        SecretState.Ignored => SecurityModel.IgnoredBrush,
        SecretState.Rotated => InFile ? SecurityModel.WrittenBrush : SecurityModel.DoneBrush,
        _ => Sent ? SecurityModel.PendingBrush : SecurityModel.WrittenBrush,
    };
    public IBrush ToneFill => State switch
    {
        SecretState.Ignored => SecurityModel.IgnoredFill,
        SecretState.Rotated => InFile ? SecurityModel.WrittenFill : SecurityModel.DoneFill,
        _ => Sent ? SecurityModel.PendingFill : SecurityModel.WrittenFill,
    };

    /// <summary>Icono del recuadro de qué hacer: aviso si queda algo por hacer, visto si ya está resuelto.</summary>
    public string AdviceGlyph => State == SecretState.Pending || (State == SecretState.Rotated && InFile) ? ""
        : State == SecretState.Rotated ? "" : "";

    /// <summary>Si su tipo, su valor, un proyecto o un fichero contienen el texto buscado.</summary>
    public bool Matches(string query) =>
        Kind.Contains(query, StringComparison.OrdinalIgnoreCase) || Masked.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        Projects.Contains(query, StringComparison.OrdinalIgnoreCase) || Files.Any(f => f.Contains(query, StringComparison.OrdinalIgnoreCase));

    /// <summary>Si salió en un proyecto, por su nombre como se muestra.</summary>
    public bool InProject(string project) => _group.Projects.Contains(project, StringComparer.OrdinalIgnoreCase);

    /// <summary>Proyectos donde salió, para el filtro.</summary>
    public IReadOnlyList<string> ProjectList => _group.Projects;

    /// <summary>Cuántas veces, en cuántas sesiones y ficheros, y en qué proyectos.</summary>
    public string Subtitle
    {
        get
        {
            var files = _group.Appearances.Count(a => a.Written);
            var places = new List<string>();
            if (_group.Sessions > 0) places.Add(_group.Sessions == 1 ? "en 1 sesión" : $"en {_group.Sessions} sesiones");
            if (files > 0) places.Add(files == 1 ? "escrita en 1 fichero" : $"escrita en {files} ficheros");
            var count = _group.Appearances.Count;
            return (count == 1 ? "1 aparición " : $"{count} apariciones ") + string.Join(" y ", places) + " · " + Projects;
        }
    }

    /// <summary>Qué hacer con él, según esté pendiente o marcado, y se haya enviado o siga escrito en un fichero.</summary>
    public string Advice => State switch
    {
        SecretState.Ignored => "Marcado como falso positivo: no cuenta en ninguna parte. Si sí era un secreto, vuelve a dejarlo pendiente.",
        SecretState.Rotated when InFile =>
            "Marcado como rotado, pero sigue escrito tal cual en un fichero de configuración. Pásalo a una variable de entorno y deja en el fichero solo su nombre, como ${NOMBRE}.",
        SecretState.Rotated => "Marcado como rotado: ya lo cambiaste en su servicio y no cuenta como pendiente.",
        _ when Sent && InFile =>
            "Llegó a Anthropic y además sigue escrito tal cual en un fichero de configuración. Cámbialo en su servicio, pásalo a una variable de entorno y deja en el fichero solo su nombre, como ${NOMBRE}.",
        _ when InFile =>
            "Aún no se ha enviado, pero está escrito tal cual en un fichero de configuración que Claude puede leer. Pásalo a una variable de entorno y deja en el fichero solo su nombre, como ${NOMBRE}: al quitarlo, sale solo de la lista.",
        _ => "Llegó a Anthropic en una conversación: trátalo como expuesto. Cámbialo en su servicio y márcalo como rotado; si no es un secreto o ya no sirve para nada, márcalo según corresponda.",
    };

    /// <summary>Cada vez que apareció, de la más reciente a la más antigua; se monta al elegir el secreto.</summary>
    public IReadOnlyList<AppearanceRow> Appearances
    {
        get
        {
            if (_appearances is not null) return _appearances;
            var sessions = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            bool Exists(string id) => sessions.TryGetValue(id, out var known) ? known : sessions[id] = SessionsView.FindTranscript(id) is not null;
            return _appearances = _group.Appearances.Select(a => new AppearanceRow(a, !a.Written && a.SessionId.Length > 0 && Exists(a.SessionId))).ToList();
        }
    }

    public string AppearancesTitle => _group.Appearances.Count == 1 ? "1 APARICIÓN" : $"{_group.Appearances.Count} APARICIONES";

    public string Status => State switch
    {
        SecretState.Rotated => "Rotado",
        SecretState.Ignored => "Falso positivo",
        _ => "Pendiente",
    };
    public IBrush StatusBrush => State switch
    {
        SecretState.Rotated => SecurityModel.DoneBrush,
        SecretState.Ignored => SecurityModel.IgnoredBrush,
        _ => SecurityModel.PendingBrush,
    };
    public IBrush StatusFill => State switch
    {
        SecretState.Rotated => SecurityModel.DoneFill,
        SecretState.Ignored => SecurityModel.IgnoredFill,
        _ => SecurityModel.PendingFill,
    };

    public bool PendingVisible => State == SecretState.Pending;
    public bool MarkedVisible => State != SecretState.Pending;

    /// <summary>Eliminar solo se ofrece en lo marcado que no siga escrito en un fichero: eso volvería en el siguiente análisis.</summary>
    public bool DeleteVisible => State != SecretState.Pending && !InFile;

    public string UnmarkTip => State == SecretState.Ignored
        ? "Sí es un secreto: quita la marca de falso positivo y vuelve a pendientes"
        : "No está cambiado todavía: quita la marca de rotado y vuelve a pendientes";
}

/// <summary>Una aparición de un secreto en el detalle: cuándo, dónde y de qué salió, y lo que se puede hacer con ella.</summary>
public sealed class AppearanceRow : INotifyPropertyChanged
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
    private readonly SecretFinding _finding;
    private string? _prompt;
    private bool _promptOpen;

    /// <summary>Prepara una aparición; la sesión solo se ofrece si su transcript sigue en el equipo.</summary>
    public AppearanceRow(SecretFinding finding, bool sessionExists)
    {
        _finding = finding;
        SessionButtonVisible = sessionExists;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string When => _finding.WhenUtc.ToLocalTime().ToString("d MMM yyyy, HH:mm", Es).Replace(".", "");
    public string Place => SecretReport.PlaceOf(_finding);
    public string SessionId => _finding.SessionId;
    public string Session => _finding.Written ? "escrita en un fichero"
        : "sesión " + (_finding.SessionId.Length > 8 ? _finding.SessionId[..8] : _finding.SessionId);
    public string Source => _finding.Subagent ? _finding.Source + " · en un subagente" : _finding.Source;

    /// <summary>Icono de dónde salió: un prompt, un texto pegado, la respuesta de Claude, un fichero, un comando o la configuración.</summary>
    public string Glyph => _finding switch
    {
        { Written: true } => "",
        { Source: "Texto pegado" } => "",
        { Source: "Tu mensaje" or "Historial de prompts" } => "",
        { Source: "Respuesta de Claude" } => "",
        { File: not null } => "",
        _ => "",
    };
    public string? Path => string.IsNullOrWhiteSpace(_finding.File) ? null : _finding.File;
    public bool PathVisible => Path is not null;
    public bool SessionButtonVisible { get; }

    private bool IsPrompt => SecretScanner.IsPrompt(_finding);
    public bool PromptButtonVisible => IsPrompt;
    public string PromptButtonText => (_promptOpen ? "Ocultar " : "Ver ") + (_finding.Source == "Texto pegado" ? "el texto pegado" : "el prompt");

    public bool ActionsVisible => IsPrompt || Path is not null || SessionButtonVisible || WhereButtonVisible;

    public bool PromptVisible => _promptOpen;
    public string PromptText => _prompt ?? "";

    /// <summary>Despliega o pliega el prompt; la primera vez lo lee de Claude Code, con cada secreto tapado.</summary>
    public void TogglePrompt()
    {
        _promptOpen = !_promptOpen;
        if (_promptOpen && _prompt is null)
        {
            try
            {
                _prompt = SecretScanner.PromptText(_finding)
                          ?? "Claude Code ya no lo tiene: lo ha borrado de su historial o de sus textos pegados. El secreto se envió igual.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                JsonStore.LogError("security-prompt", ex);
                _prompt = "No se ha podido leer: " + ex.Message;
            }
        }
        foreach (var name in new[] { nameof(PromptVisible), nameof(PromptText), nameof(PromptButtonText) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // Lo que se enseña al desplegar dónde salió; null mientras no se ha pedido o se está buscando.
    private string? _where;
    private bool _whereOpen;
    private bool _searching;

    /// <summary>Referencia corta del secreto para abrir la sesión en su paso; null si no salió en una conversación.</summary>
    /// <remarks>La página busca el paso que lleva el secreto; si no lo encuentra, abre la sesión sin más.</remarks>
    public string? StepRef => IsPrompt || _finding.Written ? null : SessionChannel.SecretRef(_finding);

    /// <summary>Si se puede enseñar dónde salió: en una conversación con transcript, no en un prompt, un texto pegado ni un fichero.</summary>
    public bool WhereButtonVisible => !IsPrompt && !_finding.Written;
    public string WhereButtonText => _whereOpen ? "Ocultar dónde" : "Ver dónde";
    public bool WhereVisible => _whereOpen;
    public string WhereText => _searching ? "Buscando en el transcript…" : _where ?? "";

    /// <summary>Despliega o pliega dónde salió la aparición. El texto se lee del transcript cada vez que se despliega, en
    /// segundo plano y sin guardarlo; si el hallazgo es de antes de guardar dónde salió, se apunta antes su llamada y su posición.</summary>
    /// <param name="ui">El hilo de la ventana, para avisar al terminar la lectura.</param>
    public void ToggleWhere(Avalonia.Threading.Dispatcher ui)
    {
        _whereOpen = !_whereOpen;
        _where = null;
        if (_whereOpen && !_searching)
        {
            _searching = true;
            var finding = _finding;
            Task.Run(() =>
            {
                try
                {
                    var scanner = new SecretScanner();
                    if (!finding.Located)
                    {
                        bool Same(SecretFinding f) => f.Fingerprint == finding.Fingerprint && f.SessionId == finding.SessionId
                                                      && string.Equals(f.Transcript, finding.Transcript, StringComparison.OrdinalIgnoreCase);
                        scanner.Locate(Same, maxTranscripts: 1);
                        if (scanner.AllFindings().FirstOrDefault(Same) is { } located)
                            (finding.ToolUseId, finding.Position, finding.TextLength, finding.Located) =
                                (located.ToolUseId, located.Position, located.TextLength, located.Located);
                    }
                    return WhereOf(finding, scanner.Spot(finding));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException
                                               or System.Security.Cryptography.CryptographicException)
                {
                    JsonStore.LogError("security-where", ex);
                    return "No se ha podido leer ahora: el almacén o el transcript están ocupados. Vuelve a intentarlo en un momento.";
                }
            }).ContinueWith(task => ui.Post(() =>
            {
                _searching = false;
                if (_whereOpen) _where = task.IsCompletedSuccessfully ? task.Result : "No se ha podido leer ahora.";
                Notify(nameof(WhereText));
            }));
        }
        Notify(nameof(WhereVisible), nameof(WhereText), nameof(WhereButtonText));
    }

    /// <summary>Dónde salió, en palabras, la llamada que lo produjo y el texto de alrededor, con el secreto tapado.</summary>
    private static string WhereOf(SecretFinding f, SecretSpot? spot)
    {
        if (spot is null)
            return "No se encuentra en el transcript: Claude Code lo ha borrado o ha cambiado. El secreto se envió igual.";
        // «Salida de Bash» pasa a «salida de Bash»: solo la primera letra, el nombre de la herramienta se deja como es.
        var said = f.Source.Length > 0 ? char.ToLowerInvariant(f.Source[0]) + f.Source[1..] : f.Source;
        var source = f.Subagent ? said + " de un subagente" : said;
        var at = f.Position is { } p && f.TextLength is { } n
            ? string.Format(Es, " En el carácter {0:N0} de {1:N0}", p + 1, n)
              + (p >= SessionTraceReader.MaxText ? string.Format(Es, ": más allá de los {0:N0} primeros, que son los que se ven en el paso.", SessionTraceReader.MaxText) : ".")
            : "";
        var call = spot.Call is null ? "" : $"\n\nLlamada: {spot.Call}";
        return $"En la {source}.{at}{call}\n\n{spot.Context}";
    }

    // El valor real mientras se mantiene pulsado; null en cuanto se suelta. Nunca se guarda ni se copia.
    private string? _revealed;
    private bool _revealing;

    public bool RevealVisible => _revealing;
    public string RevealText => _revealed ?? "Buscando el valor…";

    /// <summary>Enseña el valor real mientras se mantiene pulsado, leído en el momento del fichero donde se halló.</summary>
    /// <param name="ui">El hilo de la ventana, para enseñarlo al terminar la lectura.</param>
    public void BeginReveal(Avalonia.Threading.Dispatcher ui)
    {
        if (_revealing) return;
        _revealing = true;
        _revealed = null;
        Notify(nameof(RevealVisible), nameof(RevealText));
        var finding = _finding;
        Task.Run(() =>
        {
            try { return new SecretScanner().Reveal(finding); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
            {
                JsonStore.LogError("security-reveal", ex);
                return null;
            }
        }).ContinueWith(task => ui.Post(() =>
        {
            // Si ya se soltó, el valor no se enseña.
            if (!_revealing) return;
            _revealed = (task.IsCompletedSuccessfully ? task.Result : null) ?? "Ya no se encuentra: el fichero donde salió se ha borrado o ha cambiado.";
            Notify(nameof(RevealText));
        }));
        // Pulsado con el teclado no hay puntero que soltar: se tapa solo pasado un rato.
        var started = ++_revealTurn;
        Avalonia.Threading.DispatcherTimer.RunOnce(() => { if (_revealTurn == started) EndReveal(); }, RevealFor);
    }

    // Lo que dura a la vista como mucho, y la vez de enseñar a la que pertenece cada temporizador.
    private static readonly TimeSpan RevealFor = TimeSpan.FromSeconds(10);
    private int _revealTurn;

    /// <summary>Vuelve a tapar el valor al soltar, y lo olvida.</summary>
    public void EndReveal()
    {
        if (!_revealing) return;
        _revealing = false;
        _revealed = null;
        Notify(nameof(RevealVisible), nameof(RevealText));
    }

    private void Notify(params string[] names)
    {
        foreach (var name in names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>Un tramo del reparto de los secretos por estado: cuántos, qué son y su color.</summary>
public sealed record ShareSlice(int Count, string Label, IBrush Brush)
{
    public string Tip => $"{Count} {Label}";
}

/// <summary>Un valor de un filtro con cuántos secretos tiene.</summary>
public sealed record CountRow(string Name, int Count);

/// <summary>Estado de la sección de seguridad: la cabecera, las tres listas, los filtros y el secreto elegido.</summary>
public sealed class SecurityModel : INotifyPropertyChanged
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    // Pendiente es peligro: el rojo del tema, que en claro es más oscuro.
    public static IBrush PendingBrush => ThemeColors.Of("Danger", "#E03E4C");
    public static IBrush PendingFill => ThemeColors.Of("Danger", "#E03E4C", 0.18);
    // Escrita en un fichero: el ámbar del tema, el mismo de los avisos de los límites.
    public static IBrush WrittenBrush => ThemeColors.Of("DataFiveHour", "#E5C07B");
    public static IBrush WrittenFill => ThemeColors.Of("DataFiveHour", "#E5C07B", 0.16);
    public static readonly IBrush DoneBrush = new ImmutableSolidColorBrush(Color.FromArgb(0xFF, 0x3F, 0xB2, 0x7F));
    public static readonly IBrush DoneFill = new ImmutableSolidColorBrush(Color.FromArgb(0x2E, 0x3F, 0xB2, 0x7F));
    public static readonly IBrush IgnoredBrush = new ImmutableSolidColorBrush(Color.FromArgb(0xFF, 0x8A, 0x8D, 0x9C));
    public static readonly IBrush IgnoredFill = new ImmutableSolidColorBrush(Color.FromArgb(0x2E, 0x8A, 0x8D, 0x9C));

    private IReadOnlyList<ShareSlice> _shares = [];
    private string _headline = "";
    private string _headlineDetail = "";
    private string _totals = "";
    private List<SecretRow> _pending = [];
    private List<SecretRow> _rotated = [];
    private List<SecretRow> _ignored = [];
    private IReadOnlyList<SecretRow> _visible = [];
    private SecretState _tab = SecretState.Pending;
    private string? _kind;
    private string? _project;
    private string _query = "";
    private SecretRow? _selected;
    private string _emptyReason = "";
    private string _scanStatus = "";
    private bool _canScanAll = true;
    private string _scanAllText = "Analizar todo el historial";

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Se dispara al elegir otro secreto, para llevar el detalle a su principio.</summary>
    public event Action? SelectionChanged;

    // CABECERA: el estado de un vistazo; el color lo pone la barra, no el título.
    public string Headline => _headline;
    public string HeadlineDetail => _headlineDetail;
    public string Totals => _totals;

    /// <summary>El reparto de todos los secretos: enviados sin cambiar, solo escritos en un fichero, rotados y falsos positivos.</summary>
    public IReadOnlyList<ShareSlice> Shares => _shares;

    /// <summary>Los tramos del reparto para su barra.</summary>
    public IReadOnlyList<BarSegment> ShareSegments => _shares.Select(s => new BarSegment(s.Count, s.Brush)).ToList();

    /// <summary>Tooltip de la barra: cuántos de cada estado.</summary>
    public string SharesTip => string.Join("\n", _shares.Where(s => s.Count > 0).Select(s => s.Tip));

    public bool EmptyBarVisible => _shares.Sum(x => x.Count) == 0;

    /// <summary>Los secretos de la pestaña elegida que pasan los filtros, con lo visto más recientemente primero.</summary>
    public IReadOnlyList<SecretRow> VisibleRows => _visible;

    /// <summary>El secreto cuyo detalle se ve; null si la lista está vacía.</summary>
    public SecretRow? Selected
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value)) return;
            var other = _selected?.Fingerprint != value?.Fingerprint;
            _selected = value;
            Notify(nameof(Selected), nameof(DetailVisible), nameof(DetailEmptyVisible), nameof(DetailEmptyText));
            if (other) SelectionChanged?.Invoke();
        }
    }

    public bool DetailVisible => _selected is not null;
    public bool DetailEmptyVisible => _selected is null;
    public string DetailEmptyText => _visible.Count == 0 ? "" : "Elige un secreto de la lista para ver su detalle.";

    // PESTAÑAS: los dos chips no elegidos reciben false al marcar uno, y ese false no debe deshacer la elección.
    public bool ShowPending { get => _tab == SecretState.Pending; set { if (value) SetTab(SecretState.Pending); } }
    public bool ShowRotated { get => _tab == SecretState.Rotated; set { if (value) SetTab(SecretState.Rotated); } }
    public bool ShowIgnored { get => _tab == SecretState.Ignored; set { if (value) SetTab(SecretState.Ignored); } }
    public string PendingChip => $"Pendientes ({_pending.Count})";
    public string RotatedChip => $"Rotados ({_rotated.Count})";
    public string IgnoredChip => $"Falsos positivos ({_ignored.Count})";

    public int IgnoredCount => _ignored.Count;
    public bool ResetIgnoredVisible => _tab == SecretState.Ignored && _ignored.Count > 0;

    /// <summary>Texto buscado en la lista: tipo, valor enmascarado, proyecto o fichero.</summary>
    public string Query
    {
        get => _query;
        set
        {
            _query = value ?? "";
            Notify(nameof(Query));
            Refilter();
        }
    }

    /// <summary>Tipo de secreto que se enseña; null para todos.</summary>
    public string? KindFilter
    {
        get => _kind;
        set
        {
            _kind = value;
            Notify(nameof(KindFilterText));
            Refilter();
        }
    }

    /// <summary>Proyecto que se enseña; null para todos.</summary>
    public string? ProjectFilter
    {
        get => _project;
        set
        {
            _project = value;
            Notify(nameof(ProjectFilterText));
            Refilter();
        }
    }

    public string KindFilterText => $"Tipo: {_kind ?? "todos"}  ▾";
    public string ProjectFilterText => $"Proyecto: {_project ?? "todos"}  ▾";

    /// <summary>Cuántos secretos de la pestaña hay de cada tipo, del más frecuente al menos.</summary>
    public IReadOnlyList<CountRow> KindCounts() => TabRows()
        .GroupBy(r => r.Kind).Select(g => new CountRow(g.Key, g.Count()))
        .OrderByDescending(c => c.Count).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Cuántos secretos de la pestaña han salido en cada proyecto, del que más al que menos.</summary>
    public IReadOnlyList<CountRow> ProjectCounts() => TabRows()
        .SelectMany(r => r.ProjectList).GroupBy(p => p, StringComparer.OrdinalIgnoreCase).Select(g => new CountRow(g.Key, g.Count()))
        .OrderByDescending(c => c.Count).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public string ListEmptyText => _emptyReason;
    public bool ListEmptyVisible => _visible.Count == 0;

    public string ScanStatus { get => _scanStatus; private set { _scanStatus = value; Notify(nameof(ScanStatus)); } }
    public bool CanScanAll { get => _canScanAll; private set { _canScanAll = value; Notify(nameof(CanScanAll)); } }
    public string ScanAllText { get => _scanAllText; private set { _scanAllText = value; Notify(nameof(ScanAllText)); } }

    /// <summary>Pinta la cabecera y las tres listas, conservando el secreto elegido o, si ya no está en su lista, el que ocupa su sitio.</summary>
    /// <param name="groups">Secretos pendientes y rotados.</param>
    /// <param name="ignored">Secretos marcados como falso positivo.</param>
    public void Show(IReadOnlyList<SecretGroup> groups, IReadOnlySet<string> rotated, IReadOnlyList<SecretGroup> ignored)
    {
        // Los grupos ya vienen con lo visto más recientemente primero.
        _pending = groups.Where(g => !rotated.Contains(g.Fingerprint)).Select(g => Row(g, SecretState.Pending)).ToList();
        _rotated = groups.Where(g => rotated.Contains(g.Fingerprint)).Select(g => Row(g, SecretState.Rotated)).ToList();
        _ignored = ignored.Select(g => Row(g, SecretState.Ignored)).ToList();
        var sessions = groups.SelectMany(g => g.Appearances).Where(a => !a.Written).Select(a => a.SessionId).Distinct().Count();
        ShowHeader(_pending.Count(r => r.Sent), _pending.Count(r => r.InFile), groups.Count + ignored.Count, sessions);
        Notify(nameof(PendingChip), nameof(RotatedChip), nameof(IgnoredChip), nameof(IgnoredCount), nameof(ResetIgnoredVisible));
        Refilter();
    }

    /// <summary>Deja la sección vacía con un aviso, si los hallazgos no se pueden leer.</summary>
    public void ShowError()
    {
        _pending = [];
        _rotated = [];
        _ignored = [];
        ShowHeader(0, 0, 0, 0);
        _headline = "No se han podido leer los hallazgos";
        _headlineDetail = "El detalle está en errors.log.";
        Notify(nameof(Headline), nameof(HeadlineDetail));
        Notify(nameof(PendingChip), nameof(RotatedChip), nameof(IgnoredChip), nameof(IgnoredCount), nameof(ResetIgnoredVisible));
        Refilter("No se han podido leer los hallazgos. El detalle está en errors.log.");
    }

    /// <summary>Elige un secreto, en su pestaña y sin filtros ni búsqueda que lo escondan.</summary>
    public void ShowFinding(string fingerprint)
    {
        if (Find(fingerprint) is not { } row) return;
        _tab = row.State;
        _kind = null;
        _project = null;
        _query = "";
        Notify(nameof(ShowPending), nameof(ShowRotated), nameof(ShowIgnored), nameof(ResetIgnoredVisible), nameof(KindFilterText),
            nameof(ProjectFilterText), nameof(Query));
        Refilter();
        Selected = _visible.FirstOrDefault(r => r.Fingerprint == fingerprint);
    }

    /// <summary>La fila de un secreto, esté en la lista que esté; null si no está en ninguna.</summary>
    public SecretRow? Find(string fingerprint) =>
        _pending.Concat(_rotated).Concat(_ignored).FirstOrDefault(r => r.Fingerprint == fingerprint);

    /// <summary>Pinta el estado del análisis: en marcha, cuándo fue el último y si ya se miró todo el historial.</summary>
    public void ShowScan(SecurityMonitor monitor)
    {
        CanScanAll = !monitor.FullRunning;
        ScanAllText = monitor.FullRunning ? "Analizando el historial…" : "Analizar todo el historial";
        var last = monitor.Running ? "Analizando…" : monitor.LastScanUtc is { } l ? "Último análisis: " + Ago(l) : "Todavía no se ha analizado nada";
        var full = monitor.FullScanUtc is { } f
            ? "historial completo analizado " + At(f.ToLocalTime())
            : "el historial anterior a la app aún no se ha analizado";
        ScanStatus = monitor.LastError is { } error ? $"{last} · {full} · el último falló: {error}" : $"{last} · {full}";
    }

    /// <summary>Pasa a otra pestaña, sin filtro de tipo ni de proyecto que ahí no tendría sentido.</summary>
    private void SetTab(SecretState tab)
    {
        if (_tab == tab) return;
        _tab = tab;
        _selected = null;
        if (_kind is not null && TabRows().All(r => r.Kind != _kind)) _kind = null;
        if (_project is not null && TabRows().All(r => !r.InProject(_project))) _project = null;
        Notify(nameof(ShowPending), nameof(ShowRotated), nameof(ShowIgnored), nameof(ResetIgnoredVisible), nameof(KindFilterText), nameof(ProjectFilterText));
        Refilter();
    }

    /// <summary>Las filas de la pestaña elegida, sin filtrar.</summary>
    private List<SecretRow> TabRows() => _tab switch
    {
        SecretState.Rotated => _rotated,
        SecretState.Ignored => _ignored,
        _ => _pending,
    };

    /// <summary>Vuelve a calcular la lista visible y deja elegido el mismo secreto, el que ocupa su sitio o el primero.</summary>
    /// <param name="error">Si los hallazgos no se han podido leer, por qué: sale en lugar de la lista.</param>
    private void Refilter(string? error = null)
    {
        var previous = _selected;
        var index = previous is null ? 0 : Math.Max(0, _visible.ToList().FindIndex(r => r.Fingerprint == previous.Fingerprint));
        var tab = TabRows();
        _visible = tab
            .Where(r => _kind is null || r.Kind == _kind)
            .Where(r => _project is null || r.InProject(_project))
            .Where(r => _query.Length == 0 || r.Matches(_query))
            .ToList();
        _emptyReason = error
            ?? (tab.Count > 0 ? "Ningún secreto coincide con el filtro."
                : _tab switch
                {
                    SecretState.Rotated => "Ningún secreto marcado como rotado.",
                    SecretState.Ignored => "Ningún secreto marcado como falso positivo.",
                    _ => _rotated.Count + _ignored.Count > 0
                        ? "No queda ningún secreto pendiente: los encontrados están marcados como rotados o falsos positivos."
                        : "No se ha encontrado ningún secreto en lo analizado.",
                });
        Notify(nameof(VisibleRows), nameof(ListEmptyText), nameof(ListEmptyVisible));
        Selected = previous is null ? _visible.FirstOrDefault()
            : _visible.FirstOrDefault(r => r.Fingerprint == previous.Fingerprint)
              ?? (_visible.Count == 0 ? null : _visible[Math.Min(index, _visible.Count - 1)]);
        Notify(nameof(DetailEmptyText));
    }

    /// <summary>La fila de un secreto, con cuándo salió por última vez.</summary>
    private static SecretRow Row(SecretGroup g, SecretState state) => new(g, state, At(g.LastUtc.ToLocalTime()));

    /// <summary>Monta la cabecera: la frase del estado, su desglose, el reparto y los totales.</summary>
    /// <param name="sent">Pendientes que llegaron a Anthropic.</param>
    /// <param name="written">Pendientes escritos en un fichero, se hayan enviado o no.</param>
    /// <param name="distinct">Secretos distintos, también los falsos positivos.</param>
    private void ShowHeader(int sent, int written, int distinct, int sessions)
    {
        var pending = _pending.Count;
        _headline = pending == 0 ? "Ningún secreto pendiente" : Plural(pending, "secreto pendiente", "secretos pendientes");
        _headlineDetail = pending > 0
            ? $"{Plural(sent, "enviado sin cambiar", "enviados sin cambiar")} · {Plural(written, "escrito en un fichero", "escritos en un fichero")}"
            : distinct > 0 ? "Todo lo encontrado está rotado o descartado." : "No se ha encontrado ningún secreto en lo analizado.";
        var onlyWritten = _pending.Count(r => !r.Sent);
        _shares =
        [
            new(sent, sent == 1 ? "enviada sin cambiar" : "enviadas sin cambiar", PendingBrush),
            new(onlyWritten, onlyWritten == 1 ? "solo escrita en un fichero" : "solo escritas en un fichero", WrittenBrush),
            new(_rotated.Count, _rotated.Count == 1 ? "rotada" : "rotadas", DoneBrush),
            new(_ignored.Count, _ignored.Count == 1 ? "falso positivo" : "falsos positivos", IgnoredBrush),
        ];
        _totals = $"{Plural(distinct, "secreto distinto", "secretos distintos")} · {Plural(sessions, "sesión", "sesiones")}";
        Notify(nameof(Headline), nameof(HeadlineDetail), nameof(Totals), nameof(Shares), nameof(ShareSegments), nameof(SharesTip), nameof(EmptyBarVisible));
    }

    /// <summary>Una cifra con su nombre en singular o en plural: «1 sesión», «3 sesiones».</summary>
    private static string Plural(int value, string one, string many) => $"{value.ToString("N0", Es)} {(value == 1 ? one : many)}";

    /// <summary>Hace cuánto, en frase: «hace 3 min», «ahora mismo».</summary>
    private static string Ago(DateTime utc) => Format.Ago(utc) is var a && a == "ahora" ? "ahora mismo" : "hace " + a;

    /// <summary>Cuándo, corto: «hoy 14:05», «ayer 09:12» o «29 sep 18:40».</summary>
    private static string At(DateTime local)
    {
        var time = local.ToString("HH:mm", Es);
        return (DateTime.Today - local.Date).Days switch
        {
            0 => "hoy " + time,
            1 => "ayer " + time,
            _ => local.ToString("d MMM", Es).Replace(".", "") + " " + time,
        };
    }

    /// <summary>Avisa a la vista de un cambio de propiedades.</summary>
    private void Notify(params string[] names)
    {
        foreach (var name in names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
