namespace Ax206Display.Config.Models;

public sealed record AppConfig
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public bool StartWithWindows { get; init; }

    public List<DeviceProfileConfig> Devices { get; init; } = [];

    public List<IntegrationConfig> Integrations { get; init; } = [];
}
