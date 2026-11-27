using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace Arkko.Core;

/// <summary>El informe de seguridad que escribe <c>check-security.ps1</c> de arkko-stack: qué está expuesto en el equipo y
/// en los repos, y qué avisos oficiales tocan al stack.</summary>
/// <remarks>
/// Es un contrato de arkko-stack (su <c>docs/seguridad.md</c>, sección «El informe»): Arkko lo lee y lo enseña, sin
/// reglas propias. Si el esquema cambia de número, Arkko dice que no lo conoce y no interpreta nada.
/// </remarks>
public sealed class StackReport
{
    [JsonPropertyName("esquema")] public int Schema { get; set; }
    [JsonPropertyName("generado")] public DateTimeOffset? Generated { get; set; }
    [JsonPropertyName("origen")] public StackOrigin? Origin { get; set; }
    [JsonPropertyName("exposicion")] public StackExposure? Exposure { get; set; }
    [JsonPropertyName("noticias")] public StackNews? News { get; set; }

    /// <summary>Las comprobaciones de la exposición; vacío si nunca se ha comprobado.</summary>
    [JsonIgnore] public IReadOnlyList<StackCheck> Checks => Exposure?.Checks ?? [];

    /// <summary>Los avisos de las noticias que tocan al stack; vacío si nunca se han mirado.</summary>
    [JsonIgnore] public IReadOnlyList<StackNotice> Notices => News?.Notices ?? [];

    /// <summary>Las fuentes de noticias con su estado; vacío si nunca se han mirado.</summary>
    [JsonIgnore] public IReadOnlyList<StackSource> Sources => News?.Sources ?? [];
}

/// <summary>De dónde sale el informe: el equipo, lo que se ejecutó esa vez y quién lo lanzó.</summary>
public sealed class StackOrigin
{
    [JsonPropertyName("equipo")] public string? Machine { get; set; }
    [JsonPropertyName("ambito")] public string? Scope { get; set; }
    [JsonPropertyName("lanzadoPor")] public string? LaunchedBy { get; set; }
}

/// <summary>La exposición: cuándo se comprobó y cada comprobación.</summary>
public sealed class StackExposure
{
    [JsonPropertyName("fecha")] public DateTimeOffset? Date { get; set; }
    [JsonPropertyName("comprobaciones")] public List<StackCheck> Checks { get; set; } = [];
}

/// <summary>Una comprobación: una pieza del equipo o de un repo, con su estado y, si falla, cómo se arregla.</summary>
public sealed class StackCheck
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("objeto")] public string? Object { get; set; }
    [JsonPropertyName("nombre")] public string? Name { get; set; }
    [JsonPropertyName("estado")] public string? State { get; set; }
    [JsonPropertyName("detalle")] public string? Detail { get; set; }
    [JsonPropertyName("arreglo")] public string? Fix { get; set; }

    /// <summary>Hasta cuándo tiene soporte la pieza, «aaaa-mm-dd»; solo en algunas comprobaciones, y opcional.</summary>
    [JsonPropertyName("finSoporte")] public string? SupportEnds { get; set; }

    /// <summary>La versión que se juzga, tal como la da la fuente; solo en los programas del equipo, y opcional.</summary>
    [JsonPropertyName("instalada")] public string? Installed { get; set; }

    /// <summary>La última versión de su línea, la que se instala para corregir; no viene en una FALLA por fin de soporte.</summary>
    [JsonPropertyName("ultima")] public string? Latest { get; set; }

    /// <summary>La gravedad más alta de lo encontrado: «critica», «alta», «media» o «baja»; solo en las dependencias, y opcional.</summary>
    /// <remarks>Puede venir con estado OK: una gravedad media no da FALLA.</remarks>
    [JsonPropertyName("gravedad")] public string? Severity { get; set; }

    /// <summary>El fin de soporte como fecha; null si no viene o no se entiende.</summary>
    [JsonIgnore] public DateOnly? SupportEndsDate => DateOnly.TryParseExact(SupportEnds, "yyyy-MM-dd", out var d) ? d : null;

    /// <summary>La gravedad como orden, de 0 (crítica) a 3 (baja); null si no viene o no se conoce.</summary>
    [JsonIgnore] public int? SeverityRank => Severity switch
    {
        StackSecurity.Critical => 0,
        StackSecurity.High => 1,
        StackSecurity.Medium => 2,
        StackSecurity.Low => 3,
        _ => null,
    };

    [JsonIgnore] public bool Failing => State == StackSecurity.Fail;
    [JsonIgnore] public bool Unchecked => State == StackSecurity.Unchecked;

    /// <summary>Si es del equipo y no de un repo.</summary>
    [JsonIgnore] public bool OfMachine => string.IsNullOrEmpty(Object) || Object == StackSecurity.Machine;
}

/// <summary>Las noticias: cuándo se miraron, las fuentes y los avisos que tocan al stack.</summary>
public sealed class StackNews
{
    [JsonPropertyName("fecha")] public DateTimeOffset? Date { get; set; }
    [JsonPropertyName("fuentes")] public List<StackSource> Sources { get; set; } = [];
    [JsonPropertyName("avisos")] public List<StackNotice> Notices { get; set; } = [];
}

/// <summary>Una fuente de avisos (INCIBE-CERT, CISA KEV, Node.js) y cómo respondió la última vez.</summary>
public sealed class StackSource
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("nombre")] public string? Name { get; set; }
    [JsonPropertyName("estado")] public string? State { get; set; }
    [JsonPropertyName("detalle")] public string? Detail { get; set; }

    /// <summary>La última vez que la fuente respondió; sin red, se conserva la anterior.</summary>
    [JsonPropertyName("consultada")] public DateTimeOffset? Checked { get; set; }
}

/// <summary>Un aviso oficial que toca a alguna pieza del stack.</summary>
public sealed class StackNotice
{
    [JsonPropertyName("fuente")] public string? Source { get; set; }
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("titulo")] public string? Title { get; set; }
    [JsonPropertyName("fecha")] public DateTimeOffset? Date { get; set; }
    [JsonPropertyName("enlace")] public string? Link { get; set; }
    [JsonPropertyName("piezas")] public List<string> Parts { get; set; } = [];
    [JsonPropertyName("visto")] public bool Seen { get; set; }

    /// <summary>Si es el tramo que el RSS de INCIBE no cubre: no es un aviso, sino unos días que hay que revisar en la web.</summary>
    [JsonIgnore] public bool IsGap => Id.StartsWith("INCIBE-HUECO-", StringComparison.Ordinal);
}

/// <summary>Qué relación hay entre la versión instalada y la otra que da el informe.</summary>
public enum VersionNote
{
    /// <summary>Falla, y la otra es la que lo arregla.</summary>
    Newer,

    /// <summary>La instalada es la última.</summary>
    Same,

    /// <summary>No falla, pero la última publicada es otra: sin comprobar, o una preliminar que va por delante.</summary>
    Published,

    /// <summary>Falla por fin de soporte: no hay versión de su línea que lo arregle.</summary>
    NoSupport,
}

/// <summary>Las versiones de una comprobación: la instalada y, si la hay, la otra que la explica.</summary>
public sealed record StackVersions(string Installed, string? Other, VersionNote Note);

/// <summary>Un trozo del arreglo: texto para leer, o código para copiar tal cual.</summary>
public sealed record FixPart(string Text, bool IsCode);

/// <summary>Cómo ha ido leer el informe.</summary>
public enum StackReportStatus
{
    Ok,
    Missing,
    Unreadable,
    UnknownSchema,
}

/// <summary>El resultado de leer el informe: el informe, si se ha podido, o por qué no.</summary>
/// <param name="Problem">Qué ha fallado, en una línea para la persona; null si se ha leído bien.</param>
public sealed record StackReportRead(StackReportStatus Status, StackReport? Report, string? Problem, string Path);

/// <summary>Dónde está el informe de seguridad de arkko-stack y su script, cómo se lee y cuándo toca volver a lanzarlo.</summary>
public static class StackSecurity
{
    /// <summary>El único esquema del informe que Arkko sabe leer.</summary>
    public const int Schema = 1;

    public const string Ok = "OK";
    public const string Fail = "FALLA";
    public const string Unchecked = "SIN COMPROBAR";

    public const string Critical = "critica";
    public const string High = "alta";
    public const string Medium = "media";
    public const string Low = "baja";

    /// <summary>El objeto de las comprobaciones del equipo; el resto llevan el nombre de su repo.</summary>
    public const string Machine = "equipo";

    public const string ScopeNews = "noticias";
    public const string ScopeExposure = "exposicion";
    public const string ScopeAll = "todo";

    /// <summary>Lo que aguantan las noticias antes de darse por viejas; es el umbral del hook de arkko-stack.</summary>
    public static readonly TimeSpan NewsMaxAge = TimeSpan.FromDays(1);

    /// <summary>Lo que aguanta la exposición antes de darse por vieja; es el umbral del hook de arkko-stack.</summary>
    public static readonly TimeSpan ExposureMaxAge = TimeSpan.FromDays(7);

    private const string FileName = "security.json";
    private const string FolderVariable = "ARKKO_STACK_SECURITY_DIR";
    private const string HomeVariable = "ARKKO_STACK_HOME";

    private static readonly JsonSerializerOptions ReportOptions = new() { PropertyNameCaseInsensitive = false };

    /// <summary>La carpeta del informe: la de <c>ARKKO_STACK_SECURITY_DIR</c> si existe; si no, la de arkko-stack en los
    /// datos locales del usuario.</summary>
    public static string ReportFolder() =>
        UserVariable(FolderVariable) is { Length: > 0 } custom && Directory.Exists(custom)
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "arkko-stack");

    /// <summary>La ruta del informe.</summary>
    public static string ReportFile() => Path.Combine(ReportFolder(), FileName);

    /// <summary>La carpeta de arkko-stack, de <c>ARKKO_STACK_HOME</c>; null si no está puesta.</summary>
    public static string? StackHome() => UserVariable(HomeVariable) is { Length: > 0 } home ? home : null;

    /// <summary>La ruta de <c>check-security.ps1</c>; null si no hay arkko-stack, no trae el script o no es Windows.</summary>
    /// <remarks>El script es de PowerShell y solo existe para Windows: en Mac y Linux, Arkko lee el informe pero no lo lanza.</remarks>
    public static string? ScriptPath()
    {
        if (!OperatingSystem.IsWindows() || StackHome() is not { } home) return null;
        var script = Path.Combine(home, "scripts", "check-security.ps1");
        return File.Exists(script) ? script : null;
    }

    /// <summary>Lee una variable de entorno del proceso o, si no la ha heredado, la del usuario en el registro.</summary>
    /// <remarks>Una variable puesta después de abrir la sesión de Windows no llega a los procesos que ya estaban
    /// abiertos, como Arkko arrancado al iniciar: el registro sí la tiene.</remarks>
    private static string? UserVariable(string name)
    {
        if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } value) return value;
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey("Environment");
            return key?.GetValue(name) as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    /// <summary>Lee el informe de su sitio, o de la ruta indicada.</summary>
    public static StackReportRead Read(string? path = null)
    {
        path ??= ReportFile();
        if (!File.Exists(path)) return new(StackReportStatus.Missing, null, "Todavía no hay informe de seguridad de arkko-stack", path);
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(StackReportStatus.Unreadable, null, "No se puede abrir el informe: " + ex.Message, path);
        }
        return Parse(json, path);
    }

    /// <summary>Interpreta el texto del informe: primero su esquema y, si es el que se conoce, todo lo demás.</summary>
    public static StackReportRead Parse(string json, string path = "")
    {
        try
        {
            using (var doc = JsonDocument.Parse(json))
            {
                if (!doc.RootElement.TryGetProperty("esquema", out var schema) || schema.ValueKind != JsonValueKind.Number || schema.GetInt32() != Schema)
                {
                    var number = doc.RootElement.TryGetProperty("esquema", out var s) ? s.ToString() : "sin número";
                    return new(StackReportStatus.UnknownSchema, null,
                        $"El informe tiene un esquema que Arkko no conoce ({number}); Arkko lee el {Schema}. Actualiza Arkko o arkko-stack.", path);
                }
            }
            var report = JsonSerializer.Deserialize<StackReport>(json, ReportOptions)!;
            Clean(report);
            return new(StackReportStatus.Ok, report, null, path);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            return new(StackReportStatus.Unreadable, null, "El informe no se entiende: " + ex.Message, path);
        }
    }

    /// <summary>Quita lo que el contrato no admite: comprobaciones o avisos sin id, y enlaces que no son https.</summary>
    /// <remarks>arkko-stack ya no deja pasar un enlace sin https; se comprueba igual, porque Arkko lo abre en el navegador.</remarks>
    private static void Clean(StackReport report)
    {
        report.Exposure?.Checks.RemoveAll(c => string.IsNullOrWhiteSpace(c.Id));
        report.News?.Sources.RemoveAll(s => string.IsNullOrWhiteSpace(s.Id));
        report.News?.Notices.RemoveAll(n => string.IsNullOrWhiteSpace(n.Id));
        foreach (var check in report.Checks)
        {
            check.Installed = Blank(check.Installed);
            check.Latest = Blank(check.Latest);
            check.Severity = Blank(check.Severity);
        }
        foreach (var notice in report.Notices)
            if (notice.Link is { } link && !link.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) notice.Link = null;
    }

    /// <summary>Deja un campo opcional vacío como ausente, que es lo que dice el contrato.</summary>
    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Lo que dicen las versiones de una comprobación, para enseñarlas en lugar de la frase; null si no dicen nada claro.</summary>
    /// <remarks>Sin versión instalada no hay nada que enseñar. Una FALLA sin última solo se explica por el fin de soporte; si
    /// no es eso, o si la última es la que ya está, la frase del informe dice mejor qué pasa.</remarks>
    public static StackVersions? Versions(StackCheck check, DateOnly today)
    {
        if (check.Installed is not { } installed) return null;
        if (check.Latest is not { } latest)
            return check.Failing && check.SupportEndsDate is { } end && end <= today ? new(installed, null, VersionNote.NoSupport) : null;
        if (latest == installed) return check.Failing ? null : new(installed, null, VersionNote.Same);
        return new(installed, latest, check.Failing ? VersionNote.Newer : VersionNote.Published);
    }

    /// <summary>Parte el arreglo en texto y código, el que va entre comillas invertidas; una comilla sin pareja se queda como texto.</summary>
    /// <remarks>El contrato solo admite comillas de un nivel y en pareja, sin otro Markdown: no se interpreta nada más.</remarks>
    public static IReadOnlyList<FixPart> FixParts(string? fix)
    {
        var parts = new List<FixPart>();
        if (string.IsNullOrEmpty(fix)) return parts;
        var at = 0;
        while (at < fix.Length)
        {
            var open = fix.IndexOf('`', at);
            var close = open < 0 ? -1 : fix.IndexOf('`', open + 1);
            if (close < 0)
            {
                parts.Add(new(fix[at..], false));
                break;
            }
            if (open > at) parts.Add(new(fix[at..open], false));
            if (close > open + 1) parts.Add(new(fix[(open + 1)..close], true));
            at = close + 1;
        }
        return parts;
    }

    /// <summary>El arreglo como frase, sin las comillas invertidas: para copiarlo entero o enseñarlo donde no hay código.</summary>
    public static string PlainFix(string? fix) => string.Concat(FixParts(fix).Select(p => p.Text));

    /// <summary>Indica si las noticias están viejas: nunca se han mirado o hace más de un día.</summary>
    public static bool NewsStale(StackReport? report, DateTime nowUtc) => Stale(report?.News?.Date, NewsMaxAge, nowUtc);

    /// <summary>Indica si la exposición está vieja: nunca se ha comprobado o hace más de siete días.</summary>
    public static bool ExposureStale(StackReport? report, DateTime nowUtc) => Stale(report?.Exposure?.Date, ExposureMaxAge, nowUtc);

    private static bool Stale(DateTimeOffset? date, TimeSpan maxAge, DateTime nowUtc) => date is not { } d || nowUtc - d.UtcDateTime > maxAge;

    /// <summary>Lo que toca lanzar según la antigüedad del informe: las noticias, la exposición, todo o nada.</summary>
    /// <remarks>Cuenta cualquier ejecución, también la de check-parity: solo mira las fechas del informe. Sin informe, todo.</remarks>
    public static string? DueScope(StackReport? report, DateTime nowUtc) => (NewsStale(report, nowUtc), ExposureStale(report, nowUtc)) switch
    {
        (true, true) => ScopeAll,
        (true, false) => ScopeNews,
        (false, true) => ScopeExposure,
        _ => null,
    };
}
