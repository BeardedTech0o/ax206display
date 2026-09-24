using Ax206Display.Server.Auth;

namespace Ax206Display.Tests.Server;

public class WebPasswordStoreTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("ax206display-web-").FullName;

    [Fact]
    public void EnsureInitialized_WritesAWorkingOneTimePassword()
    {
        var store = new WebPasswordStore(_directory);

        Assert.True(store.EnsureInitialized());

        var generated = File.ReadAllText(store.InitialPasswordFilePath).Trim();
        Assert.Equal(16, generated.Length);
        Assert.True(store.Verify(generated));
        Assert.False(store.Verify(generated + "x"));
    }

    [Fact]
    public void EnsureInitialized_Twice_KeepsTheFirstPassword()
    {
        var store = new WebPasswordStore(_directory);
        store.EnsureInitialized();
        var generated = File.ReadAllText(store.InitialPasswordFilePath).Trim();

        Assert.False(new WebPasswordStore(_directory).EnsureInitialized());
        Assert.True(new WebPasswordStore(_directory).Verify(generated));
    }

    [Fact]
    public void SetPassword_ReplacesItDeletesTheInitialFileAndChangesTheStamp()
    {
        var store = new WebPasswordStore(_directory);
        store.EnsureInitialized();
        var generated = File.ReadAllText(store.InitialPasswordFilePath).Trim();
        var stampBefore = store.Stamp;

        store.SetPassword("a much better password");

        Assert.False(File.Exists(store.InitialPasswordFilePath));
        Assert.False(store.Verify(generated));
        Assert.True(new WebPasswordStore(_directory).Verify("a much better password"));
        Assert.NotEqual(stampBefore, store.Stamp);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    public void SetPassword_TooShort_Throws(string password)
    {
        Assert.Throws<ArgumentException>(() => new WebPasswordStore(_directory).SetPassword(password));
    }

    [Fact]
    public void Verify_WithNoPasswordSet_RejectsEverything()
    {
        var store = new WebPasswordStore(_directory);

        Assert.False(store.Verify(string.Empty));
        Assert.False(store.Verify("anything"));
    }

    [Fact]
    public void PasswordFiles_AreOwnerOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var store = new WebPasswordStore(_directory);
        store.EnsureInitialized();

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(store.InitialPasswordFilePath));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(_directory, "web-password.json")));
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }
}
