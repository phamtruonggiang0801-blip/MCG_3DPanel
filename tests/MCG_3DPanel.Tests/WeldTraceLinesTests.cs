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
