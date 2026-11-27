using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Arkko.Core;

/// <summary>Añade y retira la statusline, los hooks y las reglas deny de la app en el settings.json de Claude Code.</summary>
/// <remarks>
/// Solo toca las entradas propias, reconocidas por el ejecutable al que llaman o, las reglas deny, por
/// la carpeta que protegen; el resto del fichero, incluidos otros hooks del mismo evento y otras
/// reglas, se conserva tal cual.
/// </remarks>
public static class SettingsInstaller
{
    /// <summary>Eventos que registra la app y el matcher de herramientas de cada uno.</summary>
    public static readonly (string Event, string? Matcher)[] Hooks =
    [
        ("SessionStart", null),
        ("UserPromptSubmit", null),
        ("PreToolUse", "AskUserQuestion|ExitPlanMode"),
        ("PostToolUse", "*"),
        ("Notification", null),
        ("Stop", null),
        ("SessionEnd", null),
    ];

    // Los comandos llevan la ruta entre comillas y con barras normales: «/ak"» es el ejecutable sin .exe de macOS y Linux.
    // Se reconocen también los de antes (ccstats y los scripts de PowerShell), para sustituirlos sin duplicar las entradas.
    private static readonly string[] OwnMarkers = ["/ak.exe\"", "/ak\"", "ccstats.exe", "/ccstats\"", "cc-statusline.ps1", "cc-prompt-usage.ps1"];

    /// <summary>Tipos de fichero con secretos que se recomienda que Claude no lea: nombre, por qué y sus reglas deny.</summary>
    /// <remarks>Un tipo puede llevar varias reglas, como los dos formatos de certificado; se activa o se quita entero.</remarks>
    public static readonly (string Name, string Why, string[] Rules)[] DenyGroups =
    [
        ("Ficheros .env", "Los .env de cualquier proyecto", ["Read(**/.env)"]),
        ("Variantes de .env", "Como .env.local o .env.production", ["Read(**/.env.*)"]),
        ("Certificados .pfx y .p12", "Llevan dentro su clave privada", ["Read(**/*.pfx)", "Read(**/*.p12)"]),
        ("Claves .key", "Claves privadas sueltas", ["Read(**/*.key)"]),
        ("Claves SSH", "Las de tu usuario, en ~/.ssh", ["Read(~/.ssh/**)"]),
        ("Credenciales de Claude Code", "Las de tu propia cuenta", ["Read(~/.claude/.credentials.json)"]),
        ("Secretos de usuario de .NET", "Los de dotnet user-secrets",
            [OperatingSystem.IsWindows() ? "Read(~/AppData/Roaming/Microsoft/UserSecrets/**)" : "Read(~/.microsoft/usersecrets/**)"]),
    ];

    // El resolver va explícito: sin él, ToJsonString falla en la ventana («must specify a TypeInfoResolver»).
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = JsonSerializerOptions.Default.TypeInfoResolver,
    };

    /// <summary>Devuelve el settings con la statusline y los hooks de la app, y los avisos del cambio.</summary>
    /// <param name="securityDir">Carpeta de los hallazgos de seguridad que Claude Code no debe leer; por defecto, la de la app.</param>
    /// <param name="backupsDir">Carpeta de las copias de settings.json, que Claude Code tampoco debe leer; por defecto, la de la app.</param>
    public static (JsonObject Settings, List<string> Warnings) Install(JsonObject settings, string exePath, string? securityDir = null,
        string? backupsDir = null)
    {
        var warnings = new List<string>();
        RemoveOwn(settings);

        var command = Quote(exePath);
        if (settings["statusLine"] is JsonObject existing)
            warnings.Add("Se sustituye la statusLine que había: " + existing.ToJsonString());
        settings["statusLine"] = new JsonObject
        {
            ["type"] = "command",
            ["command"] = command + " statusline",
            ["padding"] = 0,
        };

        var hooks = settings["hooks"] as JsonObject ?? new JsonObject();
        settings["hooks"] = hooks;
        foreach (var (evt, matcher) in Hooks)
        {
            var list = hooks[evt] as JsonArray ?? new JsonArray();
            hooks[evt] = list;
            var group = new JsonObject();
            if (matcher is not null) group["matcher"] = matcher;
            group["hooks"] = new JsonArray(new JsonObject
            {
                ["type"] = "command",
                ["command"] = command + " hook",
                ["timeout"] = 15,
            });
            list.Add(group);
        }
        AddDenyRules(settings, [SecurityDenyRule(securityDir ?? AppPaths.Security), BackupsDenyRule(backupsDir ?? AppPaths.Backups)]);
        return (settings, warnings);
    }

    /// <summary>Retira la statusline y los hooks de la app, de esta versión o de la de PowerShell.</summary>
    public static JsonObject Uninstall(JsonObject settings)
    {
        RemoveOwn(settings);
        return settings;
    }

    /// <summary>Indica si el settings ya tiene la statusline de la app.</summary>
    public static bool IsInstalled(JsonObject settings) =>
        settings["statusLine"] is JsonObject s && IsOwn(s["command"]);

    /// <summary>Regla deny que impide a Claude Code leer, y por tanto enviar, los hallazgos de seguridad.</summary>
    public static string SecurityDenyRule(string securityDir, string? home = null) => $"Read({RulePath(securityDir, home)}/**)";

    /// <summary>Regla deny que impide a Claude Code leer las copias de settings.json, que llevan las claves que tuviera el original.</summary>
    /// <remarks>Restaurar una copia no necesita leerla: basta con copiar el fichero.</remarks>
    public static string BackupsDenyRule(string backupsDir, string? home = null) => $"Read({RulePath(backupsDir, home)}/**)";

    /// <summary>Reglas deny que tiene el settings.</summary>
    public static List<string> DenyRules(JsonObject settings) =>
        settings["permissions"]?["deny"] is JsonArray deny
            ? deny.OfType<JsonValue>().Select(v => v.TryGetValue<string>(out var t) ? t : null).OfType<string>().ToList()
            : [];

    /// <summary>Añade al settings las reglas deny que aún no tenga.</summary>
    public static void AddDenyRules(JsonObject settings, IEnumerable<string> rules)
    {
        var permissions = settings["permissions"] as JsonObject ?? new JsonObject();
        settings["permissions"] = permissions;
        var deny = permissions["deny"] as JsonArray ?? new JsonArray();
        permissions["deny"] = deny;
        var present = DenyRules(settings).ToHashSet(StringComparer.Ordinal);
        foreach (var rule in rules)
            if (present.Add(rule)) deny.Add(rule);
    }

    /// <summary>Quita del settings unas reglas deny; las demás, también las puestas a mano, se quedan como estaban.</summary>
    public static void RemoveDenyRules(JsonObject settings, IEnumerable<string> rules)
    {
        if (settings["permissions"]?["deny"] is not JsonArray deny) return;
        var remove = rules.ToHashSet(StringComparer.Ordinal);
        for (var i = deny.Count - 1; i >= 0; i--)
            if (deny[i] is JsonValue v && v.TryGetValue<string>(out var text) && remove.Contains(text)) deny.RemoveAt(i);
    }

    /// <summary>Ruta con el formato de las reglas de permisos: «~/…» dentro del perfil y, fuera de él, «//c/…» en Windows y «//ruta» en el resto.</summary>
    /// <remarks>
    /// Claude Code compara las rutas de Windows pasadas a la forma POSIX, con la unidad en minúscula. Una ruta
    /// absoluta empieza por «//»: con una sola barra sería relativa a la carpeta del settings.json.
    /// </remarks>
    public static string RulePath(string path, string? home = null)
    {
        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var full = Path.GetFullPath(path).TrimEnd('\\', '/');
        var root = Path.GetFullPath(home).TrimEnd('\\', '/');
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (full.StartsWith(root + Path.DirectorySeparatorChar, comparison))
            return "~/" + full[(root.Length + 1)..].Replace('\\', '/');
        if (!OperatingSystem.IsWindows()) return "/" + full;
        return full.Length >= 2 && full[1] == ':'
            ? "//" + char.ToLowerInvariant(full[0]) + full[2..].Replace('\\', '/')
            : full.Replace('\\', '/');
    }

    /// <summary>Lee el settings.json; un fichero ausente equivale a uno vacío.</summary>
    public static JsonObject Load(string path)
    {
        if (!File.Exists(path)) return new JsonObject();
        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text)) return new JsonObject();
        var node = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });
        return node as JsonObject ?? throw new InvalidDataException("settings.json no contiene un objeto JSON.");
    }

    /// <summary>Guarda el settings.json, dejando antes una copia del original en data/backups.</summary>
    public static string? Save(string path, JsonObject settings)
    {
        string? backup = null;
        if (File.Exists(path))
        {
            Directory.CreateDirectory(AppPaths.Backups);
            backup = Path.Combine(AppPaths.Backups, $"settings-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(path, backup, overwrite: true);
        }
        JsonStore.WriteText(path, settings.ToJsonString(WriteOptions) + Environment.NewLine);
        return backup;
    }

    /// <summary>Quita del settings todas las entradas propias.</summary>
    private static void RemoveOwn(JsonObject settings)
    {
        if (settings["statusLine"] is JsonObject status && IsOwn(status["command"]))
            settings.Remove("statusLine");
        RemoveOwnDeny(settings);

        if (settings["hooks"] is not JsonObject hooks) return;
        foreach (var evt in hooks.Select(p => p.Key).ToList())
        {
            if (hooks[evt] is not JsonArray groups) continue;
            for (var g = groups.Count - 1; g >= 0; g--)
            {
                if (groups[g] is not JsonObject group || group["hooks"] is not JsonArray entries) continue;
                for (var h = entries.Count - 1; h >= 0; h--)
                {
                    if (entries[h] is JsonObject entry && IsOwn(entry["command"]))
                        entries.RemoveAt(h);
                }
                if (entries.Count == 0) groups.RemoveAt(g);
            }
            if (groups.Count == 0) hooks.Remove(evt);
        }
        if (hooks.Count == 0) settings.Remove("hooks");
    }

    /// <summary>Quita las reglas deny de la app, también las de otra carpeta de datos, y lo que quede vacío.</summary>
    private static void RemoveOwnDeny(JsonObject settings)
    {
        if (settings["permissions"] is not JsonObject permissions || permissions["deny"] is not JsonArray deny) return;
        for (var i = deny.Count - 1; i >= 0; i--)
        {
            if (deny[i] is JsonValue v && v.TryGetValue<string>(out var rule) && IsOwnDeny(rule))
                deny.RemoveAt(i);
        }
        if (deny.Count == 0) permissions.Remove("deny");
        if (permissions.Count == 0) settings.Remove("permissions");
    }

    /// <summary>Indica si una regla deny es de esta app: la de los hallazgos o la de las copias de settings.json.</summary>
    private static bool IsOwnDeny(string rule) =>
        rule.StartsWith("Read(", StringComparison.Ordinal) &&
        (rule.EndsWith("/data/security/**)", StringComparison.Ordinal) || rule.EndsWith("/data/backups/**)", StringComparison.Ordinal)) &&
        rule.Contains("ClaudeStats", StringComparison.OrdinalIgnoreCase);

    /// <summary>Indica si un comando pertenece a esta app.</summary>
    private static bool IsOwn(JsonNode? command) =>
        command is JsonValue value && value.TryGetValue<string>(out var text) && IsOwnCommand(text);

    /// <summary>Indica si el texto de un comando de hook o de statusline es de esta app, también uno de antes.</summary>
    public static bool IsOwnCommand(string? command) =>
        command is not null && OwnMarkers.Any(m => command.Contains(m, StringComparison.OrdinalIgnoreCase));

    /// <summary>El comando del hook Stop de la app, tal como está en el settings; null si no está.</summary>
    public static string? OwnStopCommand(JsonObject settings) =>
        settings["hooks"] is JsonObject hooks && hooks["Stop"] is JsonArray groups
            ? groups.OfType<JsonObject>().SelectMany(g => g["hooks"] as JsonArray ?? []).OfType<JsonObject>()
                .Select(e => e["command"] is JsonValue v && v.TryGetValue<string>(out var text) ? text : null)
                .FirstOrDefault(IsOwnCommand)
            : null;

    /// <summary>Los eventos que registra la app y que no tienen ya su entrada en el settings; vacío si están todos.</summary>
    public static List<string> MissingHooks(JsonObject settings) =>
        Hooks.Select(h => h.Event)
            .Where(evt => !(settings["hooks"] is JsonObject hooks && hooks[evt] is JsonArray groups &&
                            groups.OfType<JsonObject>().Any(g => g["hooks"] is JsonArray entries && entries.OfType<JsonObject>().Any(e => IsOwn(e["command"])))))
            .ToList();

    /// <summary>Ruta entre comillas y con barras normales, válida tanto en bash como en cmd.</summary>
    private static string Quote(string path) => "\"" + path.Replace('\\', '/') + "\"";
}
