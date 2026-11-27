using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Arkko.Core;

/// <summary>En qué confía una conexión de tablero además de las CA del equipo: un certificado concreto por su huella, o una raíz propia.</summary>
/// <param name="PinnedSha256">Huella SHA-256 del certificado del servidor que la persona ha confirmado, con o sin «:».</param>
/// <param name="RootCertificate">Certificado raíz propio (DER o PEM), como el de una CA interna que no está instalada en el equipo.</param>
public sealed record BoardTrust(string? PinnedSha256 = null, byte[]? RootCertificate = null);

/// <summary>El certificado que presenta un servidor, para que la persona lo confirme antes de confiar en él.</summary>
public sealed record ServerCertificate(string Fingerprint, string Subject, string Issuer, DateTime NotAfterUtc, bool TrustedByWindows);

/// <summary>Conexiones HTTPS de los tableros: comprueban siempre el certificado, y no hay forma de desactivarlo.</summary>
/// <remarks>
/// Un panel firmado por una CA interna no instalada falla la comprobación normal. En lugar de saltársela
/// (como `curl -k`), la conexión puede confiar en ese certificado exacto, por su huella, o en esa raíz
/// concreta; el nombre del servidor tiene que coincidir siempre. Un servidor impostor no tendría ni la
/// misma huella ni un certificado firmado por esa raíz.
/// </remarks>
public static class BoardHttp
{
    /// <summary>Cliente HTTP que acepta los certificados del equipo y, además, los que permite <paramref name="trust"/>.</summary>
    public static HttpClient Create(BoardTrust trust, TimeSpan? timeout = null)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, cert, _, errors) => Accept(cert, errors, trust),
        };
        return new HttpClient(handler) { Timeout = timeout ?? TimeSpan.FromSeconds(30) };
    }

    /// <summary>Decide si se acepta un certificado: el del equipo sin errores, o el que permite la conexión con el nombre correcto.</summary>
    public static bool Accept(X509Certificate2? cert, SslPolicyErrors errors, BoardTrust trust)
    {
        if (errors == SslPolicyErrors.None) return true;
        // Un nombre que no coincide o un servidor sin certificado no se arregla con ninguna confianza propia.
        if (cert is null || errors != SslPolicyErrors.RemoteCertificateChainErrors) return false;

        if (trust.PinnedSha256 is { Length: > 0 } pin && Normalize(pin) == Normalize(Fingerprint(cert))) return true;
        return trust.RootCertificate is { Length: > 0 } root && ChainsTo(cert, root);
    }

    /// <summary>Huella SHA-256 de un certificado, en hexadecimal con «:», como la enseñan los navegadores.</summary>
    public static string Fingerprint(X509Certificate2 cert) =>
        string.Join(":", cert.GetCertHash(HashAlgorithmName.SHA256).Select(b => b.ToString("X2")));

    /// <summary>Pide al servidor su certificado sin enviar nada más, para enseñarlo antes de confiar en él.</summary>
    public static async Task<ServerCertificate> ServerCertificateAsync(Uri address, CancellationToken ct = default)
    {
        ServerCertificate? seen = null;
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, cert, _, errors) =>
            {
                if (cert is not null)
                    seen = new ServerCertificate(Fingerprint(cert), cert.Subject, cert.Issuer, cert.NotAfter.ToUniversalTime(),
                        errors == SslPolicyErrors.None);
                // Se corta aquí: solo se quería ver el certificado, sin llegar a mandar la petición.
                return false;
            },
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        try
        {
            using var _ = await http.GetAsync(new Uri(address, "/"), ct);
        }
        catch (HttpRequestException) when (seen is not null)
        {
        }
        catch (HttpRequestException ex)
        {
            throw new BoardException($"No se ha podido conectar con {address.Host}: {ex.Message}", ex);
        }
        return seen ?? throw new BoardException($"{address.Host} no ha presentado ningún certificado.");
    }

    /// <summary>Indica si el certificado se encadena hasta la raíz dada, sin mirar las raíces del equipo.</summary>
    /// <remarks>Sin comprobar la revocación: una CA interna no suele publicar dónde mirarla, y el certificado no lo dice.</remarks>
    private static bool ChainsTo(X509Certificate2 cert, byte[] rootBytes)
    {
        try
        {
            using var root = ReadCertificate(rootBytes);
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(root);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            return chain.Build(cert);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>Lee un certificado en DER o en PEM; si no es un certificado, lanza CryptographicException.</summary>
    public static X509Certificate2 ReadCertificate(byte[] bytes)
    {
        var text = System.Text.Encoding.ASCII.GetString(bytes);
        return text.Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal)
            ? X509Certificate2.CreateFromPem(text)
            : new X509Certificate2(bytes);
    }

    /// <summary>Huella sin separadores ni espacios y en mayúsculas, para comparar las escritas de una forma u otra.</summary>
    private static string Normalize(string fingerprint) =>
        new string(fingerprint.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
}
