using Avalonia;
using Avalonia.Controls;

namespace Arkko.Desktop;

/// <summary>La superficie de un corcho: coloca cada uno de sus hijos en el rectángulo que le toca.</summary>
/// <remarks>
/// Los paneles de un corcho son hijos de esta superficie todo el tiempo que están en él, aunque se partan o se junten
/// recuadros: solo cambia dónde va cada uno. Si se anidaran en contenedores, cada cambio de forma los sacaría y
/// volvería a meter, y sus páginas se recargarían. Un hijo sin rectángulo no se ve.
/// Un cambio de sitio puede ir animado: cada hijo va de donde se ve a donde le toca en un momento, frenando al final; lo
/// que aparece de nuevo sale ya en su sitio.
/// </remarks>
internal sealed class CorkBoard : Panel
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(180);

    private Dictionary<Control, Rect> _places = new();

    // Dónde se ve ahora cada hijo; y, mientras dura una animación, de dónde salió y cuánto lleva.
    private readonly Dictionary<Control, Rect> _shown = new();
    private Dictionary<Control, Rect>? _from;
    private TimeSpan? _start;
    private double _progress = 1;

    /// <summary>Pone dónde va cada hijo y vuelve a colocarlos, de golpe o animado.</summary>
    public void Place(Dictionary<Control, Rect> places, bool animate = false)
    {
        _places = places;
        if (animate && _shown.Count > 0)
        {
            _from = new Dictionary<Control, Rect>(_shown);
            _start = null;
            _progress = 0;
            NextFrame();
        }
        else if (_from is null) _progress = 1;
        InvalidateMeasure();
        InvalidateArrange();
    }

    /// <summary>Pide el siguiente paso de la animación para cuando se vaya a pintar.</summary>
    private void NextFrame() => TopLevel.GetTopLevel(this)?.RequestAnimationFrame(time =>
    {
        if (_from is null) return;
        _start ??= time;
        _progress = Math.Min(1, (time - _start.Value) / Duration);
        if (_progress >= 1) _from = null;
        else NextFrame();
        InvalidateMeasure();
        InvalidateArrange();
    });

    /// <summary>Dónde se ve un hijo en este momento de la animación: entre de dónde salió y a dónde va.</summary>
    private Rect Current(Control child)
    {
        var to = _places.TryGetValue(child, out var place) ? place : default;
        if (_from is null || !_from.TryGetValue(child, out var from) || from.Width <= 0 || to.Width <= 0) return to;
        var t = 1 - Math.Pow(1 - _progress, 3);
        return new Rect(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t,
            from.Width + (to.Width - from.Width) * t, from.Height + (to.Height - from.Height) * t);
    }

    /// <summary>Mide cada hijo con el tamaño de su rectángulo.</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(Current(child).Size);
        return availableSize;
    }

    /// <summary>Coloca cada hijo en su rectángulo; el que no tiene, sin tamaño.</summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        _shown.Clear();
        foreach (var child in Children)
        {
            var rect = Current(child);
            _shown[child] = rect;
            child.Arrange(rect);
        }
        return finalSize;
    }
}
