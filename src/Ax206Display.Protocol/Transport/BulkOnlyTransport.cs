namespace Ax206Display.Protocol.Transport;

/// <summary>
/// Constants for the Bulk-Only-Transport-shaped framing the AX206 vendor
/// protocol reuses (Command Block Wrapper / Command Status Wrapper), as
/// reverse-derived from dreamlayers/dpf-ax, ukoda/lcd4linux-ax206,
/// wjohnsaunders/Client-DPF-AX206 and plumbum/go2dpf. See docs/protocol-spec.md
/// for the full write-up and citations.
/// </summary>
public static class BulkOnlyTransport
{
    /// <summary>"USBC" - dCBWSignature.</summary>
    public const uint CommandBlockSignature = 0x43425355;

    /// <summary>"USBS" - dCSWSignature.</summary>
    public const uint CommandStatusSignature = 0x53425355;

    /// <summary>
    /// Fixed CBW tag used by every known host implementation. Real Bulk-Only
    /// Transport normally increments this per request, but no reference
    /// implementation does, and the AX206 firmware does not appear to require it.
    /// </summary>
    public const uint FixedTag = 0xEFBEADDE;

    public const int CommandBlockWrapperLength = 31;

    public const int CommandStatusWrapperLength = 13;

    public const int CommandDescriptorBlockLength = 16;

    public const byte BulkOutEndpoint = 0x01;

    public const byte BulkInEndpoint = 0x81;

    public const byte DirectionFlagIn = 0x80;

    public const byte DirectionFlagOut = 0x00;

    /// <summary>
    /// Conservative bulk transfer timeout. Reference implementations range from
    /// 1s to 5s; a full 800x480 RGB565 frame is ~768 KB so a short timeout risks
    /// spurious failures on slow hosts/hubs.
    /// </summary>
    public static readonly TimeSpan DefaultTransferTimeout = TimeSpan.FromSeconds(5);
}
