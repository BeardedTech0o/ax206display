using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Ax206Display.Config.Models;
using Ax206Display.DataSources.Http;

namespace Ax206Display.Tests.DataSources;

public class IntegrationHttpClientFactoryTests
{
    [Fact]
    public void IsPinnedCertificateMatch_MatchingThumbprint_ReturnsTrue()
    {
        using var cert = CreateEphemeralCertificate();
        var thumbprint = cert.GetCertHashString(HashAlgorithmName.SHA256);

        var result = IntegrationHttpClientFactory.IsPinnedCertificateMatch(cert, thumbprint);

        Assert.True(result);
    }

    [Fact]
    public void IsPinnedCertificateMatch_ThumbprintComparisonIsCaseInsensitive()
    {
        using var cert = CreateEphemeralCertificate();
        var thumbprint = cert.GetCertHashString(HashAlgorithmName.SHA256).ToLowerInvariant();

        var result = IntegrationHttpClientFactory.IsPinnedCertificateMatch(cert, thumbprint);

        Assert.True(result);
    }

    [Fact]
    public void IsPinnedCertificateMatch_DifferentCertificate_ReturnsFalse()
    {
        using var pinnedCert = CreateEphemeralCertificate();
        using var presentedCert = CreateEphemeralCertificate();

        var result = IntegrationHttpClientFactory.IsPinnedCertificateMatch(
            presentedCert, pinnedCert.GetCertHashString(HashAlgorithmName.SHA256));

        Assert.False(result);
    }

    [Fact]
    public void IsPinnedCertificateMatch_NullCertificate_ReturnsFalse()
    {
        var result = IntegrationHttpClientFactory.IsPinnedCertificateMatch(null, "anything");

        Assert.False(result);
    }

    [Fact]
    public void CreateHandler_NoPinnedThumbprint_DoesNotInstallCustomValidation()
    {
        var config = MakeConfig(pinnedThumbprint: null);

        using var handler = IntegrationHttpClientFactory.CreateHandler(config, enableCookies: false);

        Assert.Null(handler.ServerCertificateCustomValidationCallback);
    }

    [Fact]
    public void CreateHandler_WithPinnedThumbprint_InstallsCustomValidation()
    {
        var config = MakeConfig(pinnedThumbprint: "AABBCC");

        using var handler = IntegrationHttpClientFactory.CreateHandler(config, enableCookies: false);

        Assert.NotNull(handler.ServerCertificateCustomValidationCallback);
    }

    [Fact]
    public void CreateHandler_CookiesDisabled_DoesNotEnableCookieJar()
    {
        var config = MakeConfig(pinnedThumbprint: null);

        using var handler = IntegrationHttpClientFactory.CreateHandler(config, enableCookies: false);

        Assert.False(handler.UseCookies);
    }

    [Fact]
    public void CreateHandler_CookiesEnabled_EnablesCookieJar()
    {
        var config = MakeConfig(pinnedThumbprint: null);

        using var handler = IntegrationHttpClientFactory.CreateHandler(config, enableCookies: true);

        Assert.True(handler.UseCookies);
    }

    [Fact]
    public void Create_SetsBaseAddressAndDefaultTimeout()
    {
        var config = MakeConfig(pinnedThumbprint: null) with { BaseUrl = "https://pve.local:8006" };

        using var client = IntegrationHttpClientFactory.Create(config, enableCookies: false);

        Assert.Equal(new Uri("https://pve.local:8006"), client.BaseAddress);
        Assert.Equal(IntegrationHttpClientFactory.DefaultTimeout, client.Timeout);
    }

    private static IntegrationConfig MakeConfig(string? pinnedThumbprint) => new()
    {
        Id = "test",
        Kind = "proxmox",
        BaseUrl = "https://example.local",
        PinnedCertificateSha256Thumbprint = pinnedThumbprint,
    };

    [Fact]
    public void CreateHandler_DoesNotFollowRedirects()
    {
        using var handler = IntegrationHttpClientFactory.CreateHandler(
            new IntegrationConfig { Id = "x", Kind = "unifi", BaseUrl = "https://192.168.1.1" },
            enableCookies: true);

        Assert.False(handler.AllowAutoRedirect);
    }

    [Fact]
    public async Task Create_ReportsARedirectInsteadOfFollowingItToAnotherAddress()
    {
        // A host that passes the address check could answer the login with a
        // redirect to somewhere the check never saw (or somewhere that would
        // receive the credentials). The second listener must never be hit.
        using var target = new HttpListener();
        var targetPort = StartOnFreePort(target);
        var targetHits = 0;
        var targetTask = Task.Run(async () =>
        {
            try
            {
                var context = await target.GetContextAsync();
                Interlocked.Increment(ref targetHits);
                context.Response.Close();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                // Stopped by the test: nothing arrived, which is the passing case.
            }
        });

        using var redirector = new HttpListener();
        var redirectorPort = StartOnFreePort(redirector);
        var redirectorTask = Task.Run(async () =>
        {
            var context = await redirector.GetContextAsync();
            context.Response.StatusCode = 302;
            context.Response.RedirectLocation = $"http://127.0.0.1:{targetPort}/stolen";
            context.Response.Close();
        });

        using var client = IntegrationHttpClientFactory.Create(
            new IntegrationConfig { Id = "x", Kind = "unifi", BaseUrl = $"http://127.0.0.1:{redirectorPort}" },
            enableCookies: false);

        using var response = await client.PostAsync("/api/auth/login", new StringContent("secret"));
        await redirectorTask;
        target.Stop();
        await targetTask;

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(0, targetHits);
    }

    private static int StartOnFreePort(HttpListener listener)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            try
            {
                listener.Prefixes.Clear();
                listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                listener.Start();
                return port;
            }
            catch (HttpListenerException)
            {
                // Port taken between the probe and the bind; try another.
            }
        }

        throw new InvalidOperationException("Could not find a free loopback port.");
    }

    private static X509Certificate2 CreateEphemeralCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=ax206display-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(5));
    }
}
