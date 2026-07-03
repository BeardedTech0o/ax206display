namespace Ax206Display.Protocol.Commands;

/// <summary>
/// Property tokens for <see cref="Ax206UserCommand.SetProperty"/>. Only
/// <see cref="Brightness"/> is confirmed to work against stock/commercial
/// firmware by every reference implementation; see docs/protocol-spec.md for
/// the caveats on the others (in particular, <see cref="Orientation"/> is only
/// documented against dpf-ax's own replacement firmware, not stock firmware).
/// </summary>
public enum Ax206Property : ushort
{
    Brightness = 0x01,
    ForegroundColor = 0x02,
    BackgroundColor = 0x03,
    Orientation = 0x10,
}
