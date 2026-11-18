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
        public void WeldTrace_Hidden_Null() =>
            Assert.Null(SectionSymbolRules.WeldTracePlacement(new ParallelPlate { Visible = false, OutlineA = new P2(0, 0), OutlineB = new P2(0, -10) }));

        [Fact]
        public void WeldTrace_MidHeight()
        {
            var p = new ParallelPlate { Type = "Web", Visible = true, OutlineA = new P2(887.4, 0), OutlineB = new P2(887.4, -374) };
            var at = SectionSymbolRules.WeldTracePlacement(p);
            Assert.NotNull(at);
            Assert.Equal(-187, at.Value.Y, 6);
            Assert.Equal(887.4, at.Value.X, 6);   // tâm chữ trên nét (bản vẽ tay)
        }

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
