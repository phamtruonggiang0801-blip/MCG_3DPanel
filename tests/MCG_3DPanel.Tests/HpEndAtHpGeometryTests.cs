using System;
using MCG_3DPanel.Services.Builders;
using Xunit;

namespace MCG_3DPanel.Tests
{
    /// <summary>Quy cách "HP TO HP END" (CAS.dump.json) — biên dạng dao cắt trong mặt cắt HP chạy.</summary>
    public class HpEndAtHpGeometryTests
    {
        // HP120×6 theo bảng: c=17, r=5 ⇒ dx=99.98889, d=20.01111, p=16
        [Fact]
        public void BulbHeight_Hp120x6_Table() =>
            Assert.Equal(20.0111, HpEndAtHpGeometry.BulbHeight(120, 6, 17, 5), 3);

        [Fact]
        public void BulbHeight_MatchesDumpBlock_R45()   // block HP_120x6_sv: mặt hàn 71.34
            => Assert.InRange(120 - HpEndAtHpGeometry.BulbHeight(120, 6, 17, 4.5) - 30, 71.29, 71.39);

        [Fact]
        public void BulbProtrusion_Hp120x6() => Assert.Equal(16.0, HpEndAtHpGeometry.BulbProtrusion(17, 5, 6), 6);

        [Fact]
        public void CutterOutline_Inside_Hp120()
        {
            var o = HpEndAtHpGeometry.CutterOutline(120, 6, 17, 5, inside: true);
            Assert.NotNull(o);
            Assert.Equal(7, o.Count);
            Assert.Equal(-30.0, o[0].X, 3); Assert.Equal(2.0, o[0].Y, 3);
            Assert.Equal(0.0, o[1].X, 3); Assert.Equal(2.0, o[1].Y, 3);
            Assert.Equal(0.0, o[2].X, 3); Assert.Equal(-69.9889, o[2].Y, 3);
            Assert.Equal(16.0, o[3].X, 3); Assert.Equal(-69.9889, o[3].Y, 3);
            Assert.Equal(-0.41421, o[3].Bulge, 4);
            Assert.Equal(46.0, o[4].X, 3); Assert.Equal(-99.9889, o[4].Y, 3);
            Assert.Equal(46.0, o[5].X, 3); Assert.Equal(-122.0, o[5].Y, 3);
            Assert.Equal(-30.0, o[6].X, 3); Assert.Equal(-122.0, o[6].Y, 3);
            for (int i = 0; i < o.Count; i++) if (i != 3) Assert.Equal(0.0, o[i].Bulge, 9);
        }

        [Fact]
        public void CutterOutline_Outside_Hp120()
        {
            var o = HpEndAtHpGeometry.CutterOutline(120, 6, 17, 5, inside: false);
            Assert.NotNull(o);
            Assert.Equal(6, o.Count);
            Assert.Equal(0.0, o[2].X, 3); Assert.Equal(-69.9889, o[2].Y, 3);
            Assert.Equal(-0.41421, o[2].Bulge, 4);
            Assert.Equal(30.0, o[3].X, 3); Assert.Equal(-99.9889, o[3].Y, 3);
            Assert.Equal(30.0, o[4].X, 3); Assert.Equal(-122.0, o[4].Y, 3);
        }

        [Fact]
        public void CutterOutline_Hp60_And_Hp430_NotDegenerate()
        {
            Assert.NotNull(HpEndAtHpGeometry.CutterOutline(60, 5, 13, 3.5, true));     // mặt hàn ≈ 15.8
            Assert.NotNull(HpEndAtHpGeometry.CutterOutline(430, 14, 62.5, 19.5, true));
        }

        [Fact]
        public void CutterOutline_NoWeldLength_ReturnsNull()
            => Assert.Null(HpEndAtHpGeometry.CutterOutline(40, 5, 13, 3.5, true));

        [Fact]
        public void CutterOutline_InvalidParams_ReturnsNull()
            => Assert.Null(HpEndAtHpGeometry.CutterOutline(120, 6, 5, 5, true));   // c ≤ t

        [Fact]
        public void HalfSpan_Perpendicular_And_Oblique()
        {
            Assert.Equal(17 + 5 + 6 + 10, HpEndAtHpGeometry.HalfSpan(6, 6, 17, 5, true, 0.0), 3);
            Assert.True(HpEndAtHpGeometry.HalfSpan(6, 6, 17, 5, true, 0.5) > 38 / Math.Sin(Math.PI / 3));
        }
    }
}
