using System.Security.Cryptography;
using System.Text;
using Ax206Display.Config.Secrets;

namespace Ax206Display.Tests.Config;

public class KeyFileSecretProtectorTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("ax206display-key-").FullName;

    private string KeyPath => Path.Combine(_directory, "state", "secret.key");

    [Fact]
    public void ProtectThenUnprotect_RoundTrips()
    {
        var protector = new KeyFileSecretProtector(KeyPath);
        var plaintext = Encoding.UTF8.GetBytes("hunter2");

        var ciphertext = protector.Protect(plaintext);

        Assert.NotEqual(plaintext, ciphertext);
        Assert.Equal(plaintext, protector.Unprotect(ciphertext));
    }

    [Fact]
    public void Protect_UsesAFreshNonceEachTime()
    {
        var protector = new KeyFileSecretProtector(KeyPath);
        var plaintext = Encoding.UTF8.GetBytes("same input");

        Assert.NotEqual(protector.Protect(plaintext), protector.Protect(plaintext));
    }

    [Fact]
    public void Key_PersistsAcrossInstances()
    {
        var ciphertext = new KeyFileSecretProtector(KeyPath).Protect(Encoding.UTF8.GetBytes("survives restart"));

        var plaintext = new KeyFileSecretProtector(KeyPath).Unprotect(ciphertext);

        Assert.Equal("survives restart", Encoding.UTF8.GetString(plaintext));
    }

    [Fact]
    public void Unprotect_WithADifferentKey_Throws()
    {
        var ciphertext = new KeyFileSecretProtector(KeyPath).Protect(Encoding.UTF8.GetBytes("secret"));
        var otherKeyPath = Path.Combine(_directory, "other.key");

        Assert.ThrowsAny<CryptographicException>(() => new KeyFileSecretProtector(otherKeyPath).Unprotect(ciphertext));
    }

    [Fact]
    public void Unprotect_TamperedCiphertext_Throws()
    {
        var protector = new KeyFileSecretProtector(KeyPath);
        var ciphertext = protector.Protect(Encoding.UTF8.GetBytes("secret"));
        ciphertext[^1] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(ciphertext));
    }

    [Fact]
    public void KeyFile_IsOwnerOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        new KeyFileSecretProtector(KeyPath).Protect([1, 2, 3]);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(KeyPath));
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(Path.GetDirectoryName(KeyPath)!));
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }
}
