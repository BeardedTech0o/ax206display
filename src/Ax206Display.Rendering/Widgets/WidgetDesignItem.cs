using System.Text.Json.Nodes;
using Ax206Display.Config.Models;

namespace Ax206Display.Rendering.Widgets;

/// <summary>
/// A mutable, editable stand-in for the immutable <see cref="WidgetConfig"/>
/// record - the Widget Designer needs to move/resize/retype a widget in
/// place (drag, property panel edits) without rebuilding a new record on
/// every keystroke or mouse-move tick.
/// </summary>
public sealed class WidgetDesignItem
{
    public required string Id { get; set; }

    public required string Type { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public int ZOrder { get; set; }

    public JsonObject Settings { get; set; } = [];

    public static WidgetDesignItem FromConfig(WidgetConfig config)
    {
        return new WidgetDesignItem
        {
            Id = config.Id,
            Type = config.Type,
            X = config.X,
            Y = config.Y,
            Width = config.Width,
            Height = config.Height,
            ZOrder = config.ZOrder,
            Settings = (JsonObject)config.Settings.DeepClone(),
        };
    }

    public WidgetConfig ToConfig()
    {
        return new WidgetConfig
        {
            Id = Id,
            Type = Type,
            X = X,
            Y = Y,
            Width = Width,
            Height = Height,
            ZOrder = ZOrder,
            Settings = (JsonObject)Settings.DeepClone(),
        };
    }

    public string? GetSetting(string key) => Settings[key]?.GetValue<string>();

    public void SetSetting(string key, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            Settings.Remove(key);
        }
        else
        {
            Settings[key] = value;
        }
    }

    public int GetIntSetting(string key, int defaultValue) => Settings[key]?.GetValue<int>() ?? defaultValue;

    public void SetIntSetting(string key, int value) => Settings[key] = value;

    public double GetDoubleSetting(string key, double defaultValue) => Settings[key]?.GetValue<double>() ?? defaultValue;

    public void SetDoubleSetting(string key, double value) => Settings[key] = value;

    public bool GetBoolSetting(string key, bool defaultValue) => Settings[key]?.GetValue<bool>() ?? defaultValue;

    public void SetBoolSetting(string key, bool value) => Settings[key] = value;
}
