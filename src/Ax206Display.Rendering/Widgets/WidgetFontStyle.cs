namespace Ax206Display.Rendering.Widgets;

/// <summary>
/// Font family, weight, slant, and size-scale for a widget's text. This is a
/// record *class*, not a record struct: for a struct, `new WidgetFontStyle()`
/// invokes the struct's built-in zero-init constructor rather than the
/// primary constructor's declared defaults, silently producing
/// <see cref="SizeScale"/> = 0 instead of 1 (shrinking all text to nothing).
/// Only a reference-type record runs the declared defaults on a parameterless
/// `new()`.
/// </summary>
public sealed record WidgetFontStyle(string? FontFamily = null, bool Bold = false, bool Italic = false, float SizeScale = 1f)
{
    public static readonly WidgetFontStyle Default = new();
}
