namespace Ax206Display.Rendering.Widgets;

/// <summary>
/// Font family, weight, slant, and size for a widget's text. This is a
/// record *class*, not a record struct: for a struct, `new WidgetFontStyle()`
/// invokes the struct's built-in zero-init constructor rather than the
/// primary constructor's declared defaults, silently producing
/// <see cref="SizeScale"/> = 0 instead of 1 (shrinking all text to nothing).
/// Only a reference-type record runs the declared defaults on a parameterless
/// `new()`.
/// </summary>
/// <param name="SizeScale">
/// Legacy relative sizing (multiplier on the auto-fitted size) - kept so
/// layouts saved before <paramref name="FixedSizePixels"/> existed still
/// render identically. Ignored when <paramref name="FixedSizePixels"/> is set.
/// </param>
/// <param name="FixedSizePixels">
/// Exact text size in device pixels (the AX206 panel's own pixels, so 24
/// means 24px tall on the display regardless of the widget's box height).
/// Null = auto-fit to the widget box, the historical behavior.
/// </param>
public sealed record WidgetFontStyle(string? FontFamily = null, bool Bold = false, bool Italic = false, float SizeScale = 1f, float? FixedSizePixels = null)
{
    public static readonly WidgetFontStyle Default = new();
}
