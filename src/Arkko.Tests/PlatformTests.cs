using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Arkko.Core;

namespace Arkko.Tests;

public class PlatformTests
{
    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

    [Fact]
    public void El_cifrado_del_llavero_descifra_lo_que_cifra()
    {
        var data = "token de prueba ñ"u8.ToArray();
        var sealed1 = KeyringCipher.Seal(Key, data);
        var sealed2 = KeyringCipher.Seal(Key, data);

        Assert.Equal(data, KeyringCipher.Open(Key, sealed1));
        Assert.NotEqual(sealed1, sealed2);
        Assert.False(sealed1.AsSpan().IndexOf(data) >= 0);
    }

    [Fact]
    public void El_cifrado_del_llavero_rechaza_otra_clave_y_datos_dañados()
    {
        var blob = KeyringCipher.Seal(Key, [1, 2, 3]);
        var otherKey = Key.Select(b => (byte)(b ^ 0xFF)).ToArray();
        var tampered = blob.ToArray();
        tampered[^1] ^= 0x01;

        Assert.ThrowsAny<CryptographicException>(() => KeyringCipher.Open(otherKey, blob));
        Assert.ThrowsAny<CryptographicException>(() => KeyringCipher.Open(Key, tampered));
        Assert.ThrowsAny<CryptographicException>(() => KeyringCipher.Open(Key, [1, 2, 3]));
    }

    [Fact]
    public void En_Windows_el_cifrado_es_el_de_siempre()
    {
        if (!OperatingSystem.IsWindows()) return;
        var data = "lo ya guardado se sigue leyendo"u8.ToArray();

        Assert.Equal(data, UserCipher.Unprotect(Dpapi.Protect(data)));
        Assert.Equal(data, Dpapi.Unprotect(UserCipher.Protect(data)));
    }

    [Fact]
    public void La_entrada_de_autostart_de_Linux_usa_saltos_LF_y_la_ruta_entre_comillas()
    {
        var entry = Autostart.DesktopEntry("/home/x/Agent \"Pulse\"/AgentPulse");

        Assert.DoesNotContain("\r", entry);
        Assert.StartsWith("[Desktop Entry]\n", entry);
        Assert.Contains("Exec=\"/home/x/Agent \\\"Pulse\\\"/AgentPulse\"\n", entry);
    }

    [Fact]
    public void El_LaunchAgent_de_macOS_escapa_la_ruta()
    {
        var plist = Autostart.LaunchAgent("/Users/x/A&B/AgentPulse");

        Assert.DoesNotContain("\r", plist);
        Assert.Contains("<string>/Users/x/A&amp;B/AgentPulse</string>", plist);
        Assert.Contains("<key>RunAtLoad</key>", plist);
    }

    [Fact]
    public void Ak_sin_exe_se_reconoce_como_propio()
    {
        const string unixExe = "/home/x/.local/share/ClaudeStats/bin/ak";
        var original = JsonNode.Parse("""{ "model": "opus" }""")!.AsObject();

        var once = SettingsInstaller.Install(original.DeepClone().AsObject(), unixExe).Settings;
        var twice = SettingsInstaller.Install(JsonNode.Parse(once.ToJsonString())!.AsObject(), unixExe).Settings;

        Assert.Equal("\"/home/x/.local/share/ClaudeStats/bin/ak\" hook",
            once["hooks"]!["Stop"]![0]!["hooks"]![0]!["command"]!.GetValue<string>());
        Assert.Equal(once.ToJsonString(), twice.ToJsonString());
        Assert.True(JsonNode.DeepEquals(original, SettingsInstaller.Uninstall(twice)));
    }

    [Fact]
    public void Ccstats_sin_exe_de_antes_se_sustituye_por_ak()
    {
        var original = JsonNode.Parse("""{ "model": "opus" }""")!.AsObject();
        var old = SettingsInstaller.Install(original.DeepClone().AsObject(), "/home/x/.local/share/ClaudeStats/bin/ccstats").Settings;

        var now = SettingsInstaller.Install(JsonNode.Parse(old.ToJsonString())!.AsObject(), "/home/x/.local/share/ClaudeStats/bin/ak").Settings;

        Assert.DoesNotContain("ccstats", now.ToJsonString());
        Assert.True(JsonNode.DeepEquals(original, SettingsInstaller.Uninstall(now)));
    }

    [Fact]
    public void La_ventana_esta_en_su_carpeta_junto_a_la_de_ak()
    {
        var name = OperatingSystem.IsWindows() ? "Arkko.exe" : "Arkko";
        Assert.Equal(Path.Combine(AppPaths.Home, "desktop", name), AppPaths.WindowPath);
        Assert.Equal(Path.GetDirectoryName(AppPaths.WindowPath), Path.GetDirectoryName(AppPaths.OldWindowPath));
    }

    [Theory]
    [InlineData("\"C:\\Datos\\desktop\\AgentPulse.exe\"", "\"C:\\Datos\\desktop\\Arkko.exe\"")]
    [InlineData("\"c:\\datos\\DESKTOP\\agentpulse.exe\" ", "\"C:\\Datos\\desktop\\Arkko.exe\"")]
    [InlineData("\"C:\\Prueba\\desktop\\AgentPulse.exe\"", null)]
    [InlineData("\"C:\\Datos\\desktop\\Arkko.exe\"", null)]
    [InlineData(null, null)]
    public void El_arranque_solo_cambia_si_abre_la_ventana_vieja_de_la_misma_carpeta(string? current, string? expected) =>
        Assert.Equal(expected, Autostart.Retargeted(current, @"C:\Datos\desktop\AgentPulse.exe", @"C:\Datos\desktop\Arkko.exe"));
}
