using System.Buffers.Binary;
using Ax206Display.Protocol.Transport;

namespace Ax206Display.Protocol.Commands;

/// <summary>
/// Builds the <see cref="CommandBlockWrapper"/>s for every AX206 display
/// operation this app uses. See docs/protocol-spec.md for the byte-level
/// derivation and reference-implementation citations.
/// </summary>
public static class Ax206CommandBuilder
{
    private const int CdbLength = Transport.BulkOnlyTransport.CommandDescriptorBlockLength;

    /// <summary>Queries the device's screen width/height. 5-byte data-in response; see <see cref="LcdParametersResponse"/>.</summary>
    public static CommandBlockWrapper GetLcdParameters()
    {
        var cdb = new byte[CdbLength];
        cdb[0] = Ax206CdbSelector.VendorOpcode;
        cdb[5] = Ax206CdbSelector.GetLcdParameters;

        return new CommandBlockWrapper
        {
            DataTransferLength = LcdParametersResponse.Length,
            IsDataPhaseIn = true,
            CommandDescriptorBlock = cdb,
        };
    }

    /// <summary>Sets a device property (e.g. backlight brightness). No data phase.</summary>
    public static CommandBlockWrapper SetProperty(Ax206Property property, ushort value)
    {
        var cdb = new byte[CdbLength];
        cdb[0] = Ax206CdbSelector.VendorOpcode;
        cdb[5] = Ax206CdbSelector.UserCommand;
        cdb[6] = (byte)Ax206UserCommand.SetProperty;
        BinaryPrimitives.WriteUInt16LittleEndian(cdb.AsSpan(7, 2), (ushort)property);
        BinaryPrimitives.WriteUInt16LittleEndian(cdb.AsSpan(9, 2), value);

        return new CommandBlockWrapper
        {
            DataTransferLength = 0,
            IsDataPhaseIn = false,
            CommandDescriptorBlock = cdb,
        };
    }

    /// <summary>
    /// Uploads a rectangle of big-endian RGB565 pixel data to the screen.
    /// <paramref name="right"/>/<paramref name="bottom"/> are exclusive (as in
    /// a normal .NET rectangle); the wire format encodes them inclusive, so
    /// this method does the -1 adjustment internally.
    /// </summary>
    public static CommandBlockWrapper Blit(ushort left, ushort top, ushort right, ushort bottom)
    {
        if (right <= left || bottom <= top)
        {
            throw new ArgumentException("Blit rectangle must have positive width and height.");
        }

        var cdb = new byte[CdbLength];
        cdb[0] = Ax206CdbSelector.VendorOpcode;
        cdb[5] = Ax206CdbSelector.UserCommand;
        cdb[6] = (byte)Ax206UserCommand.Blit;
        BinaryPrimitives.WriteUInt16LittleEndian(cdb.AsSpan(7, 2), left);
        BinaryPrimitives.WriteUInt16LittleEndian(cdb.AsSpan(9, 2), top);
        BinaryPrimitives.WriteUInt16LittleEndian(cdb.AsSpan(11, 2), (ushort)(right - 1));
        BinaryPrimitives.WriteUInt16LittleEndian(cdb.AsSpan(13, 2), (ushort)(bottom - 1));

        var pixelCount = (right - left) * (bottom - top);
        var byteLength = (uint)(pixelCount * 2);

        return new CommandBlockWrapper
        {
            DataTransferLength = byteLength,
            IsDataPhaseIn = false,
            CommandDescriptorBlock = cdb,
        };
    }
}
