namespace Ax206Display.Config.Models;

public sealed record DeviceProfileConfig
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required DeviceIdentity Identity { get; init; }

    public required int ScreenWidth { get; init; }

    public required int ScreenHeight { get; init; }

    public DeviceOrientation Orientation { get; init; } = DeviceOrientation.Landscape;

    public int TargetFps { get; init; } = 1;

    /// <summary>Absolute path to an image file drawn full-canvas beneath all widgets, or null for the solid background color.</summary>
    public string? BackgroundImagePath { get; init; }

    /// <summary>Hardware backlight level sent via SetProperty(Brightness, ...). Range is 0 (min/off) to 7 (max) - see docs/protocol-spec.md.</summary>
    public int Brightness { get; init; } = 7;

    public List<WidgetConfig> Widgets { get; init; } = [];
}
