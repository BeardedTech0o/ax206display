namespace Ax206Display.Protocol.Commands;

/// <summary>CDB byte 5 values. 0x02 is a leaf command; 0x06 dispatches to an <see cref="Ax206UserCommand"/> at byte 6.</summary>
public static class Ax206CdbSelector
{
    public const byte GetLcdParameters = 0x02;

    public const byte UserCommand = 0x06;

    /// <summary>Vendor opcode byte at CDB[0] for every display-runtime command.</summary>
    public const byte VendorOpcode = 0xCD;
}
