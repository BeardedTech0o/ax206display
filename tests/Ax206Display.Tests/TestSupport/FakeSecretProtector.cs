using Ax206Display.Config.Secrets;

namespace Ax206Display.Tests.TestSupport;

/// <summary>A reversible, non-cryptographic stand-in for DPAPI so secret-store logic can be tested off Windows.</summary>
public sealed class FakeSecretProtector : ISecretProtector
{
    public byte[] Protect(byte[] plaintext) => plaintext.Reverse().ToArray();

    public byte[] Unprotect(byte[] ciphertext) => ciphertext.Reverse().ToArray();
}
