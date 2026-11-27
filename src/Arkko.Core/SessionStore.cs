namespace Arkko.Core;

/// <summary>Lectura y actualización del estado de las sesiones en data/sessions.</summary>
public static class SessionStore
{
    /// <summary>Aplica un cambio al estado de una sesión, creándolo si no existe.</summary>
    public static SessionState Update(string sessionId, Action<SessionState> change)
    {
        using var _ = JsonStore.Lock("session-" + sessionId);
        var path = AppPaths.SessionFile(sessionId);
        var now = DateTime.UtcNow;
        var state = JsonStore.Read<SessionState>(path) ?? new SessionState
        {
            SessionId = sessionId,
            StartedUtc = now,
            StatusSinceUtc = now,
        };

        var before = state.Status;
        change(state);
        if (state.Status != before) state.StatusSinceUtc = now;
        state.UpdatedUtc = now;

        JsonStore.Write(path, state);
        if (state.Status != before) Log(sessionId, state.Status, now);
        return state;
    }

    /// <summary>Aplica un cambio al estado de una sesión solo si sigue guardada; null si ya no está.</summary>
    /// <remarks>
    /// Es lo que usa la app, que escribe sin que Claude Code se lo pida: crear el fichero de una sesión cerrada la
    /// resucitaría. Sin <paramref name="touch"/> no cambia cuándo se actualizó, que es lo que dicen los hooks.
    /// </remarks>
    public static SessionState? UpdateIfExists(string sessionId, Action<SessionState> change, bool touch = true)
    {
        using var _ = JsonStore.Lock("session-" + sessionId);
        var path = AppPaths.SessionFile(sessionId);
        if (JsonStore.Read<SessionState>(path) is not { } state) return null;
        var now = DateTime.UtcNow;
        var before = state.Status;
        change(state);
        if (state.Status != before) state.StatusSinceUtc = now;
        if (touch) state.UpdatedUtc = now;
        JsonStore.Write(path, state);
        if (state.Status != before) Log(sessionId, state.Status, now);
        return state;
    }

    /// <summary>Borra el estado de una sesión cerrada.</summary>
    public static void Remove(string sessionId)
    {
        using var _ = JsonStore.Lock("session-" + sessionId);
        var path = AppPaths.SessionFile(sessionId);
        if (!File.Exists(path)) return;
        try
        {
            File.Delete(path);
            Log(sessionId, null, DateTime.UtcNow);
        }
        catch (IOException) { }
    }

    /// <summary>Apunta el cambio en el historial de estados.</summary>
    /// <remarks>Un fallo al apuntarlo no puede parar el hook: se anota y la sesión sigue.</remarks>
    private static void Log(string sessionId, SessionStatus? status, DateTime nowUtc)
    {
        try
        {
            StateLog.Append(sessionId, status, nowUtc);
        }
        catch (Exception ex)
        {
            JsonStore.LogError("states", ex);
        }
    }

    /// <summary>Carga todas las sesiones guardadas.</summary>
    public static List<SessionState> LoadAll()
    {
        var result = new List<SessionState>();
        if (!Directory.Exists(AppPaths.Sessions)) return result;
        foreach (var file in Directory.EnumerateFiles(AppPaths.Sessions, "*.json"))
        {
            var state = JsonStore.Read<SessionState>(file);
            if (state is not null && state.SessionId.Length > 0) result.Add(state);
        }
        return result;
    }
}
