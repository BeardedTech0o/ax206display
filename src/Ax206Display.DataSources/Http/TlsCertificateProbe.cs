using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace Ax206Display.DataSources.Http;

/// <summary>
/// Fetches the certificate a host presents during a TLS handshake, for a
/// one-click "detect this server's certificate" flow in the Integrations UI
/// instead of requiring the user to manually extract/paste a SHA-256
/// thumbprint via a browser or openssl. This is a trust-on-first-use
/// pattern: the caller is expected to show the thumbprint to the user for
/// confirmation before pinning it via
/// <see cref="IntegrationHttpClientFactory.IsPinnedCertificateMatch"/> -
/// the handshake here accepts any certificate unconditionally purely to
/// capture it, it is never used to make a real request.
/// </summary>
public static class TlsCertificateProbe
{
    public static async Task<X509Certificate2?> FetchCertificateAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(host, port, cancellationToken);

        X509Certificate2? captured = null;

        // CA5359 flags accepting-any-certificate callbacks because they
        // usually mean real traffic will trust an unverified peer. That
        // doesn't apply here: this handshake exists purely to capture the
        // certificate for the caller to show the user before pinning it
        // (trust-on-first-use) - no request or response ever crosses this
        // connection, and it's torn down immediately after.
#pragma warning disable CA5359
        await using var sslStream = new SslStream(
            tcpClient.GetStream(),
            leaveInnerStreamOpen: false,
            userCertificateValidationCallback: (_, certificate, _, _) =>
            {
                if (certificate is not null)
                {
                    captured = new X509Certificate2(certificate);
                }

                return true;
            });
#pragma warning restore CA5359

        var options = new SslClientAuthenticationOptions { TargetHost = host };
        await sslStream.AuthenticateAsClientAsync(options, cancellationToken);

        return captured;
    }
}
