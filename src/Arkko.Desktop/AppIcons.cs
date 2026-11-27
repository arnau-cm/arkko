using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>El icono de la app, la tecla oscura o la clara: el de las ventanas, el de la bandeja y el logo de dentro.</summary>
/// <remarks>
/// Las ventanas y los logos lo toman de los recursos de la app (<c>AppWindowIcon</c> y <c>AppLogo</c>), así que cambiarlo
/// los cambia al momento. El que lleva dentro el ejecutable (el Explorador, el menú Inicio y el botón anclado a la barra
/// de tareas) se fija al compilar y es siempre el oscuro. Las notificaciones llevan otro, solo la letra, que sigue el modo
/// de Windows y no este ajuste (<see cref="ModernToasts"/>).
/// </remarks>
internal static class AppIcons
{
    private static bool? _light;

    /// <summary>Si ahora es la tecla clara.</summary>
    public static bool Light => _light == true;

    /// <summary>El logo de la app, para pintarlo a mano (la bandeja, las vistas previas de la pastilla).</summary>
    public static Bitmap? Logo { get; private set; }

    /// <summary>Avisa al cambiar de tecla, para lo que se pinta a mano.</summary>
    public static event Action? Changed;

    /// <summary>Pone la tecla elegida en los recursos de la app y avisa; si ya era esa, no hace nada.</summary>
    public static void Apply(bool light)
    {
        if (_light == light || Application.Current is not { } app) return;
        _light = light;
        var suffix = light ? "-claro" : "";
        try
        {
            using (var ico = AssetLoader.Open(new Uri($"avares://Arkko/Assets/arkko{suffix}.ico")))
                app.Resources["AppWindowIcon"] = new WindowIcon(ico);
            using (var png = AssetLoader.Open(new Uri($"avares://Arkko/Assets/arkko{suffix}-48.png")))
                Logo = new Bitmap(png);
            app.Resources["AppLogo"] = Logo;
        }
        catch (Exception ex)
        {
            JsonStore.LogError("app-icon", ex);
        }
        Changed?.Invoke();
    }
}
