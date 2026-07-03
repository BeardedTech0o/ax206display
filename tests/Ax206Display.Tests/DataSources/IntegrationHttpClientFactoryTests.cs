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

    private static X509Certificate2 CreateEphemeralCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=ax206display-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(5));
    }
}
