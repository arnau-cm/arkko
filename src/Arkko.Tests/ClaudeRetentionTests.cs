using System.Text.Json.Nodes;
using Arkko.Core;

namespace Arkko.Tests;

public class ClaudeRetentionTests
{
    [Fact]
    public void Lee_los_dos_plazos_y_descarta_los_no_validos()
    {
        Assert.Equal(new ClaudeRetentionDays(14, 30),
            ClaudeRetention.Read(JsonNode.Parse("""{ "cleanupPeriodDays": 14, "desktopSessionCleanupPeriodDays": 30 }""")!.AsObject()));
        Assert.Equal(new ClaudeRetentionDays(null, null), ClaudeRetention.Read(new JsonObject()));
        Assert.Equal(new ClaudeRetentionDays(null, null),
            ClaudeRetention.Read(JsonNode.Parse("""{ "cleanupPeriodDays": 0, "desktopSessionCleanupPeriodDays": "7" }""")!.AsObject()));
    }

    [Fact]
    public void Escribir_conserva_el_resto_y_null_quita_la_clave()
    {
        var settings = JsonNode.Parse("""{ "model": "opus", "desktopSessionCleanupPeriodDays": 90 }""")!.AsObject();

        ClaudeRetention.Apply(settings, new ClaudeRetentionDays(14, null));

        Assert.Equal(14, settings["cleanupPeriodDays"]!.GetValue<int>());
        Assert.False(settings.ContainsKey("desktopSessionCleanupPeriodDays"));
        Assert.Equal("opus", settings["model"]!.GetValue<string>());
        Assert.Equal(new ClaudeRetentionDays(14, null), ClaudeRetention.Read(settings));
    }

    [Fact]
    public void La_configuracion_de_la_empresa_se_lee_si_existe()
    {
        var path = Path.Combine(Path.GetTempPath(), "ccs-managed-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Assert.Equal(new ClaudeRetentionDays(null, null), ClaudeRetention.Managed(path));
            File.WriteAllText(path, """{ "cleanupPeriodDays": 7 }""");
            Assert.Equal(new ClaudeRetentionDays(7, null), ClaudeRetention.Managed(path));
            File.WriteAllText(path, "{ no es json");
            Assert.Equal(new ClaudeRetentionDays(null, null), ClaudeRetention.Managed(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
