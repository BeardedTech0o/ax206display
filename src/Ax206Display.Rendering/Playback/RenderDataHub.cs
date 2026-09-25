namespace Ax206Display.Rendering.Playback;

/// <summary>
/// The bridge between data-source pumps (which poll hardware sensors, HTTP
/// APIs, ...) and display loops (which render frames). Copy-on-write: writers
/// swap in a fresh dictionary, so readers get an immutable snapshot without
/// taking a lock on the render path.
///
/// Also retains a bounded history of every numeric value published, for
/// chart widgets/the web dashboard's charts (<see cref="GetHistory"/>) -
/// history uses its own lock (<see cref="_historyLock"/>) rather than
/// <see cref="_writeLock"/> so a slow history read/prune never blocks the
/// latest-snapshot publish path a display loop is waiting on.
/// </summary>
public sealed class RenderDataHub : IRenderDataProvider
{
    // Bounds memory regardless of how often a source polls - a 2-second
    // poller hitting this count still only holds ~11 hours of samples, but
    // that's the same tradeoff every fixed-capacity metrics ring buffer
    // makes, and 20,000 doubles-plus-timestamps per key is negligible.
    private const int MaxSamplesPerKey = 20_000;
    private static readonly TimeSpan HistoryRetention = TimeSpan.FromDays(7);

    private readonly object _writeLock = new();
    private Dictionary<string, object> _current = [];

    private readonly object _historyLock = new();
    private readonly Dictionary<string, List<MetricSample>> _history = [];

    public IReadOnlyDictionary<string, object> GetSnapshot() => _current;

    /// <summary>Sets one value, keeping all other published keys. Numeric values are also recorded into that key's history.</summary>
    public void Publish(string key, object value)
    {
        lock (_writeLock)
        {
            var next = new Dictionary<string, object>(_current)
            {
                [key] = value,
            };
            _current = next;
        }

        if (value is double numeric)
        {
            RecordHistory(key, numeric);
        }
    }

    /// <summary>
    /// Removes a key, e.g. when a sensor stops reporting, so widgets fall
    /// back to their placeholder rendering. Deliberately leaves that key's
    /// history intact - a transient outage shouldn't erase a chart's past
    /// data, it should just show a gap.
    /// </summary>
    public void Remove(string key)
    {
        lock (_writeLock)
        {
            if (!_current.ContainsKey(key))
            {
                return;
            }

            var next = new Dictionary<string, object>(_current);
            next.Remove(key);
            _current = next;
        }
    }

    public IReadOnlyList<MetricSample> GetHistory(string key, TimeSpan window)
    {
        lock (_historyLock)
        {
            if (!_history.TryGetValue(key, out var samples))
            {
                return [];
            }

            var cutoff = DateTimeOffset.UtcNow - window;
            return samples.Where(s => s.Timestamp >= cutoff).ToList();
        }
    }

    private void RecordHistory(string key, double value)
    {
        lock (_historyLock)
        {
            if (!_history.TryGetValue(key, out var samples))
            {
                samples = [];
                _history[key] = samples;
            }

            var now = DateTimeOffset.UtcNow;
            samples.Add(new MetricSample(now, value));

            var cutoff = now - HistoryRetention;
            var firstKeptIndex = samples.FindIndex(s => s.Timestamp >= cutoff);
            if (firstKeptIndex < 0)
            {
                samples.Clear();
            }
            else if (firstKeptIndex > 0)
            {
                samples.RemoveRange(0, firstKeptIndex);
            }

            if (samples.Count > MaxSamplesPerKey)
            {
                samples.RemoveRange(0, samples.Count - MaxSamplesPerKey);
            }
        }
    }
}
