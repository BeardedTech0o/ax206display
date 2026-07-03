namespace Ax206Display.Protocol.Commands;

/// <summary>
/// Opcodes dispatched via CDB byte 5 = 0x06 ("user command"), CDB byte 6 =
/// this value. Declared per dpf-ax/include/usbuser.h; several are documented
/// but never exercised by any of the four reference host implementations -
/// see docs/protocol-spec.md before relying on those.
/// </summary>
public enum Ax206UserCommand : byte
{
    GetProperty = 0x00,
    SetProperty = 0x01,
    MemRead = 0x04,
    AppLoad = 0x05,
    FillRect = 0x11,
    Blit = 0x12,
    CopyRect = 0x13,
    FlashLock = 0x20,
    Probe = 0xFF,
}
