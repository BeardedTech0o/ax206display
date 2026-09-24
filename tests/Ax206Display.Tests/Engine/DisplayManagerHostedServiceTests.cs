using Ax206Display.Config.Services;
using Ax206Display.Engine.Services;
using Ax206Display.Rendering.Playback;
using Ax206Display.Transport.Mock;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ax206Display.Tests.Engine;

public class DisplayManagerHostedServiceTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("ax206display-manager-").FullName;

    /// <summary>
    /// Regression test: a display seen for the very first time used to get
    /// its default profile provisioned, but its supervisor started before
    /// that profile was saved, found nothing in config, and quit - leaving
    /// the panel dark until the next restart.
    /// </summary>
    [Fact]
    public async Task StartAsync_BrandNewDisplay_IsProvisionedAndDriven()
    {
        using var config = new ConfigService(Path.Combine(_directory, "config.json"));
        var transport = new MockAx206Transport("NEW-PANEL", 480, 320);
        var discovery = new MockAx206DeviceDiscovery();
        discovery.Devices.Add(transport);
        using var manager = new DisplayManagerHostedService(discovery, config, new RenderDataHub(), NullLogger<DisplayManagerHostedService>.Instance);

        await manager.StartAsync(CancellationToken.None);
        try
        {
            var saved = await config.LoadAsync();
            Assert.Contains(saved.Devices, d => d.Id == "NEW-PANEL" && d.Widgets.Count > 0);

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (transport.BlitCalls.Count == 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            Assert.NotEmpty(transport.BlitCalls);
            Assert.Contains("NEW-PANEL", manager.ConnectedDeviceIds);
        }
        finally
        {
            await manager.StopAsync(CancellationToken.None);
        }
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }
}
