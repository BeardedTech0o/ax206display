using Ax206Display.DataSources.Network;

namespace Ax206Display.Tests.DataSources;

public class NetworkSpeedRateCalculatorTests
{
    [Fact]
    public void ComputeBytesPerSecond_ComputesRateOverElapsedTime()
    {
        var rate = NetworkSpeedRateCalculator.ComputeBytesPerSecond(1000, 3000, TimeSpan.FromSeconds(2));

        Assert.Equal(1000, rate);
    }

    [Fact]
    public void ComputeBytesPerSecond_CounterReset_ClampsToZeroInsteadOfNegative()
    {
        var rate = NetworkSpeedRateCalculator.ComputeBytesPerSecond(5000, 100, TimeSpan.FromSeconds(1));

        Assert.Equal(0, rate);
    }

    [Fact]
    public void ComputeBytesPerSecond_ZeroElapsed_ReturnsZeroInsteadOfDividingByZero()
    {
        var rate = NetworkSpeedRateCalculator.ComputeBytesPerSecond(1000, 2000, TimeSpan.Zero);

        Assert.Equal(0, rate);
    }

    [Fact]
    public void ComputeBytesPerSecond_NoChange_ReturnsZero()
    {
        var rate = NetworkSpeedRateCalculator.ComputeBytesPerSecond(1000, 1000, TimeSpan.FromSeconds(2));

        Assert.Equal(0, rate);
    }
}
