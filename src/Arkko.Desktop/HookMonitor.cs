using System.Diagnostics;
using System.IO;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Lo que se sabe de los hooks de Arkko en todo el equipo: si funcionan y, si no, qué falla y qué hacer.</summary>
public sealed record HookHealth(bool Ok, string? Problem, string? Fix, DateTime CheckedUtc);

/// <summary>Vigila que los hooks de Arkko funcionen: en cada sesión, por lo que apunta Claude Code en su transcript, y en
/// el equipo, con <c>settings.json</c> y arrancando <c>ak.exe</c>.</summary>
/// <remarks>
/// Si <c>ak.exe</c> no arranca, Claude Code sigue sin error y la app se queda con lo último apuntado. Lo que sí queda es
/// el transcript: con él se marca la sesión con el motivo, se da por terminado el turno cuyo Stop falló y, cuando un
/// hook vuelve a funcionar, se quita la marca.
/// </remarks>
public sealed class HookMonitor
{
    /// <summary>Cada cuánto se comprueba el equipo con todo en orden.</summary>
    public static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(5);

    /// <summary>Cada cuánto, con algo fallando: para ver enseguida que se ha arreglado.</summary>
    public static readonly TimeSpan CheckEveryFailing = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly Dictionary<string, HookTrace> _traces = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _nextCheckUtc;
    private bool _checking;

    /// <summary>Lo último comprobado del equipo; null hasta la primera comprobación.</summary>
    public HookHealth? Health { get; private set; }

    /// <summary>Ha terminado una comprobación del equipo. Llega en un hilo de fondo.</summary>
    public event Action? HealthChanged;

    /// <summary>Lee lo nuevo de los transcripts de las sesiones abiertas y corrige su estado; devuelve las que ha
    /// reescrito, con su estado nuevo.</summary>
    public Dictionary<string, SessionState> Check(IReadOnlyList<SessionState> sessions)
    {
        foreach (var gone in _traces.Keys.Except(sessions.Select(s => s.SessionId), StringComparer.OrdinalIgnoreCase).ToList()) _traces.Remove(gone);

        var written = new Dictionary<string, SessionState>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in sessions)
        {
            if (string.IsNullOrEmpty(s.TranscriptPath) || !File.Exists(s.TranscriptPath)) continue;
            if (!_traces.TryGetValue(s.SessionId, out var trace)) _traces[s.SessionId] = trace = new HookTrace();
            try
            {
                trace.Read(s.TranscriptPath);
                if (Apply(s, trace) is { } state) written[s.SessionId] = state;
            }
            catch (Exception ex)
            {
                JsonStore.LogError("hook-watch", ex);
            }
        }
        if (written.Values.Any(s => s.HookError is not null)) CheckSoon();
        return written;
    }

    /// <summary>Corrige el estado de una sesión con lo que dice su transcript; null si no ha hecho falta escribir.</summary>
    private SessionState? Apply(SessionState s, HookTrace trace)
    {
        SessionState? state = null;

        // El turno terminó sin que llegara su Stop: se cierra como lo habría hecho el hook. Si después ha escrito otro
        // hook, la sesión ya siguió adelante y ese cierre llega tarde.
        if (trace.MissedStopUtc is { } missed && s.Status != SessionStatus.Done && s.UpdatedUtc < missed &&
            JsonStore.Read<SessionState>(AppPaths.SessionFile(s.SessionId)) is { } fresh && fresh.Status != SessionStatus.Done && fresh.UpdatedUtc < missed)
            state = TurnClose.Close(s.SessionId, s.TranscriptPath, missed, create: false)?.State;

        // La marca dice cómo acabó el último hook de la sesión, y se queda hasta que otro funcione en ella: aunque ak.exe ya
        // arranque, lo que pasó mientras fallaba no llegó a la app.
        var current = state ?? s;
        var failing = trace.Last is { Ok: false } last ? last : null;
        if (failing is not null && (current.HookError != failing.Reason || current.HookErrorUtc != failing.Utc))
        {
            state = SessionStore.UpdateIfExists(s.SessionId, x => (x.HookError, x.HookErrorUtc) = (failing.Reason, failing.Utc), touch: false) ?? state;
        }
        else if (failing is null && current.HookError is not null)
        {
            state = SessionStore.UpdateIfExists(s.SessionId, x => (x.HookError, x.HookErrorUtc) = (null, null), touch: false) ?? state;
        }
        return state;
    }

    /// <summary>Indica si un fallo de hooks sigue siendo de ahora: posterior a la última vez que <c>ak.exe</c> arrancó bien
    /// en la comprobación del equipo, o sin comprobación todavía.</summary>
    public bool StillFailing(DateTime failedUtc) => Health is not { Ok: true } health || health.CheckedUtc < failedUtc;

    /// <summary>Adelanta la próxima comprobación del equipo a ahora.</summary>
    public void CheckSoon() => _nextCheckUtc = DateTime.MinValue;

    /// <summary>Comprueba el equipo, en segundo plano, si le toca.</summary>
    public void CheckIfDue()
    {
        if (_checking || DateTime.UtcNow < _nextCheckUtc) return;
        _checking = true;
        Task.Run(() =>
        {
            try
            {
                var health = CheckMachine() with { CheckedUtc = DateTime.UtcNow };
                Health = health;
                _nextCheckUtc = DateTime.UtcNow + (health.Ok ? CheckEvery : CheckEveryFailing);
            }
            catch (Exception ex)
            {
                JsonStore.LogError("hook-health", ex);
                _nextCheckUtc = DateTime.UtcNow + CheckEvery;
            }
            finally
            {
                _checking = false;
            }
            HealthChanged?.Invoke();
        });
    }

    /// <summary>Comprueba que los hooks siguen en settings.json y que el <c>ak.exe</c> al que apuntan arranca.</summary>
    private static HookHealth CheckMachine()
    {
        string? command;
        try
        {
            var settings = SettingsInstaller.Load(AppPaths.ClaudeSettingsFile);
            var missing = SettingsInstaller.MissingHooks(settings);
            if (missing.Count == SettingsInstaller.Hooks.Length)
                return Bad("Los hooks de Arkko no están en settings.json", Reinstall);
            if (missing.Count > 0)
                return Bad($"Faltan hooks de Arkko en settings.json: {string.Join(", ", missing)}", Reinstall);
            command = SettingsInstaller.OwnStopCommand(settings);
        }
        catch (Exception ex)
        {
            return Bad("No se puede leer settings.json de Claude Code", ex.Message);
        }

        var exe = ExeOf(command) ?? Path.Combine(AppPaths.Bin, OperatingSystem.IsWindows() ? "ak.exe" : "ak");
        if (!File.Exists(exe)) return Bad("No encuentra ak.exe", $"Falta {exe}: vuelve a instalarlo con deploy-hooks.");
        return Start(exe);
    }

    /// <summary>Arranca <c>ak.exe</c> sin argumentos, que solo enseña su ayuda, y dice si ha podido.</summary>
    private static HookHealth Start(string exe)
    {
        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
        };
        try
        {
            using var process = Process.Start(info);
            if (process is null) return Bad("No arranca ak.exe", null);
            process.StandardInput.Close();
            var stderr = process.StandardError.ReadToEndAsync();
            _ = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(Timeout))
            {
                try { process.Kill(); } catch (InvalidOperationException) { }
                return Bad("ak.exe no responde", "Puede que Windows lo esté analizando: se vuelve a probar enseguida.");
            }
            var error = stderr.Wait(TimeSpan.FromSeconds(1)) ? stderr.Result : "";
            // Sin argumentos, ak enseña su ayuda y sale con 1; cualquier otra cosa es que no ha llegado a ejecutarse.
            if (process.ExitCode is 0 or 1) return new HookHealth(true, null, null, default);
            return Failed(HookTrace.Reason(error, process.ExitCode));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return Failed(HookTrace.Reason(ex.Message, null));
        }
    }

    /// <summary>Qué hacer si faltan los hooks: la orden que los vuelve a poner, con la ruta de ak.exe, que no está en el PATH.</summary>
    private static string Reinstall => $"Vuelve a instalarlos con «{Path.Combine(AppPaths.Bin, OperatingSystem.IsWindows() ? "ak.exe" : "ak")} install» en una terminal.";

    /// <summary>Un problema del equipo, con qué hacer; la hora la pone quien comprueba.</summary>
    private static HookHealth Bad(string problem, string? fix) => new(false, problem, fix, default);

    /// <summary>El fallo de arranque de <c>ak.exe</c>, con qué hacer según el motivo.</summary>
    private static HookHealth Failed(string reason) => Bad($"ak.exe no arranca: {reason}", reason switch
    {
        "No encuentra .NET" => "Si se está actualizando .NET o Visual Studio, se arregla solo al acabar; si no, instala el runtime de .NET.",
        "Windows lo ha bloqueado (Smart App Control)" => "Vuelve a instalarlo con deploy-hooks, que reintenta hasta que Windows lo deja.",
        _ => null,
    });

    /// <summary>La ruta del ejecutable de un comando de hook, que va entre comillas al principio; null si no la lleva.</summary>
    internal static string? ExeOf(string? command)
    {
        if (command is null || !command.StartsWith('"')) return null;
        var end = command.IndexOf('"', 1);
        return end > 1 ? command[1..end] : null;
    }
}
