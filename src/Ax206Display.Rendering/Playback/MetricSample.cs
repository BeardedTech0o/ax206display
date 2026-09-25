namespace Ax206Display.Rendering.Playback;

/// <summary>One historical reading of a numeric render-data key - see <see cref="IRenderDataProvider.GetHistory"/>.</summary>
public readonly record struct MetricSample(DateTimeOffset Timestamp, double Value);
