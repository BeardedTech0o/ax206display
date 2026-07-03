using Ax206Display.Config.Secrets;
using Ax206Display.Tests.TestSupport;

namespace Ax206Display.Tests.Config;

public class SecretStoreTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _filePath;

    public SecretStoreTests()
    {
        _tempDirectory = Directory.CreateTempSubdirectory("ax206display-tests-").FullName;
        _filePath = Path.Combine(_tempDirectory, "secrets.dat");
    }

    [Fact]
    public void SetAndGetSecret_RoundTripsThroughTheProtector()
    {
        var store = new SecretStore(new FakeSecretProtector(), _filePath);

        store.SetSecret("unifi-password", "hunter2");

        Assert.Equal("hunter2", store.GetSecret("unifi-password"));
    }

    [Fact]
    public void GetSecret_UnknownKey_ReturnsNull()
    {
        var store = new SecretStore(new FakeSecretProtector(), _filePath);

        Assert.Null(store.GetSecret("missing"));
    }

    [Fact]
    public async Task SaveThenLoad_PersistsEncryptedValuesAcrossInstances()
    {
        var protector = new FakeSecretProtector();
        var first = new SecretStore(protector, _filePath);
        first.SetSecret("proxmox-password", "s3cr3t");
        await first.SaveAsync();

        var second = new SecretStore(protector, _filePath);
        await second.LoadAsync();

        Assert.Equal("s3cr3t", second.GetSecret("proxmox-password"));
    }

    [Fact]
    public void RemoveSecret_DeletesTheEntry()
    {
        var store = new SecretStore(new FakeSecretProtector(), _filePath);
        store.SetSecret("temp", "value");

        store.RemoveSecret("temp");

        Assert.Null(store.GetSecret("temp"));
    }

    public void Dispose()
    {
        Directory.Delete(_tempDirectory, recursive: true);
        GC.SuppressFinalize(this);
    }
}
