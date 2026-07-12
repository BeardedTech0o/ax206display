using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static Ax206Display.App.Security.NativeMethods;

namespace Ax206Display.App.Security;

/// <summary>
/// Verifies the running executable's Authenticode signature via wintrust.dll's
/// WinVerifyTrust - the same API Windows itself uses to validate a signed
/// binary's full trust chain (not just "does it have an embedded
/// certificate", which extracting an <see cref="System.Security.Cryptography.X509Certificates.X509Certificate"/>
/// would only tell you).
/// </summary>
/// <remarks>
/// <see cref="EnforcementEnabled"/> is currently false: no release pipeline
/// signs the published binary yet (see the "Sign" step template in
/// .github/workflows/ci.yml). The mechanism is built and unit-testable now so
/// it's ready to flip on the moment a real code-signing certificate exists -
/// until then, forcing every unsigned dev/CI build to refuse to start would
/// just make the app permanently unusable.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class AuthenticodeVerifier
{
    // static readonly rather than const: keeps this a runtime value instead
    // of a compile-time constant, so the compiler doesn't treat the enforced
    // branch below as statically unreachable while this is false.
    public static readonly bool EnforcementEnabled = false;

    public static bool IsCurrentAssemblyTrusted()
    {
        var path = Environment.ProcessPath;
        return path is not null && IsFileTrusted(path);
    }

    public static bool IsFileTrusted(string filePath)
    {
        var fileInfo = new WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
            pcwszFilePath = filePath,
            hFile = IntPtr.Zero,
            pgKnownSubject = IntPtr.Zero,
        };

        var fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, fDeleteOld: false);

            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                dwUIChoice = WtdUiNone,
                fdwRevocationChecks = WtdRevokeWholeChain,
                dwUnionChoice = WtdChoiceFile,
                pFile = fileInfoPtr,
                dwStateAction = WtdStateActionVerify,
                dwProvFlags = WtdRevocationCheckChainExcludeRoot,
            };

            var actionId = WintrustActionGenericVerifyV2;
            var result = WinVerifyTrust(IntPtr.Zero, ref actionId, ref data);

            // Release WinVerifyTrust's internal state for this call - required
            // by the documented WinVerifyTrust usage pattern regardless of the
            // verify result above.
            data.dwStateAction = WtdStateActionClose;
            _ = WinVerifyTrust(IntPtr.Zero, ref actionId, ref data);

            return result == ErrorSuccess;
        }
        finally
        {
            Marshal.FreeHGlobal(fileInfoPtr);
        }
    }
}
