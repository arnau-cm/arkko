using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Arkko.Desktop;

/// <summary>Sección Tokens del panel: los tokens de la persona y el recordatorio de cada uno, que la app crea sola.</summary>
/// <remarks>
/// Lo escribe la persona a mano; la app no detecta nada aquí. Es aparte de Seguridad, que enseña lo que la
/// app ha encontrado en las conversaciones. Nunca se guarda el valor de un token.
/// </remarks>
public partial class TokensView : UserControl
{
    private readonly SecretPlanModel _model = new();
    private SecurityMonitor? _monitor;

    /// <summary>Crea la sección con los tokens guardados.</summary>
    public TokensView()
    {
        InitializeComponent();
        DataContext = _model;
        _model.Reload();
    }

    private Window? Owner => TopLevel.GetTopLevel(this) as Window;

    /// <summary>Se dispara al pedir ver en Seguridad un hallazgo de un token, con su huella.</summary>
    public event Action<string>? FindingRequested;

    /// <summary>Conecta la sección con el análisis de Seguridad, para poner al día su contador al marcar desde aquí.</summary>
    public void Init(SecurityMonitor monitor) => _monitor = monitor;

    /// <summary>Pone la sección al día al entrar, también lo que ha salido en Seguridad: tras cambiar un token, su variable ya tiene otro valor.</summary>
    public void Activate()
    {
        SecretPlanService.Instance.CheckLeaks();
        _model.Reload();
    }

    /// <summary>Enseña un token, desde Seguridad: su recordatorio si lo tiene y, si no, sus datos.</summary>
    public void ShowToken(string id)
    {
        if (_model.Cards.FirstOrDefault(c => c.Id == id) is not { } card) return;
        if (card.HasReminder) _model.ShowReminder(card);
        else _model.ShowCard(card);
    }

    /// <summary>Pasa a la pestaña de tokens, con el primero elegido.</summary>
    public void ShowCardsTab() => _model.SetTab(cards: true);

    /// <summary>Abre en Seguridad el hallazgo más reciente de un token.</summary>
    private void ShowLeak_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not CardItem card) return;
        if (SecretPlanService.Instance.PendingOf(card.Card).MaxBy(g => g.LastUtc) is { } last) FindingRequested?.Invoke(last.Fingerprint);
    }

    /// <summary>Abre el alta de un token: cuatro preguntas en el lado derecho.</summary>
    private void NewCard_Click(object? sender, RoutedEventArgs e) => _model.StartNew();

    /// <summary>Crea el token de las cuatro preguntas, con su recordatorio.</summary>
    private void CreateNew_Click(object? sender, RoutedEventArgs e) => _model.CreateFromDraft();

    /// <summary>Deja el alta sin crear nada.</summary>
    private void CancelNew_Click(object? sender, RoutedEventArgs e) => _model.CancelNew();

    /// <summary>Busca en las variables del equipo las que parecen tokens y enseña la lista.</summary>
    private void FindTokens_Click(object? sender, RoutedEventArgs e) => _model.StartDiscovery();

    /// <summary>Añade como tokens las variables marcadas.</summary>
    private void AddFound_Click(object? sender, RoutedEventArgs e) => _model.AddFromDiscovery();

    /// <summary>Cierra la lista de lo encontrado sin añadir nada.</summary>
    private void CancelFound_Click(object? sender, RoutedEventArgs e) => _model.CancelDiscovery();

    /// <summary>Abre, desde la cabecera, el recordatorio de lo que toca.</summary>
    private void Soon_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is CardItem card) _model.ShowReminder(card);
    }

    /// <summary>Da por cambiado hoy un token; si tiene hallazgos pendientes en Seguridad, pregunta si se marcan también como rotados.</summary>
    private async void PlanRotated_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not CardItem card) return;
        var pending = _model.PendingInSecurity(card);
        var mark = pending > 0 && await AppDialog.ConfirmAsync(Owner, "¿Marcarlo también en Seguridad?",
            (pending == 1 ? "1 hallazgo pendiente de Seguridad es" : $"{pending} hallazgos pendientes de Seguridad son") +
            $" de {card.Title}. Si ya lo has cambiado, se pueden marcar como rotados y dejan de contar como pendientes.",
            "Marcar también", DialogTone.Question, cancel: "Solo el token");
        _model.Rotated(card, mark);
        if (mark) _monitor?.RecountPending();
    }

    /// <summary>Abre bajo el botón el menú para mover la fecha del recordatorio de esta vez.</summary>
    /// <remarks>
    /// Si ya toca, se deja para más tarde contando desde hoy; si aún no, se mueve antes o después de su fecha,
    /// por ejemplo porque cae en vacaciones. Si está movido, también se puede volver a la fecha normal.
    /// </remarks>
    private void PlanMove_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: CardItem card } anchor) return;
        var menu = new ContextMenu { Placement = PlacementMode.Bottom };

        void Add(string label, Action action)
        {
            var entry = new MenuItem { Header = label };
            entry.Click += (_, _) => action();
            menu.Items.Add(entry);
        }

        if (card.IsDue)
        {
            Add("Mañana", () => _model.Snooze(card, 1));
            Add("En 3 días", () => _model.Snooze(card, 3));
            Add("En una semana", () => _model.Snooze(card, 7));
        }
        else
        {
            Add("Una semana antes", () => _model.MoveBy(card, -7));
            Add("Un día antes", () => _model.MoveBy(card, -1));
            Add("Un día después", () => _model.MoveBy(card, 1));
            Add("3 días después", () => _model.MoveBy(card, 3));
            Add("Una semana después", () => _model.MoveBy(card, 7));
        }
        if (card.IsMoved && card.Card.NextRotation is { } normal)
        {
            menu.Items.Add(new Separator());
            Add($"Volver a la fecha normal ({PlanDates.Text(normal)})", () => _model.ResetDate(card));
        }
        menu.Open(anchor);
    }

    /// <summary>Lleva del recordatorio a los datos de su token.</summary>
    private void PlanShowCard_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is CardItem card) _model.ShowCard(card);
    }

    /// <summary>Lleva del token a su recordatorio.</summary>
    private void PlanShowReminder_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is CardItem card) _model.ShowReminder(card);
    }

    /// <summary>Quita un token y su recordatorio, tras confirmarlo.</summary>
    private async void PlanDeleteCard_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not CardItem card) return;
        if (!await AppDialog.ConfirmAsync(Owner, $"¿Eliminar «{card.Title}»?",
                "Se borran sus datos y su recordatorio. El token de verdad, en su servicio, no se toca.", "Eliminar", DialogTone.Warning)) return;
        _model.Delete(card);
    }
}
