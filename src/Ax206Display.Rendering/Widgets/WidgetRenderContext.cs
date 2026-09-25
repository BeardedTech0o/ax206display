using Ax206Display.Rendering.Playback;

namespace Ax206Display.Rendering.Widgets;

/// <summary>
/// Everything a widget needs to draw one frame. <see cref="Data"/> holds the
/// latest snapshot published by data sources (system stats, weather, ...),
/// keyed by the same source id widgets declare they depend on.
/// </summary>
public sealed class WidgetRenderContext
{
    public required DateTimeOffset Now { get; init; }

    public required IReadOnlyDictionary<string, object> Data { get; init; }

    /// <summary>
    /// Null in contexts with no data provider at all (there are none left in
    /// practice - both DeviceDisplayLoop and the web preview endpoint always
    /// supply one - but kept nullable rather than required so a future
    /// caller/test isn't forced to wire one up just to render a frame with
    /// no chart widgets in it). <see cref="ChartWidget"/> is the only
    /// consumer today.
    /// </summary>
    public IRenderDataProvider? DataProvider { get; init; }

    public T? GetData<T>(string key)
    {
        return Data.TryGetValue(key, out var value) && value is T typed ? typed : default;
    }

    public IReadOnlyList<MetricSample> GetHistory(string key, TimeSpan window) =>
        DataProvider?.GetHistory(key, window) ?? [];
}
