using System.IO;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Busca secretos en segundo plano: lo reciente de los transcripts y del historial de prompts cada poco, la configuración de Claude Code entera y, si se pide, todos los transcripts.</summary>
/// <remarks>
/// El análisis va fuera del hilo de la ventana: el historial completo son cientos de ficheros. El
/// automático solo mira los transcripts escritos desde el anterior, y el escáner no relee lo ya
/// visto de cada uno. Nunca corren dos a la vez: el mutex del escáner deja de esperar a los pocos
/// segundos, y dos análisis solapados se pisarían los hallazgos.
/// </remarks>
public sealed class SecurityMonitor
{
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(2);

    // Margen al mirar qué se ha escrito: la hora de modificación de un fichero puede ir un poco por detrás.
    private static readonly TimeSpan Overlap = TimeSpan.FromMinutes(1);

    private readonly AppSettings _settings;
    private readonly Action _save;
    private bool _fullPending;

    // Huella de lo que miró el último análisis automático, para saltarse lo que no ha cambiado: la de
    // ~/.claude.json y la carpeta de copias, que dicen qué ficheros de configuración hay; la de esos
    // ficheros, y la del historial de prompts con los textos pegados. Solo las toca el análisis en curso.
    private string _stateStamp = "";
    private List<ConfigFile> _configFiles = [];
    private string _configStamp = "";
    private string _historyStamp = "";

    // Secretos pendientes por sesión, leídos del almacén; se rehacen al cambiar el fichero de hallazgos o lo resuelto.
    private (DateTime Stamp, IReadOnlySet<string>? Settled, Dictionary<string, List<SecretFinding>> BySession) _pending =
        (default, null, new(StringComparer.OrdinalIgnoreCase));

    /// <summary>Crea el monitor sobre los ajustes de la app, donde apunta cuándo analizó.</summary>
    /// <param name="save">Guarda los ajustes; se llama tras analizar todo el historial.</param>
    public SecurityMonitor(AppSettings settings, Action save)
    {
        _settings = settings;
        _save = save;
    }

    /// <summary>Se dispara en el hilo de la ventana al empezar y al terminar cada análisis.</summary>
    public event Action? Changed;

    public bool Running { get; private set; }

    /// <summary>Si hay un análisis de todo el historial en marcha o esperando turno.</summary>
    public bool FullRunning { get; private set; }

    public DateTime? LastScanUtc => _settings.SecurityScanUtc;
    public DateTime? FullScanUtc => _settings.SecurityFullScanUtc;

    /// <summary>Si el último análisis falló, por qué; null si fue bien.</summary>
    public string? LastError { get; private set; }

    /// <summary>Secretos pendientes de cambiar: ni rotados ni marcados como falso positivo. Lo enseñan los avisos.</summary>
    public int PendingCount { get; private set; }

    /// <summary>Se dispara en el hilo de la ventana cuando cambia el número de pendientes.</summary>
    public event Action? PendingChanged;

    /// <summary>Huellas de los secretos resueltos (falso positivo o rotado), para retirar el aviso de las tarjetas.</summary>
    public IReadOnlySet<string> Settled { get; private set; } = new HashSet<string>();

    /// <summary>Indica si todos los secretos del aviso de una sesión ya están resueltos en Seguridad.</summary>
    /// <remarks>Un aviso sin huellas, de antes de guardarlas o con el almacén sin abrir, nunca cuenta como resuelto.</remarks>
    public bool IsSettled(SessionState s) => s.SecretAlertFingerprints is { Count: > 0 } prints && prints.All(Settled.Contains);

    /// <summary>Secretos de una sesión aún pendientes: ni rotados ni marcados como falso positivo.</summary>
    /// <remarks>
    /// El almacén va cifrado y leerlo cuesta: se lee una vez, se reparte por sesión y solo se vuelve a leer si
    /// cambia el fichero de hallazgos o lo resuelto. Si no se puede leer (el análisis lo está escribiendo), no hay ninguno.
    /// </remarks>
    public IReadOnlyList<SecretFinding> PendingFor(string sessionId)
    {
        try
        {
            var file = Path.Combine(AppPaths.Security, "findings.bin");
            var stamp = File.Exists(file) ? File.GetLastWriteTimeUtc(file) : default;
            if (stamp != _pending.Stamp || !ReferenceEquals(Settled, _pending.Settled))
            {
                var settled = Settled;
                var pending = stamp == default ? [] : new SecretScanner().Findings().Where(f => !settled.Contains(f.Fingerprint));
                _pending = (stamp, settled, pending.GroupBy(f => f.SessionId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase));
            }
            return _pending.BySession.TryGetValue(sessionId, out var list) ? list : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or System.Security.Cryptography.CryptographicException)
        {
            JsonStore.LogError("security-pending-session", ex);
            return [];
        }
    }

    /// <summary>Qué secreto sin revisar lleva una sesión, para su escudo: los tipos hallados en el análisis o, si aún no lo ha visto, el aviso del hook; null si nada.</summary>
    /// <remarks>El escudo solo se va al rotar o descartar el secreto aquí: verlo o pulsar la tarjeta no lo quita.</remarks>
    public string? SecretLabel(SessionState s)
    {
        var kinds = PendingFor(s.SessionId).Select(f => f.Kind).Distinct().ToList();
        if (kinds.Count > 0) return string.Join(", ", kinds);
        return s.SecretAlert is not null && !IsSettled(s) && !Unconfirmed(s) ? s.SecretAlert : null;
    }

    /// <summary>Indica si el aviso del hook ya se ha comprobado en el transcript sin encontrar nada pendiente.</summary>
    /// <remarks>
    /// Un análisis que empezó después del aviso, con el margen para que Claude Code escriba la línea, ya ha leído
    /// lo que se envió. Si de esa sesión no queda ningún secreto pendiente, el aviso no tiene hallazgo que rotar
    /// ni descartar, y sin esto se quedaría a la vista para siempre.
    /// </remarks>
    private bool Unconfirmed(SessionState s) =>
        s.SecretAlertUtc is { } at && LastScanUtc is { } scan && scan >= at + AfterAlert;

    /// <summary>Vuelve a contar los pendientes en el almacén; se llama tras cada análisis y tras cada marca.</summary>
    public void RecountPending()
    {
        int count;
        try
        {
            var scanner = new SecretScanner();
            var rotated = scanner.Rotated();
            count = scanner.Findings().Select(f => f.Fingerprint).Distinct().Count(f => !rotated.Contains(f));
            Settled = scanner.Settled();
        }
        catch (Exception ex)
        {
            JsonStore.LogError("security-pending", ex);
            return;
        }
        if (count == PendingCount) return;
        PendingCount = count;
        PendingChanged?.Invoke();
    }

    // Tras un aviso del hook, lo que tarda Claude Code en escribir esa línea en el transcript, con margen.
    private static readonly TimeSpan AfterAlert = TimeSpan.FromSeconds(5);
    private DateTime? _soonUtc;

    /// <summary>Pide un análisis en cuanto el transcript tenga la línea del aviso, sin esperar a que toque.</summary>
    /// <remarks>
    /// El hook avisa antes de que Claude Code escriba la línea, y el análisis solo lee líneas completas:
    /// por eso espera unos segundos. Si aún no estaba, la recoge el siguiente automático.
    /// </remarks>
    public void ScanSoon() => _soonUtc ??= DateTime.UtcNow + AfterAlert;

    /// <summary>Lanza el análisis automático si toca, o el pedido tras un aviso si ya ha pasado su espera.</summary>
    public void ScanIfDue()
    {
        if (Running) return;
        var asked = _soonUtc is { } at && DateTime.UtcNow >= at;
        if (!asked && _settings.SecurityScanUtc is { } last && DateTime.UtcNow - last < Every) return;
        _soonUtc = null;
        Start(full: false);
    }

    /// <summary>Analiza todos los transcripts del equipo, también los de antes de instalar la app.</summary>
    /// <remarks>Si hay un automático en marcha, se lanza en cuanto termine.</remarks>
    public void ScanAll()
    {
        if (FullRunning) return;
        FullRunning = true;
        if (Running)
        {
            _fullPending = true;
            Changed?.Invoke();
            return;
        }
        Start(full: true);
    }

    /// <summary>Analiza la configuración, los transcripts y el historial; en el automático, solo lo que ha cambiado desde el anterior.</summary>
    /// <returns>Si se ha analizado algo: si no, los hallazgos son los mismos.</returns>
    /// <remarks>
    /// Cada dos minutos, con nada nuevo, se interpretaba ~/.claude.json entero, se releía el historial y se
    /// volvía a escribir el almacén cifrado. Mirar fecha y tamaño de cada fichero basta para saber si toca.
    /// </remarks>
    private bool Analyze(bool full, DateTime since)
    {
        var scanner = new SecretScanner();
        var analyzed = false;

        var stateStamp = Stamp([SecretScanner.ClaudeStateFile, AppPaths.Backups]);
        if (full || stateStamp != _stateStamp) (_configFiles, _stateStamp) = (SecretScanner.ConfigFiles(), stateStamp);
        var configStamp = Stamp(_configFiles.Select(f => f.Path));
        if (full || configStamp != _configStamp)
        {
            scanner.ScanConfig(_configFiles);
            _configStamp = configStamp;
            analyzed = true;
        }

        var transcripts = (full ? SecretScanner.AllTranscripts() : SecretScanner.TranscriptsSince(since)).ToList();
        if (transcripts.Count > 0)
        {
            scanner.Scan(transcripts);
            analyzed = true;
        }

        var historyStamp = Stamp([SecretScanner.HistoryFile, SecretScanner.PasteCacheDir]);
        if (full || historyStamp != _historyStamp)
        {
            scanner.ScanHistory(SecretScanner.HistoryFile, SecretScanner.PasteCacheDir);
            _historyStamp = historyStamp;
            analyzed = true;
        }

        // Los pendientes de antes de guardar dónde aparecieron se buscan en su transcript, unos pocos por análisis, hasta
        // que no queda ninguno: así su paso lleva el escudo en Sesiones. Después no se vuelve a descifrar el almacén por esto.
        if (!_located)
        {
            var settled = Settled;
            var located = scanner.Locate(f => !settled.Contains(f.Fingerprint), LocatePerScan);
            _located = located == 0 && !scanner.AllFindings().Any(f => !f.Located && !f.Written && !SecretScanner.IsPrompt(f) && !settled.Contains(f.Fingerprint));
            analyzed |= located > 0;
        }
        return analyzed;
    }

    // Transcripts que se leen enteros en cada análisis para completar hallazgos antiguos.
    private const int LocatePerScan = 10;

    // Si ya no queda ningún pendiente sin saber dónde apareció.
    private bool _located;

    /// <summary>Huella de unos ficheros o carpetas: la fecha y el tamaño de cada uno, sin abrirlos.</summary>
    private static string Stamp(IEnumerable<string> paths) => string.Join("|", paths.Select(p =>
    {
        var file = new FileInfo(p);
        if (file.Exists) return $"{file.LastWriteTimeUtc.Ticks}:{file.Length}";
        return Directory.Exists(p) ? $"d{Directory.GetLastWriteTimeUtc(p).Ticks}" : "-";
    }));

    /// <summary>Analiza en segundo plano y, al terminar, apunta cuándo y avisa.</summary>
    private void Start(bool full)
    {
        Running = true;
        Changed?.Invoke();
        var started = DateTime.UtcNow;
        var since = (_settings.SecurityScanUtc ?? started.AddDays(-1)) - Overlap;
        Task.Run(() => Analyze(full, since))
            .ContinueWith(task =>
            {
                Running = false;
                if (task.Exception is { } ex)
                {
                    JsonStore.LogError("security-scan", ex.GetBaseException());
                    LastError = ex.GetBaseException().Message;
                }
                else
                {
                    LastError = null;
                    _settings.SecurityScanUtc = started;
                    if (full)
                    {
                        _settings.SecurityFullScanUtc = started;
                        _save();
                    }
                }
                if (full) FullRunning = false;
                // Sin nada nuevo que analizar, los pendientes son los mismos: no hace falta descifrar el almacén otra vez.
                if (task.Exception is not null || task.Result) RecountPending();
                if (_fullPending)
                {
                    _fullPending = false;
                    Start(full: true);
                    return;
                }
                Changed?.Invoke();
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }
}
