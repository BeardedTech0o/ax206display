namespace Ax206Display.Rendering.Playback;

/// <summary>
/// The bridge between data-source pumps (which poll hardware sensors, HTTP
/// APIs, ...) and display loops (which render frames). Copy-on-write: writers
/// swap in a fresh dictionary, so readers get an immutable snapshot without
/// taking a lock on the render path.
/// </summary>
public sealed class RenderDataHub : IRenderDataProvider
{
    private readonly object _writeLock = new();
    private Dictionary<string, object> _current = [];

    public IReadOnlyDictionary<string, object> GetSnapshot() => _current;

    /// <summary>Sets one value, keeping all other published keys.</summary>
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
    }

    /// <summary>Removes a key, e.g. when a sensor stops reporting, so widgets fall back to their placeholder rendering.</summary>
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
}
