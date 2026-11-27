using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Arkko.Core;

namespace Arkko.Tests;

public class BoardHttpTests
{
    /// <summary>Una CA inventada y un certificado de servidor firmado por ella, como el panel con su CA interna.</summary>
    private static (X509Certificate2 Root, X509Certificate2 Server) InternalCa()
    {
        using var rootKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest("CN=CA Interna de Prueba", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        var root = rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

        using var serverKey = RSA.Create(2048);
        var serverRequest = new CertificateRequest("CN=panel.example.test", serverKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var serial = new byte[8];
        RandomNumberGenerator.Fill(serial);
        var server = serverRequest.Create(root, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddMonths(6), serial);
        return (root, server);
    }

    [Fact]
    public void Sin_errores_se_acepta_siempre()
    {
        var (_, server) = InternalCa();
        Assert.True(BoardHttp.Accept(server, SslPolicyErrors.None, new BoardTrust()));
    }

    [Fact]
    public void Una_CA_interna_sin_confianza_propia_se_rechaza()
    {
        var (_, server) = InternalCa();
        Assert.False(BoardHttp.Accept(server, SslPolicyErrors.RemoteCertificateChainErrors, new BoardTrust()));
    }

    [Fact]
    public void Con_la_huella_fijada_se_acepta_ese_certificado_y_ningun_otro()
    {
        var (_, server) = InternalCa();
        var (_, other) = InternalCa();
        var pin = BoardHttp.Fingerprint(server);

        Assert.True(BoardHttp.Accept(server, SslPolicyErrors.RemoteCertificateChainErrors, new BoardTrust(pin)));
        // Escrita sin «:» y en minúsculas, también vale.
        Assert.True(BoardHttp.Accept(server, SslPolicyErrors.RemoteCertificateChainErrors, new BoardTrust(pin.Replace(":", "").ToLowerInvariant())));
        Assert.False(BoardHttp.Accept(other, SslPolicyErrors.RemoteCertificateChainErrors, new BoardTrust(pin)));
    }

    [Fact]
    public void Con_la_raiz_propia_se_acepta_lo_que_firma_y_no_lo_de_otra()
    {
        var (root, server) = InternalCa();
        var (_, foreign) = InternalCa();
        var trust = new BoardTrust(RootCertificate: root.Export(X509ContentType.Cert));

        Assert.True(BoardHttp.Accept(server, SslPolicyErrors.RemoteCertificateChainErrors, trust));
        Assert.False(BoardHttp.Accept(foreign, SslPolicyErrors.RemoteCertificateChainErrors, trust));
        // En PEM, como suelen repartirse.
        Assert.True(BoardHttp.Accept(server, SslPolicyErrors.RemoteCertificateChainErrors,
            new BoardTrust(RootCertificate: System.Text.Encoding.ASCII.GetBytes(root.ExportCertificatePem()))));
    }

    [Fact]
    public void Un_nombre_que_no_coincide_no_se_arregla_con_la_huella()
    {
        var (_, server) = InternalCa();
        var trust = new BoardTrust(BoardHttp.Fingerprint(server));
        Assert.False(BoardHttp.Accept(server, SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateChainErrors, trust));
        Assert.False(BoardHttp.Accept(null, SslPolicyErrors.RemoteCertificateNotAvailable, trust));
    }
}
