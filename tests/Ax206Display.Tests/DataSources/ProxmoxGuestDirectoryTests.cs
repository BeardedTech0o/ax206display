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
    public void Update_ThenGetSnapshot_ReturnsTheNewList()
    {
        var directory = new ProxmoxGuestDirectory();
        var guests = new List<ProxmoxGuestStatus>
        {
            new() { Node = "pve1", VmId = 100, Name = "web-vm", Type = "qemu", Status = "running" },
        };

        directory.Update(guests);

        Assert.Same(guests, directory.GetSnapshot());
    }

    [Fact]
    public void Update_DoesNotMutateEarlierSnapshots()
    {
        var directory = new ProxmoxGuestDirectory();
        var first = new List<ProxmoxGuestStatus> { new() { Node = "pve1", VmId = 100, Name = "a", Type = "qemu", Status = "running" } };
        directory.Update(first);

        var before = directory.GetSnapshot();
        directory.Update([]);

        Assert.Single(before);
    }
}
