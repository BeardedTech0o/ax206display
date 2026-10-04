namespace Ax206Display.Transport;

/// <summary>The one-retry rule for USB reads, kept free of libusb types so it can be tested without hardware.</summary>
public static class TransferRetry
{
    /// <summary>
    /// Runs <paramref name="attempt"/>; if its result satisfies
    /// <paramref name="shouldRetry"/>, runs it exactly once more and returns
    /// that result, whatever it is. Never more than two attempts in total.
    /// </summary>
    public static async Task<T> RetryOnceAsync<T>(Func<Task<T>> attempt, Func<T, bool> shouldRetry)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(shouldRetry);

        var result = await attempt();
        return shouldRetry(result) ? await attempt() : result;
    }
}
