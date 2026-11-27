using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Arkko.Desktop;

/// <summary>Columna del tablero de sesiones: apila sus tarjetas y anima las que cambian de sitio.</summary>
/// <remarks>
/// Cada columna va en su propio scroll, que recorta lo que se sale de ella. Por eso, cuando una sesión cambia
/// de columna, no se desliza la tarjeta real (se vería cortada a medio camino): vuela por encima del tablero una
/// copia suya, desde donde estaba hasta su sitio nuevo, y al llegar aparece la real. Dentro de la misma columna,
/// la tarjeta se desliza sin más. Las tarjetas se reconocen por su sesión, porque la lista se rehace entera en
/// cada refresco.
/// </remarks>
public sealed class SessionColumnPanel : Panel
{
    private static readonly TimeSpan MoveDuration = TimeSpan.FromMilliseconds(420);
    private static readonly TimeSpan AppearDuration = TimeSpan.FromMilliseconds(260);

    // Dónde estaba cada sesión la última vez: en qué columna y en qué punto, del tablero y de la columna.
    private static readonly Dictionary<string, (SessionColumnPanel Column, Point AtRoot, Point AtColumn)> Last = new();

    // Columnas vivas, para poder pedirles que no animen la siguiente colocación.
    private static readonly List<WeakReference<SessionColumnPanel>> Columns = [];

    private readonly List<(FlightGhost Ghost, AdornerLayer Layer, Control Child)> _flights = [];
    private bool _laidOut;

    // El tablero vive en el Inicio del panel, que se oculta al pasar a otra sección. Mientras no está a la vista
    // no hay colocación, así que los cambios se acumularían y se representarían de golpe al volver: la primera
    // colocación tras volver no anima, porque una transición que nadie ha visto no hay que contarla.
    private bool _resync;

    public SessionColumnPanel() => Columns.Add(new WeakReference<SessionColumnPanel>(this));

    /// <summary>Las columnas dejan de estar a la vista: cortan sus vuelos y no animan la vuelta.</summary>
    public static void Resync()
    {
        Columns.RemoveAll(w => !w.TryGetTarget(out _));
        foreach (var weak in Columns)
        {
            if (!weak.TryGetTarget(out var column)) continue;
            column._resync = true;
            column.CancelFlights();
        }
    }

    /// <summary>Mide las tarjetas al ancho de la columna, una debajo de otra.</summary>
    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? 380 : available.Width;
        double height = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(width, double.PositiveInfinity));
            height += child.DesiredSize.Height;
        }
        return new Size(width, height);
    }

    /// <summary>Coloca las tarjetas y anima las que han cambiado de sitio o de columna.</summary>
    protected override Size ArrangeOverride(Size final)
    {
        double top = 0;
        var first = !_laidOut;
        _laidOut = true;
        var animate = !_resync;
        _resync = false;
        foreach (var child in Children)
        {
            var at = new Point(0, top);
            var size = new Size(final.Width, child.DesiredSize.Height);
            child.Arrange(new Rect(at, size));
            top += size.Height;

            if ((child.DataContext as SessionRow)?.SessionId is not { } key) continue;
            var root = Board();
            var atRoot = root is null ? at : this.TranslatePoint(at, root) ?? at;

            if (animate)
            {
                if (Last.TryGetValue(key, out var before))
                {
                    if (before.Column == this)
                    {
                        var delta = before.AtColumn - at;
                        if (Math.Abs(delta.X) + Math.Abs(delta.Y) > 0.5) Slide(child, delta);
                    }
                    else if (root is not null)
                    {
                        Fly(child, root, before.AtRoot, atRoot, size);
                    }
                }
                else if (!first)
                {
                    Appear(child);
                }
            }
            Last[key] = (this, atRoot, at);
        }
        return final;
    }

    /// <summary>Tablero que contiene la columna: el primer antecesor llamado BoardArea.</summary>
    private Control? Board() => this.GetVisualAncestors().OfType<Control>().FirstOrDefault(c => c.Name == "BoardArea");

    /// <summary>Desliza una tarjeta dentro de su columna, desde su sitio anterior.</summary>
    private static void Slide(Control child, Vector from)
    {
        var move = new TranslateTransform(from.X, from.Y);
        child.RenderTransform = move;
        Tween.Run(MoveDuration, t =>
        {
            move.X = from.X * (1 - t);
            move.Y = from.Y * (1 - t);
        });
    }

    /// <summary>Hace aparecer una tarjeta nueva con un fundido y un leve descenso.</summary>
    private static void Appear(Control child)
    {
        var drop = new TranslateTransform(0, -10);
        child.RenderTransform = drop;
        Tween.Run(AppearDuration, t =>
        {
            drop.Y = -10 * (1 - t);
            child.Opacity = t;
        }, easeOutOnly: true);
    }

    /// <summary>Hace volar una copia de la tarjeta por encima del tablero hasta su columna nueva.</summary>
    /// <remarks>La tarjeta real se oculta durante el vuelo y aparece al llegar la copia.</remarks>
    private void Fly(Control child, Control root, Point from, Point to, Size size)
    {
        child.Opacity = 0;
        // La foto se hace cuando la tarjeta ya está pintada en su sitio nuevo, tras esta pasada de colocación.
        Dispatcher.UIThread.Post(() =>
        {
            // Entre la colocación y la foto se puede haber cambiado de sección: entonces no hay vuelo.
            if (!IsEffectivelyVisible || AdornerLayer.GetAdornerLayer(root) is not { } layer || size.Width < 1 || size.Height < 1)
            {
                child.Opacity = 1;
                return;
            }
            var ghost = new FlightGhost(Snapshot(child, size), size, CardMargin(child), from, to);
            AdornerLayer.SetAdornedElement(ghost, root);
            layer.Children.Add(ghost);
            _flights.Add((ghost, layer, child));
            Tween.Run(MoveDuration, t => ghost.Progress = t, () =>
            {
                _flights.RemoveAll(f => f.Ghost == ghost);
                layer.Children.Remove(ghost);
                child.Opacity = 1;
            });
        }, DispatcherPriority.Background);
    }

    /// <summary>Corta los vuelos en marcha y devuelve sus tarjetas a la vista.</summary>
    /// <remarks>La tarjeta real está oculta mientras vuela su copia: sin esto se quedaría invisible.</remarks>
    private void CancelFlights()
    {
        foreach (var (ghost, layer, child) in _flights)
        {
            layer.Children.Remove(ghost);
            child.Opacity = 1;
        }
        _flights.Clear();
    }

    /// <summary>Margen de la plantilla de la tarjeta: el hueco entre tarjetas, que va dentro de su caja pero no es tarjeta.</summary>
    private static Thickness CardMargin(Control child) =>
        child.GetVisualChildren().OfType<Control>().FirstOrDefault() is { } card ? card.Margin : default;

    /// <summary>Foto de la tarjeta a su tamaño, para la copia que vuela.</summary>
    private static Bitmap Snapshot(Control child, Size size)
    {
        var scale = TopLevel.GetTopLevel(child)?.RenderScaling ?? 1;
        var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale)),
            new Vector(96 * scale, 96 * scale));
        // La tarjeta está oculta mientras espera a la copia: se fotografía visible, sin que llegue a verse.
        var previous = child.Opacity;
        child.Opacity = 1;
        bitmap.Render(child);
        child.Opacity = previous;
        return bitmap;
    }
}

/// <summary>Copia de una tarjeta que vuela de una columna a otra, pintada sobre el tablero.</summary>
public sealed class FlightGhost : Control
{
    private readonly Bitmap _image;
    private readonly Size _size;
    private readonly Thickness _margin;
    private readonly Point _from;
    private readonly Point _to;
    private double _progress;

    /// <summary>Crea la copia con su punto de salida y de llegada, en coordenadas del tablero.</summary>
    /// <param name="margin">Parte transparente de la foto alrededor de la tarjeta, que no lleva sombra.</param>
    public FlightGhost(Bitmap image, Size size, Thickness margin, Point from, Point to)
    {
        _image = image;
        _size = size;
        _margin = margin;
        _from = from;
        _to = to;
        IsHitTestVisible = false;
    }

    /// <summary>Avance del vuelo, de 0 (salida) a 1 (llegada).</summary>
    public double Progress
    {
        get => _progress;
        set
        {
            _progress = value;
            InvalidateVisual();
        }
    }

    /// <summary>Pinta la copia en su punto del vuelo, con una sombra suave solo por fuera de la tarjeta.</summary>
    public override void Render(DrawingContext dc)
    {
        var at = new Point(_from.X + (_to.X - _from.X) * _progress, _from.Y + (_to.Y - _from.Y) * _progress);
        var card = new Rect(at.X + _margin.Left, at.Y + _margin.Top,
            Math.Max(0, _size.Width - _margin.Left - _margin.Right), Math.Max(0, _size.Height - _margin.Top - _margin.Bottom));
        var shadow = new BoxShadows(new BoxShadow { OffsetY = 3, Blur = 8, Color = Color.FromArgb(0x40, 0, 0, 0) });
        dc.DrawRectangle(Brushes.Transparent, null, new RoundedRect(card, 12), shadow);
        dc.DrawImage(_image, new Rect(at, _size));
    }
}
