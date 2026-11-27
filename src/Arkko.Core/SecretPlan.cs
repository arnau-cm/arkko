using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Arkko.Core;

/// <summary>Un token: qué es, para qué sirve, dónde se cambia y dónde se guarda, y cada cuánto se cambia.</summary>
/// <remarks>Nunca lleva el valor del secreto: solo lo que hace falta para saber cuándo y cómo cambiarlo.</remarks>
public sealed class SecretCard
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Use { get; set; } = "";
    public string Permissions { get; set; } = "";
    public string WhereToChange { get; set; } = "";
    public string WhereStored { get; set; } = "";

    /// <summary>Variable de entorno donde vive su valor; la app lo lee solo para calcular su huella y reconocerlo en Seguridad, y lo olvida.</summary>
    public string EnvVar { get; set; } = "";

    /// <summary>Huellas de hallazgos de Seguridad que son este token: las enlazadas a mano y las de sus valores anteriores.</summary>
    public HashSet<string> Fingerprints { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Si ha salido en un hallazgo pendiente de Seguridad después de su último cambio; lo calcula la app y no se guarda.</summary>
    [JsonIgnore]
    public bool Leaked { get; set; }

    /// <summary>Cuándo conviene cambiarlo sin esperar a su fecha: una sospecha de filtración, un cambio de servidor…</summary>
    public string RotateNowIf { get; set; } = "";

    public DateOnly? Created { get; set; }
    public DateOnly? LastRotated { get; set; }

    /// <summary>Cada cuántos días se cambia; null si no tiene recordatorio («Nunca»).</summary>
    public int? RotateEveryDays { get; set; }

    public List<string> RotationSteps { get; set; } = [];

    /// <summary>Pasos del cambio en curso ya hechos, por su posición; se vacía al darlo por cambiado.</summary>
    public HashSet<int> StepsDone { get; set; } = [];

    /// <summary>La fecha de esta vez, si se ha movido antes o después de la normal; null para la normal.</summary>
    public DateOnly? RemindOn { get; set; }

    /// <summary>Cuándo toca cambiarlo: su último cambio, o su creación, más su frecuencia; null sin frecuencia o sin desde cuándo contar.</summary>
    [JsonIgnore]
    public DateOnly? NextRotation => RotateEveryDays is { } days && (LastRotated ?? Created) is { } from ? from.AddDays(days) : null;

    /// <summary>La fecha de su recordatorio: la normal o, si se ha movido, la de esta vez; null sin frecuencia.</summary>
    [JsonIgnore]
    public DateOnly? Due => RotateEveryDays is null ? null : RemindOn ?? NextRotation;
}

/// <summary>Los tokens de la persona y lo que se puede hacer con ellos.</summary>
/// <remarks>
/// El recordatorio de cada token no se guarda aparte: sale de su último cambio y de cada cuánto se cambia, así
/// que se crea al ponerle frecuencia, se mueve al cambiarlo y desaparece con «Nunca» o al borrarlo.
/// </remarks>
public sealed class SecretPlan
{
    /// <summary>Días antes de su fecha en que un token pasa a «toca pronto».</summary>
    public const int WarnDays = 7;

    public List<SecretCard> Cards { get; set; } = [];

    /// <summary>Cuántos tokens toca cambiar hoy o ya tocaba.</summary>
    public int DueCount(DateOnly today) => Cards.Count(c => IsDue(c, today));

    /// <summary>Cuántos tokens toca cambiar en los próximos días, sin contar los de hoy.</summary>
    public int SoonCount(DateOnly today) => Cards.Count(c => IsSoon(c, today));

    /// <summary>Indica si toca cambiarlo hoy o ya tocaba, o si ha salido en Seguridad: entonces toca ya, tenga o no frecuencia.</summary>
    public static bool IsDue(SecretCard card, DateOnly today) => card.Leaked || card.Due is { } due && due <= today;

    /// <summary>Indica si toca cambiarlo en los próximos días, sin contar hoy.</summary>
    public static bool IsSoon(SecretCard card, DateOnly today) => !card.Leaked && card.Due is { } due && due > today && due <= today.AddDays(WarnDays);

    /// <summary>Da por cambiado un token hoy: su recordatorio pasa a la siguiente fecha normal, sin pasos hechos.</summary>
    public static void Rotated(SecretCard card, DateOnly today)
    {
        card.LastRotated = today;
        card.RemindOn = null;
        card.StepsDone.Clear();
    }

    /// <summary>Mueve el recordatorio de esta vez a otro día, antes o después; nunca antes de hoy. A la fecha normal, deja de estar movido.</summary>
    public static void MoveTo(SecretCard card, DateOnly date, DateOnly today)
    {
        if (date < today) date = today;
        card.RemindOn = date == card.NextRotation ? null : date;
    }

    /// <summary>Deja el recordatorio para dentro de unos días, contados desde hoy.</summary>
    public static void Snooze(SecretCard card, DateOnly today, int days) => MoveTo(card, today.AddDays(Math.Max(1, days)), today);
}

/// <summary>Reconoce los tokens entre los hallazgos de Seguridad por su huella, sin guardar nunca su valor.</summary>
public static class TokenLeaks
{
    /// <summary>Las huellas de un token: las que tiene apuntadas y la del valor actual de su variable, si la tiene y existe.</summary>
    public static HashSet<string> FingerprintsOf(SecretCard card, Func<string, string?> readVariable, Func<string, string> fingerprint)
    {
        var all = new HashSet<string>(card.Fingerprints, StringComparer.Ordinal);
        if (card.EnvVar.Trim() is { Length: > 0 } name && readVariable(name) is { Length: > 0 } value) all.Add(fingerprint(value));
        return all;
    }

    /// <summary>Marca como filtrado cada token con algún hallazgo pendiente visto después de su último cambio, y dice de qué token es cada huella.</summary>
    /// <remarks>
    /// Un hallazgo de antes del último cambio es de un valor que ya no vale: no vuelve a poner el token en rojo,
    /// aunque en Seguridad siga pendiente. Si dos tokens tuvieran la misma huella, se queda con el primero.
    /// </remarks>
    public static Dictionary<string, SecretCard> Mark(IEnumerable<SecretCard> cards, IReadOnlyList<SecretGroup> pending,
        Func<string, string?> readVariable, Func<string, string> fingerprint)
    {
        var owners = new Dictionary<string, SecretCard>(StringComparer.Ordinal);
        foreach (var card in cards)
        {
            var mine = FingerprintsOf(card, readVariable, fingerprint);
            foreach (var fp in mine) owners.TryAdd(fp, card);
            card.Leaked = pending.Any(g => mine.Contains(g.Fingerprint) && (card.LastRotated is not { } last || DateOnly.FromDateTime(g.LastUtc.ToLocalTime()) > last));
        }
        return owners;
    }
}

/// <summary>Guarda los tokens cifrados para el usuario: dicen qué tokens hay y dónde, aunque no su valor.</summary>
public sealed class SecretPlanStore(string? dataDir = null)
{
    private readonly string _file = Path.Combine(dataDir ?? AppPaths.Security, "plan.bin");

    /// <summary>El plan guardado; vacío si no hay ninguno. Si está pero no se puede leer, lanza la excepción para no pisarlo.</summary>
    public SecretPlan Load()
    {
        if (!File.Exists(_file)) return new SecretPlan();
        return JsonSerializer.Deserialize<SecretPlan>(UserCipher.Unprotect(File.ReadAllBytes(_file)), JsonStore.Options) ?? new SecretPlan();
    }

    /// <summary>Guarda el plan entero, cifrado.</summary>
    public void Save(SecretPlan plan)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        JsonStore.WriteBytes(_file, UserCipher.Protect(JsonSerializer.SerializeToUtf8Bytes(plan, JsonStore.Options)));
    }

    /// <summary>Si existe el fichero del plan; para no confundir «sin plan» con «no se puede leer».</summary>
    public bool Exists => File.Exists(_file);

    /// <summary>Indica si un error al leer el plan es de un fichero que no se puede descifrar o entender.</summary>
    public static bool IsUnreadable(Exception ex) => ex is CryptographicException or JsonException;
}
