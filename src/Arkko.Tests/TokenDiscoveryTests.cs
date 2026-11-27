using Arkko.Core;

namespace Arkko.Tests;

public sealed class TokenDiscoveryTests
{
    // Partido en trozos, como en los tests del detector, para que el código no lleve el valor entero.
    private static readonly string GitHub = "gh" + "p_" + "R4nd0mT0k3nV4lu3Abc9XyZ8wQ7eP6oL5kJ4";

    [Fact]
    public void Propone_por_el_nombre_y_por_el_valor_y_nunca_contraseñas_ni_lo_que_no_parece_token()
    {
        var found = TokenDiscovery.Find(
        [
            ("GITEA_TOKEN", "abc123", false),
            ("MI_VARIABLE", GitHub, false),
            ("OPENAI_API_KEY", "valor", false),
            ("DB_PASSWORD", "abc123", false),
            ("PATH", @"C:\Windows", true),
            ("PSModulePath", @"C:\Modules", true),
            ("KEYBOARD_LAYOUT", "es", false),
            ("VACIA_TOKEN", "  ", false),
        ], []);

        Assert.Equal(["GITEA_TOKEN", "MI_VARIABLE", "OPENAI_API_KEY"], found.Select(c => c.Name));
        Assert.Equal("Token de GitHub", found.Single(c => c.Name == "MI_VARIABLE").Kind);
        Assert.DoesNotContain(found, c => c.Masked.Contains(GitHub[8..^4]));
    }

    [Fact]
    public void Deja_fuera_las_que_ya_son_un_token_y_prefiere_la_del_usuario()
    {
        var found = TokenDiscovery.Find(
        [
            ("GITEA_TOKEN", "abc123", true),
            ("GITEA_TOKEN", "def456", false),
            ("NPM_TOKEN", "abc123", false),
        ], [new SecretCard { EnvVar = "npm_token" }]);

        Assert.Equal(("GITEA_TOKEN", false), (found.Single().Name, found.Single().Machine));
    }
}
