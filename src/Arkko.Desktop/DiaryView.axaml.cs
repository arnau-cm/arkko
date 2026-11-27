using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Sección Diario del panel: el trabajo con agentes como una agenda, con un bloque por sesión en su hora.</summary>
/// <remarks>
/// Los días se calculan fuera del hilo de la ventana (<see cref="DiaryStore"/>), y un día ya cerrado se lee de su
/// resumen guardado. Con la sección a la vista, el día de hoy se recalcula cada pocos minutos. El calendario por
/// horas, el mes y la agenda, las fichas y el calendario desplegable van cada uno en su parte de la clase.
/// </remarks>
public partial class DiaryView : UserControl
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
    private static readonly TimeSpan TodayEvery = TimeSpan.FromMinutes(2);

    // Días que se leen de una vez antes de pintar lo que hay.
    private const int LoadBatch = 4;

    private readonly DiaryModel _model = new();
    private readonly DispatcherTimer _today = new() { Interval = TodayEvery };
    private int _generation;

    /// <summary>Crea la sección; lee sus días al entrar.</summary>
    public DiaryView()
    {
        InitializeComponent();
        DataContext = _model;
        _model.RangeChanged += () =>
        {
            _scrolled = false;
            _agendaDays = AgendaStep;
            _ = LoadAsync();
        };
        _model.LookChanged += Build;
        _model.Picked += () =>
        {
            Mark();
            BuildSide();
        };
        _today.Tick += async (_, _) => await LoadAsync();
        BarTips();
    }

    /// <summary>Se dispara al pedir ver una sesión, con su id.</summary>
    public event Action<string>? SessionRequested;

    /// <summary>Se dispara al pedir ver una tarea en el Tablero, con su clave.</summary>
    public event Action<string>? TaskRequested;

    /// <summary>Pone la sección al día al entrar y empieza a recalcular hoy.</summary>
    public void Activate()
    {
        _today.Start();
        _ = LoadAsync();
    }

    /// <summary>Deja de recalcular hoy al salir de la sección.</summary>
    public void Deactivate() => _today.Stop();

    /// <summary>Retrocede un paso, como la flecha de la barra.</summary>
    public void Previous() => _model.Move(-1);

    /// <summary>Cambia de vista, como los chips de la barra.</summary>
    public void SetMode(DiaryMode mode) => _model.Mode = mode;

    /// <summary>Abre el calendario desplegable y da su contenido, para guardarlo en el recorrido de imágenes.</summary>
    public Visual OpenPicker()
    {
        Range_Click(null, new RoutedEventArgs());
        return PickerBody.Parent as Visual ?? PickerBody;
    }

    /// <summary>Pone a la derecha una muestra de cada ficha de tooltip con los datos de hoy, para verlas en el recorrido de imágenes.</summary>
    public void ShowSampleCards()
    {
        if (!_model.Days.TryGetValue(DateTime.Today, out var day) || day.Blocks.Count == 0) return;
        var cards = new List<Control> { BlockCard(day, day.Blocks[0]), DayCard(day, "1.º día con más horas de agente de 20") };
        if (day.Tasks.SelectMany(t => t.Commits).FirstOrDefault() is { } commit) cards.Add(CommitCard(commit));
        cards.Add(MoveCard(new TaskStateChange { Key = "DEVLAB-99", From = TaskState.Doing, To = TaskState.Review, WhenUtc = DateTime.UtcNow }));
        var list = new StackPanel();
        foreach (var card in cards)
            list.Children.Add(new Border
            {
                Background = DiaryInk.Of("TipFill", "#202228"), BorderBrush = DiaryInk.Of("TipEdge", "#30FFFFFF"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 10), Margin = new Thickness(0, 0, 0, 12), Child = card,
            });
        SideHost.Child = new ScrollViewer { Content = list };
        SideHost.IsVisible = true;
    }

    /// <summary>Elige el primer bloque de hoy, para ver su ficha en el recorrido de imágenes.</summary>
    /// <param name="withFolder">Elige el primero que trabajó en la carpeta de un worktree, si lo hay.</param>
    public void SelectFirstToday(bool withFolder = false, bool withMove = false)
    {
        if (!_model.Days.TryGetValue(DateTime.Today, out var day) || day.Blocks.Count == 0) return;
        var moves = day.Tasks.SelectMany(t => t.Moves).ToList();
        var block = withMove ? day.Blocks.FirstOrDefault(b => moves.Any(m => m.WhenUtc >= b.StartUtc && m.WhenUtc <= b.EndUtc)) ?? day.Blocks[0]
            : withFolder ? day.Blocks.FirstOrDefault(b => b.Folder is not null) ?? day.Blocks[0] : day.Blocks[0];
        _model.Selected = (day, block);
    }

    /// <summary>Lee los días del rango que faltan o aún pueden cambiar, y repinta.</summary>
    /// <remarks>
    /// Un día de antes de ayer ya leído no se vuelve a leer: no cambia. La rueda de carga solo sale si falta alguno,
    /// para que el recálculo de hoy no haga parpadear la vista. Se lee de pocos en pocos, los más recientes primero,
    /// pintando cada tanda: un mes sin resúmenes guardados tarda.
    /// </remarks>
    private async Task LoadAsync()
    {
        var generation = ++_generation;
        var need = Wanted().Where(d => d <= DateTime.Today
                                           && (!_model.Days.TryGetValue(d, out var known) || (d >= DateTime.Today.AddDays(-1) && !DiaryStore.Final(known))))
            .ToList();
        Build();
        if (need.Count == 0) return;
        if (need.Any(d => !_model.Days.ContainsKey(d))) _model.Loading = true;
        try
        {
            var (settings, links, title) = DiaryKeeper.Snapshot();
            foreach (var batch in need.OrderByDescending(d => d).Chunk(LoadBatch))
            {
                var days = await Task.Run(() => batch.Select(d => DiaryStore.Get(d, settings, links, title, pullRequests: true, DateTime.UtcNow)).ToList());
                if (generation != _generation) return;
                _model.SetDays(days);
                Build();
            }
        }
        catch (Exception ex)
        {
            JsonStore.LogError("diary", ex);
        }
        finally
        {
            if (generation == _generation) _model.Loading = false;
        }
        Build();
        // En la agenda, si los días leídos no tenían nada y quedan más, se leen los siguientes sin esperar al scroll.
        if (generation == _generation && _model.IsAgenda && AgendaWindow().All(d => !_model.Days.TryGetValue(d, out var day) || day.Empty) && MoreAgenda())
        {
            _agendaDays += AgendaStep;
            await LoadAsync();
        }
    }

    // La agenda lee de una semana en una semana, la más reciente primero: leer el mes entero de golpe pesaba demasiado.
    private const int AgendaStep = 7;
    private int _agendaDays = AgendaStep;

    /// <summary>Los días que hay que leer: los del rango o, en la agenda, solo los que se enseñan ya.</summary>
    private List<DateTime> Wanted() => _model.IsAgenda ? AgendaWindow() : _model.Range;

    /// <summary>Los días que enseña la agenda: los más recientes del mes, hasta hoy, de una semana en una semana.</summary>
    private List<DateTime> AgendaWindow() =>
        _model.Range.Where(d => d <= DateTime.Today).OrderByDescending(d => d).Take(_agendaDays).ToList();

    /// <summary>Si al mes le quedan días por enseñar en la agenda.</summary>
    private bool MoreAgenda() => _model.Range.Count(d => d <= DateTime.Today) > _agendaDays;

    /// <summary>Al llegar cerca del final de la agenda, lee la semana anterior.</summary>
    private void Agenda_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_model.Loading || !MoreAgenda()) return;
        if (AgendaScroll.Offset.Y + AgendaScroll.Viewport.Height < AgendaScroll.Extent.Height - 240) return;
        _agendaDays += AgendaStep;
        _ = LoadAsync();
    }

    /// <summary>Pinta la vista elegida, el lado derecho y la leyenda.</summary>
    private void Build()
    {
        _blocks.Clear();
        if (_model.GridVisible) BuildGrid();
        else if (_model.IsMonth) BuildMonth();
        else BuildAgenda();
        BuildSide();
        BuildLegend();
        DiaryInk.Tip(WorkHoursChip, WorkHoursCard());
    }

    /// <summary>Pinta a la derecha la ficha del bloque elegido o, en la vista Día, el parte del día.</summary>
    private void BuildSide()
    {
        SideHost.Child = _model.Selected is { } s ? Detail(s.Day, s.Block)
            : _model.ParteVisible && _model.Days.TryGetValue(_model.Anchor, out var day) && !day.Empty
                ? new ScrollViewer { VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = DayParte(day) }
                : null;
        SideHost.IsVisible = SideHost.Child is not null;
    }

    // BARRA

    /// <summary>Las fichas de los botones de la barra.</summary>
    private void BarTips()
    {
        DiaryInk.Tip(TodayButton, DiaryInk.Card("Hoy", "Vuelve al día, la semana o el mes de hoy, según la vista."));
        DiaryInk.Tip(RangeButton, DiaryInk.Card("Elegir en el calendario", "Un día, una semana o un mes, según la vista. Los días con un punto tienen sesiones."));
        DiaryInk.Tip(CopyButton, DiaryInk.Card("Copiar parte",
            "Lo hecho en los días que se ven, por tarea y en texto, para pegarlo en la daily o al imputar horas.",
            footer: "Lo hecho son los mensajes de commit; sin commits, lo que se pidió en las sesiones."));
        PreviousButton.AddHandler(PointerEnteredEvent, (_, _) => DiaryInk.Tip(PreviousButton, DiaryInk.Card(_model.PreviousTip)));
        NextButton.AddHandler(PointerEnteredEvent, (_, _) => DiaryInk.Tip(NextButton, DiaryInk.Card(_model.NextTip)));
    }

    /// <summary>La ficha del chip del horario laboral: qué franja se ve con él y dónde se cambia.</summary>
    private static Control WorkHoursCard()
    {
        var schedule = DiaryModel.Schedule;
        return DiaryInk.Card("Horario laboral", schedule.Describe(), rows:
        [
            ("Encendido", "Solo la franja de trabajo; lo que caiga fuera se avisa arriba o abajo de cada día"),
            ("Apagado", "El día entero, de 0:00 a 24:00"),
        ], footer: "La franja se cambia en Configuración → General.");
    }

    private void Today_Click(object? sender, RoutedEventArgs e) => _model.Anchor = DateTime.Today;
    private void Previous_Click(object? sender, RoutedEventArgs e) => _model.Move(-1);
    private void Next_Click(object? sender, RoutedEventArgs e) => _model.Move(1);

    /// <summary>Copia el parte de los días que se ven y lo dice un momento en la barra.</summary>
    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
            await clipboard.SetTextAsync(_model.Report());
            _model.CopiedText = "Copiado";
            DispatcherTimer.RunOnce(() => _model.CopiedText = "", TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            JsonStore.LogError("diary", ex);
        }
    }

    // LEYENDA

    /// <summary>Explica las marcas de la vista que se ve.</summary>
    private void BuildLegend()
    {
        Legend.Children.Clear();
        if (_model.IsMonth)
        {
            var scale = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var step in new[] { 0.08, 0.18, 0.28, 0.36 })
                scale.Children.Add(new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 2, 0), Background = DiaryInk.Of("Accent", "#61AFEF", step) });
            LegendItem(scale, "Más intenso, más horas de agente, frente al día más trabajado del mes");
            LegendItem(DiaryInk.CommitMark(DiaryInk.Muted), "Commits");
            return;
        }
        LegendItem(DiaryInk.CommitMark(DiaryInk.Muted), "Commit");
        if (_model.GridVisible)
        {
            LegendItem(ArtifactTile(ArtifactKind.Page, 13), "Artifact publicado, con el color de su clase");
            LegendItem(DiaryInk.MoveMark(), "Cambio de columna de una tarea");
            LegendItem(new Border { Width = 14, Height = 10, CornerRadius = new CornerRadius(2), Background = DiaryInk.Of("DataFiveHour", "#E5C07B", 0.45) }, "Esperándote");
            LegendItem(new Border { Width = 14, Height = 2, Background = DiaryInk.Of("Danger", "#E03E4C") }, "Ahora");
        }
        LegendItem(new Border { Width = 4, Height = 12, CornerRadius = new CornerRadius(2), Background = DiaryInk.Of("Accent", "#61AFEF") },
            "Color del repositorio: el de su grupo en Sesiones");
    }

    /// <summary>Una entrada de la leyenda: su marca y lo que significa.</summary>
    private void LegendItem(Control mark, string text)
    {
        mark.VerticalAlignment = VerticalAlignment.Center;
        var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 18, 4) };
        item.Children.Add(mark);
        var label = DiaryInk.Text(text, 11, DiaryInk.Faint);
        label.Margin = new Thickness(7, 0, 0, 0);
        label.VerticalAlignment = VerticalAlignment.Center;
        item.Children.Add(label);
        Legend.Children.Add(item);
    }
}
