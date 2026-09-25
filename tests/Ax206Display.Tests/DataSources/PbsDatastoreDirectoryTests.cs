using Ax206Display.DataSources.Pbs;

namespace Ax206Display.Tests.DataSources;

public class PbsDatastoreDirectoryTests
{
    [Fact]
    public void GetSnapshot_StartsEmpty()
    {
        var directory = new PbsDatastoreDirectory();

        Assert.Empty(directory.GetSnapshot());
    }

    [Fact]
    public void Update_ThenGetSnapshot_ReturnsTheNewListUnderThatHost()
    {
        var directory = new PbsDatastoreDirectory();
        var datastores = new List<PbsDatastoreStatus> { new() { Store = "backup" } };

        directory.Update("host-1", "Main PBS", datastores);

        var hostSnapshot = Assert.Single(directory.GetSnapshot()).Value;
        Assert.Equal("Main PBS", hostSnapshot.DisplayName);
        Assert.Same(datastores, hostSnapshot.Items);
    }

    [Fact]
    public void RemoveHost_DropsIt()
    {
        var directory = new PbsDatastoreDirectory();
        directory.Update("host-1", "Host 1", []);

        directory.RemoveHost("host-1");

        Assert.Empty(directory.GetSnapshot());
    }

    [Fact]
    public void RemoveHost_UnknownHost_IsANoOp()
    {
        var directory = new PbsDatastoreDirectory();
        directory.Update("host-1", "Host 1", []);

        directory.RemoveHost("never-existed");

        Assert.Single(directory.GetSnapshot());
    }
}
