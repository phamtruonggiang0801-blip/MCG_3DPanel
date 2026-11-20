using System;
using System.Collections.Generic;
using System.Linq;
using MCG_3DPanel.Services.Detail;
using Xunit;

public class SectionCurveRulesTests
{
    static List<P2> L(params (double x, double y)[] p) => p.Select(q => new P2(q.x, q.y)).ToList();

    [Fact]
    public void AdaptiveParams_Line_TwoEndsOnlyAfterSeed()
    {
        var t = SectionCurveRules.AdaptiveParams(0, 1, (a, m, b) => 0, 0.1);
        Assert.Equal(0, t.First(), 9);
        Assert.Equal(1, t.Last(), 9);
        Assert.True(t.Count <= 9);
    }

    [Fact]
    public void AdaptiveParams_Circle_ChordErrorWithinEps()
    {
        double r = 500;
        P2 F(double a) => new P2(r * Math.Cos(a), r * Math.Sin(a));
        double Dev(double a, double m, double b)
        {
            P2 pa = F(a), pb = F(b), pm = F(m);
            return SectionCurveRules.DistanceToSegment(pm, pa, pb);
        }
        var t = SectionCurveRules.AdaptiveParams(0, Math.PI, Dev, 0.1);
        for (int i = 1; i < t.Count; i++)
        {
            double half = (t[i] - t[i - 1]) / 2;
            double sag = r * (1 - Math.Cos(half));
            Assert.True(sag <= 0.1 + 1e-9, $"sag {sag}");
        }
        Assert.True(t.Count < 200);
    }

    [Fact]
    public void Densify_StepBounded()
    {
        var d = SectionCurveRules.Densify(L((0, 0), (10, 0)), 3);
        Assert.Equal(new P2(0, 0).X, d[0].X);
        Assert.Equal(10, d.Last().X, 9);
        for (int i = 1; i < d.Count; i++) Assert.True((d[i] - d[i - 1]).Length <= 3 + 1e-9);
    }

    [Fact]
    public void Covers_PointsNearUnionOfPolylines()
    {
        var polys = new List<IList<P2>> { L((0, 0), (5, 0)), L((5, 0), (10, 0)) };
        Assert.True(SectionCurveRules.Covers(L((0, 0.3), (5, -0.2), (9.9, 0.4)), polys, 0.5));
        Assert.False(SectionCurveRules.Covers(L((0, 0.3), (12, 0)), polys, 0.5));
        Assert.False(SectionCurveRules.Covers(L((0, 0)), new List<IList<P2>>(), 0.5));
    }

    [Fact]
    public void Attribute_PicksCoveringMember_ClosestWins()
    {
        var members = new List<IList<IList<P2>>>
        {
            new List<IList<P2>> { L((0, 0), (100, 0)) },          // 0
            new List<IList<P2>> { L((0, 0.4), (100, 0.4)) },      // 1, gần hơn
            new List<IList<P2>> { L((0, 50), (100, 50)) },        // 2
        };
        Assert.Equal(1, SectionCurveRules.Attribute(L((10, 0.35), (90, 0.4)), members, 0.5));
        Assert.Equal(2, SectionCurveRules.Attribute(L((10, 50), (90, 50.2)), members, 0.5));
        Assert.Equal(-1, SectionCurveRules.Attribute(L((10, 20), (90, 20)), members, 0.5));
        // chỉ phủ 1 phần ⇒ không gán
        Assert.Equal(-1, SectionCurveRules.Attribute(L((10, 0), (150, 0)), members, 0.5));
    }

    [Fact]
    public void ChainLoops_JoinsReversedPiecesIntoClosedLoop()
    {
        var pieces = new List<List<P2>>
        {
            L((0, 0), (10, 0)),
            L((10, 10), (10, 0)),          // ngược chiều
            L((10, 10), (0, 10)),
            L((0, 0), (0, 10)),            // ngược chiều
            L((50, 50), (60, 50)), L((60, 50), (55, 60)), L((55, 60), (50, 50)),
        };
        var loops = SectionCurveRules.ChainLoops(pieces, 0.05, out int open);
        Assert.Equal(0, open);
        Assert.Equal(2, loops.Count);
        var sq = loops.OrderByDescending(l => Math.Abs(SectionCurveRules.SignedArea(l))).First();
        Assert.Equal(100, Math.Abs(SectionCurveRules.SignedArea(sq)), 6);
        // vòng kín: không lặp điểm đầu ở cuối
        Assert.True((sq[0] - sq[sq.Count - 1]).Length > 0.05);
    }

    [Fact]
    public void ChainLoops_OpenChainCounted()
    {
        var loops = SectionCurveRules.ChainLoops(new List<List<P2>> { L((0, 0), (10, 0)), L((10, 0), (10, 10)) }, 0.05, out int open);
        Assert.Equal(1, open);
        Assert.Single(loops);
        Assert.Equal(3, loops[0].Count);
    }

    [Fact]
    public void SignedArea_CcwPositive()
    {
        Assert.Equal(100, SectionCurveRules.SignedArea(L((0, 0), (10, 0), (10, 10), (0, 10))), 9);
        Assert.Equal(-100, SectionCurveRules.SignedArea(L((0, 0), (0, 10), (10, 10), (10, 0))), 9);
    }

    [Fact]
    public void IsParallel_WithinOneDegree_EitherDirection()
    {
        Assert.True(SectionCurveRules.IsParallel(new P2(1, 0), new P2(-1, 0.01), 1));
        Assert.True(SectionCurveRules.IsParallel(new P2(0, 2), new P2(0, -1), 1));
        Assert.False(SectionCurveRules.IsParallel(new P2(1, 0), new P2(1, 0.03), 1));
    }
}
