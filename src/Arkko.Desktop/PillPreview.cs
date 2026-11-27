using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Arkko.Desktop;

/// <summary>Muestra de un diseño de la pastilla, con datos de ejemplo, sobre un trozo de fondo de escritorio.</summary>
/// <remarks>
/// También pinta la pastilla en miniatura del dibujo de los monitores, con <see cref="Paint"/>. Las gotas llevan
/// los colores de sus estados en todos los diseños: son información, no adorno.
/// </remarks>
public sealed class PillPreview : Control
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
    private static readonly IBrush[] Drops = [new SolidColorBrush(Color.Parse("#4493F8")), new SolidColorBrush(Color.Parse("#E5C07B")), new SolidColorBrush(Color.Parse("#3FB950"))];
    private static readonly IBrush Danger = new SolidColorBrush(Color.Parse("#E5534B"));

    /// <summary>Fondo de escritorio de las muestras y de las pantallas dibujadas: un degradado azulado claro.</summary>
    public static readonly IBrush Wallpaper = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops = [new GradientStop(Color.Parse("#5E93C9"), 0), new GradientStop(Color.Parse("#8577C2"), 1)],
    };

    public static readonly StyledProperty<PillDesign?> DesignProperty = AvaloniaProperty.Register<PillPreview, PillDesign?>(nameof(Design));

    static PillPreview() => AffectsRender<PillPreview>(DesignProperty);

    public PillDesign? Design { get => GetValue(DesignProperty); set => SetValue(DesignProperty, value); }

    /// <summary>Todo el ancho disponible y un alto fijo, con sitio alrededor de la pastilla.</summary>
    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 220 : availableSize.Width, 64);

    /// <summary>Pinta el fondo de escritorio y, centrada, la pastilla a su tamaño real.</summary>
    public override void Render(DrawingContext dc)
    {
        var area = new Rect(Bounds.Size);
        dc.DrawRectangle(Wallpaper, null, area, 8, 8);
        if (Design is not { } design) return;
        // Si no cabe entera, sin el límite; y si tampoco, más pequeña.
        var room = area.Width - 16;
        var limit = PillWidth(design, 38) <= room;
        var height = 38 * Math.Min(1, room / PillWidth(design, 38, limit));
        var width = PillWidth(design, height, limit);
        Paint(dc, new Rect((area.Width - width) / 2, (area.Height - height) / 2, width, height), design, detailed: true, limit: limit);
    }

    /// <summary>Ancho de la pastilla de un diseño a un alto, con su contenido de ejemplo; con o sin el límite.</summary>
    public static double PillWidth(PillDesign d, double height, bool limit = true)
    {
        var k = height / 38;
        var width = 24 + (d.ShowLogo ? 26 : 0) + (d.HasPrompt ? 34 : 0) + 3 * 14 + 4 + 26;
        if (d.ShowLimit && limit) width += 8 + 22 + 54 + 8 + 30;
        return width * k;
    }

    /// <summary>Pinta la pastilla de un diseño en un rectángulo; en detalle, con el límite (si se pide) y el contador.</summary>
    public static void Paint(DrawingContext dc, Rect box, PillDesign d, bool detailed, double edge = 1, bool limit = true)
    {
        var k = box.Height / 38;
        var radius = Math.Min(d.Radius * k, box.Height / 2);
        dc.DrawRectangle(d.Back, new Pen(d.Edge, edge), box, radius, radius);
        using var clip = dc.PushClip(box);
        var x = box.Left + 12 * k;
        var cy = box.Center.Y;
        if (d.ShowLogo && AppIcons.Logo is { } logo)
        {
            dc.DrawImage(logo, new Rect(x, cy - 9 * k, 18 * k, 18 * k));
            x += 26 * k;
        }
        if (d.Prompt is { } prompt)
        {
            if (detailed || box.Height >= 14)
            {
                var text = Text(prompt, d, 13 * k, FontWeight.Bold, d.Text);
                dc.DrawText(text, new Point(x, cy - text.Height / 2));
                x += text.Width + 8 * k;
            }
            else
            {
                dc.DrawRectangle(d.Text, null, new Rect(x, cy - 2 * k * 2, 8 * k, 4 * k * 2));
                x += 12 * k;
            }
        }
        foreach (var drop in Drops)
        {
            if (x + 10 * k > box.Right - 8 * k) break;
            dc.DrawEllipse(drop, null, new Point(x + 5 * k, cy), 5 * k, 5 * k);
            x += 14 * k;
        }
        if (!detailed) return;
        if (d.ShowLimit && limit)
        {
            x += 8 * k;
            var label = Text("5 h", d, 11 * k, FontWeight.Normal, d.Muted);
            dc.DrawText(label, new Point(x, cy - label.Height / 2));
            x += label.Width + 8 * k;
            var bar = new Rect(x, cy - 4 * k, 54 * k, 8 * k);
            dc.DrawRectangle(d.Track, null, bar, 4 * k, 4 * k);
            dc.DrawRectangle(d.Mono ? d.Text : Drops[2], null, bar.WithWidth(bar.Width * 0.42), 4 * k, 4 * k);
            x = bar.Right + 8 * k;
            var pct = Text("42 %", d, 11 * k, FontWeight.SemiBold, d.Text);
            dc.DrawText(pct, new Point(x, cy - pct.Height / 2));
            x += pct.Width + 8 * k;
        }
        var count = Text("2", d, 11 * k, FontWeight.Bold, Brushes.White);
        var badge = new Rect(x, cy - 9 * k, Math.Max(18 * k, count.Width + 8 * k), 18 * k);
        if (badge.Right > box.Right - 4 * k) return;
        dc.DrawRectangle(Danger, null, badge, 9 * k, 9 * k);
        dc.DrawText(count, new Point(badge.Center.X - count.Width / 2, cy - count.Height / 2));
    }

    /// <summary>Texto con la letra del diseño.</summary>
    private static FormattedText Text(string text, PillDesign d, double size, FontWeight weight, IBrush brush) =>
        new(text, Es, FlowDirection.LeftToRight, new Typeface(d.Font, FontStyle.Normal, weight), Math.Max(1, size), brush);
}
