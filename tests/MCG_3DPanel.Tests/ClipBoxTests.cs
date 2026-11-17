using System;
using System.Collections.Generic;
using MCG_3DPanel.Services.Detail;
using Xunit;

public class ClipBoxTests
{
    static DetailRegion Rect() => new DetailRegion
    {
        Corners = new[] { new P2(0, 0), new P2(200, 0), new P2(200, 100), new P2(0, 100) }
    };

    static List<(P2, double)> Pts(params (double x, double y, double z)[] p)
    {
        var l = new List<(P2, double)>();
        foreach (var q in p) l.Add((new P2(q.x, q.y), q.z));
        return l;
    }

    [Fact]
    public void RectFrame_AxisAligned()
    {
        var f = ClipBox.RectFrame(Rect().Corners);
        Assert.Equal(100, f.center.X, 6);
        Assert.Equal(50, f.center.Y, 6);
        Assert.Equal(1, f.u.X, 6);
        Assert.Equal(200, f.lenU, 6);
        Assert.Equal(100, f.lenV, 6);
    }

    [Fact]
    public void RectFrame_ClockwiseInput_NormalisedToCcw()
    {
        var c = Rect().Corners;
        Array.Reverse(c);
        var f = ClipBox.RectFrame(c);
        // v = trái của u ⇒ hệ thuận
        var v = new P2(-f.u.Y, f.u.X);
        Assert.Equal(100, f.center.X, 6);
        Assert.True(f.lenU > 0 && f.lenV > 0);
        Assert.Equal(200 * 100, f.lenU * f.lenV, 6);
        Assert.True(Math.Abs(v.Length - 1) < 1e-9);
    }

    [Fact]
    public void Slab_CornersFromLineAndLook()
    {
        var s = ClipBox.Slab(new P2(0, 50), new P2(200, 50), new P2(0, 1), 50);
        Assert.Equal(4, s.Length);
        Assert.Equal(0, s[0].X, 6); Assert.Equal(50, s[0].Y, 6);
        Assert.Equal(200, s[1].X, 6); Assert.Equal(50, s[1].Y, 6);
        Assert.Equal(200, s[2].X, 6); Assert.Equal(100, s[2].Y, 6);
        Assert.Equal(0, s[3].X, 6); Assert.Equal(100, s[3].Y, 6);
    }

    [Fact]
    public void Plan_TopFaceIsSection_SideFacesAreBoundary()
    {
        var b = ClipBox.Plan(Rect(), -500, -1);
        // cạnh nằm trên mặt bên x=200 (đứng)
        Assert.True(b.OnSideFace(Pts((200.3, 10, -1), (200.3, 10, -300)), 0.5));
        // cạnh nằm trên mặt cắt (Z=-1) bên trong ⇒ không phải biên
        Assert.False(b.OnSideFace(Pts((10, 10, -1), (150, 10, -1)), 0.5));
        // cạnh chéo qua 2 mặt bên ⇒ không phải biên
        Assert.False(b.OnSideFace(Pts((200, 10, -50), (100, 0, -50)), 0.5));
        // mặt đáy vẫn là mặt bên
        Assert.True(b.OnSideFace(Pts((10, 10, -500), (150, 60, -500)), 0.5));
    }

    [Fact]
    public void Plan_Circle_CylinderIsBoundary()
    {
        var b = ClipBox.Plan(DetailRegion.FromCircle(new P2(0, 0), 100), -500, -1);
        Assert.True(b.OnSideFace(Pts((100, 0, -10), (0, 100.2, -10), (-70.71, 70.71, -10)), 0.5));
        Assert.False(b.OnSideFace(Pts((50, 0, -10), (0, 100, -10)), 0.5));
    }

    [Fact]
    public void Section_CutPlaneExcluded_SlabEndsAndBackAreBoundary()
    {
        // vùng 200×100, đường cắt y=50 nhìn +Y, sâu 50
        var b = ClipBox.Section(Rect(), new P2(0, 50), new P2(200, 50), new P2(0, 1), 50, -600, 30);
        Assert.False(b.OnSideFace(Pts((10, 50, -10), (150, 50, -10)), 0.5));   // trên mặt cắt
        Assert.True(b.OnSideFace(Pts((0, 60, -10), (0, 90, -200)), 0.5));      // đầu slab x=0
        Assert.True(b.OnSideFace(Pts((20, 100, -10), (120, 100, -10)), 0.5));  // mặt sau
        Assert.True(b.OnSideFace(Pts((20, 60, 30), (120, 80, 30)), 0.5));      // mặt trên
        Assert.False(b.OnSideFace(Pts((20, 60, -10), (120, 80, -10)), 0.5));
    }

    [Fact]
    public void Section_RegionFaceCoplanarWithCut_Excluded()
    {
        // đường cắt trùng cạnh dưới vùng (y=0)
        var b = ClipBox.Section(Rect(), new P2(0, 0), new P2(200, 0), new P2(0, 1), 100, -600, 30);
        Assert.False(b.OnSideFace(Pts((10, 0, -10), (150, 0, -10)), 0.5));
    }

    [Fact]
    public void Bounds_SectionIntersectsRegionAndSlab()
    {
        var b = ClipBox.Section(Rect(), new P2(0, 50), new P2(200, 50), new P2(0, 1), 50, -600, 30);
        var r = b.Bounds();
        Assert.Equal(0, r.minX, 6); Assert.Equal(200, r.maxX, 6);
        Assert.Equal(50, r.minY, 6); Assert.Equal(100, r.maxY, 6);
        Assert.True(b.Meets(150, 60, -100, 400, 70, -50));
        Assert.False(b.Meets(10, 0, -100, 20, 40, -50));    // phía sau đường cắt
        Assert.False(b.Meets(10, 60, 40, 20, 70, 60));      // trên đỉnh hộp
    }

    [Fact]
    public void Bounds_Circle()
    {
        var b = ClipBox.Plan(DetailRegion.FromCircle(new P2(10, 20), 5), -100, -1);
        var r = b.Bounds();
        Assert.Equal(5, r.minX, 6); Assert.Equal(25, r.maxY, 6);
    }
}
