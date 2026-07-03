using Ax206Display.Protocol.Commands;
using Ax206Display.Protocol.Transport;
using LibUsbDotNet;
using LibUsbDotNet.LibUsb;
using LibUsbDotNet.Main;

namespace Ax206Display.Transport.LibUsb;

/// <summary>
/// Drives a real AX206 display over libusb (via LibUsbDotNet), using the
/// Bulk-Only-Transport-shaped CBW/data/CSW exchange documented in
/// docs/protocol-spec.md. The device must already be open with interface 0
/// claimed before construction (see <see cref="Discovery.LibUsbAx206DeviceDiscovery"/>).
/// </summary>
public sealed class LibUsbAx206Transport : IAx206Transport
{
    private readonly IUsbDevice _device;
    private readonly UsbEndpointWriter _writer;
    private readonly UsbEndpointReader _reader;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private readonly int _timeoutMs;

    public LibUsbAx206Transport(IUsbDevice device, string deviceId, TimeSpan? timeout = null)
    {
        _device = device;
        DeviceId = deviceId;
        _timeoutMs = (int)(timeout ?? BulkOnlyTransport.DefaultTransferTimeout).TotalMilliseconds;
        _writer = device.OpenEndpointWriter(WriteEndpointID.Ep01, EndpointType.Bulk);
        _reader = device.OpenEndpointReader(ReadEndpointID.Ep01, 1024 * 1024, EndpointType.Bulk);
    }

    public string DeviceId { get; }

    public async Task<LcdParametersResponse> GetLcdParametersAsync(CancellationToken cancellationToken = default)
    {
        var cbw = Ax206CommandBuilder.GetLcdParameters();
        var response = await ExecuteAsync(cbw, dataOut: null, expectedInLength: (int)LcdParametersResponse.Length, cancellationToken);
        return LcdParametersResponse.Parse(response);
    }

    public async Task SetPropertyAsync(Ax206Property property, ushort value, CancellationToken cancellationToken = default)
    {
        var cbw = Ax206CommandBuilder.SetProperty(property, value);
        await ExecuteAsync(cbw, dataOut: null, expectedInLength: 0, cancellationToken);
    }

    public async Task BlitAsync(ushort left, ushort top, ushort right, ushort bottom, ReadOnlyMemory<byte> rgb565BigEndianPixels, CancellationToken cancellationToken = default)
    {
        var cbw = Ax206CommandBuilder.Blit(left, top, right, bottom);
        await ExecuteAsync(cbw, dataOut: rgb565BigEndianPixels, expectedInLength: 0, cancellationToken);
    }

    private async Task<byte[]> ExecuteAsync(CommandBlockWrapper cbw, ReadOnlyMemory<byte>? dataOut, int expectedInLength, CancellationToken cancellationToken)
    {
        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            var (cbwError, _) = await _writer.WriteAsync(cbw.ToBytes(), _timeoutMs);
            ThrowIfFailed(cbwError, "writing the command block");

            var dataIn = Array.Empty<byte>();
            if (dataOut is { Length: > 0 } outBytes)
            {
                var (dataError, _) = await _writer.WriteAsync(outBytes, _timeoutMs);
                ThrowIfFailed(dataError, "writing the data phase");
            }
            else if (expectedInLength > 0)
            {
                var buffer = new byte[expectedInLength];
                var (readError, readCount) = await _reader.ReadAsync(buffer, _timeoutMs);
                ThrowIfFailed(readError, "reading the data phase");
                dataIn = readCount == buffer.Length ? buffer : buffer[..readCount];
            }

            var cswBuffer = new byte[BulkOnlyTransport.CommandStatusWrapperLength];
            var (cswError, cswRead) = await _reader.ReadAsync(cswBuffer, _timeoutMs);
            ThrowIfFailed(cswError, "reading the command status");

            var csw = CommandStatusWrapper.Parse(cswBuffer.AsSpan(0, cswRead));
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
    }

    private static void ThrowIfFailed(Error error, string phase)
    {
        if (error != Error.Success)
        {
            throw new IOException($"AX206 USB transfer failed while {phase}: {error}.");
        }
    }

    public void Dispose()
    {
        _ioLock.Dispose();
        if (_device.IsOpen)
        {
            _device.Close();
        }
    }
}
