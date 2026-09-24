using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ax206Display.Server.Auth;

/// <summary>
/// The web UI's single admin password, stored as a salted PBKDF2-SHA256 hash.
/// There's deliberately no "set a password on first visit" flow: on a LAN
/// appliance, whoever loads the page first would get to claim it. Instead,
/// the first start generates a random password and writes it to
/// <c>initial-password.txt</c> (owner-only) in the data directory, where
/// only someone with a shell on the box can read it. Changing the password
/// deletes that file.
/// </summary>
public sealed class WebPasswordStore
{
    public const int MinimumLength = 8;

    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;
    private const int Iterations = 210_000;

    private readonly string _hashFilePath;
    private readonly string _initialPasswordFilePath;
    private readonly object _sync = new();
    private StoredHash? _current;

    public WebPasswordStore(string dataDirectory)
    {
        _hashFilePath = Path.Combine(dataDirectory, "web-password.json");
        _initialPasswordFilePath = Path.Combine(dataDirectory, "initial-password.txt");
    }

    public string InitialPasswordFilePath => _initialPasswordFilePath;

    /// <summary>
    /// Changes whenever the password does. Baked into each login cookie so a
    /// password change signs out every other existing session.
    /// </summary>
    public string Stamp => Load()?.Stamp ?? string.Empty;

    /// <summary>Creates a random password if none exists yet. Returns true when it did.</summary>
    public bool EnsureInitialized()
    {
        lock (_sync)
        {
            if (Load() is not null)
            {
                return false;
            }

            var generated = GeneratePassword();
            Save(generated);
            WriteOwnerOnly(_initialPasswordFilePath, generated + Environment.NewLine);
            return true;
        }
    }

    public bool Verify(string? password)
    {
        var stored = Load();
        if (stored is null || string.IsNullOrEmpty(password))
        {
            return false;
        }

        var candidate = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), stored.Salt, stored.Iterations, HashAlgorithmName.SHA256, stored.Hash.Length);
        return CryptographicOperations.FixedTimeEquals(candidate, stored.Hash);
    }

    public void SetPassword(string newPassword)
    {
        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < MinimumLength)
        {
            throw new ArgumentException($"Password must be at least {MinimumLength} characters.", nameof(newPassword));
        }

        lock (_sync)
        {
            Save(newPassword);
            if (File.Exists(_initialPasswordFilePath))
            {
                File.Delete(_initialPasswordFilePath);
            }
        }
    }

    private StoredHash? Load()
    {
        lock (_sync)
        {
            if (_current is not null)
            {
                return _current;
            }

            if (!File.Exists(_hashFilePath))
            {
                return null;
            }

            var dto = JsonSerializer.Deserialize<HashFile>(File.ReadAllText(_hashFilePath))
                ?? throw new InvalidDataException($"'{_hashFilePath}' is empty or corrupt.");
            _current = new StoredHash(Convert.FromBase64String(dto.Salt), Convert.FromBase64String(dto.Hash), dto.Iterations);
            return _current;
        }
    }

    private void Save(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, HashSizeBytes);
        var dto = new HashFile(Convert.ToBase64String(salt), Convert.ToBase64String(hash), Iterations);

        var tempPath = _hashFilePath + ".tmp";
        WriteOwnerOnly(tempPath, JsonSerializer.Serialize(dto));
        File.Move(tempPath, _hashFilePath, overwrite: true);
        _current = new StoredHash(salt, hash, Iterations);
    }

    private static string GeneratePassword()
    {
        // No look-alike characters (0/O, 1/l/I): this gets read off a
        // terminal and typed into a browser by hand.
        const string alphabet = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return RandomNumberGenerator.GetString(alphabet, 16);
    }

    private static void WriteOwnerOnly(string path, string contents)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using var writer = new StreamWriter(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), options);
        writer.Write(contents);
    }

    private sealed record HashFile(string Salt, string Hash, int Iterations);

    private sealed record StoredHash(byte[] Salt, byte[] Hash, int Iterations)
    {
        public string Stamp { get; } = Convert.ToHexString(SHA256.HashData(Hash))[..16];
    }
}
