namespace Ax206Display.Rendering.Playback;

/// <summary>
/// Supplies the latest data snapshot for a frame render. Implementations must
/// be safe to call from any thread - display loops run concurrently, one per
/// device.
/// </summary>
public interface IRenderDataProvider
{
    IReadOnlyDictionary<string, object> GetSnapshot();
}
