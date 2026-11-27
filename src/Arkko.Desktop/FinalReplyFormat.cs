using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Si la respuesta final del directo se ve con el formato de Arkko: un solo ajuste para la sección Sesiones y
/// para los paneles de los corchos, que se enteran todos por <see cref="Changed"/> en cuanto se cambia en cualquiera.</summary>
internal static class FinalReplyFormat
{
    private static AppSettings? _settings;
    private static Action? _save;

    /// <summary>Se dispara cuando se enciende o se apaga.</summary>
    public static event Action? Changed;

    /// <summary>Si está encendido; apagado hasta que se conecten los ajustes.</summary>
    public static bool On => _settings?.FinalReplyFormat ?? false;

    /// <summary>Recibe los ajustes, donde se guarda, y cómo guardarlos; avisa, para que los paneles ya abiertos lo sepan.</summary>
    public static void Init(AppSettings settings, Action save)
    {
        _settings = settings;
        _save = save;
        Changed?.Invoke();
    }

    /// <summary>Lo enciende o lo apaga, lo guarda y avisa a todas las páginas; si no cambia, no hace nada.</summary>
    public static void Set(bool on)
    {
        if (_settings is not { } settings || settings.FinalReplyFormat == on) return;
        settings.FinalReplyFormat = on;
        _save?.Invoke();
        Changed?.Invoke();
    }
}
