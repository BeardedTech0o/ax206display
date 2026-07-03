using System.Buffers.Binary;

namespace Ax206Display.Protocol.Commands;

/// <summary>
/// The 5-byte reply to <see cref="Ax206CommandBuilder.GetLcdParameters"/>.
/// <see cref="IsMarkerValid"/> reflects a validity check one reference client
/// (Client-DPF-AX206) performs and the others don't - treat it as a useful but
/// not universally-required sanity check.
/// </summary>
public sealed record LcdParametersResponse(ushort Width, ushort Height, bool IsMarkerValid)
{
    public const uint Length = 5;

    private const byte ExpectedMarker = 0xFF;

    public static LcdParametersResponse Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < Length)
        {
            throw new ArgumentException($"Expected at least {Length} bytes, got {bytes.Length}.", nameof(bytes));
        }

        var width = BinaryPrimitives.ReadUInt16LittleEndian(bytes[..2]);
        var height = BinaryPrimitives.ReadUInt16LittleEndian(bytes[2..4]);
        var markerValid = bytes[4] == ExpectedMarker;

        return new LcdParametersResponse(width, height, markerValid);
    }

    /// <summary>
    /// A loose sanity bound for auto-detection probing: rejects clearly
    /// nonsensical responses from a device that isn't actually an AX206
    /// display, without hardcoding a specific supported resolution.
    /// </summary>
    public bool HasPlausibleDimensions => Width is > 0 and <= 4096 && Height is > 0 and <= 4096;
}
