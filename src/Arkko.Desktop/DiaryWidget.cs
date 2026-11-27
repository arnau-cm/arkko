using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>El widget del Diario: en qué se ha ido un día, el de hoy al abrirse, de dos formas. «Franja y tareas»: la franja
/// del día con un bloque por sesión y una línea por tarea con su tiempo y sus commits. «Barras por tarea»: el tiempo de
/// agente en grande y una barra por tarea a su escala. De serie, las barras en S y la franja desde M.</summary>
/// <remarks>
/// Lee los días como la sección (<see cref="DiaryStore"/>), sin tocar su vista: lo elegido aquí (la forma y el horario) es
/// del widget. El día no se guarda: al volver a abrir la app, vuelve a hoy. Pulsar una tarea o un bloque abre su ficha
/// dentro del widget (encima en S y M, al lado en L). Hoy, el bloque de una sesión que sigue trabajando va rayado y crece con
/// la hora, y la tarea que suma un commit destella.
/// </remarks>
internal sealed class DiaryWidget : CorkWidget
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    // Alto de la franja del día, ancho de la ficha en L y lo que dura una tarea corta, que va recogida al final.
    private const double StripHeight = 22;
    private const double CardWidth = 320;
    private static readonly TimeSpan Short = TimeSpan.FromMinutes(15);

    private readonly Dictionary<DateTime, DiaryDay> _days = new();
    private readonly DispatcherTimer _today = new() { Interval = TimeSpan.FromMinutes(2) };

    // Mientras se ve hoy, la franja se repinta cada poco para que el bloque en marcha y la hora de ahora avancen.
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(30) };
    private DateTime _day = DateTime.Today;
    private DiaryBlock? _selected;
    private string? _task;
    private bool _all;
    private bool _loading;
    private int _generation;

    /// <summary>Crea el widget; lee su día al verse.</summary>
    public DiaryWidget()
    {
        _today.Tick += (_, _) => _ = LoadAsync();
        _tick.Tick += (_, _) =>
        {
            if (_day == DateTime.Today) Render();
        };
    }

    public override PanelSection Section => PanelSection.Diary;

    /// <summary>Azul si hoy hay una sesión trabajando; nada si no.</summary>
    public override IBrush? Mood =>
        _days.TryGetValue(DateTime.Today, out var day) && day.Blocks.Any(b => Live(day, b)) ? Brushes_.Working : null;

    /// <summary>El tiempo de agente de hoy, si ya se ha leído.</summary>
    public override string? Headline =>
        _days.TryGetValue(DateTime.Today, out var day) && !day.Empty ? $"hoy {Diary.Hours(day.WorkingMs)}" : null;

    /// <summary>Si solo se ve el horario laboral: lo elegido en el widget o, si no, como el Diario de la app.</summary>
    private bool WorkHours => Get("work") is { } work ? work == "1" : DiaryModel.WorkHoursOnlyDefault;

    protected override void Attached() => RepoColors.Changed += Render;

    protected override void Detached()
    {
        RepoColors.Changed -= Render;
        _today.Stop();
        _tick.Stop();
    }

    protected override void Started()
    {
        _today.Start();
        _tick.Start();
        _ = LoadAsync();
    }

    protected override void Stopped()
    {
        _today.Stop();
        _tick.Stop();
    }

    /// <summary>La forma elegida en el menú: «auto» (según el espacio), «strip» o «bars».</summary>
    private string Look => Get("look") ?? "auto";

    /// <summary>Si toca la forma de barras: la elegida o, según el espacio, en S.</summary>
    private bool Bars => Look == "bars" || (Look == "auto" && Size == WidgetSize.S);

    public override IEnumerable<Control> MenuItems()
    {
        yield return Ui.Caption("Vista");
        foreach (var (id, name) in new[] { ("auto", "Según el espacio"), ("strip", "Franja y tareas"), ("bars", "Barras por tarea") })
            yield return Ui.Check(name, Look == id, () =>
            {
                Set("look", id == "auto" ? null : id);
                Render();
            });
        yield return new Separator();
        yield return Ui.Check("Solo el horario laboral", WorkHours, () =>
        {
            Set("work", WorkHours ? "0" : "1");
            Render();
        });
        yield return Ui.Item("Copiar parte", Copy, _days.TryGetValue(_day, out var day) && !day.Empty);
    }

    /// <summary>Lee el día que se ve si falta o aún puede cambiar (hoy y ayer), y repinta.</summary>
    private async Task LoadAsync()
    {
        var day = _day;
        if (day > DateTime.Today) return;
        if (_days.TryGetValue(day, out var known) && (day < DateTime.Today.AddDays(-1) || DiaryStore.Final(known))) return;
        var generation = ++_generation;
        _loading = !_days.ContainsKey(day);
        Render();
        try
        {
            var (settings, links, title) = DiaryKeeper.Snapshot();
            var read = await Task.Run(() => DiaryStore.Get(day, settings, links, title, pullRequests: true, DateTime.UtcNow));
            if (generation != _generation) return;
            _days[day] = read;
            Refreshed();
        }
        catch (Exception ex)
        {
            JsonStore.LogError("diary-widget", ex);
        }
        finally
        {
            if (generation == _generation) _loading = false;
            Render();
        }
    }

    /// <summary>Va al día de antes o al de después; con 0, a hoy.</summary>
    private void Move(int days)
    {
        _day = days == 0 ? DateTime.Today : _day.AddDays(days);
        if (_day > DateTime.Today) _day = DateTime.Today;
        _selected = null;
        _task = null;
        _all = false;
        Render();
        _ = LoadAsync();
    }

    /// <summary>Copia el parte del día que se ve, para pegarlo en la daily o al imputar horas.</summary>
    private async void Copy()
    {
        try
        {
            if (!_days.TryGetValue(_day, out var day) || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
            await clipboard.SetTextAsync(Diary.Report([day]));
        }
        catch (Exception ex)
        {
            JsonStore.LogError("diary-widget", ex);
        }
    }

    protected override Control Build()
    {
        _days.TryGetValue(_day, out var day);
        var ficha = day is null ? null : Ficha(day);
        if (ficha is not null && Size != WidgetSize.L) return Over(ficha, Back);

        var dock = new DockPanel();
        void Top(Control control, double gap = 8)
        {
            control.Margin = new Thickness(0, 0, 0, gap);
            DockPanel.SetDock(control, Dock.Top);
            dock.Children.Add(control);
        }
        Top(Bar());
        if (day is null)
        {
            dock.Children.Add(Ui.Empty(_loading ? "Leyendo el día…" : "Sin leer todavía."));
            return dock;
        }
        if (day.Empty)
        {
            dock.Children.Add(Ui.Empty("Nada este día."));
            return dock;
        }
        Control body;
        if (Bars)
        {
            Top(Total(day), 10);
            body = Scroll("tasks", TaskBars(day));
        }
        else
        {
            Top(Summary(day));
            var (from, to) = Span(day);
            Top(Strip(day, from, to), 10);
            body = Scroll("tasks", TaskLines(day));
        }
        if (ficha is null)
        {
            dock.Children.Add(body);
            return dock;
        }
        var split = new Grid { ColumnDefinitions = new ColumnDefinitions($"*,{CardWidth}") };
        split.Children.Add(body);
        var side = new Border
        {
            Background = Ui.Section, CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8), Margin = new Thickness(8, 0, 0, 0),
            Child = Over(ficha, Back),
        };
        Grid.SetColumn(side, 1);
        split.Children.Add(side);
        dock.Children.Add(split);
        return dock;
    }

    /// <summary>Abre la ficha de un bloque.</summary>
    private void Pick(DiaryBlock block)
    {
        _selected = block;
        Render();
    }

    /// <summary>Abre la ficha de una tarea.</summary>
    private void PickTask(DiaryTask task)
    {
        _task = IdOf(task);
        _selected = null;
        Render();
    }

    /// <summary>Cierra la ficha abierta: la de un bloque vuelve a la de su tarea, si se llegó desde ella.</summary>
    private void Back()
    {
        if (_selected is not null) _selected = null;
        else _task = null;
        Render();
    }

    /// <summary>Con qué se reconoce una tarea del parte entre una lectura del día y la siguiente.</summary>
    private static string IdOf(DiaryTask task) => task.Key ?? $"{task.Project}|{task.Folder}";

    /// <summary>La ficha abierta: la del bloque elegido o la de la tarea elegida; null si no hay ninguna.</summary>
    private Control? Ficha(DiaryDay day)
    {
        if (_selected is not null) return Ui.Column(0, DiaryView.BlockCard(day, _selected), SessionLink(_selected));
        return day.Tasks.FirstOrDefault(t => IdOf(t) == _task) is { } task ? TaskCard(day, task) : null;
    }

    /// <summary>El enlace a la sesión de un bloque, debajo de su ficha.</summary>
    private static Control SessionLink(DiaryBlock block)
    {
        var link = Ui.Link("Ver sesión ↗", () => SessionActions.RequestSession(block.SessionId));
        link.Margin = new Thickness(0, 10, 0, 0);
        return link;
    }

    /// <summary>La barra: «‹ Hoy ›» o el día que se ve, para moverse de uno en uno.</summary>
    private Control Bar()
    {
        var today = _day == DateTime.Today;
        var name = Size == WidgetSize.S ? _day.ToString("ddd d MMM", Es).Replace(".", "") : _day.ToString("dddd d 'de' MMMM", Es);
        var title = Ui.Text(today ? $"Hoy · {name}" : DiaryModel.Capital(name), 13, bold: true);
        var row = Ui.Row(6, Ui.IconButton("", () => Move(-1), DiaryInk.Card("El día de antes")), title);
        var next = Ui.IconButton("", () => Move(1), DiaryInk.Card("El día de después"));
        next.IsEnabled = !today;
        row.Children.Add(next);
        if (!today) row.Children.Add(Ui.Link("Hoy", () => Move(0)));
        if (_loading) row.Children.Add(Ui.Text("Leyendo…", 11, Ui.Faint));
        return row;
    }

    /// <summary>El resumen del día en chips: horas de agente, esperándote, commits y PR.</summary>
    private static Control Summary(DiaryDay day)
    {
        var chips = new WrapPanel();
        void Chip(string text, IBrush? color = null)
        {
            var pill = Ui.Pill(text, color ?? Ui.Muted);
            pill.Margin = new Thickness(0, 0, 6, 4);
            chips.Children.Add(pill);
        }
        Chip($"{Diary.Hours(day.WorkingMs)} de agente", Ui.Accent);
        if (day.WaitMs >= 60_000) Chip($"{Diary.Hours(day.WaitMs)} esperándote", Ui.Wait);
        if (day.Commits > 0) Chip(day.Commits == 1 ? "1 commit" : $"{day.Commits} commits");
        if (PullRequests(day) is > 0 and var prs) Chip(prs == 1 ? "1 PR" : $"{prs} PR");
        return chips;
    }

    /// <summary>Cuántas PR tocó el día.</summary>
    private static int PullRequests(DiaryDay day) => day.Tasks.Sum(t => t.PullRequests.Count);

    /// <summary>El tiempo de agente del día en grande, con lo que se esperó, y los commits y las PR a un lado.</summary>
    private Control Total(DiaryDay day)
    {
        var dock = new DockPanel();
        var counts = new List<string>();
        if (day.Commits > 0) counts.Add(day.Commits == 1 ? "1 commit" : $"{day.Commits} commits");
        if (PullRequests(day) is > 0 and var prs) counts.Add(prs == 1 ? "1 PR" : $"{prs} PR");
        if (counts.Count > 0 && Size != WidgetSize.S)
        {
            var right = Ui.Text(string.Join(" · ", counts), 11, Ui.Faint);
            right.VerticalAlignment = VerticalAlignment.Bottom;
            DockPanel.SetDock(right, Dock.Right);
            dock.Children.Add(right);
        }
        var big = Ui.Text(Diary.Hours(day.WorkingMs), 22, bold: true);
        var note = Ui.Text(day.WaitMs >= 60_000 ? $"de agente, {Diary.Hours(day.WaitMs)} esperándote" : "de agente", 11.5, Ui.Muted);
        note.VerticalAlignment = VerticalAlignment.Bottom;
        note.Margin = new Thickness(0, 0, 0, 3);
        var line = Ui.Row(7, big, note);
        dock.Children.Add(Size == WidgetSize.S && counts.Count > 0 ? Ui.Column(1, line, Ui.Text(string.Join(" · ", counts), 11, Ui.Faint)) : line);
        return dock;
    }

    /// <summary>Las horas que se ven del día: las del horario laboral si se pide, ampliadas a lo trabajado fuera; si no, de la
    /// primera sesión a la última. Hoy llega al menos hasta ahora.</summary>
    private (double From, double To) Span(DiaryDay day)
    {
        double Hour(DateTime utc) => (utc.ToLocalTime() - day.Day.Date).TotalHours;
        var first = day.Blocks.Count > 0 ? day.Blocks.Min(b => Hour(b.StartUtc)) : 9;
        var last = day.Blocks.Count > 0 ? day.Blocks.Max(b => Hour(b.EndUtc)) : 17;
        if (day.Day.Date == DateTime.Today) last = Math.Max(last, (DateTime.Now - DateTime.Today).TotalHours);
        if (WorkHours)
        {
            var (open, close) = DiaryModel.Schedule.Hours;
            first = Math.Min(first, open);
            last = Math.Max(last, close + 1);
        }
        return (Math.Max(0, Math.Floor(first)), Math.Min(24, Math.Ceiling(last)));
    }

    /// <summary>La franja del día: un bloque por sesión en el color de su repositorio, a su hora, con las horas debajo y la de
    /// ahora en rojo.</summary>
    private Control Strip(DiaryDay day, double from, double to)
    {
        var lane = new Canvas { Height = StripHeight, Background = Ui.Section };
        lane.SizeChanged += (_, _) => PlaceStrip(lane, day, from, to);
        var axis = new Grid { Margin = new Thickness(0, 3, 0, 0) };
        var hours = (int)(to - from);
        var step = Size == WidgetSize.S ? Math.Max(2, hours / 4) : Math.Max(1, hours / 10);
        for (var h = 0; h < hours; h++) axis.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        for (var h = 0; h < hours; h += step)
        {
            var label = Ui.Text($"{(int)from + h}h", 10, Ui.Faint, mono: true);
            Grid.SetColumn(label, h);
            Grid.SetColumnSpan(label, Math.Min(step, hours - h));
            axis.Children.Add(label);
        }
        return Ui.Column(0, new Border { CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = lane }, axis);
    }

    /// <summary>Si un bloque de hoy sigue en marcha: es el último de su sesión y la sesión trabaja ahora, o espera a sus agentes
    /// o a un comando.</summary>
    private static bool Live(DiaryDay day, DiaryBlock block)
    {
        if (day.Day.Date != DateTime.Today) return false;
        if (Pieces.Sessions().FirstOrDefault(s => s.SessionId == block.SessionId) is not { Status: SessionStatus.Working or SessionStatus.Agents or SessionStatus.Command }) return false;
        return day.Blocks.Where(b => b.SessionId == block.SessionId).MaxBy(b => b.StartUtc) == block;
    }

    /// <summary>Coloca los bloques de la franja con el ancho que ya tiene, y la hora de ahora si es hoy; el que sigue en marcha
    /// llega hasta ahora, rayado y con el borde de su color.</summary>
    private void PlaceStrip(Canvas lane, DiaryDay day, double from, double to)
    {
        lane.Children.Clear();
        var width = lane.Bounds.Width;
        if (width <= 0) return;
        var perHour = width / Math.Max(1, to - from);
        foreach (var block in day.Blocks.OrderBy(b => b.StartUtc))
        {
            var on = _selected is not null && _selected.SessionId == block.SessionId && _selected.StartUtc == block.StartUtc;
            var live = Live(day, block);
            var end = live && DateTime.UtcNow > block.EndUtc ? DateTime.UtcNow : block.EndUtc;
            var box = new Border
            {
                Width = Math.Max(3, (end - block.StartUtc).TotalHours * perHour - 1), Height = StripHeight - 8,
                Background = live ? Ui.Hatch(DiaryInk.Repo(block.Project)) : DiaryInk.Repo(block.Project, 0.8), CornerRadius = new CornerRadius(3),
                BorderBrush = on ? Ui.Fg : live ? DiaryInk.Repo(block.Project) : null, BorderThickness = new Thickness(on ? 1.5 : live ? 1 : 0),
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            DiaryInk.Tip(box, DiaryView.BlockCard(day, block));
            box.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton == MouseButton.Left) Pick(block);
            };
            Canvas.SetLeft(box, ((block.StartUtc.ToLocalTime() - day.Day.Date).TotalHours - from) * perHour);
            Canvas.SetTop(box, 4);
            lane.Children.Add(box);
        }
        if (day.Day.Date != DateTime.Today) return;
        var now = new Rectangle { Width = 2, Height = StripHeight, Fill = Ui.Danger, IsHitTestVisible = false };
        Canvas.SetLeft(now, ((DateTime.Now - DateTime.Today).TotalHours - from) * perHour);
        lane.Children.Add(now);
    }

    /// <summary>Las tareas del día, de la que más tiempo llevó a la que menos; las cortas, recogidas al final hasta que se piden.</summary>
    private (List<DiaryTask> Shown, int Hidden) Tasks(DiaryDay day)
    {
        var ordered = day.Tasks.OrderByDescending(t => t.WorkingMs).ToList();
        if (_all) return (ordered, 0);
        var shown = ordered.Where((t, i) => i < 3 || TimeSpan.FromMilliseconds(t.WorkingMs) >= Short).ToList();
        return (shown, ordered.Count - shown.Count);
    }

    /// <summary>El enlace que despliega las tareas cortas recogidas.</summary>
    private Control MoreLink(int hidden)
    {
        var link = Ui.Link(hidden == 1 ? "+ 1 de menos de 15 min" : $"+ {hidden} de menos de 15 min", () =>
        {
            _all = true;
            Render();
        });
        link.Margin = new Thickness(2, 6, 0, 0);
        return link;
    }

    /// <summary>Cómo se llama una tarea en una línea: su clave, o su repositorio si no tiene; y su título o su carpeta.</summary>
    private static (string Key, string Title) Names(DiaryTask task) =>
        task.Key is { } key ? (key, task.Title ?? "") : (task.Project, task.Folder ?? "");

    /// <summary>Lo que, si cambia en una tarea del día, cuenta como novedad: un commit o una PR más.</summary>
    private static string Signature(DiaryTask task) => $"{task.Commits.Count}:{task.PullRequests.Count}";

    /// <summary>Lo que se cuenta de una tarea a la derecha de su línea: su tiempo, sus commits y sus PR.</summary>
    private static string Facts(DiaryTask task)
    {
        var facts = Diary.Hours(task.WorkingMs);
        if (task.Commits.Count > 0) facts += task.Commits.Count == 1 ? " · 1 commit" : $" · {task.Commits.Count} commits";
        if (task.PullRequests.Count > 0) facts += $" · PR {string.Join(", ", task.PullRequests.Select(p => $"#{p}"))}";
        return facts;
    }

    /// <summary>Una línea por tarea: su punto de color, su clave y su título, y su tiempo, sus commits y sus PR a la derecha.</summary>
    private Control TaskLines(DiaryDay day)
    {
        var (tasks, hidden) = Tasks(day);
        var list = new StackPanel();
        foreach (var task in tasks)
        {
            var (key, title) = Names(task);
            var row = new DockPanel();
            var dot = Ui.Dot(DiaryInk.Repo(task.Project));
            dot.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(dot, Dock.Left);
            row.Children.Add(dot);
            var facts = Ui.Text(Size == WidgetSize.S ? Diary.Hours(task.WorkingMs) : Facts(task), 11, Ui.Faint, mono: true);
            facts.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(facts, Dock.Right);
            row.Children.Add(facts);
            var name = Ui.Text(key, 11, Ui.Muted, bold: true, mono: true);
            name.Margin = new Thickness(0, 0, 6, 0);
            DockPanel.SetDock(name, Dock.Left);
            row.Children.Add(name);
            var text = Ui.Text(title, 12);
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            row.Children.Add(text);
            list.Children.Add(Watch(Line(row, () => PickTask(task), IdOf(task) == _task), $"tasks:{_day:yyyyMMdd}", IdOf(task), Signature(task), DiaryInk.Repo(task.Project)));
        }
        if (hidden > 0) list.Children.Add(MoreLink(hidden));
        return list;
    }

    /// <summary>Una barra por tarea, a la escala de la que más tiempo llevó, con su clave y su título dentro y su tiempo al lado.</summary>
    private Control TaskBars(DiaryDay day)
    {
        var (tasks, hidden) = Tasks(day);
        var top = Math.Max(1, day.Tasks.Count > 0 ? day.Tasks.Max(t => t.WorkingMs) : 1);
        var list = new StackPanel { Spacing = 5 };
        foreach (var task in tasks)
        {
            var (key, title) = Names(task);
            var bar = new Grid { Height = 24 };
            bar.Children.Add(new Border { Background = Ui.Section, CornerRadius = new CornerRadius(5) });
            bar.Children.Add(new FractionFill { Fraction = (double)task.WorkingMs / top, Fill = DiaryInk.Repo(task.Project, 0.45), Radius = 5 });
            var inside = new DockPanel { Margin = new Thickness(8, 0), VerticalAlignment = VerticalAlignment.Center };
            var name = Ui.Text(key, 11, Ui.Muted, bold: true, mono: true);
            name.Margin = new Thickness(0, 0, 6, 0);
            DockPanel.SetDock(name, Dock.Left);
            inside.Children.Add(name);
            var text = Ui.Text(title, 11.5);
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            inside.Children.Add(text);
            bar.Children.Add(inside);
            var row = new DockPanel();
            var time = Ui.Text(Diary.Hours(task.WorkingMs), 11, Ui.Faint, mono: true);
            time.Width = 58;
            time.TextAlignment = TextAlignment.Right;
            time.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(time, Dock.Right);
            row.Children.Add(time);
            row.Children.Add(bar);
            var hit = new Border { Background = Brushes.Transparent, Child = row, Cursor = new Cursor(StandardCursorType.Hand) };
            DiaryInk.Tip(hit, DiaryInk.Card(Diary.NameOf(task), Facts(task)));
            hit.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton == MouseButton.Left) PickTask(task);
            };
            list.Children.Add(Watch(hit, $"tasks:{_day:yyyyMMdd}", IdOf(task), Signature(task), DiaryInk.Repo(task.Project)));
        }
        if (hidden > 0) list.Children.Add(MoreLink(hidden));
        return list;
    }

    /// <summary>Una línea de lista con su raya de abajo, que se ilumina al pasar y hace lo dado al pulsarla.</summary>
    private static Border Line(Control child, Action click, bool on)
    {
        var line = new Border
        {
            Child = child, Padding = new Thickness(4, 6), BorderBrush = Ui.TrackEdge, BorderThickness = new Thickness(0, 0, 0, 1),
            Background = on ? ThemeColors.Of("Accent", "#61AFEF", 0.14) : Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand),
        };
        var rest = line.Background;
        line.PointerEntered += (_, _) => line.Background = Ui.Track;
        line.PointerExited += (_, _) => line.Background = rest;
        line.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left) click();
        };
        return line;
    }

    /// <summary>La ficha de una tarea: su nombre, lo que llevó, lo hecho (sus commits o, sin ellos, lo pedido) y sus sesiones,
    /// cada una con su ficha al pulsarla.</summary>
    private Control TaskCard(DiaryDay day, DiaryTask task)
    {
        var list = new StackPanel { Spacing = 8 };
        list.Children.Add(Ui.Row(6, Ui.Dot(DiaryInk.Repo(task.Project)), Ui.Text(task.Project, 11, Ui.Muted)));
        list.Children.Add(Ui.Text(Diary.NameOf(task), 14, bold: true, wrap: true));
        var meta = $"{task.Sessions} {(task.Sessions == 1 ? "sesión" : "sesiones")} · {Facts(task)}";
        if (task.WaitMs >= 60_000) meta += $" · {Diary.Hours(task.WaitMs)} esperándote";
        list.Children.Add(Ui.Text(meta, 11.5, Ui.Muted, wrap: true));
        // Lo pedido por un agente llega como un mensaje entre etiquetas: no es lo que pidió la persona.
        var done = task.Commits.Count > 0 ? task.Commits.OrderBy(c => c.WhenUtc).Select(c => c.Subject).Take(8).ToList()
            : Diary.Asked(day, task, 10).Where(a => !a.TrimStart().StartsWith('<')).Take(5).ToList();
        if (done.Count > 0)
        {
            var lines = new List<Control> { Ui.Label(task.Commits.Count > 0 ? "Commits" : "Lo pedido") };
            foreach (var line in done)
            {
                var text = Ui.Text(line, 11.5, wrap: true);
                text.MaxLines = 3;
                text.TextTrimming = TextTrimming.CharacterEllipsis;
                lines.Add(text);
            }
            list.Children.Add(Ui.Block(lines.ToArray()));
        }
        var sessions = new List<Control> { Ui.Label("Sesiones") };
        foreach (var block in Diary.BlocksOf(day, task).OrderBy(b => b.StartUtc))
        {
            var when = $"{block.StartUtc.ToLocalTime():HH:mm}–{block.EndUtc.ToLocalTime():HH:mm}";
            var row = new DockPanel();
            var time = Ui.Text(when, 11, Ui.Faint, mono: true);
            time.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(time, Dock.Left);
            row.Children.Add(time);
            var name = Ui.Text(block.SessionName ?? block.Task ?? block.Project, 11.5);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            row.Children.Add(name);
            sessions.Add(Line(row, () => Pick(block), false));
        }
        list.Children.Add(Ui.Block(sessions.ToArray()));
        if (task.Key is { } key) list.Children.Add(Ui.Link("Ver tarea ↗", () => SessionActions.RequestTask(key)));
        return Scroll($"task:{IdOf(task)}", list);
    }
}
