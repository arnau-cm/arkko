using Avalonia;
using Avalonia.Controls;

namespace Arkko.Desktop;

/// <summary>Cómo coloca <see cref="DiaryColumn"/> a un hijo.</summary>
public enum DiaryPlace
{
    /// <summary>De su inicio a su fin, en su carril: un bloque de trabajo.</summary>
    Block,

    /// <summary>A todo el ancho, centrado en su inicio: la línea de una hora o la de ahora.</summary>
    Line,

    /// <summary>Pegado al borde derecho y centrado en su inicio: la marca de un commit.</summary>
    Right,

    /// <summary>Pegado al borde izquierdo y centrado en su inicio: la marca de un cambio de columna.</summary>
    Left,
}

/// <summary>Coloca sus hijos en vertical según los minutos que ocupan, como los eventos de un calendario.</summary>
/// <remarks>
/// Los minutos van de <see cref="From"/> a <see cref="To"/>. Con <see cref="MinuteHeight"/> la columna mide lo que
/// pide; sin él, reparte el alto que le den, que es lo que hace dentro de un bloque para dibujar sus esperas. Los
/// bloques que se solapan van lado a lado: cada uno dice su carril y cuántos hay.
/// </remarks>
public sealed class DiaryColumn : Panel
{
    public static readonly StyledProperty<double> FromProperty = AvaloniaProperty.Register<DiaryColumn, double>(nameof(From));
    public static readonly StyledProperty<double> ToProperty = AvaloniaProperty.Register<DiaryColumn, double>(nameof(To), 60);
    public static readonly StyledProperty<double> MinuteHeightProperty = AvaloniaProperty.Register<DiaryColumn, double>(nameof(MinuteHeight));
    public static readonly StyledProperty<double> MarkGutterProperty = AvaloniaProperty.Register<DiaryColumn, double>(nameof(MarkGutter));
    public static readonly StyledProperty<double> MoveGutterProperty = AvaloniaProperty.Register<DiaryColumn, double>(nameof(MoveGutter));
    public static readonly StyledProperty<bool> HorizontalProperty = AvaloniaProperty.Register<DiaryColumn, bool>(nameof(Horizontal));

    public static readonly AttachedProperty<double> StartProperty = AvaloniaProperty.RegisterAttached<DiaryColumn, Control, double>("Start");
    public static readonly AttachedProperty<double> EndProperty = AvaloniaProperty.RegisterAttached<DiaryColumn, Control, double>("End");
    public static readonly AttachedProperty<int> LaneProperty = AvaloniaProperty.RegisterAttached<DiaryColumn, Control, int>("Lane");
    public static readonly AttachedProperty<int> LanesProperty = AvaloniaProperty.RegisterAttached<DiaryColumn, Control, int>("Lanes", 1);
    public static readonly AttachedProperty<DiaryPlace> PlaceProperty = AvaloniaProperty.RegisterAttached<DiaryColumn, Control, DiaryPlace>("Place");

    public static readonly StyledProperty<double> MinBlockProperty = AvaloniaProperty.Register<DiaryColumn, double>(nameof(MinBlock), 2);

    /// <summary>Alto mínimo de un bloque, para que uno de un minuto se pueda ver, leer y pulsar.</summary>
    public double MinBlock { get => GetValue(MinBlockProperty); set => SetValue(MinBlockProperty, value); }

    // Hueco entre dos bloques que van lado a lado.
    private const double LaneGap = 2;

    static DiaryColumn()
    {
        AffectsMeasure<DiaryColumn>(FromProperty, ToProperty, MinuteHeightProperty, MarkGutterProperty, MoveGutterProperty, HorizontalProperty, MinBlockProperty);
        AffectsParentArrange<DiaryColumn>(StartProperty, EndProperty, LaneProperty, LanesProperty, PlaceProperty);
    }

    public double From { get => GetValue(FromProperty); set => SetValue(FromProperty, value); }
    public double To { get => GetValue(ToProperty); set => SetValue(ToProperty, value); }
    public double MinuteHeight { get => GetValue(MinuteHeightProperty); set => SetValue(MinuteHeightProperty, value); }

    /// <summary>Ancho que los bloques dejan libre a la derecha, para las marcas de los commits.</summary>
    public double MarkGutter { get => GetValue(MarkGutterProperty); set => SetValue(MarkGutterProperty, value); }

    /// <summary>Ancho que los bloques dejan libre a la izquierda, para las marcas de los cambios de columna.</summary>
    public double MoveGutter { get => GetValue(MoveGutterProperty); set => SetValue(MoveGutterProperty, value); }

    /// <summary>Los minutos van a lo ancho, como en una línea de tiempo; solo coloca bloques, a todo el alto.</summary>
    public bool Horizontal { get => GetValue(HorizontalProperty); set => SetValue(HorizontalProperty, value); }

    /// <summary>Pone a un hijo en la columna, de un minuto a otro y en su carril.</summary>
    public static T Put<T>(T child, double start, double end = 0, DiaryPlace place = DiaryPlace.Block, int lane = 0, int lanes = 1) where T : Control
    {
        child.SetValue(StartProperty, start);
        child.SetValue(EndProperty, end);
        child.SetValue(PlaceProperty, place);
        child.SetValue(LaneProperty, lane);
        child.SetValue(LanesProperty, Math.Max(1, lanes));
        return child;
    }

    private double Span => Math.Max(1, To - From);

    /// <inheritdoc />
    protected override Size MeasureOverride(Size available)
    {
        if (Horizontal)
        {
            var across = double.IsInfinity(available.Width) ? 0 : available.Width;
            var tall = double.IsInfinity(available.Height) ? 0 : available.Height;
            foreach (var child in Children)
                child.Measure(new Size(Math.Max(1, (child.GetValue(EndProperty) - child.GetValue(StartProperty)) * across / Span), tall));
            return new Size(across, tall);
        }
        var height = MinuteHeight > 0 ? Span * MinuteHeight : double.IsInfinity(available.Height) ? 0 : available.Height;
        var width = double.IsInfinity(available.Width) ? 0 : available.Width;
        var scale = height / Span;
        foreach (var child in Children)
        {
            if (child.GetValue(PlaceProperty) == DiaryPlace.Block)
            {
                var lanes = child.GetValue(LanesProperty);
                child.Measure(new Size(Math.Max(0, (width - MarkGutter - MoveGutter) / lanes - LaneGap), Math.Max(MinBlock, (child.GetValue(EndProperty) - child.GetValue(StartProperty)) * scale)));
            }
            else child.Measure(new Size(width, double.PositiveInfinity));
        }
        return new Size(width, height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size final)
    {
        if (Horizontal)
        {
            var across = final.Width / Span;
            foreach (var child in Children)
            {
                var x = (child.GetValue(StartProperty) - From) * across;
                child.Arrange(new Rect(x, 0, Math.Max(1, (child.GetValue(EndProperty) - child.GetValue(StartProperty)) * across), final.Height));
            }
            return final;
        }
        var scale = final.Height / Span;
        foreach (var child in Children)
        {
            var y = (child.GetValue(StartProperty) - From) * scale;
            var size = child.DesiredSize;
            switch (child.GetValue(PlaceProperty))
            {
                case DiaryPlace.Block:
                    var lanes = child.GetValue(LanesProperty);
                    var laneWidth = Math.Max(0, final.Width - MarkGutter - MoveGutter) / lanes;
                    var height = Math.Max(MinBlock, (child.GetValue(EndProperty) - child.GetValue(StartProperty)) * scale);
                    child.Arrange(new Rect(MoveGutter + child.GetValue(LaneProperty) * laneWidth, y, Math.Max(0, laneWidth - LaneGap), height));
                    break;
                case DiaryPlace.Line:
                    child.Arrange(new Rect(0, y - size.Height / 2, final.Width, size.Height));
                    break;
                case DiaryPlace.Right:
                    child.Arrange(new Rect(final.Width - size.Width - 2, y - size.Height / 2, size.Width, size.Height));
                    break;
                case DiaryPlace.Left:
                    child.Arrange(new Rect(0, y - size.Height / 2, size.Width, size.Height));
                    break;
            }
        }
        return final;
    }
}
