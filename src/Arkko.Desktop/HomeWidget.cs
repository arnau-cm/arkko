using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Arkko.Desktop;

/// <summary>El widget de Inicio: cada límite en su tarjeta, con el uso en grande y la barra de su ventana de tiempo (lo
/// gastado, la hora de ahora y lo que se calcula al renovarse), el consumo de hoy plegado en una línea y las sesiones en filas
/// agrupadas por estado, de las que te esperan a las que reposan.</summary>
/// <remarks>
/// Usa el modelo de la ventana flotante (<see cref="MainViewModel"/>), el mismo que el Inicio del panel. No hay ficha de
/// aviso aparte: lo que te espera se ve en su grupo, cada fila con el color y el icono de lo que pide, y si el grupo queda
/// fuera de la vista, una pastilla flotante lleva a él; un límite que aprieta se pone en rojo en su propia tarjeta.
/// </remarks>
internal sealed class HomeWidget : CorkWidget
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    // A partir de qué uso un límite aprieta y su tarjeta se pone en rojo; también en Avisos.
    internal const double Pressing = 80;

    // Lo que tarda una espera en pasar a roja; la raya de debajo de la fila se llena en ese tiempo.
    private static readonly TimeSpan WaitFull = TimeSpan.FromMinutes(15);

    // Los iconos de lo que pide una sesión que espera: un permiso, una pregunta, un plan o revisar lo terminado.
    private const string LockGlyph = "";
    private const string AskGlyph = "";
    private const string PlanGlyph = "";
    private const string ReviewGlyph = "";

    private readonly MainViewModel? _model;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(30) };
    private bool _resting;

    /// <summary>Crea el widget sobre el modelo de la ventana flotante.</summary>
    public HomeWidget(object? model)
    {
        _model = model as MainViewModel;
        _clock.Tick += (_, _) => Render();
    }

    public override PanelSection Section => PanelSection.Home;

    protected override void Attached()
    {
        if (_model is not null) _model.PropertyChanged += OnModelChanged;
    }

    protected override void Detached()
    {
        if (_model is not null) _model.PropertyChanged -= OnModelChanged;
        _clock.Stop();
    }

    protected override void Started() => _clock.Start();

    protected override void Stopped() => _clock.Stop();

    /// <summary>Repinta cuando cambian los límites, el consumo o las sesiones.</summary>
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Limits) or nameof(MainViewModel.Sessions) or nameof(MainViewModel.Models) or nameof(MainViewModel.ModelsTotal)
            or nameof(MainViewModel.LimitsNote) or nameof(MainViewModel.HookProblem))
        {
            Refreshed();
            Render();
        }
    }

    /// <summary>De lo más grave a lo menos: rojo si fallan los hooks, si un límite aprieta o si algo lleva rato esperando,
    /// ámbar si una sesión pide algo, verde si solo hay que revisar; nada si no te espera nadie.</summary>
    public override IBrush? Mood
    {
        get
        {
            if (_model is null) return null;
            var sessions = _model.Sessions;
            if (_model.HookProblemVisible || _model.Limits.Any(l => l.Pct >= Pressing) || sessions.Any(s => s.WaitLevel == 2 || s.ReviewOverdue))
                return Brushes_.High;
            if (sessions.Any(s => s.Waiting)) return Brushes_.Waiting;
            return sessions.Any(s => s.NeedsReview) ? Brushes_.Done : null;
        }
    }

    public override string? Headline
    {
        get
        {
            if (_model is null) return null;
            if (_model.HookProblemVisible) return "hooks caídos";
            var waiting = _model.Sessions.Count(s => s.BoardColumn == 2);
            if (waiting > 0) return waiting == 1 ? "1 te espera" : $"{waiting} te esperan";
            var all = _model.Sessions.Count;
            return all == 1 ? "1 sesión" : $"{all} sesiones";
        }
    }

    public override IEnumerable<Control> MenuItems()
    {
        var open = Get("models") == "1";
        yield return Ui.Check("Hoy por modelo, desplegado", open, () =>
        {
            Set("models", open ? null : "1");
            Render();
        });
    }

    protected override Control Build()
    {
        if (_model is null) return Ui.Empty("Inicio no está disponible en esta ventana.");
        var list = new StackPanel { Spacing = 8 };
        if (_model.HookProblem is { } problem) list.Children.Add(HookBanner(problem));

        var limits = _model.Limits.Select(Window).ToList();
        if (limits.Count == 0) list.Children.Add(Ui.Text(_model.LimitsNote, 11, Ui.Faint, wrap: true));
        else if (Size != WidgetSize.S) list.Children.Add(Ui.Columns(limits.Count, 8, limits));
        else foreach (var limit in limits) list.Children.Add(limit);
        if (limits.Count > 0 && _model.LimitsNoteVisible && _model.LimitsNote.StartsWith("Último", StringComparison.Ordinal))
            list.Children.Add(Ui.Text(_model.LimitsNote, 10.5, Ui.Faint, wrap: true));

        if (_model.ModelsSummaryVisible) list.Children.Add(Usage());
        list.Children.Add(Sessions());
        return Scroll("home", list);
    }

    /// <summary>El aviso de que fallan los hooks de Arkko, arriba del todo: mientras falle, lo de debajo puede estar viejo.</summary>
    private static Control HookBanner(string problem)
    {
        var text = Ui.Text(problem, 11.5, wrap: true);
        var icon = new HookIcon { Margin = new Thickness(0, 2, 8, 0), VerticalAlignment = VerticalAlignment.Top };
        DockPanel.SetDock(icon, Dock.Left);
        return new Border
        {
            Background = Soft(Brushes_.High, 0.14), BorderBrush = Brushes_.High, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 7),
            Child = new DockPanel { Children = { icon, text } },
        };
    }

    /// <summary>El color dado, más transparente: para el fondo de lo que lleva ese color.</summary>
    private static IBrush Soft(IBrush brush, double opacity) =>
        brush is ISolidColorBrush solid ? new SolidColorBrush(solid.Color, opacity) : Ui.Track;

    /// <summary>Un límite en su tarjeta: el uso en grande con cuándo se renueva y la previsión en una pastilla de su color; debajo,
    /// la barra de su ventana de tiempo, con lo gastado, lo que se calcula al renovarse (rayado) y la marca de ahora, y las horas.</summary>
    private Control Window(LimitRow limit)
    {
        var span = limit.Label.StartsWith('5') ? TimeSpan.FromHours(5) : TimeSpan.FromDays(7);
        var pressing = limit.Pct >= Pressing;
        var top = new DockPanel();
        var lead = Ui.Row(6, Ui.Text(limit.PctText, 24, pressing ? Ui.Danger : limit.Brush, bold: true));
        var of = Ui.Text($"de {limit.Label}", 11.5, Ui.Muted);
        of.VerticalAlignment = VerticalAlignment.Bottom;
        of.Margin = new Thickness(0, 0, 0, 3);
        lead.Children.Add(of);
        DockPanel.SetDock(lead, Dock.Left);
        top.Children.Add(lead);
        var renews = Ui.Text(limit.ResetText, 11, Ui.Muted);
        renews.HorizontalAlignment = HorizontalAlignment.Right;
        renews.VerticalAlignment = VerticalAlignment.Center;
        renews.TextTrimming = TextTrimming.CharacterEllipsis;
        renews.Margin = new Thickness(8, 0, 0, 0);
        top.Children.Add(renews);
        // La previsión va en su propia línea: al lado de la cifra no cabe en una tarjeta estrecha.
        var forecast = Ui.Pill(limit.ForecastText, limit.ForecastBrush, Soft(limit.ForecastBrush, 0.14));
        forecast.HorizontalAlignment = HorizontalAlignment.Left;
        forecast.IsVisible = limit.ForecastText.Length > 0;

        var track = new Grid { Height = 8, Margin = new Thickness(0, 4, 0, 0) };
        track.Children.Add(new Border { Background = Ui.Track, CornerRadius = new CornerRadius(4) });
        var used = Math.Clamp(limit.Pct / 100, 0, 1);
        var projected = Math.Clamp(limit.Projected / 100, used, 1);
        track.Children.Add(Fill(projected, Ui.Hatch(limit.GhostBrush)));
        track.Children.Add(Fill(used, limit.Brush));
        double? now = null;
        if (limit.ResetsAtUtc is { } reset)
        {
            var start = reset - span;
            now = Math.Clamp((DateTime.UtcNow - start) / span, 0, 1);
            var marker = new Rectangle { Width = 2, Fill = Ui.Fg, Margin = new Thickness(0, -3) };
            var holder = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(Math.Max(0.0001, now.Value), GridUnitType.Star), new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(Math.Max(0.0001, 1 - now.Value), GridUnitType.Star),
                },
            };
            Grid.SetColumn(marker, 1);
            holder.Children.Add(marker);
            track.Children.Add(holder);
        }

        var ticks = new Grid { Height = 14 };
        if (limit.ResetsAtUtc is { } at && now is { } where)
        {
            var start = (at - span).ToLocalTime();
            var end = at.ToLocalTime();
            var format = span.TotalDays >= 1 ? "ddd d" : "HH:mm";
            var last = Ui.Text(end.ToString(format, Es).Replace(".", ""), 9.5, Ui.Faint, mono: true);
            last.HorizontalAlignment = HorizontalAlignment.Right;
            ticks.Children.Add(last);
            var first = Ui.Text(start.ToString(format, Es).Replace(".", ""), 9.5, Ui.Faint, mono: true);
            first.HorizontalAlignment = HorizontalAlignment.Left;
            ticks.Children.Add(first);
            // «Ahora» va debajo de su raya, sin salirse por los lados ni pisar las horas de los extremos.
            var middle = Ui.Text($"ahora {DateTime.Now:HH:mm}", 9.5, Ui.Muted, mono: true);
            var place = new Canvas();
            place.Children.Add(middle);
            place.SizeChanged += (_, _) =>
            {
                middle.Measure(Avalonia.Size.Infinity);
                var width = place.Bounds.Width;
                var label = middle.DesiredSize.Width;
                var left = Math.Clamp(where * width - label / 2, 0, Math.Max(0, width - label));
                Canvas.SetLeft(middle, left);
                first.IsVisible = left > 44;
                last.IsVisible = left + label < width - 44;
            };
            ticks.Children.Add(place);
        }
        var window = new Border
        {
            Background = pressing ? Soft(Ui.Danger, 0.08) : Ui.CardFill, BorderBrush = pressing ? Ui.Danger : Ui.CardEdge,
            BorderThickness = new Thickness(pressing ? 1.5 : 1), CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10, 8, 10, 6), Child = Ui.Column(3, top, forecast, track, ticks),
        };
        var left = limit.ResetsAtUtc is { } until ? $"Te queda el {100 - limit.Pct:0} % hasta las {until.ToLocalTime():HH:mm}." : $"Te queda el {100 - limit.Pct:0} %.";
        DiaryInk.Tip(window, DiaryInk.Card(limit.TipTitle, pressing ? left : null, rows: [("Usado", limit.PctText), ("Se renueva", limit.ResetText), ("Previsión", limit.ForecastText)],
            footer: "La barra va del inicio de la ventana a su renovación: lo lleno es lo gastado, lo rayado lo que se calcula hasta renovarse y la raya, ahora."));
        return window;
    }

    /// <summary>Un tramo de la barra de un límite que llena una parte de su ancho desde la izquierda.</summary>
    private static Control Fill(double fraction, IBrush brush)
    {
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(Math.Max(0.0001, fraction), GridUnitType.Star), new ColumnDefinition(Math.Max(0.0001, 1 - fraction), GridUnitType.Star) },
        };
        grid.Children.Add(new Border { Background = brush, CornerRadius = new CornerRadius(4), IsVisible = fraction > 0 });
        return grid;
    }

    /// <summary>El consumo de hoy, plegado en una línea con el total y el reparto; desplegado, una fila por modelo.</summary>
    private Control Usage()
    {
        var open = Get("models") == "1";
        var head = new DockPanel();
        var share = new ShareBar { Slices = _model!.ModelShares, Height = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        var lead = Ui.Row(6, Ui.Icon(open ? "" : "", 9), Ui.Text("Hoy", 11.5, Ui.Muted), Ui.Text($"{_model.ModelsTotal} tokens", 12, bold: true));
        DockPanel.SetDock(lead, Dock.Left);
        head.Children.Add(lead);
        head.Children.Add(share);
        var list = Ui.Column(4, head);
        if (open)
            foreach (var row in _model.Models)
            {
                var line = new DockPanel();
                var total = Ui.Text(row.Total, 11.5, bold: true);
                DockPanel.SetDock(total, Dock.Right);
                line.Children.Add(total);
                line.Children.Add(Ui.Row(6, Ui.Dot(row.Brush), Ui.Text(row.Name, 11.5), Ui.Text(row.Detail, 10.5, Ui.Faint)));
                list.Children.Add(line);
            }
        var card = Ui.Card(list, () =>
        {
            Set("models", open ? null : "1");
            Render();
        });
        DiaryInk.Tip(card, DiaryInk.Card("Hoy por modelo", _model.ModelsDetail, footer: open ? "Pulsa para plegarlo." : "Pulsa para ver cada modelo."));
        return card;
    }

    /// <summary>Las sesiones en filas alineadas, agrupadas por estado: las que te esperan, las que trabajan y las que reposan,
    /// estas recogidas a partir de la cuarta. Cada grupo se pliega. En L, un grupo por columna.</summary>
    /// <remarks>Si el grupo de las que te esperan queda fuera de la vista, una pastilla flotante lleva a él.</remarks>
    private Control Sessions()
    {
        var rows = _model!.Sessions.OrderByDescending(s => s.BoardColumn).ThenBy(Urgency).ThenByDescending(s => s.SinceUtc).ToList();
        if (rows.Count == 0) return Ui.Text("Sin sesiones abiertas", 12, Ui.Faint);
        var groups = new[] { (2, "Te esperan"), (1, "Trabajando"), (0, "En reposo") }
            .Select(g => (g.Item2, Rows: rows.Where(r => r.BoardColumn == g.Item1).ToList(), Column: g.Item1))
            .Where(g => g.Rows.Count > 0 || Size == WidgetSize.L)
            .Select(g =>
            {
                var mood = g.Column == 2 && g.Rows.Count > 0 ? ColorOf(g.Rows[0]) : null;
                var label = Ui.Label($"{g.Item1} · {g.Rows.Count}");
                if (mood is not null) label.Foreground = mood;
                var head = FoldHead(g.Item1, label);
                var group = Ui.Column(4, head);
                if (Folded(g.Item1))
                {
                    foreach (var row in g.Rows) Note("sessions", row.SessionId, Signature(row), g.Item1);
                    return (Control)group;
                }
                var shown = g.Column == 0 && !_resting && Size != WidgetSize.L ? g.Rows.Take(3).ToList() : g.Rows;
                foreach (var row in shown) group.Children.Add(Watch(SessionLine(row), "sessions", row.SessionId, Signature(row), ColorOf(row), g.Item1));
                // Lo que lleva a ellas es la primera fila: con solo la cabecera a la vista, aún no se ve quién espera.
                if (mood is not null && group.Children.Count > 1)
                    Beacon(group.Children[1], g.Rows.Count == 1 ? "1 te espera" : $"{g.Rows.Count} te esperan", mood);
                foreach (var row in g.Rows.Skip(shown.Count)) Note("sessions", row.SessionId, Signature(row));
                if (shown.Count < g.Rows.Count)
                    group.Children.Add(Ui.Link($"+ {g.Rows.Count - shown.Count} más", () =>
                    {
                        _resting = true;
                        Render();
                    }));
                return (Control)group;
            }).ToList();
        return Size == WidgetSize.L ? Ui.Columns(3, 10, groups) : Ui.Column(8, groups.ToArray());
    }

    /// <summary>Lo urgente que es una sesión dentro de su grupo, de menos a más tarde: primero la que lleva rato esperando,
    /// después la que pide un permiso o una respuesta, después la que lleva rato sin revisar y al final lo recién terminado.</summary>
    private static int Urgency(SessionRow row) => row.Waiting ? (row.WaitLevel == 2 ? 0 : 1) : row.ReviewOverdue ? 2 : 3;

    /// <summary>Lo que, si cambia en una sesión, cuenta como novedad: su grupo y su estado, no el tiempo que lleva.</summary>
    private static string Signature(SessionRow row) => $"{row.BoardColumn}:{row.Status}:{row.HookErrorVisible}";

    /// <summary>El color de una sesión según lo que te pide: ámbar si espera un permiso o una respuesta, verde si hay que
    /// revisarla, rojo si cualquiera de las dos lleva rato; azul si trabaja y, si reposa, el de su repositorio apagado.</summary>
    private static IBrush ColorOf(SessionRow row)
    {
        if (row.Waiting) return row.WaitLevel == 2 ? Brushes_.High : Brushes_.Waiting;
        if (row.NeedsReview) return row.ReviewOverdue ? Brushes_.High : Brushes_.Done;
        return row.Working ? Brushes_.Working : RepoColors.Of(row.Title, 0.55);
    }

    /// <summary>El icono de lo que pide una sesión que te espera: un candado, una interrogación, una lista o una marca.</summary>
    private static string GlyphOf(SessionRow row) => row.Waiting
        ? row.Status switch
        {
            "Permiso" => LockGlyph,
            "Plan" => PlanGlyph,
            _ => AskGlyph,
        }
        : ReviewGlyph;

    /// <summary>La fila de una sesión, según su grupo: si te espera, con el fondo de su color, el icono de lo que pide y, si
    /// bloquea al agente, una raya que se llena con la espera; si trabaja, con su raya azul; si reposa, apagada, con la raya
    /// de su repositorio y solo desde cuándo. Al pulsarla, la da por revisada y la abre.</summary>
    private Control SessionLine(SessionRow row)
    {
        var waits = row.BoardColumn == 2;
        var rests = row.BoardColumn == 0;
        var color = ColorOf(row);
        var status = row.NeedsReview && !row.Waiting ? "Por revisar" : row.Status;
        var line = new DockPanel();
        var state = rests
            ? Ui.Row(5, Ui.Text(row.Since, 11, Ui.Faint))
            : Ui.Row(5, Ui.Text(status, 11, color, bold: waits), Ui.Text(row.Since, 11, row.SinceBrush ?? Ui.Faint));
        state.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(state, Dock.Right);
        line.Children.Add(state);
        if (waits)
        {
            var icon = new Border
            {
                Width = 17, Height = 17, CornerRadius = new CornerRadius(4), Background = Soft(color, 0.22), Margin = new Thickness(0, 0, 7, 0),
                Child = new TextBlock
                {
                    Text = GlyphOf(row), FontSize = 10, Foreground = color, HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center, Classes = { "icon", "ownColor" },
                },
            };
            DockPanel.SetDock(icon, Dock.Left);
            line.Children.Add(icon);
        }
        if (row.HookErrorVisible)
        {
            var broken = new HookIcon { Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(broken, Dock.Left);
            line.Children.Add(broken);
        }
        var name = Ui.Text(row.Title, 12, rests ? Ui.Muted : null, bold: !rests);
        name.Margin = new Thickness(0, 0, 6, 0);
        DockPanel.SetDock(name, Dock.Left);
        line.Children.Add(name);
        var tree = Ui.Text(row.Worktree ?? "", 11.5, rests ? Ui.Faint : Ui.Muted);
        tree.TextTrimming = TextTrimming.CharacterEllipsis;
        line.Children.Add(tree);

        Control body = line;
        if (row.Waiting)
        {
            var waited = Math.Clamp((DateTime.UtcNow - row.SinceUtc) / WaitFull, 0.02, 1);
            var bar = new Grid { Height = 2, Margin = new Thickness(0, 5, 0, 0) };
            bar.Children.Add(new Border { Background = Ui.Track, CornerRadius = new CornerRadius(1) });
            bar.Children.Add(Fill(waited, new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops = { new GradientStop(SolidOf(Brushes_.Waiting), 0), new GradientStop(SolidOf(Brushes_.High), 1) },
            }));
            body = Ui.Column(0, line, bar);
        }
        var card = Ui.Card(body, () =>
        {
            SessionActions.Card.Execute(row);
            SessionActions.ViewSession.Execute(row);
        }, stripe: color, fill: waits ? Soft(color, 0.12) : rests ? Brushes.Transparent : null);
        card.Padding = new Thickness(9, 5, 9, 5);
        var title = row.Worktree is { } worktree ? $"{row.Title} · {worktree}" : row.Title;
        var rows = new List<(string, string)>();
        if (row.TaskKey is { } key) rows.Add(("Tarea", row.TaskTitle is { } t ? $"{key} · {t}" : key));
        if (row.HookError is { } hooks) rows.Add(("Hooks", $"Falló el último: {hooks}"));
        DiaryInk.Tip(card, DiaryInk.Card(title, $"{status} · {row.Since}", rows: rows.Count > 0 ? rows : null,
            footer: row.Recap is { } recap ? recap : "Pulsa para abrirla en Sesiones."));
        return card;
    }

    /// <summary>El color de un pincel liso, para los degradados; gris si no lo es.</summary>
    private static Color SolidOf(IBrush brush) => brush is ISolidColorBrush solid ? solid.Color : Colors.Gray;
}
