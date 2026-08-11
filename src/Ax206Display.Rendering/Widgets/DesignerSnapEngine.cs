namespace Ax206Display.Rendering.Widgets;

/// <summary>A widget's rectangle as the snap engine sees it, in canvas (device-pixel) coordinates.</summary>
public readonly record struct SnapBox(int X, int Y, int Width, int Height);

/// <summary>
/// Where a dragged box landed after snapping. <see cref="VerticalGuide"/> /
/// <see cref="HorizontalGuide"/> are the canvas coordinates of the alignment
/// line that was snapped to (for drawing a guide), or null on that axis if
/// nothing was within tolerance.
/// </summary>
public sealed record SnapMoveResult(int X, int Y, int? VerticalGuide, int? HorizontalGuide);

/// <summary>
/// Canva-style move snapping for the Widget Designer: while dragging, a box's
/// edges and center pull into alignment with the canvas edges, the canvas
/// center, and every other widget's edges and centers, once within
/// <see cref="DefaultTolerance"/> pixels. Pure math - the WPF overlay draws
/// the guides and applies the returned position.
/// </summary>
public static class DesignerSnapEngine
{
    public const int DefaultTolerance = 6;

    public static SnapMoveResult SnapMove(
        SnapBox moving,
        IEnumerable<SnapBox> otherBoxes,
        int canvasWidth,
        int canvasHeight,
        int tolerance = DefaultTolerance)
    {
        var verticalLines = new List<int> { 0, canvasWidth / 2, canvasWidth };
        var horizontalLines = new List<int> { 0, canvasHeight / 2, canvasHeight };

        foreach (var box in otherBoxes)
        {
            verticalLines.Add(box.X);
            verticalLines.Add(box.X + box.Width / 2);
            verticalLines.Add(box.X + box.Width);

            horizontalLines.Add(box.Y);
            horizontalLines.Add(box.Y + box.Height / 2);
            horizontalLines.Add(box.Y + box.Height);
        }

        var (x, verticalGuide) = SnapAxis(moving.X, moving.Width, verticalLines, tolerance);
        var (y, horizontalGuide) = SnapAxis(moving.Y, moving.Height, horizontalLines, tolerance);

        return new SnapMoveResult(x, y, verticalGuide, horizontalGuide);
    }

    /// <summary>
    /// One axis of the snap: tries the box's leading edge, center, and
    /// trailing edge against every candidate line and takes the closest match
    /// within tolerance (ties go to the earliest candidate, i.e. canvas lines
    /// before widget lines, in list order).
    /// </summary>
    private static (int Position, int? Guide) SnapAxis(int position, int size, List<int> lines, int tolerance)
    {
        var bestDistance = tolerance + 1;
        var bestPosition = position;
        int? guide = null;

        ReadOnlySpan<int> anchorOffsets = [0, size / 2, size];

        foreach (var line in lines)
        {
            foreach (var offset in anchorOffsets)
            {
                var distance = Math.Abs(position + offset - line);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestPosition = line - offset;
                    guide = line;
                }
            }
        }

        return (bestPosition, guide);
    }
}
