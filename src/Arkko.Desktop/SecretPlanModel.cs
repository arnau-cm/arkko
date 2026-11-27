using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Media;
using Avalonia.Threading;
using Arkko.Core;

namespace Arkko.Desktop;
/// <summary>Cada cuánto se cambia un token, elegido con botones: 1, 3 o 6 meses, 1 año, nunca u otro número de días.</summary>
public sealed class FrequencyPicker(int? days, Action<int?> changed) : INotifyPropertyChanged
{
    private static readonly int[] Presets = [30, 90, 180, 365];

    private int? _days = days;
    private bool _other = days is { } d && !Presets.Contains(d);
    private string _otherText = days is { } o && !Presets.Contains(o) ? o.ToString(CultureInfo.InvariantCulture) : "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public int? Days => _days;

    // Los botones no elegidos reciben false al marcar uno, y ese false no debe deshacer la elección.
    public bool IsMonth { get => Is(30); set { if (value) Pick(30); } }
    public bool IsQuarter { get => Is(90); set { if (value) Pick(90); } }
    public bool IsHalf { get => Is(180); set { if (value) Pick(180); } }
    public bool IsYear { get => Is(365); set { if (value) Pick(365); } }
    public bool IsNever { get => !_other && _days is null; set { if (value) Pick(null); } }

    public bool IsOther
    {
        get => _other;
        set
        {
            if (!value || _other) return;
            _other = true;
            _otherText = _days?.ToString(CultureInfo.InvariantCulture) ?? "";
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
    }

    /// <summary>Los días escritos con «Otro»; lo que no es un número mayor que cero no cambia nada.</summary>
    public string OtherDaysText
    {
        get => _otherText;
        set
        {
            _otherText = value ?? "";
            if (!int.TryParse(_otherText.Trim(), out var d) || d <= 0) return;
            _days = d;
            changed(d);
        }
    }

    public bool OtherVisible => _other;

    /// <summary>Indica si está elegido uno de los botones de días fijos.</summary>
    private bool Is(int days) => !_other && _days == days;

    /// <summary>Elige un botón: unos días fijos o «Nunca».</summary>
    private void Pick(int? days)
    {
        _other = false;
        _days = days;
        changed(days);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    /// <summary>«Cada 3 meses», «Cada 45 días» o «Sin recordatorio».</summary>
    public static string Text(int? days) => days switch
    {
        null => "Sin recordatorio",
        30 => "Cada mes",
        90 => "Cada 3 meses",
        180 => "Cada 6 meses",
        365 => "Cada año",
        var d => $"Cada {d} días",
    };
}

/// <summary>Un paso de una lista: con casilla para marcarlo mientras se hace, o sin ella cuando aún no toca.</summary>
public sealed class PlanStep(int index, string text, bool checkable, bool done, Action<int, bool> set) : INotifyPropertyChanged
{
    private bool _done = checkable && done;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Number => $"{index + 1}.";

    /// <summary>El texto del paso, con su formato: negrita, código y enlaces.</summary>
    public string Text => text;

    public bool BoxVisible => checkable;

    /// <summary>Hecho, el texto se apaga.</summary>
    public double TextOpacity => _done ? 0.5 : 1;

    public bool Done
    {
        get => _done;
        set
        {
            if (_done == value || !checkable) return;
            _done = value;
            set(index, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Done)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TextOpacity)));
        }
    }
}

/// <summary>Un token en la vista: sus datos para editar y su recordatorio, que sale de ellos.</summary>
public sealed class CardItem : INotifyPropertyChanged
{
    private readonly SecretPlanModel _owner;
    private string _steps;

    public CardItem(SecretCard card, SecretPlanModel owner)
    {
        Card = card;
        _owner = owner;
        _steps = string.Join(Environment.NewLine, card.RotationSteps);
        Frequency = new FrequencyPicker(card.RotateEveryDays, days => Edit(() => Card.RotateEveryDays = days));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SecretCard Card { get; }
    public string Id => Card.Id;
    public string Title => Card.Name.Trim().Length > 0 ? Card.Name.Trim() : "Token sin nombre";
    public string ReminderTitle => $"Cambiar {Title}";
    public FrequencyPicker Frequency { get; }

    public string Name { get => Card.Name; set => Edit(() => Card.Name = value ?? ""); }
    public string Use { get => Card.Use; set => Edit(() => Card.Use = value ?? ""); }
    public string Permissions { get => Card.Permissions; set => Edit(() => Card.Permissions = value ?? ""); }
    public string WhereToChange { get => Card.WhereToChange; set => Edit(() => Card.WhereToChange = value ?? ""); }
    public string WhereStored { get => Card.WhereStored; set => Edit(() => Card.WhereStored = value ?? ""); }

    /// <summary>Su variable de entorno; con ella, la app lo reconoce sola en Seguridad.</summary>
    public string EnvVar { get => Card.EnvVar; set => Edit(() => Card.EnvVar = (value ?? "").Trim()); }
    public string RotateNowIf { get => Card.RotateNowIf; set => Edit(() => Card.RotateNowIf = value ?? ""); }
    public DateTime? CreatedDay { get => PlanDates.Day(Card.Created); set => Edit(() => Card.Created = PlanDates.Day(value)); }
    public DateTime? LastRotatedDay { get => PlanDates.Day(Card.LastRotated); set => Edit(() => Card.LastRotated = PlanDates.Day(value)); }

    /// <summary>Los pasos para cambiarlo, uno por línea.</summary>
    public string StepsText
    {
        get => _steps;
        set
        {
            _steps = value ?? "";
            Edit(() =>
            {
                Card.RotationSteps = _steps.Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                Card.StepsDone.RemoveWhere(i => i >= Card.RotationSteps.Count);
            });
        }
    }

    /// <summary>Si sale en la lista de recordatorios: con frecuencia, o porque ha salido en Seguridad y toca cambiarlo ya.</summary>
    public bool HasReminder => Card.Due is not null || Card.Leaked;
    public bool IsDue => SecretPlan.IsDue(Card, SecretPlanService.Today);
    public bool IsSoon => SecretPlan.IsSoon(Card, SecretPlanService.Today);

    /// <summary>Rojo si toca, ámbar si toca pronto, verde si queda tiempo y gris sin recordatorio.</summary>
    public IBrush StateBrush => IsDue ? SecurityModel.PendingBrush : IsSoon ? SecurityModel.WrittenBrush : HasReminder ? SecurityModel.DoneBrush : SecurityModel.IgnoredBrush;

    /// <summary>Cuándo toca cambiarlo, en una frase.</summary>
    public string StateText
    {
        get
        {
            var today = SecretPlanService.Today;
            if (Card.Leaked) return "Toca cambiarlo ya: ha salido en Seguridad";
            if (Card.RotateEveryDays is null) return "Sin recordatorio: no se cambia cada cierto tiempo";
            if (Card.Due is not { } due) return "Falta la fecha de creación para saber cuándo toca";
            if (due < today) return $"Toca cambiarlo desde el {PlanDates.Text(due)} ({PlanDates.Relative(due, today)})";
            if (due == today) return "Toca cambiarlo hoy";
            if (IsMoved && Card.NextRotation is { } next)
                return $"Te lo recuerdo el {PlanDates.Text(due)} ({PlanDates.Relative(due, today)}); la fecha normal era el {PlanDates.Text(next)}";
            return $"Toca cambiarlo el {PlanDates.Text(due)} ({PlanDates.Relative(due, today)})";
        }
    }

    /// <summary>Indica si el recordatorio de esta vez se ha movido de su fecha normal.</summary>
    public bool IsMoved => Card.RemindOn is { } on && on != Card.NextRotation;

    /// <summary>La fecha de esta vez, para el calendario; al elegir otra, el recordatorio se mueve a ella.</summary>
    public DateTime? RemindDay
    {
        get => PlanDates.Day(Card.Due);
        set
        {
            if (PlanDates.Day(value) is { } day && day != Card.Due) _owner.MoveTo(this, day);
        }
    }

    /// <summary>Con lo que toca o ya ha pasado se deja para más tarde desde hoy; si no, se mueve desde su fecha.</summary>
    public string MoveText => IsDue ? "Recuérdamelo más tarde  ▾" : "Mover la fecha  ▾";

    /// <summary>Lo que ha salido en Seguridad no se aplaza: se cambia, o se marca allí si no hace falta.</summary>
    public bool MoveVisible => !Card.Leaked;

    public bool LeakVisible => Card.Leaked;

    /// <summary>Por qué ha salido en Seguridad: dónde apareció y cuándo, la última vez.</summary>
    public string LeakLine
    {
        get
        {
            var pending = SecretPlanService.Instance.PendingOf(Card);
            if (!Card.Leaked || pending.Count == 0) return "";
            var last = pending.MaxBy(g => g.LastUtc)!;
            var where = pending.Any(g => g.Sent) ? "Ha pasado por una conversación con Claude" : "Está escrito tal cual en un fichero de configuración";
            var count = pending.Count == 1 ? "1 hallazgo pendiente" : $"{pending.Count} hallazgos pendientes";
            return $"{where}, la última vez el {PlanDates.Text(DateOnly.FromDateTime(last.LastUtc.ToLocalTime()))}. En Seguridad: {count}.";
        }
    }

    /// <summary>Cómo lo reconoce la app en Seguridad: por su variable, por enlaces hechos a mano, o todavía no.</summary>
    public string RecognizeLine
    {
        get
        {
            var name = Card.EnvVar.Trim();
            var byVariable = name.Length == 0 ? null
                : SecretPlanService.HasVariable(name) ? $"Se reconoce en Seguridad por el valor de {name}."
                : $"No hay ninguna variable {name} en este equipo: así no se puede reconocer.";
            var byHand = Card.Fingerprints.Count switch
            {
                0 => null,
                1 => "Y por 1 hallazgo enlazado en Seguridad.",
                var n => $"Y por {n} hallazgos enlazados en Seguridad.",
            };
            return string.Join(" ", new[] { byVariable ?? (byHand is null ? "Aún no se reconoce en Seguridad: pon su variable, o enlázalo desde un hallazgo con «Es uno de mis tokens»." : null), byHand }
                .OfType<string>());
        }
    }

    /// <summary>Cada cuánto se cambia y cuándo fue la última vez.</summary>
    public string FrequencyLine => FrequencyPicker.Text(Card.RotateEveryDays) + " · " +
        (Card.LastRotated is { } last ? $"último cambio el {PlanDates.Text(last)}" : Card.Created is { } created ? $"creado el {PlanDates.Text(created)}, aún sin cambiar" : "aún sin cambiar");

    /// <summary>Los pasos: con casilla desde una semana antes de que toque, y como lista normal el resto del tiempo.</summary>
    public List<PlanStep> Steps => Card.RotationSteps
        .Select((s, i) => new PlanStep(i, s, IsDue || IsSoon, Card.StepsDone.Contains(i), (index, done) =>
        {
            if (done) Card.StepsDone.Add(index);
            else Card.StepsDone.Remove(index);
            _owner.Touch();
        }))
        .ToList();

    public string StepsHeader => IsDue || IsSoon ? "PASOS · MÁRCALOS MIENTRAS LOS HACES" : "PASOS";
    public bool StepsVisible => Card.RotationSteps.Count > 0;
    public bool NoStepsVisible => Card.RotationSteps.Count == 0;
    public bool WhereToChangeVisible => Card.WhereToChange.Trim().Length > 0;
    public bool RotateNowIfVisible => Card.RotateNowIf.Trim().Length > 0;
    public bool ReminderVisible => HasReminder;

    /// <summary>Su recordatorio, visto desde el token.</summary>
    public string ReminderLine => HasReminder ? "Recordatorio: " + char.ToLower(StateText[0], CultureInfo.CurrentCulture) + StateText[1..] : StateText;

    /// <summary>Aplica un cambio de un campo, repinta el token y pide guardar.</summary>
    private void Edit(Action change)
    {
        change();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        _owner.Touch();
    }

    /// <summary>Avisa de que pueden haber cambiado todos los datos que se pintan.</summary>
    public void NotifyAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}

/// <summary>Lo que se pregunta al crear un token: nombre, para qué sirve, cuándo se creó y cada cuánto cambiarlo.</summary>
public sealed class NewTokenDraft : INotifyPropertyChanged
{
    private string _name = "";

    public NewTokenDraft() => Frequency = new FrequencyPicker(90, _ => { });

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => _name;
        set
        {
            _name = value ?? "";
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanCreate)));
        }
    }

    public string Use { get; set; } = "";
    public string EnvVar { get; set; } = "";
    public DateTime? CreatedDay { get; set; } = DateTime.Today;
    public FrequencyPicker Frequency { get; }
    public bool CanCreate => _name.Trim().Length > 0;
}

/// <summary>Una variable del equipo que parece un token, en la lista para añadirla: su nombre, su valor tapado y de qué tipo parece.</summary>
public sealed class CandidateRow(EnvCandidate candidate, Action changed) : INotifyPropertyChanged
{
    private bool _chosen;

    public event PropertyChangedEventHandler? PropertyChanged;

    public EnvCandidate Candidate => candidate;
    public string Name => candidate.Name;
    public string Masked => candidate.Masked;
    public string Detail => (candidate.Kind ?? "Por su nombre") + " · " + (candidate.Machine ? "variable del equipo" : "variable de usuario");

    public bool Chosen
    {
        get => _chosen;
        set
        {
            if (_chosen == value) return;
            _chosen = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Chosen)));
            changed();
        }
    }
}

/// <summary>Lo encontrado al buscar en el equipo: las variables que parecen tokens, cuáles añadir y cada cuánto cambiarlas.</summary>
public sealed class DiscoveryDraft : INotifyPropertyChanged
{
    public DiscoveryDraft(List<EnvCandidate> found)
    {
        Rows = found.Select(c => new CandidateRow(c, () =>
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanAdd)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AddText)));
        })).ToList();
        Frequency = new FrequencyPicker(90, _ => { });
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public List<CandidateRow> Rows { get; }
    public FrequencyPicker Frequency { get; }
    public bool CanAdd => Rows.Any(r => r.Chosen);
    public string AddText => Rows.Count(r => r.Chosen) is var n && n == 1 ? "Añadir 1 token" : $"Añadir {n} tokens";
    public bool RowsVisible => Rows.Count > 0;
    public bool EmptyVisible => Rows.Count == 0;
}

/// <summary>La sección Tokens: los tokens de la persona y el recordatorio de cada uno, que se crea solo.</summary>
/// <remarks>
/// Editar un campo no rehace las listas, para no quitar el foco a quien escribe: solo se guarda, medio
/// segundo después del último cambio. Las acciones (crear, cambiado, más tarde, eliminar) sí las reordenan.
/// </remarks>
public sealed class SecretPlanModel : INotifyPropertyChanged
{
    private readonly SecretPlanService _service = SecretPlanService.Instance;
    private readonly DispatcherTimer _save = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private CardItem? _selectedCard;
    private CardItem? _selectedReminder;
    private NewTokenDraft? _draft;
    private DiscoveryDraft? _discovery;
    private string _notice = "";
    private bool _showCards;
    private bool _saving;

    public SecretPlanModel()
    {
        _save.Tick += (_, _) =>
        {
            _save.Stop();
            SaveNow();
        };
        _service.Changed += () =>
        {
            if (_saving) return;
            Reload();
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Los tokens, por nombre.</summary>
    public ObservableCollection<CardItem> Cards { get; } = [];

    /// <summary>Los recordatorios: un token con frecuencia cada uno, el que toca antes primero.</summary>
    public ObservableCollection<CardItem> Reminders { get; } = [];

    /// <summary>Lo que toca hoy o en los próximos días, para la cabecera.</summary>
    public ObservableCollection<CardItem> Soon { get; } = [];

    public CardItem? SelectedCard
    {
        get => _selectedCard;
        set
        {
            if (!ReferenceEquals(_selectedCard, value))
            {
                Notice = "";
                // Elegir otro token en la lista cancela el alta de uno nuevo y la búsqueda en el equipo.
                if (value is not null)
                {
                    Draft = null;
                    Discovery = null;
                }
            }
            _selectedCard = value;
            Notify(nameof(SelectedCard), nameof(CardDetailVisible), nameof(CardEmptyVisible));
        }
    }

    public CardItem? SelectedReminder
    {
        get => _selectedReminder;
        set
        {
            _selectedReminder = value;
            Notify(nameof(SelectedReminder), nameof(ReminderDetailVisible), nameof(ReminderEmptyVisible));
        }
    }

    /// <summary>El token que se está creando; null si no se está creando ninguno.</summary>
    public NewTokenDraft? Draft
    {
        get => _draft;
        private set
        {
            _draft = value;
            Notify(nameof(Draft), nameof(DraftVisible), nameof(CardDetailVisible), nameof(CardEmptyVisible));
        }
    }

    /// <summary>Lo encontrado al buscar tokens en el equipo; null si no se está buscando.</summary>
    public DiscoveryDraft? Discovery
    {
        get => _discovery;
        private set
        {
            _discovery = value;
            Notify(nameof(Discovery), nameof(DiscoveryVisible), nameof(CardDetailVisible), nameof(CardEmptyVisible));
        }
    }

    public bool DiscoveryVisible => _discovery is not null;

    /// <summary>Lo que se acaba de hacer, como «Creado. Recordatorio: …»; vacío si nada.</summary>
    public string Notice
    {
        get => _notice;
        private set
        {
            _notice = value;
            Notify(nameof(Notice), nameof(NoticeVisible));
        }
    }

    public bool NoticeVisible => _notice.Length > 0;
    public bool DraftVisible => _draft is not null;
    public bool CardDetailVisible => _draft is null && _discovery is null && _selectedCard is not null;
    public bool CardEmptyVisible => _draft is null && _discovery is null && _selectedCard is null;
    public bool ReminderDetailVisible => _selectedReminder is not null;
    public bool ReminderEmptyVisible => _selectedReminder is null;

    // PESTAÑAS: la no elegida recibe false al marcar la otra, y ese false no debe deshacer la elección.
    public bool ShowReminders { get => !_showCards; set { if (value) SetTab(cards: false); } }
    public bool ShowCards { get => _showCards; set { if (value) SetTab(cards: true); } }
    public bool RemindersVisible => !_showCards;
    public bool CardsVisible => _showCards;

    public string RemindersChip => $"Recordatorios ({Reminders.Count})";
    public string CardsChip => $"Tokens ({Cards.Count})";

    // CABECERA: qué toca hoy, qué toca pronto y cuántos hay.
    public string Headline => (_service.DueCount, _service.SoonCount) switch
    {
        (1, _) => "1 token por cambiar hoy",
        ( > 1, _) => $"{_service.DueCount} tokens por cambiar hoy",
        (_, 1) => "1 token por cambiar esta semana",
        (_, > 1) => $"{_service.SoonCount} tokens por cambiar esta semana",
        _ => "Nada que cambiar",
    };

    /// <summary>Lo que además toca esta semana, o lo siguiente, o por qué no hay nada.</summary>
    public string HeadlineDetail
    {
        get
        {
            var today = SecretPlanService.Today;
            if (_service.DueCount > 0 && _service.SoonCount is > 0 and var soon) return soon == 1 ? "Y 1 más esta semana." : $"Y {soon} más esta semana.";
            if (Reminders.FirstOrDefault(c => !c.IsDue && !c.IsSoon) is { Card.Due: { } due } next)
                return $"Lo siguiente: cambiar {next.Title} el {PlanDates.Text(due)} ({PlanDates.Relative(due, today)}).";
            return Cards.Count == 0
                ? "Apunta aquí tus tokens: la app te recordará cuándo cambiarlos."
                : "Ningún token tiene recordatorio: ponle «Cada cuánto cambiarlo» a los que quieras.";
        }
    }

    public bool SoonVisible => Soon.Count > 0;
    public string Totals => (Cards.Count == 1 ? "1 token" : $"{Cards.Count} tokens") + " · " +
        (Reminders.Count == 1 ? "1 con recordatorio" : $"{Reminders.Count} con recordatorio");

    public string CardsEmptyText => _service.Unreadable
        ? "No se han podido leer los tokens guardados: el detalle está en errors.log. No se guardará nada encima."
        : "Aún no hay ningún token. Pulsa «Nuevo token» y crea uno por cada token o clave de acceso: Git, una API, un servicio…";
    public bool CardsEmptyVisible => Cards.Count == 0;
    public string RemindersEmptyText => Cards.Count == 0
        ? "Aún no hay recordatorios. Se crean solos al crear un token con «Cada cuánto cambiarlo»."
        : "Ningún token tiene recordatorio. Ponle «Cada cuánto cambiarlo» a un token y su recordatorio saldrá aquí.";
    public bool RemindersEmptyVisible => Reminders.Count == 0;

    /// <summary>Vuelve a montar las listas con lo guardado, conservando lo elegido.</summary>
    public void Reload()
    {
        var card = _selectedCard?.Id;
        var reminder = _selectedReminder?.Id;
        var items = _service.Plan.Cards.Select(c => new CardItem(c, this)).ToList();
        Cards.Clear();
        foreach (var c in items.OrderBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase)) Cards.Add(c);
        Reminders.Clear();
        foreach (var c in items.Where(c => c.HasReminder).OrderBy(c => c.Card.Leaked ? 0 : 1).ThenBy(c => c.Card.Due)) Reminders.Add(c);
        Soon.Clear();
        foreach (var c in Reminders.Where(c => c.IsDue || c.IsSoon)) Soon.Add(c);
        _selectedCard = Cards.FirstOrDefault(c => c.Id == card) ?? Cards.FirstOrDefault();
        Notify(nameof(SelectedCard), nameof(CardDetailVisible), nameof(CardEmptyVisible));
        SelectedReminder = Reminders.FirstOrDefault(c => c.Id == reminder) ?? Reminders.FirstOrDefault();
        Notify(nameof(CardsChip), nameof(RemindersChip), nameof(CardsEmptyText), nameof(CardsEmptyVisible), nameof(RemindersEmptyText),
            nameof(RemindersEmptyVisible), nameof(Headline), nameof(HeadlineDetail), nameof(Totals), nameof(SoonVisible));
    }

    /// <summary>Pasa a la pestaña de recordatorios o a la de tokens.</summary>
    public void SetTab(bool cards)
    {
        if (_showCards == cards) return;
        _showCards = cards;
        Notify(nameof(ShowCards), nameof(ShowReminders), nameof(CardsVisible), nameof(RemindersVisible));
    }

    /// <summary>Abre los datos de un token, en la pestaña de tokens.</summary>
    public void ShowCard(CardItem card)
    {
        Draft = null;
        Discovery = null;
        SetTab(cards: true);
        SelectedCard = Cards.FirstOrDefault(c => c.Id == card.Id);
    }

    /// <summary>Abre el recordatorio de un token, en la pestaña de recordatorios.</summary>
    public void ShowReminder(CardItem card)
    {
        SetTab(cards: false);
        SelectedReminder = Reminders.FirstOrDefault(c => c.Id == card.Id);
    }

    /// <summary>Pide guardar en cuanto se deje de escribir.</summary>
    public void Touch()
    {
        _save.Stop();
        _save.Start();
    }

    /// <summary>Guarda ya, sin rehacer las listas: es un cambio propio.</summary>
    private void SaveNow()
    {
        _saving = true;
        try
        {
            _service.Save();
        }
        finally
        {
            _saving = false;
        }
        // Al guardar se vuelve a mirar Seguridad, por si se ha escrito una variable: cada token repinta su estado sin rehacer las listas.
        foreach (var card in Cards) card.NotifyAll();
        Notify(nameof(Headline), nameof(HeadlineDetail));
    }

    /// <summary>Guarda y vuelve a montar las listas, tras una acción que cambia el orden o lo que hay.</summary>
    private void Commit()
    {
        _save.Stop();
        SaveNow();
        Reload();
    }

    /// <summary>Empieza a crear un token: las cuatro preguntas, en el lado derecho.</summary>
    public void StartNew()
    {
        SetTab(cards: true);
        Notice = "";
        Discovery = null;
        Draft = new NewTokenDraft();
    }

    /// <summary>Busca en las variables del equipo las que parecen tokens y enseña la lista, en el lado derecho.</summary>
    public void StartDiscovery()
    {
        SetTab(cards: true);
        Notice = "";
        Draft = null;
        Discovery = new DiscoveryDraft(_service.FindInEnvironment());
    }

    /// <summary>Deja la búsqueda sin añadir nada.</summary>
    public void CancelDiscovery() => Discovery = null;

    /// <summary>Añade como tokens las variables marcadas, cada una con su variable y su recordatorio, y deja elegido el primero.</summary>
    public void AddFromDiscovery()
    {
        if (_discovery is not { CanAdd: true } found) return;
        var added = found.Rows.Where(r => r.Chosen).Select(r => new SecretCard
        {
            Name = r.Name,
            EnvVar = r.Name,
            WhereStored = r.Candidate.Machine ? $"Variable del equipo {r.Name}" : $"Variable de usuario {r.Name}",
            Created = SecretPlanService.Today,
            RotateEveryDays = found.Frequency.Days,
        }).ToList();
        _service.Plan.Cards.AddRange(added);
        Discovery = null;
        Commit();
        ShowCard(Cards.First(c => c.Id == added[0].Id));
        Notice = (added.Count == 1 ? "Añadido 1 token" : $"Añadidos {added.Count} tokens") +
            ", cada uno con su variable: la app los reconoce solos en Seguridad. Se han creado con fecha de hoy; si sabes cuándo se crearon de verdad, cámbiala en cada uno para que su recordatorio cuente bien.";
    }

    /// <summary>Deja de crear el token, sin guardar nada.</summary>
    public void CancelNew() => Draft = null;

    /// <summary>Crea el token de las preguntas, con su recordatorio si tiene frecuencia, y lo deja elegido con un aviso de cuándo toca.</summary>
    public void CreateFromDraft()
    {
        if (_draft is not { CanCreate: true } draft) return;
        var card = new SecretCard
        {
            Name = draft.Name.Trim(),
            Use = draft.Use.Trim(),
            EnvVar = draft.EnvVar.Trim(),
            Created = PlanDates.Day(draft.CreatedDay) ?? SecretPlanService.Today,
            RotateEveryDays = draft.Frequency.Days,
        };
        _service.Plan.Cards.Add(card);
        Draft = null;
        Commit();
        ShowCard(Cards.First(c => c.Id == card.Id));
        Notice = card.Due is { } due
            ? $"Creado. Su recordatorio, «Cambiar {card.Name}», toca el {PlanDates.Text(due)} ({PlanDates.Relative(due, SecretPlanService.Today)}). Cuando quieras, añade abajo dónde se cambia, dónde se guarda y los pasos."
            : "Creado, sin recordatorio. Si quieres que la app te avise, elige abajo «Cada cuánto cambiarlo».";
    }

    /// <summary>Borra un token y, con él, su recordatorio.</summary>
    public void Delete(CardItem card)
    {
        _service.Plan.Cards.Remove(card.Card);
        _selectedCard = null;
        Commit();
    }

    /// <summary>Da por cambiado hoy un token; si se pide, marca también como rotados en Seguridad sus hallazgos pendientes.</summary>
    /// <remarks>
    /// Las huellas de esos hallazgos se quedan apuntadas en el token: son de un valor anterior suyo, y así Seguridad
    /// sigue diciendo de quién son. No lo vuelven a poner en rojo, porque son de antes de este cambio.
    /// </remarks>
    public void Rotated(CardItem card, bool markInSecurity = false)
    {
        var pending = _service.PendingOf(card.Card).Select(g => g.Fingerprint).ToList();
        card.Card.Fingerprints.UnionWith(pending);
        SecretPlan.Rotated(card.Card, SecretPlanService.Today);
        if (markInSecurity && pending.Count > 0)
        {
            try
            {
                SecretPlanService.MarkRotated(pending);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
            {
                JsonStore.LogError("token-leaks", ex);
            }
        }
        Commit();
    }

    /// <summary>Cuántos hallazgos pendientes de Seguridad son de un token, para preguntar si se marcan al cambiarlo.</summary>
    public int PendingInSecurity(CardItem card) => _service.PendingOf(card.Card).Count;

    /// <summary>Deja el recordatorio de un token para dentro de unos días, contados desde hoy.</summary>
    public void Snooze(CardItem card, int days)
    {
        SecretPlan.Snooze(card.Card, SecretPlanService.Today, days);
        Commit();
    }

    /// <summary>Mueve el recordatorio de esta vez a otro día; después de que el calendario termine de cerrarse, porque rehace las listas.</summary>
    public void MoveTo(CardItem card, DateOnly day)
    {
        SecretPlan.MoveTo(card.Card, day, SecretPlanService.Today);
        Dispatcher.UIThread.Post(Commit);
    }

    /// <summary>Mueve el recordatorio de esta vez unos días antes (negativos) o después de su fecha.</summary>
    public void MoveBy(CardItem card, int days)
    {
        if (card.Card.Due is not { } due) return;
        SecretPlan.MoveTo(card.Card, due.AddDays(days), SecretPlanService.Today);
        Commit();
    }

    /// <summary>Devuelve el recordatorio de esta vez a su fecha normal.</summary>
    public void ResetDate(CardItem card)
    {
        card.Card.RemindOn = null;
        Commit();
    }

    private void Notify(params string[] names)
    {
        foreach (var name in names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
