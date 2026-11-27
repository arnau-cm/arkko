using Arkko.Core;

namespace Arkko.Tests;

/// <remarks>Lee el catálogo de verdad, el que va con la página de Sesiones, y lo compara con sus estilos.</remarks>
public sealed class CodeCatalogTests
{
    private static readonly string Web = FindWeb();

    /// <summary>La carpeta Web de la ventana, subiendo desde donde corren los tests hasta el repositorio.</summary>
    private static string FindWeb()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var web = Path.Combine(dir.FullName, "src", "Arkko.Desktop", "Web");
            if (File.Exists(Path.Combine(web, "codigo.json"))) return web;
        }
        throw new DirectoryNotFoundException("No se encuentra src/Arkko.Desktop/Web/codigo.json.");
    }

    private static CodeCatalog Catalog()
    {
        var (catalog, error) = CodeCatalog.Load(Path.Combine(Web, "codigo.json"));
        Assert.Null(error);
        return catalog;
    }

    [Fact]
    public void El_catalogo_tiene_los_lenguajes_tipicos_con_ids_y_extensiones_sin_repetir()
    {
        var catalog = Catalog();

        Assert.Equal(catalog.Languages.Count, catalog.Languages.Select(l => l.Id).Distinct().Count());
        var extensions = catalog.Languages.SelectMany(l => l.Extensions).ToList();
        Assert.Equal(extensions.Count, extensions.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var id in new[] { "cs", "razor", "js", "ts", "json", "xml", "xaml", "html", "css", "sql", "py", "ps", "md", "other" })
            Assert.Contains(catalog.Languages, l => l.Id == id);
    }

    [Fact]
    public void CSharp_y_Razor_van_de_serie_en_Visual_Studio_claro_y_el_resto_como_la_app()
    {
        var catalog = Catalog();

        Assert.Equal("vs-light", catalog.ThemeOf("cs", new Dictionary<string, string>()));
        Assert.Equal("vs-light", catalog.ThemeOf("razor", new Dictionary<string, string>()));
        Assert.All(catalog.Languages.Where(l => l.Id is not "cs" and not "razor"), l => Assert.Equal(CodeCatalog.AppTheme, l.DefaultTheme));
    }

    [Fact]
    public void Lo_elegido_gana_al_de_serie_salvo_un_tema_que_no_existe()
    {
        var catalog = Catalog();

        Assert.Equal("vs-dark", catalog.ThemeOf("cs", new Dictionary<string, string> { ["cs"] = "vs-dark" }));
        Assert.Equal("vs-light", catalog.ThemeOf("cs", new Dictionary<string, string> { ["cs"] = "borrado" }));
        Assert.Equal(CodeCatalog.AppTheme, catalog.ThemeOf("no-existe", new Dictionary<string, string>()));
    }

    [Fact]
    public void Cada_tema_tiene_sus_colores_en_la_hoja_de_estilos()
    {
        var css = File.ReadAllText(Path.Combine(Web, "sesiones.css"));

        foreach (var theme in Catalog().Themes) Assert.Contains($"[data-ct=\"{theme.Id}\"]", css);
    }

    [Fact]
    public void Un_lenguaje_con_un_tema_de_serie_que_no_existe_sigue_a_la_app_y_un_fichero_roto_da_el_vacio()
    {
        var catalog = CodeCatalog.Parse("""
            { "themes": [ { "id": "app", "name": "Como la app" } ],
              "languages": [ { "id": "x", "name": "X", "extensions": ["x"], "defaultTheme": "no-existe" } ] }
            """);
        Assert.Equal(CodeCatalog.AppTheme, Assert.Single(catalog.Languages).DefaultTheme);

        var path = Path.Combine(Path.GetTempPath(), "cs-codigo-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, "{ roto");
        try
        {
            var (broken, error) = CodeCatalog.Load(path);
            Assert.NotNull(error);
            Assert.Empty(broken.Languages);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
