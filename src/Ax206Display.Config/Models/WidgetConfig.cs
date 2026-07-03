using System.Text.Json.Nodes;

namespace Ax206Display.Config.Models;

/// <summary>
/// A single widget placed on a device's layout. <see cref="Settings"/> holds the
/// widget-type-specific properties as a JSON object so new widget types don't
/// require config schema/version changes.
/// </summary>
public sealed record WidgetConfig
{
    public required string Id { get; init; }

    /// <summary>Discriminator matched against a registered widget factory, e.g. "clock", "weather".</summary>
    public required string Type { get; init; }

    public required int X { get; init; }

    public required int Y { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public int ZOrder { get; init; }

    public JsonObject Settings { get; init; } = new();
}
