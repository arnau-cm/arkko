using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>El widget de Estadísticas: las cifras clave arriba y, debajo, un mosaico de gráficas, tantas como quepan (una en S,
/// cuatro en M, seis en L), en el orden en que se marcan en el menú.</summary>
/// <remarks>
/// Lee el histórico como la sección (<see cref="History"/>), pero el periodo, el horario, las cifras y las gráficas son del
/// widget. Lleva tres gráficas que el panel no tiene (actividad por hora, reparto por modelo y coste por prompt), para
/// probarlas aquí antes de llevarlas a la sección. Una cifra que se sale de lo normal frente al tramo anterior (el doble o
/// más, o la caché quince puntos por debajo) se marca en ámbar y dice por qué.
/// </remarks>
internal sealed class StatsWidget : CorkWidget
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    // Las cifras clave y las gráficas: lo que se guarda de cada una y cómo se dice.
    private static readonly (string Id, string Name)[] Figures =
        [("tokens", "Tokens nuevos"), ("prompts", "Prompts"), ("sessions", "Sesiones"), ("time", "Tiempo de Claude"), ("cache", "De la entrada, en caché")];
    private static readonly (string Id, string Name)[] Charts =
    [
        ("history", "Histórico"), ("projects", "Por proyecto"), ("limits", "Evolución de los límites"),
        ("heat", "Actividad por hora"), ("cache", "Caché"), ("costly", "Prompts más caros"),
        ("models", "Reparto por modelo"), ("perprompt", "Coste por prompt"),
    ];
    private static readonly string[] DefaultCharts = ["history", "projects", "limits", "heat"];
    private static readonly (HistoryPeriod Period, string Short, string Long)[] Periods =
        [(HistoryPeriod.Today, "Hoy", "Hoy"), (HistoryPeriod.Week, "7 d", "7 días"), (HistoryPeriod.Month, "30 d", "30 días"), (HistoryPeriod.All, "Todo", "Todo")];

    // Lo que cuenta como salirse de lo normal frente al tramo anterior: el doble, o la caché tantos puntos por debajo.
    private const double UnusualRatio = 2;
    private const double UnusualCacheDrop = 15;

    private readonly DispatcherTimer _reload = new() { Interval = TimeSpan.FromSeconds(30) };
    private Snapshot? _data;
    private bool _loading;
    private int _generation;

    /// <summary>Lo leído para un periodo: lo que pintan las cifras y las gráficas.</summary>
    private sealed record Snapshot(HistoryPeriod Period, List<PromptRecord> Records, HistorySummary Summary, HistorySummary? Before,
        List<SummaryBucket> Series, BucketSize Size, List<UsageBucket> Usage, List<CacheBucket> Cache, List<LimitsSample> Limits,
        List<PromptRecord> Week);

    /// <summary>Crea el widget; lee al verse.</summary>
    public StatsWidget()
    {
        _reload.Tick += (_, _) => _ = LoadAsync();
    }

    public override PanelSection Section => PanelSection.Stats;

    /// <summary>Ámbar si alguna de las cifras que se ven se sale de lo normal; nada si no.</summary>
    public override IBrush? Mood =>
        _data is { } data && ShownFigures().Any(f => Unusual(f.Id, data) is not null) ? Brushes_.Waiting : null;

    /// <summary>El periodo que se mira.</summary>
    public override string? Headline => Periods.First(p => p.Period == Period).Long;

    /// <summary>Las cifras clave que se enseñan, las marcadas en el menú.</summary>
    private List<(string Id, string Name)> ShownFigures()
    {
        var marked = GetSet("figs", Figures.Select(x => x.Id));
        return Figures.Where(f => marked.Contains(f.Id)).ToList();
    }

    /// <summary>Por qué una cifra se sale de lo normal frente al tramo anterior; null si no se sale o no hay con qué comparar.</summary>
    /// <remarks>Lo que antes era casi cero no cuenta: ya lo dice «nuevo» en su cambio.</remarks>
    private static string? Unusual(string id, Snapshot data)
    {
        var (_, _, before, _, now) = Measure(id, data);
        if (before is not { } old) return null;
        if (id == "cache")
            return old - now >= UnusualCacheDrop ? $"La caché ha bajado {old - now:0} puntos frente al tramo anterior." : null;
        if (old <= 0 || now / old >= 100 || now / old < UnusualRatio) return null;
        return $"{now / old:0.#} veces lo del tramo anterior.";
    }
    private HistoryPeriod Period => Enum.TryParse<HistoryPeriod>(Get("period"), out var p) ? p : (HistoryPeriod)Math.Clamp(NativeViews.Settings().PanelPeriod, 0, 3);
    private bool WorkHours => Get("work") is { } work ? work == "1" : NativeViews.Settings().PanelWorkHoursOnly;
    private List<string> ShownCharts => Get("charts") is { } saved ? saved.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList() : [.. DefaultCharts];

    protected override void Detached() => _reload.Stop();

    protected override void Started()
    {
        _reload.Start();
        _ = LoadAsync();
    }

    protected override void Stopped() => _reload.Stop();

    public override IEnumerable<Control> MenuItems()
    {
        yield return Ui.Caption("Cifras");
        var figs = GetSet("figs", Figures.Select(f => f.Id));
        foreach (var (id, name) in Figures)
        {
            var on = figs.Contains(id);
            yield return Ui.Check(name, on, () =>
            {
                SetSet("figs", Figures.Select(f => f.Id).Where(f => f == id ? !on : figs.Contains(f)));
                Render();
            });
        }
        yield return Ui.Caption("Gráficas");
        var charts = ShownCharts;
        foreach (var (id, name) in Charts)
        {
            var on = charts.Contains(id);
            yield return Ui.Check(name, on, () =>
            {
                SetSet("charts", on ? charts.Where(c => c != id) : charts.Append(id));
                Render();
            });
        }
        yield return new Separator();
        yield return Ui.Check("Solo el horario laboral", WorkHours, () =>
        {
            Set("work", WorkHours ? "0" : "1");
            _ = LoadAsync();
        });
        yield return Ui.Item("Exportar a CSV…", () => _ = ExportAsync(), _data is { Records.Count: > 0 });
    }

    /// <summary>Lee el histórico del periodo, con el horario si se pide, y repinta.</summary>
    private async Task LoadAsync()
    {
        var period = Period;
        var work = WorkHours ? NativeViews.Settings().WorkHours : null;
        var generation = ++_generation;
        _loading = _data is null || _data.Period != period;
        if (_loading) Render();
        try
        {
            var data = await Task.Run(() =>
            {
                var now = DateTime.Now;
                var from = History.From(period, DateTime.Today);
                var records = History.Load(from);
                if (work is not null) records = History.InSchedule(records, work);
                HistorySummary? before = null;
                if (History.PreviousWindow(period, now) is { } window)
                {
                    var previous = History.Between(History.Load(window.From, window.To), window.From, window.To);
                    if (work is not null) previous = History.InSchedule(previous, work);
                    before = History.Summarize(previous);
                }
                var (_, series) = History.Series(records, period, now, work);
                var (size, usage) = History.Buckets(records, period, now, work);
                var (_, cache) = History.CacheBuckets(records, period, now, work);
                // La actividad por hora de hoy no dice nada: se mira la última semana.
                var week = period == HistoryPeriod.Today ? History.Load(DateTime.Today.AddDays(-6)) : records;
                if (period == HistoryPeriod.Today && work is not null) week = History.InSchedule(week, work);
                return new Snapshot(period, records, History.Summarize(records), before, series.Where(b => b.Start <= now).ToList(), size, usage, cache,
                    History.LoadLimits(from), week);
            });
            if (generation != _generation) return;
            _data = data;
            Refreshed();
        }
        catch (Exception ex)
        {
            JsonStore.LogError("stats-widget", ex);
        }
        finally
        {
            if (generation == _generation) _loading = false;
            Render();
        }
    }

    /// <summary>Exporta a CSV los prompts del periodo, en la carpeta que elija la persona.</summary>
    private async Task ExportAsync()
    {
        if (_data is null || TopLevel.GetTopLevel(this) is not { } top) return;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exportar a CSV",
            SuggestedFileName = $"arkko-{PanelModel.PeriodSlug(_data.Period)}-{DateTime.Today:yyyy-MM-dd}.csv",
            FileTypeChoices = [new FilePickerFileType("CSV (separado por punto y coma)") { Patterns = ["*.csv"] }],
            DefaultExtension = "csv",
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        try
        {
            HistoryCsv.Write(path, _data.Records);
        }
        catch (Exception ex)
        {
            JsonStore.LogError("csv", ex);
        }
    }

    protected override Control Build()
    {
        var dock = new DockPanel();
        var bar = new WrapPanel();
        foreach (var (period, shortName, longName) in Periods)
        {
            var on = period == Period;
            var chip = Ui.Chip(Ui.Text(Size == WidgetSize.S ? shortName : longName, 11.5), on, () =>
            {
                Set("period", period.ToString());
                _ = LoadAsync();
            });
            chip.Margin = new Thickness(0, 0, 5, 4);
            bar.Children.Add(chip);
        }
        if (WorkHours) bar.Children.Add(Ui.Pill("Horario laboral", Ui.Muted));
        bar.Margin = new Thickness(0, 0, 0, 4);
        DockPanel.SetDock(bar, Dock.Top);
        dock.Children.Add(bar);
        if (_data is not { } data || _loading)
        {
            dock.Children.Add(Ui.Empty("Leyendo el histórico…"));
            return dock;
        }

        var figs = ShownFigures();
        if (figs.Count > 0)
        {
            // En S, una fila corta por cifra; desde M, todas en una sola fila que se reparten el ancho.
            var top = Size == WidgetSize.S
                ? Ui.Column(4, figs.Select(f => FigureRow(f.Id, f.Name, data)).ToArray())
                : (Control)Ui.Columns(figs.Count, 6, figs.Select(f => Figure(f.Id, f.Name, data)));
            top.Margin = new Thickness(0, 0, 0, 8);
            DockPanel.SetDock(top, Dock.Top);
            dock.Children.Add(top);
        }

        var across = Size switch { WidgetSize.S => 1, WidgetSize.M => 2, _ => 3 };
        var charts = ShownCharts.Where(id => Charts.Any(c => c.Id == id)).Take(Size == WidgetSize.S ? 1 : across * 2).Select(id => Chart(id, data)).ToList();
        if (charts.Count == 0)
        {
            dock.Children.Add(Ui.Empty("Sin gráficas: se eligen en el menú ··· del recuadro."));
            return dock;
        }
        dock.Children.Add(Mosaic(charts, Math.Min(across, charts.Count)));
        return dock;
    }

    /// <summary>Las gráficas en celdas iguales que llenan el alto; si la última fila no se llena, su última gráfica ocupa lo que
    /// queda, para que no quede hueco.</summary>
    private static Grid Mosaic(List<Control> charts, int across)
    {
        var grid = new Grid();
        var rows = (charts.Count + across - 1) / across;
        for (var c = 0; c < across; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        for (var r = 0; r < rows; r++) grid.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));
        for (var i = 0; i < charts.Count; i++)
        {
            var (row, column) = (i / across, i % across);
            var span = i == charts.Count - 1 ? across - column : 1;
            var chart = charts[i];
            Grid.SetRow(chart, row);
            Grid.SetColumn(chart, column);
            Grid.SetColumnSpan(chart, span);
            chart.Margin = new Thickness(column == 0 ? 0 : 4, row == 0 ? 0 : 4, column + span == across ? 0 : 4, row == rows - 1 ? 0 : 4);
            grid.Children.Add(chart);
        }
        return grid;
    }

    /// <summary>Una cifra clave: su nombre, su valor, su curva del periodo y su cambio frente al tramo anterior.</summary>
    private Control Figure(string id, string name, Snapshot data)
    {
        var (value, series, before, color, now) = Measure(id, data);
        var unusual = Unusual(id, data);
        var label = Ui.Text(name, 11, Ui.Muted);
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        var list = Ui.Column(1, label, Ui.Text(value, 17, bold: true));
        list.Children.Add(new Sparkline { Values = series.ToList(), Stroke = color, Height = 18, Margin = new Thickness(0, 2, 0, 1), ShowEnd = true });
        if (before is { } old)
            list.Children.Add(Ui.Text(Delta(id, now, old), 10.5, unusual is not null ? Ui.Wait : now >= old ? ThemeColors.Of("DataHaiku", "#98C379") : Ui.Faint, bold: unusual is not null));
        DiaryInk.Tip(list, DiaryInk.Card(name, unusual ?? value, rows: unusual is null ? null : [("Ahora", value)]));
        return new Border
        {
            Background = unusual is null ? Ui.CardFill : ThemeColors.Of("DataFiveHour", "#E5C07B", 0.08), BorderBrush = unusual is null ? Ui.CardEdge : Ui.Wait,
            BorderThickness = new Thickness(unusual is null ? 1 : 1.5), CornerRadius = new CornerRadius(7), Padding = new Thickness(8, 6), Child = list,
        };
    }

    /// <summary>Una cifra clave en una fila corta, para S: su nombre, su valor y su curva.</summary>
    private Control FigureRow(string id, string name, Snapshot data)
    {
        var (value, series, before, color, now) = Measure(id, data);
        var unusual = Unusual(id, data);
        var row = new DockPanel();
        var curve = new Sparkline { Values = series.ToList(), Stroke = color, Width = 60, Height = 14, Margin = new Thickness(8, 0, 0, 0), ShowEnd = true };
        DockPanel.SetDock(curve, Dock.Right);
        row.Children.Add(curve);
        var number = Ui.Text(value, 13, bold: true);
        DockPanel.SetDock(number, Dock.Right);
        row.Children.Add(number);
        var label = Ui.Text(name, 11.5, Ui.Muted);
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        label.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(label);
        var line = new Border
        {
            Background = unusual is null ? Ui.CardFill : ThemeColors.Of("DataFiveHour", "#E5C07B", 0.08), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 4),
            Child = row, BorderBrush = Ui.Wait, BorderThickness = new Thickness(unusual is null ? 0 : 1.5),
        };
        if (before is { } old) DiaryInk.Tip(line, DiaryInk.Card(name, unusual ?? value, rows: [("Frente al tramo anterior", Delta(id, now, old))]));
        return line;
    }

    /// <summary>Lo que dice una cifra clave: su valor escrito, su curva del periodo, su valor del tramo anterior, su color y
    /// su valor como número.</summary>
    private static (string Value, IEnumerable<double?> Series, double? Before, IBrush Color, double Now) Measure(string id, Snapshot data)
    {
        var (value, series, before, color) = id switch
        {
            "tokens" => (Format.Tokens(data.Summary.NewTokens), data.Series.Select(b => (double?)b.Summary.NewTokens), (double?)data.Before?.NewTokens,
                ThemeColors.Of("DataOpus", "#F09A76")),
            "prompts" => (data.Summary.Prompts.ToString(Es), data.Series.Select(b => (double?)b.Summary.Prompts), data.Before?.Prompts,
                ThemeColors.Of("DataSonnet", "#61AFEF")),
            "sessions" => (data.Summary.Sessions.ToString(Es), data.Series.Select(b => (double?)b.Summary.Sessions), data.Before?.Sessions,
                ThemeColors.Of("DataFable", "#C678DD")),
            "time" => (Format.Duration(data.Summary.Working), data.Series.Select(b => (double?)b.Summary.Working.TotalMinutes), data.Before?.Working.TotalMinutes,
                ThemeColors.Of("DataHaiku", "#98C379")),
            _ => (data.Summary.CachePct is { } pct ? $"{pct:0} %" : "—", data.Series.Select(b => b.Summary.CachePct), data.Before?.CachePct,
                ThemeColors.Of("DataCache", "#56B6C2")),
        };
        var now = id switch
        {
            "tokens" => (double)data.Summary.NewTokens,
            "prompts" => data.Summary.Prompts,
            "sessions" => data.Summary.Sessions,
            "time" => data.Summary.Working.TotalMinutes,
            _ => data.Summary.CachePct ?? 0,
        };
        return (value, series, before, color, now);
    }

    /// <summary>El cambio de una cifra frente al tramo anterior: en puntos la caché, en tanto por ciento lo demás.</summary>
    private static string Delta(string id, double now, double before)
    {
        if (id == "cache")
        {
            var points = Math.Round(Math.Abs(now - before));
            return points == 0 ? "= que antes" : $"{(now > before ? "▲" : "▼")} {points:0} {(points == 1 ? "punto" : "puntos")}";
        }
        return Change(now, before) ?? "= que antes";
    }

    /// <summary>Cuánto ha cambiado un valor frente al de antes, o nada si es el mismo.</summary>
    /// <remarks>
    /// Con un antes casi a cero el porcentaje sale enorme y no dice nada: por encima de cien veces se dice «nuevo», y por
    /// encima de diez, las veces («×12»).
    /// </remarks>
    private static string? Change(double now, double before)
    {
        if (before <= 0 || now / before >= 100) return now > 0 ? "▲ nuevo" : null;
        if (now / before >= 10) return $"▲ ×{now / before:0}";
        var change = (now - before) / before * 100;
        return Math.Abs(change) < 0.5 ? null : $"{(change > 0 ? "▲" : "▼")} {Math.Abs(change):0} %";
    }

    /// <summary>Una baldosa del mosaico: el título de la gráfica y la gráfica, que llena la baldosa.</summary>
    private Control Chart(string id, Snapshot data)
    {
        var (_, name) = Charts.First(c => c.Id == id);
        var head = Ui.Row(6, Ui.Label(name));
        head.Margin = new Thickness(0, 0, 0, 6);
        Control body = id switch
        {
            "history" => HistoryChart(data),
            "projects" => ProjectsChart(data),
            "limits" => LimitsChart(data),
            "heat" => HeatChart(data),
            "cache" => CacheChart(data),
            "costly" => CostlyChart(data),
            "models" => ModelsChart(data),
            _ => PerPromptChart(data),
        };
        var dock = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        dock.Children.Add(head);
        dock.Children.Add(body);
        return new Border
        {
            Background = Ui.CardFill, BorderBrush = Ui.CardEdge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8), MinHeight = 130, Child = dock,
        };
    }

    /// <summary>La etiqueta de una barra del histórico, según su tamaño: la hora, el día o el inicio de la semana.</summary>
    private static string BucketLabel(DateTime start, BucketSize size) => size switch
    {
        BucketSize.Hour => start.ToString("HH", Es),
        BucketSize.Day => start.ToString("d", Es),
        _ => start.ToString("d MMM", Es).Replace(".", ""),
    };

    /// <summary>El histórico: una barra por tramo, con los tokens nuevos de cada modelo apilados.</summary>
    private static Control HistoryChart(Snapshot data)
    {
        if (data.Usage.Count == 0 || data.Usage.All(b => b.Total == 0)) return Ui.Empty("Sin prompts en el periodo.");
        var max = Math.Max(1, data.Usage.Max(b => b.Total));
        var bars = new Grid();
        var every = Math.Max(1, data.Usage.Count / 8);
        for (var i = 0; i < data.Usage.Count; i++)
        {
            var bucket = data.Usage[i];
            bars.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            var segments = bucket.ByModel.OrderBy(m => m.Key).Select(m => new BarSegment(m.Value, ModelColors.For(m.Key))).ToList();
            var tip = new BarTip(BucketLabel(bucket.Start, data.Size), bucket.ByModel.Select(m => new TipRow(Format.ModelName(m.Key), Format.Tokens(m.Value), ModelColors.For(m.Key))).ToList(),
                Format.Tokens(bucket.Total));
            var column = new DockPanel { Margin = new Thickness(1, 0) };
            var label = Ui.Text(i % every == 0 ? BucketLabel(bucket.Start, data.Size) : "", 9.5, Ui.Faint, mono: true);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            DockPanel.SetDock(label, Dock.Bottom);
            column.Children.Add(label);
            var bar = new StackedBar { Bar = new BarModel(tip.Title, segments, max, tip) };
            DiaryInk.Tip(bar, DiaryInk.Card(tip.Title, $"{tip.Total} tokens nuevos", rows: tip.Rows.Select(r => (r.Name, r.Value))));
            column.Children.Add(bar);
            Grid.SetColumn(column, i);
            bars.Children.Add(column);
        }
        return bars;
    }

    /// <summary>Por proyecto: los que más gastan, cada uno con su barra a escala del primero.</summary>
    private static Control ProjectsChart(Snapshot data)
    {
        var projects = History.ByProject(data.Records).Take(5).ToList();
        if (projects.Count == 0) return Ui.Empty("Sin prompts en el periodo.");
        var list = new StackPanel { Spacing = 4 };
        var top = Math.Max(1, projects.Max(p => p.NewTokens));
        foreach (var p in projects)
        {
            var row = new Grid();
            row.Children.Add(new FractionFill { Fraction = (double)p.NewTokens / top, Fill = RepoColors.Of(p.Project, 0.18), Radius = 5 });
            var text = new DockPanel { Margin = new Thickness(7, 3) };
            var right = Ui.Text($"{Format.Tokens(p.NewTokens)} · {p.Pct:0} %", 11, Ui.Muted);
            DockPanel.SetDock(right, Dock.Right);
            text.Children.Add(right);
            text.Children.Add(Ui.Row(6, Ui.Dot(RepoColors.Of(p.Project)), Ui.Text(p.Project, 11.5, bold: true)));
            row.Children.Add(text);
            DiaryInk.Tip(row, DiaryInk.Card(p.Project, $"{p.Prompts} prompts · {p.Sessions} sesiones", rows: [("Tokens nuevos", Format.Tokens(p.NewTokens)), ("Del periodo", $"{p.Pct:0} %")]));
            list.Children.Add(row);
        }
        return list;
    }

    /// <summary>La evolución de los límites: una curva por límite a lo largo del periodo, con su último valor.</summary>
    private static Control LimitsChart(Snapshot data)
    {
        var series = data.Limits.SelectMany(s => s.Limits.Select(l => (s.WhenUtc, l.Label, l.UsedPct))).GroupBy(x => x.Label).ToList();
        if (series.Count == 0) return Ui.Empty("Sin datos de los límites en el periodo.");
        var list = new StackPanel { Spacing = 6 };
        foreach (var limit in series)
        {
            var points = limit.OrderBy(x => x.WhenUtc).Select(x => (double?)x.UsedPct).ToList();
            var color = limit.Key.StartsWith('5') ? ThemeColors.Of("DataFiveHour", "#E5C07B") : ThemeColors.Of("DataSevenDay", "#56B6C2");
            var head = Ui.Row(6, Ui.Dot(color), Ui.Text(limit.Key, 11.5, bold: true), Ui.Text($"{points[^1]:0} % ahora", 11, Ui.Muted));
            list.Children.Add(head);
            list.Children.Add(new Sparkline { Values = points, Stroke = color, Height = 28 });
        }
        return list;
    }

    /// <summary>La actividad por hora: un mapa de calor de las horas de cada día de la semana, más intenso con más prompts.</summary>
    private static Control HeatChart(Snapshot data)
    {
        var counts = new int[7, 24];
        foreach (var r in data.Week)
        {
            var local = r.WhenUtc.ToLocalTime();
            counts[((int)local.DayOfWeek + 6) % 7, local.Hour]++;
        }
        var used = Enumerable.Range(0, 24).Where(h => Enumerable.Range(0, 7).Any(d => counts[d, h] > 0)).ToList();
        if (used.Count == 0) return Ui.Empty("Sin prompts en la semana.");
        var (first, last) = (used.Min(), used.Max());
        var max = Math.Max(1, Enumerable.Range(0, 7).SelectMany(d => Enumerable.Range(first, last - first + 1).Select(h => counts[d, h])).Max());
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("20," + string.Join(",", Enumerable.Repeat("*", last - first + 1))) };
        for (var d = 0; d <= 7; d++) grid.RowDefinitions.Add(new RowDefinition(d == 7 ? GridLength.Auto : new GridLength(1, GridUnitType.Star)));
        var accent = ThemeColors.Of("DataOpus", "#F09A76") is ISolidColorBrush { Color: var c } ? c : Colors.Orange;
        for (var d = 0; d < 7; d++)
        {
            var label = Ui.Text(WorkSchedule.Letter(WorkSchedule.Order[d]), 9.5, Ui.Faint, mono: true);
            Grid.SetRow(label, d);
            grid.Children.Add(label);
            for (var h = first; h <= last; h++)
            {
                var n = counts[d, h];
                var cell = new Border
                {
                    CornerRadius = new CornerRadius(2), Margin = new Thickness(1), MinHeight = 6,
                    Background = n == 0 ? Ui.Track : new SolidColorBrush(accent, 0.15 + 0.85 * n / max),
                };
                DiaryInk.Tip(cell, DiaryInk.Card($"{WorkSchedule.Letter(WorkSchedule.Order[d])} · {h}:00", n == 1 ? "1 prompt" : $"{n} prompts"));
                Grid.SetRow(cell, d);
                Grid.SetColumn(cell, h - first + 1);
                grid.Children.Add(cell);
            }
        }
        for (var h = first; h <= last; h += Math.Max(1, (last - first + 1) / 5))
        {
            var label = Ui.Text($"{h}h", 9.5, Ui.Faint, mono: true);
            Grid.SetRow(label, 7);
            Grid.SetColumn(label, h - first + 1);
            Grid.SetColumnSpan(label, 3);
            grid.Children.Add(label);
        }
        var dock = new DockPanel();
        var note = Ui.Text(data.Period == HistoryPeriod.Today ? "La última semana" : "En el periodo", 10.5, Ui.Faint);
        DockPanel.SetDock(note, Dock.Bottom);
        dock.Children.Add(note);
        dock.Children.Add(grid);
        return dock;
    }

    /// <summary>La caché: qué parte de la entrada se leyó de caché en cada tramo.</summary>
    private static Control CacheChart(Snapshot data)
    {
        var values = data.Cache.Select(b => b.Pct).ToList();
        if (values.All(v => v is null)) return Ui.Empty("Sin uso de caché en el periodo.");
        var dock = new DockPanel();
        var now = Ui.Text(data.Summary.CachePct is { } pct ? $"{pct:0} % de la entrada, de caché" : "", 11, Ui.Muted);
        DockPanel.SetDock(now, Dock.Top);
        dock.Children.Add(now);
        dock.Children.Add(new Sparkline { Values = values, Stroke = ThemeColors.Of("DataCache", "#56B6C2"), Margin = new Thickness(0, 6, 0, 0) });
        return dock;
    }

    /// <summary>Los prompts más caros del periodo: cuándo, de qué proyecto, su principio y lo que costaron.</summary>
    private static Control CostlyChart(Snapshot data)
    {
        var costly = History.Costliest(data.Records, 5);
        if (costly.Count == 0) return Ui.Empty("Sin prompts en el periodo.");
        var top = Math.Max(1, costly.Max(r => PromptSummary.NewTokens(r.Usage.Totals())));
        var list = new StackPanel { Spacing = 4 };
        foreach (var r in costly)
        {
            var tokens = PromptSummary.NewTokens(r.Usage.Totals());
            var row = new Grid { Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
            row.Children.Add(new FractionFill { Fraction = (double)tokens / top, Fill = ThemeColors.Of("DataOpus", "#F09A76", 0.16), Radius = 5, FromRight = true });
            var text = new DockPanel { Margin = new Thickness(7, 3) };
            var right = Ui.Text(Format.Tokens(tokens), 11, Ui.Fg, bold: true);
            DockPanel.SetDock(right, Dock.Right);
            text.Children.Add(right);
            var when = Ui.Text(r.WhenUtc.ToLocalTime().ToString("HH:mm", Es), 10.5, Ui.Faint, mono: true);
            when.Margin = new Thickness(0, 0, 7, 0);
            DockPanel.SetDock(when, Dock.Left);
            text.Children.Add(when);
            text.Children.Add(Ui.Text(History.PromptPreview(r.Prompt) ?? "(sin texto)", 11.5));
            row.Children.Add(text);
            DiaryInk.Tip(row, DiaryInk.Card(History.PromptPreview(r.Prompt) ?? "Prompt", $"{History.ProjectOf(r)} · {r.WhenUtc.ToLocalTime():d MMM, HH:mm}",
                footer: "Pulsa para abrir su sesión."));
            var session = r.SessionId;
            row.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton == Avalonia.Input.MouseButton.Left && session.Length > 0) SessionActions.RequestSession(session);
            };
            list.Children.Add(row);
        }
        return list;
    }

    /// <summary>El reparto por modelo: la barra de los tokens nuevos del periodo entre los modelos, con lo de cada uno debajo.</summary>
    private static Control ModelsChart(Snapshot data)
    {
        var byModel = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in data.Records)
            foreach (var (model, tokens) in r.Usage.ByModel)
                byModel[model] = byModel.GetValueOrDefault(model) + PromptSummary.NewTokens(tokens);
        var total = byModel.Values.Sum();
        if (total == 0) return Ui.Empty("Sin prompts en el periodo.");
        var ordered = byModel.OrderByDescending(m => m.Value).ToList();
        var list = new StackPanel { Spacing = 6 };
        list.Children.Add(Ui.Text($"{Format.Tokens(total)} tokens nuevos", 13, bold: true));
        list.Children.Add(new ShareBar { Slices = ordered.Select(m => new BarSegment(m.Value, ModelColors.For(m.Key))).ToList(), Height = 10 });
        foreach (var (model, tokens) in ordered.Take(4))
            list.Children.Add(Ui.Row(6, Ui.Dot(ModelColors.For(model)), Ui.Text(Format.ModelName(model), 11.5), Ui.Text($"{Format.Tokens(tokens)} · {100.0 * tokens / total:0} %", 11, Ui.Muted)));
        return list;
    }

    /// <summary>El coste por prompt: los tokens nuevos de media por prompt en cada tramo, y la media del periodo frente al anterior.</summary>
    private static Control PerPromptChart(Snapshot data)
    {
        if (data.Summary.Prompts == 0) return Ui.Empty("Sin prompts en el periodo.");
        var average = (double)data.Summary.NewTokens / data.Summary.Prompts;
        var line = $"{Format.Tokens((long)average)} de media";
        if (data.Before is { Prompts: > 0 } before)
        {
            var old = (double)before.NewTokens / before.Prompts;
            line += Change(average, old) is { } change ? $" · {change} frente al tramo anterior" : " · igual que antes";
        }
        var dock = new DockPanel();
        var head = Ui.Text(line, 11, Ui.Muted);
        DockPanel.SetDock(head, Dock.Top);
        dock.Children.Add(head);
        var values = data.Series.Select(b => b.Summary.Prompts > 0 ? (double?)b.Summary.NewTokens / b.Summary.Prompts : null).ToList();
        dock.Children.Add(new Sparkline { Values = values, Stroke = ThemeColors.Of("DataOpus", "#F09A76"), Margin = new Thickness(0, 6, 0, 0) });
        return dock;
    }
}
