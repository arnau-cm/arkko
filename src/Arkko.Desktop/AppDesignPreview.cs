using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Arkko.Desktop;

/// <summary>Muestra de un diseño de la app: una ventana en miniatura, con menú, tarjetas, un chip y una barra, en sus colores.</summary>
/// <remarks>
/// Toma los colores del tema de ese diseño y no del activo, así que se ven todas las muestras a la vez, cada una con
/// lo suyo; también su letra, para que la de terminal se distinga.
/// </remarks>
public sealed class AppDesignPreview : Control
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    public static readonly StyledProperty<PillDesign?> DesignProperty = AvaloniaProperty.Register<AppDesignPreview, PillDesign?>(nameof(Design));

    static AppDesignPreview() => AffectsRender<AppDesignPreview>(DesignProperty);

    public PillDesign? Design { get => GetValue(DesignProperty); set => SetValue(DesignProperty, value); }

    /// <summary>Todo el ancho disponible y un alto fijo.</summary>
    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 200 : availableSize.Width, 96);

    /// <summary>Pinta la ventana en miniatura con los recursos del tema del diseño.</summary>
    public override void Render(DrawingContext dc)
    {
        if (Design is not { } design || Bounds.Width < 40) return;
        var variant = App.VariantOf(design.Key);
        IBrush Brush(string key, string fallback) =>
            Application.Current?.TryGetResource(key, variant, out var value) == true && value is IBrush brush ? brush : new SolidColorBrush(Color.Parse(fallback));
        var font = Application.Current?.TryGetResource("UiFont", variant, out var f) == true && f is FontFamily family ? family : FontFamily.Default;
        // Las esquinas, rectas en el de terminal, como en su tema.
        var round = design.Key == "terminal" ? 0 : 1;

        var window = new Rect(Bounds.Size).Deflate(1);
        dc.DrawRectangle(Brush("GlassBase", "#16181F"), new Pen(Brush("GlassEdge", "#33FFFFFF"), 1), window, 8 * round, 8 * round);
        dc.DrawRectangle(Brush("Sheen", "#00FFFFFF"), null, window, 8 * round, 8 * round);

        // Menú lateral, con su opción elegida.
        var menu = new Rect(window.X + 6, window.Y + 6, window.Width * 0.27, window.Height - 12);
        dc.DrawRectangle(Brush("SubNavFill", "#33000000"), null, menu, 5 * round, 5 * round);
        for (var i = 0; i < 4; i++)
        {
            var row = new Rect(menu.X + 4, menu.Y + 16 + i * 14, menu.Width - 8, 10);
            if (i == 1) dc.DrawRectangle(Brush("ChipOn", "#66A3141D"), null, row.Inflate(new Thickness(0, 1)), 3 * round, 3 * round);
            dc.DrawRectangle(Brush(i == 1 ? "Fg" : "FgFaint", "#8A8D9C"), null, new Rect(row.X + 4, row.Center.Y - 1.5, row.Width * (0.5 + 0.1 * (i % 2)), 3), 1.5 * round, 1.5 * round);
        }
        // Sin recortar con PushClip, que deja recortado lo que se pinta después: si el nombre no cabe, sus iniciales.
        FormattedText Name(string text) => new(text, Es, FlowDirection.LeftToRight, new Typeface(font, FontStyle.Normal, FontWeight.SemiBold), 8, Brush("Fg", "#F2F3F7"));
        var name = Name("Arkko");
        if (name.Width > menu.Width - 8) name = Name("AP");
        dc.DrawText(name, new Point(menu.X + 5, menu.Y + 3));

        // Contenido: un título y dos tarjetas, una con un chip y otra con una barra.
        var content = new Rect(menu.Right + 8, window.Y + 8, window.Right - menu.Right - 16, window.Height - 16);
        var title = new FormattedText("Inicio", Es, FlowDirection.LeftToRight, new Typeface(font, FontStyle.Normal, FontWeight.SemiBold), 10, Brush("Fg", "#F2F3F7"));
        dc.DrawText(title, content.TopLeft);
        var cardTop = content.Y + title.Height + 6;
        var card = new Rect(content.X, cardTop, content.Width, (content.Bottom - cardTop - 6) / 2);
        foreach (var (box, i) in new[] { (card, 0), (card.Translate(new Vector(0, card.Height + 6)), 1) })
        {
            dc.DrawRectangle(Brush("LensFill", "#14FFFFFF"), new Pen(Brush("LensEdge", "#26FFFFFF"), 1), box, 5 * round, 5 * round);
            dc.DrawRectangle(Brush("FgMuted", "#B3B6C4"), null, new Rect(box.X + 6, box.Y + 5, box.Width * 0.45, 3), 1.5 * round, 1.5 * round);
            if (i == 0)
            {
                var chip = new Rect(box.Right - 34, box.Center.Y - 4, 28, 9);
                dc.DrawRectangle(Brush("ChipOn", "#66A3141D"), new Pen(Brush("ChipOnEdge", "#C2323C"), 1), chip, 4.5 * round, 4.5 * round);
                dc.DrawRectangle(Brush("FgFaint", "#8A8D9C"), null, new Rect(box.X + 6, box.Y + 11, box.Width * 0.3, 2.5), 1.25 * round, 1.25 * round);
            }
            else
            {
                var track = new Rect(box.X + 6, box.Bottom - 8, box.Width - 12, 4);
                dc.DrawRectangle(Brush("TrackFill", "#1FFFFFFF"), null, track, 2 * round, 2 * round);
                dc.DrawRectangle(Brush("Accent", "#F09A76"), null, track.WithWidth(track.Width * 0.6), 2 * round, 2 * round);
            }
        }
    }
}
