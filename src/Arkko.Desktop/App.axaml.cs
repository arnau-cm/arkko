using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Arranque de la ventana, con una sola instancia por carpeta de datos.</summary>
/// <remarks>
/// El candado es el mismo que el de la ventana de WPF: las dos leen y escriben los mismos datos y ajustes, y
/// abiertas a la vez avisarían dos veces y se pisarían la configuración.
/// </remarks>
public partial class App : Application
{
    private Mutex? _single;
    private CancellationTokenSource? _showListener;
    private string _scope = "";

    /// <summary>Carga los estilos y recursos de App.axaml.</summary>
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <summary>Abre la ventana, o trae al frente la que ya estaba abierta y sale.</summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        // Una instancia por carpeta de datos: con CLAUDE_STATS_HOME se puede probar otra a la vez.
        _scope = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(AppPaths.Home.ToLowerInvariant())))[..8];
        _single = new Mutex(true, @"Local\ClaudeStats-app-" + _scope, out var first);
        if (!first)
        {
            if (SignalRunning())
            {
                desktop.Shutdown();
                return;
            }
            desktop.MainWindow = new NoticeWindow(
                "Arkko ya está abierta",
                "Hay otra ventana de Arkko abierta con los mismos datos, la de Windows. Ciérrala para abrir esta.");
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            base.OnFrameworkInitializationCompleted();
            return;
        }

        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => MarkDesign(window));
        // Los tooltips, los menús y los desplegables se abren en una capa propia, fuera de la ventana: se marca al cargarse.
        Control.LoadedEvent.AddClassHandler<PopupRoot>((popup, _) => MarkDesign(popup));

        Dispatcher.UIThread.UnhandledException += (_, args) =>
        {
            JsonStore.LogError("app", args.Exception);
            args.Handled = true;
        };

        // El icono elegido, antes de abrir ninguna ventana: lo toman de los recursos de la app.
        AppIcons.Apply(JsonStore.Read<AppSettings>(AppPaths.AppSettingsFile)?.AppIconLight == true);

        // El arranque con el sistema que aún abre el ejecutable de cuando la app se llamaba Agent Pulse pasa a abrir este.
        try
        {
            Autostart.Retarget(AppPaths.OldWindowPath, AppPaths.WindowPath);
        }
        catch (Exception ex)
        {
            JsonStore.LogError("autostart-retarget", ex);
        }

        // La flotante no se da como ventana principal: si la app arranca en el panel, no debe verse. La abre
        // Start, y la app termina cuando se cierra ella, se viera o no.
        var window = new MainWindow();
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        window.Closed += (_, _) => desktop.Shutdown();
        desktop.Exit += (_, _) =>
        {
            _showListener?.Cancel();
            _single?.Dispose();
        };
        ListenForShow(window.BringToFront);
        base.OnFrameworkInitializationCompleted();
        window.Start();
    }

    /// <summary>Nombre del canal por el que otra instancia pide que esta se ponga delante.</summary>
    private string ShowPipe => "Arkko-show-" + _scope;

    /// <summary>Avisa a la instancia de esta ventana que ya está abierta; false si quien tiene el candado es otra app.</summary>
    private bool SignalRunning()
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", ShowPipe, PipeDirection.Out);
            pipe.Connect(500);
            pipe.WriteByte(1);
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException)
        {
            return false;
        }
    }

    /// <summary>Espera, en segundo plano, a que otra instancia pida traer esta al frente.</summary>
    /// <remarks>Un canal con nombre y no un evento con nombre: fuera de Windows, .NET no tiene eventos con nombre.</remarks>
    private void ListenForShow(Action show)
    {
        _showListener = new CancellationTokenSource();
        var token = _showListener.Token;
        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(ShowPipe, PipeDirection.In, 1);
                    await server.WaitForConnectionAsync(token);
                    Dispatcher.UIThread.Post(show);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (IOException ex)
                {
                    JsonStore.LogError("show-pipe", ex);
                    await Task.Delay(1000, token);
                }
            }
        }, token);
    }

    /// <summary>Se dispara tras cambiar de tema, para lo que no se pinta con recursos, como la vista web de Sesiones.</summary>
    public static event Action? ThemeChanged;

    /// <summary>Si el diseño activo es claro; lo cambia <see cref="ApplyDesign"/>.</summary>
    public static bool Light { get; private set; }

    /// <summary>Diseño activo de la app, por su clave: uno de <see cref="PillDesigns.All"/>.</summary>
    public static string Design { get; private set; } = PillDesigns.DefaultKey;

    // Temas propios: heredan del oscuro, que pone todo lo que ellos no cambian, también en los controles del tema.
    public static readonly ThemeVariant SolidVariant = new("Solid", ThemeVariant.Dark);
    public static readonly ThemeVariant BrandVariant = new("Brand", ThemeVariant.Dark);
    public static readonly ThemeVariant TerminalVariant = new("Terminal", ThemeVariant.Dark);

    /// <summary>Tema de un diseño: claro u oscuro para los de cristal, y uno propio para los demás.</summary>
    public static ThemeVariant VariantOf(string design) => design switch
    {
        "glass-light" => ThemeVariant.Light,
        "solid" => SolidVariant,
        "brand" => BrandVariant,
        "terminal" => TerminalVariant,
        _ => ThemeVariant.Dark,
    };

    /// <summary>Aplica un diseño a toda la app: los recursos dinámicos se repintan solos.</summary>
    public static void ApplyDesign(string design)
    {
        Design = PillDesigns.Find(design).Key;
        Light = Design == "glass-light";
        if (Current is not null) Current.RequestedThemeVariant = VariantOf(Design);
        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            foreach (var window in desktop.Windows) MarkDesign(window);
        ThemeChanged?.Invoke();
    }

    /// <summary>Marca una ventana o una capa emergente con la clase «terminal» si ese es el diseño activo, y se la quita si no.</summary>
    /// <remarks>Lo que un recurso no puede cambiar, como el color de la negrita, lo cambian estilos que dependen de esa clase.</remarks>
    private static void MarkDesign(StyledElement root) => root.Classes.Set("terminal", Design == "terminal");
}
