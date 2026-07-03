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

    public T? GetData<T>(string key)
    {
        return Data.TryGetValue(key, out var value) && value is T typed ? typed : default;
    }
}
