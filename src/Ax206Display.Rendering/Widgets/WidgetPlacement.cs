namespace Ax206Display.Rendering.Widgets;

/// <summary>Where on the device canvas a widget instance is drawn.</summary>
public sealed record WidgetPlacement(IWidget Widget, int X, int Y, int ZOrder);
