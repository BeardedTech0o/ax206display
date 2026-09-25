using Ax206Display.DataSources.Proxmox;

namespace Ax206Display.Tests.DataSources;

public class ProxmoxNodeDirectoryTests
{
    [Fact]
    public void GetSnapshot_StartsEmpty()
    {
        var directory = new ProxmoxNodeDirectory();

        Assert.Empty(directory.GetSnapshot());
    }

    [Fact]
    public void Update_ThenGetSnapshot_ReturnsTheNewListUnderThatHost()
    {
        var directory = new ProxmoxNodeDirectory();
        var nodes = new List<ProxmoxNodeStatus> { new() { Node = "pve1", Status = "online" } };

        directory.Update("host-1", "Main Cluster", nodes);

        var hostSnapshot = Assert.Single(directory.GetSnapshot()).Value;
        Assert.Equal("Main Cluster", hostSnapshot.DisplayName);
        Assert.Same(nodes, hostSnapshot.Items);
    }

    [Fact]
    public void Update_DoesNotMutateEarlierSnapshots()
    {
        var directory = new ProxmoxNodeDirectory();
        var first = new List<ProxmoxNodeStatus> { new() { Node = "pve1", Status = "online" } };
        directory.Update("host-1", "Host 1", first);

        var before = directory.GetSnapshot();
        directory.Update("host-1", "Host 1", []);

        Assert.Single(before["host-1"].Items);
    }

    [Fact]
    public void RemoveHost_DropsIt()
    {
        var directory = new ProxmoxNodeDirectory();
        directory.Update("host-1", "Host 1", []);

        directory.RemoveHost("host-1");

        Assert.Empty(directory.GetSnapshot());
    }
}
