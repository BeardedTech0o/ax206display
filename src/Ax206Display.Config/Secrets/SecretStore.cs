using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ax206Display.Config.Services;

namespace Ax206Display.Config.Secrets;

/// <summary>
/// A flat key/value store of encrypted secrets, persisted as base64 blobs next
/// to the main config file. Values are only ever decrypted in memory on demand.
/// </summary>
public sealed class SecretStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly ISecretProtector _protector;
    private readonly string _filePath;
    private Dictionary<string, string> _encryptedByKey;

    public SecretStore(ISecretProtector protector, string filePath)
    {
        _protector = protector;
        _filePath = filePath;
        _encryptedByKey = new Dictionary<string, string>();
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            _encryptedByKey = new Dictionary<string, string>();
            return;
        }

        await using var stream = File.OpenRead(_filePath);
        _encryptedByKey = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream, cancellationToken: cancellationToken)
            ?? new Dictionary<string, string>();
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            SecureDirectory.EnsureExists(directory);
        }

        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, _encryptedByKey, SerializerOptions, cancellationToken);
    }

    public void SetSecret(string key, string plaintextValue)
    {
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintextValue);
        try
        {
            var encryptedBytes = _protector.Protect(plaintextBytes);
            _encryptedByKey[key] = Convert.ToBase64String(encryptedBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }
    }

    /// <summary>
    /// Decrypts and returns the secret as a <see cref="string"/>. Note that
    /// .NET strings are immutable and can't be reliably wiped from memory once
    /// created - keep the returned value in scope for as short a time as
    /// possible (e.g. hand it straight to an HttpClient call, don't cache it).
    /// The intermediate decrypted byte buffer this method allocates is zeroed
    /// before returning.
    /// </summary>
    public string? GetSecret(string key)
    {
        if (!_encryptedByKey.TryGetValue(key, out var encoded))
        {
            return null;
        }

        var encryptedBytes = Convert.FromBase64String(encoded);
        var plaintextBytes = _protector.Unprotect(encryptedBytes);
        try
        {
            return Encoding.UTF8.GetString(plaintextBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }
    }

    public void RemoveSecret(string key) => _encryptedByKey.Remove(key);
}
