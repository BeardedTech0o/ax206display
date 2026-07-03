using System.Text;
using System.Text.Json;

namespace Ax206Display.Config.Secrets;

/// <summary>
/// A flat key/value store of encrypted secrets, persisted as base64 blobs next
/// to the main config file. Values are only ever decrypted in memory on demand.
/// </summary>
public sealed class SecretStore
{
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
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, _encryptedByKey, new JsonSerializerOptions { WriteIndented = true }, cancellationToken);
    }

    public void SetSecret(string key, string plaintextValue)
    {
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintextValue);
        var encryptedBytes = _protector.Protect(plaintextBytes);
        _encryptedByKey[key] = Convert.ToBase64String(encryptedBytes);
    }

    public string? GetSecret(string key)
    {
        if (!_encryptedByKey.TryGetValue(key, out var encoded))
        {
            return null;
        }

        var encryptedBytes = Convert.FromBase64String(encoded);
        var plaintextBytes = _protector.Unprotect(encryptedBytes);
        return Encoding.UTF8.GetString(plaintextBytes);
    }

    public void RemoveSecret(string key) => _encryptedByKey.Remove(key);
}
