using System.Globalization;
using System.Security.Cryptography;

namespace Ax206Display.DataSources.Auth;

/// <summary>
/// Generates RFC 6238 TOTP codes (HMAC-SHA1, 30-second step, 6 digits - the
/// defaults every authenticator app and UniFi OS itself use) from a
/// base32-encoded shared secret. Letting a background service compute its own
/// code from the secret - the same secret an authenticator app was seeded
/// with - is what lets it log back into a 2FA-protected account on its own
/// after a session expires, instead of needing a human to type in a fresh
/// 6-digit code every time (which a typed one-off code can't do: it's
/// single-use and expires in 30 seconds).
/// </summary>
public static class TotpGenerator
{
    private const int DigitCount = 6;
    private const int TimeStepSeconds = 30;

    public static string GenerateCode(string base32Secret, DateTimeOffset? at = null)
    {
        var secretBytes = Base32Decode(base32Secret);
        var timeStep = (long)((at ?? DateTimeOffset.UtcNow) - DateTimeOffset.UnixEpoch).TotalSeconds / TimeStepSeconds;

        var counterBytes = BitConverter.GetBytes(timeStep);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counterBytes);
        }

        // CA5350 flags HMAC-SHA1 as weak crypto in general, but RFC 6238
        // mandates HMAC-SHA1 for the default TOTP algorithm - it's what
        // every authenticator app and every service issuing these secrets
        // (UniFi included) actually computes against, not a choice made
        // here. SHA1's known weakness is collision resistance for signing/
        // hashing, which doesn't apply to its use as a keyed MAC in HMAC.
#pragma warning disable CA5350
        var hash = HMACSHA1.HashData(secretBytes, counterBytes);
#pragma warning restore CA5350

        // Dynamic truncation per RFC 4226 section 5.3.
        var offset = hash[^1] & 0x0F;
        var binaryCode =
            ((hash[offset] & 0x7F) << 24) |
            ((hash[offset + 1] & 0xFF) << 16) |
            ((hash[offset + 2] & 0xFF) << 8) |
            (hash[offset + 3] & 0xFF);

        var code = binaryCode % 1_000_000;
        return code.ToString(CultureInfo.InvariantCulture).PadLeft(DigitCount, '0');
    }

    /// <summary>
    /// RFC 4648 base32 decoding - .NET has no built-in decoder for it (only
    /// base64). Accepts secrets with or without "=" padding and stray
    /// whitespace, since that's how most authenticator-app setup screens
    /// display the manual-entry key.
    /// </summary>
    private static byte[] Base32Decode(string base32Secret)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        var cleaned = base32Secret.Replace(" ", string.Empty).TrimEnd('=').ToUpperInvariant();

        var output = new List<byte>(cleaned.Length * 5 / 8);
        var bitBuffer = 0;
        var bitCount = 0;

        foreach (var c in cleaned)
        {
            var value = alphabet.IndexOf(c);
            if (value < 0)
            {
                throw new FormatException($"'{c}' is not a valid base32 character.");
            }

            bitBuffer = (bitBuffer << 5) | value;
            bitCount += 5;

            if (bitCount >= 8)
            {
                bitCount -= 8;
                output.Add((byte)((bitBuffer >> bitCount) & 0xFF));
            }
        }

        return [.. output];
    }
}
