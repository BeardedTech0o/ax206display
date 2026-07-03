namespace Ax206Display.Config.Models;

/// <summary>
/// Identifies one physical USB display, captured from auto-detection at pairing
/// time. This is persisted per-device so a saved layout re-attaches to the same
/// physical unit across reconnects/reboots - it is never used to seed a hardcoded
/// device filter at startup (see Ax206Display.Transport device discovery).
/// </summary>
public sealed record DeviceIdentity
{
    public required int VendorId { get; init; }

    public required int ProductId { get; init; }

    /// <summary>USB iSerialNumber string descriptor, when the device exposes one.</summary>
    public string? SerialNumber { get; init; }

    /// <summary>Physical USB bus/port location path, used as a fallback pairing key
    /// for devices that don't expose a serial number.</summary>
    public string? UsbLocationPath { get; init; }
}
