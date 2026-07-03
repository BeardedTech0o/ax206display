namespace Ax206Display.Config.Secrets;

/// <summary>
/// Encrypts/decrypts small secret payloads (integration passwords, API tokens)
/// for at-rest storage. Abstracted so config/service logic stays testable on any
/// OS even though the production implementation is Windows DPAPI-only.
/// </summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] plaintext);

    byte[] Unprotect(byte[] ciphertext);
}
