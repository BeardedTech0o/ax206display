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
    public void Update_ThenGetSnapshot_ReturnsTheNewList()
    {
        var directory = new ProxmoxNodeDirectory();
        var nodes = new List<ProxmoxNodeStatus> { new() { Node = "pve1", Status = "online" } };

        directory.Update(nodes);

        Assert.Same(nodes, directory.GetSnapshot());
    }

    [Fact]
    public void Update_DoesNotMutateEarlierSnapshots()
    {
        var directory = new ProxmoxNodeDirectory();
        var first = new List<ProxmoxNodeStatus> { new() { Node = "pve1", Status = "online" } };
        directory.Update(first);

        var before = directory.GetSnapshot();
        directory.Update([]);

        Assert.Single(before);
    }
}
