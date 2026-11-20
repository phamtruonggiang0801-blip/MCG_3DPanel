using System.Collections.Generic;
using MCG_3DPanel.Services.Detail;
using Xunit;

namespace MCG_3DPanel.Tests
{
    /// <summary>Luật "$" theo mặt cắt 7A-7A (kỹ sư 2026-11-18).</summary>
    public class WeldTraceLinesTests
    {
        static IList<P2> L(double x0, double y0, double x1, double y1) => new List<P2> { new P2(x0, y0), new P2(x1, y1) };
        static readonly IList<IList<P2>> NoBlock = new List<IList<P2>>();
        static IEnumerable<(IList<P2>, bool)> C(params IList<P2>[] ls) { foreach (var l in ls) yield return (l, false); }

        [Fact]
        public void EndAgainstCutPlate_Skipped()
        {
            // 9B-9B: đầu tấm song song x = -82.5 trùng mép vết cắt của vách dày 10 (-82.5..-72.5)
            var cut = new List<IList<P2>> { new List<P2> { new P2(-82.5, -180), new P2(-72.5, -180), new P2(-72.5, 194), new P2(-82.5, 194), new P2(-82.5, -180) } };
            Assert.Empty(SectionSymbolRules.WeldTraceLines(C(L(-82.5, -180, -82.5, 194)), cut));
        }

        [Fact]
        public void EndUnderHangingHp_Skipped()
        {
            // 9A-9A: đầu vách x = -611.5 chạy -180 → 74, HP bị cắt treo từ 74 → 194 cùng đường thẳng
            var hp = new List<IList<P2>> { L(-611.5, 74, -611.5, 194), L(-617.5, 194, -617.5, 94) };
            Assert.Empty(SectionSymbolRules.WeldTraceLines(C(L(-611.5, -180, -611.5, 74)), hp));
        }

        [Fact]
        public void HpElsewhere_DoesNotBlock()
        {
            var hp = new List<IList<P2>> { L(-611.5, 74, -611.5, 194) };
            Assert.Single(SectionSymbolRules.WeldTraceLines(C(L(215.9, -180, 215.9, 194)), hp));
        }

        [Fact]
        public void ChamferStartNextToJoint_JointWins()
        {
            // 9B-9B: nét bắt đầu vát 1:4 ở x = 511 (dài nhất) và mối nối 10/6 ở x = 527
            var cands = new (IList<P2>, bool)[] { (L(511, -180, 511, 194), false), (L(527, -170, 527, 194), true) };
            var r = SectionSymbolRules.WeldTraceLines(cands, NoBlock);
            Assert.Single(r);
            Assert.Equal(527, r[0].A.X, 6);
        }

        [Fact]
        public void JointBetweenParallelPlates_Gets()
        {
            var r = SectionSymbolRules.WeldTraceLines(C(L(215.9, 194, 215.9, -180)), NoBlock);
            Assert.Single(r);
            Assert.Equal(7, ((r[0].A + r[0].B) * 0.5).Y, 6);
        }

        [Fact]
        public void EndAtHp_Skipped()
        {
            // đầu vách x = -611.5 trùng cạnh web HP bị cắt
            var hp = new List<IList<P2>> { L(-611.5, -200, -611.5, 194) };
            Assert.Empty(SectionSymbolRules.WeldTraceLines(C(L(-611.5, -180, -611.5, 74)), hp));
        }

        [Fact]
        public void HorizontalLine_Skipped() =>
            Assert.Empty(SectionSymbolRules.WeldTraceLines(C(L(272.9, -200, 671.5, -200)), NoBlock));

        [Fact]
        public void SameJointTwice_OneSymbol()
        {
            var r = SectionSymbolRules.WeldTraceLines(C(L(215.9, 194, 215.9, 20), L(215.5, 10, 215.5, -180)), NoBlock);
            Assert.Single(r);
            Assert.Equal(-180, r[0].A.Y, 6);
        }

        [Fact]
        public void DistinctJoints_EachGets()
        {
            var r = SectionSymbolRules.WeldTraceLines(C(L(100, 194, 100, -180), L(400, 194, 400, -180)), NoBlock);
            Assert.Equal(2, r.Count);
        }

        // Top Plate dưới mặt y = 194, flange đáy mặt trên y = -180 (như 17E-17E)
        static readonly IList<IList<P2>> Supports = new List<IList<P2>> { L(-1000, 194, 1000, 194), L(-1000, -180, 1000, -180) };

        [Fact]
        public void FullHeightLine_Gets() =>
            Assert.Single(SectionSymbolRules.WeldTraceLines(C(L(887, 194, 887, -180)), NoBlock, supports: Supports));

        [Fact]
        public void BracketFreeEdge_Skipped()   // 17E-17E: mép tự do bracket (643,-178)-(658,42), lệch đứng ~4°, đầu trên không tì tấm nào
            => Assert.Empty(SectionSymbolRules.WeldTraceLines(C(L(643, -178, 658, 42)), NoBlock, supports: Supports));

        [Fact]
        public void SlantedBracketEdge_Skipped()   // 16E-16E: (-138,-180)-(-20,59)
            => Assert.Empty(SectionSymbolRules.WeldTraceLines(C(L(-138, -180, -20, 59)), NoBlock, supports: Supports));

        [Fact]
        public void ShortLineUnderHp_Skipped()   // 17G-17G: (329,89)-(329,194) chỉ chạm Top Plate
            => Assert.Empty(SectionSymbolRules.WeldTraceLines(C(L(329, 89, 329, 194)), NoBlock, supports: Supports));

        [Fact]
        public void BrokenFullHeightLine_JoinedAndGets()
        {
            var r = SectionSymbolRules.WeldTraceLines(C(L(215.9, 194, 215.9, 20), L(215.5, 10, 215.5, -180)), NoBlock, supports: Supports);
            Assert.Single(r);
            Assert.Equal(7, ((r[0].A + r[0].B) * 0.5).Y, 0);
        }

        [Fact]
        public void ChamferPair40Apart_OneSymbol()   // 17E-17E: x = -356 / -316 (vát 1:4)
            => Assert.Single(SectionSymbolRules.WeldTraceLines(C(L(-356, -180, -356, 194), L(-316, 194, -316, -180)), NoBlock,
                supports: Supports));

        [Fact]
        public void ShortLine_Skipped() =>
            Assert.Empty(SectionSymbolRules.WeldTraceLines(C(L(272.9, -180, 272.9, -195)), NoBlock));

        [Theory]
        [InlineData("Web", true)]
        [InlineData("Bracket", true)]
        [InlineData("HP", false)]
        [InlineData("FlatBar", false)]
        [InlineData("FacePlate", false)]
        [InlineData("Flange", false)]
        [InlineData("TopPlate", false)]
        public void TakesWeldTrace_ByType(string type, bool expected) =>
            Assert.Equal(expected, SectionSymbolRules.TakesWeldTrace(type));
    }
}
