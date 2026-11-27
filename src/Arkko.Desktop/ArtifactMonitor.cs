using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>El índice de lo publicado en claude.ai, compartido por la sección Artifacts, Sesiones, el Diario y Worktrees.</summary>
/// <remarks>
/// Al arrancar mira todos los transcripts, y después, cada minuto, los escritos desde la vez anterior; del índice solo se
/// lee lo nuevo de cada uno, así que la primera vez es la única que cuesta. Lee en segundo plano y avisa en el hilo de la
/// ventana.
/// </remarks>
public sealed class ArtifactMonitor
{
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(1);

    // Margen al mirar qué se ha escrito: la hora de modificación de un fichero puede ir un poco por detrás.
    private static readonly TimeSpan Overlap = TimeSpan.FromMinutes(1);

    private readonly ArtifactIndex _index = new();
    private DateTime? _scannedUtc;
    private bool _running;

    // Cuándo cambió por última vez la lista de la galería, para avisar también cuando la trae una sesión.
    private DateTime _galleryStamp;

    public static ArtifactMonitor Instance { get; } = new();

    /// <summary>Dónde deja una sesión de Claude Code la lista de la galería.</summary>
    public string GalleryFile => _index.GalleryFile;

    /// <summary>Todo lo publicado, también lo quitado, lo borrado y lo que solo está en la galería; vacío hasta la primera lectura.</summary>
    public IReadOnlyList<AgentArtifact> Items { get; private set; } = [];

    /// <summary>Si ya ha terminado la primera lectura.</summary>
    public bool Loaded { get; private set; }

    /// <summary>Se dispara en el hilo de la ventana cuando cambia la lista.</summary>
    public event Action? Changed;

    /// <summary>Lo que se ve de una sesión: lo publicado desde ella que no se ha quitado ni borrado.</summary>
    public IEnumerable<AgentArtifact> ForSession(string sessionId) =>
        Items.Where(a => !a.Removed && !a.Deleted && string.Equals(a.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Pone el índice al día si ha pasado un minuto desde la vez anterior, o si aún no se ha leído nunca.</summary>
    public void ScanIfDue()
    {
        if (_running || (_scannedUtc is { } last && DateTime.UtcNow - last < Every)) return;
        _running = true;
        var started = DateTime.UtcNow;
        var since = _scannedUtc - Overlap;
        Task.Run(() =>
            {
                var changed = _index.Scan(since is { } from ? SecretScanner.TranscriptsSince(from) : SecretScanner.AllTranscripts());
                var stamp = File.Exists(_index.GalleryFile) ? File.GetLastWriteTimeUtc(_index.GalleryFile) : default;
                return (Changed: changed || stamp != _galleryStamp, Stamp: stamp, Items: _index.AllWithGallery());
            })
            .ContinueWith(task =>
            {
                _running = false;
                _scannedUtc = started;
                if (task.Exception is { } ex)
                {
                    JsonStore.LogError("artifacts-scan", ex.GetBaseException());
                    return;
                }
                var first = !Loaded;
                Loaded = true;
                Items = task.Result.Items;
                _galleryStamp = task.Result.Stamp;
                if (first || task.Result.Changed) Changed?.Invoke();
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Quita algo de la lista o lo devuelve a ella, solo en la app.</summary>
    public void SetRemoved(AgentArtifact item, bool removed) => Change(item, a => a.Removed = removed);

    /// <summary>Cambia algo de lo publicado, solo en la app: quitarlo, o darle repositorio o clase a lo de la galería.</summary>
    public void Change(AgentArtifact item, Action<AgentArtifact> change)
    {
        try
        {
            _index.Change(item, change);
            Items = _index.AllWithGallery();
            Changed?.Invoke();
        }
        catch (IOException ex)
        {
            JsonStore.LogError("artifacts", ex);
        }
    }
}
