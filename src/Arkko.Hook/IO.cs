using System.Text;

namespace Arkko.Hook;

/// <summary>Salida por stdout en UTF-8, sin depender de la codepage de la consola.</summary>
internal static class IO
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>Escribe el texto tal cual en stdout.</summary>
    public static void Write(string text)
    {
        using var stdout = Console.OpenStandardOutput();
        var bytes = Utf8NoBom.GetBytes(text);
        stdout.Write(bytes, 0, bytes.Length);
    }
}
