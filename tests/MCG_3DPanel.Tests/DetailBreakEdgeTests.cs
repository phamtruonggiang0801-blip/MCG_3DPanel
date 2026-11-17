using System;
using System.Collections.Generic;
using System.Linq;
using MCG_3DPanel.Services.Detail;
using Xunit;

/// <summary>Review Task 11: cạnh vết cắt nằm trên biên vùng / trùng break ⇒ AM_4 amzigzag, không vẽ đè AM_0.</summary>
public class DetailBreakEdgeTests
{
    static P2[] Rect(double x0, double y0, double x1, double y1) =>
        new[] { new P2(x0, y0), new P2(x1, y0), new P2(x1, y1), new P2(x0, y1) };

    static DetailRegion Box() => DetailRegion.FromPolyline(
        new List<(double, double, double)> { (0, 0, 0), (1000, 0, 0), (1000, 500, 0), (0, 500, 0) }, true, out _);

    // ─── Khoảng cách tới biên vùng ───

    [Fact]
    public void Distance_Rectangle()
    {
        var r = Box();
        Assert.Equal(0, r.DistanceToBoundary(new P2(1000, 250)), 9);
        Assert.Equal(100, r.DistanceToBoundary(new P2(900, 250)), 9);
        Assert.Equal(50, r.DistanceToBoundary(new P2(500, 550)), 9);
    }

    [Fact]
    public void Distance_Circle()
    {
        var r = DetailRegion.FromCircle(new P2(0, 0), 100);
        Assert.Equal(0, r.DistanceToBoundary(new P2(0, 100)), 9);
        Assert.Equal(40, r.DistanceToBoundary(new P2(60, 0)), 9);
    }

    // ─── Phân loại cạnh ───

    [Fact]
    public void Web_CutByRegionEdge_ThatEdgeIsBreak()
    {
        // web 10 dày chạy x 600..1000, mép phải trùng biên x=1000
        var o = Rect(600, 100, 1000, 110);
        var r = Box();
        var brk = DetailComposeRules.BreakEdges(o, p => r.DistanceToBoundary(p), null, 0.5);
        Assert.Equal(new[] { false, true, false, false }, brk);
    }

    [Fact]
    public void Edge_CoveredByBreakSegment_IsBreak()
    {
        var o = Rect(0, 0, 10, 300);
        var brk = DetailComposeRules.BreakEdges(o, _ => 1e9, new[] { (new P2(-5, 300.2), new P2(20, 300.2)) }, 0.5);
        Assert.Equal(new[] { false, false, true, false }, brk);
    }

    [Fact]
    public void Edge_PartlyCovered_IsNotBreak()
    {
        var o = Rect(0, 0, 10, 300);
        var brk = DetailComposeRules.BreakEdges(o, _ => 1e9, new[] { (new P2(0, 300), new P2(4, 300)) }, 0.5);
        Assert.All(brk, b => Assert.False(b));
    }

    [Fact]
    public void Edge_OnlyEndsOnBoundary_IsNotBreak()
    {
        // cạnh nối 2 biên đối diện (chéo trong vùng) không nằm trên biên
        var o = new[] { new P2(0, 0), new P2(1000, 500), new P2(990, 500) };
        var r = Box();
        var brk = DetailComposeRules.BreakEdges(o, p => r.DistanceToBoundary(p), null, 0.5);
        Assert.False(brk[0]);
    }

    // ─── Chuỗi cạnh ───

    [Fact]
    public void Runs_NoBreak_OneClosedLoop()
    {
        var runs = DetailComposeRules.Runs(Rect(0, 0, 10, 10), new[] { false, false, false, false });
        var run = Assert.Single(runs);
        Assert.True(run.Closed); Assert.False(run.Break); Assert.Equal(4, run.Pts.Count);
    }

    [Fact]
    public void Runs_AllBreak_OneClosedBreakLoop()
    {
        var run = Assert.Single(DetailComposeRules.Runs(Rect(0, 0, 10, 10), new[] { true, true, true, true }));
        Assert.True(run.Closed); Assert.True(run.Break);
    }

    [Fact]
    public void Runs_OneBreakEdge_OpenSolidRunPlusBreak()
    {
        var o = Rect(600, 100, 1000, 110);
        var runs = DetailComposeRules.Runs(o, new[] { false, true, false, false });
        Assert.Equal(2, runs.Count);
        var b = runs.Single(q => q.Break);
        Assert.False(b.Closed);
        Assert.Equal(new[] { new P2(1000, 100), new P2(1000, 110) }, b.Pts);
        var s = runs.Single(q => !q.Break);
        Assert.False(s.Closed);
        // chạy liền vòng qua đỉnh đầu: (1000,110) → (600,110) → (600,100) → (1000,100)
        Assert.Equal(new[] { new P2(1000, 110), new P2(600, 110), new P2(600, 100), new P2(1000, 100) }, s.Pts);
    }

    [Fact]
    public void Runs_TwoBreakEdgesApart_FourRuns()
    {
        var runs = DetailComposeRules.Runs(Rect(0, 0, 10, 10), new[] { true, false, true, false });
        Assert.Equal(4, runs.Count);
        Assert.Equal(2, runs.Count(q => q.Break));
    }

    // ─── Dấu XData ngắn ───

    [Fact]
    public void Marker_Short()
    {
        var plan = new DetailRecord { Kind = DetailKind.Plan, Panel = "7D-16P", Number = 3 };
        var sec = new DetailRecord { Kind = DetailKind.Section, Panel = "7D-16P", Number = 3, Letter = "B" };
        Assert.Equal("Plan 7D-16P DET.3", plan.Marker());
        Assert.Equal("Section 7D-16P DET.3 3B", sec.Marker());
        Assert.True(sec.Marker().Length < 255);
    }
}
