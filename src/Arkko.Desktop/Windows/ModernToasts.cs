using System.IO;
using System.Security;
using Arkko.Core;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Arkko.Desktop;

/// <summary>Notificaciones modernas de Windows: cabecera con el icono de la app y guardadas en el centro de notificaciones.</summary>
/// <remarks>
/// Una app de escritorio sin paquete se da a conocer como remitente registrando su identificador
/// (AUMID) en HKCU\Software\Classes\AppUserModelId con nombre e icono; Windows saca de ahí la
/// cabecera. Cada sesión usa su id como etiqueta: un aviso nuevo sustituye al anterior de la misma
/// sesión en lugar de acumularse.
/// </remarks>
public static class ModernToasts
{
    private const string Aumid = "Arkko";
    private const string Group = "sessions";

    // El remitente de cuando la app se llamaba Agent Pulse: se retira al registrar el nuevo.
    private const string OldAumid = "AgentPulse";
    private static bool? _available;
    private static string? _icon;

    /// <summary>Registra la app como remitente; false si Windows no lo permite y hay que usar los globos.</summary>
    public static bool Register()
    {
        if (_available is { } known) return known;
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\AppUserModelId\" + Aumid))
                key.SetValue("DisplayName", "Arkko");
            ToastNotificationManager.CreateToastNotifier(Aumid);
            _available = true;
            RetireOld();
        }
        catch (Exception ex)
        {
            JsonStore.LogError("toast-register", ex);
            _available = false;
        }
        return _available.Value;
    }

    /// <summary>Quita el remitente viejo, «AgentPulse», y las notificaciones suyas que queden en el centro de
    /// notificaciones; si no estaba, no hace nada.</summary>
    /// <remarks>Si falla, se apunta y sigue: el remitente nuevo ya funciona.</remarks>
    private static void RetireOld()
    {
        try
        {
            using (var old = Registry.CurrentUser.OpenSubKey(@"Software\Classes\AppUserModelId\" + OldAumid))
                if (old is null) return;
            ToastNotificationManager.History.Clear(OldAumid);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\AppUserModelId\" + OldAumid, throwOnMissingSubKey: false);
        }
        catch (Exception ex)
        {
            JsonStore.LogError("toast-retire", ex);
        }
    }

    /// <summary>Pone de icono del remitente el que se lee en el modo de Windows de ahora: la letra clara en modo oscuro y la
    /// oscura en modo claro.</summary>
    /// <remarks>
    /// Windows admite un solo icono por remitente, pero lo vuelve a leer en cada notificación: basta con cambiarlo antes
    /// de mandarla. Las que ya estén en el centro de notificaciones siguen con el de antes hasta la siguiente. Si falla, se
    /// apunta y la notificación sale igual, con el icono que hubiera.
    /// </remarks>
    private static void UpdateIcon()
    {
        try
        {
            bool light;
            using (var personalize = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                light = personalize?.GetValue("SystemUsesLightTheme") is int value && value != 0;
            var icon = Path.Combine(AppContext.BaseDirectory, "Assets", light ? "arkko-notificacion-modo-claro.png" : "arkko-notificacion-modo-oscuro.png");
            if (icon == _icon || !File.Exists(icon)) return;
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\AppUserModelId\" + Aumid))
                key.SetValue("IconUri", icon);
            _icon = icon;
        }
        catch (Exception ex)
        {
            JsonStore.LogError("toast-icon", ex);
        }
    }

    /// <summary>Muestra una notificación de una sesión; false si no se pudo y hay que usar los globos.</summary>
    public static bool Show(string sessionId, string title, string text, bool sound)
    {
        if (!Register()) return false;
        UpdateIcon();
        try
        {
            var audio = sound ? "<audio src=\"ms-winsoundevent:Notification.Default\" />" : "<audio silent=\"true\" />";
            var xml = new XmlDocument();
            xml.LoadXml($"""
                <toast>
                  <visual>
                    <binding template="ToastGeneric">
                      <text>{SecurityElement.Escape(title)}</text>
                      <text>{SecurityElement.Escape(text)}</text>
                    </binding>
                  </visual>
                  {audio}
                </toast>
                """);
            var toast = new ToastNotification(xml) { Tag = Tag(sessionId), Group = Group };
            ToastNotificationManager.CreateToastNotifier(Aumid).Show(toast);
            return true;
        }
        catch (Exception ex)
        {
            JsonStore.LogError("toast-show", ex);
            return false;
        }
    }

    /// <summary>Quita del centro de notificaciones el aviso de una sesión, si lo hay.</summary>
    public static void Remove(string sessionId)
    {
        if (_available != true) return;
        try
        {
            ToastNotificationManager.History.Remove(Tag(sessionId), Group, Aumid);
        }
        catch (Exception ex)
        {
            JsonStore.LogError("toast-remove", ex);
        }
    }

    /// <summary>Etiqueta de la notificación de una sesión; Windows admite como máximo 64 caracteres.</summary>
    private static string Tag(string sessionId) => sessionId.Length <= 64 ? sessionId : sessionId[..64];
}
