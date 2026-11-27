using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Arkko.Desktop;

/// <summary>Ventana del editor de la terminal, para abrirlo desde la flotante.</summary>
public partial class TerminalLayoutWindow : Window
{
    /// <summary>Abre el editor; cada cambio se guarda solo.</summary>
    public TerminalLayoutWindow() => InitializeComponent();

    /// <summary>Cierra la ventana; lo cambiado ya está guardado.</summary>
    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    /// <summary>Arrastra la ventana desde la barra de título.</summary>
    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }
}
