using Ax206Display.Transport.Mock;

namespace Ax206Display.Tests.Transport;

public class MockAx206DeviceDiscoveryTests
{
    [Fact]
    public async Task DiscoverAsync_ReturnsConfiguredDevices()
    {
        var discovery = new MockAx206DeviceDiscovery();
        discovery.Devices.Add(new MockAx206Transport("mock-1", 480, 320));
        discovery.Devices.Add(new MockAx206Transport("mock-2", 320, 240));

        var found = await discovery.DiscoverAsync();

        Assert.Equal(2, found.Count);
        Assert.Contains(found, d => d.DeviceId == "mock-1");
        Assert.Contains(found, d => d.DeviceId == "mock-2");
    }

    [Fact]
    public async Task DiscoverAsync_NoDevices_ReturnsEmpty()
    {
        var discovery = new MockAx206DeviceDiscovery();

        var found = await discovery.DiscoverAsync();

        Assert.Empty(found);
    }
}
