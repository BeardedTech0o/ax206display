using System.Security.Cryptography;
using Ax206Display.Config.Secrets;

namespace Ax206Display.Daemon.Secrets;

/// <summary>
/// Linux counterpart to the Windows app's DPAPI-backed protector. DPAPI ties
/// encryption to the logged-in Windows user/machine; there's no OS-level
/// equivalent on Linux, so this ties it to a locally-generated AES-256 key
/// file instead, restricted to owner-only access
/// (<see cref="EnsureKeyFileExists"/>). Anyone who can read that key file and
/// the encrypted secrets can decrypt them - same threat model as DPAPI plus
/// a compromised admin account, just enforced via filesystem permissions
/// instead of the OS user profile.
/// </summary>
public sealed class LinuxFileSecretProtector : ISecretProtector
{
    private const int KeySizeBytes = 32;
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    private readonly byte[] _key;

    public LinuxFileSecretProtector(string keyFilePath)
    {
        _key = EnsureKeyFileExists(keyFilePath);
    }

    public byte[] Protect(byte[] plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSizeBytes];

        using var aesGcm = new AesGcm(_key, TagSizeBytes);
        aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);

        var result = new byte[NonceSizeBytes + ciphertext.Length + TagSizeBytes];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSizeBytes);
        Buffer.BlockCopy(ciphertext, 0, result, NonceSizeBytes, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, result, NonceSizeBytes + ciphertext.Length, TagSizeBytes);
        return result;
    }

    public byte[] Unprotect(byte[] ciphertext)
    {
        if (ciphertext.Length < NonceSizeBytes + TagSizeBytes)
        {
            throw new CryptographicException("Encrypted payload is too short to contain a nonce and tag.");
        }

        var nonce = ciphertext[..NonceSizeBytes];
        var tag = ciphertext[^TagSizeBytes..];
        var cipherSpan = ciphertext.AsSpan(NonceSizeBytes, ciphertext.Length - NonceSizeBytes - TagSizeBytes);
        var plaintext = new byte[cipherSpan.Length];

        using var aesGcm = new AesGcm(_key, TagSizeBytes);
        aesGcm.Decrypt(nonce, cipherSpan, tag, plaintext);
        return plaintext;
    }

    private static byte[] EnsureKeyFileExists(string keyFilePath)
    {
        var directory = Path.GetDirectoryName(keyFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            if (OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        if (File.Exists(keyFilePath))
        {
            return Convert.FromBase64String(File.ReadAllText(keyFilePath));
        }

        var key = RandomNumberGenerator.GetBytes(KeySizeBytes);
        File.WriteAllText(keyFilePath, Convert.ToBase64String(key));
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(keyFilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return key;
    }
}
