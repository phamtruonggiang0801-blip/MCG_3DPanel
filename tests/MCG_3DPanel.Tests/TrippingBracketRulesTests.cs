using System.Collections.Generic;
using System.Linq;
using MCG_3DPanel.Services.Builders;
using Xunit;

namespace MCG_3DPanel.Tests
{
    /// <summary>Tripping Bracket (kỹ sư 2026-11-05: khoảng cách gối ≤ 3 × bước stiffener max) + polygon bracket B dùng chung.</summary>
    public class TrippingBracketRulesTests
    {
        // Panel 7D-16C, vùng dưới (giữa girder Y≈995 và Y≈10607): tâm HP theo Y.
        private static readonly double[] Lower =
            { 1457.6, 2232.6, 3007.6, 3782.6, 4557.6, 5332.6, 6107.6, 6876.6, 7651.6, 8385.6, 9160.6, 9935.6 };

        private static double[] Picked(IReadOnlyList<double> s, double lo, double hi, int sign, double? split)
            => TrippingBracketRules.PickBySpacing(s, lo, hi, 2325, sign, split).Select(i => s[i]).ToArray();

        [Fact] public void Pick_Starboard_7D16C()
            => Assert.Equal(new[] { 3007.6, 5332.6, 7651.6, 9935.6 }, Picked(Lower, 995, 10607, +1, null));
        [Fact] public void Pick_Port_7D16C()
            => Assert.Equal(new[] { 1457.6, 3782.6, 6107.6, 8385.6 }, Picked(Lower, 995, 10607, -1, null));
        [Fact] public void Pick_Center_7D16C()
            => Assert.Equal(new[] { 3007.6, 5332.6, 7651.6, 8385.6 }, Picked(Lower, 995, 10607, +1, 7678));
        [Fact] public void Pick_Center_UpperBeam10046()
            => Assert.Equal(new[] { 11004.0 }, Picked(new[] { 11004.0, 11485.6, 12260.6, 13035.6 }, 10607, 13088, -1, 7678));
        [Fact] public void Pick_Center_UpperBeam11986()
            => Assert.Equal(new[] { 12260.6 }, Picked(new[] { 11004.0, 11485.6, 12260.6, 13035.6, 13800.6 }, 10607, 13940, -1, 7678));
        [Fact] public void Pick_SegmentNotLongerThanL_None()
            => Assert.Empty(Picked(new[] { 11004.0, 11485.6 }, 10607, 12241, +1, null));
        [Fact] public void Pick_NoHpWithinL_TakesNearestNext()
            => Assert.Equal(new[] { 3000.0 }, Picked(new[] { 3000.0 }, 0, 4000, +1, null));
        [Fact] public void Pick_Center_HpAtSplit_GoesToLowerHalf()
            => Assert.Equal(new[] { 2000.0, 5000.0 }, Picked(new[] { 2000.0, 5000.0 }, 0, 7000, +1, 5000));

        [Fact]
        public void MaxSpacing_TakesPanelMax()
            => Assert.Equal(775.0, TrippingBracketRules.MaxSpacing(new[] { new[] { 1.0, 300.0 }, new[] { 3007.6, 3782.6, 4557.6 } }), 6);

        [Fact]
        public void Segments_RunEndsAreSupports()
            => Assert.Equal(new[] { (0.0, 847.0), (847.0, 10459.0), (10459.0, 11300.0) },
                TrippingBracketRules.Segments(new List<double> { 847, 10459 }, 11300, 20));

        [Fact]
        public void Segments_RunEndNearCut_NotDuplicated()
            => Assert.Equal(new[] { (-5.0, 847.0), (847.0, 1000.0) },
                TrippingBracketRules.Segments(new List<double> { -5, 847 }, 1000, 20));

        [Fact]
        public void Segments_SplitAtCuts()
        {
            var s = TrippingBracketRules.Segments(new List<double> { 0, 9605, 11311 }, 11311, 20);
            Assert.Equal(2, s.Count);
            Assert.Equal((0.0, 9605.0), s[0]);
            Assert.Equal((9605.0, 11311.0), s[1]);
        }

        [Fact]
        public void Segments_SingleCut_SplitsRun()
            => Assert.Equal(new[] { (0.0, 500.0), (500.0, 1000.0) }, TrippingBracketRules.Segments(new List<double> { 500 }, 1000, 20));

        [Fact]
        public void Segments_DedupesNearCuts()   // 2 cut cách ≤ 1mm (2 mặt web CG gộp) ⇒ 1
            => Assert.Single(TrippingBracketRules.Segments(new List<double> { 1000.4, 0, 1000 }, 1000.4, 20));

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
