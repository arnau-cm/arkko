using Arkko.Core;

namespace Arkko.Tests;

public sealed class BoardStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccs-boards-" + Guid.NewGuid().ToString("N"));

    public BoardStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        // Solo la carpeta temporal que ha creado este test.
        if (_dir.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)) Directory.Delete(_dir, recursive: true);
    }

    private static BoardProfile PanelProfile() => BoardProfile.Parse(TestProfiles.Panel);

    [Fact]
    public void Guarda_y_lee_las_conexiones_sin_ninguna_clave()
    {
        var store = new BoardStore(_dir);
        var connection = new BoardConnection { Profile = "panel-prueba", Name = "Panel", Color = "#E5C07B", PinnedSha256 = "AA:BB" };
        connection.Boards.Add(new BoardSelection("p1", "DEV", "Desarrollo", "b1", "WEB"));
        store.Save(new BoardSettings { Connections = [connection] });
        store.SetSecret(connection.Id, "apiKey", "clave-muy-secreta-123");

        var read = store.Load().Connections.Single();
        Assert.Equal(("Panel", "#E5C07B", "AA:BB"), (read.Name, read.Color, read.PinnedSha256));
        Assert.Equal("WEB", read.Boards.Single().BoardName);

        // Ni la configuración ni el fichero cifrado llevan la clave en claro.
        foreach (var file in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories))
            Assert.DoesNotContain("clave-muy-secreta-123", File.ReadAllText(file));
        Assert.True(store.HasSecret(connection.Id, "apiKey"));
    }

    [Fact]
    public void Lo_guardado_y_lo_cambiado_fuera_se_lee_aunque_se_lea_de_memoria()
    {
        // La lectura guarda en memoria lo leído y solo vuelve a abrir el fichero si cambian su fecha o su tamaño.
        var store = new BoardStore(_dir);
        store.Save(new BoardSettings { TaskPattern = "AAA" });
        Assert.Equal("AAA", store.Load().TaskPattern);
        store.Save(new BoardSettings { TaskPattern = "BBB" });
        Assert.Equal("BBB", store.Load().TaskPattern);

        // Cambiado por otro proceso, con el mismo tamaño.
        var file = Path.Combine(_dir, "boards.json");
        File.WriteAllText(file, File.ReadAllText(file).Replace("BBB", "CCC"));
        Assert.Equal("CCC", store.Load().TaskPattern);

        // Cada lectura da un objeto nuevo: cambiar uno no cambia lo que lee el siguiente.
        store.Load().TaskPattern = "XXX";
        Assert.Equal("CCC", store.Load().TaskPattern);
    }

    [Fact]
    public void La_clave_de_la_app_y_el_valor_por_defecto_llegan_al_cliente()
    {
        var store = new BoardStore(_dir);
        var connection = new BoardConnection { Profile = "panel-prueba" };
        store.SetSecret(connection.Id, "apiKey", "k1");

        var values = store.Resolve(PanelProfile(), connection);

        Assert.Equal("k1", values["apiKey"]);
        Assert.Equal("https://panel.example.test", values["baseUrl"]);
    }

    [Fact]
    public void La_clave_de_una_variable_de_entorno_se_lee_sin_copiarla()
    {
        var variable = "AGENTPULSE_TEST_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(variable, "k-env");
        try
        {
            var store = new BoardStore(_dir);
            var connection = new BoardConnection { Profile = "panel-prueba" };
            connection.SecretEnv["apiKey"] = variable;
            store.Save(new BoardSettings { Connections = [connection] });

            Assert.Equal("k-env", store.Resolve(PanelProfile(), connection)["apiKey"]);
            Assert.False(store.HasSecret(connection.Id, "apiKey"));
            Assert.DoesNotContain("k-env", File.ReadAllText(Path.Combine(_dir, "boards.json")));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void Quitar_las_claves_de_una_conexion_no_toca_las_de_otra()
    {
        var store = new BoardStore(_dir);
        store.SetSecret("a", "apiKey", "1");
        store.SetSecret("b", "apiKey", "2");
        store.RemoveSecrets("a");
        Assert.False(store.HasSecret("a", "apiKey"));
        Assert.True(store.HasSecret("b", "apiKey"));
        store.SetSecret("b", "apiKey", "");
        Assert.False(store.HasSecret("b", "apiKey"));
    }

    [Fact]
    public void Importa_un_perfil_pegado_lo_lista_y_lo_devuelve_tal_cual()
    {
        var store = new BoardStore(_dir);
        Assert.Empty(store.Profiles());

        var profile = store.SaveProfile(TestProfiles.Panel);

        Assert.Equal("panel-prueba", profile.Id);
        Assert.True(store.HasProfile("panel-prueba"));
        Assert.Equal("Panel de prueba", store.Profiles().Single().Name);
        // Con sus comentarios, para que al copiarlo se comparta lo mismo que se pegó.
        Assert.Contains("// Panel de prueba", store.ProfileText("panel-prueba"));
        Assert.Equal("panel-prueba", store.ProfileOf(new BoardConnection { Profile = "panel-prueba" })!.Id);

        store.DeleteProfile("panel-prueba");
        Assert.Empty(store.Profiles());
    }

    [Fact]
    public void Un_perfil_que_no_vale_no_se_guarda_y_dice_por_que()
    {
        var store = new BoardStore(_dir);
        var ex = Assert.Throws<BoardException>(() => store.SaveProfile(TestProfiles.Panel.Replace("\"panel-prueba\"", "\"../fuera\"")));
        Assert.Contains("id", ex.Message);
        Assert.Throws<BoardException>(() => store.SaveProfile("esto no es un perfil"));
        Assert.Empty(store.Profiles());
    }

    [Fact]
    public void El_resumen_dice_que_pide_como_se_identifica_y_que_lee()
    {
        var summary = BoardProfile.Parse(TestProfiles.Panel).Summary();
        Assert.Contains("Pide: dirección y clave personal", summary);
        Assert.Contains("clave Bearer", summary);
        Assert.Contains("lee proyectos, subtableros, tareas, una tarea por su clave y quién eres", summary);
        Assert.Contains("traduce 8 estados", summary);
    }

    [Fact]
    public void La_plantilla_de_la_ayuda_es_un_perfil_valido_y_va_en_el_texto_para_Claude()
    {
        var template = BoardProfile.Parse(BoardProfileHelp.Template);
        Assert.Equal("mi-panel", template.Id);
        Assert.True(template.Has("task", "status"));
        Assert.Contains(BoardProfileHelp.Template, BoardProfileHelp.ClaudePrompt);
    }

    [Fact]
    public void Reconoce_la_tarea_en_la_rama_o_la_carpeta()
    {
        var settings = new BoardSettings();
        Assert.Equal("DEVLAB-99", settings.TaskKeyIn("DEVLAB-99"));
        Assert.Equal("DEVLAB-99", settings.TaskKeyIn("feature/DEVLAB-99-mesa"));
        Assert.Null(settings.TaskKeyIn("develop"));
        Assert.Null(new BoardSettings { TaskPattern = "([" }.TaskKeyIn("DEVLAB-99"));
    }
}
