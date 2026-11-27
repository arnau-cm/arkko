using System.Globalization;

namespace Arkko.Core;

/// <summary>Un cambio de estado de una sesión, una línea en data/states/&lt;día&gt;.jsonl.</summary>
public sealed class StateChange
{
    public DateTime WhenUtc { get; set; }
    public string SessionId { get; set; } = "";

    /// <summary>El estado al que pasa; null cuando la sesión se cierra.</summary>
    public SessionStatus? Status { get; set; }
}

/// <summary>El historial de estados de las sesiones: cuándo trabajó cada una, cuándo esperó a la persona y cuándo se cerró.</summary>
/// <remarks>
/// El estado actual de una sesión se sobrescribe en cada evento; este registro guarda los cambios, para saber
/// después cuánto tiempo pasó esperando una respuesta. Solo se escribe cuando el estado cambia.
/// </remarks>
public static class StateLog
{
    /// <summary>Apunta que una sesión ha pasado a un estado, o que se ha cerrado si es null.</summary>
    public static void Append(string sessionId, SessionStatus? status, DateTime nowUtc, string? dataDir = null) =>
        JsonStore.AppendLine(PathOf(nowUtc.ToLocalTime(), dataDir), new StateChange { WhenUtc = nowUtc, SessionId = sessionId, Status = status });

    /// <summary>Los cambios de un día local, en el orden en que se apuntaron.</summary>
    public static List<StateChange> Read(DateTime localDay, string? dataDir = null) =>
        JsonStore.ReadLines<StateChange>(PathOf(localDay, dataDir));

    /// <summary>El fichero de un día local.</summary>
    public static string PathOf(DateTime localDay, string? dataDir = null) =>
        Path.Combine(dataDir ?? AppPaths.Data, "states", localDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".jsonl");
}
