using Ax206Display.Config.Secrets;
using Ax206Display.Config.Services;
using Ax206Display.DataSources.Auth;
using Ax206Display.Engine.Integrations;
using Ax206Display.Tests.TestSupport;

namespace Ax206Display.Tests.Engine;

public sealed class IntegrationSetupServiceTests : IDisposable
{
    private const string TotpSecret = "JBSWY3DPEHPK3PXP";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ax206-setup-" + Guid.NewGuid().ToString("N"));
    private readonly ConfigService _config;
    private readonly IntegrationSetupService _service;

    public IntegrationSetupServiceTests()
    {
        Directory.CreateDirectory(_directory);
        _config = new ConfigService(Path.Combine(_directory, "config.json"));
        var secrets = new SecretStore(new FakeSecretProtector(), Path.Combine(_directory, "secrets.dat"));
        _service = new IntegrationSetupService(_config, secrets);
    }

    [Fact]
    public async Task UniFiFailure_NeverEchoesALiveTotpCode()
    {
        // Nothing listens on port 1, so the login fails fast and the
        // failure path (which builds the 2FA hint) runs.
        var request = new IntegrationSetupRequest
        {
            BaseUrl = "http://127.0.0.1:1",
            Username = "admin",
            Secret = "password",
            TotpSecret = TotpSecret,
        };

        var result = await _service.TestAndSaveAsync(IntegrationSetupService.UniFiKind, request);

        Assert.False(result.Success);
        Assert.Contains("2FA secret", result.Message, StringComparison.Ordinal);

        // Allow for the code rolling over between the failure and this check.
        var now = DateTimeOffset.UtcNow;
        foreach (var moment in new[] { now.AddSeconds(-60), now.AddSeconds(-30), now, now.AddSeconds(30) })
        {
            Assert.DoesNotContain(TotpGenerator.GenerateCode(TotpSecret, moment), result.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task UniFiFailure_FlagsATotpSecretThatIsNotBase32()
    {
        var request = new IntegrationSetupRequest
        {
            BaseUrl = "http://127.0.0.1:1",
            Username = "admin",
            Secret = "password",
            TotpSecret = "not base32 !!!",
        };

        var result = await _service.TestAndSaveAsync(IntegrationSetupService.UniFiKind, request);

        Assert.False(result.Success);
        Assert.Contains("isn't valid base32", result.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(IntegrationSetupService.UniFiKind)]
    [InlineData(IntegrationSetupService.ProxmoxKind)]
    public async Task TestAndSave_RejectsLinkLocalMetadataAddress(string kind)
    {
        var request = new IntegrationSetupRequest { BaseUrl = "http://169.254.169.254", Username = "u", Secret = "p" };

        var result = await _service.TestAndSaveAsync(kind, request);

        Assert.False(result.Success);
        Assert.Contains("isn't allowed", result.Message, StringComparison.Ordinal);
        Assert.Empty((await _config.LoadAsync()).Integrations);
    }

    [Fact]
    public async Task TestAndSavePiHole_RejectsLinkLocalAddress()
    {
        var request = new IntegrationSetupRequest { Host = "169.254.169.254", Port = 80, Secret = "p" };

        var result = await _service.TestAndSaveAsync(IntegrationSetupService.PiHoleKind, request);

        Assert.False(result.Success);
        Assert.Contains("isn't allowed", result.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ftp://192.168.1.1")]
    [InlineData("http://169.254.169.254")]
    [InlineData("not a url")]
    public async Task DetectCertificateThumbprint_RejectsUnsafeAddressesBeforeConnecting(string url)
    {
        await Assert.ThrowsAsync<FormatException>(() => IntegrationSetupService.DetectCertificateThumbprintAsync(url));
    }

    public void Dispose()
    {
        _service.Dispose();
        _config.Dispose();
        Directory.Delete(_directory, recursive: true);
    }
}
