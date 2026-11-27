using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Cómo se ve el Diario: cuántos días y cuáles.</summary>
public enum DiaryMode
{
    Day,
    WorkWeek,
    Week,
    Month,
    Agenda,
}

/// <summary>La vista del Diario que se recuerda entre arranques, en data/diary-view.json.</summary>
public sealed class DiaryViewState
{
    public DiaryMode Mode { get; set; } = DiaryMode.WorkWeek;

    /// <summary>Si el calendario por horas enseña solo la franja del horario laboral.</summary>
    public bool WorkHoursOnly { get; set; } = true;
}

/// <summary>Estado de la sección Diario: el rango que se ve, sus días ya calculados y el bloque elegido.</summary>
public sealed class DiaryModel : INotifyPropertyChanged
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
    private static string StateFile => Path.Combine(AppPaths.Data, "diary-view.json");

    private readonly DiaryViewState _state = JsonStore.Read<DiaryViewState>(StateFile) ?? new DiaryViewState();

    /// <summary>Si el Diario de la app enseña solo el horario laboral, como lo dejó la persona: con eso empieza un widget.</summary>
    public static bool WorkHoursOnlyDefault => (JsonStore.Read<DiaryViewState>(StateFile) ?? new DiaryViewState()).WorkHoursOnly;
    private DateTime _anchor = DateTime.Today;
    private bool _loading;
    private string _copied = "";
    private (DiaryDay Day, DiaryBlock Block)? _selected;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Se dispara al cambiar el rango que se ve, para leer sus días.</summary>
    public event Action? RangeChanged;

    /// <summary>Se dispara al cambiar algo que solo pide volver a pintar, como el horario laboral.</summary>
    public event Action? LookChanged;

    /// <summary>Se dispara al elegir o soltar un bloque, para pintar su ficha o el parte del día.</summary>
    public event Action? Picked;

    /// <summary>Los días que se ven, ya calculados, por fecha.</summary>
    public Dictionary<DateTime, DiaryDay> Days { get; } = new();

    public DiaryMode Mode
    {
        get => _state.Mode;
        set
        {
            if (_state.Mode == value) return;
            _state.Mode = value;
            Save();
            foreach (var name in new[] { nameof(IsDay), nameof(IsWorkWeek), nameof(IsWeek), nameof(IsMonth), nameof(IsAgenda),
                         nameof(GridVisible), nameof(PreviousTip), nameof(NextTip) })
                OnChanged(name);
            RangeMoved();
        }
    }

    public bool IsDay { get => Mode == DiaryMode.Day; set { if (value) Mode = DiaryMode.Day; } }
    public bool IsWorkWeek { get => Mode == DiaryMode.WorkWeek; set { if (value) Mode = DiaryMode.WorkWeek; } }
    public bool IsWeek { get => Mode == DiaryMode.Week; set { if (value) Mode = DiaryMode.Week; } }
    public bool IsMonth { get => Mode == DiaryMode.Month; set { if (value) Mode = DiaryMode.Month; } }
    public bool IsAgenda { get => Mode == DiaryMode.Agenda; set { if (value) Mode = DiaryMode.Agenda; } }

    /// <summary>Si se ve el calendario por horas: en Día y en las semanas.</summary>
    public bool GridVisible => Mode is DiaryMode.Day or DiaryMode.WorkWeek or DiaryMode.Week;

    /// <summary>Si el calendario por horas enseña solo la franja del horario laboral, o el día entero.</summary>
    public bool WorkHoursOnly
    {
        get => _state.WorkHoursOnly;
        set
        {
            if (_state.WorkHoursOnly == value) return;
            _state.WorkHoursOnly = value;
            Save();
            OnChanged();
            LookChanged?.Invoke();
        }
    }

    /// <summary>El horario laboral de Configuración → General, leído de nuevo en cada llamada para ver sus cambios.</summary>
    public static WorkSchedule Schedule => JsonStore.Read<AppSettings>(AppPaths.AppSettingsFile)?.WorkHours ?? new WorkSchedule();

    /// <summary>Un día cualquiera del rango que se ve.</summary>
    public DateTime Anchor
    {
        get => _anchor;
        set
        {
            if (_anchor == value.Date) return;
            _anchor = value.Date;
            RangeMoved();
        }
    }

    /// <summary>Los días que se ven, en orden: uno, de lunes a viernes o a domingo, o los del mes.</summary>
    public List<DateTime> Range => RangeOf(Mode, _anchor);

    /// <summary>Los días que vería una vista en torno a una fecha.</summary>
    public static List<DateTime> RangeOf(DiaryMode mode, DateTime anchor)
    {
        if (mode == DiaryMode.Day) return [anchor.Date];
        if (mode is DiaryMode.Month or DiaryMode.Agenda)
        {
            var first = new DateTime(anchor.Year, anchor.Month, 1);
            return Enumerable.Range(0, DateTime.DaysInMonth(first.Year, first.Month)).Select(i => first.AddDays(i)).ToList();
        }
        var monday = Monday(anchor);
        return Enumerable.Range(0, mode == DiaryMode.WorkWeek ? 5 : 7).Select(i => monday.AddDays(i)).ToList();
    }

    /// <summary>El lunes de la semana de una fecha.</summary>
    public static DateTime Monday(DateTime date) => date.Date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    /// <summary>El rango en una línea: «Lunes 26 oct 2026», «20 – 24 oct 2026» u «Octubre 2026».</summary>
    public string RangeText
    {
        get
        {
            if (Mode == DiaryMode.Day) return Capital(_anchor.ToString("dddd d MMM yyyy", Es));
            if (Mode is DiaryMode.Month or DiaryMode.Agenda) return Capital(_anchor.ToString("MMMM yyyy", Es));
            var range = Range;
            var (first, last) = (range[0], range[^1]);
            return first.Month == last.Month
                ? $"{first.Day} – {last.ToString("d MMM yyyy", Es)}"
                : $"{first.ToString("d MMM", Es)} – {last.ToString("d MMM yyyy", Es)}";
        }
    }

    public string PreviousTip => Mode switch
    {
        DiaryMode.Day => "Día anterior",
        DiaryMode.Month or DiaryMode.Agenda => "Mes anterior",
        _ => "Semana anterior",
    };

    public string NextTip => Mode switch
    {
        DiaryMode.Day => "Día siguiente",
        DiaryMode.Month or DiaryMode.Agenda => "Mes siguiente",
        _ => "Semana siguiente",
    };

    /// <summary>Mueve el rango un paso atrás o adelante: un día, una semana o un mes, según la vista.</summary>
    public void Move(int steps) => Anchor = Mode switch
    {
        DiaryMode.Day => _anchor.AddDays(steps),
        DiaryMode.Month or DiaryMode.Agenda => _anchor.AddMonths(steps),
        _ => _anchor.AddDays(7 * steps),
    };

    /// <summary>Abre un día en la vista Día, como al pulsarlo en el mes.</summary>
    public void OpenDay(DateTime day)
    {
        _anchor = day.Date;
        if (Mode == DiaryMode.Day) RangeMoved();
        else Mode = DiaryMode.Day;
    }

    /// <summary>Un texto con la primera letra en mayúscula.</summary>
    public static string Capital(string text) => text.Length == 0 ? text : char.ToUpper(text[0], Es) + text[1..];

    public bool Loading
    {
        get => _loading;
        set
        {
            if (_loading == value) return;
            _loading = value;
            OnChanged();
            OnChanged(nameof(NothingVisible));
            OnChanged(nameof(CanCopy));
        }
    }

    /// <summary>Si no hay nada que enseñar en los días que se ven, una vez leídos.</summary>
    public bool NothingVisible => !_loading && Range.All(d => !Days.TryGetValue(d, out var day) || day.Empty);

    public bool CanCopy => !_loading && Range.Any(d => Days.TryGetValue(d, out var day) && !day.Empty);

    /// <summary>Lo que dice la barra un momento tras copiar el parte.</summary>
    public string CopiedText
    {
        get => _copied;
        set
        {
            if (_copied == value) return;
            _copied = value;
            OnChanged();
        }
    }

    /// <summary>Pone los días leídos del rango, conservando el bloque elegido si sigue ahí.</summary>
    public void SetDays(IEnumerable<DiaryDay> days)
    {
        foreach (var day in days) Days[day.Day.Date] = day;
        if (_selected is { } s && Days.TryGetValue(s.Day.Day.Date, out var fresh))
            _selected = fresh.Blocks.FirstOrDefault(b => b.SessionId == s.Block.SessionId && b.StartUtc == s.Block.StartUtc) is { } block
                ? (fresh, block)
                : null;
        OnChanged(nameof(NothingVisible));
        OnChanged(nameof(CanCopy));
        SelectionChanged();
    }

    /// <summary>El parte de los días que se ven, en texto.</summary>
    public string Report() => Diary.Report(Range.Where(Days.ContainsKey).Select(d => Days[d]));

    /// <summary>El bloque elegido, con su día; null si no hay.</summary>
    public (DiaryDay Day, DiaryBlock Block)? Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            SelectionChanged();
        }
    }

    public bool DetailVisible => _selected is not null;

    /// <summary>Si se ve el parte del día a la derecha: en la vista Día, sin un bloque elegido.</summary>
    public bool ParteVisible => Mode == DiaryMode.Day && _selected is null;

    /// <summary>Avisa de que la ficha o el parte de la derecha han cambiado.</summary>
    private void SelectionChanged()
    {
        OnChanged(nameof(DetailVisible));
        OnChanged(nameof(ParteVisible));
        Picked?.Invoke();
    }

    /// <summary>Avisa de que el rango ha cambiado.</summary>
    private void RangeMoved()
    {
        _selected = null;
        OnChanged(nameof(DetailVisible));
        OnChanged(nameof(ParteVisible));
        OnChanged(nameof(RangeText));
        OnChanged(nameof(NothingVisible));
        OnChanged(nameof(CanCopy));
        RangeChanged?.Invoke();
    }

    /// <summary>Guarda la vista para el próximo arranque.</summary>
    private void Save()
    {
        try
        {
            JsonStore.Write(StateFile, _state);
        }
        catch (Exception ex)
        {
            JsonStore.LogError("diary", ex);
        }
    }

    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
