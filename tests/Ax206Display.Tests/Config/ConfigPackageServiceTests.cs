using Ax206Display.Config.Models;
using Ax206Display.Config.Services;

namespace Ax206Display.Tests.Config;

public class ConfigPackageServiceTests : IDisposable
{
    private readonly string _tempDirectory;

    public ConfigPackageServiceTests()
    {
        _tempDirectory = Directory.CreateTempSubdirectory("ax206display-tests-").FullName;
    }

    [Fact]
    public async Task ExportThenImport_RoundTripsConfigAndBackgroundImage()
    {
        var backgroundPath = Path.Combine(_tempDirectory, "wallpaper.png");
        var backgroundBytes = new byte[] { 1, 2, 3, 4, 5 };
        await File.WriteAllBytesAsync(backgroundPath, backgroundBytes);

        var config = new AppConfig
        {
            Devices =
            [
                new DeviceProfileConfig
                {
                    Id = "dev-1",
                    Name = "Left monitor",
                    Identity = new DeviceIdentity { VendorId = 0x1908, ProductId = 0x0102, SerialNumber = "ABC123" },
                    ScreenWidth = 480,
                    ScreenHeight = 320,
                    BackgroundImagePath = backgroundPath,
                    Widgets = [new WidgetConfig { Id = "w1", Type = "clock", X = 0, Y = 0, Width = 480, Height = 60 }],
                },
            ],
            Integrations =
            [
                new IntegrationConfig { Id = "int-1", Kind = "unifi", BaseUrl = "https://unifi.local", Username = "admin", SecretKey = "unifi-password" },
            ],
        };

        var zipPath = Path.Combine(_tempDirectory, "export.zip");
        await ConfigPackageService.ExportAsync(config, zipPath);

        var importedBackgroundsDirectory = Path.Combine(_tempDirectory, "imported-backgrounds");
        var imported = await ConfigPackageService.ImportAsync(zipPath, importedBackgroundsDirectory);

        var device = Assert.Single(imported.Devices);
        Assert.Equal("dev-1", device.Id);
        Assert.NotNull(device.BackgroundImagePath);
        Assert.NotEqual(backgroundPath, device.BackgroundImagePath);
        Assert.Equal(backgroundBytes, await File.ReadAllBytesAsync(device.BackgroundImagePath!));

        var integration = Assert.Single(imported.Integrations);
        Assert.Equal("unifi", integration.Kind);
        Assert.Equal("admin", integration.Username);
    }

    [Fact]
    public async Task Export_SkipsMissingBackgroundImageInsteadOfThrowing()
    {
        var config = new AppConfig
        {
            Devices =
            [
                new DeviceProfileConfig
                {
                    Id = "dev-1",
                    Name = "Left monitor",
                    Identity = new DeviceIdentity { VendorId = 0x1908, ProductId = 0x0102, SerialNumber = "ABC123" },
                    ScreenWidth = 480,
                    ScreenHeight = 320,
                    BackgroundImagePath = Path.Combine(_tempDirectory, "does-not-exist.png"),
                },
            ],
        };

        var zipPath = Path.Combine(_tempDirectory, "export.zip");
        await ConfigPackageService.ExportAsync(config, zipPath);

        var imported = await ConfigPackageService.ImportAsync(zipPath, Path.Combine(_tempDirectory, "imported-backgrounds"));

        Assert.Null(Assert.Single(imported.Devices).BackgroundImagePath);
    }

    [Fact]
    public async Task Export_TwoDevicesSharingSameFileName_ProducesDistinctEntries()
    {
        var directoryA = Directory.CreateDirectory(Path.Combine(_tempDirectory, "a"));
        var directoryB = Directory.CreateDirectory(Path.Combine(_tempDirectory, "b"));
        await File.WriteAllBytesAsync(Path.Combine(directoryA.FullName, "bg.png"), [1, 1, 1]);
        await File.WriteAllBytesAsync(Path.Combine(directoryB.FullName, "bg.png"), [2, 2, 2]);

        var config = new AppConfig
        {
            Devices =
            [
                new DeviceProfileConfig
                {
                    Id = "dev-a",
                    Name = "A",
                    Identity = new DeviceIdentity { VendorId = 1, ProductId = 1, SerialNumber = "A" },
                    ScreenWidth = 1,
                    ScreenHeight = 1,
                    BackgroundImagePath = Path.Combine(directoryA.FullName, "bg.png"),
                },
                new DeviceProfileConfig
                {
                    Id = "dev-b",
                    Name = "B",
                    Identity = new DeviceIdentity { VendorId = 1, ProductId = 1, SerialNumber = "B" },
                    ScreenWidth = 1,
                    ScreenHeight = 1,
                    BackgroundImagePath = Path.Combine(directoryB.FullName, "bg.png"),
                },
            ],
        };

        var zipPath = Path.Combine(_tempDirectory, "export.zip");
        await ConfigPackageService.ExportAsync(config, zipPath);

        var imported = await ConfigPackageService.ImportAsync(zipPath, Path.Combine(_tempDirectory, "imported-backgrounds"));

        var deviceA = imported.Devices.Single(d => d.Id == "dev-a");
        var deviceB = imported.Devices.Single(d => d.Id == "dev-b");
        Assert.Equal(new byte[] { 1, 1, 1 }, await File.ReadAllBytesAsync(deviceA.BackgroundImagePath!));
        Assert.Equal(new byte[] { 2, 2, 2 }, await File.ReadAllBytesAsync(deviceB.BackgroundImagePath!));
    }

    [Fact]
    public async Task RepeatedExportImportRoundTrips_KeepBackgroundImagePathStable()
    {
        var backgroundPath = Path.Combine(_tempDirectory, "wallpaper.png");
        await File.WriteAllBytesAsync(backgroundPath, [9, 9, 9]);

        var config = new AppConfig
        {
            Devices =
            [
                new DeviceProfileConfig
                {
                    Id = "dev-1",
                    Name = "Left monitor",
                    Identity = new DeviceIdentity { VendorId = 1, ProductId = 1, SerialNumber = "A" },
                    ScreenWidth = 480,
                    ScreenHeight = 320,
                    BackgroundImagePath = backgroundPath,
                },
            ],
        };

        var backgroundsDirectory = Path.Combine(_tempDirectory, "imported-backgrounds");
        string? previousPath = null;

        // Simulates exporting from one machine, importing into another,
        // then exporting *that* machine's config again (e.g. re-exporting
        // to move to a third machine) - each round trip's export must start
        // from the previous round's already-imported file.
        for (var round = 0; round < 3; round++)
        {
            var zipPath = Path.Combine(_tempDirectory, $"round-{round}.zip");
            await ConfigPackageService.ExportAsync(config, zipPath);
            var imported = await ConfigPackageService.ImportAsync(zipPath, backgroundsDirectory);
            config = imported;

            var currentPath = Assert.Single(config.Devices).BackgroundImagePath;
            Assert.NotNull(currentPath);
            if (previousPath is not null)
            {
                Assert.Equal(previousPath, currentPath);
            }

            previousPath = currentPath;
        }
    }

    [Fact]
    public void MergeImported_ReplacesMatchingIdsAndKeepsAndAddsTheRest()
    {
        var existing = new AppConfig
        {
            Devices =
            [
                new DeviceProfileConfig { Id = "dev-1", Name = "Old name", Identity = new DeviceIdentity { VendorId = 1, ProductId = 1, SerialNumber = "A" }, ScreenWidth = 1, ScreenHeight = 1 },
                new DeviceProfileConfig { Id = "dev-2", Name = "Untouched", Identity = new DeviceIdentity { VendorId = 1, ProductId = 1, SerialNumber = "B" }, ScreenWidth = 1, ScreenHeight = 1 },
            ],
        };
        var imported = new AppConfig
        {
            Devices =
            [
                new DeviceProfileConfig { Id = "dev-1", Name = "New name", Identity = new DeviceIdentity { VendorId = 1, ProductId = 1, SerialNumber = "A" }, ScreenWidth = 1, ScreenHeight = 1 },
                new DeviceProfileConfig { Id = "dev-3", Name = "Brand new", Identity = new DeviceIdentity { VendorId = 1, ProductId = 1, SerialNumber = "C" }, ScreenWidth = 1, ScreenHeight = 1 },
            ],
        };

        var merged = ConfigPackageService.MergeImported(existing, imported);

        Assert.Equal(3, merged.Devices.Count);
        Assert.Equal("New name", merged.Devices.Single(d => d.Id == "dev-1").Name);
        Assert.Equal("Untouched", merged.Devices.Single(d => d.Id == "dev-2").Name);
        Assert.Equal("Brand new", merged.Devices.Single(d => d.Id == "dev-3").Name);
    }

    public void Dispose()
    {
        Directory.Delete(_tempDirectory, recursive: true);
        GC.SuppressFinalize(this);
    }
}
