using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Marcas de «visto» de las sesiones terminadas, guardadas en data/reviewed.json.</summary>
/// <remarks>
/// El mismo fichero y el mismo formato que la ventana de WPF. Cada marca guarda el instante en que la sesión
/// pasó a Terminado: si vuelve a trabajar y termina otra vez, ese instante cambia y la tarjeta vuelve a pedir revisión.
/// </remarks>
public sealed class ReviewStore
{
    private static readonly string FilePath = Path.Combine(AppPaths.Data, "reviewed.json");
    private readonly Dictionary<string, DateTime> _reviewed;

    /// <summary>Carga las marcas guardadas.</summary>
    public ReviewStore() =>
        _reviewed = JsonStore.Read<Dictionary<string, DateTime>>(FilePath) ?? new Dictionary<string, DateTime>();

    /// <summary>Indica si la persona ya vio el final actual de la sesión.</summary>
    public bool IsReviewed(SessionState s) =>
        _reviewed.TryGetValue(s.SessionId, out var at) && at >= s.StatusSinceUtc;

    /// <summary>Marca como visto el final actual de la sesión.</summary>
    public void MarkReviewed(SessionState s)
    {
        _reviewed[s.SessionId] = s.StatusSinceUtc;
        Save();
    }

    /// <summary>Olvida las marcas de sesiones que ya no existen.</summary>
    public void Prune(IEnumerable<string> alive)
    {
        var keep = alive.ToHashSet();
        var gone = _reviewed.Keys.Where(k => !keep.Contains(k)).ToList();
        if (gone.Count == 0) return;
        foreach (var id in gone) _reviewed.Remove(id);
        Save();
    }

    /// <summary>Escribe las marcas en disco sin interrumpir la vista si falla.</summary>
    private void Save()
    {
        try
        {
            JsonStore.Write(FilePath, _reviewed);
        }
        catch (Exception ex)
        {
            JsonStore.LogError("reviewed", ex);
        }
    }
}

/// <summary>Avisos de secreto enviado que la persona ya ha visto en la tarjeta de su sesión, en data/secret-alerts-seen.json.</summary>
/// <remarks>Se guarda el instante del aviso visto: si el hook ve otro secreto después, la tarjeta vuelve a marcarse.</remarks>
public sealed class SecretAlertStore
{
    private static readonly string FilePath = Path.Combine(AppPaths.Data, "secret-alerts-seen.json");
    private readonly Dictionary<string, DateTime> _seen;

    /// <summary>Carga los avisos vistos.</summary>
    public SecretAlertStore() =>
        _seen = JsonStore.Read<Dictionary<string, DateTime>>(FilePath) ?? new Dictionary<string, DateTime>();

    /// <summary>Si la sesión tiene un aviso de secreto que aún no se ha visto.</summary>
    public bool IsPending(SessionState s) =>
        s.SecretAlertUtc is { } at && !(_seen.TryGetValue(s.SessionId, out var seen) && seen >= at);

    /// <summary>Da por visto el aviso actual de la sesión.</summary>
    public void MarkSeen(SessionState s)
    {
        if (s.SecretAlertUtc is not { } at) return;
        _seen[s.SessionId] = at;
        Save();
    }

    /// <summary>Olvida los avisos de sesiones que ya no existen.</summary>
    public void Prune(IEnumerable<string> alive)
    {
        var keep = alive.ToHashSet();
        var gone = _seen.Keys.Where(k => !keep.Contains(k)).ToList();
        if (gone.Count == 0) return;
        foreach (var id in gone) _seen.Remove(id);
        Save();
    }

    /// <summary>Escribe los avisos vistos sin interrumpir la vista si falla.</summary>
    private void Save()
    {
        try
        {
            JsonStore.Write(FilePath, _seen);
        }
        catch (Exception ex)
        {
            JsonStore.LogError("secret-alerts", ex);
        }
    }
}
