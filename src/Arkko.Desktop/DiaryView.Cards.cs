using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Las vistas de resumen del Diario (Mes y Agenda), el parte de un día y la ficha de un bloque.</summary>
public partial class DiaryView
{
    // Tareas que enseña un día del mes antes de recoger el resto en «+N más».
    private const int MonthTasks = 3;

    private FontFamily Mono => this.FindResource("MonoFont") as FontFamily ?? FontFamily.Default;

    // MES

    /// <summary>Pinta el mes de lunes a domingo: cada día con su fondo según lo trabajado, sus tareas y sus commits.</summary>
    /// <remarks>El fondo va a escala del día que más se trabajó del mes, para que se vean los días fuertes y los flojos.</remarks>
    private void BuildMonth()
    {
        var range = _model.Range;
        var start = DiaryModel.Monday(range[0]);
        var weeks = ((range[^1] - start).Days + 7) / 7;
        var worked = range.Select(d => (Day: d, Ms: _model.Days.GetValueOrDefault(d)?.WorkingMs ?? 0)).Where(x => x.Ms > 0)
            .OrderByDescending(x => x.Ms).ToList();
        var most = Math.Max(1, worked.FirstOrDefault().Ms);
        // Hasta tener todos los días, el mes entero en esqueleto: la intensidad de cada uno depende del más trabajado.
        var complete = range.Where(d => d <= DateTime.Today).All(_model.Days.ContainsKey);

        MonthGrid.Children.Clear();
        MonthGrid.RowDefinitions = new RowDefinitions("Auto," + string.Join(",", Enumerable.Repeat("*", weeks)));
        MonthGrid.ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat("*", 7)));
        for (var c = 0; c < 7; c++)
        {
            var name = DiaryInk.Text(start.AddDays(c).ToString("dddd", Es), 11, DiaryInk.Faint);
            name.Margin = new Thickness(4, 8, 0, 6);
            Grid.SetColumn(name, c);
            MonthGrid.Children.Add(name);
        }
        for (var i = 0; i < weeks * 7; i++)
        {
            var date = start.AddDays(i);
            var inMonth = date.Month == range[0].Month;
            var rank = worked.FindIndex(x => x.Day == date) + 1;
            var cell = complete
                ? MonthCell(date, inMonth ? _model.Days.GetValueOrDefault(date) : null, inMonth, most, rank, worked.Count)
                : SkeletonCell(date, inMonth);
            Grid.SetRow(cell, 1 + i / 7);
            Grid.SetColumn(cell, i % 7);
            MonthGrid.Children.Add(cell);
        }
    }

    /// <summary>Un día del mes: su número y sus horas, hasta tres tareas con el color de su repositorio y lo commiteado; un clic lo abre.</summary>
    private Control MonthCell(DateTime date, DiaryDay? day, bool inMonth, long most, int rank, int ranked)
    {
        var today = date == DateTime.Today;
        var worked = day?.WorkingMs ?? 0;
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        if (worked > 0)
        {
            var hours = DiaryInk.Text(Diary.Hours(worked), 11, DiaryInk.Muted);
            DockPanel.SetDock(hours, Dock.Right);
            top.Children.Add(hours);
        }
        var number = DiaryInk.Text(date.Day.ToString(Es), 13, today ? DiaryInk.Of("Accent", "#61AFEF") : inMonth ? DiaryInk.Fg : DiaryInk.Of("FgFaint", "#8A8D9C", 0.5), bold: true);
        top.Children.Add(number);

        var content = new StackPanel();
        content.Children.Add(top);
        if (day is not null)
        {
            foreach (var task in day.Tasks.Take(MonthTasks)) content.Children.Add(DiaryInk.Dotted(RepoOf(task), task.Key ?? task.Folder ?? task.Project));
            if (day.Tasks.Count > MonthTasks)
            {
                var more = DiaryInk.Text($"+{day.Tasks.Count - MonthTasks} más", 11, DiaryInk.Faint);
                more.Margin = new Thickness(14, 2, 0, 0);
                content.Children.Add(more);
            }
            if (day.Commits > 0)
            {
                var commits = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };
                commits.Children.Add(DiaryInk.CommitMark(DiaryInk.Muted, 8));
                var count = DiaryInk.Text($"{day.Commits}{(day.Tasks.Sum(t => t.PullRequests.Count) is > 0 and var prs ? $" · PR {prs}" : "")}", 11, DiaryInk.Faint);
                count.Margin = new Thickness(5, 0, 0, 0);
                commits.Children.Add(count);
                content.Children.Add(commits);
            }
        }

        var cell = new Border
        {
            Classes = { "monthDay" },
            Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(10, 8), CornerRadius = new CornerRadius(8), ClipToBounds = true,
            Background = worked > 0 ? DiaryInk.Of("Accent", "#61AFEF", 0.06 + 0.3 * worked / most) : inMonth ? DiaryInk.Of("TaskCardFill", "#2A2D36", 0.5) : Brushes.Transparent,
            BorderBrush = today ? DiaryInk.Of("Accent", "#61AFEF") : Brushes.Transparent, BorderThickness = new Thickness(today ? 1.5 : 1),
            Child = content, Cursor = new Cursor(StandardCursorType.Hand),
        };
        if (day is not null && !day.Empty)
            DiaryInk.Tip(cell, DayCard(day, rank > 0 ? $"{Ordinal(rank)} día con más horas de agente de {ranked}" : null));
        cell.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left) _model.OpenDay(date);
        };
        return cell;
    }

    /// <summary>Un día del mes mientras se lee: su número y dos barras grises en lugar de sus tareas.</summary>
    private static Control SkeletonCell(DateTime date, bool inMonth)
    {
        var content = new StackPanel();
        content.Children.Add(DiaryInk.Text(date.Day.ToString(Es), 13, inMonth ? DiaryInk.Muted : DiaryInk.Of("FgFaint", "#8A8D9C", 0.5), bold: true));
        if (inMonth && date <= DateTime.Today)
            foreach (var width in new[] { 0.7, 0.45 })
                content.Children.Add(new Border
                {
                    Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
                    Width = 120 * width, Background = DiaryInk.Of("TrackFill", "#20FFFFFF"),
                });
        return new Border
        {
            Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(10, 8), CornerRadius = new CornerRadius(8),
            Background = inMonth ? DiaryInk.Of("TaskCardFill", "#2A2D36", 0.5) : Brushes.Transparent, Child = content,
        };
    }

    /// <summary>Un ordinal corto: «1.º», «3.º».</summary>
    private static string Ordinal(int n) => $"{n}.º";

    /// <summary>El color de una entrada del parte: el de su repositorio, o el de su clave si solo se movió de columna.</summary>
    private static IBrush RepoOf(DiaryTask task, double opacity = 1) => DiaryInk.Repo(task.Project.Length > 0 ? task.Project : task.Key ?? "", opacity);

    // AGENDA Y PARTE

    /// <summary>Pinta los días del mes con algo, del más reciente al más antiguo, a todo el ancho.</summary>
    private void BuildAgenda()
    {
        AgendaList.Children.Clear();
        foreach (var date in AgendaWindow())
            if (_model.Days.TryGetValue(date, out var day) && !day.Empty) AgendaList.Children.Add(AgendaDay(day));
        if (!MoreAgenda()) return;
        var more = new Button { Theme = this.FindResource("ChipButton") as ControlTheme, Content = "Ver la semana anterior", Margin = new Thickness(94, 0, 0, 0) };
        DiaryInk.Tip(more, DiaryInk.Card("Ver la semana anterior", "También se lee sola al llegar al final."));
        more.Click += (_, _) =>
        {
            _agendaDays += AgendaStep;
            _ = LoadAsync();
        };
        AgendaList.Children.Add(more);
    }

    /// <summary>Un día de la agenda: la fecha en grande a la izquierda y, a la derecha, sus cifras y una tarjeta por tarea.</summary>
    private Control AgendaDay(DiaryDay day)
    {
        var today = day.Day == DateTime.Today;
        var badge = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var weekday = DiaryInk.Text(day.Day.ToString("ddd", Es).TrimEnd('.').ToUpperInvariant(), 11, today ? DiaryInk.Of("Accent", "#61AFEF") : DiaryInk.Faint, bold: true);
        weekday.HorizontalAlignment = HorizontalAlignment.Center;
        var number = DiaryInk.Text(day.Day.Day.ToString(Es), 30, today ? DiaryInk.Of("Accent", "#61AFEF") : DiaryInk.Fg, bold: true);
        number.HorizontalAlignment = HorizontalAlignment.Center;
        var month = DiaryInk.Text(day.Day.ToString("MMM", Es).TrimEnd('.'), 11, DiaryInk.Muted);
        month.HorizontalAlignment = HorizontalAlignment.Center;
        badge.Children.Add(weekday);
        badge.Children.Add(number);
        badge.Children.Add(month);
        var date = new Border
        {
            Width = 78, Padding = new Thickness(6, 10), CornerRadius = new CornerRadius(12), VerticalAlignment = VerticalAlignment.Top,
            Background = DiaryInk.Of("TaskCardFill", "#2A2D36"), BorderBrush = today ? DiaryInk.Of("Accent", "#61AFEF") : DiaryInk.Of("TaskCardEdge", "#30FFFFFF"),
            BorderThickness = new Thickness(today ? 1.5 : 1), Child = badge, Cursor = new Cursor(StandardCursorType.Hand),
        };
        DiaryInk.Tip(date, DiaryInk.Card("Abrir el día", "En la vista Día, con sus bloques en su hora."));
        date.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left) _model.OpenDay(day.Day);
        };

        var body = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };
        body.Children.Add(Pills(day));
        foreach (var task in day.Tasks) body.Children.Add(TaskCard(day, task, wide: true));
        if (PublishedCard(day) is { } published) body.Children.Add(published);

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 0, 0, 22) };
        row.Children.Add(date);
        Grid.SetColumn(body, 1);
        row.Children.Add(body);
        return row;
    }

    /// <summary>Las cifras de un día en pastillas: tiempo de agente, espera, commits, PR, tokens y pico del límite.</summary>
    private static WrapPanel Pills(DiaryDay day)
    {
        var pills = new WrapPanel { Margin = new Thickness(0, 2, 0, 8) };
        void Pill(string glyph, string text, IBrush? color = null)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = new TextBlock { Text = glyph, Classes = { "icon" }, FontSize = 11, Foreground = color ?? DiaryInk.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 6, 0) };
            content.Children.Add(icon);
            content.Children.Add(DiaryInk.Text(text, 11));
            pills.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 3), Margin = new Thickness(0, 0, 6, 6),
                Background = DiaryInk.Of("TrackFill", "#20FFFFFF"), Child = content,
            });
        }
        Pill("", $"{Diary.Hours(day.WorkingMs)} de agente", DiaryInk.Of("Accent", "#61AFEF"));
        if (day.WaitMs >= 60_000) Pill("", $"{Diary.Hours(day.WaitMs)} esperándote", DiaryInk.Wait);
        if (day.Commits > 0) Pill("", day.Commits == 1 ? "1 commit" : $"{day.Commits} commits");
        if (day.Tasks.Sum(t => t.PullRequests.Count) is > 0 and var prs) Pill("", prs == 1 ? "1 PR" : $"{prs} PR");
        Pill("", $"{Format.Tokens(day.NewTokens)} tokens");
        if (day.PeakFiveHourPct is { } peak) Pill("", $"Límite de 5 h: pico {peak:0} %");
        if (PublishedOn(day.Day).Count is > 0 and var published) Pill("", published == 1 ? "1 publicado" : $"{published} publicados");
        return pills;
    }

    /// <summary>El parte de un día para el lado derecho de la vista Día: sus cifras y una tarjeta por tarea.</summary>
    private Control DayParte(DiaryDay day)
    {
        var parte = new StackPanel();
        var title = DiaryInk.Text("PARTE DEL DÍA", 10, DiaryInk.Faint);
        title.Margin = new Thickness(2, 0, 0, 6);
        parte.Children.Add(title);
        parte.Children.Add(Pills(day));
        foreach (var task in day.Tasks) parte.Children.Add(TaskCard(day, task, wide: false));
        if (PublishedCard(day) is { } published) parte.Children.Add(published);
        return parte;
    }

    // LO PUBLICADO

    /// <summary>Lo publicado en claude.ai un día, o actualizado ese día, de lo último a lo primero.</summary>
    private static List<AgentArtifact> PublishedOn(DateTime day) => ArtifactMonitor.Instance.Items
        .Where(a => !a.Removed && !a.Deleted && (a.CreatedUtc.ToLocalTime().Date == day.Date || a.UpdatedUtc.ToLocalTime().Date == day.Date))
        .OrderByDescending(a => a.UpdatedUtc).ToList();

    /// <summary>Si un bloque es de una tarea del parte: la de su clave o, sin ella, la de su repositorio y su carpeta.</summary>
    private static bool Owns(DiaryTask task, DiaryBlock block) => block.Task is { } key
        ? string.Equals(task.Key, key, StringComparison.OrdinalIgnoreCase)
        : task.Key is null && task.Project == block.Project && task.Folder == block.Folder;

    /// <summary>Lo publicado un día desde las sesiones de una tarea del parte.</summary>
    private static List<AgentArtifact> PublishedBy(DiaryDay day, DiaryTask task)
    {
        var sessions = day.Blocks.Where(b => Owns(task, b)).Select(b => b.SessionId).ToHashSet();
        return PublishedOn(day.Day).Where(a => a.SessionId is { } id && sessions.Contains(id)).ToList();
    }

    /// <summary>Lo publicado durante un bloque: desde su sesión, entre su inicio y su fin.</summary>
    private static List<AgentArtifact> PublishedIn(DiaryBlock block) => ArtifactMonitor.Instance.ForSession(block.SessionId)
        .Where(a => !a.Removed && !a.Deleted && (Within(a.CreatedUtc, block) || Within(a.UpdatedUtc, block)))
        .OrderBy(a => a.CreatedUtc).ToList();

    /// <summary>Si un instante cae en un bloque, con un minuto de margen: la publicación puede llegar justo al cerrarlo.</summary>
    private static bool Within(DateTime utc, DiaryBlock block) =>
        utc >= block.StartUtc - TimeSpan.FromMinutes(1) && utc <= block.EndUtc + TimeSpan.FromMinutes(1);

    /// <summary>Cuándo cuenta un artifact en un día y qué pasó: publicado ese día, o solo actualizado.</summary>
    private static (DateTime Utc, string What) PublishedWhen(AgentArtifact item, DateTime day)
    {
        if (item.CreatedUtc.ToLocalTime().Date == day.Date)
            return (item.CreatedUtc, item.Versions > 1 && item.UpdatedUtc.ToLocalTime().Date == day.Date ? $"publicado · v{item.Versions}" : "publicado");
        return (item.UpdatedUtc, $"actualizado · v{item.Versions}");
    }

    /// <summary>Una línea de lo publicado: su hora, su baldosa del color de su clase, su título y qué pasó; pulsada, lo abre en
    /// Artifacts.</summary>
    private Control ArtifactLine(AgentArtifact item, DateTime day)
    {
        var (utc, what) = PublishedWhen(item, day);
        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*") };
        var at = new TextBlock { Text = utc.ToLocalTime().ToString("HH:mm", Es), FontFamily = Mono, FontSize = 11, Foreground = DiaryInk.Faint, Margin = new Thickness(0, 4, 8, 0), VerticalAlignment = VerticalAlignment.Top };
        line.Children.Add(at);
        var tile = ArtifactTile(item.Kind, 22);
        tile.VerticalAlignment = VerticalAlignment.Top;
        tile.Margin = new Thickness(0, 0, 9, 0);
        Grid.SetColumn(tile, 1);
        line.Children.Add(tile);
        var texts = new StackPanel();
        var title = DiaryInk.Text(item.Title ?? "Sin título", 12, bold: true);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        texts.Children.Add(title);
        var meta = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 1, 0, 0) };
        var kind = DiaryInk.Text(ArtifactTexts.Kind(item.Kind), 10.5, ArtifactTexts.Color(item.Kind), bold: true);
        kind.Classes.Add("ownColor");
        meta.Children.Add(kind);
        meta.Children.Add(DiaryInk.Text(what, 10.5, DiaryInk.Faint));
        texts.Children.Add(meta);
        Grid.SetColumn(texts, 2);
        line.Children.Add(texts);

        var row = new Border
        {
            CornerRadius = new CornerRadius(7), Padding = new Thickness(6, 5), Margin = new Thickness(-6, 2, -6, 0), Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand), Child = line,
        };
        row.PointerEntered += (_, _) => row.Background = DiaryInk.Of("TrackFill", "#20FFFFFF");
        row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
        DiaryInk.Tip(row, ArtifactTexts.Card(item));
        row.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left) SessionActions.RequestArtifact(item.Url);
        };
        return row;
    }

    /// <summary>La baldosa de una clase: su icono en su color sobre un fondo tenue del mismo, o la chispa de Claude si no tiene.</summary>
    private static Border ArtifactTile(ArtifactKind kind, double size)
    {
        Control mark = kind == ArtifactKind.Unknown
            ? new ClaudeLogo { Width = size * 0.5, Height = size * 0.5 }
            : new TextBlock { Text = ArtifactTexts.Icon(kind), FontSize = size * 0.5, Foreground = ArtifactTexts.Color(kind), Classes = { "icon", "ownColor" } };
        mark.HorizontalAlignment = HorizontalAlignment.Center;
        mark.VerticalAlignment = VerticalAlignment.Center;
        return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.27), Background = ArtifactTexts.Color(kind, 0.16), Child = mark };
    }

    /// <summary>Lo publicado de una tarea, bajo lo hecho: un título y una línea por artifact.</summary>
    private void AddPublished(StackPanel body, List<AgentArtifact> items, DateTime day)
    {
        if (items.Count == 0) return;
        var caption = DiaryInk.Text("PUBLICADO", 10, DiaryInk.Faint);
        caption.Margin = new Thickness(0, 10, 0, 2);
        body.Children.Add(caption);
        foreach (var item in items) body.Children.Add(ArtifactLine(item, day));
    }

    /// <summary>La tarjeta de lo publicado un día que no es de ninguna tarea del parte, como una tarea más con la franja de Claude;
    /// null si no queda nada.</summary>
    private Control? PublishedCard(DiaryDay day)
    {
        var claimed = day.Tasks.SelectMany(t => PublishedBy(day, t)).Select(a => a.Url).ToHashSet();
        var items = PublishedOn(day.Day).Where(a => !claimed.Contains(a.Url)).ToList();
        if (items.Count == 0) return null;

        var body = new StackPanel { Margin = new Thickness(12, 10, 12, 8) };
        var head = new DockPanel();
        var count = DiaryInk.Text(ArtifactTexts.Count(items.Count), 11, DiaryInk.Faint);
        count.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(count, Dock.Right);
        head.Children.Add(count);
        var logo = new ClaudeLogo { Width = 13, Height = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(logo, Dock.Left);
        head.Children.Add(logo);
        head.Children.Add(DiaryInk.Text("Publicado en claude.ai", 13, bold: true));
        body.Children.Add(head);
        foreach (var item in items) body.Children.Add(ArtifactLine(item, day.Day));

        var card = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        card.Children.Add(new Border { Width = 4, CornerRadius = new CornerRadius(10, 0, 0, 10), Background = ArtifactTexts.Color(ArtifactKind.Unknown) });
        Grid.SetColumn(body, 1);
        card.Children.Add(body);
        return new Border
        {
            Margin = new Thickness(0, 0, 0, 8), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1),
            Background = DiaryInk.Of("TaskCardFill", "#2A2D36"), BorderBrush = DiaryInk.Of("TaskCardEdge", "#30FFFFFF"), Child = card,
        };
    }

    /// <summary>La tarjeta de una tarea en el parte: su franja de color, clave, título y repositorio; sus sesiones; lo hecho; su PR y sus cambios.</summary>
    /// <remarks>Lo hecho son los commits; sin ellos, lo que se pidió, para que ninguna tarea quede sin decir nada.</remarks>
    private Control TaskCard(DiaryDay day, DiaryTask task, bool wide)
    {
        var body = new StackPanel { Margin = new Thickness(12, 10, 12, 10) };

        var head = new DockPanel();
        if (task.Sessions > 0)
        {
            var time = DiaryInk.Text($"{(task.Sessions == 1 ? "1 sesión" : $"{task.Sessions} sesiones")} · {Diary.Hours(task.WorkingMs)}"
                                     + (task.WaitMs >= 60_000 ? $" · {Diary.Hours(task.WaitMs)} esperándote" : ""), 11, DiaryInk.Faint);
            time.VerticalAlignment = VerticalAlignment.Center;
            time.Margin = new Thickness(10, 0, 0, 0);
            DockPanel.SetDock(time, wide ? Dock.Right : Dock.Bottom);
            head.Children.Add(time);
        }
        var names = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        if (task.Key is { } key)
        {
            var chip = new Border
            {
                CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 1, 6, 2), Margin = new Thickness(0, 0, 8, 2),
                Background = DiaryInk.Of("TrackFill", "#20FFFFFF"), Cursor = new Cursor(StandardCursorType.Hand),
                Child = new TextBlock { Text = key, FontFamily = Mono, FontSize = 11, Foreground = DiaryInk.Of("FgLabel", "#C8CCD6") },
            };
            DiaryInk.Tip(chip, DiaryInk.Card("Ver en el Tablero", task.Title ?? key));
            chip.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton == MouseButton.Left) TaskRequested?.Invoke(key);
            };
            names.Children.Add(chip);
        }
        var title = DiaryInk.Text(task.Key is null ? task.Folder ?? task.Project : task.Title ?? "", 13, bold: true, wrap: true);
        title.Margin = new Thickness(0, 0, 8, 2);
        if (title.Text!.Length > 0) names.Children.Add(title);
        // La pastilla del repositorio, como en DEVLAB-99 · Tienda: con tarea o con la carpeta de un worktree («master» · arkko-stack).
        if ((task.Key is not null || task.Folder is not null) && task.Project.Length > 0
            && !string.Equals(task.Project, task.Key, StringComparison.OrdinalIgnoreCase))
            names.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(9), Padding = new Thickness(7, 0, 7, 1), Margin = new Thickness(0, 1, 0, 2),
                Background = RepoOf(task, 0.18), VerticalAlignment = VerticalAlignment.Center,
                Child = DiaryInk.Text(task.Project, 10, RepoOf(task)),
            });
        head.Children.Add(names);
        body.Children.Add(head);

        if (task.Commits.Count > 0)
            foreach (var commit in task.Commits) body.Children.Add(DoneLine(commit.WhenUtc.ToLocalTime().ToString("HH:mm"), commit.Subject, DiaryInk.CommitMark(RepoOf(task), 8)));
        else if (Diary.Asked(day, task) is { Count: > 0 } asked)
        {
            var caption = DiaryInk.Text("SIN COMMITS · LO QUE SE PIDIÓ", 10, DiaryInk.Faint);
            caption.Margin = new Thickness(0, 8, 0, 0);
            body.Children.Add(caption);
            foreach (var text in asked)
            {
                var line = DiaryInk.Text($"«{text}»", 12, DiaryInk.Muted, wrap: true);
                line.FontStyle = FontStyle.Italic;
                line.Margin = new Thickness(0, 4, 0, 0);
                body.Children.Add(line);
            }
        }

        AddPublished(body, PublishedBy(day, task), day.Day);

        var marks = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var pr in task.PullRequests) marks.Children.Add(Chip($"PR #{pr}", DiaryInk.Of("Accent", "#61AFEF")));
        foreach (var move in task.Moves)
        {
            var flow = new StackPanel { Orientation = Orientation.Horizontal };
            flow.Children.Add(DiaryInk.Dotted(TaskStates.ColorOf(move.From), TaskBoard.ColumnName(move.From), 11));
            var arrow = DiaryInk.Text("→", 11, DiaryInk.Faint);
            arrow.Margin = new Thickness(6, 2, 6, 0);
            flow.Children.Add(arrow);
            flow.Children.Add(DiaryInk.Dotted(TaskStates.ColorOf(move.To), TaskBoard.ColumnName(move.To), 11));
            var chip = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 0, 8, 2), Margin = new Thickness(0, 0, 6, 4), Background = DiaryInk.Of("TrackFill", "#20FFFFFF"), Child = flow };
            DiaryInk.Tip(chip, MoveCard(move));
            marks.Children.Add(chip);
        }
        if (marks.Children.Count > 0) body.Children.Add(marks);

        var card = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        card.Children.Add(new Border { Width = 4, CornerRadius = new CornerRadius(10, 0, 0, 10), Background = RepoOf(task) });
        Grid.SetColumn(body, 1);
        card.Children.Add(body);
        return new Border
        {
            Margin = new Thickness(0, 0, 0, 8), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1),
            Background = DiaryInk.Of("TaskCardFill", "#2A2D36"), BorderBrush = DiaryInk.Of("TaskCardEdge", "#30FFFFFF"), Child = card,
        };
    }

    /// <summary>Una línea de lo hecho: su marca, su hora en monoespaciada y su texto.</summary>
    private Control DoneLine(string time, string text, Control mark)
    {
        var line = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        mark.VerticalAlignment = VerticalAlignment.Top;
        mark.Margin = new Thickness(0, 4, 8, 0);
        DockPanel.SetDock(mark, Dock.Left);
        line.Children.Add(mark);
        var at = new TextBlock { Text = time, FontFamily = Mono, FontSize = 11, Foreground = DiaryInk.Faint, Margin = new Thickness(0, 1, 8, 0) };
        DockPanel.SetDock(at, Dock.Left);
        line.Children.Add(at);
        line.Children.Add(DiaryInk.Text(text, 12, wrap: true));
        return line;
    }

    /// <summary>Una pastilla de texto en un color, con su fondo tenue.</summary>
    private static Border Chip(string text, IBrush color) => new()
    {
        CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 0, 8, 2), Margin = new Thickness(0, 0, 6, 4),
        Background = color is ISolidColorBrush solid ? new SolidColorBrush(solid.Color, 0.16) : DiaryInk.Of("TrackFill", "#20FFFFFF"),
        Child = DiaryInk.Text(text, 11, color),
    };

    // FICHA DEL BLOQUE

    /// <summary>La ficha del bloque elegido, como el detalle de una tarea en el Tablero: franja del repositorio arriba,
    /// cabecera con su hora, clave y título; cifras, línea de tiempo, sesión, cambios de columna, prompts y commits en bloques con fondo;
    /// y los botones abajo, fijos.</summary>
    private Control Detail(DiaryDay day, DiaryBlock block)
    {
        var color = DiaryInk.Repo(block.Project);
        var entry = day.Tasks.FirstOrDefault(t => Owns(t, block));

        var dock = new DockPanel();

        // Franja del repositorio, en su color y con su nombre; la cruz, a su derecha.
        var ink = Brushes.White;
        var close = new Border
        {
            Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock { Text = "", Classes = { "icon" }, FontSize = 10, Foreground = ink, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        close.PointerEntered += (_, _) => close.Background = new SolidColorBrush(Colors.Black, 0.15);
        close.PointerExited += (_, _) => close.Background = Brushes.Transparent;
        close.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left) _model.Selected = null;
        };
        DiaryInk.Tip(close, DiaryInk.Card("Cerrar la ficha"));
        var bandRow = new Grid { ColumnDefinitions = new ColumnDefinitions("24,*,24") };
        var repoName = new TextBlock { Text = block.Project.ToUpperInvariant(), FontSize = 13, FontWeight = FontWeight.Bold, Foreground = ink, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(repoName, 1);
        bandRow.Children.Add(repoName);
        Grid.SetColumn(close, 2);
        bandRow.Children.Add(close);
        var band = new Border { CornerRadius = new CornerRadius(11, 11, 0, 0), Background = RepoColors.Band(color), Padding = new Thickness(10, 5), Child = bandRow };
        DockPanel.SetDock(band, Dock.Top);
        dock.Children.Add(band);

        // Cabecera: la hora como pastilla, la clave y la carpeta; el título; el día.
        var head = new StackPanel { Margin = new Thickness(20, 16, 14, 14) };
        var line = new DockPanel();
        var tags = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        var hours = new StackPanel { Orientation = Orientation.Horizontal };
        hours.Children.Add(new Avalonia.Controls.Shapes.Ellipse { Width = 7, Height = 7, Fill = color, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 6, 0) });
        hours.Children.Add(DiaryInk.Text($"{block.StartUtc.ToLocalTime():HH:mm} – {block.EndUtc.ToLocalTime():HH:mm}", 11, bold: true));
        tags.Children.Add(new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 2, 10, 3), Background = DiaryInk.Repo(block.Project, 0.18), Child = hours, Margin = new Thickness(0, 0, 10, 4) });
        if (block.Task is { } taskKey)
        {
            var key = new TextBlock { Text = taskKey, FontFamily = Mono, FontSize = 12, Foreground = DiaryInk.Of("FgLabel", "#C8CCD6"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 4) };
            tags.Children.Add(key);
        }
        if (block.Folder is { } folder)
            tags.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 1, 7, 2), Background = DiaryInk.Of("TrackFill", "#20FFFFFF"), Margin = new Thickness(0, 0, 0, 4),
                VerticalAlignment = VerticalAlignment.Center, Child = DiaryInk.Text(folder, 10, DiaryInk.Of("FgLabel", "#C8CCD6"), bold: true),
            });
        line.Children.Add(tags);
        head.Children.Add(line);
        // El título: el de la tarea o, sin ella, el nombre de la sesión, que es el que ya enseña el bloque en el calendario.
        var title = DiaryInk.Text(block.Task is null ? block.SessionName ?? block.Folder ?? block.Project : entry?.Title ?? block.SessionName ?? block.Task, 17, bold: true, wrap: true);
        title.LineHeight = 23;
        title.Margin = new Thickness(0, 12, 0, 0);
        head.Children.Add(title);
        var when = DiaryInk.Text(DiaryModel.Capital(day.Day.ToString("dddd d 'de' MMMM", Es)), 12, DiaryInk.Muted);
        when.Margin = new Thickness(0, 4, 0, 0);
        head.Children.Add(when);
        DockPanel.SetDock(head, Dock.Top);
        dock.Children.Add(head);

        // Acciones, abajo del todo.
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var open = new Button { Theme = this.FindResource("PrimaryButton") as ControlTheme, Content = "Ver sesión", Margin = new Thickness(0, 0, 8, 0) };
        DiaryInk.Tip(open, DiaryInk.Card("Ver sesión", "La abre en Sesiones, con su chat, su mapa y su repetición."));
        open.Click += (_, _) => SessionRequested?.Invoke(block.SessionId);
        actions.Children.Add(open);
        if (block.Task is { } toOpen)
        {
            var task = new Button { Theme = this.FindResource("ChipButton") as ControlTheme, Content = "Ver tarea", VerticalAlignment = VerticalAlignment.Center };
            DiaryInk.Tip(task, DiaryInk.Card("Ver tarea", $"Abre {toOpen} en el Tablero."));
            task.Click += (_, _) => TaskRequested?.Invoke(toOpen);
            actions.Children.Add(task);
        }
        var bottom = new Border { BorderBrush = DiaryInk.Of("TaskCardEdge", "#30FFFFFF"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(20, 12), Child = actions };
        DockPanel.SetDock(bottom, Dock.Bottom);
        dock.Children.Add(bottom);

        // Los bloques, con su scroll entre la cabecera y las acciones.
        var sections = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
        sections.Children.Add(Figures(day, block, color));
        sections.Children.Add(Timeline(block, color));
        sections.Children.Add(SessionSection(block, color));
        if (PublishedIn(block) is { Count: > 0 } published)
        {
            var list = new StackPanel();
            foreach (var item in published) list.Children.Add(ArtifactLine(item, day.Day));
            sections.Children.Add(Section($"PUBLICADO ({published.Count})", list));
        }
        if (Moves(day, block, entry) is { } moves) sections.Children.Add(moves);
        if (block.Turns.Any(t => t.Text is not null)) sections.Children.Add(Prompts(block, color));
        if (entry is { Commits.Count: > 0 }) sections.Children.Add(Commits(block, entry, color));
        dock.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = sections });

        return new Border
        {
            Background = DiaryInk.Of("TaskCardFill", "#2A2D36"), BorderBrush = DiaryInk.Of("TaskCardEdge", "#30FFFFFF"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), ClipToBounds = true, Child = dock,
        };
    }

    /// <summary>Las cifras del bloque en tres fichas con icono: lo que trabajó Claude, lo que te esperó y los tokens nuevos.</summary>
    /// <remarks>
    /// La espera es el tiempo en que Claude estaba parado pidiendo un permiso o con una pregunta, hasta tu respuesta. Solo
    /// se sabe desde que se apuntan los estados: antes, sale una raya y su ficha lo explica.
    /// </remarks>
    private static Control Figures(DiaryDay day, DiaryBlock block, IBrush color)
    {
        var waited = (long)block.Waits.Sum(w => (w.EndUtc - w.StartUtc).TotalMilliseconds);
        var tiles = new (string Glyph, string Value, string Caption, IBrush Tint, Control Tip)[]
        {
            ("", Diary.Hours(block.WorkingMs), "de agente", DiaryInk.Of("Accent", "#61AFEF"),
                DiaryInk.Card("De agente", "Lo que Claude estuvo trabajando en los prompts del bloque, sumando lo que duró cada uno.")),
            ("", day.WaitsKnown(block) ? Diary.Hours(waited) : "—", "esperándote", DiaryInk.Wait,
                DiaryInk.Card("Esperándote", day.WaitsKnown(block)
                    ? "El tiempo en que Claude estuvo parado pidiéndote un permiso o con una pregunta, hasta que respondiste. En la línea de tiempo, en ámbar."
                    : "Sin datos: la app apunta cuándo te espera una sesión desde que se instaló esa función, y este bloque es de antes.")),
            ("", Format.Tokens(block.NewTokens), "tokens nuevos", color,
                DiaryInk.Card("Tokens nuevos", "Entrada, salida y escritura en caché de los prompts del bloque; sin las lecturas de caché, como en el resto de la app.")),
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*") };
        for (var i = 0; i < tiles.Length; i++)
        {
            var (glyph, value, caption, tint, tip) = tiles[i];
            var tile = new StackPanel();
            tile.Children.Add(new TextBlock { Text = glyph, Classes = { "icon" }, FontSize = 13, Foreground = tint, Margin = new Thickness(0, 0, 0, 5) });
            tile.Children.Add(DiaryInk.Text(value, 16, bold: true));
            tile.Children.Add(DiaryInk.Text(caption, 11, DiaryInk.Muted));
            var box = new Border
            {
                CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 7), Margin = new Thickness(i == 0 ? 0 : 3, 0, i == 2 ? 0 : 3, 0),
                Background = DiaryInk.Of("TaskCardFill", "#2A2D36"), Child = tile,
            };
            DiaryInk.Tip(box, tip);
            Grid.SetColumn(box, i);
            grid.Children.Add(box);
        }
        return Section("CIFRAS", grid);
    }

    /// <summary>La línea de tiempo del bloque: cada prompt en el color del repositorio y las esperas en ámbar, con su ficha al pasar.</summary>
    private static Control Timeline(DiaryBlock block, IBrush color)
    {
        var span = Math.Max(1, (block.EndUtc - block.StartUtc).TotalMinutes);
        double At(DateTime t) => (t - block.StartUtc).TotalMinutes;
        var track = new DiaryColumn { Horizontal = true, From = 0, To = span, Height = 18 };
        var number = 0;
        foreach (var turn in block.Turns)
        {
            var mine = turn.Kind == PromptKind.Prompt;
            if (mine) number++;
            // Lo que no empezó un prompt tuyo va en el color del repositorio, pero tenue: también es trabajo de Claude.
            var bar = new Border { Background = mine ? color : DiaryInk.Repo(block.Project, 0.45), CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 1, 0) };
            var (title, label) = turn.Kind switch
            {
                PromptKind.Shell => ("Comando «!»", "Comando"),
                PromptKind.Notification => ("Aviso de tarea en segundo plano", "Aviso"),
                _ => ($"Prompt {number}", "Pedido"),
            };
            DiaryInk.Tip(bar, DiaryInk.Card(title,
                $"{turn.StartUtc.ToLocalTime():HH:mm} – {turn.EndUtc.ToLocalTime():HH:mm} · {Diary.Hours((long)(turn.EndUtc - turn.StartUtc).TotalMilliseconds)}",
                color, rows: turn.Text is { } said ? [(label, said)] : null));
            track.Children.Add(DiaryColumn.Put(bar, At(turn.StartUtc), At(turn.EndUtc)));
        }
        foreach (var w in block.Waits)
        {
            var wait = new Border { Background = DiaryInk.Wait, CornerRadius = new CornerRadius(4) };
            DiaryInk.Tip(wait, DiaryInk.Card("Esperándote",
                $"{w.StartUtc.ToLocalTime():HH:mm} – {w.EndUtc.ToLocalTime():HH:mm} · {Diary.Hours((long)(w.EndUtc - w.StartUtc).TotalMilliseconds)}", DiaryInk.Wait,
                footer: "Claude pidió un permiso o hizo una pregunta y esperó tu respuesta."));
            track.Children.Add(DiaryColumn.Put(wait, At(w.StartUtc), At(w.EndUtc)));
        }
        var timeline = new StackPanel();
        timeline.Children.Add(new Border { CornerRadius = new CornerRadius(6), Background = DiaryInk.Of("TaskCardFill", "#2A2D36"), Padding = new Thickness(3), Child = track });
        var ends = new DockPanel { Margin = new Thickness(0, 5, 0, 0) };
        var last = DiaryInk.Text(block.EndUtc.ToLocalTime().ToString("HH:mm"), 10, DiaryInk.Faint);
        DockPanel.SetDock(last, Dock.Right);
        ends.Children.Add(last);
        var middle = DiaryInk.Text(TurnCounts(block, upper: false)
                                   + (block.Waits.Count > 0 ? $" · {block.Waits.Count} {(block.Waits.Count == 1 ? "espera" : "esperas")}" : ""), 10, DiaryInk.Muted);
        middle.HorizontalAlignment = HorizontalAlignment.Center;
        var first = DiaryInk.Text(block.StartUtc.ToLocalTime().ToString("HH:mm"), 10, DiaryInk.Faint);
        DockPanel.SetDock(first, Dock.Left);
        ends.Children.Add(first);
        ends.Children.Add(middle);
        timeline.Children.Add(ends);
        return Section("LÍNEA DE TIEMPO", timeline);
    }

    /// <summary>Los turnos del bloque con su hora: tus prompts numerados en el color del repositorio; los comandos «!» y los avisos, con su icono y sin número.</summary>
    private Control Prompts(DiaryBlock block, IBrush color)
    {
        var list = new StackPanel();
        var number = 0;
        foreach (var turn in block.Turns)
        {
            var mine = turn.Kind == PromptKind.Prompt;
            var mark = new Border
            {
                Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = DiaryInk.Repo(block.Project, mine ? 0.25 : 0.12),
                Child = mine
                    ? new TextBlock
                    {
                        Text = (++number).ToString(Es), FontSize = 10, FontWeight = FontWeight.SemiBold, Foreground = color,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    }
                    : new TextBlock
                    {
                        Text = turn.Kind == PromptKind.Shell ? "" : "", Classes = { "icon" }, FontSize = 10, Foreground = DiaryInk.Muted,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    },
            };
            var text = turn.Text is null ? "(texto ya no guardado)" : turn.Kind == PromptKind.Shell ? "! " + turn.Text : turn.Text;
            var line = (DockPanel)DoneLine(turn.StartUtc.ToLocalTime().ToString("HH:mm"), text, mark);
            line.Margin = default;
            if (!mine && line.Children[^1] is TextBlock said)
            {
                said.Foreground = DiaryInk.Muted;
                if (turn.Kind == PromptKind.Shell) said.FontFamily = Mono;
                else said.FontStyle = FontStyle.Italic;
            }
            var row = new Border
            {
                CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 7), Margin = new Thickness(0, 0, 0, 4), Background = DiaryInk.Of("TaskCardFill", "#2A2D36"), Child = line,
            };
            if (!mine)
                DiaryInk.Tip(row, turn.Kind == PromptKind.Shell
                    ? DiaryInk.Card("Comando «!»", "Lo lanzaste tú desde Claude Code; Claude contestó a su salida.", footer: "No cuenta como prompt, pero su tiempo y sus tokens sí.")
                    : DiaryInk.Card("Aviso de tarea en segundo plano", "Claude Code avisa así de que ha terminado algo que Claude dejó en marcha, y Claude sigue trabajando.",
                        footer: "No lo escribiste tú: no cuenta como prompt, pero su tiempo y sus tokens sí."));
            list.Children.Add(row);
        }
        return Foldable("prompts", TurnCounts(block, upper: true), list);
    }

    /// <summary>Cuántos turnos tiene el bloque, por tipo: «3 prompts · 1 comando · 2 avisos».</summary>
    private static string TurnCounts(DiaryBlock block, bool upper)
    {
        string Count(PromptKind kind, string one, string many)
        {
            var n = block.Turns.Count(t => t.Kind == kind);
            return n == 0 ? "" : $"{n} {(n == 1 ? one : many)}";
        }
        var parts = new[] { Count(PromptKind.Prompt, "prompt", "prompts"), Count(PromptKind.Shell, "comando", "comandos"), Count(PromptKind.Notification, "aviso", "avisos") }
            .Where(p => p.Length > 0);
        var text = string.Join(" · ", parts);
        return upper ? text.ToUpper(Es) : text;
    }

    /// <summary>Los commits del día en la tarea del bloque: lo que se hizo con ella, aunque fuera en otro bloque.</summary>
    private Control Commits(DiaryBlock block, DiaryTask entry, IBrush color)
    {
        var list = new StackPanel();
        foreach (var commit in entry.Commits)
        {
            var row = new Border
            {
                CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 7), Margin = new Thickness(0, 0, 0, 4), Background = DiaryInk.Of("TaskCardFill", "#2A2D36"),
                Child = DoneLine(commit.WhenUtc.ToLocalTime().ToString("HH:mm"), commit.Subject, DiaryInk.CommitMark(color, 9)),
            };
            ((Control)row.Child!).Margin = default;
            DiaryInk.Tip(row, CommitCard(commit));
            list.Children.Add(row);
        }
        return Foldable("commits", $"COMMITS DEL DÍA EN {(block.Task ?? block.Folder ?? block.Project).ToUpperInvariant()} ({entry.Commits.Count})", list);
    }

    /// <summary>Los cambios de columna del bloque: los del día en su tarea y los de cualquier tarea vistos mientras duró; null si no hay.</summary>
    /// <remarks>Los de otra tarea llevan su clave delante: mover una tarea en el panel suele hacerse desde la sesión de otra.</remarks>
    private Control? Moves(DiaryDay day, DiaryBlock block, DiaryTask? entry)
    {
        var moves = day.Tasks
            .SelectMany(t => t.Moves.Where(m => t == entry || (m.WhenUtc >= block.StartUtc && m.WhenUtc <= block.EndUtc)))
            .OrderBy(m => m.WhenUtc).ToList();
        if (moves.Count == 0) return null;
        var list = new StackPanel();
        foreach (var move in moves)
        {
            var line = new DockPanel();
            var mark = DiaryInk.MoveMark();
            mark.VerticalAlignment = VerticalAlignment.Center;
            mark.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(mark, Dock.Left);
            line.Children.Add(mark);
            var at = new TextBlock { Text = move.WhenUtc.ToLocalTime().ToString("HH:mm"), FontFamily = Mono, FontSize = 11, Foreground = DiaryInk.Faint, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            DockPanel.SetDock(at, Dock.Left);
            line.Children.Add(at);
            var flow = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (!string.Equals(move.Key, block.Task, StringComparison.OrdinalIgnoreCase))
                flow.Children.Add(new TextBlock { Text = move.Key, FontFamily = Mono, FontSize = 12, Foreground = DiaryInk.Of("FgLabel", "#C8CCD6"), Margin = new Thickness(0, 0, 10, 0) });
            flow.Children.Add(DiaryInk.Dotted(TaskStates.ColorOf(move.From), TaskBoard.ColumnName(move.From), 12));
            var arrow = DiaryInk.Text("→", 12, DiaryInk.Faint);
            arrow.Margin = new Thickness(8, 1, 8, 0);
            flow.Children.Add(arrow);
            flow.Children.Add(DiaryInk.Dotted(TaskStates.ColorOf(move.To), TaskBoard.ColumnName(move.To), 12));
            line.Children.Add(flow);
            var row = new Border
            {
                CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 7), Margin = new Thickness(0, 0, 0, 4), Background = DiaryInk.Of("TaskCardFill", "#2A2D36"), Child = line,
            };
            DiaryInk.Tip(row, MoveCard(move));
            list.Children.Add(row);
        }
        return Foldable("moves", $"CAMBIOS EN EL TABLERO ({moves.Count})", list);
    }

    /// <summary>La sesión del bloque, como una fila de sesión del Tablero: su punto de color, su nombre y dónde trabajó.</summary>
    private Control SessionSection(DiaryBlock block, IBrush color)
    {
        var row = new DockPanel();
        var dot = new Avalonia.Controls.Shapes.Ellipse { Width = 8, Height = 8, Fill = color, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 8, 0) };
        DockPanel.SetDock(dot, Dock.Left);
        row.Children.Add(dot);
        var texts = new StackPanel();
        texts.Children.Add(DiaryInk.Text(block.SessionName ?? "Sesión sin nombre", 12));
        var where = string.Join(" · ", new[] { block.Project, block.Folder, block.Prompts == 1 ? "1 prompt" : $"{block.Prompts} prompts" }.OfType<string>());
        texts.Children.Add(DiaryInk.Text(where, 11, DiaryInk.Faint));
        row.Children.Add(texts);
        var box = new Border
        {
            CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 7), Background = DiaryInk.Of("TaskCardFill", "#2A2D36"),
            Cursor = new Cursor(StandardCursorType.Hand), Child = row,
        };
        DiaryInk.Tip(box, DiaryInk.Card("Ver la sesión", "La abre en Sesiones."));
        box.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left) SessionRequested?.Invoke(block.SessionId);
        };
        return Section("SESIÓN", box);
    }

    /// <summary>Un bloque de la ficha con su rótulo, con fondo propio, como los del detalle del Tablero.</summary>
    // Los bloques de la ficha plegados, por su nombre: se recuerda al pasar de un bloque a otro.
    private static readonly HashSet<string> Folded = [];

    /// <summary>Un bloque de la ficha que se pliega pulsando su rótulo, con su flecha; plegado, solo queda el rótulo con su recuento.</summary>
    private static Border Foldable(string id, string caption, Control content)
    {
        var chevron = new TextBlock { Classes = { "icon" }, FontSize = 9, Foreground = DiaryInk.Muted, VerticalAlignment = VerticalAlignment.Center };
        var label = DiaryInk.Text(caption, 10, DiaryInk.Faint, bold: true);
        var head = new DockPanel { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand) };
        DockPanel.SetDock(chevron, Dock.Right);
        head.Children.Add(chevron);
        head.Children.Add(label);
        content.Margin = new Thickness(0, 8, 0, 0);
        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(content);

        void Show()
        {
            var folded = Folded.Contains(id);
            content.IsVisible = !folded;
            chevron.Text = folded ? "" : "";
            DiaryInk.Tip(head, DiaryInk.Card(folded ? "Desplegar" : "Plegar", "Se recuerda para los demás bloques."));
        }
        head.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            if (!Folded.Add(id)) Folded.Remove(id);
            Show();
        };
        Show();
        return new Border
        {
            Background = DiaryInk.Of("TaskSection", "#20FFFFFF"), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 12), Margin = new Thickness(14, 0, 14, 10), Child = stack,
        };
    }

    private static Border Section(string caption, Control content)
    {
        var stack = new StackPanel();
        var label = DiaryInk.Text(caption, 10, DiaryInk.Faint, bold: true);
        label.Margin = new Thickness(2, 0, 0, 8);
        stack.Children.Add(label);
        stack.Children.Add(content);
        return new Border
        {
            Background = DiaryInk.Of("TaskSection", "#20FFFFFF"), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 12), Margin = new Thickness(14, 0, 14, 10), Child = stack,
        };
    }
}
