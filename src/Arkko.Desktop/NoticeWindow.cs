using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Arkko.Desktop;

/// <summary>Ventana pequeña con un título, un texto y un botón para cerrarla.</summary>
/// <remarks>Para avisar antes de que haya ventana principal, como cuando otra app ya tiene los datos abiertos.</remarks>
public sealed class NoticeWindow : Window
{
    /// <summary>Crea el aviso con su título y su explicación.</summary>
    public NoticeWindow(string title, string text)
    {
        Title = "Arkko";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = this.FindResource("GlassBase") as IBrush;

        var close = new Button { Content = "Cerrar", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        if (this.FindResource("PrimaryButton") is Avalonia.Styling.ControlTheme theme) close.Theme = theme;
        close.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Children =
            {
                new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 8) },
                new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
                close,
            },
        };
    }
}
