using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>El widget de Artifacts: lo publicado, de lo último a lo primero, agrupado por días (hoy, ayer, esta semana…) o por
/// repositorio, con la hora o el día de cada uno; en L, los grupos en columnas y cada uno en su ficha. Pulsar uno lo abre
/// donde se elija en el menú: dentro del widget, en el Navegador del corcho o en el navegador del sistema.</summary>
/// <remarks>
/// Lee lo mismo que la sección (<see cref="ArtifactMonitor"/>), lo de este equipo y lo traído de las galerías de claude.ai,
/// mezclado por fecha. La clase, la agrupación y dónde se abre son del widget. Lo publicado desde la última vez que se miró el
/// widget lleva un punto, y lo ya abierto desde él va apagado.
/// </remarks>
internal sealed class ArtifactsWidget : CorkWidget
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
    private static readonly ArtifactKind[] Kinds =
        [ArtifactKind.Page, ArtifactKind.Design, ArtifactKind.Animation, ArtifactKind.Document, ArtifactKind.Slides, ArtifactKind.Dashboard, ArtifactKind.Other, ArtifactKind.Unknown];

    // Ancho de la lista estrecha al lado de lo abierto, en L.
    private const double ListWidth = 300;

    private string _query = "";
    private bool _searching;
    private TextBox? _search;
    private AgentArtifact? _open;
    private BrowserPane? _browser;

    // Desde cuándo cuenta como nuevo lo publicado: la última vez que se miró el widget antes de esta. Se lee una vez, de lo
    // guardado con el corcho, y avanza al dejar de mirarlo; y desde cuándo se está mirando ahora.
    private DateTime _since = DateTime.UtcNow.AddDays(-1);
    private bool _sinceRead;
    private DateTime? _shownAt;

    // Lo que hay que mirarlo para que lo nuevo pase a visto: un vistazo de paso al cambiar de pestaña no cuenta.
    private static readonly TimeSpan Looked = TimeSpan.FromSeconds(3);

    // Cuántos enlaces abiertos se recuerdan, para apagarlos en la lista.
    private const int OpenedKept = 150;

    public override PanelSection Section => PanelSection.Artifacts;

    protected override void Attached()
    {
        ArtifactMonitor.Instance.Changed += OnChanged;
        RepoColors.Changed += Render;
    }

    protected override void Detached()
    {
        ArtifactMonitor.Instance.Changed -= OnChanged;
        RepoColors.Changed -= Render;
    }

    /// <summary>Al verse, lo nuevo es lo publicado desde la última vez que se miró, leído de lo guardado la primera vez; y se
    /// apunta que se mira ahora, para la próxima vez que se abra la app.</summary>
    protected override void Started()
    {
        if (!_sinceRead)
        {
            _sinceRead = true;
            if (long.TryParse(Get("seen"), out var ticks)) _since = new DateTime(ticks, DateTimeKind.Utc);
        }
        _shownAt = DateTime.UtcNow;
        Set("seen", DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
        ArtifactMonitor.Instance.ScanIfDue();
    }

    /// <summary>Al recuperar lo guardado con el corcho, lo nuevo vuelve a contar desde la última vez que se miró.</summary>
    protected override void Restored()
    {
        if (long.TryParse(Get("seen"), out var ticks)) _since = new DateTime(ticks, DateTimeKind.Utc);
        _sinceRead = true;
    }

    /// <summary>Al dejar de verse tras mirarlo un rato, lo publicado hasta ahora pasa a visto.</summary>
    protected override void Stopped()
    {
        if (_shownAt is { } at && DateTime.UtcNow - at >= Looked)
        {
            _since = DateTime.UtcNow;
            Set("seen", _since.Ticks.ToString(CultureInfo.InvariantCulture));
        }
        _shownAt = null;
    }

    /// <summary>Ha cambiado lo publicado: se apunta cuándo se leyó y se repinta.</summary>
    private void OnChanged()
    {
        Refreshed();
        Render();
    }

    /// <summary>Verde si hay algo nuevo sin abrir; nada si no.</summary>
    public override IBrush? Mood => Visible().Any(IsNew) ? ArtifactTexts.Green() : null;

    /// <summary>Cuántos nuevos hay o, si ninguno, cuántos.</summary>
    public override string? Headline
    {
        get
        {
            var all = Visible();
            var fresh = all.Count(IsNew);
            if (fresh > 0) return fresh == 1 ? "1 nuevo" : $"{fresh} nuevos";
            return all.Count == 0 ? null : ArtifactTexts.Count(all.Count);
        }
    }

    /// <summary>Lo publicado que no se ha quitado ni borrado, de cualquier clase.</summary>
    private static List<AgentArtifact> Visible() => ArtifactMonitor.Instance.Items.Where(a => !a.Removed && !a.Deleted).ToList();

    /// <summary>Los enlaces abiertos desde el widget, del último al primero.</summary>
    private List<string> Opened() => Get("opened") is { } saved ? saved.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList() : [];

    /// <summary>Si un artifact ya se abrió desde el widget.</summary>
    private bool WasOpened(AgentArtifact item) => Opened().Contains(item.Url, StringComparer.Ordinal);

    /// <summary>Si es nuevo: publicado o actualizado desde la última vez que se miró el widget, y sin abrir.</summary>
    private bool IsNew(AgentArtifact item) => Last(item) > _since && !WasOpened(item);

    /// <summary>Apunta que un artifact se ha abierto, el primero de la lista, sin pasar de los que se recuerdan.</summary>
    /// <remarks>Los enlaces van separados por espacios: un enlace no lleva ninguno.</remarks>
    private void MarkOpened(AgentArtifact item)
    {
        var opened = Opened().Where(u => u != item.Url).Prepend(item.Url).Take(OpenedKept);
        Set("opened", string.Join(' ', opened));
    }

    /// <summary>La clase que enseña el widget; null si todas.</summary>
    private ArtifactKind? Kind => Enum.TryParse<ArtifactKind>(Get("kind"), out var kind) ? kind : null;

    /// <summary>Si se agrupa por repositorio; si no, por días.</summary>
    private bool ByRepo => Get("group") == "repos";

    // Los sitios donde se abre un artifact, para el menú del widget y el del clic derecho.
    private static readonly (string Id, string Name)[] Targets =
        [("widget", "Este widget"), ("cork", "El navegador del corcho"), ("system", "El navegador del sistema")];

    /// <summary>Dónde se abre lo pulsado: «widget» (de serie), «cork» (el Navegador del corcho) o «system».</summary>
    private string Target => Get("target") ?? "widget";

    public override IEnumerable<Control> MenuItems()
    {
        yield return Ui.Caption("Abrir en");
        foreach (var (id, name) in Targets)
            yield return Ui.Check(name, Target == id, () =>
            {
                Set("target", id == "widget" ? null : id);
                Render();
            });
        yield return new Separator();
        if (_open is { } open) yield return Ui.Item("Abrir lo de ahora en el navegador del sistema", () => SessionsView.OpenLink(open.Url));
        yield return Ui.Item("Buscar de nuevo lo publicado", () => ArtifactMonitor.Instance.ScanIfDue());
    }

    /// <summary>El grupo de un artifact según lo elegido: su día o su repositorio.</summary>
    private string GroupOf(AgentArtifact item) => ByRepo ? ArtifactTexts.Home(item) : DayOf(item);

    /// <summary>Cuándo, en su fila: por días, la hora o el día de su grupo; por repositorio, también «ayer» para no confundirlo con hoy.</summary>
    private string WhenIn(AgentArtifact item) => ByRepo && (DateTime.Today - Last(item).ToLocalTime().Date).Days == 1 ? "ayer" : WhenOf(item);

    /// <summary>La cabecera de un grupo: su nombre, con el color de su repositorio si se agrupa así.</summary>
    private Control GroupHead(string name, int count, bool label)
    {
        var text = label ? (Control)Ui.Label(name) : Ui.Text(name, 12, bold: true);
        var head = Ui.Row(6, text, Ui.Text(count.ToString(), 11, Ui.Faint, mono: true));
        if (ByRepo) head.Children.Insert(0, new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(2), Background = RepoColors.Of(name) });
        return head;
    }

    /// <summary>Lo publicado que se ve: sin lo quitado ni lo borrado, de la clase y la búsqueda del widget, de lo último a lo primero.</summary>
    private List<AgentArtifact> Items()
    {
        var kind = Kind;
        return ArtifactMonitor.Instance.Items
            .Where(a => !a.Removed && !a.Deleted && (kind is null || a.Kind == kind))
            .Where(a => _query.Length == 0 || (a.Title ?? "").Contains(_query, StringComparison.CurrentCultureIgnoreCase)
                        || ArtifactTexts.Home(a).Contains(_query, StringComparison.CurrentCultureIgnoreCase))
            .OrderByDescending(Last)
            .ToList();
    }

    /// <summary>Lo que, si cambia en un artifact, cuenta como novedad: una versión nueva.</summary>
    private static string Signature(AgentArtifact item) => Last(item).Ticks.ToString(CultureInfo.InvariantCulture);

    /// <summary>Cuándo se publicó o se actualizó por última vez.</summary>
    private static DateTime Last(AgentArtifact item) => item.UpdatedUtc > item.CreatedUtc ? item.UpdatedUtc : item.CreatedUtc;

    /// <summary>El punto verde de lo nuevo, con su ficha.</summary>
    private static Avalonia.Controls.Shapes.Ellipse NewDot()
    {
        var dot = Ui.Dot(ArtifactTexts.Green(), 6);
        DiaryInk.Tip(dot, DiaryInk.Card("Nuevo", "Publicado o actualizado desde la última vez que miraste este widget, y sin abrir."));
        return dot;
    }

    /// <summary>El grupo de días de un artifact: hoy, ayer, esta semana, este mes o antes.</summary>
    private static string DayOf(AgentArtifact item)
    {
        var day = Last(item).ToLocalTime().Date;
        var ago = (DateTime.Today - day).Days;
        return ago switch
        {
            0 => "Hoy",
            1 => "Ayer",
            < 7 => "Esta semana",
            < 31 => "Este mes",
            _ => "Antes",
        };
    }

    /// <summary>La hora de un artifact en su grupo: la hora hoy y ayer, el día de la semana esta semana y la fecha antes.</summary>
    private static string WhenOf(AgentArtifact item)
    {
        var local = Last(item).ToLocalTime();
        var ago = (DateTime.Today - local.Date).Days;
        return ago switch
        {
            <= 1 => local.ToString("HH:mm", Es),
            < 7 => local.ToString("ddd", Es).Replace(".", ""),
            _ => local.ToString("d MMM", Es).Replace(".", ""),
        };
    }

    protected override Control Build()
    {
        var items = Items();
        if (_open is not null && Size != WidgetSize.L) return Over(Browser(_open), () => Close(), OpenOutside(_open));

        var dock = new DockPanel();
        var bar = Bar(items.Count);
        bar.Margin = new Thickness(0, 0, 0, 8);
        DockPanel.SetDock(bar, Dock.Top);
        dock.Children.Add(bar);
        if (_searching)
        {
            var box = SearchBox();
            box.Margin = new Thickness(0, 0, 0, 8);
            DockPanel.SetDock(box, Dock.Top);
            dock.Children.Add(box);
        }
        if (!ArtifactMonitor.Instance.Loaded)
        {
            dock.Children.Add(Ui.Empty("Buscando lo publicado…"));
            return dock;
        }
        if (items.Count == 0)
        {
            dock.Children.Add(Ui.Empty(_query.Length > 0 || Kind is not null ? "Nada con este filtro." : "Aún no hay nada publicado."));
            return dock;
        }
        if (Size == WidgetSize.L && _open is null)
        {
            var groups = items.GroupBy(GroupOf).Take(4).Select(g =>
            {
                var cards = new StackPanel { Spacing = 6 };
                foreach (var item in g.Take(30)) cards.Children.Add(Watch(Card(item), "artifacts", item.Url, Signature(item), ArtifactTexts.Green()));
                var head = GroupHead(g.Key, g.Count(), label: false);
                head.Margin = new Thickness(2, 0, 0, 6);
                var column = new DockPanel();
                DockPanel.SetDock(head, Dock.Top);
                column.Children.Add(head);
                column.Children.Add(Scroll($"day:{g.Key}", cards));
                return (Control)new Border { Background = Ui.Section, CornerRadius = new CornerRadius(8), Padding = new Thickness(7), Child = column };
            }).ToList();
            dock.Children.Add(Ui.Columns(groups.Count, 8, groups, stretch: true));
            return dock;
        }
        var list = Feed(items, two: Size != WidgetSize.S && _open is null);
        if (_open is null)
        {
            dock.Children.Add(Scroll("feed", list));
            return dock;
        }
        var split = new Grid { ColumnDefinitions = new ColumnDefinitions($"{ListWidth},*") };
        split.Children.Add(Scroll("feed", list));
        var side = Over(Browser(_open), () => Close(), OpenOutside(_open));
        side.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(side, 1);
        split.Children.Add(side);
        dock.Children.Add(split);
        return dock;
    }

    /// <summary>Abre un artifact donde se ha elegido: dentro del widget, en el Navegador del corcho (dentro del widget si no va
    /// en un corcho) o en el navegador del sistema.</summary>
    private void Open(AgentArtifact item) => OpenIn(item, Target);

    /// <summary>Abre un artifact en un sitio: «widget», «cork» (el Navegador del corcho; dentro del widget si no va en un
    /// corcho) o «system».</summary>
    private void OpenIn(AgentArtifact item, string target)
    {
        MarkOpened(item);
        if (target == "system")
        {
            SessionsView.OpenLink(item.Url);
            return;
        }
        if (target == "cork" && BrowserWidget.OpenIn(this, item.Url)) return;
        _open = item;
        Render();
    }

    /// <summary>Con qué se recuerda plegado un grupo: el día y el repositorio se guardan aparte.</summary>
    private string FoldId(string group) => (ByRepo ? "repo:" : "day:") + group;

    /// <summary>Pone en un artifact el menú del clic derecho, para abrir ese en un sitio concreto sin cambiar el de siempre.</summary>
    private void OpenMenu(Control control, AgentArtifact item)
    {
        control.ContextRequested += (_, e) =>
        {
            e.Handled = true;
            var menu = new ContextMenu();
            menu.Items.Add(Ui.Caption("Abrir en"));
            foreach (var (id, name) in Targets)
                menu.Items.Add(Ui.Item(id == Target ? $"{name} (lo de siempre)" : name, () => OpenIn(item, id)));
            menu.Open(control);
        };
    }

    /// <summary>Cierra lo abierto y vuelve a la lista.</summary>
    private void Close()
    {
        _open = null;
        Render();
    }

    /// <summary>La barra: la clase en un desplegable, con cuántos hay; cuándo se publicó lo último; y buscar.</summary>
    private Control Bar(int count)
    {
        var bar = new DockPanel();
        var search = Ui.IconButton("", () =>
        {
            _searching = !_searching;
            if (!_searching)
            {
                _query = "";
                _search = null;
            }
            Render();
        }, DiaryInk.Card("Buscar", "Por título o repositorio, solo en este widget."));
        DockPanel.SetDock(search, Dock.Right);
        bar.Children.Add(search);
        if (Size != WidgetSize.S && ArtifactMonitor.Instance.Items.Where(a => !a.Removed && !a.Deleted).Select(Last).DefaultIfEmpty().Max() is var last && last != default)
        {
            var when = Ui.Text($"el último {TasksModel.Ago(last)}", 11, Ui.Faint);
            when.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(when, Dock.Right);
            bar.Children.Add(when);
        }
        var group = Ui.Row(4,
            Ui.Chip(Ui.Text("Días", 11.5), !ByRepo, () => Group(null)),
            Ui.Chip(Ui.Text("Repos", 11.5), ByRepo, () => Group("repos")));
        group.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(group, Dock.Left);
        bar.Children.Add(group);
        var label = Kind is { } kind ? $"{ArtifactTexts.Plural(kind)} · {count}" : $"Todo · {count}";
        var picker = new Button
        {
            Content = Ui.Row(6, Ui.Text(label, 12, bold: true), Ui.Icon("", 9)), Focusable = false, HorizontalAlignment = HorizontalAlignment.Left,
        };
        if (Application.Current?.FindResource("ChipButton") is Avalonia.Styling.ControlTheme theme) picker.Theme = theme;
        picker.Click += (_, _) => KindMenu().Open(picker);
        bar.Children.Add(picker);
        return bar;
    }

    /// <summary>El menú de la clase: todas, o una, con cuántos hay de cada una.</summary>
    private ContextMenu KindMenu()
    {
        var all = ArtifactMonitor.Instance.Items.Where(a => !a.Removed && !a.Deleted).ToList();
        var menu = new ContextMenu();
        menu.Items.Add(Ui.Check($"Todo · {all.Count}", Kind is null, () =>
        {
            Set("kind", null);
            Render();
        }));
        foreach (var kind in Kinds)
        {
            var n = all.Count(a => a.Kind == kind);
            if (n == 0) continue;
            menu.Items.Add(Ui.Check($"{ArtifactTexts.Plural(kind)} · {n}", Kind == kind, () =>
            {
                Set("kind", kind.ToString());
                Render();
            }));
        }
        return menu;
    }

    /// <summary>La caja de búsqueda, siempre la misma, sacada del dibujo anterior para no perder el foco.</summary>
    private Control SearchBox()
    {
        if (_search is null)
        {
            _search = new TextBox { PlaceholderText = "Buscar por título o repositorio", FontSize = 12 };
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

    /// <summary>Agrupa la lista por repositorio con «repos», o por días con null.</summary>
    private void Group(string? by)
    {
        Set("group", by);
        Render();
    }

    /// <summary>La lista por grupos (días o repositorios): el nombre de cada grupo y una fila por artifact, con su hora a la izquierda.</summary>
    private Control Feed(List<AgentArtifact> items, bool two)
    {
        var list = new StackPanel { Spacing = 4 };
        foreach (var group in items.GroupBy(GroupOf))
        {
            var id = FoldId(group.Key);
            var head = FoldHead(id, GroupHead(group.Key, group.Count(), label: true));
            head.Margin = new Thickness(0, list.Children.Count == 0 ? 0 : 4, 0, 0);
            list.Children.Add(head);
            if (Folded(id))
            {
                foreach (var item in group) Note("artifacts", item.Url, Signature(item), id);
                continue;
            }
            foreach (var item in group.Take(60)) list.Children.Add(Watch(Row(item, two), "artifacts", item.Url, Signature(item), ArtifactTexts.Green(), id));
        }
        return list;
    }

    /// <summary>Una fila: la hora, la raya del color de su clase, el título y, si se pide, su clase y su repositorio debajo.</summary>
    private Control Row(AgentArtifact item, bool two)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("44,*,Auto") };
        var when = Ui.Text(WhenIn(item), 10.5, Ui.Faint, mono: true);
        when.VerticalAlignment = two ? VerticalAlignment.Top : VerticalAlignment.Center;
        when.Margin = new Thickness(0, two ? 2 : 0, 0, 0);
        grid.Children.Add(when);
        var opened = WasOpened(item);
        var texts = Ui.Column(1, Ui.Text(item.Title ?? "Sin título", 12, opened ? Ui.Muted : null, bold: !opened));
        if (two)
            texts.Children.Add(Ui.Row(6, Ui.Text(ArtifactTexts.Kind(item.Kind), 10.5, ArtifactTexts.Color(item.Kind), bold: true),
                Ui.Text(ArtifactTexts.Home(item), 10.5, Ui.Muted)));
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        if (IsNew(item))
        {
            var dot = NewDot();
            dot.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(dot, 2);
            grid.Children.Add(dot);
        }
        var row = Ui.Card(grid, () => Open(item), stripe: ArtifactTexts.Color(item.Kind), on: _open?.Url == item.Url);
        row.Padding = new Thickness(8, 5);
        DiaryInk.Tip(row, ArtifactTexts.Card(item));
        OpenMenu(row, item);
        return row;
    }

    /// <summary>La ficha de un artifact en L: una baldosa del color de su clase con su icono, su título, su clase, su repositorio y cuándo.</summary>
    private Control Card(AgentArtifact item)
    {
        var tile = new Border
        {
            Height = 46, CornerRadius = new CornerRadius(6), Background = ArtifactTexts.Color(item.Kind, 0.22),
            Child = new TextBlock
            {
                Text = ArtifactTexts.Icon(item.Kind), FontSize = 18, Foreground = ArtifactTexts.Color(item.Kind), HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, Classes = { "icon", "ownColor" },
            },
        };
        var meta = new DockPanel();
        var ago = Ui.Text(WhenIn(item), 10.5, Ui.Faint);
        DockPanel.SetDock(ago, Dock.Right);
        meta.Children.Add(ago);
        if (IsNew(item))
        {
            var dot = NewDot();
            dot.Margin = new Thickness(0, 0, 6, 0);
            DockPanel.SetDock(dot, Dock.Right);
            meta.Children.Add(dot);
        }
        meta.Children.Add(Ui.Row(6, Ui.Text(ArtifactTexts.Kind(item.Kind), 10.5, ArtifactTexts.Color(item.Kind), bold: true), Ui.Text(ArtifactTexts.Home(item), 10.5, Ui.Muted)));
        var opened = WasOpened(item);
        var card = Ui.Card(Ui.Column(5, tile, Ui.Text(item.Title ?? "Sin título", 12, opened ? Ui.Muted : null, bold: !opened), meta), () => Open(item));
        DiaryInk.Tip(card, ArtifactTexts.Card(item));
        OpenMenu(card, item);
        return card;
    }

    /// <summary>El navegador integrado con un artifact abierto; siempre el mismo, sacado del dibujo anterior.</summary>
    private Control Browser(AgentArtifact item)
    {
        _browser ??= new BrowserPane("Abriendo…");
        if (_browser.Parent is Decorator frame) frame.Child = null;
        if (_browser.Current?.ToString() != item.Url) _browser.Navigate(item.Url);
        var head = Ui.Column(2, Ui.Text(item.Title ?? "Sin título", 13, bold: true),
            Ui.Row(6, Ui.Text(ArtifactTexts.Kind(item.Kind), 10.5, ArtifactTexts.Color(item.Kind), bold: true), Ui.Text(ArtifactTexts.Home(item), 10.5, Ui.Muted)));
        head.Margin = new Thickness(0, 0, 0, 8);
        var dock = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        dock.Children.Add(head);
        dock.Children.Add(new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = _browser });
        return dock;
    }

    /// <summary>El enlace que abre un artifact en el navegador del sistema.</summary>
    private static Control OpenOutside(AgentArtifact item) => Ui.Link("Abrir fuera ↗", () => SessionsView.OpenLink(item.Url));
}
