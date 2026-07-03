using System.Buffers.Binary;

namespace Ax206Display.Protocol.Transport;

/// <summary>The 13-byte status reply read from the bulk IN endpoint after a CBW (and any data phase).</summary>
public sealed record CommandStatusWrapper(bool SignatureValid, byte Status)
{
    public bool Succeeded => SignatureValid && Status == 0;

    public static CommandStatusWrapper Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < BulkOnlyTransport.CommandStatusWrapperLength)
        {
            throw new ArgumentException(
                $"CSW must be at least {BulkOnlyTransport.CommandStatusWrapperLength} bytes, got {bytes.Length}.");
        }

        var signature = BinaryPrimitives.ReadUInt32LittleEndian(bytes[..4]);
        var signatureValid = signature == BulkOnlyTransport.CommandStatusSignature;
        var status = bytes[12];

        return new CommandStatusWrapper(signatureValid, status);
    }
}
