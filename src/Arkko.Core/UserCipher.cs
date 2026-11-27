using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Arkko.Core;

/// <summary>Cifra y descifra datos para el usuario actual: DPAPI en Windows; en macOS y Linux, AES-GCM con una clave del llavero del sistema.</summary>
/// <remarks>
/// En Windows los datos son los de siempre, así que lo ya cifrado se sigue leyendo sin migrar nada. Sin llavero
/// no hay alternativa en claro: se lanza <see cref="KeyringUnavailableException"/>, que no es un error de
/// descifrado, para que nadie tome un fichero bueno por dañado y lo sobrescriba.
/// </remarks>
internal static class UserCipher
{
    /// <summary>Cifra unos bytes para el usuario actual.</summary>
    public static byte[] Protect(byte[] data) =>
        OperatingSystem.IsWindows() ? Dpapi.Protect(data) : KeyringCipher.Seal(KeyringCipher.MasterKey(), data);

    /// <summary>Descifra unos bytes cifrados para el usuario actual.</summary>
    public static byte[] Unprotect(byte[] data) =>
        OperatingSystem.IsWindows() ? Dpapi.Unprotect(data) : KeyringCipher.Open(KeyringCipher.MasterKey(), data);
}

/// <summary>El llavero del sistema no está o no responde: lo cifrado no se puede leer ni guardar.</summary>
public sealed class KeyringUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Cifrado de Windows ligado al usuario (DPAPI), sin paquetes externos.</summary>
internal static class Dpapi
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Blob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr mem);

    private const int UiForbidden = 0x1;

    /// <summary>Cifra unos bytes para el usuario actual.</summary>
    public static byte[] Protect(byte[] data) => Run(data, protect: true);

    /// <summary>Descifra unos bytes cifrados para el usuario actual.</summary>
    public static byte[] Unprotect(byte[] data) => Run(data, protect: false);

    /// <summary>Llama a DPAPI con los bytes dados y devuelve el resultado.</summary>
    private static byte[] Run(byte[] data, bool protect)
    {
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            var input = new Blob { Size = data.Length, Data = handle.AddrOfPinnedObject() };
            var ok = protect
                ? CryptProtectData(ref input, "Agent Pulse", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, out var output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output);
            if (!ok) throw new CryptographicException(Marshal.GetLastWin32Error());
            try
            {
                var result = new byte[output.Size];
                Marshal.Copy(output.Data, result, 0, output.Size);
                return result;
            }
            finally
            {
                LocalFree(output.Data);
            }
        }
        finally
        {
            handle.Free();
        }
    }
}

/// <summary>AES-GCM con una clave maestra guardada en el llavero del sistema, para macOS y Linux.</summary>
/// <remarks>Cada dato cifrado lleva una marca, el nonce y la etiqueta delante del texto cifrado.</remarks>
internal static class KeyringCipher
{
    private static readonly byte[] Magic = "APK1"u8.ToArray();
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const string Service = "arkko";
    private const string Account = "master-key";

    // El servicio de cuando la app se llamaba Agent Pulse: su clave se copia al nuevo la primera vez.
    private const string OldService = "agent-pulse";

    private static readonly object Gate = new();
    private static byte[]? _key;

    /// <summary>La clave maestra del usuario: la del llavero, la de cuando la app se llamaba Agent Pulse (que se copia
    /// al servicio nuevo) o, la primera vez, una nueva que se guarda allí.</summary>
    /// <remarks>Crear una nueva habiendo una vieja dejaría ilegible todo lo cifrado con ella.</remarks>
    public static byte[] MasterKey()
    {
        lock (Gate)
        {
            if (_key is not null) return _key;
            // La ventana y un hook pueden pedirla a la vez: solo uno debe crearla.
            using var _ = JsonStore.Lock("keyring-master");
            var stored = Keyring.Read(Service, Account);
            if (stored is null && Keyring.Read(OldService, Account) is { } old)
            {
                Keyring.Write(Service, Account, old);
                stored = old;
            }
            if (stored is null)
            {
                Keyring.Write(Service, Account, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
                stored = Keyring.Read(Service, Account)
                    ?? throw new KeyringUnavailableException("El llavero del sistema no guardó la clave de Arkko.");
            }
            return _key = Convert.FromHexString(stored);
        }
    }

    /// <summary>Cifra unos bytes con una clave.</summary>
    public static byte[] Seal(byte[] key, byte[] data)
    {
        var header = Magic.Length + NonceSize + TagSize;
        var result = new byte[header + data.Length];
        Magic.CopyTo(result, 0);
        var nonce = result.AsSpan(Magic.Length, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, data, result.AsSpan(header), result.AsSpan(Magic.Length + NonceSize, TagSize));
        return result;
    }

    /// <summary>Descifra lo cifrado con <see cref="Seal"/>; lanza CryptographicException si es de otra clave o está dañado.</summary>
    public static byte[] Open(byte[] key, byte[] blob)
    {
        var header = Magic.Length + NonceSize + TagSize;
        if (blob.Length < header || !blob.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new CryptographicException("No es un dato cifrado por Arkko en este sistema.");
        var result = new byte[blob.Length - header];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(blob.AsSpan(Magic.Length, NonceSize), blob.AsSpan(header), blob.AsSpan(Magic.Length + NonceSize, TagSize), result);
        return result;
    }
}

/// <summary>Lee y guarda un secreto en el llavero del sistema: Keychain en macOS y libsecret en Linux.</summary>
/// <remarks>
/// El secreto viaja siempre por la entrada o la salida estándar, nunca en la línea de comandos, que puede
/// leer cualquier proceso del usuario. En macOS, la primera vez el sistema puede pedir permiso para usar el
/// llavero; si nadie responde, se corta a los 15 segundos.
/// </remarks>
internal static class Keyring
{
    // `security` sale con este código cuando la entrada no existe (errSecItemNotFound).
    private const int MacNotFound = 44;

    /// <summary>El secreto guardado; null si no hay ninguno.</summary>
    public static string? Read(string service, string account)
    {
        if (OperatingSystem.IsMacOS())
        {
            var (exit, output, error) = Run("security", ["find-generic-password", "-s", service, "-a", account, "-w"], null);
            if (exit == 0 && output.Trim().Length > 0) return output.Trim();
            if (exit == MacNotFound) return null;
            throw Unavailable("security", error);
        }
        else
        {
            var (exit, output, error) = Run("secret-tool", ["lookup", "service", service, "account", account], null);
            if (exit == 0 && output.Trim().Length > 0) return output.Trim();
            // Sin entrada sale con 1 y sin decir nada; con un error del llavero, lo explica. Tomar un error por
            // «no hay» crearía una clave nueva y dejaría ilegible todo lo cifrado con la anterior.
            if (exit == 1 && error.Trim().Length == 0) return null;
            throw Unavailable("secret-tool", error);
        }
    }

    /// <summary>Guarda un secreto nuevo; en macOS no sustituye uno que ya exista.</summary>
    public static void Write(string service, string account, string secret)
    {
        var (exit, _, error) = OperatingSystem.IsMacOS()
            ? Run("security", ["-i"], $"add-generic-password -s {service} -a {account} -l \"Arkko\" -w {secret}\n")
            : Run("secret-tool", ["store", "--label=Arkko", "service", service, "account", account], secret);
        if (exit != 0) throw Unavailable(OperatingSystem.IsMacOS() ? "security" : "secret-tool", error);
    }

    /// <summary>Error de un llavero que no está o no responde, con lo que dijo la herramienta.</summary>
    private static KeyringUnavailableException Unavailable(string tool, string error) =>
        new($"El llavero del sistema no responde ({tool}): {error.Trim()}");

    /// <summary>Ejecuta una herramienta del llavero con una entrada dada y devuelve su código de salida y lo que escribió.</summary>
    private static (int Exit, string Output, string Error) Run(string tool, string[] args, string? input)
    {
        var info = new ProcessStartInfo(tool)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args) info.ArgumentList.Add(a);
        Process process;
        try
        {
            process = Process.Start(info) ?? throw new KeyringUnavailableException($"No se pudo arrancar {tool}.");
        }
        catch (Win32Exception ex)
        {
            var package = OperatingSystem.IsMacOS() ? "" : " (paquete libsecret-tools)";
            throw new KeyringUnavailableException($"Falta {tool}{package}, que Arkko usa para guardar su clave en el llavero.", ex);
        }
        using (process)
        {
            if (input is not null) process.StandardInput.Write(input);
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(15_000))
            {
                process.Kill(entireProcessTree: true);
                throw new KeyringUnavailableException($"{tool} no respondió: el llavero puede estar esperando a que se desbloquee.");
            }
            return (process.ExitCode, output.Result, error.Result);
        }
    }
}
