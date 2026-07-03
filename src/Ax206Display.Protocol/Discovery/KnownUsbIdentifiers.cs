namespace Ax206Display.Protocol.Discovery;

public sealed record KnownUsbIdentifier(int VendorId, int ProductId, string Description);

/// <summary>
/// USB VID/PID pairs documented by the reference implementations, for display
/// in compatibility docs/logging only. Device discovery (Ax206Display.Transport)
/// must NOT filter on this list - it enumerates all USB devices and confirms a
/// real AX206 display by sending the GetLcdParameters probe and checking the
/// response looks sane, so unlisted/rebadged clones still work.
/// </summary>
public static class KnownUsbIdentifiers
{
    public static readonly IReadOnlyList<KnownUsbIdentifier> All =
    [
        new KnownUsbIdentifier(0x1908, 0x0102, "AX206-based photo frame / USB LCD monitor (normal runtime mode)"),
        new KnownUsbIdentifier(0x1908, 0x3318, "AX206 mask-ROM bootloader mode (firmware recovery only, not a display)"),
    ];
}
