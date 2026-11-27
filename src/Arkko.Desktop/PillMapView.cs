using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;

namespace Arkko.Desktop;

/// <summary>Los monitores conectados, dibujados como monitores en su posición y proporción, con la pastilla en miniatura donde saldrá.</summary>
/// <remarks>
/// La pastilla se arrastra a cualquier punto de cualquier monitor; pulsar en un monitor la lleva allí. Cerca de un
/// borde se pega a él. Al soltarla se guarda en proporción a la zona útil de su monitor: ver <see cref="PillPlacement"/>.
/// Los monitores llevan colores propios, de aparato, iguales con los dos temas; solo sus nombres siguen al tema.
/// </remarks>
public sealed class PillMapView : Control
{
    private const double Pad = 8;
    private const double Gap = 6;
    private const double Bezel = 4;
    private const double StandHeight = 14;
    private const double LabelHeight = 20;
    private const double MinPillWidth = 34;
    private const double MinPillHeight = 10;
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
    private static readonly Cursor MoveCursor = new(StandardCursorType.SizeAll);
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    // Colores de aparato: el marco y el pie oscuros de un monitor, y un fondo de escritorio azulado.
    private static readonly IBrush FrameFill = new SolidColorBrush(Color.Parse("#1B1D22"));
    private static readonly IPen FrameEdge = new Pen(new SolidColorBrush(Color.Parse("#3A3D45")), 1);
    private static readonly IBrush StandFill = new SolidColorBrush(Color.Parse("#2A2D33"));
    private static readonly IBrush Wallpaper = PillPreview.Wallpaper;
    private static readonly IBrush Dimmed = new SolidColorBrush(Color.Parse("#000000"), 0.32);
    private static readonly IBrush Taskbar = new SolidColorBrush(Color.Parse("#0B0C0F"), 0.6);

    private MainViewModel? _model;
    private IReadOnlyList<MonitorBox> _monitors = [];
    private bool _dragging;
    private Vector _grab;
    private (string Key, double X, double Y)? _drag;

    /// <summary>Datos de la ventana flotante, con el sitio de la pastilla.</summary>
    public MainViewModel? Model
    {
        get => _model;
        set
        {
            if (_model is not null) _model.PropertyChanged -= OnModelChanged;
            _model = value;
            if (_model is not null) _model.PropertyChanged += OnModelChanged;
            InvalidateVisual();
        }
    }

    /// <summary>Repinta con cualquier cambio de los datos: el sitio de la pastilla, las pantallas o el tema.</summary>
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    /// <summary>Alto fijo y todo el ancho disponible.</summary>
    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width, 230);

    /// <summary>Un monitor en el dibujo: su pantalla y su clave, el marco y, dentro, la imagen de su pantalla.</summary>
    private sealed record MonitorBox(string Key, string Label, Screen Screen, Rect Frame, Rect Display)
    {
        /// <summary>Punto del dibujo de un punto de esta pantalla, en píxeles.</summary>
        public Point ToMap(PixelPoint p) => new(
            Display.X + (p.X - Screen.Bounds.X) * Display.Width / Screen.Bounds.Width,
            Display.Y + (p.Y - Screen.Bounds.Y) * Display.Height / Screen.Bounds.Height);

        /// <summary>Punto de esta pantalla, en píxeles, de un punto del dibujo.</summary>
        public PixelPoint ToScreen(Point p) => new(
            Screen.Bounds.X + (int)Math.Round((p.X - Display.X) * Screen.Bounds.Width / Display.Width),
            Screen.Bounds.Y + (int)Math.Round((p.Y - Display.Y) * Screen.Bounds.Height / Display.Height));

        /// <summary>Cuántos puntos del dibujo es un píxel de esta pantalla.</summary>
        public double Scale => Display.Width / Screen.Bounds.Width;
    }

    /// <summary>Coloca los monitores a escala, centrados, dejando debajo sitio para el pie y el nombre.</summary>
    private IReadOnlyList<MonitorBox> Measure()
    {
        if (TopLevel.GetTopLevel(this) is not Window window || Bounds.Width <= 0) return [];
        var monitors = PillPlacement.Monitors(window.Screens.All);
        if (monitors.Count == 0) return [];
        var left = monitors.Min(m => m.Screen.Bounds.X);
        var top = monitors.Min(m => m.Screen.Bounds.Y);
        var width = monitors.Max(m => m.Screen.Bounds.Right) - left;
        var height = monitors.Max(m => m.Screen.Bounds.Bottom) - top;
        var room = new Size(Bounds.Width - 2 * Pad, Bounds.Height - 2 * Pad - StandHeight - LabelHeight);
        var scale = Math.Min(room.Width / width, room.Height / height);
        var origin = new Point((Bounds.Width - width * scale) / 2, Pad + (room.Height - height * scale) / 2);
        return monitors.Select(m =>
        {
            var b = m.Screen.Bounds;
            var frame = new Rect(origin.X + (b.X - left) * scale, origin.Y + (b.Y - top) * scale, b.Width * scale, b.Height * scale).Deflate(Gap / 2);
            return new MonitorBox(m.Key, m.Label, m.Screen, frame, frame.Deflate(Bezel));
        }).ToList();
    }

    /// <summary>Monitor sobre el que va la pastilla: donde se puso o, si no se ha puesto o no está conectado, el de esta ventana.</summary>
    private MonitorBox? PillMonitor()
    {
        if (_monitors.Count == 0) return null;
        if (_drag is { } drag) return _monitors.FirstOrDefault(m => m.Key == drag.Key);
        var chosen = _model?.PillScreen is { } key ? _monitors.FirstOrDefault(m => m.Key == key) : null;
        if (chosen is not null) return chosen;
        var here = TopLevel.GetTopLevel(this) is Window window ? window.Screens.ScreenFromWindow(window) : null;
        return _monitors.FirstOrDefault(m => m.Screen.Bounds == here?.Bounds) ?? _monitors.FirstOrDefault(m => m.Screen.IsPrimary) ?? _monitors[0];
    }

    /// <summary>Tamaño de la pastilla en píxeles de un monitor, con su escala.</summary>
    private PixelSize PillPixels(Screen screen)
    {
        var size = _model?.PillSize ?? new Size(150, 38);
        return new PixelSize((int)Math.Ceiling(size.Width * screen.Scaling), (int)Math.Ceiling(size.Height * screen.Scaling));
    }

    /// <summary>Margen de la pastilla con su borde, en píxeles de un monitor.</summary>
    private static int EdgeGap(Screen screen) => (int)Math.Round(PillPlacement.Margin * screen.Scaling);

    /// <summary>Rectángulo de la pastilla en el dibujo, sobre su monitor; con un mínimo para poder cogerla.</summary>
    private Rect PillBox(MonitorBox monitor)
    {
        var (x, y) = _drag is { } drag ? (drag.X, drag.Y) : _model?.PillSpot ?? (1, 1);
        var size = PillPixels(monitor.Screen);
        var topLeft = PillPlacement.Position(monitor.Screen.WorkingArea, size, EdgeGap(monitor.Screen), x, y);
        var box = new Rect(monitor.ToMap(topLeft), new Size(size.Width * monitor.Scale, size.Height * monitor.Scale));
        var width = Math.Max(MinPillWidth, box.Width);
        var height = Math.Max(MinPillHeight, box.Height);
        // Si se agranda para poder cogerla, crece hacia dentro de la pantalla: pegada a la derecha, hacia la izquierda.
        var d = monitor.Display;
        var left = Math.Clamp(box.Left - (width - box.Width) * x, d.Left, Math.Max(d.Left, d.Right - width));
        var top = Math.Clamp(box.Top - (height - box.Height) * y, d.Top, Math.Max(d.Top, d.Bottom - height));
        return new Rect(left, top, width, height);
    }

    /// <summary>Pinta los monitores con su pie y su nombre, y la pastilla en el suyo.</summary>
    public override void Render(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        _monitors = Measure();
        if (_monitors.Count == 0 || _model is null) return;
        var pillMonitor = PillMonitor();

        // Primero los pies, para que un monitor colocado encima de otro tape el pie del de arriba.
        foreach (var m in _monitors) DrawStand(dc, m);
        foreach (var m in _monitors) DrawMonitor(dc, m, m == pillMonitor);
        if (pillMonitor is not null) PillPreview.Paint(dc, PillBox(pillMonitor), _model.PillLook, detailed: false, _dragging ? 2 : 1.2);
    }

    /// <summary>El pie del monitor: un cuello estrecho bajo el marco y una base ancha, y debajo su nombre.</summary>
    private void DrawStand(DrawingContext dc, MonitorBox m)
    {
        var neckWidth = Math.Max(8, m.Frame.Width * 0.08);
        var baseWidth = Math.Max(24, m.Frame.Width * 0.32);
        var neck = new Rect(m.Frame.Center.X - neckWidth / 2, m.Frame.Bottom, neckWidth, StandHeight - 4);
        var foot = new Rect(m.Frame.Center.X - baseWidth / 2, neck.Bottom, baseWidth, 4);
        dc.DrawRectangle(StandFill, null, neck);
        dc.DrawRectangle(StandFill, null, foot, 2, 2);
        DrawLabel(dc, m, new Point(m.Frame.Center.X, foot.Bottom + 4));
    }

    /// <summary>El monitor: marco oscuro, pantalla con fondo de escritorio y su barra de tareas; los demás, más apagados.</summary>
    private static void DrawMonitor(DrawingContext dc, MonitorBox m, bool mine)
    {
        dc.DrawRectangle(FrameFill, FrameEdge, m.Frame, 5, 5);
        dc.DrawRectangle(Wallpaper, null, m.Display, 2, 2);
        // Lo que queda fuera de la zona útil es la barra de tareas, en el lado donde esté.
        var work = new Rect(m.ToMap(m.Screen.WorkingArea.TopLeft), m.ToMap(m.Screen.WorkingArea.BottomRight)).Intersect(m.Display);
        var d = m.Display;
        foreach (var strip in new[]
                 {
                     new Rect(d.Left, d.Top, d.Width, work.Top - d.Top),
                     new Rect(d.Left, work.Bottom, d.Width, d.Bottom - work.Bottom),
                     new Rect(d.Left, work.Top, work.Left - d.Left, work.Height),
                     new Rect(work.Right, work.Top, d.Right - work.Right, work.Height),
                 })
            if (strip.Width >= 1 && strip.Height >= 1) dc.DrawRectangle(Taskbar, null, strip);
        if (!mine) dc.DrawRectangle(Dimmed, null, d, 2, 2);
    }

    /// <summary>Nombre y resolución del monitor, centrados bajo su pie; si no caben, solo el nombre.</summary>
    private static void DrawLabel(DrawingContext dc, MonitorBox m, Point center)
    {
        var brush = ThemeColors.Of("FgMuted", "#9AA0AA");
        var typeface = new Typeface(ThemeColors.Font);
        foreach (var text in new[] { m.Label, m.Label.Split(" · ")[0] })
        {
            var label = new FormattedText(text, Es, FlowDirection.LeftToRight, typeface, 11, brush);
            if (label.Width > m.Frame.Width + Gap && text != m.Label.Split(" · ")[0]) continue;
            dc.DrawText(label, new Point(center.X - label.Width / 2, center.Y));
            return;
        }
    }

    /// <summary>Coge la pastilla para arrastrarla; pulsar en otro punto de un monitor la lleva allí.</summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_model is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var point = e.GetPosition(this);
        var box = PillMonitor() is { } monitor ? PillBox(monitor) : default;
        if (box.Width > 0 && box.Inflate(4).Contains(point)) _grab = point - box.TopLeft;
        else if (_monitors.Any(m => m.Frame.Contains(point))) _grab = new Vector(box.Width / 2, box.Height / 2);
        else return;
        _dragging = true;
        e.Pointer.Capture(this);
        MoveTo(point);
        e.Handled = true;
    }

    /// <summary>Mueve la pastilla con el ratón mientras se arrastra; si no, cambia el cursor sobre la pastilla y los monitores.</summary>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetPosition(this);
        if (_dragging)
        {
            MoveTo(point);
            return;
        }
        var onPill = PillMonitor() is { } monitor && PillBox(monitor).Inflate(4).Contains(point);
        Cursor = onPill ? MoveCursor : _monitors.Any(m => m.Frame.Contains(point)) ? HandCursor : Cursor.Default;
    }

    /// <summary>Suelta la pastilla y guarda su sitio, en el monitor donde se ha soltado.</summary>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
        if (_drag is { } drag && _model is not null)
        {
            _drag = null;
            _model.SetPill(drag.X, drag.Y, drag.Key);
        }
        InvalidateVisual();
    }

    /// <summary>Si se pierde el ratón a medio arrastre, la pastilla vuelve a donde estaba.</summary>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (!_dragging) return;
        _dragging = false;
        _drag = null;
        InvalidateVisual();
    }

    /// <summary>Calcula a qué monitor y a qué punto de su zona útil va la pastilla con el ratón en un punto del dibujo.</summary>
    private void MoveTo(Point point)
    {
        if (_monitors.Count == 0) return;
        var monitor = _monitors.FirstOrDefault(m => m.Frame.Contains(point)) ?? _monitors.MinBy(m => Distance(m.Frame, point))!;
        var screen = monitor.Screen;
        // El imán atrae a menos de 10 puntos del dibujo de un borde, sea cual sea la escala.
        var snap = (int)Math.Round(10 / monitor.Scale);
        var (x, y) = PillPlacement.Fractions(screen.WorkingArea, PillPixels(screen), EdgeGap(screen), monitor.ToScreen(point - _grab), snap);
        _drag = (monitor.Key, x, y);
        InvalidateVisual();
    }

    /// <summary>Distancia de un punto a un rectángulo, para elegir el monitor más cercano si se suelta fuera de todos.</summary>
    private static double Distance(Rect r, Point p)
    {
        var dx = Math.Max(Math.Max(r.Left - p.X, 0), p.X - r.Right);
        var dy = Math.Max(Math.Max(r.Top - p.Y, 0), p.Y - r.Bottom);
        return Math.Sqrt(dx * dx + dy * dy);
    }

}
