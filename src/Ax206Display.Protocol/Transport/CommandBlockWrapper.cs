using System.Buffers.Binary;

namespace Ax206Display.Protocol.Transport;

/// <summary>The 31-byte command envelope sent on the bulk OUT endpoint before any data phase.</summary>
public sealed class CommandBlockWrapper
{
    public required uint DataTransferLength { get; init; }

    public required bool IsDataPhaseIn { get; init; }

    public required byte[] CommandDescriptorBlock { get; init; }

    public byte[] ToBytes()
    {
        if (CommandDescriptorBlock.Length > BulkOnlyTransport.CommandDescriptorBlockLength)
        {
            throw new ArgumentException(
                $"CDB must be at most {BulkOnlyTransport.CommandDescriptorBlockLength} bytes, got {CommandDescriptorBlock.Length}.");
        }

        var buffer = new byte[BulkOnlyTransport.CommandBlockWrapperLength];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0, 4), BulkOnlyTransport.CommandBlockSignature);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4, 4), BulkOnlyTransport.FixedTag);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8, 4), DataTransferLength);
        buffer[12] = IsDataPhaseIn ? BulkOnlyTransport.DirectionFlagIn : BulkOnlyTransport.DirectionFlagOut;
        buffer[13] = 0x00; // LUN
        buffer[14] = BulkOnlyTransport.CommandDescriptorBlockLength;

        CommandDescriptorBlock.CopyTo(buffer.AsSpan(15));
        return buffer;
    }
}
