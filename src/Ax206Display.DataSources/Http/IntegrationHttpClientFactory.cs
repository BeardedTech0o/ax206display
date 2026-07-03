using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Ax206Display.Config.Models;

namespace Ax206Display.DataSources.Http;

/// <summary>
/// The single blessed way to build an <see cref="HttpClient"/> for a UniFi or
/// Proxmox <see cref="IntegrationConfig"/>: applies a sane default timeout and,
/// when the integration serves a self-signed certificate (the common case on a
/// LAN), pins to that exact certificate instead of disabling TLS validation
/// outright. There is deliberately no "accept any certificate" code path here -
/// a config with no pinned thumbprint just gets normal system validation.
/// </summary>
public static class IntegrationHttpClientFactory
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    /// <param name="enableCookies">
    /// UniFi OS relies on a session cookie set by /api/auth/login flowing
    /// automatically to later requests, so its client needs a cookie jar.
    /// Proxmox manages its PVEAuthCookie header manually, so its client should
    /// pass false to avoid the handler's own cookie jar interfering.
    /// </param>
    public static HttpClient Create(IntegrationConfig config, bool enableCookies)
    {
        var handler = CreateHandler(config, enableCookies);
        return new HttpClient(handler)
        {
            BaseAddress = new Uri(config.BaseUrl),
            Timeout = DefaultTimeout,
        };
    }

    public static HttpClientHandler CreateHandler(IntegrationConfig config, bool enableCookies)
    {
        var handler = new HttpClientHandler { UseCookies = enableCookies };
        if (enableCookies)
        {
            handler.CookieContainer = new CookieContainer();
        }

        if (config.PinnedCertificateSha256Thumbprint is { Length: > 0 } expectedThumbprint)
        {
            handler.ServerCertificateCustomValidationCallback =
                (_, certificate, _, _) => IsPinnedCertificateMatch(certificate, expectedThumbprint);
        }

        return handler;
    }

    public static bool IsPinnedCertificateMatch(X509Certificate2? certificate, string expectedSha256Thumbprint)
    {
        if (certificate is null)
        {
            return false;
        }

        var actualThumbprint = certificate.GetCertHashString(HashAlgorithmName.SHA256);
        return string.Equals(actualThumbprint, expectedSha256Thumbprint, StringComparison.OrdinalIgnoreCase);
    }
}
