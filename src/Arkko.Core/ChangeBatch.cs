namespace Arkko.Core;

/// <summary>Junta en un aviso los cambios que llegan seguidos: avisa cuando pasa un rato sin ninguno, pero no más tarde
/// de un máximo desde el primero de la tanda, para que un cambio largo se vea igual al poco.</summary>
/// <remarks>Lo usan los vigilantes de ficheros, que reciben del sistema un aviso por cada escritura.</remarks>
internal sealed class ChangeBatch : IDisposable
{
    private readonly object _gate = new();
    private readonly TimeSpan _quiet;
    private readonly TimeSpan _longest;
    private readonly Action _fire;
    private Timer? _wait;
    private DateTime _firstUtc;
    private bool _disposed;

    /// <summary>Prepara el agrupado con sus plazos y lo que hace al avisar, que se llama en un hilo de fondo.</summary>
    public ChangeBatch(TimeSpan quiet, TimeSpan longest, Action fire) => (_quiet, _longest, _fire) = (quiet, longest, fire);

    /// <summary>Apunta un cambio: avisa cuando pase un rato sin otro, o como mucho al cumplirse el máximo de la tanda.</summary>
    public void Poke()
    {
        lock (_gate)
        {
            if (_disposed) return;
            var now = DateTime.UtcNow;
            if (_wait is null)
            {
                _firstUtc = now;
                _wait = new Timer(_ => Fire(), null, _quiet, Timeout.InfiniteTimeSpan);
                return;
            }
            var left = _longest - (now - _firstUtc);
            _wait.Change(left < _quiet ? (left > TimeSpan.Zero ? left : TimeSpan.Zero) : _quiet, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Deja de avisar, también de la tanda que esté esperando.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _wait?.Dispose();
            _wait = null;
        }
    }

    /// <summary>Cierra la tanda y avisa.</summary>
    private void Fire()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _wait?.Dispose();
            _wait = null;
        }
        _fire();
    }
}
