using System.Collections.Generic;
using MCG_3DPanel.Services.Builders;
using Xunit;

namespace MCG_3DPanel.Tests
{
    /// <summary>Tripping Bracket (CAS.dump.json, kỹ sư 2026-11-04) + polygon bracket B dùng chung.</summary>
    public class TrippingBracketRulesTests
    {
        [Fact]
        public void Segments_SplitAtCuts()
        {
            var s = TrippingBracketRules.Segments(new List<double> { 0, 9605, 11311 });
            Assert.Equal(2, s.Count);
            Assert.Equal((0.0, 9605.0), s[0]);
            Assert.Equal((9605.0, 11311.0), s[1]);
        }

        [Fact]
        public void Segments_RequireCutAtBothEnds()
            => Assert.Empty(TrippingBracketRules.Segments(new List<double> { 500 }));

        [Fact]
        public void Segments_DedupesNearCuts()   // 2 cut cách ≤ 1mm (2 mặt web CG gộp) ⇒ 1
            => Assert.Single(TrippingBracketRules.Segments(new List<double> { 1000.4, 0, 1000 }));

        [Fact]
        public void PickHp_DumpExample()
            => Assert.Equal(2, TrippingBracketRules.PickHp(new List<double> { 633.6, 833.6, 1043.6, 1273.6 }, 959.6, 1));

        [Fact]
        public void PickHp_LessThan3_Null()
            => Assert.Null(TrippingBracketRules.PickHp(new List<double> { 100, 200 }, 150, 1));

        [Fact]
        public void PickHp_Tie_FollowsDoz()
        {
            var s = new List<double> { 100, 300, 500, 700 };
            Assert.Equal(2, TrippingBracketRules.PickHp(s, 400, +1));
            Assert.Equal(1, TrippingBracketRules.PickHp(s, 400, -1));
        }

        [Fact]
        public void Side_DozAndFlip()
        {
            Assert.Equal(1, TrippingBracketRules.Side(0.5, false));
            Assert.Equal(-1, TrippingBracketRules.Side(-0.5, false));
            Assert.Equal(-1, TrippingBracketRules.Side(0.5, true));
        }

        [Fact]
        public void SelectBracketB_Overhang()
        {
            Assert.Equal((false, 15.0, 30.0), TrippingBracketRules.SelectBracketB(37, true, 210));
            Assert.Equal((true, 49.0, 0.0), TrippingBracketRules.SelectBracketB(64, true, 210));
            Assert.Equal((true, 195.0, 0.0), TrippingBracketRules.SelectBracketB(300, true, 210));
            Assert.Equal((false, 15.0, 30.0), TrippingBracketRules.SelectBracketB(80, false, 210));
        }

        [Fact]
        public void BracketBPolygon_MatchesLegacyOrder()
        {
            var p = TrippingBracketRules.BracketBPolygon(210, 254, 15, 30);
            var expect = new (double X, double Y, double B)[]
            {
                (210, 239, 0), (15, 30, 0), (0, 30, 0), (0, 204, 0.41421356), (50, 254, 0), (210, 254, 0),
            };
            Assert.Equal(expect.Length, p.Count);
            for (int i = 0; i < expect.Length; i++)
            {
                Assert.Equal(expect[i].X, p[i].X, 6); Assert.Equal(expect[i].Y, p[i].Y, 6); Assert.Equal(expect[i].B, p[i].Bulge, 8);
            }
        }
    }
}
