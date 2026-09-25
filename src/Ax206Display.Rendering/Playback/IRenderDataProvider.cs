namespace Ax206Display.Rendering.Playback;

/// <summary>
/// Supplies the latest data snapshot for a frame render. Implementations must
/// be safe to call from any thread - display loops run concurrently, one per
/// device.
/// </summary>
public interface IRenderDataProvider
{
    IReadOnlyDictionary<string, object> GetSnapshot();

    /// <summary>
    /// Every numeric sample published for <paramref name="key"/> within the
    /// last <paramref name="window"/>, oldest first - for a chart widget/the
    /// web dashboard's charts. Empty if the key has never been published as
    /// a number (a chart widget renders a placeholder rather than throwing -
    /// see <see cref="Widgets.ChartWidget"/>).
    /// </summary>
    IReadOnlyList<MetricSample> GetHistory(string key, TimeSpan window);
}
