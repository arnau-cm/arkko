using System.Text.Json.Nodes;
using Arkko.Core;

namespace Arkko.Tests;

public class SettingsInstallerTests
{
    private const string Exe = @"C:\Users\x\AppData\Local\ClaudeStats\bin\ak.exe";
    private const string OldExe = @"C:\Users\x\AppData\Local\ClaudeStats\bin\ccstats.exe";

    private const string Existing = """
        {
          "model": "opus",
          "hooks": {
            "PreToolUse": [
              { "matcher": "Edit|Write", "hooks": [ { "type": "command", "command": "powershell -File other.ps1" } ] }
            ]
          }
        }
        """;

    [Fact]
    public void Install_conserva_lo_ajeno_y_añade_lo_propio()
    {
        var (settings, warnings) = SettingsInstaller.Install(JsonNode.Parse(Existing)!.AsObject(), Exe);

        Assert.Empty(warnings);
        Assert.Equal("opus", settings["model"]!.GetValue<string>());
        Assert.Equal("\"C:/Users/x/AppData/Local/ClaudeStats/bin/ak.exe\" statusline",
            settings["statusLine"]!["command"]!.GetValue<string>());

        var pre = settings["hooks"]!["PreToolUse"]!.AsArray();
        Assert.Equal(2, pre.Count);
        Assert.Equal("powershell -File other.ps1", pre[0]!["hooks"]![0]!["command"]!.GetValue<string>());
        Assert.Equal("AskUserQuestion|ExitPlanMode", pre[1]!["matcher"]!.GetValue<string>());

        foreach (var (evt, _) in SettingsInstaller.Hooks)
            Assert.NotNull(settings["hooks"]![evt]);
    }

    [Fact]
    public void Install_dos_veces_no_duplica()
    {
        var once = SettingsInstaller.Install(JsonNode.Parse(Existing)!.AsObject(), Exe).Settings.ToJsonString();
        var twice = SettingsInstaller.Install(JsonNode.Parse(once)!.AsObject(), Exe).Settings.ToJsonString();
        Assert.Equal(once, twice);
    }

    [Fact]
    public void Uninstall_deja_el_fichero_como_estaba()
    {
        var installed = SettingsInstaller.Install(JsonNode.Parse(Existing)!.AsObject(), Exe).Settings;
        var restored = SettingsInstaller.Uninstall(installed);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Existing), restored));
    }

    [Fact]
    public void Install_retira_la_version_de_powershell()
    {
        var old = JsonNode.Parse("""
            {
              "statusLine": { "type": "command", "command": "powershell -File C:/u/.claude/cc-statusline.ps1" },
              "hooks": { "Stop": [ { "hooks": [ { "type": "command", "command": "powershell -File C:/u/.claude/cc-prompt-usage.ps1" } ] } ] }
            }
            """)!.AsObject();

        var (settings, warnings) = SettingsInstaller.Install(old, Exe);

        Assert.Empty(warnings);
        var stop = settings["hooks"]!["Stop"]!.AsArray();
        Assert.Single(stop);
        Assert.Contains("ak.exe", stop[0]!["hooks"]![0]!["command"]!.GetValue<string>());
    }

    [Fact]
    public void Install_sustituye_las_entradas_de_ccstats_sin_duplicar_ni_tocar_las_ajenas()
    {
        var old = SettingsInstaller.Install(JsonNode.Parse(Existing)!.AsObject(), OldExe).Settings;

        var (settings, warnings) = SettingsInstaller.Install(JsonNode.Parse(old.ToJsonString())!.AsObject(), Exe);

        Assert.Empty(warnings);
        Assert.DoesNotContain("ccstats", settings.ToJsonString());
        var fresh = SettingsInstaller.Install(JsonNode.Parse(Existing)!.AsObject(), Exe).Settings;
        Assert.Equal(fresh.ToJsonString(), settings.ToJsonString());
    }

    [Fact]
    public void Install_añade_las_reglas_deny_de_la_app_y_conserva_las_ajenas()
    {
        var other = JsonNode.Parse("""{ "permissions": { "allow": ["Bash(git status)"], "deny": ["Bash(sqlcmd *)"] } }""")!.AsObject();
        var security = @"C:\Users\x\AppData\Local\ClaudeStats\data\security";
        var backups = @"C:\Users\x\AppData\Local\ClaudeStats\data\backups";

        var once = SettingsInstaller.Install(other, Exe, security, backups).Settings;
        var twice = SettingsInstaller.Install(JsonNode.Parse(once.ToJsonString())!.AsObject(), Exe, security, backups).Settings;

        Assert.Equal("Read(~/AppData/Local/ClaudeStats/data/security/**)", SettingsInstaller.SecurityDenyRule(security, @"C:\Users\x"));
        Assert.Equal("Read(~/AppData/Local/ClaudeStats/data/backups/**)", SettingsInstaller.BackupsDenyRule(backups, @"C:\Users\x"));
        Assert.Equal(["Bash(sqlcmd *)", SettingsInstaller.SecurityDenyRule(security), SettingsInstaller.BackupsDenyRule(backups)],
            SettingsInstaller.DenyRules(twice));

        var removed = SettingsInstaller.Uninstall(twice);
        Assert.Equal(["Bash(sqlcmd *)"], SettingsInstaller.DenyRules(removed));
        Assert.Equal("Bash(git status)", removed["permissions"]!["allow"]![0]!.GetValue<string>());
    }

    [Fact]
    public void Las_rutas_de_las_reglas_usan_el_formato_de_Claude_Code()
    {
        Assert.Equal("~/.ssh", SettingsInstaller.RulePath(@"C:\Users\x\.ssh", @"C:\Users\x"));
        Assert.Equal("//d/datos/ClaudeStats/data/security", SettingsInstaller.RulePath(@"D:\datos\ClaudeStats\data\security\", @"C:\Users\x"));
        // Un perfil que empieza igual que otro no cuenta como dentro.
        Assert.Equal("//c/Users/xy/datos", SettingsInstaller.RulePath(@"C:\Users\xy\datos", @"C:\Users\x"));
    }

    [Fact]
    public void Guardar_escribe_el_settings_con_las_reglas()
    {
        var path = Path.Combine(Path.GetTempPath(), "ccs-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var settings = JsonNode.Parse("""{ "model": "opus" }""")!.AsObject();
            SettingsInstaller.AddDenyRules(settings, ["Read(**/.env)"]);
            SettingsInstaller.Save(path, settings);
            Assert.Equal(["Read(**/.env)"], SettingsInstaller.DenyRules(SettingsInstaller.Load(path)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Añadir_reglas_no_repite_las_que_ya_estan()
    {
        var settings = JsonNode.Parse("""{ "permissions": { "deny": ["Read(**/.env)"] } }""")!.AsObject();
        SettingsInstaller.AddDenyRules(settings, ["Read(**/.env)", "Read(**/*.pfx)"]);
        Assert.Equal(["Read(**/.env)", "Read(**/*.pfx)"], SettingsInstaller.DenyRules(settings));
    }

    [Fact]
    public void Quitar_reglas_deja_las_demas_y_las_puestas_a_mano()
    {
        var settings = JsonNode.Parse("""{ "permissions": { "deny": ["Read(**/.env)", "Bash(sqlcmd *)", "Read(**/*.pfx)", "Read(**/*.p12)"] } }""")!.AsObject();
        SettingsInstaller.RemoveDenyRules(settings, ["Read(**/*.pfx)", "Read(**/*.p12)", "Read(**/*.key)"]);
        Assert.Equal(["Read(**/.env)", "Bash(sqlcmd *)"], SettingsInstaller.DenyRules(settings));

        // Sin lista deny no hay nada que quitar, y no se crea.
        var empty = JsonNode.Parse("""{ "model": "opus" }""")!.AsObject();
        SettingsInstaller.RemoveDenyRules(empty, ["Read(**/.env)"]);
        Assert.Null(empty["permissions"]);
    }

    [Fact]
    public void Install_avisa_si_sustituye_una_statusline_ajena()
    {
        var other = JsonNode.Parse("""{ "statusLine": { "type": "command", "command": "otra.exe" } }""")!.AsObject();
        var (_, warnings) = SettingsInstaller.Install(other, Exe);
        Assert.Single(warnings);
    }
}
