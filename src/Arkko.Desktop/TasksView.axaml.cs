using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Sección Tareas: el tablero elegido en sus columnas, el backlog y el detalle de una tarea, con sus sesiones.</summary>
/// <remarks>
/// Solo lee el proveedor. Lo que se mueve aquí (traer del backlog, devolver, limpiar las hechas) es la
/// planificación local de la app y no cambia nada en el panel. Las tareas se quedan en memoria, en el
/// directorio que comparte con el Inicio y Sesiones; en disco, solo las claves del plan.
/// </remarks>
public partial class TasksView : UserControl
{
    private static readonly TimeSpan Stale = TimeSpan.FromMinutes(5);

    private readonly TasksModel _model = new(TaskDirectory.Instance);
    private readonly DispatcherTimer _refresh = new() { Interval = Stale };
    private readonly DispatcherTimer _live = new() { Interval = TimeSpan.FromSeconds(3) };
    private int _clockTicks;

    /// <summary>Pide abrir Configuración → Tableros, desde el aviso de que no hay ninguno.</summary>
    public event Action? ConfigureRequested;

    /// <summary>Pide abrir una sesión en Sesiones, desde el detalle de una tarea.</summary>
    public event Action<string>? SessionRequested;

    /// <summary>Crea la sección; no lee nada hasta que se entra en ella.</summary>
    public TasksView()
    {
        InitializeComponent();
        DataContext = _model;
        _refresh.Tick += async (_, _) => await _model.LoadAsync(force: true);
        _live.Tick += (_, _) =>
        {
            _model.RefreshLive();
            // El «Actualizado hace…» basta ponerlo al día cada medio minuto.
            if (++_clockTicks % 10 == 0) _model.Tick();
        };
        // Los avisos de la app se escuchan mientras la vista está en una ventana: puede haber varias copias (el panel y los
        // corchos), y la que se cierra no debe quedarse viva escuchándolos.
        AttachedToVisualTree += (_, _) =>
        {
            WorktreeMonitor.Instance.Changed += OnWorktrees;
            RepoColors.Changed += OnRepoColors;
        };
        DetachedFromVisualTree += (_, _) =>
        {
            WorktreeMonitor.Instance.Changed -= OnWorktrees;
            RepoColors.Changed -= OnRepoColors;
        };
    }

    /// <summary>Pone al día los worktrees de cada tarea.</summary>
    private void OnWorktrees() => _model.UpdateWorktrees();

    /// <summary>Los subtableros toman el color de su repositorio: si cambia, se repinta el tablero.</summary>
    private void OnRepoColors()
    {
        if (_live.IsEnabled) _model.Reload();
    }

    private Window? Owner => TopLevel.GetTopLevel(this) as Window;

    /// <summary>Recibe de dónde salen las sesiones abiertas, para el estado en directo de cada tarea.</summary>
    public void Init(Func<IReadOnlyList<SessionState>> openSessions) => _model.OpenSessions = openSessions;

    /// <summary>Al entrar en la sección: relee la configuración y, si lo leído tiene más de 5 minutos, el tablero y los worktrees.</summary>
    public async void Activate()
    {
        _model.Reload();
        _refresh.Start();
        _live.Start();
        _ = WorktreeMonitor.Instance.RefreshAsync(WorktreeMonitor.Stale);
        await _model.LoadAsync(force: false, maxAge: Stale);
    }

    /// <summary>Al salir de la sección o esconder el panel: deja de releer.</summary>
    public void Deactivate()
    {
        _refresh.Stop();
        _live.Stop();
    }

    /// <summary>Enseña una tarea por su clave: elige su tablero, si ya se ha leído, y abre su detalle.</summary>
    public async void Focus(string key)
    {
        _model.Reload();
        await _model.FocusAsync(key);
    }

    private async void Refresh_Click(object? sender, RoutedEventArgs e) => await _model.LoadAsync(force: true);

    /// <summary>Copia la orden que cierra el worktree de la tarea y lo dice un momento en el botón.</summary>
    private async void CopyClose_Click(object? sender, RoutedEventArgs e)
    {
        if (_model.Selected is not { } card) return;
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
            await clipboard.SetTextAsync(card.CloseCommand);
            card.MarkCloseCopied(true);
            DispatcherTimer.RunOnce(() => card.MarkCloseCopied(false), TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            JsonStore.LogError("tasks", ex);
        }
    }

    private void Configure_Click(object? sender, RoutedEventArgs e) => ConfigureRequested?.Invoke();

    /// <summary>Si la pulsación que acaba es del botón principal: las de los demás no eligen ni marcan.</summary>
    private static bool IsLeft(PointerReleasedEventArgs e) => e.InitialPressMouseButton == MouseButton.Left;

    /// <summary>Abre el menú para elegir otro tablero de los configurados.</summary>
    private void Board_Click(object? sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { Placement = PlacementMode.Bottom };
        foreach (var option in _model.Boards)
        {
            var item = new MenuItem { Header = (option.Id == _model.Board?.Id ? "✓  " : "     ") + option.Label };
            item.Click += async (_, _) => await _model.ChooseAsync(option);
            menu.Items.Add(item);
        }
        menu.Open((Control)sender!);
    }

    /// <summary>Abre el desplegable de etiquetas, con cuántas tareas tiene cada una.</summary>
    private void Labels_Click(object? sender, RoutedEventArgs e) => _model.LabelsOpen = true;

    /// <summary>Enciende o apaga una etiqueta del filtro, sin cerrar el desplegable.</summary>
    private void LabelOption_Click(object? sender, PointerReleasedEventArgs e)
    {
        if (IsLeft(e) && (sender as Control)?.DataContext is LabelOption option) _model.ToggleLabel(option.Name);
    }

    private void ClearLabels_Click(object? sender, RoutedEventArgs e) => _model.ClearLabels();

    /// <summary>Marca o desmarca un subtablero; «Todos» los enseña todos.</summary>
    private void SubBoard_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is SubBoardChip chip) _model.ToggleSubBoard(chip.Id);
    }

    /// <summary>Elige una tarea para ver su detalle; el mismo clic sobre la elegida lo cierra. Con Ctrl la marca, y con Mayúsculas marca hasta ella.</summary>
    /// <remarks>En macOS, Cmd hace de Ctrl, como en el resto del sistema.</remarks>
    private void Card_Click(object? sender, PointerReleasedEventArgs e)
    {
        if (!IsLeft(e) || (sender as Control)?.DataContext is not TaskCardModel card) return;
        var toggle = e.KeyModifiers.HasFlag(KeyModifiers.Control) || OperatingSystem.IsMacOS() && e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) _model.CheckRange(card);
        else if (toggle) _model.Check(card);
        else _model.Select(card);
    }

    /// <summary>Marca o desmarca una tarjeta con su casilla, sin abrir su detalle.</summary>
    private void CheckBox_Click(object? sender, PointerReleasedEventArgs e)
    {
        if (!IsLeft(e)) return;
        e.Handled = true;
        if ((sender as Control)?.DataContext is TaskCardModel card) _model.Check(card);
    }

    /// <summary>Aplica a las tarjetas marcadas la acción del botón de la barra.</summary>
    private void Bulk_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is BulkAction action) _model.ActOnChecked(action.Kind);
    }

    private void ClearChecked_Click(object? sender, RoutedEventArgs e) => _model.ClearChecked();

    /// <summary>Pliega o despliega una columna desde su cabecera o su franja.</summary>
    private void Fold_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is TaskColumnModel column) _model.ToggleFold(column);
    }

    /// <summary>Despliega una columna plegada a mano al pulsar su franja.</summary>
    private void Folded_Click(object? sender, PointerReleasedEventArgs e)
    {
        if (IsLeft(e) && (sender as Control)?.DataContext is TaskColumnModel { CanFold: true } column) _model.ToggleFold(column);
    }

    /// <summary>Pliega o despliega el carril de un subtablero.</summary>
    private void LaneFold_Click(object? sender, PointerReleasedEventArgs e)
    {
        if (IsLeft(e) && (sender as Control)?.DataContext is SwimlaneModel lane) _model.ToggleLane(lane.Id);
    }

    /// <summary>Pliega o despliega los datos del detalle (asignado, estimación…).</summary>
    private void FoldFactsHeader_Click(object? sender, PointerReleasedEventArgs e)
    {
        if (IsLeft(e)) _model.FactsFolded = !_model.FactsFolded;
    }

    private void CloseDetail_Click(object? sender, RoutedEventArgs e) => _model.Select(null);

    /// <summary>La acción principal de la tarjeta, sin elegirla: traer, devolver al backlog, quitar de la vista o restaurar.</summary>
    private void CardAction_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as Control)?.DataContext is TaskCardModel { ActionKind: { } kind } card) _model.Act(card, kind);
    }

    /// <summary>La segunda acción de una tarjeta del backlog: llevarla a Por descartar.</summary>
    private void CardSecond_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as Control)?.DataContext is TaskCardModel { SecondKind: { } kind } card) _model.Act(card, kind);
    }

    // Posición del scroll de cada columna, por tablero y estado: rehacer el tablero tras una acción no la devuelve arriba.
    private readonly Dictionary<string, double> _offsets = new();

    private string? OffsetKey(object? sender) =>
        (sender as Control)?.DataContext is TaskColumnModel column ? $"{_model.Board?.Id}/{column.State}" : null;

    /// <summary>Al rehacerse una columna, vuelve a dejar su scroll donde estaba.</summary>
    private void LaneScroll_Loaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not ScrollViewer scroll || OffsetKey(scroll) is not { } key || !_offsets.TryGetValue(key, out var offset)) return;
        Dispatcher.UIThread.Post(() => scroll.Offset = new Vector(scroll.Offset.X, offset), DispatcherPriority.Loaded);
    }

    /// <summary>Recuerda dónde está el scroll de una columna cuando lo mueve la persona.</summary>
    /// <remarks>Solo cuenta un desplazamiento: los cambios de tamaño del contenido, al cargar, llegan sin desplazamiento y lo pondrían arriba.</remarks>
    private void LaneScroll_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.OffsetDelta.Y != 0 && OffsetKey(sender) is { } key) _offsets[key] = ((ScrollViewer)sender!).Offset.Y;
    }

    private void PrevPage_Click(object? sender, RoutedEventArgs e) => _model.MovePage(-1);

    private void NextPage_Click(object? sender, RoutedEventArgs e) => _model.MovePage(1);

    /// <summary>Enseña todas las sesiones de la tarea elegida, o solo las últimas.</summary>
    private void MoreSessions_Click(object? sender, RoutedEventArgs e) => _model.Selected?.ToggleAllSessions();

    /// <summary>Abre el buscador de sesiones recientes para añadirles la tarea elegida.</summary>
    private void LinkSession_Click(object? sender, RoutedEventArgs e)
    {
        if (_model.Selected is not { } card) return;
        Picker.Show((Control)sender!, false, "AÑADIR LA TAREA A UNA SESIÓN",
            "Escribe parte del nombre de la sesión o de su carpeta; Intro elige la primera.", "No hay sesiones de los últimos 14 días.",
            _model.RecentSessions(card), id => _model.AddToSession(id, card.Task.Key));
    }

    /// <summary>Quita la tarea elegida de una de sus sesiones; si es su principal, lo pregunta antes.</summary>
    private async void Unlink_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as Control)?.DataContext is not TaskSessionRow row || _model.Selected is not { } card) return;
        if (!await TaskDirectory.Instance.ConfirmRemoveAsync(Owner, row.Id, row.Folder, card.Task.Key)) return;
        _model.RemoveFromSession(row.Id, card.Task.Key);
    }

    /// <summary>Hace de la tarea elegida la principal de una de sus sesiones: se lleva su coste.</summary>
    private void MakePrincipal_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as Control)?.DataContext is TaskSessionRow row && _model.Selected is { } card) _model.MakePrincipal(row.Id, card.Task.Key);
    }

    /// <summary>Abre en Sesiones una sesión de la tarea.</summary>
    private void Session_Click(object? sender, PointerReleasedEventArgs e)
    {
        if (IsLeft(e) && (sender as Control)?.DataContext is TaskSessionRow row) SessionRequested?.Invoke(row.Id);
    }

    /// <summary>Abre la tarea en el panel, en el navegador; solo direcciones web.</summary>
    private async void OpenTask_Click(object? sender, RoutedEventArgs e)
    {
        if (_model.Selected?.Task.Url is not { } url || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return;
        try
        {
            if (TopLevel.GetTopLevel(this)?.Launcher is { } launcher) await launcher.LaunchUriAsync(uri);
        }
        catch (Exception ex)
        {
            JsonStore.LogError("tasks-open", ex);
        }
    }
}

/// <summary>Elige la plantilla de cada tarjeta: compacta para las hechas y descartadas, normal para las demás.</summary>
public sealed class CardTemplates : IDataTemplate
{
    public IDataTemplate? Open { get; set; }
    public IDataTemplate? Closed { get; set; }

    /// <summary>Monta la tarjeta con la plantilla de su sitio.</summary>
    public Control? Build(object? param) =>
        (param is TaskCardModel { Place: CardPlace.Closed } ? Closed : Open)?.Build(param);

    /// <summary>Solo sabe montar tarjetas de tareas.</summary>
    public bool Match(object? data) => data is TaskCardModel;
}
