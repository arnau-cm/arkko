using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>El calendario por horas del Diario: Día, Semana laboral y Semana.</summary>
public partial class DiaryView
{
    // Alto de un minuto: 48 píxeles por hora.
    private const double MinuteHeight = 0.8;
    private const double GutterWidth = 52;

    // Lo que dejan libre los bloques a la derecha de cada día, para las marcas de los commits.
    private const double MarkGutter = 14;

    // Lo que dejan libre a la izquierda, solo los días con cambios de columna, para que su marca no tape la hora del bloque.
    private const double MoveGutter = 20;

    // Alto mínimo de un bloque, en el que cabe una línea con su hora y su nombre; y el que pide una segunda y una tercera.
    private const double BlockMin = 18;
    private const double TwoLines = 34;
    private const double ThreeLines = 60;

    private readonly Dictionary<DiaryBlock, Border> _blocks = new();
    private bool _scrolled;

    /// <summary>Pinta la cabecera de cada día y las horas con sus bloques, sus commits y sus cambios de columna.</summary>
    private void BuildGrid()
    {
        var range = _model.Range;
        var days = range.Select(d => _model.Days.GetValueOrDefault(d)).ToList();
        var (from, to) = VisibleHours();

        DayHeaders.Children.Clear();
        HourGrid.Children.Clear();
        DayHeaders.ColumnDefinitions = Columns(range.Count);
        HourGrid.ColumnDefinitions = Columns(range.Count);

        var gutter = new DiaryColumn { From = from * 60, To = to * 60, MinuteHeight = MinuteHeight };
        for (var h = from; h <= to; h++)
        {
            var label = DiaryInk.Text($"{h:00}:00", 10, DiaryInk.Faint);
            label.HorizontalAlignment = HorizontalAlignment.Right;
            label.Margin = new Thickness(0, 0, 8, 0);
            gutter.Children.Add(DiaryColumn.Put(label, h * 60, place: DiaryPlace.Line));
        }
        HourGrid.Children.Add(gutter);

        for (var i = 0; i < range.Count; i++)
        {
            var header = DayHeader(range[i], days[i]);
            Grid.SetColumn(header, i + 1);
            DayHeaders.Children.Add(header);

            var column = DayColumn(range[i], days[i], from, to);
            Grid.SetColumn(column, i + 1);
            HourGrid.Children.Add(column);
        }
        Mark();
        Scroll(days, from);
    }

    /// <summary>Con el día entero, lleva el scroll al primer bloque una vez por rango; con el horario, arriba.</summary>
    private void Scroll(List<DiaryDay?> days, int from)
    {
        if (_scrolled) return;
        if (_model.WorkHoursOnly)
        {
            _scrolled = true;
            Dispatcher.UIThread.Post(() => Body.Offset = default, DispatcherPriority.Background);
            return;
        }
        var blocks = days.OfType<DiaryDay>().SelectMany(d => d.Blocks.Select(b => Minute(b.StartUtc, d.Day))).ToList();
        if (blocks.Count == 0) return;
        _scrolled = true;
        var first = blocks.Min();
        Dispatcher.UIThread.Post(() => Body.Offset = new Vector(0, Math.Max(0, (first - from * 60 - 30) * MinuteHeight)), DispatcherPriority.Background);
    }

    /// <summary>La columna de las horas y una por día, a partes iguales.</summary>
    private static ColumnDefinitions Columns(int days)
    {
        var columns = new ColumnDefinitions { new(GutterWidth, GridUnitType.Pixel) };
        for (var i = 0; i < days; i++) columns.Add(new ColumnDefinition(1, GridUnitType.Star));
        return columns;
    }

    /// <summary>Las horas que se ven: la franja del horario laboral, o el día entero.</summary>
    private (int From, int To) VisibleHours()
    {
        if (!_model.WorkHoursOnly) return (0, 24);
        var schedule = DiaryModel.Schedule;
        var from = Math.Clamp(schedule.StartMinutes / 60, 0, 23);
        var to = Math.Clamp((schedule.EndMinutes + 59) / 60, from + 1, 24);
        return (from, to);
    }

    /// <summary>El minuto del día de un instante, contado desde la medianoche de ese día; puede pasar de 1440 o ser negativo.</summary>
    private static double Minute(DateTime utc, DateTime day) => (utc.ToLocalTime() - day.Date).TotalMinutes;

    /// <summary>La cabecera de un día: su nombre y número y, debajo, lo que se trabajó, con su ficha al pasar.</summary>
    private Control DayHeader(DateTime date, DiaryDay? day)
    {
        var today = date == DateTime.Today;
        var panel = new StackPanel { Margin = new Thickness(8, 0, 4, 0), Background = Brushes.Transparent };
        var name = DiaryInk.Text(date.ToString("ddd d", Es), 13, today ? DiaryInk.Of("Accent", "#61AFEF") : DiaryInk.Fg, bold: true);
        if (today) name.FontWeight = FontWeight.Bold;
        panel.Children.Add(name);
        var parts = new List<string>();
        if (day is { Blocks.Count: > 0 }) parts.Add(Diary.Hours(day.WorkingMs));
        if (day is { Commits: > 0 }) parts.Add(day.Commits == 1 ? "1 commit" : $"{day.Commits} commits");
        var prs = day?.Tasks.Sum(t => t.PullRequests.Count) ?? 0;
        if (prs > 0) parts.Add($"PR {prs}");
        panel.Children.Add(DiaryInk.Text(parts.Count == 0 ? " " : string.Join(" · ", parts), 11, DiaryInk.Faint));
        if (_model.IsDay)
        {
            if (day is not null && !day.Empty) DiaryInk.Tip(panel, DayCard(day));
            return panel;
        }
        // En las semanas, la cabecera abre su día en la vista Día.
        panel.Cursor = new Cursor(StandardCursorType.Hand);
        DiaryInk.Tip(panel, day is not null && !day.Empty
            ? DayCard(day, footer: "Clic para verlo en la vista Día")
            : DiaryInk.Card(DiaryModel.Capital(date.ToString("dddd d 'de' MMMM", Es)), "Sin sesiones", footer: "Clic para verlo en la vista Día"));
        panel.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left) _model.OpenDay(date);
        };
        return panel;
    }

    /// <summary>La ficha de un día: lo trabajado y sus tareas, cada una con su color y su tiempo.</summary>
    private static Control DayCard(DiaryDay day, string? note = null, string? footer = null)
    {
        var tasks = day.Tasks.Take(6).Select(t =>
        {
            var row = DiaryInk.Dotted(DiaryInk.Repo(t.Project == "" ? t.Key ?? "" : t.Project), t.Key ?? t.Folder ?? t.Project);
            var time = DiaryInk.Text(t.Sessions > 0 ? Diary.Hours(t.WorkingMs) : "sin sesiones", 11, DiaryInk.Muted);
            time.Margin = new Thickness(16, 0, 0, 0);
            DockPanel.SetDock(time, Dock.Right);
            row.Children.Insert(1, time);
            return (Control)row;
        }).ToList();
        if (day.Tasks.Count > 6) tasks.Add(DiaryInk.Text($"+{day.Tasks.Count - 6} más", 11, DiaryInk.Faint));
        var head = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        head.Children.Add(DiaryInk.Text("TAREAS Y REPOSITORIOS", 10, DiaryInk.Faint));
        foreach (var t in tasks) head.Children.Add(t);
        var figures = new List<(string, string)> { (Diary.Hours(day.WorkingMs), "de agente"), (day.Commits.ToString(Es), "commits"), (Format.Tokens(day.NewTokens), "tokens") };
        var rows = new List<(string, string)>();
        if (day.WaitMs >= 60_000) rows.Add(("Esperándote", Diary.Hours(day.WaitMs)));
        if (day.PeakFiveHourPct is { } peak) rows.Add(("Pico del límite de 5 h", $"{peak:0} %"));
        if (note is not null) rows.Add(("En el mes", note));
        return DiaryInk.Card(DiaryModel.Capital(day.Day.ToString("dddd d 'de' MMMM", Es)), null, null, figures, rows, [head], footer);
    }

    /// <summary>La columna de un día: las horas, los bloques, los commits, los cambios de columna, lo publicado, la hora actual y lo que
    /// queda fuera.</summary>
    private Control DayColumn(DateTime date, DiaryDay? day, int from, int to)
    {
        var moves = day?.Tasks.Any(t => t.Moves.Count > 0) == true;
        var column = new DiaryColumn
        {
            From = from * 60, To = to * 60, MinuteHeight = MinuteHeight, MarkGutter = MarkGutter, MoveGutter = moves ? MoveGutter : 0, MinBlock = BlockMin, ClipToBounds = true,
        };
        for (var h = from; h <= to; h++)
            column.Children.Add(DiaryColumn.Put(new Border { Height = 1, Background = DiaryInk.Of("TrackEdge", "#20FFFFFF") }, h * 60, place: DiaryPlace.Line));

        var before = new List<DiaryBlock>();
        var after = new List<DiaryBlock>();
        if (day is not null)
        {
            foreach (var (block, lane, lanes) in Lanes(day))
            {
                var start = Minute(block.StartUtc, date);
                var end = Minute(block.EndUtc, date);
                if (end <= from * 60) before.Add(block);
                else if (start >= to * 60) after.Add(block);
                else column.Children.Add(DiaryColumn.Put(Block(day, block, Math.Max(start, from * 60), Math.Min(end, to * 60)),
                    Math.Max(start, from * 60), Math.Min(end, to * 60), DiaryPlace.Block, lane, lanes));
            }
            foreach (var task in day.Tasks)
            {
                foreach (var commit in task.Commits)
                {
                    var minute = Minute(commit.WhenUtc, date);
                    if (minute < from * 60 || minute > to * 60) continue;
                    var mark = DiaryInk.CommitMark(DiaryInk.Repo(commit.Project));
                    DiaryInk.Tip(mark, CommitCard(commit));
                    column.Children.Add(DiaryColumn.Put(mark, minute, place: DiaryPlace.Right));
                }
                foreach (var move in task.Moves)
                {
                    var minute = Minute(move.WhenUtc, date);
                    if (minute < from * 60 || minute > to * 60) continue;
                    var mark = DiaryInk.MoveMark();
                    DiaryInk.Tip(mark, MoveCard(move));
                    column.Children.Add(DiaryColumn.Put(mark, minute, place: DiaryPlace.Left));
                }
            }
        }
        foreach (var item in PublishedOn(date))
        {
            var minute = Minute(PublishedWhen(item, date).Utc, date);
            if (minute < from * 60 || minute > to * 60) continue;
            var mark = ArtifactTile(item.Kind, 13);
            mark.Cursor = new Cursor(StandardCursorType.Hand);
            DiaryInk.Tip(mark, ArtifactTexts.Card(item));
            mark.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton == MouseButton.Left) SessionActions.RequestArtifact(item.Url);
            };
            column.Children.Add(DiaryColumn.Put(mark, minute, place: DiaryPlace.Right));
        }
        var now = DateTime.Now.TimeOfDay.TotalMinutes;
        if (date == DateTime.Today && now >= from * 60 && now <= to * 60)
            column.Children.Add(DiaryColumn.Put(new Border { Height = 2, Background = DiaryInk.Of("Danger", "#E03E4C"), IsHitTestVisible = false }, now, place: DiaryPlace.Line));

        var cell = new Panel();
        cell.Children.Add(column);
        if (before.Count > 0) cell.Children.Add(Outside(before, day!, up: true));
        if (after.Count > 0) cell.Children.Add(Outside(after, day!, up: false));
        // Separador con el día anterior, a toda la altura.
        return new Border { BorderBrush = DiaryInk.Of("TrackEdge", "#20FFFFFF"), BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(2, 0), Child = cell };
    }

    /// <summary>El aviso de lo que queda fuera del horario, arriba o abajo de un día: al pulsarlo se ve el día entero.</summary>
    private Control Outside(List<DiaryBlock> blocks, DiaryDay day, bool up)
    {
        var chip = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = up ? VerticalAlignment.Top : VerticalAlignment.Bottom,
            Margin = new Thickness(0, up ? 2 : 0, 0, up ? 0 : 2), Padding = new Thickness(8, 1, 8, 2), CornerRadius = new CornerRadius(9),
            Background = DiaryInk.Of("TipFill", "#202228"), BorderBrush = DiaryInk.Of("TipEdge", "#30FFFFFF"), BorderThickness = new Thickness(1),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = DiaryInk.Text($"{(up ? "▲" : "▼")} {(blocks.Count == 1 ? "1 bloque" : $"{blocks.Count} bloques")} {(up ? "antes" : "después")}", 10, DiaryInk.Muted),
        };
        DiaryInk.Tip(chip, DiaryInk.Card(up ? "Antes del horario laboral" : "Después del horario laboral", null, null, null,
            blocks.Select(b => ($"{b.StartUtc.ToLocalTime():HH:mm}–{b.EndUtc.ToLocalTime():HH:mm}", Label(day, b))),
            footer: "Clic para ver el día entero"));
        chip.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left) _model.WorkHoursOnly = false;
        };
        return chip;
    }

    /// <summary>Los bloques de un día con su carril: los que se solapan en pantalla van lado a lado.</summary>
    /// <remarks>Un bloque corto ocupa en pantalla más que su tiempo, el alto de una línea: eso es lo que cuenta para solaparse.</remarks>
    private static List<(DiaryBlock Block, int Lane, int Lanes)> Lanes(DiaryDay day)
    {
        var minVisible = TimeSpan.FromMinutes(BlockMin / MinuteHeight);
        DateTime End(DiaryBlock b) => b.EndUtc - b.StartUtc < minVisible ? b.StartUtc + minVisible : b.EndUtc;

        var result = new List<(DiaryBlock, int, int)>();
        var group = new List<(DiaryBlock Block, int Lane)>();
        var ends = new List<DateTime>();
        var groupEnd = DateTime.MinValue;

        void Flush()
        {
            result.AddRange(group.Select(g => (g.Block, g.Lane, ends.Count)));
            group.Clear();
            ends.Clear();
        }

        foreach (var block in day.Blocks.OrderBy(b => b.StartUtc))
        {
            if (block.StartUtc >= groupEnd) Flush();
            var lane = ends.FindIndex(end => end <= block.StartUtc);
            if (lane < 0)
            {
                lane = ends.Count;
                ends.Add(End(block));
            }
            else ends[lane] = End(block);
            group.Add((block, lane));
            if (End(block) > groupEnd) groupEnd = End(block);
        }
        Flush();
        return result;
    }

    /// <summary>Lo que dice un bloque de sí mismo: su tarea o, sin ella, la carpeta de su worktree («master») o su repositorio.</summary>
    private static string Label(DiaryDay day, DiaryBlock block) => block.Task ?? block.Folder ?? block.Project;

    /// <summary>El título de la tarea de un bloque, si se conoce.</summary>
    private static string? TitleOf(DiaryDay day, DiaryBlock block) =>
        block.Task is { } key ? day.Tasks.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase))?.Title : null;

    /// <summary>Un bloque, tintado del color de su repositorio, con una franja, sus esperas en ámbar y siempre algo de texto.</summary>
    /// <remarks>
    /// Corto, una línea con su hora y su nombre; más alto, su nombre, el título de su tarea o su sesión y, si cabe, la franja
    /// de horas. Se pinta entre los minutos que se ven, que con el horario laboral pueden ser menos que los suyos.
    /// </remarks>
    private Border Block(DiaryDay day, DiaryBlock block, double start, double end)
    {
        var height = Math.Max(BlockMin, (end - start) * MinuteHeight);
        var color = DiaryInk.Repo(block.Project);

        var waits = new DiaryColumn { From = start, To = Math.Max(start + 1, end), IsHitTestVisible = false };
        foreach (var w in block.Waits)
            waits.Children.Add(DiaryColumn.Put(new Border { Background = DiaryInk.Of("DataFiveHour", "#E5C07B", 0.4) },
                Math.Max(start, Minute(w.StartUtc, day.Day)), Math.Min(end, Minute(w.EndUtc, day.Day))));

        var label = Label(day, block);
        var time = block.StartUtc.ToLocalTime().ToString("HH:mm");
        StackPanel text;
        if (height < TwoLines)
        {
            text = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
            var at = DiaryInk.Text(time, 10, DiaryInk.Muted);
            at.Margin = new Thickness(0, 0, 6, 0);
            text.Children.Add(at);
            text.Children.Add(DiaryInk.Text(label, 10.5, bold: true));
        }
        else
        {
            text = new StackPanel { Margin = new Thickness(8, 3, 4, 2) };
            text.Children.Add(DiaryInk.Text(label, 11, bold: true));
            if ((TitleOf(day, block) ?? block.SessionName) is { } second)
            {
                var line = DiaryInk.Text(second, 10, DiaryInk.Muted, wrap: true);
                line.MaxLines = height >= ThreeLines + 14 ? 2 : 1;
                line.TextTrimming = TextTrimming.CharacterEllipsis;
                text.Children.Add(line);
            }
            if (height >= ThreeLines)
                text.Children.Add(DiaryInk.Text($"{time} – {block.EndUtc.ToLocalTime():HH:mm} · {Diary.Hours(block.WorkingMs)}", 10, DiaryInk.Faint));
        }
        text.IsHitTestVisible = false;

        var content = new Panel();
        // El tinte del repositorio va sobre un fondo opaco: translúcido, las líneas de las horas se veían a través y parecían marcas.
        content.Children.Add(new Border { Background = DiaryInk.Repo(block.Project, 0.22) });
        content.Children.Add(waits);
        content.Children.Add(new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Left, Background = color });
        content.Children.Add(text);

        var border = new Border
        {
            Classes = { "block" },
            Background = DiaryInk.Of("TaskCardFill", "#2A2D36"),
            BorderBrush = DiaryInk.Repo(block.Project, 0.5),
            Child = content,
        };
        DiaryInk.Tip(border, BlockCard(day, block));
        border.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            _model.Selected = _model.Selected is { } s && s.Block == block ? null : (day, block);
        };
        _blocks[block] = border;
        return border;
    }

    /// <summary>La ficha de un bloque al pasar: qué, de qué repositorio y sesión, cuándo y cuánto.</summary>
    internal static Control BlockCard(DiaryDay day, DiaryBlock block)
    {
        var title = TitleOf(day, block);
        var name = block.Task is { } key ? title is null ? key : $"{key} · {title}" : block.Folder ?? block.Project;
        var subtitle = block.Task is null && block.Folder is null
            ? block.SessionName
            : $"{block.Project}{(block.SessionName is { } s ? " · " + s : "")}";
        var waited = (long)block.Waits.Sum(w => (w.EndUtc - w.StartUtc).TotalMilliseconds);
        return DiaryInk.Card(name, subtitle, DiaryInk.Repo(block.Project),
            [(Diary.Hours(block.WorkingMs), "de agente"), (day.WaitsKnown(block) ? Diary.Hours(waited) : "—", "esperándote"), (Format.Tokens(block.NewTokens), "tokens")],
            [
                ("Horas", $"{block.StartUtc.ToLocalTime():HH:mm} – {block.EndUtc.ToLocalTime():HH:mm}"),
                ("Prompts", block.Prompts.ToString(Es)),
            ],
            footer: "Clic para ver su ficha");
    }

    /// <summary>La ficha de un commit: su mensaje, cuándo, de qué repositorio y tarea.</summary>
    private static Control CommitCard(DiaryCommit commit)
    {
        var rows = new List<(string, string)> { ("Repositorio", commit.Project) };
        if (commit.Task is { } task) rows.Add(("Tarea", task));
        rows.Add(("Commit", commit.Hash.Length > 8 ? commit.Hash[..8] : commit.Hash));
        return DiaryInk.Card(commit.Subject, $"Commit · {commit.WhenUtc.ToLocalTime():HH:mm}", DiaryInk.Repo(commit.Project), rows: rows);
    }

    /// <summary>La ficha de un cambio de columna: la tarea, de dónde a dónde y cuándo se vio.</summary>
    private static Control MoveCard(TaskStateChange move)
    {
        var flow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        flow.Children.Add(DiaryInk.Dotted(TaskStates.ColorOf(move.From), TaskBoard.ColumnName(move.From), 12));
        var arrow = DiaryInk.Text("→", 12, DiaryInk.Faint);
        arrow.Margin = new Thickness(10, 2, 10, 0);
        flow.Children.Add(arrow);
        flow.Children.Add(DiaryInk.Dotted(TaskStates.ColorOf(move.To), TaskBoard.ColumnName(move.To), 12));
        return DiaryInk.Card(move.Key, $"Cambio de columna · {move.WhenUtc.ToLocalTime():HH:mm}", extra: [flow],
            footer: "La hora es la de la lectura del tablero que lo vio, no la del cambio en el panel.");
    }

    /// <summary>Marca el bloque elegido.</summary>
    private void Mark()
    {
        foreach (var (block, border) in _blocks) border.Classes.Set("selected", _model.Selected is { } s && s.Block == block);
    }
}
