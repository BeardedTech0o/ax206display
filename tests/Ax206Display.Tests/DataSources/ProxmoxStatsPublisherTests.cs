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

        ProxmoxStatsPublisher.Publish("host-1", guests, hub.Publish);

        var data = hub.GetSnapshot();
        Assert.Equal(25.0, (double)data[ProxmoxGuestKeys.CpuUsedPercent("host-1", 100)]);
        Assert.Equal(50.0, (double)data[ProxmoxGuestKeys.MemoryUsedPercent("host-1", 100)]);
        Assert.Equal(0.0, (double)data[ProxmoxGuestKeys.CpuUsedPercent("host-1", 200)]);
        Assert.Equal(0.0, (double)data[ProxmoxGuestKeys.MemoryUsedPercent("host-1", 200)]);
    }

    [Fact]
    public void Publish_TwoHostsWithSameVmId_DoNotCollide()
    {
        var hub = new RenderDataHub();
        var hostAGuest = new List<ProxmoxGuestStatus> { new() { Node = "pve1", VmId = 100, Name = "a", Type = "qemu", Status = "running", CpuUsageFraction = 0.1 } };
        var hostBGuest = new List<ProxmoxGuestStatus> { new() { Node = "pve2", VmId = 100, Name = "b", Type = "qemu", Status = "running", CpuUsageFraction = 0.9 } };

        ProxmoxStatsPublisher.Publish("host-a", hostAGuest, hub.Publish);
        ProxmoxStatsPublisher.Publish("host-b", hostBGuest, hub.Publish);

        var data = hub.GetSnapshot();
        Assert.Equal(10.0, (double)data[ProxmoxGuestKeys.CpuUsedPercent("host-a", 100)]);
        Assert.Equal(90.0, (double)data[ProxmoxGuestKeys.CpuUsedPercent("host-b", 100)]);
    }

    [Fact]
    public void Publish_ZeroMemoryTotal_DoesNotDivideByZero()
    {
        var hub = new RenderDataHub();
        var guests = new List<ProxmoxGuestStatus>
        {
            new() { Node = "pve1", VmId = 300, Name = "weird-vm", Type = "qemu", Status = "running", MemoryTotalBytes = 0 },
        };

        ProxmoxStatsPublisher.Publish("host-1", guests, hub.Publish);

        Assert.Equal(0.0, (double)hub.GetSnapshot()[ProxmoxGuestKeys.MemoryUsedPercent("host-1", 300)]);
    }

    [Fact]
    public void PublishNodes_PublishesEachNodeUnderItsOwnKeys()
    {
        var hub = new RenderDataHub();
        var nodes = new List<ProxmoxNodeStatus>
        {
            new()
            {
                Node = "pve1",
                Status = "online",
                CpuUsageFraction = 0.4,
                MemoryUsedBytes = 3_000_000_000,
                MemoryTotalBytes = 6_000_000_000,
                UptimeSeconds = 864_000, // 10 days
            },
        };

        ProxmoxStatsPublisher.PublishNodes("host-1", nodes, hub.Publish);

        var data = hub.GetSnapshot();
        Assert.Equal(40.0, (double)data[ProxmoxNodeKeys.CpuUsedPercent("host-1", "pve1")]);
        Assert.Equal(50.0, (double)data[ProxmoxNodeKeys.MemoryUsedPercent("host-1", "pve1")]);
        Assert.Equal(10.0, (double)data[ProxmoxNodeKeys.UptimeDays("host-1", "pve1")]);
    }

    [Fact]
    public void PublishNodes_ZeroMemoryTotal_DoesNotDivideByZero()
    {
        var hub = new RenderDataHub();
        var nodes = new List<ProxmoxNodeStatus> { new() { Node = "pve1", Status = "online", MemoryTotalBytes = 0 } };

        ProxmoxStatsPublisher.PublishNodes("host-1", nodes, hub.Publish);

        Assert.Equal(0.0, (double)hub.GetSnapshot()[ProxmoxNodeKeys.MemoryUsedPercent("host-1", "pve1")]);
    }
}
