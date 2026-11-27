using System.Collections;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Los tokens de la app: uno solo, que leen la sección Tokens y la marca del menú.</summary>
/// <remarks>
/// Si el fichero está pero no se puede descifrar (otro usuario, dañado), no se guarda nada encima: se perdería
/// todo. Avisa de cada cambio, también cuando cambia el día, porque entonces cambia lo que toca.
/// </remarks>
public sealed class SecretPlanService
{
    public static SecretPlanService Instance { get; } = new();

    private readonly SecretPlanStore _store = new();
    private SecretPlan? _plan;
    private DateOnly _day;
    private Dictionary<string, SecretCard> _owners = new(StringComparer.Ordinal);
    private List<SecretGroup> _pending = [];
    private string _leakKey = "";

    /// <summary>Se dispara al guardar y al cambiar el día.</summary>
    public event Action? Changed;

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public SecretPlan Plan
    {
        get
        {
            Load();
            return _plan!;
        }
    }

    /// <summary>Indica si lo guardado no se ha podido leer: entonces se enseña vacío y no se guarda.</summary>
    public bool Unreadable { get; private set; }

    /// <summary>Cuántos tokens toca cambiar hoy o ya tocaba.</summary>
    public int DueCount => Unreadable ? 0 : Plan.DueCount(Today);

    /// <summary>Cuántos tokens toca cambiar en los próximos días.</summary>
    public int SoonCount => Unreadable ? 0 : Plan.SoonCount(Today);

    /// <summary>Lee lo guardado la primera vez que hace falta.</summary>
    private void Load()
    {
        if (_plan is not null) return;
        _day = Today;
        try
        {
            _plan = _store.Load();
        }
        catch (Exception ex) when (SecretPlanStore.IsUnreadable(ex) || ex is IOException or UnauthorizedAccessException)
        {
            JsonStore.LogError("secret-plan", ex);
            _plan = new SecretPlan();
            Unreadable = true;
        }
        MarkLeaks();
    }

    /// <summary>Guarda y avisa; no hace nada si lo guardado no se pudo leer.</summary>
    public void Save()
    {
        if (Unreadable) return;
        try
        {
            _store.Save(Plan);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            JsonStore.LogError("secret-plan", ex);
        }
        // Puede haber cambiado una variable o un enlace: lo que ha salido en Seguridad se vuelve a mirar.
        MarkLeaks();
        Changed?.Invoke();
    }

    /// <summary>Vuelve a mirar qué tokens han salido en Seguridad, y avisa si algo ha cambiado.</summary>
    /// <remarks>Se llama tras cada análisis de Seguridad y al entrar en Tokens: después de cambiar un token, su variable ya tiene otro valor.</remarks>
    public void CheckLeaks()
    {
        if (_plan is null || Unreadable) return;
        if (MarkLeaks()) Changed?.Invoke();
    }

    /// <summary>Marca los tokens que han salido en un hallazgo pendiente y apunta de qué token es cada huella; devuelve si ha cambiado algo.</summary>
    /// <remarks>
    /// Los hallazgos se leen descifrados solo en memoria, como en Seguridad. El valor de cada variable se lee solo
    /// para calcular su huella, y no se guarda ni se enseña.
    /// </remarks>
    private bool MarkLeaks()
    {
        try
        {
            var scanner = new SecretScanner();
            var rotated = scanner.Rotated();
            _pending = SecretReport.Group(scanner.Findings()).Where(g => !rotated.Contains(g.Fingerprint)).ToList();
            _owners = TokenLeaks.Mark(_plan!.Cards, _pending, ReadVariable, scanner.Fingerprint);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            JsonStore.LogError("token-leaks", ex);
            return false;
        }
        var key = string.Join("|", _plan.Cards.Select(c => $"{c.Id}:{c.Leaked}")) + "#" +
            string.Join("|", _owners.Select(o => $"{o.Key}:{o.Value.Id}").Order(StringComparer.Ordinal));
        if (key == _leakKey) return false;
        _leakKey = key;
        return true;
    }

    /// <summary>El valor actual de una variable de entorno: la del usuario, que es la que se cambia al rotar; si no, la del proceso o la del equipo.</summary>
    private static string? ReadVariable(string name) =>
        Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User)
        ?? Environment.GetEnvironmentVariable(name)
        ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine);

    /// <summary>Las variables de entorno del usuario y del equipo que parecen tokens y aún no son la variable de ninguno.</summary>
    /// <remarks>Sus valores se leen solo aquí, para decidir y para enseñarlos tapados; no se guardan.</remarks>
    public List<EnvCandidate> FindInEnvironment()
    {
        static IEnumerable<(string Name, string Value, bool Machine)> Read(EnvironmentVariableTarget target)
        {
            IDictionary variables;
            try
            {
                variables = Environment.GetEnvironmentVariables(target);
            }
            catch (System.Security.SecurityException)
            {
                yield break;
            }
            foreach (DictionaryEntry entry in variables)
                if (entry.Key is string name) yield return (name, entry.Value as string ?? "", target == EnvironmentVariableTarget.Machine);
        }

        return TokenDiscovery.Find(Read(EnvironmentVariableTarget.User).Concat(Read(EnvironmentVariableTarget.Machine)), Plan.Cards);
    }

    /// <summary>Si existe una variable de entorno con ese nombre y algún valor; el valor no sale de aquí.</summary>
    public static bool HasVariable(string name) => name.Trim().Length > 0 && !string.IsNullOrEmpty(ReadVariable(name.Trim()));

    /// <summary>El token de un hallazgo de Seguridad, por su huella; null si no es de ninguno.</summary>
    public SecretCard? TokenOf(string fingerprint)
    {
        Load();
        return _owners.GetValueOrDefault(fingerprint);
    }

    /// <summary>Los hallazgos pendientes en Seguridad que son de un token, también los de antes de su último cambio.</summary>
    public List<SecretGroup> PendingOf(SecretCard card)
    {
        Load();
        return _pending.Where(g => ReferenceEquals(_owners.GetValueOrDefault(g.Fingerprint), card)).ToList();
    }

    /// <summary>Apunta, a mano, que un hallazgo de Seguridad es un token; quita el enlace de cualquier otro.</summary>
    public void Link(string cardId, string fingerprint)
    {
        foreach (var card in Plan.Cards) card.Fingerprints.Remove(fingerprint);
        Plan.Cards.FirstOrDefault(c => c.Id == cardId)?.Fingerprints.Add(fingerprint);
        Save();
    }

    /// <summary>Quita el enlace hecho a mano entre un hallazgo y su token.</summary>
    public void Unlink(string fingerprint)
    {
        foreach (var card in Plan.Cards) card.Fingerprints.Remove(fingerprint);
        Save();
    }

    /// <summary>Marca como rotados en Seguridad unos hallazgos; el contador de Seguridad lo pone al día quien llama.</summary>
    public static void MarkRotated(IEnumerable<string> fingerprints)
    {
        var scanner = new SecretScanner();
        foreach (var fingerprint in fingerprints) scanner.SetRotated(fingerprint, true);
    }

    /// <summary>Avisa si ha cambiado el día desde la última vez: lo que toca puede ser otra cosa.</summary>
    public void CheckDay()
    {
        if (_plan is null || Today == _day) return;
        _day = Today;
        Changed?.Invoke();
    }
}

/// <summary>Fechas como se leen: «15-10-2026», y cuánto falta.</summary>
internal static class PlanDates
{
    public static string Text(DateOnly? date) => date?.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture) ?? "";

    /// <summary>«hoy», «mañana», «en 30 días» o «hace 3 días».</summary>
    public static string Relative(DateOnly date, DateOnly today) => (date.DayNumber - today.DayNumber) switch
    {
        0 => "hoy",
        1 => "mañana",
        -1 => "ayer",
        > 1 and var days => $"en {days} días",
        var days => $"hace {-days} días",
    };

    /// <summary>La fecha para un selector de fecha, que trabaja con DateTime.</summary>
    public static DateTime? Day(DateOnly? date) => date?.ToDateTime(TimeOnly.MinValue);

    public static DateOnly? Day(DateTime? date) => date is { } d ? DateOnly.FromDateTime(d) : null;
}
