using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Selector de color con el estilo de la app: cuadro de saturación y brillo, barra de tono y el código resultante.</summary>
public partial class ColorPicker : UserControl
{
    private double _hue;
    private double _saturation = 1;
    private double _value = 1;

    /// <summary>Se dispara al pulsar «Usar», con el código «#rrggbb» elegido.</summary>
    public event Action<string>? Chosen;

    /// <summary>Crea el selector, en rojo puro hasta que se le dé otro color.</summary>
    public ColorPicker()
    {
        InitializeComponent();
        // Las marcas se colocan sobre el tamaño real, que no se conoce hasta medir.
        Square.SizeChanged += (_, _) => Update();
        HueBar.SizeChanged += (_, _) => Update();
    }

    /// <summary>Color elegido, como «#rrggbb».</summary>
    public string Hex => HexColor.FromHsv(_hue, _saturation, _value);

    /// <summary>Coloca el selector en un color; si el código no es válido, se queda como estaba.</summary>
    public void SetColor(string? hex)
    {
        if (HexColor.ToHsv(hex) is not { } hsv) return;
        // Un gris no tiene tono: se conserva el de la barra.
        if (hsv.S > 0) _hue = hsv.H;
        _saturation = hsv.S;
        _value = hsv.V;
        Update();
    }

    /// <summary>Pinta el tono del cuadro, las dos marcas y la muestra con su código.</summary>
    private void Update()
    {
        HueFill.Background = Fill(HexColor.FromHsv(_hue, 1, 1));
        Canvas.SetLeft(SquareThumb, _saturation * Square.Bounds.Width - SquareThumb.Width / 2);
        Canvas.SetTop(SquareThumb, (1 - _value) * Square.Bounds.Height - SquareThumb.Height / 2);
        Canvas.SetLeft(HueThumb, _hue / 360 * HueBar.Bounds.Width - HueThumb.Width / 2);
        Preview.Background = Fill(Hex);
        HexText.Text = Hex;
    }

    /// <summary>Pincel de un código «#rrggbb».</summary>
    private static IBrush Fill(string hex) =>
        HexColor.ToRgb(hex) is { } c ? new ImmutableSolidColorBrush(Color.FromRgb(c.R, c.G, c.B)) : Brushes.Transparent;

    /// <summary>Saturación y brillo del punto del cuadro bajo el ratón.</summary>
    private void PickInSquare(Point p)
    {
        _saturation = Math.Clamp(p.X / Square.Bounds.Width, 0, 1);
        _value = 1 - Math.Clamp(p.Y / Square.Bounds.Height, 0, 1);
        Update();
    }

    /// <summary>Tono del punto de la barra bajo el ratón.</summary>
    /// <remarks>Se queda justo por debajo de 360: en 360 el tono vuelve a 0 y la marca saltaría al otro extremo.</remarks>
    private void PickInHue(Point p)
    {
        _hue = Math.Clamp(p.X / HueBar.Bounds.Width, 0, 1) * 359.9;
        Update();
    }

    /// <summary>Empieza a arrastrar por el cuadro y elige ya el punto pulsado.</summary>
    private void Square_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Pointer.Capture(Square);
        PickInSquare(e.GetPosition(Square));
    }

    /// <summary>Sigue el arrastre por el cuadro, aunque el ratón salga de él.</summary>
    private void Square_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (e.Pointer.Captured == Square) PickInSquare(e.GetPosition(Square));
    }

    /// <summary>Empieza a arrastrar por la barra de tono y elige ya el punto pulsado.</summary>
    private void Hue_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Pointer.Capture(HueBar);
        PickInHue(e.GetPosition(HueBar));
    }

    /// <summary>Sigue el arrastre por la barra de tono, aunque el ratón salga de ella.</summary>
    private void Hue_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (e.Pointer.Captured == HueBar) PickInHue(e.GetPosition(HueBar));
    }

    /// <summary>Termina el arrastre, del cuadro o de la barra.</summary>
    private void Drag_PointerReleased(object? sender, PointerReleasedEventArgs e) => e.Pointer.Capture(null);

    private void Use_Click(object? sender, RoutedEventArgs e) => Chosen?.Invoke(Hex);
}
