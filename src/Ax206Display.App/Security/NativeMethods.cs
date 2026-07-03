using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Ax206Display.App.Security;

/// <summary>P/Invoke declarations for wintrust.dll's Authenticode verification API.</summary>
[SupportedOSPlatform("windows")]
internal static class NativeMethods
{
    public static readonly Guid WintrustActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    public const uint WtdUiNone = 2;
    public const uint WtdRevokeWholeChain = 1;
    public const uint WtdChoiceFile = 1;
    public const uint WtdStateActionVerify = 1;
    public const uint WtdStateActionClose = 2;
    public const uint WtdRevocationCheckChainExcludeRoot = 0x00000008;

    public const int ErrorSuccess = 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    public static extern int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WINTRUST_DATA pWVTData);
}
