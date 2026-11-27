using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>El widget del Tablero: las columnas que se encienden con sus chips, una al lado de otra y del mismo ancho, o una
/// debajo de otra (en el menú, o según el espacio), con la franja de lo que te espera encima y el detalle de una tarea
/// dentro del widget. Nada se desplaza: cada columna enseña lo que cabe y «+ N más» la abre entera.</summary>
/// <remarks>
/// Solo para mirar y abrir: mover tareas, el backlog y la selección se quedan en el panel. Lee las mismas tareas que el
/// panel (<see cref="TaskDirectory"/>), sin leerlas otra vez, pero su tablero, su subtablero y sus columnas son suyos y
/// viajan con el corcho: el panel no se entera de lo que se elige aquí.
/// </remarks>
internal sealed class BoardWidget : CorkWidget
{
    // Las columnas de trabajo, que salen de serie; las de cierre se ponen desde el menú.
    private static readonly TaskState[] Working = [TaskState.Backlog, TaskState.Todo, TaskState.Doing, TaskState.Blocked, TaskState.Review];
    private static readonly TaskState[] All =
        [TaskState.Backlog, TaskState.Todo, TaskState.Doing, TaskState.Blocked, TaskState.Review, TaskState.ToDiscard, TaskState.Discarded, TaskState.Done];

    // Ancho a partir del cual una columna cabe al lado de otra según el espacio, y el del detalle al lado en L.
    private const double ColumnWidth = 210;
    private const double DetailWidth = 340;

    private readonly TaskDirectory _directory = TaskDirectory.Instance;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(5) };
    private string? _error;
    private bool _loading;
    private string _query = "";
    private bool _searching;
    private TextBox? _search;
    private string? _selected;
    private TaskState? _expanded;

    // En qué columna estaba cada tarea la última vez que se leyó el tablero, y las que se han movido desde entonces: de cuál
    // vienen, a cuál han ido y cuándo. Del tablero que se ve; al cambiar de tablero se empieza de nuevo.
    private readonly Dictionary<string, TaskState> _columns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (TaskState From, TaskState To, DateTime At)> _moves = new(StringComparer.OrdinalIgnoreCase);
    private string? _tracked;
    private bool _waiting;

    // Lo que una tarea movida dice de dónde viene.
    private static readonly TimeSpan MoveShown = TimeSpan.FromMinutes(10);

    /// <summary>Crea el widget; no lee nada hasta que se ve.</summary>
    public BoardWidget()
    {
        _clock.Tick += (_, _) => Render();
    }

    public override PanelSection Section => PanelSection.Tasks;

    /// <summary>Ámbar si una sesión de alguna tarea te espera; nada si no.</summary>
    public override IBrush? Mood => _waiting ? Brushes_.Waiting : null;

    /// <summary>El tablero que enseña, para la cabecera.</summary>
    public override string? Headline => Board()?.Label;

    /// <summary>Cuándo se leyó el tablero que enseña.</summary>
    public override DateTime? RefreshedUtc => Board() is { } board ? _directory.ReadOf(board.Id)?.ReadUtc : null;

    protected override void Attached()
    {
        _directory.Changed += Render;
        WorktreeMonitor.Instance.Changed += Render;
        RepoColors.Changed += Render;
    }

    protected override void Detached()
    {
        _directory.Changed -= Render;
        WorktreeMonitor.Instance.Changed -= Render;
        RepoColors.Changed -= Render;
        _clock.Stop();
    }

    protected override void Started()
    {
        _clock.Start();
        _ = LoadAsync(force: false);
    }

    protected override void Stopped() => _clock.Stop();

    // Cambia de forma al pasar de columnas a apilado o al revés, que según el espacio depende de si caben a lo ancho.
    protected override object LayoutKey(double width) => (SizeOf(width), IsStacked(width));

    /// <summary>La vista elegida en el menú: «auto» (según el espacio), «columns» o «stacked».</summary>
    private string Look => Get("look") ?? "auto";

    /// <summary>Si las columnas van una debajo de otra: si se elige así o, según el espacio, en S o si no caben a lo ancho.</summary>
    private bool IsStacked(double width)
    {
        if (Look != "auto") return Look == "stacked";
        var size = SizeOf(width);
        return size == WidgetSize.S || Shown().Count > Fit(width - Padding.Left - Padding.Right, _selected is not null && size == WidgetSize.L);
    }

    /// <summary>El tablero que enseña: el elegido en el widget, el último del panel o el primero.</summary>
    private BoardOption? Board()
    {
        var boards = _directory.Boards();
        return boards.FirstOrDefault(b => b.Id == Get("board")) ?? boards.FirstOrDefault(b => b.Id == _directory.Settings.LastBoard) ?? boards.FirstOrDefault();
    }

    /// <summary>Las columnas que enseña: las marcadas en el menú, en el orden del tablero.</summary>
    private List<TaskState> Shown()
    {
        var marked = GetSet("cols", Working.Select(s => s.ToString()));
        return All.Where(s => marked.Contains(s.ToString())).ToList();
    }

    /// <summary>Lee las tareas del tablero si aún no se han leído, o si se pide.</summary>
    private async Task LoadAsync(bool force)
    {
        if (Board() is not { } board || _loading) return;
        if (!force && _directory.ReadOf(board.Id) is not null) return;
        _loading = true;
        Render();
        try
        {
            await _directory.ReadAsync(board);
            _error = null;
        }
        catch (BoardException ex)
        {
            _error = $"No se ha podido leer {board.Connection.Name}: {ex.Message}";
        }
        catch (Exception ex)
        {
            JsonStore.LogError("board-widget", ex);
            _error = $"No se ha podido leer {board.Connection.Name}. El detalle está en el registro de errores.";
        }
        finally
        {
            _loading = false;
            Render();
        }
    }

    public override IEnumerable<Control> MenuItems()
    {
        yield return Ui.Caption("Vista");
        foreach (var (id, name) in new[] { ("auto", "Según el espacio"), ("columns", "En columnas"), ("stacked", "Apilada") })
            yield return Ui.Check(name, Look == id, () =>
            {
                Set("look", id == "auto" ? null : id);
                Render();
            });
        yield return new Separator();
        yield return Ui.Caption("Columnas");
        var shown = Shown();
        foreach (var state in All)
        {
            var on = shown.Contains(state);
            yield return Ui.Check(TaskStates.Name(state), on, () =>
            {
                var next = on ? shown.Where(s => s != state) : shown.Append(state);
                SetSet("cols", next.Select(s => s.ToString()));
                Render();
            });
        }
        yield return new Separator();
        yield return Ui.Item("Volver a leer el tablero", () => _ = LoadAsync(force: true));
    }

    protected override Control Build()
    {
        if (Board() is not { } board)
            return Ui.Empty("Aún no hay ningún tablero elegido. Se eligen en Configuración → Tableros.");
        var cards = Cards(board);
        Track(board, cards);
        var selected = cards.Values.SelectMany(c => c).Where(c => c.Card.Key == _selected)
            .Select(c => ((TaskCardModel Card, SessionState? Live)?)c).FirstOrDefault();
        if (selected is null) _selected = null;
        if (selected is not null && Size != WidgetSize.L) return Over(Detail(selected.Value), () => Pick(null), OpenButton(selected.Value.Card.Key));
        if (_expanded is { } whole && _directory.ReadOf(board.Id) is not null)
        {
            var all = Over(Whole(whole, cards.GetValueOrDefault(whole) ?? []), () => Expand(null));
            return selected is null ? all : WithDetail(all, selected.Value);
        }

        var dock = new DockPanel();
        void Top(Control control)
        {
            control.Margin = new Thickness(0, 0, 0, 8);
            DockPanel.SetDock(control, Dock.Top);
            dock.Children.Add(control);
        }
        Top(Bar(board));
        if (_searching) Top(SearchBox());
        if (_error is not null) Top(Ui.Warning(_error));
        var waiting = cards.Values.SelectMany(c => c).Where(c => c.Live?.Status == SessionStatus.Waiting).ToList();
        _waiting = waiting.Count > 0;
        if (waiting.Count > 0) Top(Waiting(waiting));

        if (_directory.ReadOf(board.Id) is null)
        {
            dock.Children.Add(Ui.Empty(_loading ? "Leyendo el tablero…" : "Sin leer todavía."));
            return dock;
        }
        var shown = Shown();
        Top(ColumnChips(shown, cards));
        if (shown.Count == 0)
        {
            dock.Children.Add(Ui.Empty("No hay ninguna columna encendida: se encienden con los chips de arriba."));
            return dock;
        }
        var grid = IsStacked(Bounds.Width) ? Stacked(shown, cards)
            : Ui.Columns(shown.Count, 8, shown.Select(s => Column(s, cards.GetValueOrDefault(s) ?? [])), stretch: true);
        dock.Children.Add(selected is null ? grid : WithDetail(grid, selected.Value));
        return dock;
    }

    /// <summary>Lo dado con el detalle de una tarea al lado, en L.</summary>
    private Grid WithDetail(Control content, (TaskCardModel Card, SessionState? Live) selected)
    {
        var split = new Grid { ColumnDefinitions = new ColumnDefinitions($"*,{DetailWidth}") };
        split.Children.Add(content);
        var side = new Border
        {
            Background = Ui.Section, CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8), Margin = new Thickness(8, 0, 0, 0),
            Child = Over(Detail(selected), () => Pick(null), OpenButton(selected.Card.Key)),
        };
        Grid.SetColumn(side, 1);
        split.Children.Add(side);
        return split;
    }

    /// <summary>Cuántas columnas caben a lo ancho, dejando sitio al detalle si hay una tarea abierta al lado.</summary>
    private static int Fit(double width, bool detail) => Math.Max(1, (int)((width - (detail ? DetailWidth + 8 : 0) + 8) / (ColumnWidth + 8)));

    /// <summary>Las columnas encendidas una debajo de otra, cada una plegable, repartiéndose el alto: cada una enseña las
    /// tarjetas que le caben y «+ N más»; nada se desplaza.</summary>
    private Control Stacked(List<TaskState> shown, Dictionary<TaskState, List<(TaskCardModel Card, SessionState? Live)>> cards)
    {
        var rows = new SharedRows();
        foreach (var state in shown)
        {
            var items = cards.GetValueOrDefault(state) ?? [];
            rows.Children.Add(FoldHead(state.ToString(), Ui.Dot(TaskStates.ColorOf(state)), Ui.Text(TaskStates.Name(state), 12, bold: true),
                Ui.Text(items.Count.ToString(), 11, Ui.Faint, mono: true)));
            if (Folded(state.ToString()))
            {
                foreach (var item in items) Note("cards", item.Card.Key, state.ToString(), state.ToString());
                continue;
            }
            Control body = items.Count == 0 ? None() : Fitted(state, items);
            body.Margin = new Thickness(0, 2, 0, 8);
            rows.Children.Add(body);
        }
        return rows;
    }

    /// <summary>Las tarjetas de una columna que caben en su alto y, debajo, «+ N más», que abre la columna entera.</summary>
    private FitList Fitted(TaskState state, List<(TaskCardModel Card, SessionState? Live)> items)
    {
        var list = new FitList(() => Expand(state));
        foreach (var item in items) list.Add(Watched(item, state));
        return list;
    }

    /// <summary>Lo que dice una columna sin tareas, arriba del todo.</summary>
    private static Control None()
    {
        var none = Ui.Text("Ninguna", 11, Ui.Faint);
        none.VerticalAlignment = VerticalAlignment.Top;
        return none;
    }

    /// <summary>Abre una columna entera dentro del widget, o la cierra con null.</summary>
    private void Expand(TaskState? state)
    {
        _expanded = state;
        Render();
    }

    /// <summary>Una columna entera, con todas sus tarjetas y su desplazamiento: lo que abre «+ N más».</summary>
    private Control Whole(TaskState state, List<(TaskCardModel Card, SessionState? Live)> items)
    {
        var list = new StackPanel { Spacing = 6 };
        list.Children.Add(Ui.Row(6, Ui.Dot(TaskStates.ColorOf(state)), Ui.Text(TaskStates.Name(state), 13, bold: true),
            Ui.Text(items.Count.ToString(), 11, Ui.Faint, mono: true)));
        foreach (var item in items) list.Children.Add(Watched(item, state));
        if (items.Count == 0) list.Children.Add(None());
        return Scroll($"whole:{state}", list);
    }

    /// <summary>Abre el detalle de una tarea, o lo cierra con null.</summary>
    private void Pick(string? key)
    {
        _selected = key;
        Render();
    }

    /// <summary>La barra: el tablero y el subtablero en un desplegable, cuándo se leyó y los botones de buscar y releer.</summary>
    private Control Bar(BoardOption board)
    {
        var bar = new DockPanel();
        var right = Ui.Row(6);
        right.HorizontalAlignment = HorizontalAlignment.Right;
        if (Size != WidgetSize.S && _directory.ReadOf(board.Id) is { } read)
            right.Children.Add(Ui.Text(_loading ? "Leyendo…" : TasksModel.Ago(read.ReadUtc), 11, Ui.Faint));
        right.Children.Add(Ui.IconButton("", () =>
        {
            _searching = !_searching;
            if (!_searching)
            {
                _query = "";
                _search = null;
            }
            Render();
        }, DiaryInk.Card("Buscar", "Por clave o título, solo en este widget.")));
        right.Children.Add(Ui.IconButton("", () => _ = LoadAsync(force: true), DiaryInk.Card("Volver a leer", "Pide otra vez las tareas al tablero.")));
        DockPanel.SetDock(right, Dock.Right);
        bar.Children.Add(right);

        var sub = Get("sub");
        var looks = Looks(board);
        var label = sub is not null && looks.TryGetValue(sub, out var look) ? $"{board.Label} › {look.Name}" : board.Label;
        var picker = new Button
        {
            Content = Ui.Row(6, Ui.Dot(BoardColors.BrushOf(board.Connection.Color)), Ui.Text(label, 12, bold: true), Ui.Icon("", 9)), Focusable = false,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        DiaryInk.Tip(picker, DiaryInk.Card("Tablero y subtablero", "Los de este widget: el panel sigue con los suyos."));
        if (Application.Current?.FindResource("ChipButton") is Avalonia.Styling.ControlTheme theme) picker.Theme = theme;
        picker.Click += (_, _) => BoardMenu(board, looks).Open(picker);
        bar.Children.Add(picker);
        return bar;
    }

    /// <summary>El menú del desplegable: los tableros y, si se mira un proyecto entero, sus subtableros.</summary>
    private ContextMenu BoardMenu(BoardOption board, Dictionary<string, BoardLook> looks)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Ui.Caption("Tablero"));
        foreach (var option in _directory.Boards())
            menu.Items.Add(Ui.Check(option.Label, option.Id == board.Id, () =>
            {
                Set("board", option.Id);
                Set("sub", null);
                _selected = null;
                Render();
                _ = LoadAsync(force: false);
            }));
        if (looks.Count == 0) return menu;
        menu.Items.Add(new Separator());
        menu.Items.Add(Ui.Caption("Subtablero"));
        menu.Items.Add(Ui.Check("Todos", Get("sub") is null, () =>
        {
            Set("sub", null);
            Render();
        }));
        foreach (var (id, look) in looks.OrderBy(l => l.Value.Name, StringComparer.CurrentCultureIgnoreCase))
            menu.Items.Add(Ui.Check(look.Name, Get("sub") == id, () =>
            {
                Set("sub", id);
                Render();
            }));
        return menu;
    }

    /// <summary>La caja de búsqueda, que filtra este widget por clave o título.</summary>
    /// <remarks>Es siempre la misma caja, sacada del dibujo anterior: una nueva en cada tecla perdería el foco.</remarks>
    private Control SearchBox()
    {
        if (_search is null)
        {
            _search = new TextBox { PlaceholderText = "Buscar por clave o título", FontSize = 12 };
            _search.TextChanged += (_, _) =>
            {
                if (_query == (_search.Text ?? "")) return;
                _query = _search.Text ?? "";
                Render();
            };
            Dispatcher.UIThread.Post(() => _search.Focus());
        }
        (_search.Parent as Panel)?.Children.Remove(_search);
        _search.Text = _query;
        return _search;
    }

    /// <summary>La franja de lo que te espera: las tareas con una sesión que pide algo, la primera con su título.</summary>
    private Control Waiting(List<(TaskCardModel Card, SessionState? Live)> waiting)
    {
        var first = waiting[0];
        var line = Ui.Row(7, Ui.Dot(Ui.Wait), Ui.Text("Te espera", 12, Ui.Wait, bold: true), Ui.Text(first.Card.Key, 11, Ui.Muted, mono: true));
        var title = Ui.Text(first.Card.Title, 12);
        var dock = new DockPanel();
        DockPanel.SetDock(line, Dock.Left);
        dock.Children.Add(line);
        if (waiting.Count > 1)
        {
            var more = Ui.Text($"y {waiting.Count - 1} más", 11, Ui.Muted);
            more.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(more, Dock.Right);
            dock.Children.Add(more);
        }
        title.Margin = new Thickness(8, 0, 0, 0);
        dock.Children.Add(title);
        var strip = Ui.Card(dock, () => Pick(first.Card.Key), fill: ThemeColors.Of("DataFiveHour", "#E5C07B", 0.12), edge: ThemeColors.Of("DataFiveHour", "#E5C07B", 0.4));
        DiaryInk.Tip(strip, DiaryInk.Card("Te espera", "Una sesión de esta tarea pide algo: un permiso, una pregunta o un plan.",
            rows: waiting.Select(w => (w.Card.Key, w.Card.Title))));
        return strip;
    }

    /// <summary>La tarjeta de una tarea, apuntada para los avisos de cambio: destella al llegar a su columna.</summary>
    private Control Watched((TaskCardModel Card, SessionState? Live) item, TaskState state) =>
        Watch(Card(item), "cards", item.Card.Key, state.ToString(), Ui.Wait, state.ToString());

    /// <summary>Apunta en qué columna está cada tarea y, si alguna ha cambiado desde la última lectura, de dónde viene.</summary>
    /// <remarks>La primera lectura de un tablero solo apunta: nada cuenta como movido.</remarks>
    private void Track(BoardOption board, Dictionary<TaskState, List<(TaskCardModel Card, SessionState? Live)>> cards)
    {
        if (_directory.ReadOf(board.Id) is null) return;
        var first = _tracked != board.Id;
        if (first)
        {
            _tracked = board.Id;
            _columns.Clear();
            _moves.Clear();
        }
        var now = DateTime.UtcNow;
        foreach (var (state, items) in cards)
            foreach (var (card, _) in items)
            {
                if (!first && _columns.TryGetValue(card.Key, out var was) && was != state) _moves[card.Key] = (was, state, now);
                _columns[card.Key] = state;
            }
        foreach (var stale in _moves.Where(m => now - m.Value.At > MoveShown).Select(m => m.Key).ToList()) _moves.Remove(stale);
    }

    /// <summary>De qué columna viene una tarea que se acaba de mover; null si no se ha movido hace poco.</summary>
    private (TaskState From, DateTime At)? MovedFrom(string key) =>
        _moves.TryGetValue(key, out var move) && _columns.GetValueOrDefault(key) == move.To ? (move.From, move.At) : null;

    /// <summary>Un chip por columna, con cuántas tareas lleva, que la enciende o la apaga: se ven todas las encendidas.</summary>
    /// <remarks>Salen las de trabajo y las de cierre que estén encendidas; las de cierre se encienden en el menú ···.</remarks>
    private Control ColumnChips(List<TaskState> shown, Dictionary<TaskState, List<(TaskCardModel Card, SessionState? Live)>> cards)
    {
        var chips = new WrapPanel();
        foreach (var state in All.Where(s => Working.Contains(s) || shown.Contains(s)))
        {
            var on = shown.Contains(state);
            var content = Ui.Row(5, Ui.Dot(TaskStates.ColorOf(state), 6), Ui.Text(TaskStates.Name(state), 11.5, bold: on),
                Ui.Text((cards.GetValueOrDefault(state)?.Count ?? 0).ToString(), 10.5, Ui.Faint, mono: true));
            // Una tarea acaba de llegar a una columna que no se ve, apagada o plegada.
            if ((!on || Folded(state.ToString())) && _moves.Values.Any(m => m.To == state)) content.Children.Add(Ui.NewsDot());
            var chip = Ui.Chip(content, on, () =>
            {
                SetSet("cols", All.Where(s => s == state ? !on : shown.Contains(s)).Select(s => s.ToString()));
                Render();
            });
            chip.Margin = new Thickness(0, 0, 5, 5);
            chips.Children.Add(chip);
        }
        return chips;
    }

    /// <summary>Una columna: su punto de color, su nombre, cuántas tareas lleva y las tarjetas que le caben, con «+ N más».</summary>
    private Control Column(TaskState state, List<(TaskCardModel Card, SessionState? Live)> cards)
    {
        var head = Ui.Row(6, Ui.Dot(TaskStates.ColorOf(state)), Ui.Text(TaskStates.Name(state), 12, bold: true), Ui.Text(cards.Count.ToString(), 11, Ui.Faint, mono: true));
        head.Margin = new Thickness(2, 0, 0, 6);
        var dock = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        dock.Children.Add(head);
        dock.Children.Add(cards.Count == 0 ? None() : Fitted(state, cards));
        return new Border { Background = Ui.Section, CornerRadius = new CornerRadius(8), Padding = new Thickness(7), Child = dock };
    }

    /// <summary>La tarjeta media de una tarea: su título en dos líneas, su clave, su PR y su sesión; al pulsarla, su detalle.</summary>
    private Control Card((TaskCardModel Card, SessionState? Live) item)
    {
        var (card, live) = item;
        var title = Ui.Text(card.Title, 12, wrap: true);
        title.MaxLines = 2;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        var meta = Ui.Row(6, Ui.Text(card.Key, 10.5, Ui.Faint, mono: true));
        if (card.PrVisible) meta.Children.Add(Ui.Pill(card.PrNumber, card.PrBrush, card.PrFill));
        if (live is not null)
            meta.Children.Add(Ui.Row(4, Ui.Dot(MainViewModel.StatusBrush(live.Status), 6), Ui.Text(MainViewModel.StatusWord(live), 10.5, MainViewModel.StatusBrush(live.Status))));
        var body = Ui.Column(4, title, meta);
        if (MovedFrom(card.Key) is { } moved)
            body.Children.Add(Ui.Text($"← de {TaskStates.Name(moved.From)} · {TasksModel.Ago(moved.At)}", 10.5, Ui.Wait, bold: true));
        var tile = Ui.Card(body, () => Pick(card.Key), stripe: card.StripeVisible ? card.DotBrush : null, on: card.Key == _selected);
        DiaryInk.Tip(tile, DiaryInk.Card(card.Title, card.Key, rows: Facts(card)));
        return tile;
    }

    /// <summary>Los datos de una tarea para su ficha y su detalle: tipo, prioridad, estimación y quién la lleva.</summary>
    private static List<(string Label, string Value)> Facts(TaskCardModel card)
    {
        var facts = new List<(string, string)>();
        if (card.Task.Type is { } type) facts.Add(("Tipo", card.TypeTip));
        if (card.PriorityTip is { } priority) facts.Add(("Prioridad", priority.Replace("Prioridad ", "")));
        if (card.EstimateVisible) facts.Add(("Estimación", card.Estimate));
        if (card.Assignee is { } who) facts.Add(("La lleva", who));
        return facts;
    }

    /// <summary>El detalle de una tarea: su estado, su título, sus datos, su rama, sus sesiones y su descripción.</summary>
    private Control Detail((TaskCardModel Card, SessionState? Live) item)
    {
        var (card, live) = item;
        var list = new StackPanel { Spacing = 8 };
        list.Children.Add(Ui.Row(6, Ui.Dot(TaskStates.ColorOf(card.Task.State)), Ui.Text(TaskStates.Name(card.Task.State), 11, Ui.Muted),
            Ui.Text(card.Key, 11, Ui.Faint, mono: true)));
        list.Children.Add(Ui.Text(card.Title, 14, bold: true, wrap: true));
        var facts = Facts(card);
        if (facts.Count > 0)
            list.Children.Add(Ui.Block(facts.Select(f => (Control)Ui.Row(6, Ui.Text(f.Label, 11, Ui.Faint), Ui.Text(f.Value, 11.5))).ToArray()));
        if (card.TreeVisible)
        {
            var tree = new List<Control> { Ui.Label("Rama"), Ui.Text(card.TreeFolder, 11.5, mono: true) };
            if (card.PrText is { } pr) tree.Add(PrLine(pr, card.PrBrush, card.PrFill, card.PrUrl));
            tree.Add(Ui.Text(card.TreeLocal, 11, card.TreeLocalBrush));
            list.Children.Add(Ui.Block(tree.ToArray()));
        }
        var sessions = new List<Control> { Ui.Label("Sesiones") };
        if (live is not null)
        {
            sessions.Add(Ui.Row(6, Ui.Dot(MainViewModel.StatusBrush(live.Status)), Ui.Text(MainViewModel.StatusWord(live), 11.5, MainViewModel.StatusBrush(live.Status))));
            sessions.Add(Ui.Link("Ver sesión ↗", () => SessionActions.RequestSession(live.SessionId)));
        }
        sessions.Add(Ui.Text(card.FooterRight, 11, Ui.Muted, wrap: true));
        list.Children.Add(Ui.Block(sessions.ToArray()));
        var description = new List<Control> { Ui.Label("Descripción") };
        description.Add(card.DescriptionVisible ? MarkdownView.Render(card.Task.Description) : Ui.Text("Sin descripción.", 12, Ui.Faint));
        list.Children.Add(Ui.Block(description.ToArray()));
        return Scroll($"detail:{card.Key}", list);
    }

    /// <summary>La PR de una rama, en su pastilla, con el enlace que abre su página si se sabe.</summary>
    internal static Control PrLine(string text, IBrush brush, IBrush fill, string? url)
    {
        var line = Ui.Row(8, Ui.Pill(text, brush, fill));
        if (url is not null) line.Children.Add(Ui.Link("Abrir la PR ↗", () => SessionsView.OpenLink(url)));
        return line;
    }

    /// <summary>El botón que abre la tarea en el Tablero del panel.</summary>
    private static Control OpenButton(string key) => Ui.Link("Abrir en el panel ↗", () => SessionActions.RequestTask(key));

    /// <summary>Nombre y color de cada subtablero del proyecto elegido entero, por su id; vacío si se mira un subtablero.</summary>
    private Dictionary<string, BoardLook> Looks(BoardOption board)
    {
        if (board.Selection.BoardId is not null) return new(StringComparer.OrdinalIgnoreCase);
        return _directory.SubBoardsOf(board.Id)
            .GroupBy(b => b.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => new BoardLook(g.First().Name, TasksModel.SubBoardBrush(board.Connection, g.First())), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Las tarjetas de cada columna, con la sesión abierta que más pide atención de cada tarea.</summary>
    /// <remarks>Se colocan como en el panel (con su planificación local), con el subtablero y la búsqueda de este widget.</remarks>
    private Dictionary<TaskState, List<(TaskCardModel Card, SessionState? Live)>> Cards(BoardOption board)
    {
        var result = new Dictionary<TaskState, List<(TaskCardModel, SessionState?)>>();
        if (_directory.ReadOf(board.Id) is not { } read) return result;
        var looks = Looks(board);
        var sub = Get("sub") is { } id && looks.ContainsKey(id) ? new HashSet<string>([id], StringComparer.OrdinalIgnoreCase) : null;
        var filter = new TaskFilter(Query: _query.Length > 0 ? _query : null, BoardIds: sub);
        var view = TaskBoard.Arrange(read.Tasks, _directory.Store.LoadPlan(), board.Connection.Id, board.Id, filter);
        var usage = _directory.UsageByTask();
        var shared = _directory.SharedByTask();
        var open = Pieces.Sessions()
            .SelectMany(s => _directory.TasksOfSession(s.SessionId, s.Project).Tasks.Select(k => (Key: k, Session: s)))
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Session).ToList(), StringComparer.OrdinalIgnoreCase);
        var trees = WorktreeMonitor.Instance.ByKey(_directory.Settings);
        (TaskCardModel, SessionState?) Make(BoardTask task, TaskState state)
        {
            var sessions = open.GetValueOrDefault(task.Key) ?? [];
            var card = new TaskCardModel(task, TaskCardModel.PlaceOf(state), board.Profile, usage.GetValueOrDefault(task.Key), sessions,
                task.BoardId is { } b ? looks.GetValueOrDefault(b) : null, null, SubBoardStyle.Subtle, shared.GetValueOrDefault(task.Key));
            card.SetWorktree(trees.TryGetValue(task.Key, out var tree) ? tree : null);
            var live = sessions.OrderBy(s => s.Status switch
            {
                SessionStatus.Waiting => 0,
                SessionStatus.Working or SessionStatus.Agents or SessionStatus.Command => 1,
                SessionStatus.Done => 2,
                _ => 3,
            }).FirstOrDefault();
            return (card, live);
        }
        result[TaskState.Backlog] = view.Backlog.Select(t => Make(t, TaskState.Backlog)).ToList();
        foreach (var state in TaskBoardView.Order) result[state] = view[state].Select(t => Make(t, state)).ToList();
        return result;
    }
}
