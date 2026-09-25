using Ax206Display.DataSources.Proxmox;

namespace Ax206Display.Tests.DataSources;

public class ProxmoxGuestDirectoryTests
{
    [Fact]
    public void GetSnapshot_StartsEmpty()
    {
        var directory = new ProxmoxGuestDirectory();

        Assert.Empty(directory.GetSnapshot());
    }

    [Fact]
    public void Update_ThenGetSnapshot_ReturnsTheNewListUnderThatHost()
    {
        var directory = new ProxmoxGuestDirectory();
        var guests = new List<ProxmoxGuestStatus>
        {
            new() { Node = "pve1", VmId = 100, Name = "web-vm", Type = "qemu", Status = "running" },
        };

        directory.Update("host-1", "Main Cluster", guests);

        var snapshot = directory.GetSnapshot();
        var hostSnapshot = Assert.Single(snapshot).Value;
        Assert.Equal("Main Cluster", hostSnapshot.DisplayName);
        Assert.Same(guests, hostSnapshot.Items);
    }

    [Fact]
    public void Update_DoesNotMutateEarlierSnapshots()
    {
        var directory = new ProxmoxGuestDirectory();
        var first = new List<ProxmoxGuestStatus> { new() { Node = "pve1", VmId = 100, Name = "a", Type = "qemu", Status = "running" } };
        directory.Update("host-1", "Host 1", first);

        var before = directory.GetSnapshot();
        directory.Update("host-1", "Host 1", []);

        Assert.Single(before["host-1"].Items);
    }

    [Fact]
    public void Update_TwoHosts_KeepsThemSeparate()
    {
        var directory = new ProxmoxGuestDirectory();
        var hostAGuests = new List<ProxmoxGuestStatus> { new() { Node = "pve1", VmId = 100, Name = "a", Type = "qemu", Status = "running" } };
        var hostBGuests = new List<ProxmoxGuestStatus> { new() { Node = "pve2", VmId = 100, Name = "b", Type = "qemu", Status = "running" } };

        directory.Update("host-a", "Host A", hostAGuests);
        directory.Update("host-b", "Host B", hostBGuests);

        var snapshot = directory.GetSnapshot();
        Assert.Equal(2, snapshot.Count);
        Assert.Same(hostAGuests, snapshot["host-a"].Items);
        Assert.Same(hostBGuests, snapshot["host-b"].Items);
    }

    [Fact]
    public void RemoveHost_DropsIt()
    {
        var directory = new ProxmoxGuestDirectory();
        directory.Update("host-1", "Host 1", []);

        directory.RemoveHost("host-1");

        Assert.Empty(directory.GetSnapshot());
    }

    [Fact]
    public void RemoveHost_UnknownHost_IsANoOp()
    {
        var directory = new ProxmoxGuestDirectory();
        directory.Update("host-1", "Host 1", []);

        directory.RemoveHost("never-existed");

        Assert.Single(directory.GetSnapshot());
    }
}
