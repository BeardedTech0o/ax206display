using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Ax206Display.Config.Secrets;

/// <summary>
/// Protects secrets at rest using Windows DPAPI, scoped to the current user
/// account. Ax206Display always runs as a single interactive user's tray app,
/// so user scope (rather than machine scope) is the correct choice.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretProtector : ISecretProtector
{
    private static readonly byte[] Entropy = "Ax206Display.Secrets.v1"u8.ToArray();

    public byte[] Protect(byte[] plaintext)
    {
        RequireWindows();
        return ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
    }

    public byte[] Unprotect(byte[] ciphertext)
    {
        RequireWindows();
        return ProtectedData.Unprotect(ciphertext, Entropy, DataProtectionScope.CurrentUser);
    }

    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                $"{nameof(DpapiSecretProtector)} requires Windows DPAPI and cannot run on this OS.");
        }
    }
}
