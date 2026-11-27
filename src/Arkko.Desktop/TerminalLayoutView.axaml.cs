using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Editor de lo que ak pinta en la terminal: barra de estado (piezas, orden, separadores y colores) y resumen del prompt.</summary>
/// <remarks>
/// Cada cambio se guarda solo, medio segundo después del último: un arrastre cambia varias cosas
/// seguidas y así se escribe una vez. Lo usan la ventana de la flotante y la configuración del panel.
/// El arrastre es propio, con el puntero capturado por la pieza: el del sistema no se comporta igual en
/// Windows, Linux y macOS, y aquí no hace falta sacar nada de la ventana.
/// </remarks>
public partial class TerminalLayoutView : UserControl
{
    private const double DragThreshold = 4;
    private readonly EditorModel _model = new();
    private readonly DispatcherTimer _saveDelay = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _loading;
    private Point _dragStart;
    private Point _grab;
    private BlockModel? _pressed;
    private Border? _pill;
    private Control? _ghost;

    /// <summary>Abre el editor con la configuración que hay ahora mismo.</summary>
    public TerminalLayoutView()
    {
        InitializeComponent();
        DataContext = _model;
        _model.Changed += RenderPreview;
        _model.Changed += () =>
        {
            if (_loading) return;
            _saveDelay.Stop();
            _saveDelay.Start();
        };
        _saveDelay.Tick += (_, _) =>
        {
            _saveDelay.Stop();
            Save();
        };
        // Si se cierra con un cambio aún por guardar, se guarda antes.
        Unloaded += (_, _) =>
        {
            if (!_saveDelay.IsEnabled) return;
            _saveDelay.Stop();
            Save();
        };
        Reload();
    }

    private Window? Owner => TopLevel.GetTopLevel(this) as Window;

    /// <summary>Vuelve a cargar la configuración guardada.</summary>
    public void Reload()
    {
        _loading = true;
        try
        {
            _model.LoadFrom(TerminalLayout.Load());
        }
        finally
        {
            _loading = false;
        }
        SyncHexBox();
        RenderPreview();
    }

    /// <summary>Pinta la vista previa con los mismos trozos que pintará ak en la terminal.</summary>
    private void RenderPreview()
    {
        var inlines = Preview.Inlines ??= [];
        inlines.Clear();
        if (_model.SummaryTab)
        {
            var summary = _model.ShowSummary
                ? PromptSummary.Render(_model.SummaryLine.Select(b => b.Kind), PreviewData.Usage(), PreviewData.Current().Limits)
                : "";
            inlines.Add(summary.Length > 0
                ? new Run(summary)
                : new Run(_model.ShowSummary ? "(resumen vacío)" : "(resumen oculto)") { Foreground = Brushes.Gray });
            return;
        }
        if (!_model.ShowStatusline)
        {
            inlines.Add(new Run("(barra de estado oculta)") { Foreground = Brushes.Gray });
            return;
        }
        var segments = TerminalRenderer.Segments(_model.ToLayout(), PreviewData.Current());
        if (segments.Count == 0)
        {
            inlines.Add(new Run("(línea vacía)") { Foreground = Brushes.Gray });
            return;
        }
        foreach (var s in segments)
        {
            var run = new Run(s.Text);
            if (s.Color is { } hex && TerminalRenderer.TryRgb(hex, out var r, out var g, out var b))
                run.Foreground = new ImmutableSolidColorBrush(Color.FromRgb((byte)r, (byte)g, (byte)b));
            inlines.Add(run);
        }
    }

    // ARRASTRAR Y SOLTAR

    /// <summary>Recuerda la pieza pulsada y dónde, para distinguir un clic de un arrastre, y le da el puntero.</summary>
    private void Block_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { DataContext: BlockModel block } pill || !e.GetCurrentPoint(pill).Properties.IsLeftButtonPressed) return;
        _pressed = block;
        _pill = pill;
        _dragStart = e.GetPosition(Root);
        _grab = e.GetPosition(pill);
        e.Pointer.Capture(pill);
        e.Handled = true;
    }

    /// <summary>Empieza el arrastre cuando el ratón se ha movido lo suficiente; después, mueve la copia y la marca.</summary>
    private void Block_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressed is null || _pill is null) return;
        var at = e.GetPosition(Root);
        if (_ghost is null)
        {
            if (Math.Abs(at.X - _dragStart.X) < DragThreshold && Math.Abs(at.Y - _dragStart.Y) < DragThreshold) return;
            StartGhost(_pill);
        }
        Canvas.SetLeft(_ghost!, at.X - _grab.X);
        Canvas.SetTop(_ghost!, at.Y - _grab.Y);
        HideMarkers();
        if (ZoneAt(e) is { IsLine: true } zone && zone.Summary == _pressed.IsSummary)
        {
            var (x, y, height) = MarkerPlace(zone, DropIndex(zone, e));
            Canvas.SetLeft(zone.Marker, x - 2);
            Canvas.SetTop(zone.Marker, y);
            zone.Marker.Height = height;
            zone.Marker.IsVisible = true;
        }
    }

    /// <summary>Al soltar: si se arrastraba, coloca la pieza donde cae; si fue un clic sobre una pieza de la línea, la selecciona.</summary>
    private void Block_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_pressed is not { } block) return;
        var dragged = _ghost is not null;
        var zone = dragged ? ZoneAt(e) : null;
        var index = zone is { IsLine: true } line ? DropIndex(line, e) : 0;
        EndDrag();
        e.Pointer.Capture(null);
        if (!dragged)
        {
            if (_model.Line.Contains(block)) _model.Select(block);
            SyncHexBox();
            return;
        }
        // Las piezas de una línea no se mezclan con las de la otra.
        if (zone is null || zone.Summary != block.IsSummary) return;
        if (zone.IsLine) _model.PlaceInLine(block, index);
        else if (zone.Blocks.Contains(block)) _model.RemoveFromLine(block);
        SyncHexBox();
    }

    /// <summary>Si se pierde el puntero a medio arrastre (otra ventana, una tecla del sistema), se deja todo como estaba.</summary>
    private void Block_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndDrag();

    /// <summary>«Coge» la pieza: una foto suya sigue al ratón y la original queda atenuada en su sitio.</summary>
    /// <remarks>Foto fija y no una copia en vivo: la original se atenúa durante el arrastre, y una copia en vivo también lo haría.</remarks>
    private void StartGhost(Border pill)
    {
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var size = pill.Bounds.Size;
        var bitmap = new RenderTargetBitmap(new PixelSize(Math.Max(1, (int)Math.Ceiling(size.Width * scale)), Math.Max(1, (int)Math.Ceiling(size.Height * scale))),
            new Vector(96 * scale, 96 * scale));
        bitmap.Render(pill);
        _ghost = new Border
        {
            CornerRadius = pill.CornerRadius,
            BoxShadow = BoxShadows.Parse("0 3 10 0 #40000000"),
            Opacity = 0.95,
            Child = new Image { Source = bitmap, Width = size.Width, Height = size.Height },
        };
        DragLayer.Children.Add(_ghost);
        pill.Opacity = 0.3;
        Root.Cursor = new Cursor(StandardCursorType.Hand);
    }

    /// <summary>Quita la copia, devuelve la pieza a su aspecto y esconde las marcas.</summary>
    private void EndDrag()
    {
        if (_ghost is not null) DragLayer.Children.Remove(_ghost);
        _ghost = null;
        if (_pill is not null) _pill.Opacity = 1;
        _pill = null;
        _pressed = null;
        Root.Cursor = null;
        HideMarkers();
    }

    private void HideMarkers()
    {
        DropMarker.IsVisible = false;
        SummaryMarker.IsVisible = false;
    }

    /// <summary>Una zona donde soltar: una de las dos líneas o una de sus bandejas de disponibles.</summary>
    private sealed record Zone(Border Area, bool Summary, bool IsLine, IList<BlockModel> Blocks, ItemsControl Items, Panel Origin, Border Marker);

    /// <summary>Las cuatro zonas, con lo que hace falta para colocar una pieza en cada una.</summary>
    private IEnumerable<Zone> Zones() =>
    [
        new(StatusLine, false, true, _model.Line, LineItems, LineArea, DropMarker),
        new(StatusTray, false, false, _model.Line, LineItems, LineArea, DropMarker),
        new(SummaryZone, true, true, _model.SummaryLine, SummaryItems, SummaryArea, SummaryMarker),
        new(SummaryTray, true, false, _model.SummaryLine, SummaryItems, SummaryArea, SummaryMarker),
    ];

    /// <summary>La zona bajo el ratón, si hay una a la vista.</summary>
    private Zone? ZoneAt(PointerEventArgs e) =>
        Zones().FirstOrDefault(z => z.Area.IsEffectivelyVisible && new Rect(z.Area.Bounds.Size).Contains(e.GetPosition(z.Area)));

    /// <summary>Posición de la línea en la que caería la pieza, según dónde está el ratón.</summary>
    /// <remarks>
    /// Las piezas pueden ocupar varias filas: cae antes de la primera pieza cuya fila está por debajo
    /// del ratón, o que está en su fila y a la derecha de él.
    /// </remarks>
    private static int DropIndex(Zone zone, PointerEventArgs e)
    {
        var p = e.GetPosition(zone.Items);
        for (var i = 0; i < zone.Blocks.Count; i++)
        {
            if (PieceBounds(zone.Items, i) is not { } r) continue;
            if (p.Y < r.Top || (p.Y <= r.Bottom && p.X < r.Left + r.Width / 2)) return i;
        }
        return zone.Blocks.Count;
    }

    /// <summary>Dónde se pinta la marca para una posición de la línea: coordenadas y altura.</summary>
    private static (double X, double Y, double Height) MarkerPlace(Zone zone, int index)
    {
        var origin = zone.Items.TranslatePoint(new Point(0, 0), zone.Origin) ?? default;
        var count = zone.Blocks.Count;
        if (index < count && PieceBounds(zone.Items, index) is { } r) return (origin.X + r.Left, origin.Y + r.Top, r.Height - 6);
        if (count > 0 && PieceBounds(zone.Items, count - 1) is { } last)
            return (origin.X + last.Right - 3, origin.Y + last.Top, last.Height - 6);
        return (origin.X, origin.Y, 28);
    }

    /// <summary>Rectángulo de la pieza en una posición, relativo a su línea.</summary>
    private static Rect? PieceBounds(ItemsControl items, int index) =>
        items.ContainerFromIndex(index) is { } c && c.TranslatePoint(new Point(0, 0), items) is { } at
            ? new Rect(at, c.Bounds.Size)
            : null;

    // BOTONES

    /// <summary>Añade un separador al final de la línea.</summary>
    private void AddSeparator_Click(object? sender, RoutedEventArgs e) => _model.AddSeparator();

    /// <summary>Aplica el color de la muestra pulsada a la pieza seleccionada.</summary>
    private void Swatch_Click(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        if ((sender as Control)?.DataContext is Swatch swatch) _model.SetColor(swatch.Hex);
        SyncHexBox();
    }

    /// <summary>Vuelve al color por defecto (o «según el uso» en las barras).</summary>
    private void DefaultColor_Click(object? sender, RoutedEventArgs e)
    {
        _model.SetColor(null);
        SyncHexBox();
    }

    /// <summary>Aplica el color escrito al pulsar Intro.</summary>
    private void HexBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) ApplyHex();
    }

    /// <summary>Aplica el color escrito al salir del cuadro.</summary>
    private void HexBox_LostFocus(object? sender, RoutedEventArgs e) => ApplyHex();

    /// <summary>Valida el hexadecimal escrito y lo aplica; si no es válido, restaura el actual.</summary>
    private void ApplyHex()
    {
        var hex = "#" + (HexBox.Text ?? "").Trim().TrimStart('#').ToUpperInvariant();
        if (TerminalRenderer.TryRgb(hex, out _, out _, out _)) _model.SetColor(hex);
        SyncHexBox();
    }

    /// <summary>Muestra en el cuadro el color de la pieza seleccionada.</summary>
    private void SyncHexBox() => HexBox.Text = _model.Selected?.Color?.TrimStart('#') ?? "";

    /// <summary>Guarda la configuración y lo dice un momento; ak la usa en su siguiente pintado.</summary>
    private void Save()
    {
        try
        {
            _model.ToLayout().Save();
        }
        catch (Exception ex)
        {
            JsonStore.LogError("terminal-layout", ex);
            _ = AppDialog.ShowAsync(Owner, "No se ha podido guardar la configuración de la terminal", ex.Message, note: "El detalle está en errors.log.");
            return;
        }
        SavedNote.Opacity = 1;
        DispatcherTimer.RunOnce(() => SavedNote.Opacity = 0, TimeSpan.FromSeconds(3));
    }
}

/// <summary>Una pieza en el editor: tipo, línea a la que pertenece, color y si está seleccionada.</summary>
public sealed class BlockModel : INotifyPropertyChanged
{
    private string? _color;
    private bool _isSelected;

    public BlockModel(string kind, string? color, bool isSummary = false)
    {
        Kind = kind;
        _color = color;
        IsSummary = isSummary;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Kind { get; }

    /// <summary>Pieza del resumen del prompt; si no, de la barra de estado.</summary>
    public bool IsSummary { get; }

    public string Label => IsSummary ? SummaryBlocks.Label(Kind) : TerminalBlocks.Label(Kind);

    public string? Color
    {
        get => _color;
        set
        {
            _color = value;
            OnChanged();
            OnChanged(nameof(ColorBrush));
            OnChanged(nameof(ColorVisible));
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            OnChanged();
        }
    }

    public IBrush? ColorBrush => _color is { } hex && TerminalRenderer.TryRgb(hex, out var r, out var g, out var b)
        ? new ImmutableSolidColorBrush(Avalonia.Media.Color.FromRgb((byte)r, (byte)g, (byte)b))
        : null;

    public bool ColorVisible => ColorBrush is not null;

    /// <summary>Avisa a la vista de un cambio de propiedad.</summary>
    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Una muestra de la paleta de colores.</summary>
public sealed record Swatch(string Hex)
{
    public IBrush Brush { get; } = new ImmutableSolidColorBrush(Color.Parse(Hex));
}

/// <summary>Estado del editor: las dos líneas con sus piezas disponibles, la selección y la pestaña.</summary>
public sealed class EditorModel : INotifyPropertyChanged
{
    private bool _showSummary = true;
    private bool _showStatusline = true;
    private bool _summaryTab;
    private BlockModel? _selected;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Se dispara con cualquier cambio que afecte a la vista previa.</summary>
    public event Action? Changed;

    public ObservableCollection<BlockModel> Line { get; } = [];
    public ObservableCollection<BlockModel> Available { get; } = [];
    public ObservableCollection<BlockModel> SummaryLine { get; } = [];
    public ObservableCollection<BlockModel> SummaryAvailable { get; } = [];

    /// <summary>Pestaña del resumen; si no, la de la barra de estado.</summary>
    public bool SummaryTab
    {
        get => _summaryTab;
        set
        {
            if (_summaryTab == value) return;
            _summaryTab = value;
            OnChanged();
            OnChanged(nameof(StatuslineTab));
            OnChanged(nameof(SummaryTabVisible));
            OnChanged(nameof(StatuslineTabVisible));
            OnChanged(nameof(PreviewNote));
            Changed?.Invoke();
        }
    }

    /// <summary>Inverso de SummaryTab, para el botón de la pestaña de la barra de estado.</summary>
    public bool StatuslineTab
    {
        get => !_summaryTab;
        set
        {
            if (value) SummaryTab = false;
        }
    }

    public bool SummaryTabVisible => _summaryTab;
    public bool StatuslineTabVisible => !_summaryTab;

    /// <summary>Nota bajo la vista previa: cómo se ve de verdad la línea de la pestaña activa.</summary>
    public string PreviewNote => _summaryTab
        ? "Con los datos de tu último prompt. En Claude Code sale tras cada respuesta y salta de línea si no cabe."
        : "En la terminal va en una sola línea: si no cabe en el ancho de la ventana, Claude Code la corta.";

    public IReadOnlyList<Swatch> Palette { get; } =
    [
        new("#56B6C2"), new("#61AFEF"), new("#C678DD"), new("#E06C75"), new("#E5C07B"),
        new("#98C379"), new("#D19A66"), new("#F09A76"), new("#ABB2BF"), new("#FFFFFF"),
    ];

    public bool ShowSummary
    {
        get => _showSummary;
        set
        {
            _showSummary = value;
            OnChanged();
            OnChanged(nameof(SummaryOpacity));
            Changed?.Invoke();
        }
    }

    public bool ShowStatusline
    {
        get => _showStatusline;
        set
        {
            _showStatusline = value;
            OnChanged();
            OnChanged(nameof(StatuslineOpacity));
            Changed?.Invoke();
        }
    }

    /// <summary>Las piezas se atenúan cuando la barra de estado está oculta.</summary>
    public double StatuslineOpacity => _showStatusline ? 1 : 0.4;

    /// <summary>Las piezas del resumen se atenúan cuando el resumen está oculto.</summary>
    public double SummaryOpacity => _showSummary ? 1 : 0.4;

    public BlockModel? Selected => _selected;
    public bool SelectionVisible => _selected is not null;
    public bool NoSelectionVisible => _selected is null;
    public string DefaultColorText => _selected is { } s && TerminalBlocks.IsLimit(s.Kind) ? "Según el uso" : "Por defecto";

    /// <summary>Carga una configuración en el editor.</summary>
    public void LoadFrom(TerminalLayout layout)
    {
        Select(null);
        Line.Clear();
        foreach (var item in layout.Items)
        {
            if (item.Kind != TerminalBlocks.Separator && Line.Any(b => b.Kind == item.Kind)) continue;
            Line.Add(new BlockModel(item.Kind, item.Color));
        }
        RefreshAvailable();
        SummaryLine.Clear();
        foreach (var kind in layout.SummaryItems.Where(SummaryBlocks.All.Contains).Distinct())
            SummaryLine.Add(new BlockModel(kind, null, isSummary: true));
        RefreshSummaryAvailable();
        ShowSummary = layout.ShowSummary;
        ShowStatusline = layout.ShowStatusline;
        Changed?.Invoke();
    }

    /// <summary>Configuración equivalente a lo que hay en el editor.</summary>
    public TerminalLayout ToLayout() => new()
    {
        ShowSummary = _showSummary,
        ShowStatusline = _showStatusline,
        Revision = TerminalLayout.CurrentRevision,
        Items = Line.Select(b => new LayoutItem { Kind = b.Kind, Color = b.Color }).ToList(),
        SummaryItems = SummaryLine.Select(b => b.Kind).ToList(),
    };

    /// <summary>Coloca una pieza en la línea: la mueve si ya estaba o la saca de «Disponibles».</summary>
    public void PlaceInLine(BlockModel block, int index)
    {
        var (line, available) = block.IsSummary ? (SummaryLine, SummaryAvailable) : (Line, Available);
        var from = line.IndexOf(block);
        if (from >= 0)
        {
            if (index > from) index--;
            line.RemoveAt(from);
        }
        else
        {
            available.Remove(block);
        }
        line.Insert(Math.Clamp(index, 0, line.Count), block);
        Changed?.Invoke();
    }

    /// <summary>Devuelve una pieza a «Disponibles»; los separadores simplemente desaparecen.</summary>
    public void RemoveFromLine(BlockModel block)
    {
        if (block.IsSummary)
        {
            if (!SummaryLine.Remove(block)) return;
            RefreshSummaryAvailable();
            Changed?.Invoke();
            return;
        }
        if (!Line.Remove(block)) return;
        if (_selected == block) Select(null);
        block.Color = block.Kind == TerminalBlocks.Model ? "#56B6C2" : null;
        RefreshAvailable();
        Changed?.Invoke();
    }

    /// <summary>Añade un separador al final de la línea.</summary>
    public void AddSeparator()
    {
        Line.Add(new BlockModel(TerminalBlocks.Separator, null));
        Changed?.Invoke();
    }

    /// <summary>Selecciona una pieza de la línea para cambiar su color; null quita la selección.</summary>
    public void Select(BlockModel? block)
    {
        if (block?.Kind == TerminalBlocks.Separator) block = null;
        if (_selected is not null) _selected.IsSelected = false;
        _selected = block;
        if (_selected is not null) _selected.IsSelected = true;
        OnChanged(nameof(Selected));
        OnChanged(nameof(SelectionVisible));
        OnChanged(nameof(NoSelectionVisible));
        OnChanged(nameof(DefaultColorText));
    }

    /// <summary>Cambia el color de la pieza seleccionada.</summary>
    public void SetColor(string? hex)
    {
        if (_selected is null) return;
        _selected.Color = hex;
        Changed?.Invoke();
    }

    /// <summary>Rehace «Disponibles» con las piezas que no están en la línea, en su orden habitual.</summary>
    private void RefreshAvailable()
    {
        var keep = Available.ToDictionary(b => b.Kind);
        Available.Clear();
        foreach (var kind in TerminalBlocks.All.Where(k => Line.All(b => b.Kind != k)))
            Available.Add(keep.TryGetValue(kind, out var existing) ? existing : new BlockModel(kind, kind == TerminalBlocks.Model ? "#56B6C2" : null));
    }

    /// <summary>Rehace las piezas disponibles del resumen: las que no están en él, en su orden habitual.</summary>
    private void RefreshSummaryAvailable()
    {
        var keep = SummaryAvailable.ToDictionary(b => b.Kind);
        SummaryAvailable.Clear();
        foreach (var kind in SummaryBlocks.All.Where(k => SummaryLine.All(b => b.Kind != k)))
            SummaryAvailable.Add(keep.TryGetValue(kind, out var existing) ? existing : new BlockModel(kind, null, isSummary: true));
    }

    /// <summary>Avisa a la vista de un cambio de propiedad.</summary>
    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Datos para la vista previa: los de la sesión más reciente, o un ejemplo si no hay ninguna.</summary>
public static class PreviewData
{
    /// <summary>Consumo del último prompt registrado (de hoy o de ayer), o uno de ejemplo si no hay ninguno.</summary>
    public static TurnUsage Usage()
    {
        foreach (var day in new[] { DateTime.Today, DateTime.Today.AddDays(-1) })
        {
            var last = JsonStore.ReadLines<PromptRecord>(AppPaths.PromptsFile(day)).LastOrDefault();
            if (last is not null) return last.Usage;
        }
        var sample = new TurnUsage { DurationMs = 153_000 };
        sample.ByModel["claude-opus-5-5"] = new ModelTokens { Input = 22, Output = 6_700, CacheWrite = 13_900, CacheRead = 1_200_000, ApiCalls = 11 };
        return sample;
    }

    /// <summary>Datos reales de la última sesión activa, con los límites guardados.</summary>
    public static TerminalData Current()
    {
        var limits = JsonStore.Read<LimitsSnapshot>(AppPaths.LimitsFile)?.Limits ?? [];
        if (limits.Count == 0)
        {
            limits =
            [
                new LimitInfo { Key = "five_hour", Label = "5 h", UsedPct = 22, ResetsAtUtc = DateTime.UtcNow.AddHours(2) },
                new LimitInfo { Key = "seven_day", Label = "7 días", UsedPct = 45, ResetsAtUtc = DateTime.UtcNow.AddDays(4) },
            ];
        }

        var s = SessionStore.LoadAll().OrderByDescending(x => x.UpdatedUtc).FirstOrDefault();
        return s is null
            ? new TerminalData("Opus 5.5", "mi-proyecto", "Nombre de la sesión", 39, "medium", limits, 5.25, "develop")
            : new TerminalData(s.Model ?? "Opus 5.5", s.Repo ?? s.Project ?? "mi-proyecto", s.SessionName, s.ContextPct ?? 39,
                s.Effort ?? "medium", limits, s.CostUsd ?? 5.25, s.Repo is null ? null : s.Project);
    }
}
