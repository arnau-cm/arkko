using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Avalonia.Media;
using Arkko.Core;

namespace Arkko.Desktop;
/// <summary>Estado de la categoría Tableros: las fichas de las conexiones, la que se edita y el patrón de la clave.</summary>
public sealed class BoardsModel(BoardStore store) : INotifyPropertyChanged
{
    // Colores para distinguir conexiones: los de datos del tema, los mismos que los de los proyectos.
    public static readonly string[] Palette = ["#61AFEF", "#E5C07B", "#98C379", "#C678DD", "#56B6C2", "#F09A76"];

    private BoardSettings _settings = new();
    private ConnectionEditor? _editor;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ConnectionRow> Connections { get; } = [];
    public ObservableCollection<ProfileRow> Profiles { get; } = [];
    public ConnectionEditor? Editor => _editor;

    private bool _importing;
    private bool _helpOpen;
    private string _importText = "";
    private string _importStatus = "";
    private IBrush _importBrush = Brushes.Transparent;
    private string? _importedId;
    private string _copyNote = "";

    public bool ProfilesVisible => !_importing;
    public bool NoProfilesVisible => Profiles.Count == 0;
    public bool ImportVisible => _importing;
    public bool HelpVisible => _helpOpen;
    public string Template => BoardProfileHelp.Template;
    public bool CanAdd => Profiles.Count > 0;
    public string AddNote => "Importa primero el perfil de tu panel, arriba, para poder conectarlo.";
    public bool AddNoteVisible => Profiles.Count == 0;

    public bool HelpOpen
    {
        get => _helpOpen;
        set
        {
            _helpOpen = value;
            _copyNote = "";
            OnChanged(nameof(HelpVisible));
            OnChanged(nameof(CopyNote));
        }
    }

    public string CopyNote
    {
        get => _copyNote;
        set
        {
            _copyNote = value;
            OnChanged();
        }
    }

    /// <summary>El perfil pegado: se comprueba al escribir y dice qué hace o por qué no vale.</summary>
    public string ImportText
    {
        get => _importText;
        set
        {
            _importText = value;
            OnChanged();
            Check();
        }
    }

    public string ImportStatus => _importStatus;
    public IBrush ImportBrush => _importBrush;
    public bool CanSaveProfile => _importedId is not null;

    /// <summary>Id del perfil pegado, si es válido.</summary>
    public string? ImportedId => _importedId;

    /// <summary>Abre el cuadro de importar, vacío.</summary>
    public void StartImport()
    {
        _importing = true;
        _importText = "";
        OnChanged(nameof(ImportText));
        Check();
        OnImportChanged();
    }

    /// <summary>Cierra el cuadro de importar sin guardar.</summary>
    public void CancelImport()
    {
        _importing = false;
        OnImportChanged();
    }

    /// <summary>Guarda el perfil pegado y cierra el cuadro.</summary>
    public void SaveImport()
    {
        try
        {
            store.SaveProfile(_importText);
        }
        catch (Exception ex) when (ex is BoardException or IOException or UnauthorizedAccessException)
        {
            SetImport(ex.Message, ThemeColors.Of("Danger", "#E03E4C"), null);
            return;
        }
        _importing = false;
        RefreshProfiles();
        RefreshRows();
        OnImportChanged();
    }

    public bool HasProfile(string id) => store.HasProfile(id);
    public string? ProfileText(string id) => store.ProfileText(id);

    /// <summary>Nombres de las conexiones que usan un perfil.</summary>
    public List<string> UsedBy(string id) =>
        _settings.Connections.Where(c => string.Equals(c.Profile, id, StringComparison.OrdinalIgnoreCase)).Select(c => c.Name).ToList();

    /// <summary>Borra un perfil importado.</summary>
    public void DeleteProfile(string id)
    {
        store.DeleteProfile(id);
        RefreshProfiles();
    }

    /// <summary>Comprueba el perfil pegado y deja a la vista su resumen o el motivo por el que no vale.</summary>
    private void Check()
    {
        if (string.IsNullOrWhiteSpace(_importText))
        {
            SetImport("", Brushes.Transparent, null);
            return;
        }
        try
        {
            var profile = BoardProfile.Parse(_importText);
            var replaces = store.HasProfile(profile.Id) ? " Ya hay un perfil con este id: al guardarlo se sustituye." : "";
            SetImport($"✓ {profile.Name}: {profile.Summary()}.{replaces}", ThemeColors.Of("DataHaiku", "#98C379"), profile.Id);
        }
        catch (BoardException ex)
        {
            SetImport(ex.Message, ThemeColors.Of("Danger", "#E03E4C"), null);
        }
    }

    private void SetImport(string text, IBrush brush, string? id)
    {
        _importStatus = text;
        _importBrush = brush;
        _importedId = id;
        OnChanged(nameof(ImportStatus));
        OnChanged(nameof(ImportBrush));
        OnChanged(nameof(CanSaveProfile));
    }

    /// <summary>Rehace las fichas de los perfiles importados.</summary>
    private void RefreshProfiles()
    {
        Profiles.Clear();
        foreach (var p in store.Profiles()) Profiles.Add(new ProfileRow(p));
        foreach (var name in new[] { nameof(NoProfilesVisible), nameof(CanAdd), nameof(AddNoteVisible) }) OnChanged(name);
    }

    private void OnImportChanged()
    {
        foreach (var name in new[] { nameof(ProfilesVisible), nameof(ImportVisible) }) OnChanged(name);
    }

    public bool ListVisible => _editor is null;
    public bool EditorVisible => _editor is not null;
    public bool EmptyVisible => Connections.Count == 0;

    // Vista del Tablero: cómo se ve el subtablero de cada tarea.
    public bool IsStripe { get => _settings.TasksView.Style == "Stripe"; set { if (value) SetStyle("Stripe"); } }
    public bool IsSubtle { get => _settings.TasksView.Style == "Subtle"; set { if (value) SetStyle("Subtle"); } }
    public bool IsLanes { get => _settings.TasksView.Style == "Lanes"; set { if (value) SetStyle("Lanes"); } }

    private void SetStyle(string style)
    {
        _settings.TasksView.Style = style;
        Save();
        foreach (var name in new[] { nameof(IsStripe), nameof(IsSubtle), nameof(IsLanes) }) OnChanged(name);
    }

    /// <summary>Patrón con que se busca la clave de la tarea en una rama; se guarda solo si es una expresión válida.</summary>
    public string TaskPattern
    {
        get => _settings.TaskPattern;
        set
        {
            var pattern = string.IsNullOrWhiteSpace(value) ? BoardSettings.DefaultTaskPattern : value.Trim();
            if (IsValid(pattern))
            {
                _settings.TaskPattern = pattern;
                Save();
            }
            _patternDraft = pattern;
            OnChanged(nameof(PatternText));
            OnChanged(nameof(PatternBrush));
        }
    }

    private string? _patternDraft;

    /// <summary>Cómo funciona el patrón escrito, con un ejemplo; o por qué no vale.</summary>
    public string PatternText
    {
        get
        {
            var pattern = _patternDraft ?? _settings.TaskPattern;
            if (!IsValid(pattern)) return "No es una expresión regular válida: se sigue usando la anterior.";
            const string sample = "feature/DEVLAB-99-mesa";
            return new BoardSettings { TaskPattern = pattern }.TaskKeyIn(sample) is { } key
                ? $"En «{sample}» encuentra {key}"
                : $"En «{sample}» no encuentra ninguna clave";
        }
    }

    public IBrush PatternBrush => IsValid(_patternDraft ?? _settings.TaskPattern) ? ThemeColors.Of("FgFaint", "#8A8D9C") : ThemeColors.Of("Danger", "#E03E4C");

    /// <summary>La regla que reconoce un prompt que añade una tarea a la sesión; vacía, no se añaden por comandos.</summary>
    public string TaskSwitch
    {
        get => _settings.TaskSwitch;
        set
        {
            var rule = value?.Trim() ?? "";
            if (rule.Length == 0 || IsValid(rule))
            {
                _settings.TaskSwitch = rule;
                Save();
            }
            _switchDraft = rule;
            OnChanged(nameof(SwitchText));
            OnChanged(nameof(SwitchBrush));
        }
    }

    private string? _switchDraft;

    /// <summary>Cómo funciona la regla escrita, con dos ejemplos; o por qué no vale.</summary>
    public string SwitchText
    {
        get
        {
            var rule = _switchDraft ?? _settings.TaskSwitch;
            if (rule.Length == 0) return "Apagado: ningún prompt añade tareas.";
            if (!IsValid(rule)) return "No es una expresión regular válida: se sigue usando la anterior.";
            var test = new BoardSettings { TaskPattern = _settings.TaskPattern, TaskSwitch = rule };
            const string command = "/opsx:apply DEVLAB-87";
            const string mention = "revisa lo de DEVLAB-45";
            var a = test.SwitchKeyIn(command) is { } k1 ? $"«{command}» añade {k1}" : $"«{command}» no añade nada";
            var b = test.SwitchKeyIn(mention) is { } k2 ? $"«{mention}» añade {k2}" : $"«{mention}» solo la sugiere";
            return $"{a}; {b}";
        }
    }

    public IBrush SwitchBrush => (_switchDraft ?? _settings.TaskSwitch) is var r && (r.Length == 0 || IsValid(r))
        ? ThemeColors.Of("FgFaint", "#8A8D9C")
        : ThemeColors.Of("Danger", "#E03E4C");

    /// <summary>Lee la configuración y rehace las fichas; cierra el editor.</summary>
    public void Load()
    {
        _settings = store.Load();
        _patternDraft = null;
        _switchDraft = null;
        _editor = null;
        _importing = false;
        RefreshProfiles();
        RefreshRows();
        OnImportChanged();
        OnChanged(nameof(TaskPattern));
        foreach (var name in new[] { nameof(IsStripe), nameof(IsSubtle), nameof(IsLanes) }) OnChanged(name);
        foreach (var name in new[] { nameof(TaskSwitch), nameof(SwitchText), nameof(SwitchBrush) }) OnChanged(name);
        OnChanged(nameof(PatternText));
        OnChanged(nameof(PatternBrush));
    }

    /// <summary>Añade una conexión con un perfil, con un nombre y un color que no repiten los de las demás.</summary>
    public void Add(BoardProfile profile)
    {
        var used = _settings.Connections.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = profile.Name;
        for (var n = 2; used.Contains(name); n++) name = $"{profile.Name} {n}";
        var color = Palette.FirstOrDefault(c => !_settings.Connections.Any(x => x.Color.Equals(c, StringComparison.OrdinalIgnoreCase))) ?? Palette[0];
        var connection = new BoardConnection { Profile = profile.Id, Name = name, Color = color };
        _settings.Connections.Add(connection);
        Save();
        Edit(connection);
    }

    /// <summary>Abre el editor de una conexión.</summary>
    public void Edit(BoardConnection connection)
    {
        if (store.ProfileOf(connection) is not { } profile) return;
        _editor = new ConnectionEditor(connection, profile, store, Save);
        OnEditorChanged();
    }

    /// <summary>Cierra el editor y pone al día las fichas.</summary>
    public void CloseEditor()
    {
        _editor = null;
        RefreshRows();
    }

    /// <summary>Quita una conexión y sus claves.</summary>
    public void Delete(BoardConnection connection)
    {
        _settings.Connections.Remove(connection);
        store.RemoveSecrets(connection.Id);
        Save();
        RefreshRows();
    }

    /// <summary>Rehace las fichas de las conexiones.</summary>
    public void RefreshRows()
    {
        Connections.Clear();
        var profiles = Profiles.Select(p => p.Profile).ToList();
        foreach (var c in _settings.Connections)
            Connections.Add(new ConnectionRow(c, profiles.FirstOrDefault(p => string.Equals(p.Id, c.Profile, StringComparison.OrdinalIgnoreCase)),
                profiles.Count > 1));
        OnEditorChanged();
    }

    /// <summary>Guarda la configuración; un fallo se anota sin interrumpir.</summary>
    private void Save()
    {
        try
        {
            store.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            JsonStore.LogError("boards", ex);
        }
    }

    private void OnEditorChanged()
    {
        foreach (var name in new[] { nameof(Editor), nameof(ListVisible), nameof(EditorVisible), nameof(EmptyVisible) }) OnChanged(name);
    }

    /// <summary>Indica si un texto es una expresión regular válida.</summary>
    private static bool IsValid(string pattern)
    {
        try
        {
            _ = new Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Ficha de un perfil importado: su nombre y lo que hace, en una frase.</summary>
public sealed class ProfileRow(BoardProfile profile)
{
    public BoardProfile Profile => profile;
    public string Name => profile.Name;
    public string Summary => profile.Summary() + ".";
}

/// <summary>Ficha de una conexión: su nombre y color, de qué perfil es, qué tableros ve y si funciona.</summary>
/// <param name="profile">Su perfil; null si se ha eliminado o ya no se puede leer.</param>
/// <param name="severalProfiles">Si hay más de un perfil importado: solo entonces se dice cuál usa.</param>
public sealed class ConnectionRow(BoardConnection connection, BoardProfile? profile, bool severalProfiles)
{
    public BoardConnection Connection => connection;
    public string Name => connection.Name;
    public IBrush ColorBrush => BoardColors.BrushOf(connection.Color);
    public string ProfileText => profile is null ? "   sin perfil: impórtalo de nuevo" : severalProfiles ? "   perfil: " + profile.Name : "";

    /// <summary>Servidor y tableros que se ven: «panel.example.com · DEV › WEB, API».</summary>
    public string Detail
    {
        get
        {
            var host = connection.Values.GetValueOrDefault("baseUrl") ?? profile?.Settings.FirstOrDefault(s => s.Key == "baseUrl")?.Default;
            var where = Uri.TryCreate(host, UriKind.Absolute, out var uri) ? uri.Host : host ?? "";
            var boards = connection.Boards.GroupBy(b => b.ProjectKey)
                .Select(g => g.Any(b => b.BoardId is null) ? $"{g.Key} (entero)" : $"{g.Key} › {string.Join(", ", g.Select(b => b.BoardName))}")
                .ToList();
            return boards.Count == 0 ? $"{where} · sin tableros elegidos" : $"{where} · {string.Join(" · ", boards)}";
        }
    }

    public string StatusText => connection.VerifiedUtc is { } at ? $"Conectada · probada {Format.Ago(at)}" : "Sin probar: no sale en el Tablero hasta que la prueba vaya bien";
    public IBrush StatusBrush => connection.VerifiedUtc is null ? ThemeColors.Of("FgFaint", "#8A8D9C") : ThemeColors.Of("DataHaiku", "#98C379");
}

/// <summary>Un color para elegir el de la conexión, con su aro si es el elegido.</summary>
public sealed class ColorChoice(string hex, bool on)
{
    public string Hex => hex;
    public IBrush Brush => BoardColors.BrushOf(hex);
    public IBrush Ring => on ? ThemeColors.Of("Fg", "#E6E6E6") : Brushes.Transparent;
}

/// <summary>Editor de una conexión: nombre, color, ajustes del perfil, certificado, prueba y tableros que se ven.</summary>
public sealed class ConnectionEditor : INotifyPropertyChanged
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    private readonly BoardConnection _connection;
    private readonly BoardProfile _profile;
    private readonly BoardStore _store;
    private readonly Action _save;
    private ServerCertificate? _seen;
    private string _testText = "";
    private IBrush _testBrush = Brushes.Transparent;
    private bool _testing;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Prepara el editor de una conexión; si ya funcionó alguna vez, carga sus proyectos para elegir.</summary>
    public ConnectionEditor(BoardConnection connection, BoardProfile profile, BoardStore store, Action save)
    {
        _connection = connection;
        _profile = profile;
        _store = store;
        _save = save;
        foreach (var setting in profile.Settings) Fields.Add(new SettingField(setting, connection, store, save));
        if (connection.VerifiedUtc is not null) _ = TestAsync();
    }

    public string ProfileName => _profile.Name;
    public ObservableCollection<SettingField> Fields { get; } = [];
    public ObservableCollection<ProjectChoice> Projects { get; } = [];
    public List<ColorChoice> Palette => BoardsModel.Palette.Select(c => new ColorChoice(c, c.Equals(_connection.Color, StringComparison.OrdinalIgnoreCase))).ToList();

    public string Name
    {
        get => _connection.Name;
        set
        {
            _connection.Name = string.IsNullOrWhiteSpace(value) ? _profile.Name : value.Trim();
            _save();
        }
    }

    /// <summary>Cambia el color de la conexión.</summary>
    public void SetColor(string hex)
    {
        _connection.Color = hex;
        _save();
        OnChanged(nameof(Palette));
    }

    public bool CanTest => !_testing;
    public string TestText => _testText;
    public IBrush TestBrush => _testBrush;
    public bool ProjectsVisible => Projects.Count > 0;

    // El bloque del certificado sale si el servidor enseñó uno que el equipo no reconoce, o si ya se confía en uno propio.
    public bool CertificateVisible =>
        _seen is { TrustedByWindows: false } || _connection.PinnedSha256 is not null || _connection.RootCertificate is not null;

    private bool TrustsOwn => _connection.PinnedSha256 is not null || _connection.RootCertificate is not null;
    private bool PinMatches => _seen is not null && _connection.PinnedSha256 is { } pin && Same(pin, _seen.Fingerprint);
    private bool PinChanged => _seen is { TrustedByWindows: false } && _connection.PinnedSha256 is not null && !PinMatches;

    public string CertificateTitle =>
        PinChanged ? "El certificado del servidor ha cambiado desde que confiaste en él"
        : PinMatches ? "Confías en este certificado"
        : _connection.RootCertificate is not null ? $"Confías en la raíz «{_connection.RootName ?? "propia"}»"
        : "Este equipo no reconoce el certificado del servidor";

    public string CertificateHint =>
        PinChanged ? "Puede ser una renovación normal o alguien haciéndose pasar por el servidor. Compruébalo con quien lo opera antes de confiar en el nuevo."
        : TrustsOwn ? "Solo se acepta este servidor con este certificado; el nombre tiene que coincidir siempre."
        : "Lo firma una autoridad que no está instalada en este equipo, como una CA interna de la empresa. Comprueba la huella con quien opera el servidor y confía en él, o usa su certificado raíz.";

    public string CertificateSubject => _seen?.Subject ?? "";
    public string CertificateIssuer => _seen?.Issuer ?? "";
    public string CertificateExpiry => _seen is { } c ? c.NotAfterUtc.ToLocalTime().ToString("d 'de' MMMM 'de' yyyy", Es) : "";
    public string CertificateFingerprint => _seen?.Fingerprint ?? _connection.PinnedSha256 ?? "";
    public bool TrustButtonsVisible => _seen is { TrustedByWindows: false } && !PinMatches;
    public bool UntrustVisible => TrustsOwn;

    /// <summary>Confía en el certificado que enseñó el servidor, por su huella.</summary>
    public void TrustSeenCertificate()
    {
        if (_seen is null) return;
        _connection.PinnedSha256 = _seen.Fingerprint;
        _save();
        OnCertificateChanged();
    }

    /// <summary>Confía en una raíz propia, leída de un fichero; si no es un certificado, avisa.</summary>
    public void TrustRoot(byte[] bytes, string name)
    {
        // Se comprueba que es un certificado antes de guardarlo: si no, cada prueba fallaría sin decir por qué.
        using (BoardHttp.ReadCertificate(bytes)) { }
        _connection.RootCertificate = Convert.ToBase64String(bytes);
        _connection.RootName = name;
        _save();
        OnCertificateChanged();
    }

    /// <summary>Deja de confiar en el certificado o la raíz propia.</summary>
    public void Untrust()
    {
        _connection.PinnedSha256 = null;
        _connection.RootCertificate = null;
        _connection.RootName = null;
        _connection.VerifiedUtc = null;
        _save();
        OnCertificateChanged();
        SetTest("Sin confianza propia: vuelve a probar la conexión.", ThemeColors.Of("FgFaint", "#8A8D9C"));
    }

    /// <summary>Prueba la conexión: mira el certificado, pide los proyectos y, si todo va bien, deja elegir qué se ve.</summary>
    public async Task TestAsync()
    {
        if (_testing) return;
        _testing = true;
        OnChanged(nameof(CanTest));
        SetTest("Probando…", ThemeColors.Of("FgFaint", "#8A8D9C"));
        try
        {
            var values = _store.Resolve(_profile, _connection);
            if (!values.TryGetValue("baseUrl", out var baseUrl) || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var address))
            {
                Fail("Falta la dirección del servidor.");
                return;
            }
            var missing = _profile.Settings.FirstOrDefault(s => s.Secret && !values.ContainsKey(s.Key));
            if (missing is not null)
            {
                Fail(_connection.SecretEnv.TryGetValue(missing.Key, out var variable)
                    ? $"La variable {variable} no existe o está vacía."
                    : $"Falta la {missing.Label.ToLower(Es)}.");
                return;
            }

            _seen = await BoardHttp.ServerCertificateAsync(address);
            OnCertificateChanged();
            if (!_seen.TrustedByWindows && !PinMatches && _connection.RootCertificate is null)
            {
                Fail(PinChanged ? "El certificado ha cambiado: revísalo abajo antes de seguir." : "Revisa el certificado del servidor, abajo, antes de seguir.");
                return;
            }

            using var http = BoardHttp.Create(BoardStore.Trust(_connection));
            var client = _store.Client(_profile, _connection, http);
            var projects = await client.ProjectsAsync();
            // Quién eres con esa clave confirma que es la tuya; se enseña y no se guarda.
            var me = await client.MeAsync();
            _connection.VerifiedUtc = DateTime.UtcNow;
            _save();
            var who = me?.Name is { } name ? $" como {name}" : "";
            SetTest($"Conectada{who}: {projects.Count} {(projects.Count == 1 ? "proyecto" : "proyectos")}.", ThemeColors.Of("DataHaiku", "#98C379"));
            ShowProjects(projects, client);
        }
        catch (BoardException ex)
        {
            Fail(ex.Message);
        }
        catch (Exception ex)
        {
            JsonStore.LogError("boards-test", ex);
            Fail("No se ha podido probar la conexión. El detalle está en el registro de errores.");
        }
        finally
        {
            _testing = false;
            OnChanged(nameof(CanTest));
        }
    }

    /// <summary>Pinta los proyectos para elegir, con lo ya elegido encendido y los elegidos primero.</summary>
    private void ShowProjects(List<BoardProject> projects, BoardClient client)
    {
        Projects.Clear();
        var chosen = _connection.Boards.Select(b => b.ProjectId).ToHashSet();
        foreach (var p in projects.OrderByDescending(p => chosen.Contains(p.Id)).ThenBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            Projects.Add(new ProjectChoice(p, _connection, _profile, _store, _save));
        OnChanged(nameof(ProjectsVisible));
    }

    /// <summary>Deja un fallo de la prueba a la vista.</summary>
    private void Fail(string message) => SetTest(message, ThemeColors.Of("Danger", "#E03E4C"));

    private void SetTest(string text, IBrush brush)
    {
        _testText = text;
        _testBrush = brush;
        OnChanged(nameof(TestText));
        OnChanged(nameof(TestBrush));
    }

    private void OnCertificateChanged()
    {
        foreach (var name in new[] { nameof(CertificateVisible), nameof(CertificateTitle), nameof(CertificateHint), nameof(CertificateSubject),
                     nameof(CertificateIssuer), nameof(CertificateExpiry), nameof(CertificateFingerprint), nameof(TrustButtonsVisible),
                     nameof(UntrustVisible) })
            OnChanged(name);
    }

    /// <summary>Compara dos huellas escritas con o sin separadores.</summary>
    private static bool Same(string a, string b) =>
        string.Equals(new string(a.Where(Uri.IsHexDigit).ToArray()), new string(b.Where(Uri.IsHexDigit).ToArray()), StringComparison.OrdinalIgnoreCase);

    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Un ajuste del perfil en el editor: un texto, o una clave guardada en la app o leída de una variable de entorno.</summary>
public sealed class SettingField(ProfileSetting setting, BoardConnection connection, BoardStore store, Action save) : INotifyPropertyChanged
{
    private bool _changing;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Label => setting.Label;
    public string Hint => setting.Hint ?? (setting.Default is { } d && !setting.Secret ? $"Por defecto, {d}" : "");
    public bool HintVisible => Hint.Length > 0;
    public bool TextVisible => !setting.Secret;
    public bool SecretVisible => setting.Secret;

    /// <summary>Valor de un ajuste que no es clave; vacío, se usa el de por defecto del perfil.</summary>
    public string Value
    {
        get => connection.Values.GetValueOrDefault(setting.Key) ?? setting.Default ?? "";
        set
        {
            var text = value.Trim();
            if (text.Length == 0 || text == setting.Default) connection.Values.Remove(setting.Key);
            else connection.Values[setting.Key] = text;
            connection.VerifiedUtc = null;
            save();
        }
    }

    public bool UseEnv
    {
        get => connection.SecretEnv.ContainsKey(setting.Key);
        set
        {
            if (value == UseEnv) return;
            if (value) connection.SecretEnv[setting.Key] = setting.Env ?? "";
            else connection.SecretEnv.Remove(setting.Key);
            connection.VerifiedUtc = null;
            save();
            OnSecretChanged();
        }
    }

    public bool UseApp
    {
        get => !UseEnv;
        set => UseEnv = !value;
    }

    /// <summary>Nombre de la variable de entorno de la que se lee la clave.</summary>
    public string EnvName
    {
        get => connection.SecretEnv.GetValueOrDefault(setting.Key) ?? "";
        set
        {
            connection.SecretEnv[setting.Key] = value.Trim();
            connection.VerifiedUtc = null;
            save();
            OnChanged(nameof(EnvStatus));
            OnChanged(nameof(EnvStatusBrush));
        }
    }

    // Solo se mira si la variable existe; su valor no se enseña nunca.
    private bool EnvExists => EnvName.Length > 0 &&
        (Environment.GetEnvironmentVariable(EnvName) is { Length: > 0 } || Environment.GetEnvironmentVariable(EnvName, EnvironmentVariableTarget.User) is { Length: > 0 });

    public string EnvStatus => EnvName.Length == 0 ? "" : EnvExists ? "La variable existe" : "No existe en tu usuario";
    public IBrush EnvStatusBrush => EnvExists ? ThemeColors.Of("DataHaiku", "#98C379") : ThemeColors.Of("Danger", "#E03E4C");

    private bool HasSaved => store.HasSecret(connection.Id, setting.Key);

    /// <summary>Se está escribiendo otra clave en lugar de la guardada.</summary>
    public bool Changing
    {
        get => _changing;
        set
        {
            _changing = value;
            OnSecretChanged();
        }
    }

    public bool BoxVisible => setting.Secret && !UseEnv && (!HasSaved || _changing);
    public bool SavedVisible => setting.Secret && !UseEnv && HasSaved && !_changing;
    public bool EnvVisible => setting.Secret && UseEnv;

    /// <summary>Guarda la clave escrita, cifrada.</summary>
    public void SaveSecret(string value)
    {
        store.SetSecret(connection.Id, setting.Key, value);
        connection.VerifiedUtc = null;
        save();
        _changing = false;
        OnSecretChanged();
    }

    private void OnSecretChanged()
    {
        foreach (var name in new[] { nameof(UseEnv), nameof(UseApp), nameof(EnvName), nameof(EnvStatus), nameof(EnvStatusBrush), nameof(BoxVisible),
                     nameof(SavedVisible), nameof(EnvVisible) })
            OnChanged(name);
    }

    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Un proyecto que se puede ver: encendido, el proyecto entero o los subtableros que se marquen.</summary>
public sealed class ProjectChoice : INotifyPropertyChanged
{
    private readonly BoardProject _project;
    private readonly BoardConnection _connection;
    private readonly BoardProfile _profile;
    private readonly BoardStore _store;
    private readonly Action _save;
    private string _note = "";
    private bool _loaded;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Prepara la fila de un proyecto; si ya estaba elegido, carga sus subtableros.</summary>
    public ProjectChoice(BoardProject project, BoardConnection connection, BoardProfile profile, BoardStore store, Action save)
    {
        _project = project;
        _connection = connection;
        _profile = profile;
        _store = store;
        _save = save;
        if (IsOn) _ = LoadBoardsAsync();
    }

    public string Key => _project.Key;
    public string Name => _project.Name;
    public ObservableCollection<BoardChoice> Boards { get; } = [];
    public string Note => _note;
    public bool NoteVisible => _note.Length > 0;
    public bool BoardsVisible => IsOn;

    /// <summary>Se ve el proyecto: al encenderlo, entero; al apagarlo, se quita con todos sus subtableros.</summary>
    public bool IsOn
    {
        get => _connection.Boards.Any(b => b.ProjectId == _project.Id);
        set
        {
            if (value == IsOn) return;
            _connection.Boards.RemoveAll(b => b.ProjectId == _project.Id);
            if (value) _connection.Boards.Add(new BoardSelection(_project.Id, _project.Key, _project.Name));
            _save();
            OnChanged();
            OnChanged(nameof(BoardsVisible));
            if (value && !_loaded) _ = LoadBoardsAsync();
            else RefreshChips();
        }
    }

    /// <summary>Marca el proyecto entero o un subtablero; entero y subtableros se excluyen, y sin ninguno marcado vuelve a entero.</summary>
    internal void Toggle(BoardChoice choice, bool on)
    {
        if (choice.Id is null)
        {
            // «Todo el proyecto» no se apaga a mano: se apaga al marcar un subtablero.
            if (!on)
            {
                RefreshChips();
                return;
            }
            _connection.Boards.RemoveAll(b => b.ProjectId == _project.Id);
            _connection.Boards.Add(new BoardSelection(_project.Id, _project.Key, _project.Name));
        }
        else
        {
            _connection.Boards.RemoveAll(b => b.ProjectId == _project.Id && (b.BoardId is null || b.BoardId == choice.Id));
            if (on) _connection.Boards.Add(new BoardSelection(_project.Id, _project.Key, _project.Name, choice.Id, choice.Name));
            if (!_connection.Boards.Any(b => b.ProjectId == _project.Id))
                _connection.Boards.Add(new BoardSelection(_project.Id, _project.Key, _project.Name));
        }
        _save();
        RefreshChips();
    }

    /// <summary>Pone el color de un subtablero en Tareas; null vuelve al del panel o al que pone la app.</summary>
    /// <remarks>Si el subtablero va con un repositorio, el color es de los dos y se ve igual en Sesiones y el Diario.</remarks>
    internal void SetColor(BoardChoice choice, string? hex)
    {
        if (choice.Id is null) return;
        if (!RepoColors.SetSubBoard(_connection, choice.Id, choice.Name, hex))
        {
            if (hex is null) _connection.SubBoardColors.Remove(choice.Id);
            else _connection.SubBoardColors[choice.Id] = hex;
        }
        _save();
        RepoColors.NotifyChanged();
    }

    /// <summary>Color con que se ve un subtablero en Tareas.</summary>
    internal IBrush ColorOf(BoardRef board) => TasksModel.SubBoardBrush(_connection, board);

    /// <summary>Pide los subtableros del proyecto y pinta un chip por cada uno, más «Todo el proyecto».</summary>
    private async Task LoadBoardsAsync()
    {
        _loaded = true;
        SetNote("Cargando subtableros…");
        try
        {
            using var http = BoardHttp.Create(BoardStore.Trust(_connection));
            var boards = await _store.Client(_profile, _connection, http).BoardsAsync(_project);
            Boards.Clear();
            Boards.Add(new BoardChoice(this, null, "Todo el proyecto"));
            foreach (var b in boards.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase)) Boards.Add(new BoardChoice(this, b.Id, b.Name, b));
            SetNote(boards.Count == 0 ? "Este proyecto no tiene subtableros: se ve entero." : "");
            RefreshChips();
        }
        catch (BoardException ex)
        {
            _loaded = false;
            SetNote(ex.Message);
        }
    }

    /// <summary>Pone cada chip como está en la selección.</summary>
    private void RefreshChips()
    {
        foreach (var chip in Boards)
            chip.Refresh(_connection.Boards.Any(b => b.ProjectId == _project.Id && b.BoardId == chip.Id));
    }

    private void SetNote(string text)
    {
        _note = text;
        OnChanged(nameof(Note));
        OnChanged(nameof(NoteVisible));
    }

    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Chip del proyecto entero (Id null) o de uno de sus subtableros, con su color en Tareas.</summary>
public sealed class BoardChoice(ProjectChoice project, string? id, string name, BoardRef? board = null) : INotifyPropertyChanged
{
    private bool _on;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string? Id => id;
    public string Name => name;

    public IBrush ColorBrush => board is null ? Brushes.Transparent : project.ColorOf(board);
    public string? Hex => ColorBrush is ISolidColorBrush s ? $"#{s.Color.R:X2}{s.Color.G:X2}{s.Color.B:X2}" : null;
    public bool DotVisible => board is not null;

    /// <summary>Lo que dice la ficha del punto de color: si el color es solo del subtablero o también de su repositorio.</summary>
    public string ColorNote => id is not null && RepoColors.RepoOf(id, name) is { } repo
        ? $"Va con el repositorio {repo}: el mismo color se ve en Sesiones, el Diario, Estadísticas y Worktrees."
        : "Solo en el Tablero: ningún repositorio de tus sesiones lo declara en su .arkko.json ni se llama igual.";

    /// <summary>Cambia su color en Tareas; null vuelve al de serie.</summary>
    internal void SetColor(string? hex)
    {
        project.SetColor(this, hex);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ColorBrush)));
    }

    public bool IsOn
    {
        get => _on;
        set => project.Toggle(this, value);
    }

    /// <summary>Pone el chip como está en la selección, sin volver a tocarla.</summary>
    internal void Refresh(bool on)
    {
        _on = on;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsOn)));
    }
}
