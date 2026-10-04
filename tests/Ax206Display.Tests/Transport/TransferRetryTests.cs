using Ax206Display.Transport;

namespace Ax206Display.Tests.Transport;

public class TransferRetryTests
{
    [Fact]
    public async Task RetryOnceAsync_FirstAttemptSucceeds_RunsOnlyOnce()
    {
        var attempts = 0;

        var result = await TransferRetry.RetryOnceAsync(() => Task.FromResult(++attempts), r => false);

        Assert.Equal(1, result);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task RetryOnceAsync_FirstAttemptTimesOut_ReturnsTheSecondAttempt()
    {
        var results = new Queue<string>(["timeout", "ok"]);

        var result = await TransferRetry.RetryOnceAsync(() => Task.FromResult(results.Dequeue()), r => r == "timeout");

        Assert.Equal("ok", result);
        Assert.Empty(results);
    }

    [Fact]
    public async Task RetryOnceAsync_BothAttemptsTimeOut_StopsAfterTwoAndReturnsTheFailure()
    {
        var attempts = 0;

        var result = await TransferRetry.RetryOnceAsync(() => Task.FromResult($"timeout-{++attempts}"), r => r.StartsWith("timeout", StringComparison.Ordinal));

        Assert.Equal("timeout-2", result);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task RetryOnceAsync_ADifferentFailure_IsNotRetried()
    {
        var attempts = 0;

        var result = await TransferRetry.RetryOnceAsync(() => Task.FromResult(++attempts == 1 ? "stall" : "ok"), r => r == "timeout");

        Assert.Equal("stall", result);
        Assert.Equal(1, attempts);
    }
}
