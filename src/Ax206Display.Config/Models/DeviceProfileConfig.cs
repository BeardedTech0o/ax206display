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

    public List<WidgetConfig> Widgets { get; init; } = [];
}
