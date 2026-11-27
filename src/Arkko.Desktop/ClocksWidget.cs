using Avalonia;
using Avalonia.Controls;

namespace Arkko.Desktop;

/// <summary>La vista Relojes en un recuadro del corcho: un reloj por sesión abierta, agrupados por repositorio.</summary>
/// <remarks>
/// Lleva dentro la misma vista que el apartado (<see cref="ClocksView"/>), con su página y sus datos; la página elige su
/// tamaño por el ancho del recuadro, con los mismos cortes que los demás widgets. Solo lee las sesiones mientras se ve.
/// </remarks>
internal sealed class ClocksWidget : CorkWidget
{
    private readonly ClocksView _view = new();
    private readonly MainViewModel? _home;

    /// <summary>Crea el widget sobre las filas de Inicio, de donde salen el estado de cada sesión, sin el margen de los
    /// widgets: la página pone el suyo.</summary>
    public ClocksWidget(object? home)
    {
        Padding = new Thickness(0);
        _home = home as MainViewModel;
        if (_home is not null) _view.Init(_home, SessionFeed.OpenSessions);
        _view.SessionRequested += OpenSession;
    }

    public override PanelSection Section => PanelSection.Clocks;

    protected override void Started()
    {
        if (_home is not null) _view.Activate();
    }

    protected override void Stopped() => _view.Deactivate();

    protected override void Detached() => _view.Close();

    // La página se adapta sola al ancho: el control no cambia de forma con el tamaño.
    protected override object LayoutKey(double width) => 0;

    // Cada repintado mete lo que devuelve en un panel nuevo: la vista, que es siempre la misma, sale antes del anterior.
    protected override Control Build()
    {
        (_view.Parent as Panel)?.Children.Remove(_view);
        return _view;
    }

    /// <summary>Abre en el panel la sesión de un reloj, como el widget de Inicio al pulsar una tarjeta.</summary>
    private void OpenSession(string id)
    {
        if (_home?.Sessions.FirstOrDefault(r => string.Equals(r.SessionId, id, StringComparison.OrdinalIgnoreCase)) is { } row)
            SessionActions.ViewSession.Execute(row);
    }
}
