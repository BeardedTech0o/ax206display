using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Ax206Display.Protocol.Commands;
using Ax206Display.Protocol.Transport;
using Microsoft.Win32.SafeHandles;

namespace Ax206Display.Transport.WinUsb;

/// <summary>
/// Drives a real AX206 display over the inbox WinUSB driver via raw P/Invoke,
/// as a fallback for when the libusb-based <see cref="LibUsb.LibUsbAx206Transport"/>
/// backend isn't usable on a given machine. Opened synchronously (no
/// FILE_FLAG_OVERLAPPED), so pipe I/O blocks the calling thread; interface
/// methods offload that to the thread pool via <see cref="Task.Run(Action)"/>.
/// </summary>
/// <param name="devicePath">
/// The device interface path for a device already bound to winusb.sys (as
/// returned by SetupAPI device interface enumeration for the driver's device
/// interface GUID). Discovering that path is deployment/driver-package
/// specific and out of scope for this transport - see docs/protocol-spec.md.
/// </param>
[SupportedOSPlatform("windows")]
public sealed class WinUsbAx206Transport : IAx206Transport
{
    private readonly SafeFileHandle _fileHandle;
    private readonly SafeWinUsbInterfaceHandle _interfaceHandle;
    private readonly SemaphoreSlim _ioLock = new(1, 1);

    public WinUsbAx206Transport(string devicePath, string deviceId)
    {
        DeviceId = deviceId;

        _fileHandle = NativeMethods.CreateFile(
            devicePath,
            NativeMethods.GenericRead | NativeMethods.GenericWrite,
            NativeMethods.FileShareRead | NativeMethods.FileShareWrite,
            IntPtr.Zero,
            NativeMethods.OpenExisting,
            NativeMethods.FileAttributeNormal,
            IntPtr.Zero);

        if (_fileHandle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Failed to open WinUSB device '{devicePath}'.");
        }

        if (!NativeMethods.WinUsb_Initialize(_fileHandle, out _interfaceHandle))
        {
            var error = Marshal.GetLastWin32Error();
            _fileHandle.Dispose();
            throw new Win32Exception(error, $"WinUsb_Initialize failed for device '{devicePath}'.");
        }

        var timeoutMs = (uint)BulkOnlyTransport.DefaultTransferTimeout.TotalMilliseconds;
        NativeMethods.WinUsb_SetPipePolicy(_interfaceHandle, BulkOnlyTransport.BulkOutEndpoint, NativeMethods.PipeTransferTimeout, sizeof(uint), ref timeoutMs);
        NativeMethods.WinUsb_SetPipePolicy(_interfaceHandle, BulkOnlyTransport.BulkInEndpoint, NativeMethods.PipeTransferTimeout, sizeof(uint), ref timeoutMs);
    }

    public string DeviceId { get; }

    public async Task<LcdParametersResponse> GetLcdParametersAsync(CancellationToken cancellationToken = default)
    {
        var cbw = Ax206CommandBuilder.GetLcdParameters();
        var response = await ExecuteAsync(cbw, dataOut: null, expectedInLength: (int)LcdParametersResponse.Length);
        return LcdParametersResponse.Parse(response);
    }

    public Task SetPropertyAsync(Ax206Property propertyToken, ushort value, CancellationToken cancellationToken = default)
    {
        var cbw = Ax206CommandBuilder.SetProperty(propertyToken, value);
        return ExecuteAsync(cbw, dataOut: null, expectedInLength: 0);
    }

    public Task BlitAsync(ushort left, ushort top, ushort right, ushort bottom, ReadOnlyMemory<byte> rgb565BigEndianPixels, CancellationToken cancellationToken = default)
    {
        var cbw = Ax206CommandBuilder.Blit(left, top, right, bottom);
        return ExecuteAsync(cbw, dataOut: rgb565BigEndianPixels.ToArray(), expectedInLength: 0);
    }

    private Task<byte[]> ExecuteAsync(CommandBlockWrapper cbw, byte[]? dataOut, int expectedInLength)
    {
        return Task.Run(() =>
        {
            _ioLock.Wait();
            try
            {
                WritePipe(BulkOnlyTransport.BulkOutEndpoint, cbw.ToBytes());

                var dataIn = Array.Empty<byte>();
                if (dataOut is { Length: > 0 })
                {
                    WritePipe(BulkOnlyTransport.BulkOutEndpoint, dataOut);
                }
                else if (expectedInLength > 0)
                {
                    dataIn = ReadPipe(BulkOnlyTransport.BulkInEndpoint, expectedInLength);
                }

                var cswBytes = ReadPipe(BulkOnlyTransport.BulkInEndpoint, BulkOnlyTransport.CommandStatusWrapperLength);
                var csw = CommandStatusWrapper.Parse(cswBytes);
                if (!csw.Succeeded)
                {
                    throw new IOException($"AX206 device reported a command failure (signatureValid={csw.SignatureValid}, status=0x{csw.Status:X2}).");
                }

                return dataIn;
            }
            finally
            {
                _ioLock.Release();
            }
        });
    }

    private void WritePipe(byte pipeId, byte[] buffer)
    {
        if (!NativeMethods.WinUsb_WritePipe(_interfaceHandle, pipeId, buffer, (uint)buffer.Length, out _, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"WinUsb_WritePipe failed on pipe 0x{pipeId:X2}.");
        }
    }

    private byte[] ReadPipe(byte pipeId, int expectedLength)
    {
        var buffer = new byte[expectedLength];
        if (!NativeMethods.WinUsb_ReadPipe(_interfaceHandle, pipeId, buffer, (uint)buffer.Length, out var transferred, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"WinUsb_ReadPipe failed on pipe 0x{pipeId:X2}.");
        }

        return transferred == buffer.Length ? buffer : buffer[..(int)transferred];
    }

    public void Dispose()
    {
        _ioLock.Dispose();
        _interfaceHandle.Dispose();
        _fileHandle.Dispose();
    }
}
