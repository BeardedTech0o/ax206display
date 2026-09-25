using System.Security.Cryptography;

namespace Ax206Display.Config.Secrets;

/// <summary>
/// Hashes/verifies the web UI's login password (see
/// <see cref="Models.WebAuthConfig"/>). PBKDF2-HMAC-SHA256 via the BCL's own
/// Rfc2898DeriveBytes - deliberately not routed through
/// <see cref="ISecretProtector"/>/<see cref="SecretStore"/>: those exist to
/// make a stored secret *decryptable* again (for a saved integration
/// password that gets sent back out to a real API), whereas a login
/// password only ever needs to be verified, never recovered - a salted hash
/// is the correct primitive for that, not reversible encryption.
/// </summary>
public static class WebPasswordHasher
{
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;
    private const int Iterations = 210_000; // OWASP's current PBKDF2-HMAC-SHA256 minimum recommendation.

    /// <summary>Format: "{iterations}.{base64 salt}.{base64 hash}" - self-describing so <see cref="Iterations"/> can be raised later without breaking already-hashed passwords.</summary>
    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSizeBytes);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string encodedHash)
    {
        var parts = encodedHash.Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations))
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[1]);
            var expectedHash = Convert.FromBase64String(parts[2]);
            var actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expectedHash.Length);
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
