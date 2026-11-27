using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Arkko.Core;

namespace Arkko.Desktop;
/// <summary>Estado de la sección Tareas: el tablero elegido, los filtros, lo que se enseña y las sesiones de cada tarea.</summary>
public sealed class TasksModel(TaskDirectory directory) : INotifyPropertyChanged
{
    private BoardSettings _settings = new();
    private TaskPlan _plan = new();
    private BoardOption? _board;
    private bool _loading;
    private string? _error;
    private bool _mine = true;
    private bool _withSession;
    private string _query = "";
    private bool _restored;
    private string? _pendingSelect;
    private SubBoardStyle _style = SubBoardStyle.Stripe;
    private bool _lanesActive;
    private readonly HashSet<string> _foldedLanes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _checked = new(StringComparer.OrdinalIgnoreCase);
    private string? _anchor;
    private bool _historyMode;
    private TaskCardModel? _selected;
    private string _liveKey = "";
    private readonly HashSet<string> _subBoards = new(StringComparer.OrdinalIgnoreCase);
    private bool _labelsOpen;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Las sesiones abiertas ahora, para el estado en directo de cada tarea.</summary>
    public Func<IReadOnlyList<SessionState>> OpenSessions { get; set; } = () => [];

    public List<BoardOption> Boards { get; private set; } = [];
    public BoardOption? Board => _board;
    public HashSet<string> Labels { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ObservableCollection<TaskColumnModel> Columns { get; } = [];
    private TaskColumnModel? Backlog => Columns.FirstOrDefault(c => c.State == TaskState.Backlog);

    /// <summary>Los carriles por subtablero, en la vista de carriles con «Todos»; vacío en las demás.</summary>
    public ObservableCollection<SwimlaneModel> Swimlanes { get; } = [];

    public bool LanesVisible => !_historyMode && _lanesActive && !LoadingVisible;

    /// <summary>Pliega o despliega el carril de un subtablero, y lo recuerda.</summary>
    public void ToggleLane(string id)
    {
        if (!_foldedLanes.Remove(id)) _foldedLanes.Add(id);
        Arrange();
        SaveState();
    }

    /// <summary>Las hechas y descartadas quitadas de la vista, de la última que cambió a la primera.</summary>
    public List<TaskCardModel> HistoryCards { get; private set; } = [];

    // El historial se ve por páginas de 20.
    private const int PageSize = 20;
    private int _page;
    private int Pages => Math.Max(1, (HistoryCards.Count + PageSize - 1) / PageSize);
    public List<TaskCardModel> HistoryPage => HistoryCards.Skip(_page * PageSize).Take(PageSize).ToList();
    public string PageText => HistoryCards.Count == 0 ? "" : $"{_page * PageSize + 1}–{Math.Min((_page + 1) * PageSize, HistoryCards.Count)} de {HistoryCards.Count}";
    public bool PagerVisible => HistoryCards.Count > PageSize;
    public bool CanPrevPage => _page > 0;
    public bool CanNextPage => _page < Pages - 1;

    /// <summary>Pasa a la página siguiente (1) o a la anterior (-1) del historial.</summary>
    public void MovePage(int delta)
    {
        _page = Math.Clamp(_page + delta, 0, Pages - 1);
        PageChanged();
        SaveState();
    }

    private void PageChanged()
    {
        foreach (var name in new[] { nameof(HistoryPage), nameof(PageText), nameof(PagerVisible), nameof(CanPrevPage), nameof(CanNextPage) }) OnChanged(name);
    }

    /// <summary>Lo que se puede hacer con las marcadas, cada acción con cuántas la admiten.</summary>
    public ObservableCollection<BulkAction> BulkActions { get; } = [];

    /// <summary>Los subtableros del proyecto elegido, con «Todos» delante, para verlo por subtableros.</summary>
    public ObservableCollection<SubBoardChip> SubBoards { get; } = [];
    public bool SubBoardsVisible => SubBoards.Count > 2;

    /// <summary>Las etiquetas del tablero para el desplegable, con su recuento y si están elegidas.</summary>
    public ObservableCollection<LabelOption> LabelOptions { get; } = [];
    public bool NoLabelsVisible => LabelOptions.Count == 0;
    public bool ClearLabelsVisible => Labels.Count > 0;

    public bool LabelsOpen
    {
        get => _labelsOpen;
        set
        {
            _labelsOpen = value;
            if (value) FillLabelOptions();
            OnChanged();
            OnChanged(nameof(LabelsClosed));
        }
    }

    // Con el desplegable abierto, el botón no recibe el clic: así, pulsarlo lo cierra en vez de volver a abrirlo.
    public bool LabelsClosed => !_labelsOpen;
    public TaskCardModel? Selected => _selected;

    public bool BoardVisible => Boards.Count > 0;
    public bool EmptyVisible => Boards.Count == 0;
    public string BoardLabel => _board?.Label ?? "";
    public IBrush BoardBrush => _board is null ? Brushes.Transparent : BoardColors.BrushOf(_board.Connection.Color);

    public bool CanRefresh => !_loading && _board is not null;
    /// <summary>Si el tablero elegido aún no tiene tareas leídas: entonces se enseña la carga y no las columnas, que vacías se pliegan.</summary>
    public bool LoadingVisible => _board is not null && Read is null && _error is null;
    public string Error => _error ?? "";
    public bool ErrorVisible => _error is not null;

    /// <summary>Cuándo se leyó el tablero elegido: «Actualizado hace 2 min».</summary>
    public string UpdatedText => Read is { } r ? $"Actualizado {Ago(r.ReadUtc)}" : "";

    public BoardPerson? Me => _board is null ? null : directory.MeOf(_board.Connection.Id);
    public bool MineVisible => Me is not null;
    public string MineTip => Me?.Name is { } name ? $"Solo las asignadas a ti ({name})" : "Solo las asignadas a ti";

    public bool Mine
    {
        get => _mine;
        set
        {
            _mine = value;
            OnChanged();
            Arrange();
            SaveState();
        }
    }

    /// <summary>Solo las tareas que tienen alguna sesión, abierta o en el histórico.</summary>
    public bool WithSession
    {
        get => _withSession;
        set
        {
            _withSession = value;
            OnChanged();
            Arrange();
            SaveState();
        }
    }

    public string Query
    {
        get => _query;
        set
        {
            _query = value;
            OnChanged();
            OnChanged(nameof(PlaceholderVisible));
            Arrange();
            SaveState();
        }
    }

    public bool PlaceholderVisible => _query.Length == 0;
    public string LabelsText => Labels.Count == 0 ? "Etiquetas" : $"Etiquetas ({Labels.Count})";

    /// <summary>El chip del backlog: lo mismo que pulsar la franja de su columna.</summary>
    public bool BacklogOpen
    {
        get => !_settings.IsFolded(TaskState.Backlog);
        set
        {
            if (value == BacklogOpen) return;
            SetFolded(TaskState.Backlog, !value);
        }
    }

    /// <summary>Se ve el historial en lugar del tablero.</summary>
    public bool HistoryMode
    {
        get => _historyMode;
        set
        {
            _historyMode = value;
            _checked.Clear();
            CheckedChanged();
            foreach (var name in new[] { nameof(HistoryMode), nameof(HistoryVisible), nameof(ColumnsVisible), nameof(LanesVisible) }) OnChanged(name);
            SaveState();
        }
    }

    public bool HistoryVisible => _historyMode;
    public bool ColumnsVisible => !_historyMode && !_lanesActive && !LoadingVisible;
    public string HistoryText => $"Historial ({HistoryCards.Count})";
    public bool NoHistoryVisible => HistoryCards.Count == 0;

    public string BacklogText => $"Backlog ({Backlog?.Cards.Count ?? 0})";

    /// <summary>Si los datos del detalle están plegados; se recuerda.</summary>
    public bool FactsFolded
    {
        get => _settings.FactsFolded;
        set
        {
            _settings.FactsFolded = value;
            Save(() => directory.Store.Save(_settings));
            OnChanged();
            OnChanged(nameof(FactsVisible));
            OnChanged(nameof(FactsSummaryVisible));
            OnChanged(nameof(FactsGlyph));
        }
    }

    public bool FactsVisible => !_settings.FactsFolded;
    public bool FactsSummaryVisible => _settings.FactsFolded;
    public string FactsGlyph => _settings.FactsFolded ? "\uE70D" : "\uE70E";

    // Barra de la selección múltiple: cuántas hay marcadas.
    private List<TaskCardModel> Checked => AllCards().Where(c => c.IsChecked).ToList();
    public bool SelectionVisible => _checked.Count > 0;
    public string SelectionText => _checked.Count == 1 ? "1 marcada" : $"{_checked.Count} marcadas";
    public bool DetailVisible => _selected is not null;

    private (List<BoardTask> Tasks, DateTime ReadUtc)? Read => _board is null ? null : directory.ReadOf(_board.Id);

    /// <summary>Relee la configuración y el plan: los tableros elegidos de las conexiones que funcionan, y vuelve al último.</summary>
    /// <remarks>La primera vez, deja también la sección como se dejó: subtablero, filtros, historial y la tarea abierta.</remarks>
    public void Reload()
    {
        _settings = directory.Store.Load();
        _plan = directory.Store.LoadPlan();
        if (!_restored) RestoreState();
        // La vista se elige en Configuración: se lee cada vez que se entra.
        _style = Enum.TryParse<SubBoardStyle>(_settings.TasksView.Style, out var style) ? style : SubBoardStyle.Stripe;
        Boards = directory.Boards();
        _board = Boards.FirstOrDefault(b => b.Id == _board?.Id) ?? Boards.FirstOrDefault(b => b.Id == _settings.LastBoard) ?? Boards.FirstOrDefault();
        foreach (var name in new[] { nameof(Boards), nameof(BoardVisible), nameof(EmptyVisible), nameof(BoardLabel), nameof(BoardBrush),
                     nameof(MineVisible), nameof(MineTip), nameof(UpdatedText) })
            OnChanged(name);
        Arrange();
    }

    /// <summary>Pone la sección como se dejó la última vez.</summary>
    private void RestoreState()
    {
        _restored = true;
        var state = _settings.TasksView;
        _mine = state.Mine;
        _withSession = state.WithSession;
        _query = state.Query;
        _subBoards.Clear();
        foreach (var id in state.SubBoards.Count > 0 ? state.SubBoards : state.SubBoard is { } one ? [one] : []) _subBoards.Add(id);
        _historyMode = state.History;
        _page = Math.Max(0, state.Page);
        _pendingSelect = state.Selected;
        _foldedLanes.Clear();
        foreach (var lane in state.FoldedLanes) _foldedLanes.Add(lane);
        Labels.Clear();
        foreach (var label in state.Labels) Labels.Add(label);
        foreach (var name in new[] { nameof(Mine), nameof(WithSession), nameof(Query), nameof(PlaceholderVisible), nameof(LabelsText), nameof(HistoryMode),
                     nameof(HistoryVisible), nameof(ColumnsVisible) })
            OnChanged(name);
    }

    /// <summary>Guarda cómo está la sección, para volver a ella igual; antes de restaurarla no guarda nada.</summary>
    private void SaveState()
    {
        if (!_restored) return;
        _settings.TasksView = new TasksViewState
        {
            SubBoards = _subBoards.ToList(),
            Mine = _mine,
            WithSession = _withSession,
            Labels = Labels.ToList(),
            Query = _query,
            History = _historyMode,
            Page = _page,
            Selected = _selected?.Task.Key ?? _pendingSelect,
            Style = _style.ToString(),
            FoldedLanes = _foldedLanes.ToList(),
        };
        Save(() => directory.Store.Save(_settings));
    }

    /// <summary>Cambia de tablero, lo recuerda y lo lee si hace falta.</summary>
    public async Task ChooseAsync(BoardOption option)
    {
        _board = option;
        _settings.LastBoard = option.Id;
        Save(() => directory.Store.Save(_settings));
        _selected = null;
        _subBoards.Clear();
        Labels.Clear();
        foreach (var name in new[] { nameof(BoardLabel), nameof(BoardBrush), nameof(LabelsText), nameof(MineVisible), nameof(MineTip), nameof(UpdatedText) })
            OnChanged(name);
        Arrange();
        SaveState();
        await LoadAsync(force: false);
    }

    /// <summary>Enseña una tarea: elige el tablero en que está, quita los filtros que la esconderían y abre su detalle.</summary>
    public async Task FocusAsync(string key)
    {
        if (directory.Find(key) is { } found && found.Board.Id != _board?.Id && Boards.FirstOrDefault(b => b.Id == found.Board.Id) is { } board)
            await ChooseAsync(board);
        else await LoadAsync(force: false);
        _query = "";
        _withSession = false;
        _subBoards.Clear();
        Labels.Clear();
        foreach (var name in new[] { nameof(Query), nameof(PlaceholderVisible), nameof(WithSession), nameof(LabelsText) }) OnChanged(name);
        Arrange();
        var card = AllCards().FirstOrDefault(c => string.Equals(c.Task.Key, key, StringComparison.OrdinalIgnoreCase));
        if (card is null && _mine)
        {
            // Puede no ser de la persona: sin «Mías», se ve.
            _mine = false;
            OnChanged(nameof(Mine));
            Arrange();
            card = AllCards().FirstOrDefault(c => string.Equals(c.Task.Key, key, StringComparison.OrdinalIgnoreCase));
        }
        if (card?.InBacklog == true) BacklogOpen = true;
        if (card?.Place == CardPlace.History) HistoryMode = true;
        card = AllCards().FirstOrDefault(c => string.Equals(c.Task.Key, key, StringComparison.OrdinalIgnoreCase));
        _selected = null;
        if (card is not null) Select(card);
    }

    /// <summary>Lee las tareas del tablero elegido si no se han leído, si se pide o si lo leído es más viejo que <paramref name="maxAge"/>.</summary>
    /// <remarks>Si falla, lo dice en la franja y deja a la vista lo último bueno.</remarks>
    public async Task LoadAsync(bool force, TimeSpan? maxAge = null)
    {
        if (_board is not { } board || _loading) return;
        if (!force && Read is { } r && (maxAge is null || DateTime.UtcNow - r.ReadUtc < maxAge)) return;
        SetLoading(true);
        try
        {
            await directory.ReadAsync(board);
            _error = null;
        }
        catch (BoardException ex)
        {
            _error = $"No se ha podido leer {board.Connection.Name}: {ex.Message}";
        }
        catch (Exception ex)
        {
            JsonStore.LogError("tasks-load", ex);
            _error = $"No se ha podido leer {board.Connection.Name}. El detalle está en el registro de errores.";
        }
        finally
        {
            SetLoading(false);
            foreach (var name in new[] { nameof(Error), nameof(ErrorVisible), nameof(UpdatedText), nameof(MineVisible), nameof(MineTip) }) OnChanged(name);
            Arrange();
        }
    }

    /// <summary>Pone al día el «Actualizado hace…».</summary>
    public void Tick() => OnChanged(nameof(UpdatedText));

    /// <summary>Repinta si ha cambiado el estado de alguna sesión abierta de una tarea, para que su gota vaya en directo.</summary>
    public void RefreshLive()
    {
        var key = string.Join("|", OpenSessions()
            .SelectMany(s => directory.TasksOfSession(s.SessionId, s.Project).Tasks.Select(k => (Key: k, s.Status, s.SessionId)))
            .OrderBy(x => x.SessionId).ThenBy(x => x.Key).Select(x => $"{x.Key}:{x.Status}"));
        if (key == _liveKey) return;
        _liveKey = key;
        // Con «Con sesión», un cambio puede hacer aparecer o desaparecer tarjetas: entonces sí se rehace todo.
        if (_withSession) Arrange();
        else UpdateLive();
    }

    /// <summary>Pone al día en cada tarjeta lo que depende de las sesiones abiertas y del coste, sin rehacer el tablero.</summary>
    /// <remarks>Rehacerlo entero en cada cambio de estado de una sesión volvía a montar todas las tarjetas, con sus columnas y carriles.</remarks>
    private void UpdateLive()
    {
        var usage = directory.UsageByTask();
        var shared = directory.SharedByTask();
        var (open, costIn) = OpenOfTasks();
        foreach (var card in AllCards())
            card.UpdateLive(open.GetValueOrDefault(card.Task.Key) ?? [], usage.GetValueOrDefault(card.Task.Key), costIn.GetValueOrDefault(card.Task.Key),
                shared.GetValueOrDefault(card.Task.Key));
    }

    /// <summary>Pone en cada tarjeta su worktree y su PR, de lo último que se ha leído, sin rehacer el tablero.</summary>
    public void UpdateWorktrees()
    {
        var trees = WorktreeMonitor.Instance.ByKey(_settings);
        foreach (var card in AllCards()) card.SetWorktree(trees.TryGetValue(card.Task.Key, out var tree) ? tree : null);
    }

    /// <summary>Las sesiones abiertas de cada tarea y, en las que no son su principal, la tarea a la que va su coste.</summary>
    /// <remarks>Una sesión abierta enciende la gota de todas sus tareas, no solo la de su carpeta.</remarks>
    private (Dictionary<string, List<SessionState>> Open, Dictionary<string, string> CostIn) OpenOfTasks()
    {
        var openTasks = OpenSessions().Select(s => (Session: s, Tasks: directory.TasksOfSession(s.SessionId, s.Project))).ToList();
        var open = openTasks
            .SelectMany(x => x.Tasks.Tasks.Select(k => (Key: k, x.Session)))
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Session).ToList(), StringComparer.OrdinalIgnoreCase);
        var costIn = openTasks
            .SelectMany(x => x.Tasks.Tasks.Where(k => x.Tasks.Principal is { } p && !string.Equals(k, p, StringComparison.OrdinalIgnoreCase))
                .Select(k => (Key: k, Principal: x.Tasks.Principal!)))
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Principal, StringComparer.OrdinalIgnoreCase);
        return (open, costIn);
    }

    /// <summary>Etiquetas del tablero con cuántas tareas tiene cada una, de la más usada a la menos.</summary>
    public List<(string Label, int Count)> LabelCounts() =>
        (Read?.Tasks ?? []).SelectMany(t => t.Labels)
            .GroupBy(l => l, StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.Key, g.Count())).OrderByDescending(x => x.Item2).ThenBy(x => x.Key).ToList();

    public void ToggleLabel(string label)
    {
        if (!Labels.Remove(label)) Labels.Add(label);
        LabelsChanged();
    }

    public void ClearLabels()
    {
        Labels.Clear();
        LabelsChanged();
    }

    private void LabelsChanged()
    {
        foreach (var option in LabelOptions) option.IsOn = Labels.Contains(option.Name);
        OnChanged(nameof(LabelsText));
        OnChanged(nameof(ClearLabelsVisible));
        Arrange();
        SaveState();
    }

    /// <summary>Rehace la lista del desplegable de etiquetas con lo leído del tablero.</summary>
    private void FillLabelOptions()
    {
        LabelOptions.Clear();
        foreach (var (label, count) in LabelCounts()) LabelOptions.Add(new LabelOption(label, count) { IsOn = Labels.Contains(label) });
        OnChanged(nameof(NoLabelsVisible));
        OnChanged(nameof(ClearLabelsVisible));
    }

    /// <summary>Marca o desmarca un subtablero; null, «Todos», los desmarca todos.</summary>
    /// <remarks>Marcarlos todos, o desmarcar el último, es lo mismo que «Todos», y así se queda.</remarks>
    public void ToggleSubBoard(string? id)
    {
        if (id is null) _subBoards.Clear();
        else if (!_subBoards.Remove(id)) _subBoards.Add(id);
        if (_subBoards.Count > 0 && _subBoards.Count == SubBoards.Count(c => c.Id is not null)) _subBoards.Clear();
        Arrange();
        SaveState();
    }

    /// <summary>Elige una tarea para su detalle y busca sus sesiones; null, o la misma otra vez, lo cierra.</summary>
    public void Select(TaskCardModel? card)
    {
        _selected = card is null || card.Task.Key == _selected?.Task.Key ? null : card;
        _pendingSelect = null;
        foreach (var c in AllCards()) c.IsSelected = c.Task.Key == _selected?.Task.Key;
        foreach (var name in new[] { nameof(Selected), nameof(DetailVisible) }) OnChanged(name);
        if (_selected is { } chosen) _ = LoadSessionsAsync(chosen);
        SaveState();
    }

    /// <summary>Una acción sobre una tarjeta, solo en el plan de la app; si la tarjeta no la admite, no hace nada.</summary>
    public void Act(TaskCardModel card, string kind)
    {
        if (_board is null || !Apply(card, kind)) return;
        Save(() => directory.Store.SavePlan(_plan));
        Arrange();
    }

    /// <summary>Aplica al plan una acción sobre una tarjeta: «pull» trae a Por hacer, «discard» lleva a Por descartar,
    /// «return» devuelve al backlog, «hide» quita de la vista y «restore» saca del historial.</summary>
    private bool Apply(TaskCardModel card, string kind)
    {
        if (_board is null || !card.Allows(kind)) return false;
        var id = TaskPlan.Id(_board.Connection.Id, card.Task.Key);
        switch (kind)
        {
            case "pull": _plan.Pull(id); break;
            case "discard": _plan.Discard(id); break;
            case "return": _plan.Return(id); break;
            case "hide": _plan.Hide(id); break;
            case "restore": _plan.Restore(id); break;
            default: return false;
        }
        return true;
    }

    /// <summary>Marca o desmarca una tarjeta; la marcada pasa a ser el punto de partida de Mayúsculas+clic. Las que no admiten acciones no se marcan.</summary>
    public void Check(TaskCardModel card)
    {
        if (!card.CanCheck) return;
        if (!_checked.Remove(card.Task.Key)) _checked.Add(card.Task.Key);
        _anchor = card.Task.Key;
        CheckedChanged();
    }

    /// <summary>Marca, en la columna (o en el historial) de la tarjeta, todas las que hay entre la última marcada y ella.</summary>
    public void CheckRange(TaskCardModel card)
    {
        var list = HistoryCards.Contains(card) ? HistoryPage : Columns.FirstOrDefault(c => c.Cards.Contains(card))?.Cards;
        var from = list?.FindIndex(c => string.Equals(c.Task.Key, _anchor, StringComparison.OrdinalIgnoreCase)) ?? -1;
        if (list is null || from < 0)
        {
            Check(card);
            return;
        }
        var to = list.IndexOf(card);
        foreach (var c in list.Skip(Math.Min(from, to)).Take(Math.Abs(to - from) + 1).Where(c => c.CanCheck)) _checked.Add(c.Task.Key);
        CheckedChanged();
    }

    public void ClearChecked()
    {
        _checked.Clear();
        _anchor = null;
        CheckedChanged();
    }

    /// <summary>Aplica una acción a las marcadas que la admiten; las demás siguen marcadas.</summary>
    public void ActOnChecked(string kind)
    {
        if (_board is null) return;
        foreach (var card in Checked)
            if (Apply(card, kind)) _checked.Remove(card.Task.Key);
        Save(() => directory.Store.SavePlan(_plan));
        Arrange();
    }

    /// <summary>Pone las casillas de todas las tarjetas y la barra de la selección como están.</summary>
    private void CheckedChanged()
    {
        var selecting = _checked.Count > 0;
        foreach (var c in AllCards())
        {
            c.IsChecked = _checked.Contains(c.Task.Key);
            c.Selecting = selecting;
        }
        var marked = Checked;
        BulkActions.Clear();
        foreach (var action in BulkAction.All)
            if (marked.Count(c => c.Allows(action.Kind)) is > 0 and var n) BulkActions.Add(action with { Count = n });
        OnChanged(nameof(SelectionVisible));
        OnChanged(nameof(SelectionText));
    }

    /// <summary>Pliega o despliega una columna, y lo recuerda.</summary>
    public void ToggleFold(TaskColumnModel column) => SetFolded(column.State, !column.IsFoldedByHand);

    private void SetFolded(TaskState state, bool folded)
    {
        _settings.ColumnFolds[state.ToString()] = folded;
        Save(() => directory.Store.Save(_settings));
        OnChanged(nameof(BacklogOpen));
        Arrange();
    }

    private IEnumerable<TaskCardModel> AllCards() => Columns.SelectMany(c => c.Cards).Concat(HistoryCards);

    /// <summary>Rehace las columnas con lo leído, el plan, los filtros y las sesiones de cada tarea, conservando la elegida.</summary>
    private void Arrange()
    {
        var usage = directory.UsageByTask();
        // En las tareas que no son la principal de su sesión, el pie dice a cuál va su coste, también con la sesión ya cerrada.
        var shared = directory.SharedByTask();
        var (open, costIn) = OpenOfTasks();

        var tasks = (Read?.Tasks ?? []).AsEnumerable();
        if (_withSession) tasks = tasks.Where(t => usage.ContainsKey(t.Key) || open.ContainsKey(t.Key));
        var looks = SubBoardLooks();
        if (looks.Count > 0) _subBoards.RemoveWhere(id => !looks.ContainsKey(id));
        FillSubBoards(looks);
        var filter = new TaskFilter(Mine && Me is not null ? Me : null, Labels.Count > 0 ? new HashSet<string>(Labels, StringComparer.OrdinalIgnoreCase) : null, _query,
            _subBoards.Count > 0 ? new HashSet<string>(_subBoards, StringComparer.OrdinalIgnoreCase) : null);
        var view = _board is null ? null : TaskBoard.Arrange(tasks, _plan, _board.Connection.Id, _board.Id, filter);
        var profile = _board?.Profile;

        var selecting = _checked.Count > 0;
        var trees = WorktreeMonitor.Instance.ByKey(_settings);
        TaskCardModel Card(BoardTask t, CardPlace place)
        {
            var card = new TaskCardModel(t, place, profile, usage.GetValueOrDefault(t.Key), open.GetValueOrDefault(t.Key) ?? [],
                t.BoardId is { } b ? looks.GetValueOrDefault(b) : null, costIn.GetValueOrDefault(t.Key), _style, shared.GetValueOrDefault(t.Key))
            {
                IsSelected = t.Key == _selected?.Task.Key,
                IsChecked = _checked.Contains(t.Key),
                Selecting = selecting,
            };
            card.SetWorktree(trees.TryGetValue(t.Key, out var tree) ? tree : null);
            return card;
        }

        // Lo apartado va a la izquierda: descartadas, por descartar y el backlog, plegadas de serie.
        Columns.Clear();
        foreach (var state in TaskBoardView.Order)
        {
            if (state == TaskState.Todo)
                Columns.Add(new TaskColumnModel("Backlog", TaskState.Backlog, (view?.Backlog ?? []).Select(t => Card(t, CardPlace.Backlog)).ToList(),
                    _settings.IsFolded(TaskState.Backlog)));
            var cards = (view?[state] ?? []).Select(t => Card(t, TaskCardModel.PlaceOf(state))).ToList();
            Columns.Add(new TaskColumnModel(TaskCardModel.StateName(state), state, cards, _settings.IsFolded(state)));
        }
        HistoryCards = (view?.History ?? []).Select(t => Card(t, CardPlace.History)).ToList();
        FillLanes(looks);
        _page = Math.Clamp(_page, 0, Pages - 1);
        // Las marcadas que ya no se ven (por un filtro o una acción) dejan de estarlo.
        _checked.IntersectWith(AllCards().Select(c => c.Task.Key));

        // La elegida puede haber cambiado (o desaparecido) con la nueva lectura: se conservan sus sesiones ya buscadas.
        var previous = _selected;
        _selected = AllCards().FirstOrDefault(c => c.Task.Key == previous?.Task.Key);
        if (_selected is not null && previous is not null) _selected.TakeSessions(previous);
        if (_selected is null && _pendingSelect is { } pending && AllCards().FirstOrDefault(c => string.Equals(c.Task.Key, pending, StringComparison.OrdinalIgnoreCase)) is { } restored)
        {
            _pendingSelect = null;
            _selected = restored;
            restored.IsSelected = true;
            _ = LoadSessionsAsync(restored);
        }
        foreach (var name in new[] { nameof(BacklogText), nameof(BacklogOpen), nameof(Selected), nameof(DetailVisible),
                     nameof(HistoryCards), nameof(HistoryText), nameof(NoHistoryVisible), nameof(ColumnsVisible), nameof(LanesVisible), nameof(LoadingVisible) })
            OnChanged(name);
        PageChanged();
        CheckedChanged();
    }

    /// <summary>Nombre y color de cada subtablero del proyecto elegido entero, por su id; vacío si se mira un subtablero.</summary>
    /// <remarks>El color, el elegido en Configuración; si no hay, el que da el proveedor; y si tampoco, uno fijo por su nombre.</remarks>
    private Dictionary<string, BoardLook> SubBoardLooks()
    {
        if (_board is null || _board.Selection.BoardId is not null) return new(StringComparer.OrdinalIgnoreCase);
        return directory.SubBoardsOf(_board.Id)
            .GroupBy(b => b.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => new BoardLook(g.First().Name, SubBoardBrush(_board.Connection, g.First())), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Rehace los carriles, uno por subtablero con tareas, con las mismas tarjetas que las columnas.</summary>
    /// <remarks>
    /// Solo con la vista de carriles y más de un subtablero a la vista: todos o varios marcados. Una columna se pliega
    /// en todos los carriles a la vez, para que sigan alineadas: si está plegada a mano o vacía en todos.
    /// </remarks>
    private void FillLanes(Dictionary<string, BoardLook> looks)
    {
        Swimlanes.Clear();
        _lanesActive = _style == SubBoardStyle.Lanes && looks.Count > 0 && _subBoards.Count != 1;
        if (!_lanesActive) return;
        var lanes = looks.OrderBy(l => l.Value.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(l => (Id: l.Key, l.Value.Name, l.Value.Brush, Belongs: (Func<TaskCardModel, bool>)(c => string.Equals(c.Task.BoardId, l.Key, StringComparison.OrdinalIgnoreCase))))
            .Append(("", "Sin subtablero", ThemeColors.Of("FgFaint", "#8A8D9C"), c => c.Task.BoardId is not { } b || !looks.ContainsKey(b)));
        foreach (var (id, name, brush, belongs) in lanes)
        {
            var columns = Columns.Select(c => new TaskColumnModel(c.Title, c.State, c.Cards.Where(belongs).ToList(), c.IsFoldedByHand, c.IsFolded, header: false))
                .ToList();
            var count = columns.Sum(c => c.Count);
            if (count > 0) Swimlanes.Add(new SwimlaneModel(id, name, brush, columns, count, _foldedLanes.Contains(id)));
        }
    }

    /// <summary>Color de un subtablero: el de su repositorio si tiene uno, o el elegido en Configuración, el del proveedor o uno fijo.</summary>
    internal static IBrush SubBoardBrush(BoardConnection connection, BoardRef board) => RepoColors.OfSubBoard(connection, board);

    /// <summary>Rehace los chips de subtablero de la barra, con «Todos» delante y los marcados encendidos.</summary>
    private void FillSubBoards(Dictionary<string, BoardLook> looks)
    {
        SubBoards.Clear();
        if (looks.Count > 0) SubBoards.Add(new SubBoardChip(null, "Todos", null, _subBoards.Count == 0));
        foreach (var (id, look) in looks.OrderBy(l => l.Value.Name, StringComparer.CurrentCultureIgnoreCase))
            SubBoards.Add(new SubBoardChip(id, look.Name, look.Brush, _subBoards.Contains(id)));
        OnChanged(nameof(SubBoardsVisible));
    }

    /// <summary>Sesiones recientes a las que se puede añadir una tarea: las de los últimos 14 días que no trabajan ya en ella.</summary>
    public List<PickerItem> RecentSessions(TaskCardModel card)
    {
        var mine = card.Sessions.Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var open = OpenSessions().ToDictionary(s => s.SessionId, StringComparer.OrdinalIgnoreCase);
        return SessionCatalog.List(DateTime.UtcNow.AddDays(-14))
            .Where(s => !mine.Contains(s.SessionId))
            .OrderByDescending(s => open.ContainsKey(s.SessionId)).ThenByDescending(s => s.LastUtc)
            .Take(40)
            .Select(s =>
            {
                var folder = Path.GetFileName(s.Worktree ?? s.Project) ?? "";
                var title = s.Title ?? s.FirstPrompt ?? s.SessionId;
                var live = open.GetValueOrDefault(s.SessionId);
                return new PickerItem(s.SessionId, folder, title, live is null ? Ago(s.LastUtc) : MainViewModel.StatusWord(live),
                    live is null ? ThemeColors.Of("FgFaint", "#8A8D9C") : MainViewModel.StatusBrush(live.Status), $"{title}\n{folder} · {Ago(s.LastUtc)}");
            })
            .ToList();
    }

    /// <summary>Añade una tarea a una sesión, y lo enseña al momento.</summary>
    public void AddToSession(string sessionId, string key) => ChangeSessions(() => directory.AddTask(sessionId, key));

    /// <summary>Quita una tarea de una sesión, y lo enseña al momento.</summary>
    public void RemoveFromSession(string sessionId, string key) => ChangeSessions(() => directory.RemoveTask(sessionId, key));

    /// <summary>Hace de una tarea la principal de una sesión, y lo enseña al momento.</summary>
    public void MakePrincipal(string sessionId, string key) => ChangeSessions(() => directory.SetPrincipal(sessionId, key));

    private void ChangeSessions(Action change)
    {
        change();
        Arrange();
        if (_selected is { } chosen) _ = LoadSessionsAsync(chosen);
    }

    /// <summary>Busca las sesiones de una tarea en los transcripts y las pone en su detalle, de la más reciente a la más antigua.</summary>
    /// <remarks>Solo mira los transcripts que han cambiado desde el primer prompt de la tarea, para no leerlos todos.</remarks>
    private async Task LoadSessionsAsync(TaskCardModel card)
    {
        var key = card.Task.Key;
        var since = card.Usage?.FirstUtc.AddDays(-1);
        var settings = _settings;
        var (worked, hasPrompts) = directory.SessionsOf(key);
        var bySession = directory.UsageBySession();
        var open = OpenSessions().ToDictionary(s => s.SessionId, StringComparer.OrdinalIgnoreCase);
        card.SetSessions(null, "Buscando sus sesiones…");
        try
        {
            // Las que trabajan en ella, sea o no su principal; y, de las que aún no tienen prompts guardados, las de una carpeta con su clave.
            var found = await Task.Run(() => SessionCatalog.List(since)
                .Where(s => worked.Contains(s.SessionId)
                            || !hasPrompts(s.SessionId) && string.Equals(TaskLinks.KeyOf(settings, s.Worktree ?? s.Project), key, StringComparison.OrdinalIgnoreCase))
                .ToList());
            var rows = found
                .Select(s =>
                {
                    var principal = directory.TasksOfSession(s.SessionId, s.Worktree ?? s.Project).Principal;
                    var mine = string.Equals(principal, key, StringComparison.OrdinalIgnoreCase);
                    return new TaskSessionRow(s, bySession.GetValueOrDefault(s.SessionId), open.GetValueOrDefault(s.SessionId), mine ? null : principal);
                })
                .ToList();
            card.SetSessions(rows, rows.Count == 0 ? "Ninguna sesión ha trabajado en esta tarea." : "");
        }
        catch (Exception ex)
        {
            JsonStore.LogError("tasks-sessions", ex);
            card.SetSessions([], "No se han podido leer sus sesiones.");
        }
    }

    private void SetLoading(bool loading)
    {
        _loading = loading;
        foreach (var name in new[] { nameof(CanRefresh), nameof(LoadingVisible), nameof(ColumnsVisible), nameof(LanesVisible) }) OnChanged(name);
    }

    /// <summary>Guarda algo del plan o la configuración; un fallo se anota sin interrumpir.</summary>
    private static void Save(Action save)
    {
        try
        {
            save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            JsonStore.LogError("tasks-save", ex);
        }
    }

    /// <summary>Hace cuánto: «ahora», «hace 2 min».</summary>
    internal static string Ago(DateTime utc)
    {
        var ago = Format.Ago(utc);
        return ago == "ahora" ? "ahora" : $"hace {ago}";
    }

    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Una columna del tablero, o el backlog: su nombre, su punto de color, su recuento y sus tarjetas.</summary>
/// <remarks>
/// Una columna del tablero sin tareas se pliega a una franja estrecha, para dejar el ancho a las que
/// tienen; el backlog no, porque se abre a propósito. La de hechas se enseña en tarjetas compactas.
/// </remarks>
/// <param name="folded">Plegada a mano; se recuerda en la configuración.</param>
/// <param name="fold">Plegada o no por fuera, como en los carriles, donde una columna se pliega en todos a la vez.</param>
/// <param name="header">Lleva su cabecera; en los carriles, los nombres de las columnas van arriba una sola vez.</param>
public sealed class TaskColumnModel(string title, TaskState state, List<TaskCardModel> cards, bool folded = false, bool? fold = null, bool header = true)
{
    private const double FoldedWidth = 46;

    public string Title => title;
    public TaskState State => state;
    public List<TaskCardModel> Cards => cards;
    public int Count => cards.Count;
    public IBrush Dot => TaskCardModel.ColorOf(state);

    // Una columna vacía se pliega sola y no se puede desplegar: no hay nada que enseñar. El backlog vacío, en cambio, se abre.
    private bool Empty => cards.Count == 0 && state != TaskState.Backlog;
    public bool CanFold => !Empty;
    public bool IsFoldedByHand => folded;
    public bool IsFolded => fold ?? (folded || Empty);
    public bool HeaderVisible => header;
    public bool IsCompact => state is TaskState.Done or TaskState.Discarded;
    public double FixedWidth => IsFolded ? FoldedWidth : double.NaN;
    // En un carril, la franja plegada va sin nombre: ya lo dice la cabecera de arriba.
    public string FoldedText => !header ? "" : Empty ? title : $"{title} · {cards.Count}";
    public string FoldedTip => Empty ? $"{title}: ninguna tarea con estos filtros" : $"Desplegar {title}";
    public Cursor FoldedCursor => Empty ? Arrow : Hand;
    private static readonly Cursor Arrow = new(StandardCursorType.Arrow);
    private static readonly Cursor Hand = new(StandardCursorType.Hand);
    public string EmptyText => state == TaskState.Backlog ? "Nada en el backlog con estos filtros." : "";
    public bool EmptyVisible => header && cards.Count == 0 && state == TaskState.Backlog;
}

/// <summary>Una etiqueta en su color, el mismo siempre para el mismo nombre.</summary>
public sealed record LabelChip(string Name)
{
    public IBrush Brush => ProjectColors.For(Name);
    public IBrush Fill => ProjectColors.For(Name, 0.12);
    public IBrush Edge => ProjectColors.For(Name, 0.4);
}

/// <summary>Una etiqueta en el desplegable del filtro: su color, cuántas tareas la llevan y si está elegida.</summary>
public sealed class LabelOption(string name, int count) : INotifyPropertyChanged
{
    private bool _on;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name => name;
    public string Count => count.ToString(CultureInfo.CurrentCulture);
    public IBrush Brush => ProjectColors.For(name);
    public IBrush Fill => ProjectColors.For(name, 0.12);
    public IBrush Edge => ProjectColors.For(name, 0.4);

    public bool IsOn
    {
        get => _on;
        set
        {
            if (_on == value) return;
            _on = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsOn)));
        }
    }
}

/// <summary>Una acción de la barra de la selección múltiple, con cuántas marcadas la admiten.</summary>
public sealed record BulkAction(string Kind, string Glyph, string Name, int Count = 0)
{
    /// <summary>Todas, en el orden en que salen en la barra.</summary>
    public static readonly BulkAction[] All =
    [
        new("pull", "\uE74A", "A Por hacer"),
        new("discard", "\uE74D", "A Por descartar"),
        new("return", "\uE74B", "Al backlog"),
        new("hide", "\uE711", "Quitar de la vista"),
        new("restore", "\uE7A7", "Restaurar"),
    ];

    public string Text => $"{Name} ({Count})";
}

/// <summary>Dónde está una tarjeta, que decide si se puede marcar y qué se puede hacer con ella en la app.</summary>
public enum CardPlace
{
    Backlog,
    Todo,
    ToDiscard,

    /// <summary>En curso, bloqueada o en revisión: su estado lo cambia el ciclo de trabajo, no la app.</summary>
    Working,

    /// <summary>Hecha o descartada.</summary>
    Closed,

    /// <summary>Quitada de la vista: en el historial.</summary>
    History,
}

/// <summary>Cómo se ve el subtablero de cada tarea.</summary>
public enum SubBoardStyle
{
    /// <summary>Franja ancha a la izquierda con su nombre; marcada, en su color; en el detalle, banda arriba.</summary>
    Stripe,

    /// <summary>Borde fino y su nombre como pastilla; marcada, en el color de la app.</summary>
    Subtle,

    /// <summary>Un carril por subtablero, con las mismas columnas; las tarjetas no llevan su color.</summary>
    Lanes,
}

/// <summary>El carril de un subtablero en la vista de carriles: su nombre, su color, sus columnas y si está plegado.</summary>
public sealed class SwimlaneModel(string id, string name, IBrush brush, List<TaskColumnModel> columns, int count, bool folded)
{
    public string Id => id;
    public string Name => name.ToUpperInvariant();
    public IBrush Brush => brush;
    public IBrush Tint { get; } = brush is ISolidColorBrush { Color: var c } ? new ImmutableSolidColorBrush(c, 0.12) : Brushes.Transparent;
    public List<TaskColumnModel> Columns => columns;
    public string CountText => count == 1 ? "1 tarea" : $"{count} tareas";
    public bool BodyVisible => !folded;
    public string Glyph => folded ? "\uE76C" : "\uE70D";
}

/// <summary>Nombre y color de un subtablero, para su chip y la franja de sus tarjetas.</summary>
public sealed record BoardLook(string Name, IBrush Brush);

/// <summary>Un chip de subtablero en la barra; sin id, el de «Todos».</summary>
public sealed record SubBoardChip(string? Id, string Name, IBrush? Brush, bool IsOn)
{
    public bool DotVisible => Brush is not null;
    public string Tip => Id is null ? "Las tareas de todos los subtableros" : IsOn ? "Quitar este subtablero de la vista" : "Añadir este subtablero a la vista";
}

/// <summary>Una sesión abierta en la ficha del pie de una tarjeta: su nombre, su estado con su color y desde cuándo.</summary>
public sealed record TipSession(string Name, string Status, IBrush Brush, string Since);

/// <summary>Un dato del detalle de una tarea: «Asignado · Persona».</summary>
public sealed record TaskFact(string Name, string Value);

/// <summary>Una sesión de una tarea en su detalle: su nombre, cuándo trabajó, lo que gastó y, si está abierta, su estado.</summary>
/// <param name="usage">Lo que gastó, si esta es su tarea principal; si no, su coste está en la principal.</param>
/// <param name="principal">Su tarea principal, si no es esta: la sesión la comparte y se puede quitar de ella.</param>
public sealed class TaskSessionRow(SessionSummary summary, TaskUsage? usage, SessionState? open, string? principal = null)
{
    public string Id => summary.SessionId;

    /// <summary>La carpeta de la sesión, de la que sale su tarea si aún no tiene prompts.</summary>
    public string? Folder => summary.Worktree ?? summary.Project;

    /// <summary>Quitarla se puede siempre; si es la principal, el coste pasa a otra y se pregunta antes.</summary>
    public string UnlinkTip => principal is null ? "Quitar esta tarea de la sesión: es su principal, así que su coste pasa a otra" : "Quitar esta tarea de la sesión";
    public bool MakePrincipalVisible => principal is not null;

    /// <summary>Si la comparte con otra tarea: «compartida · coste en DEVLAB-99».</summary>
    public string? Origin => principal is null ? null : $"compartida · coste en {principal}";
    public bool OriginVisible => Origin is not null;
    public string Title => summary.Title ?? summary.FirstPrompt ?? summary.SessionId[..Math.Min(8, summary.SessionId.Length)];
    public string Detail
    {
        get
        {
            var parts = new List<string> { TasksModel.Ago(summary.LastUtc) };
            if (usage is { } u)
            {
                parts.Add($"{Format.Tokens(u.NewTokens)} tokens");
                if (u.Working > TimeSpan.Zero) parts.Add(Format.Duration(u.Working));
            }
            return string.Join(" · ", parts);
        }
    }
    public IBrush StatusBrush => open is null ? ThemeColors.Of("FgFaint", "#8A8D9C") : MainViewModel.StatusBrush(open.Status);
    public string StatusText => open is null ? "Cerrada" : MainViewModel.StatusWord(open);
}

/// <summary>Una tarea en el tablero y en su detalle, con sus sesiones y lo que ha costado.</summary>
/// <param name="place">Dónde está: decide si se puede marcar y sus acciones.</param>
/// <param name="usage">Lo que ha costado en todo el histórico; null si no tiene prompts.</param>
/// <param name="open">Sus sesiones abiertas ahora.</param>
/// <param name="look">Su subtablero, si se mira el proyecto entero: su nombre y el color de la franja.</param>
/// <param name="costIn">La tarea principal de una sesión abierta que también trabaja en esta: su coste va allí.</param>
/// <param name="style">Cómo se ve su subtablero: franja con su nombre, borde fino y pastilla, o nada porque lo dice su carril.</param>
/// <param name="shared">Lo trabajado en ella desde sesiones cuya principal es otra tarea, abiertas o cerradas.</param>
public sealed class TaskCardModel(BoardTask task, CardPlace place, BoardProfile? profile, TaskUsage? usage,
    List<SessionState> open, BoardLook? look, string? costIn = null, SubBoardStyle style = SubBoardStyle.Subtle, SharedWork? shared = null) : INotifyPropertyChanged
{
    private const int FirstSessions = 3;

    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
    private bool _selected;
    private List<TaskSessionRow>? _sessions;
    private string _sessionsNote = "";
    private bool _allSessions;
    private Control? _document;
    private bool _checked;
    private bool _selecting;

    public event PropertyChangedEventHandler? PropertyChanged;

    public BoardTask Task => task;
    public TaskUsage? Usage => usage;

    /// <summary>Cambia las sesiones abiertas, el coste y a qué tarea va, y avisa de todo lo que se pinta con ellos.</summary>
    public void UpdateLive(List<SessionState> nowOpen, TaskUsage? nowUsage, string? nowCostIn, SharedWork? nowShared)
    {
        (open, usage, costIn, shared) = (nowOpen, nowUsage, nowCostIn, nowShared);
        // Un nombre vacío pone al día todos los enlaces de la tarjeta: son muchos los que dependen de esto.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
    public CardPlace Place => place;
    public bool InBacklog => place == CardPlace.Backlog;
    public string Key => task.Key;
    public string Title => task.Title;

    public bool IsSelected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            OnChanged();
        }
    }

    /// <summary>Marcada para una acción en grupo.</summary>
    public bool IsChecked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            OnChanged();
        }
    }

    /// <summary>Hay alguna marcada en el tablero: las casillas se ven siempre, no solo con el ratón encima.</summary>
    public bool Selecting
    {
        get => _selecting;
        set
        {
            if (_selecting == value) return;
            _selecting = value;
            OnChanged();
        }
    }

    /// <summary>Icono del tipo: error, épica, historia, subtarea o tarea.</summary>
    public string TypeGlyph => (task.Type ?? "").ToUpperInvariant() switch
    {
        "BUG" => "",
        "EPIC" => "",
        "STORY" => "",
        "SUBTASK" => "",
        _ => "",
    };

    public string TypeTip => task.Type is { } t ? Capitalize(t.ToLowerInvariant()) : "Tarea";

    // Color del tipo: rojo un error, morado una épica, verde una historia, turquesa una subtarea y azul una tarea.
    private (string Key, string Fallback) TypeColor => (task.Type ?? "").ToUpperInvariant() switch
    {
        "BUG" => ("Danger", "#E03E4C"),
        "EPIC" => ("DataFable", "#C678DD"),
        "STORY" => ("DataHaiku", "#98C379"),
        "SUBTASK" => ("DataCache", "#56B6C2"),
        _ => ("DataSonnet", "#61AFEF"),
    };

    public IBrush TypeBrush => ThemeColors.Of(TypeColor.Key, TypeColor.Fallback);
    public IBrush TypeFill => ThemeColors.Of(TypeColor.Key, TypeColor.Fallback, 0.12);
    public IBrush TypeEdge => ThemeColors.Of(TypeColor.Key, TypeColor.Fallback, 0.35);

    // Prioridad por su nombre, que cambia de un proveedor a otro.
    private int PriorityLevel => (task.Priority ?? "").ToLowerInvariant() switch
    {
        var p when p.Contains("highest") || p.Contains("crit") || p.Contains("urg") => 2,
        var p when p.Contains("high") || p.Contains("alta") => 1,
        var p when p.Contains("lowest") => -2,
        var p when p.Contains("low") || p.Contains("baja") => -1,
        _ => 0,
    };

    // Barras de señal encendidas: una la baja, dos la media y tres la alta; la urgente, además, en rojo.
    private int PriorityBars => PriorityLevel switch { < 0 => 1, 0 => 2, _ => 3 };
    private IBrush PriorityBrush => PriorityLevel switch
    {
        2 => ThemeColors.Of("Danger", "#E03E4C"),
        1 => ThemeColors.Of("DataFiveHour", "#E5C07B"),
        _ => ThemeColors.Of("FgMuted", "#B3B6C4"),
    };
    private IBrush Bar(int n) => PriorityBars >= n ? PriorityBrush : ThemeColors.Of("ThumbFill", "#33FFFFFF");
    public IBrush Bar1 => Bar(1);
    public IBrush Bar2 => Bar(2);
    public IBrush Bar3 => Bar(3);
    public bool PriorityVisible => task.Priority is not null;
    public string? PriorityTip => task.Priority is { } p ? $"Prioridad {p.ToLowerInvariant()}" : null;

    public List<LabelChip> Labels => task.Labels.Select(l => new LabelChip(l)).ToList();
    public bool LabelsVisible => task.Labels.Count > 0;

    public string? Assignee => task.Assignee;
    public string Initials => task.Assignee is { } name
        ? string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpper(w[0], Es)))
        : "";
    public bool InitialsVisible => task.Assignee is not null;

    // Subtablero: la franja de color a la izquierda de la tarjeta, con su nombre, y arriba en el detalle.
    // La franja ancha y la banda llevan el nombre en blanco encima: su color, oscurecido si es claro.
    public IBrush StripeBrush => look is null ? Brushes.Transparent : style == SubBoardStyle.Stripe ? RepoColors.Band(look.Brush) : look.Brush;
    public IBrush DotBrush => look?.Brush ?? Brushes.Transparent;
    public bool StripeVisible => look is not null && style != SubBoardStyle.Lanes;
    public double StripeWidth => style == SubBoardStyle.Stripe ? 18 : 3;

    /// <summary>Alto mínimo de la tarjeta para que el nombre del subtablero quepa entero en la franja, con aire arriba y abajo.</summary>
    public double StripeMinHeight => style == SubBoardStyle.Stripe && BoardName is { } name ? TextLength(name) + 24 : 0;

    private static FontFamily UiFont => Application.Current?.FindResource("UiFont") as FontFamily ?? FontFamily.Default;

    private static double TextLength(string text) =>
        new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(UiFont, FontStyle.Normal, FontWeight.Bold), 9, Brushes.Black)
            .WidthIncludingTrailingWhitespace;
    public bool StripeTextVisible => style == SubBoardStyle.Stripe;

    // Discreto: su nombre como pastilla en la línea de datos. En el detalle, la pastilla en todas menos la franja, que lleva banda.
    public bool BoardChipVisible => look is not null && style == SubBoardStyle.Subtle;
    public bool BandVisible => look is not null && style == SubBoardStyle.Stripe;
    public bool DetailChipVisible => look is not null && style != SubBoardStyle.Stripe;
    public IBrush BoardTint => look?.Brush is ISolidColorBrush { Color: var t } ? Tint(t, 0.14) : Brushes.Transparent;
    public string? BoardName => look?.Name?.ToUpperInvariant();

    /// <summary>Color del nombre sobre la franja: siempre blanco, porque la franja se oscurece lo justo para que se lea.</summary>
    public IBrush StripeForeground => Brushes.White;

    // Marcada para una acción: con la franja, en el color de su subtablero; en las demás vistas, siempre en el granate de la app.
    private bool MarkInBoardColor => style == SubBoardStyle.Stripe && look is not null;
    public IBrush CheckBrush => MarkInBoardColor ? look!.Brush : ThemeColors.Of("ChipOnEdge", "#86050D");
    public IBrush CheckTint => MarkInBoardColor && look!.Brush is ISolidColorBrush { Color: var c } ? Tint(c, 0.10) : ThemeColors.Of("ChipOn", "#2686050D");

    private static IBrush Tint(Color color, double opacity) => new ImmutableSolidColorBrush(color, opacity);
    public IBrush AvatarFill => ProjectColors.For(task.Assignee ?? "");
    public string Estimate => task.EstimatedHours is { } h ? $"{h.ToString("0.#", Es)} h" : "";
    public bool EstimateVisible => task.EstimatedHours is not null;

    public string? Untranslated => task.State == TaskState.Unknown ? $"Estado sin traducir: {task.Status}" : null;
    public bool UntranslatedVisible => task.State == TaskState.Unknown;

    /// <summary>Columna de la app de cada estado: decide qué se puede hacer con sus tarjetas.</summary>
    public static CardPlace PlaceOf(TaskState state) => state switch
    {
        TaskState.Todo => CardPlace.Todo,
        TaskState.ToDiscard => CardPlace.ToDiscard,
        TaskState.Done or TaskState.Discarded => CardPlace.Closed,
        TaskState.Backlog or TaskState.Unknown => CardPlace.Backlog,
        _ => CardPlace.Working,
    };

    // Acciones, solo en la app: del backlog se trae o se lleva a Por descartar; de Por hacer y Por descartar se
    // devuelve al backlog; lo hecho o descartado se quita de la vista, y del historial se restaura.
    public bool CanCheck => place != CardPlace.Working;
    public bool Allows(string kind) => kind switch
    {
        "pull" or "discard" => place == CardPlace.Backlog,
        "return" => place is CardPlace.Todo or CardPlace.ToDiscard,
        "hide" => place == CardPlace.Closed,
        "restore" => place == CardPlace.History,
        _ => false,
    };

    public string? ActionKind => place switch
    {
        CardPlace.Backlog => "pull",
        CardPlace.Todo or CardPlace.ToDiscard => "return",
        CardPlace.Closed => "hide",
        CardPlace.History => "restore",
        _ => null,
    };
    private BulkAction? Action => BulkAction.All.FirstOrDefault(a => a.Kind == ActionKind);
    public bool ActionVisible => ActionKind is not null;
    public string ActionGlyph => Action?.Glyph ?? "";
    public string ActionText => Action?.Name ?? "";
    public string ActionTip => ActionText + " (solo en la app; el panel no cambia)";

    // Segunda acción, solo en el backlog: llevarla a Por descartar.
    public string? SecondKind => place == CardPlace.Backlog ? "discard" : null;
    public bool SecondVisible => SecondKind is not null;
    public string SecondGlyph => "\uE74D";
    public string SecondText => "A Por descartar";
    public string SecondTip => "Llevar a Por descartar (solo en la app; el panel no cambia)";

    // Marca de una tarjeta cerrada: hecha, en verde; descartada, en gris.
    public string ClosedGlyph => task.State == TaskState.Discarded ? "\uE711" : "\uE73E";
    public IBrush ClosedBrush => task.State == TaskState.Discarded ? ThemeColors.Of("DataOther", "#ABB2BF") : ThemeColors.Of("DataHaiku", "#98C379");

    /// <summary>Último cambio, para la tabla del historial: «14 oct., 15:51».</summary>
    public string UpdatedText => task.UpdatedUtc?.ToLocalTime().ToString("d MMM, HH:mm", Es) ?? "";

    // Pie de sesiones de la tarjeta: la sesión abierta que más pide atención y lo que llevan gastado.
    private SessionState? Live => open.OrderBy(s => s.Status switch
    {
        SessionStatus.Waiting => 0,
        SessionStatus.Working or SessionStatus.Agents or SessionStatus.Command => 1,
        SessionStatus.Done => 2,
        _ => 3,
    }).FirstOrDefault();

    public bool StripVisible => usage is not null || open.Count > 0 || shared is not null;

    /// <summary>Cuántas sesiones ha tenido: las suyas, con su coste, y las de otra tarea en que también se trabajó.</summary>
    private int SessionCount => (usage?.Sessions ?? 0) + (shared?.Sessions ?? 0);

    /// <summary>A qué tarea va el coste del trabajo hecho en sesiones de otra: la de una abierta o, si no, la de una pasada.</summary>
    private string? CostElsewhere => costIn ?? shared?.Principal;

    /// <summary>Las cifras que se enseñan: las suyas o, si no tiene, las de las sesiones de otra tarea en que se trabajó.</summary>
    private TaskUsage? Shown => usage ?? shared?.Usage;

    private bool ShowingShared => usage is null && shared?.Usage is not null;

    private static string SessionWord(int n) => n == 1 ? "1 sesión" : $"{n} sesiones";
    public bool LiveVisible => Live is not null;
    public bool PastVisible => Live is null;
    public IBrush LiveBrush => Live is { } s ? MainViewModel.StatusBrush(s.Status) : Brushes.Transparent;

    // FICHA DEL PIE, al pasar por él: sus sesiones abiertas, lo que han gastado todas y cuándo trabajaron.
    public string TipTitle => open.Count switch
    {
        0 => SessionCount switch
        {
            0 => "Sin sesiones",
            1 => "1 sesión, ya cerrada",
            var n => $"{n} sesiones, todas cerradas",
        },
        1 => "1 sesión abierta",
        var n => $"{n} sesiones abiertas",
    };

    /// <summary>Las abiertas, la que más pide atención primero: su nombre, su estado y desde cuándo.</summary>
    public List<TipSession> TipOpen => open.OrderBy(s => s.Status switch
        {
            SessionStatus.Waiting => 0,
            SessionStatus.Working or SessionStatus.Agents or SessionStatus.Command => 1,
            _ => 2,
        })
        .Select(s => new TipSession(s.SessionName ?? s.Title ?? s.SessionId[..Math.Min(8, s.SessionId.Length)], MainViewModel.StatusWord(s),
            MainViewModel.StatusBrush(s.Status), TasksModel.Ago(s.StatusSinceUtc)))
        .ToList();

    public bool TipOpenVisible => open.Count > 0;
    public bool TipStatsVisible => Shown is not null;

    /// <summary>Lo que no dicen las cifras: que son de sesiones de otra tarea, que aún no ha terminado ningún prompt, o cuántas ha tenido.</summary>
    public string TipNote => usage switch
    {
        null when ShowingShared => $"Son de {SessionWord(shared!.Sessions)} de otra tarea: ya cuentan en {shared.Principal}, la principal de la sesión.",
        null when CostElsewhere is { } principal => $"Se trabaja en una sesión de otra tarea: su coste cuenta en {principal}, la principal de la sesión.",
        null => "Aún no ha terminado ningún prompt: las cifras salen al acabar el primero.",
        _ => SharedLine ?? (open.Count > 0 ? $"{SessionWord(SessionCount)} con prompts en total." : ""),
    };

    /// <summary>Lo compartido, cuando la tarea también tiene sesiones propias: «Y en 1 sesión de otra tarea, 2,1M tokens, que cuentan en DEVLAB-99».</summary>
    private string? SharedLine => usage is not null && shared is { } s
        ? $"Y en {SessionWord(s.Sessions)} de otra tarea" + (s.Usage is { } su ? $", {Format.Tokens(su.NewTokens)} tokens" : "") + $", que cuentan en {s.Principal}."
        : null;

    public bool TipNoteVisible => TipNote.Length > 0;

    /// <summary>Cuándo trabajaron en ella: «Desde el 12 oct. · la última vez, hace 2 días».</summary>
    public string TipWhen => Shown is { } u
        ? $"Desde el {u.FirstUtc.ToLocalTime().ToString("d MMM", Es)} · la última vez, {TasksModel.Ago(u.LastUtc)}"
        : "";

    public bool TipWhenVisible => Shown is not null;

    /// <summary>Izquierda del pie: el estado de la sesión abierta o, sin ninguna, cuántas ha tenido.</summary>
    public string FooterLeft => Live is { } s
        ? MainViewModel.StatusWord(s)
        : SessionCount > 0 ? $"{SessionCount} {(SessionCount == 1 ? "sesión" : "sesiones")}" : "";

    /// <summary>Derecha del pie: «12,0M tokens · 4h57m», o que aún no ha terminado ningún prompt.</summary>
    /// <remarks>Sin coste propio y trabajada en sesiones de otra tarea, abiertas o cerradas, dice en qué tarea está su coste: no es que no haya prompts.</remarks>
    public string FooterRight => usage is { } u ? Spent(u) : CostElsewhere is { } principal ? $"coste en {principal}" : "sin prompts terminados aún";

    private static string Spent(TaskUsage u) =>
        u.Working > TimeSpan.Zero ? $"{Format.Tokens(u.NewTokens)} tokens · {Format.Duration(u.Working)}" : $"{Format.Tokens(u.NewTokens)} tokens";

    // Con una sesión abierta, teñido de su estado; sin ninguna, el fondo de bloque, para que se vea sin gritar.
    public IBrush FooterFill => Live is { } s ? MainViewModel.StatusTint(s.Status) : ThemeColors.Of("TaskSection", "#14FFFFFF");
    public IBrush FooterForeground => Live is null ? ThemeColors.Of("FgMuted", "#B3B6C4") : ThemeColors.Of("Fg", "#F2F3F7");

    // RAMA: el worktree que lleva su clave, si sigue en disco, con su PR y lo que tiene sin subir; si sobra, cómo cerrarlo.
    private (RepoWorktrees Repo, WorktreeInfo Item)? _tree;
    private bool _closeCopied;

    /// <summary>Pone el worktree de la tarea y repinta lo que depende de él.</summary>
    internal void SetWorktree((RepoWorktrees Repo, WorktreeInfo Item)? tree)
    {
        if (_tree?.Item == tree?.Item && _tree?.Repo.Name == tree?.Repo.Name) return;
        _tree = tree;
        foreach (var name in new[] { nameof(PrVisible), nameof(PrNumber), nameof(PrText), nameof(PrBrush), nameof(PrFill), nameof(TreeVisible),
                     nameof(TreeFolder), nameof(TreeLocal), nameof(TreeLocalBrush), nameof(CloseReason), nameof(CloseVisible), nameof(CloseCommand),
                     nameof(ClosePaste), nameof(CloseWarning), nameof(CloseWarningVisible), nameof(CloseButtonText) })
            OnChanged(name);
    }

    public bool PrVisible => _tree?.Item.Pr is not null;
    public string PrNumber => _tree?.Item.Pr is { } n ? $"#{n}" : "";

    /// <summary>La página web de la PR de su rama; null si no tiene o no se sabe cómo es la web de su remoto.</summary>
    public string? PrUrl => _tree is { } t && t.Item.Pr is { } n ? t.Repo.PullUrl(n) : null;
    public string? PrText => _tree is { } t ? WorktreeTexts.Pr(t.Item) : null;
    public IBrush PrBrush => WorktreeTexts.PrBrush(_tree?.Item);
    public IBrush PrFill => WorktreeTexts.PrFill(_tree?.Item);

    public bool TreeVisible => _tree is not null;
    public string TreeFolder => _tree is { } t ? $"{t.Repo.Name} · {t.Item.Folder}" : "";
    public string TreeLocal => _tree is { } t ? WorktreeTexts.Local(t.Item) : "";
    public IBrush TreeLocalBrush => _tree is { } t ? WorktreeTexts.LocalBrush(t.Item) : Brushes.Transparent;

    /// <summary>Por qué ya se puede cerrar su worktree: su PR está mergeada, la tarea cerrada o la carpeta ya no existe.</summary>
    public string? CloseReason => _tree is { } t ? WorktreeTexts.Leftover(t.Item, task.State) : null;
    public bool CloseVisible => CloseReason is not null;
    public string CloseCommand => _tree is { } t ? WorktreeTexts.CloseCommand(t.Repo, t.Item) : "";
    public string ClosePaste => _tree is { } t ? WorktreeTexts.PasteHint(t.Repo, t.Item) : "";
    public string? CloseWarning => _tree is { } t && CloseVisible ? WorktreeTexts.CloseWarning(t.Item) : null;
    public bool CloseWarningVisible => CloseWarning is not null;
    public string CloseButtonText => _closeCopied ? "Copiado" : $"Copiar «{CloseCommand}»";

    /// <summary>Dice en el botón, un momento, que la orden de cierre ya está copiada.</summary>
    internal void MarkCloseCopied(bool copied)
    {
        _closeCopied = copied;
        OnChanged(nameof(CloseButtonText));
    }

    // Detalle.
    public string StateText => InBacklog ? "Backlog" : StateName(task.State);
    public IBrush StateBrush => ColorOf(InBacklog ? TaskState.Backlog : task.State);
    public IBrush StateFill => InBacklog ? ThemeColors.Of("ButtonFill", "#22FFFFFF") : TaskStateFill(task.State);
    /// <summary>Los datos principales en una línea, para cuando están plegados: «Ana López · 6 h · Medium».</summary>
    public string FactsSummary => string.Join(" · ", new[] { task.Assignee ?? "Sin asignar", Estimate, task.Priority is { } p ? Capitalize(p.ToLowerInvariant()) : "" }
        .Where(s => s.Length > 0));

    // Cifras de las sesiones del detalle y de la ficha del pie, en grande: las suyas o, sin ellas, las de las sesiones compartidas.
    public string UsagePrompts => Shown?.Prompts.ToString(Es) ?? "0";
    public string UsageTokens => Shown is { } u ? Format.Tokens(u.NewTokens) : "0";
    public string UsageTime => Shown is { } u && u.Working > TimeSpan.Zero ? Format.Duration(u.Working) : "—";

    /// <summary>Bajo las cifras del detalle: si son de sesiones de otra tarea, que ya cuentan en ella; si tiene propias, lo compartido aparte.</summary>
    public string UsageNote => ShowingShared
        ? $"Son las cifras de {SessionWord(shared!.Sessions)} de otra tarea en que también se trabajó: ya cuentan en {shared.Principal}, la principal de la sesión."
        : SharedLine ?? "";

    public bool UsageNoteVisible => UsageNote.Length > 0;
    public bool LinkVisible => task.Url is not null;
    public bool DescriptionVisible => !string.IsNullOrWhiteSpace(task.Description);
    public bool NoDescriptionVisible => string.IsNullOrWhiteSpace(task.Description);

    /// <summary>La descripción formateada, como Markdown o como HTML; se hace la primera vez que se pide.</summary>
    public Control Document => _document ??= MarkdownView.Render(task.Description);

    /// <summary>Total de la tarea, encima de sus sesiones: «12 prompts · 4,2M tokens nuevos · Claude trabajando 1h 20m».</summary>
    public string UsageTotal => Shown is { } u
        ? $"{u.Prompts} {(u.Prompts == 1 ? "prompt" : "prompts")} · {Format.Tokens(u.NewTokens)} tokens nuevos" +
          (u.Working > TimeSpan.Zero ? $" · Claude trabajando {Format.Duration(u.Working)}" : "")
        : "";
    public bool UsageTotalVisible => Shown is not null;

    /// <summary>Las sesiones que se ven: las últimas, o todas si se ha pedido.</summary>
    public List<TaskSessionRow> Sessions => _allSessions ? _sessions ?? [] : (_sessions ?? []).Take(FirstSessions).ToList();
    public bool MoreSessionsVisible => _sessions is { Count: > FirstSessions };
    public string MoreSessionsText => _allSessions ? "Ver solo las últimas" : $"Ver todas ({_sessions?.Count ?? 0})";

    /// <summary>Enseña todas las sesiones, o vuelve a las últimas.</summary>
    internal void ToggleAllSessions()
    {
        _allSessions = !_allSessions;
        OnChanged(nameof(Sessions));
        OnChanged(nameof(MoreSessionsText));
    }
    public string SessionsTitle => _sessions is { Count: > 0 } s ? $"SESIONES ({s.Count})" : "SESIONES";
    public string SessionsNote => _sessionsNote;

    public bool SessionsNoteVisible => _sessionsNote.Length > 0;

    /// <summary>Pone las sesiones encontradas, o lo que se está haciendo con ellas.</summary>
    internal void SetSessions(List<TaskSessionRow>? sessions, string note)
    {
        _sessions = sessions;
        _sessionsNote = note;
        foreach (var name in new[] { nameof(Sessions), nameof(SessionsTitle), nameof(SessionsNote), nameof(SessionsNoteVisible), nameof(MoreSessionsVisible),
                     nameof(MoreSessionsText) })
            OnChanged(name);
    }

    /// <summary>Conserva las sesiones ya buscadas de la misma tarea al rehacer el tablero.</summary>
    internal void TakeSessions(TaskCardModel previous) => SetSessions(previous._sessions, previous._sessionsNote);

    /// <summary>Los datos de la tarea que el perfil sabe rellenar.</summary>
    public List<TaskFact> Facts
    {
        get
        {
            var facts = new List<TaskFact>();
            void Add(string name, string? value)
            {
                if (!string.IsNullOrWhiteSpace(value)) facts.Add(new TaskFact(name, value));
            }
            Add("Asignado", task.Assignee ?? (profile?.Has("task", "assignee") == true ? "Sin asignar" : null));
            Add("Estimación", Estimate);
            Add("Prioridad", task.Priority is { } p ? Capitalize(p.ToLowerInvariant()) : null);
            Add("Tipo", task.Type is { } t ? Capitalize(t.ToLowerInvariant()) : null);
            Add("Subtablero", look?.Name);
            Add("Vence", task.DueDate?.ToString("d 'de' MMMM", Es));
            Add("Creada", task.CreatedUtc?.ToLocalTime().ToString("d 'de' MMMM, HH:mm", Es));
            Add("Último cambio", task.UpdatedUtc?.ToLocalTime().ToString("d 'de' MMMM, HH:mm", Es));
            return facts;
        }
    }

    /// <summary>Nombre de una columna de la app.</summary>
    public static string StateName(TaskState state) => TaskStates.Name(state);

    /// <summary>Color de una columna de la app.</summary>
    public static IBrush ColorOf(TaskState state) => TaskStates.ColorOf(state);

    private static IBrush TaskStateFill(TaskState state) => state switch
    {
        TaskState.Todo => ThemeColors.Of("DataSonnet", "#61AFEF", 0.16),
        TaskState.Doing => ThemeColors.Of("DataFiveHour", "#E5C07B", 0.16),
        TaskState.Blocked => ThemeColors.Of("Danger", "#E03E4C", 0.16),
        TaskState.Review => ThemeColors.Of("DataFable", "#C678DD", 0.16),
        TaskState.Done => ThemeColors.Of("DataHaiku", "#98C379", 0.16),
        TaskState.ToDiscard => ThemeColors.Of("DataOpus", "#F09A76", 0.16),
        TaskState.Discarded => ThemeColors.Of("DataOther", "#ABB2BF", 0.16),
        _ => ThemeColors.Of("FgFaint", "#8A8D9C", 0.16),
    };

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpper(text[0], Es) + text[1..];

    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Reparte el ancho entre las columnas del tablero: las plegadas se llevan el suyo y el resto se reparte a partes iguales.</summary>
/// <remarks>El ancho fijo lo dice la columna de cada hijo, su contexto de datos: las columnas se rehacen al plegar una, y con ello el reparto.</remarks>
public sealed class LanePanel : Panel
{
    protected override Size MeasureOverride(Size available)
    {
        var widths = Widths(available.Width);
        var height = 0.0;
        for (var i = 0; i < Children.Count; i++)
        {
            Children[i].Measure(new Size(widths[i], available.Height));
            height = Math.Max(height, Children[i].DesiredSize.Height);
        }
        // Pide el alto de la columna más alta, no todo el disponible: en el tablero lo estira su celda al colocarlo,
        // y en la fila de cabeceras de los carriles tiene que dejar sitio debajo.
        return new Size(double.IsInfinity(available.Width) ? widths.Sum() : available.Width, Math.Min(height, available.Height));
    }

    protected override Size ArrangeOverride(Size final)
    {
        var widths = Widths(final.Width);
        var x = 0.0;
        for (var i = 0; i < Children.Count; i++)
        {
            Children[i].Arrange(new Rect(x, 0, widths[i], final.Height));
            x += widths[i];
        }
        return final;
    }

    /// <summary>El ancho fijo de un hijo, si es una columna plegada; NaN si se reparte.</summary>
    private static double FixedWidth(Control child) => child.DataContext is TaskColumnModel column ? column.FixedWidth : double.NaN;

    /// <summary>Ancho de cada columna dentro de <paramref name="total"/>.</summary>
    private double[] Widths(double total)
    {
        var fixedSum = Children.Select(FixedWidth).Where(w => !double.IsNaN(w)).Sum();
        var flexible = Children.Count(c => double.IsNaN(FixedWidth(c)));
        var share = flexible == 0 ? 0 : double.IsInfinity(total) ? 320 : Math.Max(0, (total - fixedSum) / flexible);
        return Children.Select(c => FixedWidth(c) is var w && double.IsNaN(w) ? share : w).ToArray();
    }
}

/// <summary>Colores de los tableros elegidos en Configuración, como «#3FB27F».</summary>
public static class BoardColors
{
    /// <summary>El pincel de un color hexadecimal; gris si no se entiende.</summary>
    public static IBrush BrushOf(string hex) =>
        HexColor.ToRgb(hex) is { } c ? new ImmutableSolidColorBrush(Color.FromRgb(c.R, c.G, c.B)) : Brushes.Gray;
}
