using System.Diagnostics;
using Avalonia.Threading;

namespace Arkko.Desktop;

/// <summary>Animación de un valor de 0 a 1 en un tiempo, con curva suave, para lo que se mueve desde el código.</summary>
/// <remarks>
/// Las animaciones de estilo de Avalonia van bien para lo que se repite (una gota que late); para mover una
/// tarjeta concreta desde donde estaba hasta su sitio, es más sencillo avanzar un valor a mano.
/// </remarks>
public sealed class Tween
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
    private readonly Stopwatch _clock = new();

    private Tween(TimeSpan duration, Func<double, double> ease, Action<double> step, Action? done)
    {
        _timer.Tick += (_, _) =>
        {
            var t = Math.Min(1, _clock.Elapsed.TotalMilliseconds / duration.TotalMilliseconds);
            step(ease(t));
            if (t < 1) return;
            Stop();
            done?.Invoke();
        };
    }

    /// <summary>Arranca una animación que llama a <paramref name="step"/> con el avance ya suavizado.</summary>
    public static Tween Run(TimeSpan duration, Action<double> step, Action? done = null, bool easeOutOnly = false)
    {
        var tween = new Tween(duration, easeOutOnly ? EaseOut : EaseInOut, step, done);
        step(0);
        tween._clock.Start();
        tween._timer.Start();
        return tween;
    }

    /// <summary>Corta la animación donde esté, sin llegar al final.</summary>
    public void Stop()
    {
        _timer.Stop();
        _clock.Stop();
    }

    /// <summary>Curva cúbica que acelera y frena.</summary>
    private static double EaseInOut(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;

    /// <summary>Curva cúbica que solo frena al llegar.</summary>
    private static double EaseOut(double t) => 1 - Math.Pow(1 - t, 3);
}
