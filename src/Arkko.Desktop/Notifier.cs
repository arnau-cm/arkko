using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Icono de la bandeja y avisos: notificación del sistema y sonido.</summary>
/// <remarks>
/// Los textos son los de la ventana de WPF. Si el sistema no deja enseñar la notificación, suena el aviso y el
/// contador de la pastilla sigue funcionando.
/// </remarks>
public sealed class Notifier : IDisposable
{
    private readonly TrayIcon _tray;
    private Bitmap? _appIcon;
    private SessionStatus? _iconState;
    private bool _iconSet;

    /// <summary>Crea el icono de la bandeja asociado a la ventana.</summary>
    public Notifier(Action onOpen, Action onExit)
    {
        _appIcon = AppIcons.Logo;
        // Al cambiar de tecla, el icono se vuelve a pintar con la nueva, con el punto que llevara.
        AppIcons.Changed += () =>
        {
            _appIcon = AppIcons.Logo;
            _iconSet = false;
            SetState(_iconState);
        };

        var show = new NativeMenuItem("Mostrar");
        show.Click += (_, _) => onOpen();
        var exit = new NativeMenuItem("Salir");
        exit.Click += (_, _) => onExit();
        _tray = new TrayIcon { ToolTipText = "Arkko", Menu = new NativeMenu { show, exit }, IsVisible = true };
        _tray.Clicked += (_, _) => onOpen();
        if (Application.Current is { } app) TrayIcon.SetIcons(app, [_tray]);
        SetState(null);
    }

    /// <summary>Avisa de que una sesión ha terminado o espera respuesta.</summary>
    public void Alert(SessionState session, bool toast, bool sound)
    {
        // Punto de color del estado, como en la tarjeta: ámbar si te espera, verde si ha terminado.
        var title = session.Status == SessionStatus.Waiting
            ? $"🟡 {session.Title ?? "Claude"}: espera tu respuesta"
            : $"🟢 {session.Title ?? "Claude"}: ha terminado";
        var text = session.Detail ?? (session.LastTurn is { } turn && session.Status == SessionStatus.Done
            ? TurnText(turn)
            : "Te toca");
        // Lo que dejó en marcha no cambia que haya terminado, pero se dice: puede ser un servidor que ya no hace falta.
        if (session.Status == SessionStatus.Done && session.Commands is [_, ..] commands)
            text += commands.Count == 1 ? $"\nDeja 1 comando en marcha: {commands[0].Description}" : $"\nDeja {commands.Count} comandos en marcha";

        Notify(session.SessionId, title, text, toast, sound);
    }

    /// <summary>Duración y tokens nuevos del prompt, con la lectura de caché aparte: «2m33s · 20,6k tokens (+4,6M de caché)».</summary>
    private static string TurnText(TurnUsage turn)
    {
        var t = turn.Totals();
        var text = $"{Format.Duration(TimeSpan.FromMilliseconds(turn.DurationMs))} · {Format.Tokens(t.Input + t.Output + t.CacheWrite)} tokens";
        return t.CacheRead > 0 ? $"{text} (+{Format.Tokens(t.CacheRead)} de caché)" : text;
    }

    /// <summary>Recuerda, una sola vez, que una sesión terminada lleva rato sin revisar.</summary>
    public void RemindReview(SessionState session, TimeSpan after, bool toast, bool sound)
    {
        Notify(session.SessionId, $"🔴 {session.Title ?? "Claude"}: sin revisar",
            $"Terminó hace {(int)after.TotalMinutes} min y aún no la has mirado.", toast, sound);
    }

    /// <summary>Avisa de que en una sesión se acaba de enviar un secreto: tipo y origen, nunca el valor.</summary>
    /// <remarks>Va con su propia etiqueta, para no sustituir la notificación de estado de la misma sesión.</remarks>
    public void SecretAlert(SessionState session, bool toast, bool sound)
    {
        Notify("secret-" + session.SessionId, $"🔴 {session.Title ?? "Claude"}: secreto enviado",
            (session.SecretAlert ?? "Un secreto") + ". Revísalo en Seguridad.", toast, sound);
    }

    /// <summary>Avisa de que los hooks de Arkko fallan: sin ellos, la app no se entera de lo que pasa en las sesiones.</summary>
    /// <remarks>Va con una sola etiqueta: un fallo nuevo sustituye al anterior en vez de apilarse.</remarks>
    public void HookAlert(string title, string text, bool toast, bool sound) => Notify("hooks", "🔴 " + title, text, toast, sound);

    /// <summary>Avisa de algo nuevo en las vulnerabilidades del stack: rojo si algo falla, naranja si hay algo que mirar.</summary>
    /// <remarks>Cada tipo va con su etiqueta: uno nuevo sustituye al anterior del mismo tipo en vez de apilarse.</remarks>
    public void StackAlert(StackAlert alert, bool toast, bool sound) =>
        Notify(alert.Tag, (alert.Serious ? "🔴 " : "🟠 ") + alert.Title, alert.Text, toast, sound);

    /// <summary>Avisa de que un límite de uso ha pasado un umbral; rojo desde el 95 %, naranja por debajo.</summary>
    public void LimitAlert(LimitCrossing crossing, bool toast, bool sound)
    {
        var dot = crossing.Threshold >= 95 ? "🔴" : "🟠";
        var until = Format.Until(crossing.Limit.ResetsAtUtc);
        var text = until is "" or "ya" ? "Vigila el consumo." : $"Se renueva en {until}.";
        Notify("limit-" + crossing.Limit.Key, $"{dot} Límite de {crossing.Limit.Label} al {crossing.Threshold} %", text, toast, sound);
    }

    /// <summary>Envía el aviso como notificación del sistema y, si no se puede, solo con sonido.</summary>
    private static void Notify(string tag, string title, string text, bool toast, bool sound)
    {
        if (toast && SystemNotifications.Show(tag, title, text, sound)) return;
        if (sound) SystemNotifications.PlaySound();
    }

    /// <summary>Pinta el icono de la bandeja: el de la app, con un punto del color del estado más urgente.</summary>
    /// <remarks>Sin sesiones, el icono va limpio, sin punto.</remarks>
    public void SetState(SessionStatus? state)
    {
        if (_iconState == state && _iconSet) return;
        _iconState = state;
        _iconSet = true;

        Color? dot = state switch
        {
            SessionStatus.Waiting => Color.FromRgb(0xF2, 0xB1, 0x34),
            SessionStatus.Done => Color.FromRgb(0x3F, 0xB9, 0x50),
            SessionStatus.Working => Color.FromRgb(0x4C, 0x9A, 0xFF),
            SessionStatus.Agents => Color.FromRgb(0x9B, 0x7B, 0xF0),
            SessionStatus.Command => Color.FromRgb(0x22, 0xD3, 0xEE),
            _ => null,
        };

        try
        {
            const int size = 32;
            var bitmap = new RenderTargetBitmap(new PixelSize(size, size));
            using (var dc = bitmap.CreateDrawingContext())
            using (dc.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality, EdgeMode = EdgeMode.Antialias }))
            {
                if (_appIcon is not null) dc.DrawImage(_appIcon, new Rect(0, 0, size, size));
                if (dot is { } color)
                {
                    // Punto en la esquina inferior derecha, con borde oscuro para que se lea sobre el icono.
                    const double d = 14;
                    var center = new Point(size - d / 2, size - d / 2);
                    dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x1C, 0x1F, 0x27)), null, center, d / 2, d / 2);
                    dc.DrawEllipse(new SolidColorBrush(color), null, center, d / 2 - 1.5, d / 2 - 1.5);
                }
            }
            _tray.Icon = new WindowIcon(bitmap);
        }
        catch (Exception ex)
        {
            JsonStore.LogError("tray-icon", ex);
        }
    }

    /// <summary>Retira el icono de la bandeja.</summary>
    public void Dispose()
    {
        _tray.IsVisible = false;
        _tray.Dispose();
        _appIcon?.Dispose();
    }
}
