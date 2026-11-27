using Avalonia;

namespace Arkko.Desktop;

/// <summary>Punto de entrada de la ventana multiplataforma.</summary>
internal static class Program
{
    /// <summary>Arranca la app con el ciclo de vida de escritorio.</summary>
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    /// <summary>Configuración de Avalonia; también la usa el diseñador.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
