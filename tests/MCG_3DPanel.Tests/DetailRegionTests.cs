using System;
using System.Collections.Generic;
using MCG_3DPanel.Services.Detail;
using Xunit;

public class DetailRegionTests
{
    static List<(double x, double y, double bulge)> V(params (double, double)[] p)
    {
        var l = new List<(double, double, double)>();
        foreach (var q in p) l.Add((q.Item1, q.Item2, 0));
        return l;
    }

    [Fact]
    public void Region_FromPolyline_Rotated_Accepted()
    {
        double a = 25 * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        (double, double) R(double x, double y) => (x * c - y * s, x * s + y * c);
        var r = DetailRegion.FromPolyline(V(R(0, 0), R(200, 0), R(200, 100), R(0, 100)), true, out var why);
        Assert.NotNull(r);
        Assert.Equal(4, r.Corners.Length);
        Assert.True(r.Contains(new P2(R(100, 50).Item1, R(100, 50).Item2)));
        Assert.False(r.Contains(new P2(R(250, 50).Item1, R(250, 50).Item2)));
    }

    [Fact]
    public void Region_FromPolyline_Cw_StoredCcw()
    {
        var r = DetailRegion.FromPolyline(V((0, 0), (0, 10), (20, 10), (20, 0), (0, 0)), false, out _);
        Assert.NotNull(r);
        double a = 0;
        for (int i = 0; i < 4; i++) { var p = r.Corners[i]; var q = r.Corners[(i + 1) % 4]; a += p.X * q.Y - q.X * p.Y; }
        Assert.True(a > 0);
    }

    [Fact]
    public void Region_FromPolyline_Open_Refused()
    {
        var r = DetailRegion.FromPolyline(V((0, 0), (10, 0), (10, 5), (0, 5)), false, out var why);
        Assert.Null(r); Assert.Equal("not closed", why);
    }

    [Fact]
    public void Region_FromPolyline_Bulge_Refused()
    {
        var v = V((0, 0), (10, 0), (10, 5), (0, 5));
        v[1] = (10, 0, 0.5);
        var r = DetailRegion.FromPolyline(v, true, out var why);
        Assert.Null(r); Assert.Equal("has arcs", why);
    }

    [Fact]
    public void Region_FromPolyline_Trapezoid_Refused()
    {
        var r = DetailRegion.FromPolyline(V((0, 0), (10, 0), (8, 5), (2, 5)), true, out var why);
        Assert.Null(r); Assert.Equal("not a rectangle", why);
    }

    [Fact]
    public void Region_Circle_Contains()
    {
        var r = DetailRegion.FromCircle(new P2(5, 5), 10);
        Assert.True(r.IsCircle);
        Assert.True(r.Contains(new P2(5, 15)));
        Assert.True(r.Contains(new P2(8, 8)));
        Assert.False(r.Contains(new P2(16, 5)));
        Assert.Equal(72, r.Outline().Length);
    }
}
