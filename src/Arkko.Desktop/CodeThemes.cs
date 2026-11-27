using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>El tema del código de cada lenguaje: el catálogo de <c>Web/codigo.json</c> y lo elegido, guardado con los ajustes.</summary>
/// <remarks>
/// Se elige en dos sitios, el visor de código de Sesiones y Configuración → Sesiones, que escriben lo mismo; los dos se
/// enteran de un cambio por <see cref="Changed"/>. Solo se guarda lo que no es el tema de serie del lenguaje.
/// </remarks>
internal static class CodeThemes
{
    private static AppSettings? _settings;
    private static Action? _save;
    private static CodeCatalog? _catalog;

    /// <summary>Se dispara cuando cambia el tema de algún lenguaje.</summary>
    public static event Action? Changed;

    /// <summary>Los lenguajes y los temas, leídos una vez del fichero que va junto a la página.</summary>
    public static CodeCatalog Catalog => _catalog ??= Load();

    /// <summary>Lo elegido por lenguaje; vacío hasta que se conecten los ajustes.</summary>
    public static IReadOnlyDictionary<string, string> Chosen => _settings?.CodeThemes ?? [];

    /// <summary>Recibe los ajustes, donde se guarda lo elegido, y cómo guardarlos.</summary>
    /// <remarks>Avisa al terminar: Configuración ya está creada y tiene que enseñar lo elegido, que hasta ahora no veía.</remarks>
    public static void Init(AppSettings settings, Action save)
    {
        _settings = settings;
        _save = save;
        Changed?.Invoke();
    }

    /// <summary>El tema de un lenguaje: el elegido o el de serie.</summary>
    public static string ThemeOf(string languageId) => Catalog.ThemeOf(languageId, Chosen);

    /// <summary>Pone el tema de un lenguaje; si es el de serie, deja de guardarlo. Un lenguaje o un tema que no existen se ignoran.</summary>
    public static void Set(string languageId, string themeId)
    {
        if (_settings is not { } settings || Catalog.Languages.FirstOrDefault(l => l.Id == languageId) is not { } language
            || Catalog.Themes.All(t => t.Id != themeId) || ThemeOf(languageId) == themeId) return;
        if (themeId == language.DefaultTheme) settings.CodeThemes.Remove(languageId);
        else settings.CodeThemes[languageId] = themeId;
        _save?.Invoke();
        Changed?.Invoke();
    }

    /// <summary>Vuelve todos los lenguajes a su tema de serie.</summary>
    public static void Reset()
    {
        if (_settings is not { } settings || settings.CodeThemes.Count == 0) return;
        settings.CodeThemes.Clear();
        _save?.Invoke();
        Changed?.Invoke();
    }

    /// <summary>Lee el catálogo; si falla, lo anota y el código se ve sin colores.</summary>
    private static CodeCatalog Load()
    {
        var (catalog, error) = CodeCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Web", "codigo.json"));
        if (error is not null) JsonStore.LogError("code-themes", new InvalidDataException(error));
        return catalog;
    }
}
