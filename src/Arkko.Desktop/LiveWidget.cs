using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Arkko.Desktop;

/// <summary>Un widget del corcho en directo, como «Te toca»: su página web con la foto común de las sesiones.</summary>
/// <remarks>
/// No tiene sección en el panel. La cabecera del recuadro sale de las filas de Inicio, que siempre están al día, aunque
/// el widget no se vea.
/// </remarks>
internal sealed class LiveWidget : CorkWidget
{
    // El contexto pasa a alto y a lleno a partir de estos porcentajes, como en la página.
    private const double ContextHigh = 70;
    private const double ContextFull = 90;

    // Las posiciones que ofrece el menú del carril, aunque no haya tantas sesiones abiertas.
    private const int Slots = 6;

    private readonly LiveView _view;
    private readonly MainViewModel? _home;
    private readonly string _widget;
    private string? _head;

    /// <summary>Crea el widget de la página por su nombre («tetoca», «pulso», «todas», «avisos», «carril»), sin el margen
    /// de los widgets: la página pone el suyo.</summary>
    public LiveWidget(string widget, object? home)
    {
        Padding = new Thickness(0);
        _widget = widget;
        _view = new LiveView(widget, faces: IsLane);
        _home = home as MainViewModel;
        _view.SessionRequested += OpenSession;
        _view.ReviewSeen += Pieces.MarkReviewed;
        if (IsLane) _view.SetOptions(new { slot = Slot });
    }

    // El carril enseña una sola sesión: la que está en su posición, por orden de apertura.
    private bool IsLane => _widget == "carril";

    /// <summary>La posición de la sesión que sigue el carril, por orden de apertura, desde 1.</summary>
    private int Slot => int.TryParse(Get("slot"), out var n) && n > 0 ? n : 1;

    /// <summary>La sesión que sigue el carril ahora; null si no hay tantas abiertas.</summary>
    private SessionRow? Followed() => _home is null ? null : LiveSessions.InOrder(_home.Sessions).ElementAtOrDefault(Slot - 1);

    public override PanelSection Section => PanelSection.Home;

    public override bool InPanel => false;

    /// <summary>En Avisos, rojo si un límite aprieta y ámbar si se llegaría al tope o una sesión tiene el contexto lleno; en
    /// los demás, ámbar si alguna sesión espera respuesta y verde si solo hay revisiones. Nada si no hay de qué avisar.</summary>
    public override IBrush? Mood
    {
        get
        {
            var sessions = _home?.Sessions ?? [];
            if (IsLane)
            {
                var row = Followed();
                return row is { Waiting: true } ? Brushes_.Waiting : row is { NeedsReview: true } ? Brushes_.Done : null;
            }
            if (_widget == "avisos")
            {
                var limits = _home?.Limits ?? [];
                if (limits.Any(l => l.Pct >= HomeWidget.Pressing)) return Brushes_.High;
                return limits.Any(l => l.Projected >= 100) || sessions.Any(s => s.ContextPct >= ContextFull) ? Brushes_.Waiting : null;
            }
            if (sessions.Any(s => s.Waiting)) return Brushes_.Waiting;
            return sessions.Any(s => s.NeedsReview) ? Brushes_.Done : null;
        }
    }

    /// <summary>En «Te toca», cuántas cosas te tocan; en Avisos, el aviso más urgente; en el carril, la sesión que sigue;
    /// en los demás, cuántas sesiones trabajan de las abiertas.</summary>
    public override string? Headline
    {
        get
        {
            var sessions = _home?.Sessions ?? [];
            if (IsLane)
                return Followed() is { } row ? (row.Worktree is { Length: > 0 } where ? $"{row.Title} · {where}" : row.Title) : $"sin {Slot}.ª sesión";
            if (_widget == "avisos") return Alert(sessions);
            if (_widget == "tetoca")
            {
                var count = sessions.Count(s => s.Waiting || s.NeedsReview);
                return count == 0 ? "nada pendiente" : count == 1 ? "1 te toca" : $"{count} te tocan";
            }
            if (sessions.Count == 0) return "sin sesiones";
            var busy = sessions.Count(s => s.Working || s.Agents || s.Command);
            return $"{busy} de {sessions.Count} {(busy == 1 ? "trabaja" : "trabajan")}";
        }
    }

    /// <summary>El aviso más urgente, para la cabecera: un límite que aprieta, uno que llegaría al tope antes de renovarse o
    /// las sesiones con el contexto alto.</summary>
    private string Alert(IReadOnlyList<SessionRow> sessions)
    {
        var limits = _home?.Limits ?? [];
        if (limits.Where(l => l.Pct >= HomeWidget.Pressing).OrderByDescending(l => l.Pct).FirstOrDefault() is { } tight)
            return $"{tight.Label} al {tight.PctText}";
        if (limits.FirstOrDefault(l => l.Projected >= 100) is { } full) return $"{full.Label}: tope antes de renovarse";
        var high = sessions.Count(s => s.ContextPct >= ContextHigh);
        return high == 0 ? "sin avisos" : high == 1 ? "1 con contexto alto" : $"{high} con contexto alto";
    }

    /// <summary>En el carril, a qué sesión sigue: una por posición, con la que está ahí ahora.</summary>
    public override IEnumerable<Control> MenuItems()
    {
        if (!IsLane) yield break;
        yield return Ui.Caption("Seguir a");
        var rows = _home is null ? [] : LiveSessions.InOrder(_home.Sessions);
        for (var i = 1; i <= Math.Max(Slots, rows.Count); i++)
        {
            var slot = i;
            var row = rows.ElementAtOrDefault(i - 1);
            var name = row is null ? $"La {i}.ª sesión abierta" : $"La {i}.ª · {row.Title}{(row.Worktree is { Length: > 0 } w ? " · " + w : "")}";
            yield return Ui.Check(name, Slot == i, () =>
            {
                Set("slot", slot == 1 ? null : slot.ToString(System.Globalization.CultureInfo.InvariantCulture));
                _view.SetOptions(new { slot = Slot });
                _head = null;
                Render();
            });
        }
    }

    protected override void Restored()
    {
        if (IsLane) _view.SetOptions(new { slot = Slot });
    }

    protected override void Started() => _view.Activate();

    protected override void Stopped() => _view.Deactivate();

    protected override void Attached()
    {
        if (_home is not null) _home.PropertyChanged += OnHomeChanged;
    }

    protected override void Detached()
    {
        if (_home is not null) _home.PropertyChanged -= OnHomeChanged;
        _view.Close();
    }

    /// <summary>Cambian las sesiones de Inicio: si cambia lo que dice la cabecera, se repinta para que el recuadro lo recoja.</summary>
    private void OnHomeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(MainViewModel.Sessions) or nameof(MainViewModel.Limits))) return;
        var head = $"{(Mood as ISolidColorBrush)?.Color}:{Headline}";
        if (head == _head) return;
        _head = head;
        Render();
    }

    // La página se adapta sola al ancho: el control no cambia de forma con el tamaño.
    protected override object LayoutKey(double width) => 0;

    // Cada repintado mete lo que devuelve en un panel nuevo: la vista, que es siempre la misma, sale antes del anterior.
    protected override Control Build()
    {
        (_view.Parent as Panel)?.Children.Remove(_view);
        return _view;
    }

    /// <summary>Abre en el panel una sesión, como el widget de Inicio al pulsar una tarjeta.</summary>
    private void OpenSession(string id)
    {
        if (_home?.Sessions.FirstOrDefault(r => string.Equals(r.SessionId, id, StringComparison.OrdinalIgnoreCase)) is { } row)
            SessionActions.ViewSession.Execute(row);
    }
}
