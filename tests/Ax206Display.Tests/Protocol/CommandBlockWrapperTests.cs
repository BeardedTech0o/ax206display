using Ax206Display.Protocol.Transport;

namespace Ax206Display.Tests.Protocol;

public class CommandBlockWrapperTests
{
    [Fact]
    public void ToBytes_ProducesCorrectHeaderLayout()
    {
        var cdb = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
        var cbw = new CommandBlockWrapper
        {
            DataTransferLength = 0x1234,
            IsDataPhaseIn = true,
            CommandDescriptorBlock = cdb,
        };

        var bytes = cbw.ToBytes();

        Assert.Equal(BulkOnlyTransport.CommandBlockWrapperLength, bytes.Length);
        Assert.Equal(new byte[] { 0x55, 0x53, 0x42, 0x43 }, bytes[..4]); // "USBC"
        Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, bytes[4..8]);
        Assert.Equal(new byte[] { 0x34, 0x12, 0x00, 0x00 }, bytes[8..12]); // little-endian 0x1234
        Assert.Equal(BulkOnlyTransport.DirectionFlagIn, bytes[12]);
        Assert.Equal(0x00, bytes[13]);
        Assert.Equal(BulkOnlyTransport.CommandDescriptorBlockLength, bytes[14]);
        Assert.Equal(cdb, bytes[15..31]);
    }

    [Fact]
    public void ToBytes_OutDirection_SetsZeroFlag()
    {
        var cbw = new CommandBlockWrapper
        {
            DataTransferLength = 0,
            IsDataPhaseIn = false,
            CommandDescriptorBlock = new byte[16],
        };

        var bytes = cbw.ToBytes();

        Assert.Equal(BulkOnlyTransport.DirectionFlagOut, bytes[12]);
    }

    [Fact]
    public void ToBytes_CdbTooLong_Throws()
    {
        var cbw = new CommandBlockWrapper
        {
            DataTransferLength = 0,
            IsDataPhaseIn = false,
            CommandDescriptorBlock = new byte[17],
        };

        Assert.Throws<ArgumentException>(() => cbw.ToBytes());
    }
}
