using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>El calendario desplegable del Diario: un día, una semana o un mes, según la vista.</summary>
/// <remarks>
/// Un punto bajo un día, o bajo un mes, dice que hubo sesiones: sale de los registros de prompts, sin calcular nada.
/// En las semanas se marca la fila entera al pasar, con su número de semana delante.
/// </remarks>
public partial class DiaryView
{
    private DateTime _pickMonth;
    private HashSet<DateTime> _active = [];

    /// <summary>Abre el calendario en el mes, o el año, de lo que se ve.</summary>
    private void Range_Click(object? sender, RoutedEventArgs e)
    {
        _pickMonth = new DateTime(_model.Anchor.Year, _model.Anchor.Month, 1);
        _active = ActiveDays();
        FillPicker();
        Picker.IsOpen = true;
    }

    /// <summary>Los días con prompts registrados, por los nombres de sus ficheros.</summary>
    private static HashSet<DateTime> ActiveDays()
    {
        var days = new HashSet<DateTime>();
        if (!Directory.Exists(AppPaths.Prompts)) return days;
        foreach (var file in Directory.EnumerateFiles(AppPaths.Prompts, "*.jsonl"))
            if (DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                days.Add(day);
        return days;
    }

    /// <summary>Llena el calendario: los meses del año en Mes y Agenda; si no, los días del mes.</summary>
    private void FillPicker()
    {
        PickerBody.Children.Clear();
        var months = _model.Mode is DiaryMode.Month or DiaryMode.Agenda;
        PickerBody.Children.Add(PickerHead(months ? _pickMonth.Year.ToString(Es) : DiaryModel.Capital(_pickMonth.ToString("MMMM yyyy", Es)), months));
        if (months) FillMonths();
        else FillDays(weeks: _model.Mode != DiaryMode.Day);

        var foot = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var today = new Button { Theme = this.FindResource("ChipButton") as ControlTheme, Content = "Hoy" };
        today.Click += (_, _) => Pick(DateTime.Today);
        foot.Children.Add(today);
        var hint = DiaryInk.Text("● con sesiones", 10, DiaryInk.Faint);
        hint.HorizontalAlignment = HorizontalAlignment.Right;
        hint.VerticalAlignment = VerticalAlignment.Center;
        foot.Children.Add(hint);
        PickerBody.Children.Add(foot);
    }

    /// <summary>La cabecera del calendario: el mes o el año, entre sus flechas.</summary>
    private DockPanel PickerHead(string title, bool years)
    {
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        Button Arrow(string glyph, int step, string tip)
        {
            var button = new Button { Theme = this.FindResource("ChipButton") as ControlTheme, Content = new TextBlock { Text = glyph, Classes = { "icon" }, FontSize = 10, Margin = new Thickness(0, 2) } };
            DiaryInk.Tip(button, DiaryInk.Card(tip));
            button.Click += (_, _) =>
            {
                _pickMonth = years ? _pickMonth.AddYears(step) : _pickMonth.AddMonths(step);
                FillPicker();
            };
            return button;
        }
        var previous = Arrow("", -1, years ? "Año anterior" : "Mes anterior");
        DockPanel.SetDock(previous, Dock.Left);
        head.Children.Add(previous);
        var next = Arrow("", 1, years ? "Año siguiente" : "Mes siguiente");
        DockPanel.SetDock(next, Dock.Right);
        head.Children.Add(next);
        var text = DiaryInk.Text(title, 13, bold: true);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(text);
        return head;
    }

    /// <summary>Los días del mes de lunes a domingo; en las semanas, una fila por semana que se elige entera.</summary>
    private void FillDays(bool weeks)
    {
        var start = DiaryModel.Monday(_pickMonth);
        var shown = _model.Range.ToHashSet();

        var names = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
        if (weeks) names.Children.Add(Cell(DiaryInk.Text("sem", 10, DiaryInk.Faint), 30));
        for (var c = 0; c < 7; c++) names.Children.Add(Cell(DiaryInk.Text(start.AddDays(c).ToString("ddd", Es)[..2], 10, DiaryInk.Faint)));
        PickerBody.Children.Add(names);

        for (var row = 0; row < 6; row++)
        {
            var monday = start.AddDays(row * 7);
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            if (weeks)
                line.Children.Add(Cell(DiaryInk.Text(ISOWeek.GetWeekOfYear(monday).ToString(Es), 10, DiaryInk.Faint), 30));
            for (var c = 0; c < 7; c++)
            {
                var day = monday.AddDays(c);
                var face = DayFace(day, shown.Contains(day));
                if (weeks) line.Children.Add(face);
                else
                {
                    var pick = new Border { Classes = { "pick" }, Child = face };
                    pick.PointerReleased += (_, e) =>
                    {
                        if (e.InitialPressMouseButton == MouseButton.Left) Pick(day);
                    };
                    line.Children.Add(pick);
                }
            }
            if (!weeks)
            {
                PickerBody.Children.Add(line);
                continue;
            }
            var week = new Border { Classes = { "weekRow" }, CornerRadius = new CornerRadius(8), Cursor = new Cursor(StandardCursorType.Hand), Child = line };
            if (shown.Contains(monday) || shown.Contains(monday.AddDays(4))) week.Background = DiaryInk.Of("ChipOn", "#66A3141D");
            DiaryInk.Tip(week, DiaryInk.Card($"Semana {ISOWeek.GetWeekOfYear(monday)}",
                $"{monday.ToString("d MMM", Es)} – {monday.AddDays(6).ToString("d MMM", Es)}",
                rows: [("Con sesiones", $"{Enumerable.Range(0, 7).Count(i => _active.Contains(monday.AddDays(i)))} de 7 días")]));
            week.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton == MouseButton.Left) Pick(monday);
            };
            PickerBody.Children.Add(week);
        }
    }

    /// <summary>Un día del calendario: su número, en el acento si es hoy y apagado si es de otro mes, y un punto si hubo sesiones.</summary>
    private Control DayFace(DateTime day, bool shown)
    {
        // En Día, el borde del día elegible ocupa el píxel que en las semanas es margen: así cuadran con la cabecera.
        var face = new StackPanel { Width = 34, Height = 32, Margin = new Thickness(_model.Mode == DiaryMode.Day ? 0 : 1) };
        var color = day == DateTime.Today ? DiaryInk.Of("Accent", "#61AFEF") : day.Month == _pickMonth.Month ? DiaryInk.Fg : DiaryInk.Of("FgFaint", "#8A8D9C", 0.5);
        var number = DiaryInk.Text(day.Day.ToString(Es), 12, color, bold: shown || day == DateTime.Today);
        number.HorizontalAlignment = HorizontalAlignment.Center;
        number.Margin = new Thickness(0, 5, 0, 0);
        face.Children.Add(number);
        face.Children.Add(new Avalonia.Controls.Shapes.Ellipse
        {
            Width = 4, Height = 4, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0),
            Fill = _active.Contains(day) ? DiaryInk.Of("Accent", "#61AFEF") : Brushes.Transparent,
        });
        if (shown && _model.Mode == DiaryMode.Day) face.Background = DiaryInk.Of("ChipOn", "#66A3141D");
        return face;
    }

    /// <summary>Los doce meses del año, con un punto en los que hubo sesiones.</summary>
    private void FillMonths()
    {
        var grid = new UniformGrid { Columns = 3, Rows = 4 };
        for (var m = 1; m <= 12; m++)
        {
            var month = new DateTime(_pickMonth.Year, m, 1);
            var face = new StackPanel { Width = 74, Height = 40, Margin = new Thickness(2) };
            var current = month.Year == _model.Anchor.Year && month.Month == _model.Anchor.Month;
            var name = DiaryInk.Text(DiaryModel.Capital(month.ToString("MMM", Es).TrimEnd('.')), 12,
                month.Year == DateTime.Today.Year && month.Month == DateTime.Today.Month ? DiaryInk.Of("Accent", "#61AFEF") : DiaryInk.Fg, bold: current);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            name.Margin = new Thickness(0, 9, 0, 0);
            face.Children.Add(name);
            var days = _active.Count(d => d.Year == month.Year && d.Month == m);
            face.Children.Add(new Avalonia.Controls.Shapes.Ellipse
            {
                Width = 4, Height = 4, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0),
                Fill = days > 0 ? DiaryInk.Of("Accent", "#61AFEF") : Brushes.Transparent,
            });
            var pick = new Border { Classes = { "pick" }, Child = face };
            if (current) pick.Background = DiaryInk.Of("ChipOn", "#66A3141D");
            DiaryInk.Tip(pick, DiaryInk.Card(DiaryModel.Capital(month.ToString("MMMM yyyy", Es)), days == 0 ? "Sin sesiones" : days == 1 ? "1 día con sesiones" : $"{days} días con sesiones"));
            pick.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton == MouseButton.Left) Pick(month);
            };
            grid.Children.Add(pick);
        }
        PickerBody.Children.Add(grid);
    }

    /// <summary>Una celda de ancho fijo con su contenido centrado, para alinear la cabecera con los días.</summary>
    private static Control Cell(TextBlock text, double width = 36)
    {
        text.HorizontalAlignment = HorizontalAlignment.Center;
        return new Border { Width = width, Child = text };
    }

    /// <summary>Lleva la vista a una fecha y cierra el calendario.</summary>
    private void Pick(DateTime date)
    {
        Picker.IsOpen = false;
        _model.Anchor = date;
    }
}
