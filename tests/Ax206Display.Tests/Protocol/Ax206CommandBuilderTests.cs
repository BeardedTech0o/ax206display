using Ax206Display.Protocol.Commands;

namespace Ax206Display.Tests.Protocol;

public class Ax206CommandBuilderTests
{
    [Fact]
    public void GetLcdParameters_BuildsExpectedCdb()
    {
        var cbw = Ax206CommandBuilder.GetLcdParameters();
        var bytes = cbw.ToBytes();
        var cdb = bytes[15..31];

        Assert.Equal(Ax206CdbSelector.VendorOpcode, cdb[0]);
        Assert.Equal(Ax206CdbSelector.GetLcdParameters, cdb[5]);
        Assert.True(cbw.IsDataPhaseIn);
        Assert.Equal(LcdParametersResponse.Length, cbw.DataTransferLength);
    }

    [Fact]
    public void SetProperty_Brightness_EncodesTokenAndValueLittleEndian()
    {
        var cbw = Ax206CommandBuilder.SetProperty(Ax206Property.Brightness, 7);
        var bytes = cbw.ToBytes();
        var cdb = bytes[15..31];

        Assert.Equal(Ax206CdbSelector.VendorOpcode, cdb[0]);
        Assert.Equal(Ax206CdbSelector.UserCommand, cdb[5]);
        Assert.Equal((byte)Ax206UserCommand.SetProperty, cdb[6]);
        Assert.Equal(0x01, cdb[7]); // PROPERTY_BRIGHTNESS token, low byte
        Assert.Equal(0x00, cdb[8]);
        Assert.Equal(0x07, cdb[9]); // value, low byte
        Assert.Equal(0x00, cdb[10]);
        Assert.False(cbw.IsDataPhaseIn);
        Assert.Equal(0u, cbw.DataTransferLength);
    }

    [Fact]
    public void Blit_EncodesInclusiveRectangleAndByteLength()
    {
        var cbw = Ax206CommandBuilder.Blit(left: 10, top: 20, right: 110, bottom: 220);
        var bytes = cbw.ToBytes();
        var cdb = bytes[15..31];

        Assert.Equal(Ax206CdbSelector.VendorOpcode, cdb[0]);
        Assert.Equal(Ax206CdbSelector.UserCommand, cdb[5]);
        Assert.Equal((byte)Ax206UserCommand.Blit, cdb[6]);
        Assert.Equal(10, BitConverter.ToUInt16(cdb, 7));
        Assert.Equal(20, BitConverter.ToUInt16(cdb, 9));
        Assert.Equal(109, BitConverter.ToUInt16(cdb, 11)); // right - 1
        Assert.Equal(219, BitConverter.ToUInt16(cdb, 13)); // bottom - 1

        var expectedByteLength = (uint)((110 - 10) * (220 - 20) * 2);
        Assert.Equal(expectedByteLength, cbw.DataTransferLength);
        Assert.False(cbw.IsDataPhaseIn);
    }

    [Theory]
    [InlineData(10, 20, 10, 220)] // zero width
    [InlineData(10, 20, 110, 20)] // zero height
    [InlineData(50, 20, 10, 220)] // right before left
    public void Blit_InvalidRectangle_Throws(ushort left, ushort top, ushort right, ushort bottom)
    {
        Assert.Throws<ArgumentException>(() => Ax206CommandBuilder.Blit(left, top, right, bottom));
    }
}
