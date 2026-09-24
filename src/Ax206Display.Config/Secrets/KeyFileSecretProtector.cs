using System.Security.Cryptography;

namespace Ax206Display.Config.Secrets;

/// <summary>
/// The non-Windows stand-in for <see cref="DpapiSecretProtector"/>: AES-256-GCM
/// with a random key kept in its own file, readable only by the account the
/// service runs as (mode 0600). Linux has no DPAPI equivalent that works for
/// a headless service, so this is the same trade a keyring-less daemon always
/// makes: the secrets file alone is useless to someone who copies it, but
/// anyone who can read both files as that account can decrypt them. Keep the
/// key file out of backups you share.
/// Blob layout: 12-byte nonce | 16-byte tag | ciphertext.
/// </summary>
public sealed class KeyFileSecretProtector : ISecretProtector
{
    private const int KeySizeBytes = 32;
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    private readonly string _keyFilePath;
    private readonly object _sync = new();
    private byte[]? _key;

    public KeyFileSecretProtector(string keyFilePath)
    {
        _keyFilePath = keyFilePath;
    }

    public byte[] Protect(byte[] plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var blob = new byte[NonceSizeBytes + TagSizeBytes + plaintext.Length];
        var nonce = blob.AsSpan(0, NonceSizeBytes);
        var tag = blob.AsSpan(NonceSizeBytes, TagSizeBytes);
        var ciphertext = blob.AsSpan(NonceSizeBytes + TagSizeBytes);

        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(GetOrCreateKey(), TagSizeBytes);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        return blob;
    }

    public byte[] Unprotect(byte[] ciphertext)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        if (ciphertext.Length < NonceSizeBytes + TagSizeBytes)
        {
            throw new CryptographicException("Secret blob is too short to be valid.");
        }

        var nonce = ciphertext.AsSpan(0, NonceSizeBytes);
        var tag = ciphertext.AsSpan(NonceSizeBytes, TagSizeBytes);
        var encrypted = ciphertext.AsSpan(NonceSizeBytes + TagSizeBytes);

        var plaintext = new byte[encrypted.Length];
        using var aes = new AesGcm(GetOrCreateKey(), TagSizeBytes);
        aes.Decrypt(nonce, encrypted, tag, plaintext);
        return plaintext;
    }

    private byte[] GetOrCreateKey()
    {
        lock (_sync)
        {
            if (_key is not null)
            {
                return _key;
            }

            if (File.Exists(_keyFilePath))
            {
                var existing = File.ReadAllBytes(_keyFilePath);
                if (existing.Length != KeySizeBytes)
                {
                    throw new CryptographicException($"Secret key file '{_keyFilePath}' is corrupt (expected {KeySizeBytes} bytes, found {existing.Length}).");
                }

                _key = existing;
                return _key;
            }

            var directory = Path.GetDirectoryName(_keyFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Services.SecureDirectory.EnsureExists(directory);
            }

            var key = RandomNumberGenerator.GetBytes(KeySizeBytes);

            // CreateNew, so two processes racing to create the key can't
            // silently overwrite each other's (and orphan every secret the
            // loser already encrypted). The mode is set at creation, never
            // widened then narrowed, so the key is never briefly readable.
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using (var stream = new FileStream(_keyFilePath, options))
            {
                stream.Write(key);
            }

            _key = key;
            return _key;
        }
    }
}
