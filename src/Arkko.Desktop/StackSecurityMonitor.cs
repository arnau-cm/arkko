using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Un aviso de vulnerabilidades para la notificación: su etiqueta, si es grave, y el texto.</summary>
/// <param name="Tag">Agrupa la notificación: una nueva del mismo tipo sustituye a la anterior.</param>
/// <param name="Serious">Rojo (algo falla) o naranja (algo que mirar).</param>
public sealed record StackAlert(string Tag, bool Serious, string Title, string Text);

/// <summary>Vigila el informe de seguridad de arkko-stack: lo lee en cuanto cambia, lanza <c>check-security.ps1</c> cuando se
/// pide o, si la persona lo deja, cuando el informe se queda viejo, y avisa de lo nuevo.</summary>
/// <remarks>
/// La lógica de qué está expuesto es de arkko-stack: Arkko solo lanza su script, lee su informe y lo enseña. El script sale a
/// internet; por eso solo se lanza solo con «Comprobar solo, en segundo plano», y a mano siempre. Lo que ya se sabía se
/// guarda en datos (<see cref="StackSecurityLog"/>), para no repetir avisos al reiniciar.
/// </remarks>
public sealed class StackSecurityMonitor
{
    private static readonly TimeSpan Every = TimeSpan.FromHours(1);
    private static readonly TimeSpan FirstAfter = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan SeenTimeout = TimeSpan.FromMinutes(1);

    // Ids que se pasan a -Visto: los del contrato (INCIBE-…, CVE-…, NODE-…). Cualquier otra cosa no se manda al script.
    private static readonly Regex SafeId = new("^[A-Za-z0-9._:-]+$", RegexOptions.Compiled);

    private readonly AppSettings _settings;
    private readonly DispatcherTimer _timer = new() { Interval = Every };
    private readonly DispatcherTimer _settle = new() { Interval = TimeSpan.FromSeconds(1) };
    private FileSystemWatcher? _watcher;
    private bool _reading;
    private bool _readAgain;

    /// <summary>Crea el vigilante sobre los ajustes de la app; empieza con <see cref="Start"/>.</summary>
    public StackSecurityMonitor(AppSettings settings)
    {
        _settings = settings;
        _timer.Tick += (_, _) =>
        {
            // La carpeta del informe puede no existir al arrancar: aparece con la primera ejecución, de Arkko o de fuera.
            if (_watcher is null)
            {
                Watch();
                Reload();
            }
            RunIfDue();
        };
        _settle.Tick += (_, _) =>
        {
            _settle.Stop();
            Reload();
        };
    }

    /// <summary>Se dispara en el hilo de la ventana al leer el informe y al empezar o acabar una ejecución.</summary>
    public event Action? Changed;

    /// <summary>Hay algo nuevo de lo que avisar; llega en el hilo de la ventana y solo con los avisos encendidos.</summary>
    public event Action<StackAlert>? Alert;

    /// <summary>Lo último leído del informe; null hasta la primera lectura.</summary>
    public StackReportRead? Last { get; private set; }

    /// <summary>El informe leído, si se ha podido leer.</summary>
    public StackReport? Report => Last?.Report;

    /// <summary>Lo que se está lanzando ahora (noticias, exposición o todo); null si nada.</summary>
    public string? RunningScope { get; private set; }

    /// <summary>Si lo que se está lanzando lo ha lanzado Arkko solo.</summary>
    public bool RunningAuto { get; private set; }

    /// <summary>Cuándo empezó lo que se está lanzando.</summary>
    public DateTime? RunStartedUtc { get; private set; }

    /// <summary>Se están marcando avisos como vistos.</summary>
    public bool MarkingSeen { get; private set; }

    /// <summary>Lo que salió mal en la última ejecución lanzada desde Arkko; null si fue bien.</summary>
    public string? RunProblem { get; private set; }

    /// <summary>Qué pasó en la última ejecución lanzada desde Arkko, en una línea; null si no se ha lanzado ninguna.</summary>
    public string? RunNote { get; private set; }

    public bool Busy => RunningScope is not null || MarkingSeen;

    /// <summary>Comprobaciones en FALLA.</summary>
    public int Failures => Report?.Checks.Count(c => c.Failing) ?? 0;

    /// <summary>Avisos sin ver.</summary>
    public int Unseen => Report?.Notices.Count(n => !n.Seen) ?? 0;

    /// <summary>Comprobaciones y fuentes que quedaron sin comprobar.</summary>
    public int Unchecked => (Report?.Checks.Count(c => c.Unchecked) ?? 0) + (Report?.Sources.Count(s => s.State == StackSecurity.Unchecked) ?? 0);

    /// <summary>Si Arkko puede lanzar el script: hay arkko-stack con <c>check-security.ps1</c> y es Windows.</summary>
    public static bool CanRun => StackSecurity.ScriptPath() is not null;

    /// <summary>Empieza a vigilar: lee el informe ya, lo vuelve a leer cuando cambia y mira cada hora si toca lanzarlo.</summary>
    public void Start()
    {
        Reload();
        Watch();
        DispatcherTimer.RunOnce(RunIfDue, FirstAfter);
        _timer.Start();
    }

    /// <summary>Vigila la carpeta del informe, que se escribe de una vez al acabar cada ejecución.</summary>
    /// <remarks>Si la carpeta aún no existe (arkko-stack nunca ha escrito un informe), se lee cada hora con la comprobación.</remarks>
    private void Watch()
    {
        try
        {
            var folder = StackSecurity.ReportFolder();
            if (!Directory.Exists(folder)) return;
            _watcher = new FileSystemWatcher(folder, "security.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            FileSystemEventHandler changed = (_, _) => Dispatcher.UIThread.Post(() =>
            {
                _settle.Stop();
                _settle.Start();
            });
            _watcher.Changed += changed;
            _watcher.Created += changed;
            _watcher.Renamed += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                _settle.Stop();
                _settle.Start();
            });
        }
        catch (Exception ex)
        {
            JsonStore.LogError("vulnerabilities-watch", ex);
        }
    }

    /// <summary>Lee el informe fuera del hilo de la ventana, apunta lo que ha cambiado y avisa de lo nuevo.</summary>
    public void Reload()
    {
        if (_reading)
        {
            _readAgain = true;
            return;
        }
        _reading = true;
        Task.Run(() =>
            {
                var read = StackSecurity.Read();
                var changes = read.Report is { } report ? StackSecurityLog.Record(report, DateTime.UtcNow) : StackChanges.None;
                return (read, changes);
            })
            .ContinueWith(task =>
            {
                _reading = false;
                if (task.Exception is { } ex) JsonStore.LogError("vulnerabilities-read", ex.GetBaseException());
                else
                {
                    Last = task.Result.read;
                    AlertChanges(task.Result.changes);
                    // La primera lectura solo apunta cómo está todo, también lo viejo del informe.
                    if (!task.Result.changes.First) AlertStale();
                    Changed?.Invoke();
                }
                if (_readAgain)
                {
                    _readAgain = false;
                    Reload();
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Avisa de lo nuevo: una notificación que resume las FALLAs nuevas y otra los avisos nuevos.</summary>
    private void AlertChanges(StackChanges changes)
    {
        if (changes.NewFailures is { Count: > 0 } failures)
        {
            var names = failures.Select(Label).ToList();
            Raise(new StackAlert("stack-fail", true,
                failures.Count == 1 ? $"Hay que actualizar {names[0]}" + SeverityNote(failures[0]) : $"{failures.Count} cosas que actualizar",
                failures.Count == 1 ? (failures[0].Detail ?? "Míralo en Vulnerabilidades.") : Join(names) + ". Míralo en Vulnerabilidades."));
        }
        if (changes.NewNotices is { Count: > 0 } notices)
        {
            Raise(new StackAlert("stack-notice", false,
                notices.Count == 1 ? $"Aviso de seguridad por revisar: {notices[0].Source}" : $"{notices.Count} avisos de seguridad por revisar",
                notices.Count == 1
                    ? $"{notices[0].Title}." + (notices[0].Parts.Count > 0 ? $" Toca a {string.Join(", ", notices[0].Parts)}." : "")
                    : Join(notices.Select(n => n.Title ?? n.Id).ToList()) + "."));
        }
    }

    /// <summary>La gravedad para el título del aviso, « · gravedad alta»; vacío si el informe no la da.</summary>
    private static string SeverityNote(StackCheck check) => check.SeverityRank switch
    {
        0 => " · gravedad crítica",
        1 => " · gravedad alta",
        2 => " · gravedad media",
        3 => " · gravedad baja",
        _ => "",
    };

    /// <summary>Avisa, una vez por cada fecha, de que las noticias o la exposición están viejas, si Arkko no las va a comprobar solo.</summary>
    /// <remarks>Sin arkko-stack y sin informe no hay nada que avisar: la sección ya dice que no lo encuentra.</remarks>
    private void AlertStale()
    {
        if (_settings.StackAutoCheck && CanRun) return;
        if (Last is not { Status: StackReportStatus.Ok or StackReportStatus.Missing }) return;
        if (Report is null && StackSecurity.StackHome() is null) return;
        var now = DateTime.UtcNow;
        var how = CanRun
            ? "Arkko no lo comprueba solo: pulsa su botón en Vulnerabilidades, o actívalo en Configuración → Seguridad."
            : "Lánzalo con check-security.ps1 de arkko-stack.";
        if (StackSecurity.NewsStale(Report, now) && StackSecurityLog.TakeStaleAlert(news: true, Report?.News?.Date))
            Raise(new StackAlert("stack-stale-news", false, StaleTitle("Los avisos de seguridad", Report?.News?.Date, now), how));
        if (StackSecurity.ExposureStale(Report, now) && StackSecurityLog.TakeStaleAlert(news: false, Report?.Exposure?.Date))
            Raise(new StackAlert("stack-stale-exposure", false, StaleTitle("La comprobación de tu equipo", Report?.Exposure?.Date, now), how));
    }

    /// <summary>«Los avisos de seguridad: hace 3 días que no se miran», o que nunca se han mirado.</summary>
    private static string StaleTitle(string what, DateTimeOffset? date, DateTime nowUtc)
    {
        if (date is not { } d) return $"{what}: nunca se han mirado";
        var days = Math.Max(1, (int)(nowUtc - d.UtcDateTime).TotalDays);
        return $"{what}: hace {days} {(days == 1 ? "día" : "días")} que no se miran";
    }

    /// <summary>El nombre de una comprobación con su repo, si no es del equipo: «npm audit (Planos)».</summary>
    public static string Label(StackCheck check) => check.OfMachine ? check.Name ?? check.Id : $"{check.Name ?? check.Id} ({check.Object})";

    /// <summary>Los primeros nombres de una lista, y cuántos más hay.</summary>
    private static string Join(IReadOnlyList<string> names) =>
        names.Count <= 4 ? string.Join(", ", names) : string.Join(", ", names.Take(3)) + $" y {names.Count - 3} más";

    /// <summary>Pasa el aviso a quien lo enseña, si los avisos de vulnerabilidades están encendidos.</summary>
    /// <remarks>Un fallo al enseñarlo se anota y no sigue: cortaría la lectura del informe antes de poner al día el menú.</remarks>
    private void Raise(StackAlert alert)
    {
        if (!_settings.StackAlerts) return;
        try
        {
            Alert?.Invoke(alert);
        }
        catch (Exception ex)
        {
            JsonStore.LogError("vulnerabilities-alert", ex);
        }
    }

    /// <summary>Lanza lo que toque si la persona deja a Arkko comprobar solo.</summary>
    /// <remarks>
    /// Mira solo las fechas del informe, así que cuenta cualquier ejecución, también la de check-parity. Con un informe de un
    /// esquema que no conoce, no lanza nada: el script es más nuevo que Arkko y volvería a escribir lo mismo.
    /// </remarks>
    public void RunIfDue()
    {
        if (!_settings.StackAutoCheck || !CanRun || Busy) return;
        if (Last is { Status: StackReportStatus.UnknownSchema }) return;
        if (StackSecurity.DueScope(Report, DateTime.UtcNow) is { } scope) _ = RunAsync(scope, auto: true);
    }

    /// <summary>Lo que se espera a cada ejecución antes de darla por colgada.</summary>
    /// <remarks>Medido en el equipo de desarrollo; las noticias tardan segundos y la exposición pasa npm audit y dotnet en cada repo.</remarks>
    private static TimeSpan TimeoutFor(string scope) => scope == StackSecurity.ScopeNews ? TimeSpan.FromMinutes(2) : TimeSpan.FromMinutes(10);

    /// <summary>Lanza <c>check-security.ps1</c> con el ámbito dado y, al acabar, vuelve a leer el informe.</summary>
    /// <param name="auto">Si la lanza Arkko solo, por antigüedad, y no la persona.</param>
    public async Task RunAsync(string scope, bool auto = false)
    {
        if (Busy || StackSecurity.ScriptPath() is not { } script) return;
        RunningScope = scope;
        RunningAuto = auto;
        RunStartedUtc = DateTime.UtcNow;
        Changed?.Invoke();
        var result = await Task.Run(() => Launch(script, $"-Scope {scope} -Origen arkko", TimeoutFor(scope)));
        var before = RunProblem;
        (RunProblem, RunNote) = result.Code switch
        {
            0 or 1 or 2 => ((string?)null, (string?)null),
            3 => (null, "Ya había otra comprobación en curso, de check-parity o a mano: el informe se pondrá al día cuando acabe."),
            _ => (result.Problem, null),
        };
        RunningScope = null;
        RunStartedUtc = null;
        // El mismo fallo cada hora no se vuelve a avisar: ya se ve en la sección.
        if (RunProblem is { } problem && problem != before)
            Raise(new StackAlert("stack-run", true, "La comprobación de seguridad no ha terminado", problem));
        Changed?.Invoke();
        Reload();
    }

    /// <summary>Marca avisos como vistos con <c>-Visto</c>, que no sale a internet; devuelve lo que ha fallado, o null.</summary>
    public async Task<string?> MarkSeenAsync(IReadOnlyList<string> ids)
    {
        if (StackSecurity.ScriptPath() is not { } script) return "Arkko no encuentra check-security.ps1 de arkko-stack.";
        if (Busy) return "Hay una comprobación en curso: vuelve a probar cuando acabe.";
        if (ids.Count == 0 || ids.Any(id => !SafeId.IsMatch(id))) return "El aviso tiene un id que Arkko no sabe pasar al script.";
        MarkingSeen = true;
        Changed?.Invoke();
        var list = string.Join(",", ids.Select(id => $"'{id}'"));
        var result = await Task.Run(() => Launch(script, $"-Visto {list}", SeenTimeout));
        MarkingSeen = false;
        Changed?.Invoke();
        Reload();
        return result.Code switch
        {
            0 or 1 or 2 => null,
            3 => "Hay otra comprobación en curso: vuelve a probar cuando acabe.",
            _ => result.Problem ?? "check-security.ps1 no ha terminado.",
        };
    }

    /// <summary>Ejecuta el script con PowerShell, sin ventana, y devuelve su código de salida y, si falla, por qué.</summary>
    /// <remarks>
    /// Va por <c>-Command</c> para poner la consola en UTF-8: la primera línea de un error trae acentos, y PowerShell 5.1 escribe
    /// en la página de códigos de la consola. Los argumentos son fijos o ids ya comprobados; la ruta va entre comillas simples.
    /// Si se pasa del tiempo, se mata con todo lo que haya lanzado (npm, dotnet).
    /// </remarks>
    private static (int Code, string? Problem) Launch(string script, string arguments, TimeSpan timeout)
    {
        var shell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var command = "[Console]::OutputEncoding = [Text.Encoding]::UTF8; " +
                      $"& '{script.Replace("'", "''")}' {arguments}; exit $LASTEXITCODE";
        var info = new ProcessStartInfo(File.Exists(shell) ? shell : "powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", command }) info.ArgumentList.Add(arg);
        try
        {
            using var process = Process.Start(info);
            if (process is null) return (-1, "No se ha podido arrancar PowerShell.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeout))
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return (-1, $"Se ha cortado tras {Format.Duration(timeout)} sin terminar.");
            }
            process.WaitForExit();
            if (process.ExitCode is 0 or 1 or 2 or 3) return (process.ExitCode, null);
            var first = FirstLine(output.Result) ?? FirstLine(error.Result);
            return (process.ExitCode, first is null ? $"check-security.ps1 salió con el código {process.ExitCode}." : $"check-security.ps1: {first}");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            JsonStore.LogError("vulnerabilities-run", ex);
            return (-1, "No se ha podido lanzar check-security.ps1: " + ex.Message);
        }
    }

    /// <summary>La primera línea con texto; null si no hay ninguna.</summary>
    private static string? FirstLine(string text) =>
        text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
}
