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

    [Fact]
    public async Task SaveAsync_CreatesTheFileOwnerOnlyOnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Windows relies on the directory ACL instead.
        }

        var store = new SecretStore(new FakeSecretProtector(), _filePath);
        store.SetSecret("k", "v");
        await store.SaveAsync();

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(_filePath));
    }

    [Fact]
    public async Task SaveAsync_TightensAnExistingWorldReadableFile()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // A file left behind by an older version (or a hand-started run with a
        // permissive umask).
        await File.WriteAllTextAsync(_filePath, "{}");
        File.SetUnixFileMode(_filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        var store = new SecretStore(new FakeSecretProtector(), _filePath);
        store.SetSecret("k", "v");
        await store.SaveAsync();

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(_filePath));
    }

    [Fact]
    public async Task SaveAsync_LeavesNoTempFileBehindAndSurvivesRepeatedSaves()
    {
        var store = new SecretStore(new FakeSecretProtector(), _filePath);

        for (var i = 0; i < 3; i++)
        {
            store.SetSecret("k", "v" + i);
            await store.SaveAsync();
        }

        Assert.False(File.Exists(_filePath + ".tmp"));

        var reloaded = new SecretStore(new FakeSecretProtector(), _filePath);
        await reloaded.LoadAsync();
        Assert.Equal("v2", reloaded.GetSecret("k"));
    }

    public void Dispose()
    {
        Directory.Delete(_tempDirectory, recursive: true);
        GC.SuppressFinalize(this);
    }
}
