using Ax206Display.DataSources.Proxmox;
using Ax206Display.Rendering.Playback;

namespace Ax206Display.Tests.DataSources;

public class ProxmoxStatsPublisherTests
{
    [Fact]
    public void Publish_PublishesEachGuestUnderItsOwnKeys()
    {
        var hub = new RenderDataHub();
        var guests = new List<ProxmoxGuestStatus>
        {
            new()
            {
                Node = "pve1",
                VmId = 100,
                Name = "web-vm",
                Type = "qemu",
                Status = "running",
                CpuUsageFraction = 0.25,
                MemoryUsedBytes = 1_000_000_000,
                MemoryTotalBytes = 2_000_000_000,
            },
            new()
            {
                Node = "pve1",
                VmId = 200,
                Name = "db-ct",
                Type = "lxc",
                Status = "stopped",
                CpuUsageFraction = 0,
                MemoryUsedBytes = 0,
                MemoryTotalBytes = 536_870_912,
            },
        };

        ProxmoxStatsPublisher.Publish(guests, hub.Publish);

        var data = hub.GetSnapshot();
        Assert.Equal(25.0, (double)data[ProxmoxGuestKeys.CpuUsedPercent(100)]);
        Assert.Equal(50.0, (double)data[ProxmoxGuestKeys.MemoryUsedPercent(100)]);
        Assert.Equal(0.0, (double)data[ProxmoxGuestKeys.CpuUsedPercent(200)]);
        Assert.Equal(0.0, (double)data[ProxmoxGuestKeys.MemoryUsedPercent(200)]);
    }

    [Fact]
    public void Publish_ZeroMemoryTotal_DoesNotDivideByZero()
    {
        var hub = new RenderDataHub();
        var guests = new List<ProxmoxGuestStatus>
        {
            new() { Node = "pve1", VmId = 300, Name = "weird-vm", Type = "qemu", Status = "running", MemoryTotalBytes = 0 },
        };

        ProxmoxStatsPublisher.Publish(guests, hub.Publish);

        Assert.Equal(0.0, (double)hub.GetSnapshot()[ProxmoxGuestKeys.MemoryUsedPercent(300)]);
    }
}
