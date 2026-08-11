using Ax206Display.Rendering.Widgets;

namespace Ax206Display.Tests.Rendering;

public class DesignerSnapEngineTests
{
    private const int CanvasWidth = 480;
    private const int CanvasHeight = 320;

    [Fact]
    public void SnapMove_FarFromEverything_DoesNotMoveAndReportsNoGuides()
    {
        var moving = new SnapBox(100, 100, 50, 30);

        var result = DesignerSnapEngine.SnapMove(moving, [], CanvasWidth, CanvasHeight);

        Assert.Equal(100, result.X);
        Assert.Equal(100, result.Y);
        Assert.Null(result.VerticalGuide);
        Assert.Null(result.HorizontalGuide);
    }

    [Fact]
    public void SnapMove_NearCanvasLeftEdge_PullsFlushToIt()
    {
        var moving = new SnapBox(4, 100, 50, 30);

        var result = DesignerSnapEngine.SnapMove(moving, [], CanvasWidth, CanvasHeight);

        Assert.Equal(0, result.X);
        Assert.Equal(0, result.VerticalGuide);
    }

    [Fact]
    public void SnapMove_CenterNearCanvasCenter_CentersTheBox()
    {
        // Canvas center x = 240; a 50-wide box centered there sits at x=215.
        var moving = new SnapBox(212, 100, 50, 30);

        var result = DesignerSnapEngine.SnapMove(moving, [], CanvasWidth, CanvasHeight);

        Assert.Equal(215, result.X);
        Assert.Equal(240, result.VerticalGuide);
    }

    [Fact]
    public void SnapMove_NearAnotherWidgetsLeftEdge_AlignsWithIt()
    {
        var other = new SnapBox(200, 20, 80, 40);
        var moving = new SnapBox(196, 150, 50, 30);

        var result = DesignerSnapEngine.SnapMove(moving, [other], CanvasWidth, CanvasHeight);

        Assert.Equal(200, result.X);
        Assert.Equal(200, result.VerticalGuide);
    }

    [Fact]
    public void SnapMove_TrailingEdgeNearAnotherWidgetsRightEdge_AlignsFlush()
    {
        // Other's right edge is 280; moving box is 50 wide, so flush means x=230.
        var other = new SnapBox(200, 20, 80, 40);
        var moving = new SnapBox(233, 150, 50, 30);

        var result = DesignerSnapEngine.SnapMove(moving, [other], CanvasWidth, CanvasHeight);

        Assert.Equal(230, result.X);
        Assert.Equal(280, result.VerticalGuide);
    }

    [Fact]
    public void SnapMove_VerticalAndHorizontalSnapIndependently()
    {
        var other = new SnapBox(200, 60, 80, 40);
        // Left edge 3px from other's left; vertically (top 120, center 135,
        // bottom 150) everything is >6px from all lines (0, 160, 320, 60,
        // 80, 100), so only the x axis should snap.
        var moving = new SnapBox(203, 120, 50, 30);

        var result = DesignerSnapEngine.SnapMove(moving, [other], CanvasWidth, CanvasHeight);

        Assert.Equal(200, result.X);
        Assert.NotNull(result.VerticalGuide);
        Assert.Equal(120, result.Y);
        Assert.Null(result.HorizontalGuide);
    }

    [Fact]
    public void SnapMove_PicksTheClosestCandidateWhenSeveralAreInRange()
    {
        // Two others with left edges at 100 and 104; moving at 103 is 3px
        // from 100 but only 1px from 104.
        var farOther = new SnapBox(100, 20, 40, 20);
        var nearOther = new SnapBox(104, 250, 40, 20);
        var moving = new SnapBox(103, 150, 50, 30);

        var result = DesignerSnapEngine.SnapMove(moving, [farOther, nearOther], CanvasWidth, CanvasHeight);

        Assert.Equal(104, result.X);
        Assert.Equal(104, result.VerticalGuide);
    }

    [Fact]
    public void SnapMove_JustOutsideTolerance_DoesNotSnap()
    {
        var other = new SnapBox(200, 20, 80, 40);
        var moving = new SnapBox(200 + DesignerSnapEngine.DefaultTolerance + 1, 150, 50, 30);

        var result = DesignerSnapEngine.SnapMove(moving, [other], CanvasWidth, CanvasHeight);

        Assert.Equal(207, result.X);
        Assert.Null(result.VerticalGuide);
    }
}
