using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Arkko.Desktop;

/// <summary>Un tramo de una barra apilada: su valor y su color.</summary>
public sealed record BarSegment(double Value, IBrush Brush);

/// <summary>Una barra del histórico: etiqueta del eje, tramos por modelo y su ficha.</summary>
/// <param name="Max">Valor que llena la barra entera: el máximo del eje, igual en todas.</param>
public sealed record BarModel(string Label, IReadOnlyList<BarSegment> Segments, double Max, BarTip Tip);

/// <summary>Ficha de una barra: a qué periodo corresponde y los tokens de cada modelo.</summary>
public sealed record BarTip(string Title, IReadOnlyList<TipRow> Rows, string Total);

/// <summary>Una fila de una ficha: nombre, valor y el color de su serie.</summary>
public sealed record TipRow(string Name, string Value, IBrush Brush);

/// <summary>Una entrada de la leyenda de una gráfica.</summary>
public sealed record LegendItem(string Name, IBrush Brush);

/// <summary>Barra apilada de abajo arriba, a escala del máximo común del eje.</summary>
/// <remarks>Se dibuja a mano en vez de con una librería de gráficas: es una forma sencilla y cada DLL nueva es una evaluación más de Smart App Control.</remarks>
public sealed class StackedBar : Control
{
    public static readonly StyledProperty<BarModel?> BarProperty = AvaloniaProperty.Register<StackedBar, BarModel?>(nameof(Bar));

    static StackedBar() => AffectsRender<StackedBar>(BarProperty);

    public BarModel? Bar
    {
        get => GetValue(BarProperty);
        set => SetValue(BarProperty, value);
    }

    /// <summary>Pinta los tramos uno encima de otro; el de arriba, con las esquinas superiores redondeadas.</summary>
    public override void Render(DrawingContext dc)
    {
        if (Bar is not { Max: > 0 } bar) return;
        var w = Bounds.Width;
        var h = Bounds.Height;
        var width = Math.Max(2, Math.Min(w - 4, 28));
        var left = (w - width) / 2;
        var bottom = h;
        var segments = bar.Segments.Where(s => s.Value > 0).ToList();
        for (var i = 0; i < segments.Count; i++)
        {
            var height = h * segments[i].Value / bar.Max;
            if (height < 0.5) continue;
            var rect = new Rect(left, bottom - height, width, height);
            if (i == segments.Count - 1)
            {
                // Solo el tramo de arriba se redondea: las esquinas de abajo quedan rectas, pegadas al eje.
                var radius = Math.Min(3, Math.Min(width / 2, height));
                var geometry = new StreamGeometry();
                using (var g = geometry.Open())
                {
                    g.BeginFigure(new Point(rect.Left, rect.Bottom), true);
                    g.LineTo(new Point(rect.Left, rect.Top + radius));
                    g.ArcTo(new Point(rect.Left + radius, rect.Top), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
                    g.LineTo(new Point(rect.Right - radius, rect.Top));
                    g.ArcTo(new Point(rect.Right, rect.Top + radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
                    g.LineTo(new Point(rect.Right, rect.Bottom));
                    g.EndFigure(true);
                }
                dc.DrawGeometry(segments[i].Brush, null, geometry);
            }
            else
            {
                dc.DrawRectangle(segments[i].Brush, null, rect);
            }
            bottom -= height;
        }
    }
}

/// <summary>Mini gráfica de línea con su área degradada debajo, a escala de su propio máximo.</summary>
/// <remarks>Un valor null es un hueco: la línea se corta ahí, como en la caché de una barra sin uso.</remarks>
public sealed class Sparkline : Control
{
    public static readonly StyledProperty<bool> ShowEndProperty = AvaloniaProperty.Register<Sparkline, bool>(nameof(ShowEnd));

    public static readonly StyledProperty<IReadOnlyList<double?>?> ValuesProperty =
        AvaloniaProperty.Register<Sparkline, IReadOnlyList<double?>?>(nameof(Values));

    public static readonly StyledProperty<IBrush> StrokeProperty =
        AvaloniaProperty.Register<Sparkline, IBrush>(nameof(Stroke), Brushes.Gray);

    static Sparkline() => AffectsRender<Sparkline>(ValuesProperty, StrokeProperty, ShowEndProperty);

    /// <summary>Si se marca el último punto con un círculo: el valor de ahora.</summary>
    public bool ShowEnd
    {
        get => GetValue(ShowEndProperty);
        set => SetValue(ShowEndProperty, value);
    }

    public IReadOnlyList<double?>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public IBrush Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>Pinta cada tramo seguido de valores como una línea con su área; con un solo valor, un punto.</summary>
    public override void Render(DrawingContext dc)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (Values is not { Count: > 0 } values || w <= 0 || h <= 0) return;
        var max = values.Max(v => v ?? 0);
        if (max <= 0) max = 1;
        const double pad = 2;
        var height = h - pad * 2;
        var step = values.Count > 1 ? (w - pad * 2) / (values.Count - 1) : 0;
        Point At(int i) => new(pad + step * i, pad + height - height * (values[i] ?? 0) / max);

        var color = Stroke is ISolidColorBrush solid ? solid.Color : Colors.Gray;
        var fill = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(80, color.R, color.G, color.B), 0), new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1) },
        };
        var pen = new Pen(Stroke, 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

        var i = 0;
        while (i < values.Count)
        {
            if (values[i] is null) { i++; continue; }
            var start = i;
            while (i + 1 < values.Count && values[i + 1] is not null) i++;
            if (start == i)
            {
                dc.DrawEllipse(Stroke, null, At(i), 1.6, 1.6);
            }
            else
            {
                var line = new StreamGeometry();
                var area = new StreamGeometry();
                using (var l = line.Open())
                using (var a = area.Open())
                {
                    l.BeginFigure(At(start), false);
                    a.BeginFigure(new Point(At(start).X, h), true);
                    a.LineTo(At(start));
                    for (var k = start + 1; k <= i; k++)
                    {
                        l.LineTo(At(k));
                        a.LineTo(At(k));
                    }
                    a.LineTo(new Point(At(i).X, h));
                    l.EndFigure(false);
                    a.EndFigure(true);
                }
                dc.DrawGeometry(fill, null, area);
                dc.DrawGeometry(null, pen, line);
            }
            i++;
        }
        if (!ShowEnd) return;
        for (var last = values.Count - 1; last >= 0; last--)
        {
            if (values[last] is null) continue;
            dc.DrawEllipse(Stroke, null, At(last), 2.6, 2.6);
            break;
        }
    }
}

/// <summary>Barra horizontal repartida en tramos proporcionales, redondeados y separados por un hueco.</summary>
public sealed class ShareBar : Control
{
    public static readonly StyledProperty<IReadOnlyList<BarSegment>?> SlicesProperty =
        AvaloniaProperty.Register<ShareBar, IReadOnlyList<BarSegment>?>(nameof(Slices));

    static ShareBar() => AffectsRender<ShareBar>(SlicesProperty);

    public IReadOnlyList<BarSegment>? Slices
    {
        get => GetValue(SlicesProperty);
        set => SetValue(SlicesProperty, value);
    }

    /// <summary>Pinta los tramos de izquierda a derecha; uno muy pequeño conserva un mínimo para que se vea.</summary>
    public override void Render(DrawingContext dc)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (Slices is not { } all || w <= 0) return;
        var slices = all.Where(s => s.Value > 0).ToList();
        var total = slices.Sum(s => s.Value);
        if (total <= 0) return;
        const double gap = 3, min = 4;
        var usable = w - gap * (slices.Count - 1);
        var left = 0.0;
        var radius = Math.Min(4, h / 2);
        foreach (var s in slices)
        {
            var width = Math.Max(min, usable * s.Value / total);
            width = Math.Min(width, w - left);
            if (width <= 0) break;
            dc.DrawRectangle(s.Brush, null, new Rect(left, 0, width, h), radius, radius);
            left += width + gap;
        }
    }
}

/// <summary>Fondo redondeado que se rellena desde la izquierda hasta una fracción del ancho.</summary>
/// <remarks>El fondo de las filas de «Hoy por modelo»: la parte de cada modelo frente al que más gasta.</remarks>
public sealed class FractionFill : Control
{
    public static readonly StyledProperty<double> FractionProperty = AvaloniaProperty.Register<FractionFill, double>(nameof(Fraction));
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<FractionFill, IBrush?>(nameof(Fill));
    public static readonly StyledProperty<double> RadiusProperty = AvaloniaProperty.Register<FractionFill, double>(nameof(Radius), 8);
    public static readonly StyledProperty<bool> FromRightProperty = AvaloniaProperty.Register<FractionFill, bool>(nameof(FromRight));

    static FractionFill() => AffectsRender<FractionFill>(FractionProperty, FillProperty, RadiusProperty, FromRightProperty);

    public double Fraction { get => GetValue(FractionProperty); set => SetValue(FractionProperty, value); }
    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public double Radius { get => GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }

    /// <summary>Se rellena desde la derecha, como la barra de calor de «Prompts más caros».</summary>
    public bool FromRight { get => GetValue(FromRightProperty); set => SetValue(FromRightProperty, value); }

    /// <summary>Pinta el tramo relleno.</summary>
    public override void Render(DrawingContext dc)
    {
        var width = Bounds.Width * Math.Clamp(Fraction, 0, 1);
        if (Fill is null || width <= 0) return;
        var left = FromRight ? Bounds.Width - width : 0;
        dc.DrawRectangle(Fill, null, new Rect(left, 0, width, Bounds.Height), Radius, Radius);
    }
}

/// <summary>Colores de los modelos en las gráficas, por familia, tomados del tema activo.</summary>
public static class ModelColors
{
    /// <summary>Pincel de un modelo según su familia: Opus, Sonnet, Haiku, Fable u otro; con opacidad, su versión tenue.</summary>
    /// <remarks>Vale con el id («claude-opus-5-5») o con el nombre («Opus 5.5»): los dos dan la misma familia.</remarks>
    public static IBrush For(string model, double opacity = 1)
    {
        var (key, fallback) = Arkko.Core.Format.ModelName(model).Split(' ')[0].ToLowerInvariant() switch
        {
            "opus" => ("DataOpus", "#F09A76"),
            "sonnet" => ("DataSonnet", "#61AFEF"),
            "haiku" => ("DataHaiku", "#98C379"),
            "fable" => ("DataFable", "#C678DD"),
            _ => ("DataOther", "#ABB2BF"),
        };
        return opacity < 1 ? ThemeColors.Of(key, fallback, opacity) : ThemeColors.Of(key, fallback);
    }
}

/// <summary>Color de un proyecto en las gráficas: siempre el mismo para el mismo nombre, sea cual sea el periodo.</summary>
public static class ProjectColors
{
    private static readonly (string Key, string Fallback)[] Palette =
    [
        ("DataOpus", "#F09A76"),
        ("DataSonnet", "#61AFEF"),
        ("DataHaiku", "#98C379"),
        ("DataFable", "#C678DD"),
        ("DataCache", "#56B6C2"),
        ("DataFiveHour", "#E5C07B"),
    ];

    /// <summary>Pincel de un proyecto, elegido por una huella estable de su nombre.</summary>
    /// <remarks>string.GetHashCode cambia en cada arranque: el color saltaría de un día para otro.</remarks>
    public static IBrush For(string project, double opacity = 1)
    {
        uint hash = 2166136261;
        foreach (var c in project.ToLowerInvariant()) hash = (hash ^ c) * 16777619;
        var (key, fallback) = Palette[hash % (uint)Palette.Length];
        return opacity < 1 ? ThemeColors.Of(key, fallback, opacity) : ThemeColors.Of(key, fallback);
    }

    /// <summary>Pincel del grupo «Otros», neutro para que no se confunda con un proyecto.</summary>
    public static IBrush Others(double opacity = 1) =>
        opacity < 1 ? ThemeColors.Of("DataOther", "#ABB2BF", opacity) : ThemeColors.Of("DataOther", "#ABB2BF");
}

/// <summary>Progreso circular: una pista completa y un arco proporcional al valor, de 0 a 100.</summary>
public sealed class RingProgress : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<RingProgress, double>(nameof(Value));
    public static readonly StyledProperty<IBrush> StrokeProperty = AvaloniaProperty.Register<RingProgress, IBrush>(nameof(Stroke), Brushes.White);
    public static readonly StyledProperty<IBrush> TrackProperty =
        AvaloniaProperty.Register<RingProgress, IBrush>(nameof(Track), new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)));
    public static readonly StyledProperty<double> ThicknessProperty = AvaloniaProperty.Register<RingProgress, double>(nameof(Thickness), 3.5);

    static RingProgress() => AffectsRender<RingProgress>(ValueProperty, StrokeProperty, TrackProperty, ThicknessProperty);

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public IBrush Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public IBrush Track { get => GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public double Thickness { get => GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }

    /// <summary>Dibuja la pista y el arco, empezando arriba y en sentido horario.</summary>
    public override void Render(DrawingContext dc)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= Thickness * 2) return;

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = (size - Thickness) / 2;
        dc.DrawEllipse(null, new Pen(Track, Thickness), center, radius, radius);

        var fraction = Math.Clamp(Value, 0, 100) / 100;
        if (fraction <= 0) return;

        var pen = new Pen(Stroke, Thickness, lineCap: PenLineCap.Round);

        // Un arco no puede cerrar una circunferencia: al 100 % se dibuja la elipse entera.
        if (fraction >= 0.999)
        {
            dc.DrawEllipse(null, pen, center, radius, radius);
            return;
        }

        var angle = fraction * 2 * Math.PI;
        var start = new Point(center.X, center.Y - radius);
        var end = new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(start, false);
            ctx.ArcTo(end, new Size(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }
        dc.DrawGeometry(null, pen, geometry);
    }
}
