using Ax206Display.Config.Models;
using Ax206Display.Config.Services;

namespace Ax206Display.Tests.Config;

public class ConfigServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _configPath;

    public ConfigServiceTests()
    {
        _tempDirectory = Directory.CreateTempSubdirectory("ax206display-tests-").FullName;
        _configPath = Path.Combine(_tempDirectory, "nested", "config.json");
    }

    [Fact]
    public async Task LoadAsync_WhenFileMissing_ReturnsDefaultConfig()
    {
        var service = new ConfigService(_configPath);

        var config = await service.LoadAsync();

        Assert.Equal(AppConfig.CurrentSchemaVersion, config.SchemaVersion);
        Assert.Empty(config.Devices);
        Assert.Empty(config.Integrations);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsDeviceAndWidgetData()
    {
        var service = new ConfigService(_configPath);
        var original = new AppConfig
        {
            StartWithWindows = true,
            Devices =
            [
                new DeviceProfileConfig
                {
                    Id = "dev-1",
                    Name = "Left monitor",
                    Identity = new DeviceIdentity { VendorId = 0x1908, ProductId = 0x0102, SerialNumber = "ABC123" },
                    ScreenWidth = 480,
                    ScreenHeight = 320,
                    Widgets = [new WidgetConfig { Id = "w1", Type = "clock", X = 0, Y = 0, Width = 480, Height = 60 }],
                },
            ],
        };

        await service.SaveAsync(original);
        var loaded = await service.LoadAsync();

        Assert.True(loaded.StartWithWindows);
        var device = Assert.Single(loaded.Devices);
        Assert.Equal("dev-1", device.Id);
        Assert.Equal(0x1908, device.Identity.VendorId);
        var widget = Assert.Single(device.Widgets);
        Assert.Equal("clock", widget.Type);
    }

    [Fact]
    public async Task SaveAsync_CreatesMissingDirectories()
    {
        var service = new ConfigService(_configPath);

        await service.SaveAsync(new AppConfig());

        Assert.True(File.Exists(_configPath));
    }

    public void Dispose()
    {
        Directory.Delete(_tempDirectory, recursive: true);
    }
}
