using System.Text;
using Arkko.Core;
using Arkko.Hook;

// Punto de entrada de ak. Claude Code lo invoca como statusline y como hook; la persona,
// para instalar, desinstalar o diagnosticar. Como statusline o hook nunca devuelve error: un fallo
// aquí no debe interrumpir la sesión, así que se anota en data/errors.log y se sale con 0.

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "";
if (command is not ("statusline" or "hook"))
{
    try { Console.OutputEncoding = new UTF8Encoding(false); } catch (IOException) { }
}

try
{
    return command switch
    {
        "statusline" => Statusline.Run(ReadStdin()),
        "hook" => HookHandler.Run(ReadStdin()),
        "install" => Installer.Install(args.Contains("--autostart")),
        "uninstall" => Installer.Uninstall(),
        "check" => Installer.Check(),
        _ => Usage(),
    };
}
catch (Exception ex) when (command is "statusline" or "hook")
{
    JsonStore.LogError(command, ex);
    if (command == "hook") IO.Write("{}");
    return 0;
}

/// <summary>Lee stdin completo como UTF-8, sin depender de la codepage de la consola.</summary>
static string ReadStdin()
{
    using var stdin = Console.OpenStandardInput();
    using var reader = new StreamReader(stdin, new UTF8Encoding(false));
    return reader.ReadToEnd();
}

/// <summary>Muestra la ayuda de la línea de comandos.</summary>
static int Usage()
{
    IO.Write("""
        ak - datos de Claude Code para Arkko

          ak install [--autostart]   registra statusline, hooks y la regla deny en settings.json
          ak uninstall               los retira
          ak check                   diagnóstico, no cambia nada

        Claude Code lo llama como "ak statusline" y "ak hook".

        """);
    return 1;
}
