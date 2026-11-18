using MCG_3DPanel.Services.Detail;
using Xunit;

namespace MCG_3DPanel.Tests
{
    public class SectionSymbolRulesTests
    {
        static CutMember M(string type, double min, double max) => new CutMember
        {
            Type = type, MinSigned = min, MaxSigned = max, Thickness = 10,
            CutCenter = new P2(5, 6), CutAxis = new P2(0, 1)
        };

        [Fact] public void Continuous_BothSides_True() => Assert.True(SectionSymbolRules.IsContinuous(M("Web", -50, 50)));
        [Fact] public void Continuous_EndsAtPlane_False() => Assert.False(SectionSymbolRules.IsContinuous(M("Web", -0.3, 50)));
        [Fact] public void Continuous_Hp_False() => Assert.False(SectionSymbolRules.IsContinuous(M("HP", -50, 50)));

        [Theory]
        [InlineData(10, "(10.0)")]
        [InlineData(6, "(6.0)")]
        [InlineData(12.5, "(12.5)")]
        public void ThicknessText_Parenthesised(double t, string s) => Assert.Equal(s, SectionSymbolRules.ThicknessText(t));

        [Fact]
        public void ContinuousPlacement_AtCenter_RotatedPerpendicular()
        {
            var (at, rot) = SectionSymbolRules.ContinuousPlacement(M("Web", -5, 5));
            Assert.Equal(5, at.X, 6); Assert.Equal(6, at.Y, 6);
            Assert.Equal(System.Math.PI, rot, 6); // atan2(1,0)+pi/2
        }

        [Fact]
        public void WeldTrace_MidHeight()
        {
            var at = SectionSymbolRules.WeldTracePlacement(new P2(887.4, 0), new P2(887.4, -374));
            Assert.Equal(-187, at.Y, 6);
            Assert.Equal(887.4, at.X, 6);   // tâm chữ trên nét (bản vẽ tay)
        }

        [Theory]
        [InlineData(374, 10, true)]    // vách đứng nhìn mặt
        [InlineData(20, 200, false)]   // face plate đáy nằm ngang
        [InlineData(6, 1343, false)]   // tấm boong
        public void UprightPlate(double faceH, double t, bool expected) =>
            Assert.Equal(expected, SectionSymbolRules.IsUprightPlate(faceH, t));

        [Theory]
        [InlineData(0, -180, 0, 74, true)]       // nét đầu / nét nối đứng
        [InlineData(0, 0, 100, 100, true)]       // nét xiên 45°
        [InlineData(272.9, -200, 671.5, -200, false)]   // mép dưới face plate
        [InlineData(0, 0, 100, 20, false)]       // gần ngang (11°)
        public void MarkLine(double ax, double ay, double bx, double by, bool expected) =>
            Assert.Equal(expected, SectionSymbolRules.IsMarkLine(new P2(ax, ay), new P2(bx, by)));

        [Fact] public void Upright_Vertical_True() => Assert.True(SectionSymbolRules.IsUpright(M("Web", -50, 50)));

        [Fact]
        public void Upright_Horizontal_False()
        {
            var m = M("FacePlate", -50, 50); m.CutAxis = new P2(1, 0);
            Assert.False(SectionSymbolRules.IsUpright(m));
        }

        [Fact]
        public void ContinuousPlacement_AxisDown_Still180()
        {
            var m = M("Web", -5, 5); m.CutAxis = new P2(0, -1);
            Assert.Equal(System.Math.PI, SectionSymbolRules.ContinuousPlacement(m).rotation, 6);
        }
    }
}
