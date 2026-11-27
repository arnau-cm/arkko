using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Media;

namespace Arkko.Desktop;

/// <summary>Una serie de la gráfica de líneas: nombre, color y sus puntos en el tiempo.</summary>
public sealed record LineSeries(string Name, IBrush Brush, IReadOnlyList<(DateTime Time, double Value)> Points);

/// <summary>Gráfica de líneas en escalón de 0 a 100 %, para la evolución de los límites.</summary>
/// <remarks>
/// En escalón porque cada foto vale hasta la siguiente: el límite no sube en línea recta entre dos fotos, sino
/// de golpe al llegar el dato nuevo. Al pasar el ratón, una línea vertical marca el instante y la ficha da el
/// valor de cada serie en ese momento.
/// </remarks>
public sealed class StepChart : Control
{
    private const double AxisWidth = 40;
    private const double LabelsHeight = 18;
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
    private readonly ContentControl _tip = new();
    private double? _hoverX;

    public static readonly StyledProperty<IReadOnlyList<LineSeries>?> SeriesProperty = AvaloniaProperty.Register<StepChart, IReadOnlyList<LineSeries>?>(nameof(Series));
    public static readonly StyledProperty<DateTime> FromProperty = AvaloniaProperty.Register<StepChart, DateTime>(nameof(From));
    public static readonly StyledProperty<DateTime> ToProperty = AvaloniaProperty.Register<StepChart, DateTime>(nameof(To));
    public static readonly StyledProperty<IBrush> GridBrushProperty = AvaloniaProperty.Register<StepChart, IBrush>(nameof(GridBrush), Brushes.Gray);
    public static readonly StyledProperty<IBrush> LabelBrushProperty = AvaloniaProperty.Register<StepChart, IBrush>(nameof(LabelBrush), Brushes.Gray);

    static StepChart() => AffectsRender<StepChart>(SeriesProperty, FromProperty, ToProperty, GridBrushProperty, LabelBrushProperty);

    public IReadOnlyList<LineSeries>? Series { get => GetValue(SeriesProperty); set => SetValue(SeriesProperty, value); }
    public DateTime From { get => GetValue(FromProperty); set => SetValue(FromProperty, value); }
    public DateTime To { get => GetValue(ToProperty); set => SetValue(ToProperty, value); }
    public IBrush GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public IBrush LabelBrush { get => GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }

    /// <summary>Prepara la ficha, con la plantilla de las barras del panel.</summary>
    public StepChart()
    {
        AttachedToVisualTree += (_, _) => _tip.ContentTemplate = this.FindResource("BarTipTemplate") as IDataTemplate;
        ToolTip.SetTip(this, _tip);
        ToolTip.SetPlacement(this, PlacementMode.Pointer);
        ToolTip.SetShowDelay(this, 0);
    }

    private Rect Plot => new(AxisWidth, 6, Math.Max(0, Bounds.Width - AxisWidth - 4), Math.Max(0, Bounds.Height - LabelsHeight - 6));

    /// <summary>Posición horizontal de un instante.</summary>
    private double X(DateTime t)
    {
        var span = (To - From).TotalSeconds;
        return span <= 0 ? Plot.Left : Plot.Left + Plot.Width * Math.Clamp((t - From).TotalSeconds / span, 0, 1);
    }

    /// <summary>Posición vertical de un porcentaje.</summary>
    private double Y(double pct) => Plot.Bottom - Plot.Height * Math.Clamp(pct, 0, 100) / 100;

    /// <summary>Pinta las guías, las etiquetas de los ejes, las series y la marca del ratón.</summary>
    public override void Render(DrawingContext dc)
    {
        // Fondo transparente: así la gráfica recibe el ratón en toda su superficie, no solo sobre las líneas.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        if (Plot.Width <= 0 || Plot.Height <= 0 || To <= From) return;

        var grid = new Pen(GridBrush, 1);
        foreach (var pct in new[] { 0, 50, 100 })
        {
            dc.DrawLine(grid, new Point(Plot.Left, Y(pct)), new Point(Plot.Right, Y(pct)));
            DrawText(dc, $"{pct} %", new Point(0, Y(pct) - 7), TextAlignment.Left);
        }
        var oneDay = (To - From).TotalHours <= 26;
        for (var i = 0; i <= 4; i++)
        {
            var t = From + TimeSpan.FromTicks((To - From).Ticks * i / 4);
            var text = oneDay ? t.ToString("HH:mm", Es) : t.ToString("d MMM", Es).Replace(".", "");
            var align = i == 0 ? TextAlignment.Left : i == 4 ? TextAlignment.Right : TextAlignment.Center;
            DrawText(dc, text, new Point(X(t), Plot.Bottom + 3), align);
        }

        foreach (var s in Series ?? [])
        {
            if (s.Points.Count == 0) continue;
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(new Point(X(s.Points[0].Time), Y(s.Points[0].Value)), false);
                for (var i = 1; i < s.Points.Count; i++)
                {
                    g.LineTo(new Point(X(s.Points[i].Time), Y(s.Points[i - 1].Value)));
                    g.LineTo(new Point(X(s.Points[i].Time), Y(s.Points[i].Value)));
                }
                g.LineTo(new Point(X(To), Y(s.Points[^1].Value)));
                g.EndFigure(false);
            }
            dc.DrawGeometry(null, new Pen(s.Brush, 2, lineJoin: PenLineJoin.Round), geometry);
        }

        if (_hoverX is { } x)
            dc.DrawLine(new Pen(LabelBrush, 1, DashStyle.Dash), new Point(x, Plot.Top), new Point(x, Plot.Bottom));
    }

    /// <summary>Escribe una etiqueta de eje, alineada por su borde izquierdo, su centro o su borde derecho.</summary>
    private void DrawText(DrawingContext dc, string text, Point at, TextAlignment align)
    {
        var ft = new FormattedText(text, Es, FlowDirection.LeftToRight, new Typeface(ThemeColors.Font), 10, LabelBrush);
        var x = align switch
        {
            TextAlignment.Center => at.X - ft.Width / 2,
            TextAlignment.Right => at.X - ft.Width,
            _ => at.X,
        };
        dc.DrawText(ft, new Point(x, at.Y));
    }

    /// <summary>Mueve la marca y la ficha con el ratón, con el valor de cada serie en ese instante.</summary>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (Series is not { Count: > 0 } series || p.X < Plot.Left || p.X > Plot.Right || To <= From)
        {
            HideHover();
            return;
        }
        var t = From + TimeSpan.FromTicks((long)((To - From).Ticks * (p.X - Plot.Left) / Plot.Width));
        var rows = new List<TipRow>();
        foreach (var s in series)
        {
            var at = s.Points.Where(pt => pt.Time <= t).ToList();
            if (at.Count > 0) rows.Add(new TipRow(s.Name, $"{Math.Round(at[^1].Value)} %", s.Brush));
        }
        _hoverX = p.X;
        InvalidateVisual();
        _tip.Content = new BarTip(t.ToString("dddd d 'de' MMMM, HH:mm", Es), rows,
            rows.Count == 0 ? "Sin datos todavía" : "Uso de cada límite en ese momento");
        ToolTip.SetIsOpen(this, true);
    }

    /// <summary>Quita la marca y la ficha al salir de la gráfica.</summary>
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        HideHover();
    }

    /// <summary>Oculta la marca del ratón y la ficha.</summary>
    private void HideHover()
    {
        ToolTip.SetIsOpen(this, false);
        if (_hoverX is null) return;
        _hoverX = null;
        InvalidateVisual();
    }
}
