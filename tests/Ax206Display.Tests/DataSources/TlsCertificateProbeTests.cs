using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Ax206Display.DataSources.Http;

namespace Ax206Display.Tests.DataSources;

public class TlsCertificateProbeTests
{
    [Fact]
    public async Task FetchCertificateAsync_CapturesTheServersCertificate()
    {
        using var serverCertificate = CreateSelfSignedCertificate();
        var expectedThumbprint = serverCertificate.GetCertHashString(HashAlgorithmName.SHA256);

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var sslStream = new SslStream(client.GetStream());
            await sslStream.AuthenticateAsServerAsync(serverCertificate, clientCertificateRequired: false, checkCertificateRevocation: false);
        });

        var capturedCertificate = await TlsCertificateProbe.FetchCertificateAsync("localhost", port);
        await serverTask;

        Assert.NotNull(capturedCertificate);
        Assert.Equal(expectedThumbprint, capturedCertificate!.GetCertHashString(HashAlgorithmName.SHA256));
    }

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(5));

        // Round-trip through PFX bytes so the private key is usable by
        // SslStream's server-side handshake - CreateSelfSigned's in-memory
        // certificate can otherwise have an ephemeral key set that some
        // platforms won't let SslStream use directly.
        return new X509Certificate2(certificate.Export(X509ContentType.Pfx));
    }
}
