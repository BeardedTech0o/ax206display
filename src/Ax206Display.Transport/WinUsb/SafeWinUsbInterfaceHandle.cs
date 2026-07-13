using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Ax206Display.Transport.WinUsb;

[SupportedOSPlatform("windows")]
internal sealed class SafeWinUsbInterfaceHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeWinUsbInterfaceHandle() : base(ownsHandle: true)
    {
    }

    protected override bool ReleaseHandle() => NativeMethods.WinUsb_Free(handle);
}
