using System;
using MCG_3DPanel.Services.Detail;
using Xunit;

public class SectionFrameTests
{
    static DetailRegion Rect() => new DetailRegion
    {
        Corners = new[] { new P2(0, 0), new P2(200, 0), new P2(200, 100), new P2(0, 100) }
    };

    [Fact]
    public void Snap_MostlyHorizontal()
    {
        var p = SectionFrame.Snap(new P2(10, 10), new P2(110, 25));
        Assert.Equal(110, p.X, 6);
        Assert.Equal(10, p.Y, 6);
    }

    [Fact]
    public void Frame_LookUp_RightIsPlusX()
    {
        var f = SectionFrame.From(new P2(-50, 50), new P2(300, 50), new P2(100, 80), Rect());
        Assert.NotNull(f);
        Assert.Equal(1, f.Look.Y, 6);
        Assert.Equal(1, f.Right.X, 6);
        Assert.Equal(0, f.Start.X, 6);
        Assert.Equal(200, f.End.X, 6);
    }

    [Fact]
    public void Frame_LookDown_RightIsMinusX()
    {
        var f = SectionFrame.From(new P2(-50, 50), new P2(300, 50), new P2(100, 20), Rect());
        Assert.NotNull(f);
        Assert.Equal(-1, f.Look.Y, 6);
        Assert.Equal(-1, f.Right.X, 6);
        Assert.Equal(200, f.Start.X, 6);
    }

    [Fact]
    public void Frame_Vertical_Flag()
    {
        var v = SectionFrame.From(new P2(100, -10), new P2(100, 200), new P2(150, 50), Rect());
        var h = SectionFrame.From(new P2(-10, 50), new P2(300, 50), new P2(100, 80), Rect());
        Assert.True(v.Vertical);
        Assert.False(h.Vertical);
    }

    [Fact]
    public void Frame_ClipsToRegion()
    {
        var f = SectionFrame.From(new P2(-500, 50), new P2(900, 50), new P2(100, 80), Rect());
        Assert.Equal(200, f.Length, 6);
    }

    [Fact]
    public void Frame_DepthToFarEdge()
    {
        var up = SectionFrame.From(new P2(0, 40), new P2(200, 40), new P2(100, 60), Rect());
        var dn = SectionFrame.From(new P2(0, 40), new P2(200, 40), new P2(100, 10), Rect());
        Assert.Equal(60, up.Depth, 6);
        Assert.Equal(40, dn.Depth, 6);
    }

    [Fact]
    public void Frame_SideOnLine_ReturnsNull()
    {
        Assert.Null(SectionFrame.From(new P2(0, 50), new P2(200, 50), new P2(100, 50.5), Rect()));
    }

    [Fact]
    public void Frame_LineOutsideRegion_ReturnsNull()
    {
        Assert.Null(SectionFrame.From(new P2(-50, 300), new P2(300, 300), new P2(100, 320), Rect()));
    }

    [Fact]
    public void ToSection_LeftEndIsZero()
    {
        var f = SectionFrame.From(new P2(-50, 50), new P2(300, 50), new P2(100, 20), Rect());
        var s = f.ToSection(new P2(150, 50), -7);
        Assert.Equal(50, s.X, 6);
        Assert.Equal(-7, s.Y, 6);
        Assert.Equal(0, f.ToSection(f.Start, 0).X, 6);
    }

    [Fact]
    public void Circle_ClipsAndDepth()
    {
        var c = DetailRegion.FromCircle(new P2(0, 0), 10);
        var f = SectionFrame.From(new P2(-20, 0), new P2(20, 0), new P2(0, 5), c);
        Assert.NotNull(f);
        Assert.Equal(1, f.Right.X, 6);
        Assert.Equal(20, f.Length, 6);
        Assert.Equal(-10, f.Start.X, 6);
        Assert.Equal(10, f.End.X, 6);
        Assert.Equal(10, f.Depth, 6);
    }

    [Fact]
    public void Circle_TangentOrMiss_ReturnsNull()
    {
        var c = DetailRegion.FromCircle(new P2(0, 0), 10);
        Assert.Null(SectionFrame.From(new P2(-20, 10), new P2(20, 10), new P2(0, 15), c));
        Assert.Null(SectionFrame.From(new P2(-20, 15), new P2(20, 15), new P2(0, 20), c));
    }

    [Fact]
    public void Frame_Vertical_SideRight_StartIsLargerY()
    {
        var f = SectionFrame.From(new P2(100, -10), new P2(100, 200), new P2(150, 50), Rect());
        Assert.Equal(0, f.Right.X, 6);
        Assert.Equal(-1, f.Right.Y, 6);
        Assert.Equal(100, f.Start.Y, 6);
        Assert.Equal(0, f.End.Y, 6);
    }

    [Fact]
    public void Snap_TieIsHorizontal()
    {
        var p = SectionFrame.Snap(new P2(0, 0), new P2(10, 10));
        Assert.Equal(10, p.X, 6);
        Assert.Equal(0, p.Y, 6);
    }

    [Fact]
    public void Snap_MostlyVertical()
    {
        var p = SectionFrame.Snap(new P2(10, 10), new P2(15, 110));
        Assert.Equal(10, p.X, 6);
        Assert.Equal(110, p.Y, 6);
    }

    [Fact]
    public void Frame_SideExactlyOneMm_ReturnsNull()
    {
        Assert.Null(SectionFrame.From(new P2(0, 50), new P2(200, 50), new P2(100, 51), Rect()));
    }

    [Fact]
    public void Frame_DrawnDepth_KeptButCappedAtRegion()
    {
        // vùng cao 100, đường cắt y = 50 nhìn lên ⇒ tới biên = 50
        Assert.Equal(50, SectionFrame.From(new P2(-50, 50), new P2(300, 50), new P2(100, 80), Rect()).Depth, 6);
        Assert.Equal(30, SectionFrame.From(new P2(-50, 50), new P2(300, 50), new P2(100, 80), Rect(), 30).Depth, 6);
        Assert.Equal(50, SectionFrame.From(new P2(-50, 50), new P2(300, 50), new P2(100, 80), Rect(), 400).Depth, 6);
    }

    [Fact]
    public void Frame_LengthLimitedToDrawnEnds()
    {
        var f = SectionFrame.From(new P2(20, 50), new P2(120, 50), new P2(100, 80), Rect(), 30);
        Assert.Equal(100, f.Length, 6);
    }

    [Fact]
    public void SectionDepth_DistanceFromCutLine()
    {
        Assert.Equal(30, DetailCommandRules.SectionDepth(new P2(0, 50), new P2(100, 50), new P2(40, 80)), 6);
        Assert.Equal(25, DetailCommandRules.SectionDepth(new P2(10, 0), new P2(10, 100), new P2(-15, 40)), 6);
    }
}
