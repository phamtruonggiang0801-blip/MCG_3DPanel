using System;
using MCG_3DPanel.Services.Builders;
using Xunit;

namespace MCG_3DPanel.Tests
{
    /// <summary>Quy cách "HP TO FB END" (CAS.dump.json): mặt hàn h_fb−15 + vát 30° so với đáy HP; FB cao ⇒ R30.</summary>
    public class HpEndAtFbTests
    {
        [Fact]
        public void FbCutterOutline_Fb80_Hp120()
        {
            var o = HpEndAtHpGeometry.FbCutterOutline(120, 80, 6, 17, 5);
            Assert.NotNull(o);
            Assert.Equal(5, o.Count);
            Assert.Equal(-30.0, o[0].X, 3); Assert.Equal(2.0, o[0].Y, 3);
            Assert.Equal(0.0, o[1].X, 3); Assert.Equal(2.0, o[1].Y, 3);
            Assert.Equal(0.0, o[2].X, 3); Assert.Equal(-65.0, o[2].Y, 3);
            Assert.Equal(98.7269, o[3].X, 3); Assert.Equal(-122.0, o[3].Y, 3);
            Assert.Equal(-30.0, o[4].X, 3); Assert.Equal(-122.0, o[4].Y, 3);
            foreach (var v in o) Assert.Equal(0.0, v.Bulge, 9);
        }

        [Fact]
        public void FbCutterOutline_ChamferMatchesDump()   // dump: (574.34,1583.66)→(669.60,1528.66) ⇒ (0,-65)→(95.2628,-120)
        {
            var o = HpEndAtHpGeometry.FbCutterOutline(120, 80, 6, 17, 5);
            double t = (-120.0 - o[2].Y) / (o[3].Y - o[2].Y);
            Assert.Equal(95.2628, o[2].X + t * (o[3].X - o[2].X), 3);
        }

        [Fact]
        public void FbCutterOutline_TallFb_UsesR30Outside()
        {
            var o = HpEndAtHpGeometry.FbCutterOutline(120, 135, 6, 17, 5);
            var e = HpEndAtHpGeometry.CutterOutline(120, 6, 17, 5, inside: false);
            Assert.Equal(e.Count, o.Count);
            for (int i = 0; i < e.Count; i++)
            {
                Assert.Equal(e[i].X, o[i].X, 6); Assert.Equal(e[i].Y, o[i].Y, 6); Assert.Equal(e[i].Bulge, o[i].Bulge, 6);
            }
        }

        // Review: FB cao + dày ≥ c (HP80 c=14 r=4, FB 100×15) — guard c ≤ tRun của HP chạy không áp cho FB (p=0).
        [Fact]
        public void FbCutterOutline_TallThickFb_NotNull()
        {
            var o = HpEndAtHpGeometry.FbCutterOutline(80, 100, 15, 14, 4);
            Assert.NotNull(o);
            Assert.Equal(6, o.Count);   // biên dạng R30 OUTSIDE
        }

        [Fact]
        public void FbCutterOutline_TooShortFb_Null()
            => Assert.Null(HpEndAtHpGeometry.FbCutterOutline(120, 15, 6, 17, 5));

        [Fact]
        public void HalfSpanX_UsesCutterReach()
        {
            Assert.Equal(38.0, HpEndAtHpGeometry.HalfSpanX(6, 17, 5, 98.7269, 0.0), 3);
            double s = Math.Sin(Math.PI / 3);
            Assert.Equal(28 / s + 98.7269 * 0.5 / s + 10, HpEndAtHpGeometry.HalfSpanX(6, 17, 5, 98.7269, 0.5), 3);
        }

        [Fact]
        public void HalfSpan_EqualsHalfSpanXWithPRW()
            => Assert.Equal(HpEndAtHpGeometry.HalfSpanX(6, 17, 5, 16 + 30 + 30, 0.5),
                            HpEndAtHpGeometry.HalfSpan(6, 6, 17, 5, true, 0.5), 6);

        [Fact]
        public void Match_Fb_NoBellyNeeded_Ok()
        {
            var m = HpEndAtHpGeometry.Match((1000, 3), (1000, 800), (0, 0), (2000, 0), 6, (0, 0), 17, 5, tEnd: 6, runIsFb: true);
            Assert.True(m.Ok, m.Reject);
            Assert.False(m.Inside);
        }

        [Fact]
        public void Match_Fb_FarUsesNoBulb()   // 15 > 3 + 0 + 10
            => Assert.Equal("far", HpEndAtHpGeometry.Match((1000, 15), (1000, 800), (0, 0), (2000, 0), 6, (0, 1), 17, 5,
                                                           tEnd: 6, runIsFb: true).Reject);
    }
}
