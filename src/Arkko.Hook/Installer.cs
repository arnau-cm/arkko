using System.Diagnostics;
using Arkko.Core;

namespace Arkko.Hook;

/// <summary>Comandos de instalación, desinstalación y diagnóstico que ejecuta la persona.</summary>
internal static class Installer
{
    /// <summary>Registra statusline y hooks en settings.json y, si se pide, el arranque de la ventana al iniciar sesión.</summary>
    public static int Install(bool autostart)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("No se conoce la ruta de ak.");
        var settingsPath = AppPaths.ClaudeSettingsFile;

        var (settings, warnings) = SettingsInstaller.Install(SettingsInstaller.Load(settingsPath), exe);
        var backup = SettingsInstaller.Save(settingsPath, settings);
        Directory.CreateDirectory(AppPaths.Sessions);
        Directory.CreateDirectory(AppPaths.Prompts);

        Console.WriteLine($"Registrado en {settingsPath}");
        Console.WriteLine($"Regla deny para que Claude Code no lea los hallazgos de seguridad: {SettingsInstaller.SecurityDenyRule(AppPaths.Security)}");
        Console.WriteLine($"Regla deny para que Claude Code no lea las copias de settings.json: {SettingsInstaller.BackupsDenyRule(AppPaths.Backups)}");
        if (backup is not null) Console.WriteLine($"Copia del original: {backup}");
        foreach (var w in warnings) Console.WriteLine("AVISO: " + w);

        if (autostart)
        {
            Autostart.Enable(AppPaths.WindowPath);
            Console.WriteLine("Arranque al iniciar sesión: activado");
        }

        Console.WriteLine("Reinicia las sesiones de Claude Code abiertas para que carguen los hooks.");
        return 0;
    }

    /// <summary>Retira statusline, hooks, las reglas deny y el arranque al iniciar sesión. Los datos se conservan.</summary>
    public static int Uninstall()
    {
        var settingsPath = AppPaths.ClaudeSettingsFile;
        if (File.Exists(settingsPath))
        {
            var backup = SettingsInstaller.Save(settingsPath, SettingsInstaller.Uninstall(SettingsInstaller.Load(settingsPath)));
            Console.WriteLine($"Retirado de {settingsPath}");
            if (backup is not null) Console.WriteLine($"Copia del original: {backup}");
        }

        Autostart.Disable();

        Console.WriteLine($"Los datos siguen en {AppPaths.Data}; bórralos a mano si ya no los quieres.");
        return 0;
    }

    /// <summary>Muestra el estado de la instalación sin cambiar nada.</summary>
    public static int Check()
    {
        var settingsPath = AppPaths.ClaudeSettingsFile;
        bool installed;
        bool protectedFindings;
        bool protectedBackups;
        try
        {
            var settings = SettingsInstaller.Load(settingsPath);
            installed = SettingsInstaller.IsInstalled(settings);
            var deny = SettingsInstaller.DenyRules(settings);
            protectedFindings = deny.Contains(SettingsInstaller.SecurityDenyRule(AppPaths.Security));
            protectedBackups = deny.Contains(SettingsInstaller.BackupsDenyRule(AppPaths.Backups));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"settings.json ilegible: {ex.Message}");
            return 1;
        }

        var autostart = Autostart.IsEnabled;
        var limits = JsonStore.Read<LimitsSnapshot>(AppPaths.LimitsFile);

        Console.WriteLine($"Ejecutable:        {Environment.ProcessPath}");
        Console.WriteLine($"Datos:             {AppPaths.Data}");
        Console.WriteLine($"settings.json:     {settingsPath}");
        Console.WriteLine($"Registrado:        {(installed ? "sí" : "no")}");
        Console.WriteLine($"Hallazgos ocultos: {(protectedFindings ? "sí, regla deny en settings.json" : "no; ak install añade la regla deny")}");
        Console.WriteLine($"Copias ocultas:    {(protectedBackups ? "sí, regla deny en settings.json" : "no; ak install añade la regla deny")}");
        Console.WriteLine($"Arranque:          {(autostart ? "sí" : "no")}");
        Console.WriteLine($"Ventana abierta:   {(Process.GetProcessesByName(Path.GetFileNameWithoutExtension(AppPaths.WindowPath)).Length > 0 ? "sí" : "no")}");
        Console.WriteLine($"Sesiones:          {SessionStore.LoadAll().Count}");
        if (limits is null)
        {
            Console.WriteLine("Límites:           sin datos todavía (llegan tras la primera respuesta de una sesión)");
        }
        else
        {
            Console.WriteLine($"Límites ({Format.Ago(limits.UpdatedUtc)}):");
            foreach (var l in limits.Limits)
                Console.WriteLine($"  {l.Key,-24} {l.UsedPct,5:0.#}%  reinicio {Format.Until(l.ResetsAtUtc)}");
        }
        if (File.Exists(AppPaths.ErrorLog)) Console.WriteLine($"Hay errores anotados en {AppPaths.ErrorLog}");
        return installed ? 0 : 1;
    }
}
