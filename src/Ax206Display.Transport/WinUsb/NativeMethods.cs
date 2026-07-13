using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Ax206Display.Transport.WinUsb;

/// <summary>
/// P/Invoke declarations for the inbox WinUSB driver/API. This is the fallback
/// transport used when the LibUsbDotNet/libusb backend isn't available or
/// hasn't been paired with a device (WinUSB itself ships with Windows since
/// Vista, so this path needs no extra driver files once a device is bound to
/// winusb.sys, e.g. via Zadig or a custom device-setup INF).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class NativeMethods
{
    private const string Kernel32 = "kernel32.dll";
    private const string WinUsbDll = "winusb.dll";

    public const uint GenericRead = 0x80000000;
    public const uint GenericWrite = 0x40000000;
    public const uint FileShareRead = 0x1;
    public const uint FileShareWrite = 0x2;
    public const uint OpenExisting = 3;
    public const uint FileAttributeNormal = 0x80;
    public const uint FileFlagOverlapped = 0x40000000;

    public const int PipeTransferTimeout = 3;

    [DllImport(Kernel32, SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport(WinUsbDll, SetLastError = true)]
    public static extern bool WinUsb_Initialize(SafeFileHandle deviceHandle, out SafeWinUsbInterfaceHandle interfaceHandle);

    [DllImport(WinUsbDll, SetLastError = true)]
    public static extern bool WinUsb_Free(IntPtr interfaceHandle);

    [DllImport(WinUsbDll, SetLastError = true)]
    public static extern bool WinUsb_ReadPipe(
        SafeWinUsbInterfaceHandle interfaceHandle,
        byte pipeId,
        [Out] byte[] buffer,
        uint bufferLength,
        out uint lengthTransferred,
        IntPtr overlapped);

    [DllImport(WinUsbDll, SetLastError = true)]
    public static extern bool WinUsb_WritePipe(
        SafeWinUsbInterfaceHandle interfaceHandle,
        byte pipeId,
        byte[] buffer,
        uint bufferLength,
        out uint lengthTransferred,
        IntPtr overlapped);

    [DllImport(WinUsbDll, SetLastError = true)]
    public static extern bool WinUsb_SetPipePolicy(
        SafeWinUsbInterfaceHandle interfaceHandle,
        byte pipeId,
        int policyType,
        uint valueLength,
        ref uint value);
}
