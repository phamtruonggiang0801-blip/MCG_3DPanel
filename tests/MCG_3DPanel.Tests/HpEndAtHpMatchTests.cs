using System;
using MCG_3DPanel.Services.Builders;
using Xunit;

namespace MCG_3DPanel.Tests
{
    /// <summary>Nhận diện đầu HP chạm THÂN HP khác. HP chạy: (0,0)→(2000,0), tRun=6, c=17, r=5.</summary>
    public class HpEndAtHpMatchTests
    {
        private static HpEndMatch Match((double, double) end, (double, double) other, (double, double) belly)
            => HpEndAtHpGeometry.Match(end, other, (0, 0), (2000, 0), 6, belly, 17, 5);

        [Fact]
        public void Match_Perpendicular_Inside()   // bụng HP chạy (0,1) quay về HP đầu, đầu mút tại mặt web (y=3)
        {
            var m = Match((1000, 3), (1000, 800), (0, 1));
            Assert.True(m.Ok);
            Assert.True(m.Inside);
            Assert.Equal(1.0, m.Normal.Y, 6);
            Assert.Equal(0.0, m.CosTheta, 6);
            Assert.Equal(1000.0, m.Foot.X, 6); Assert.Equal(0.0, m.Foot.Y, 6);
            Assert.Equal(3.0, m.Dist, 6);
        }

        [Fact]
        public void Match_Perpendicular_Outside()
        {
            var m = Match((1000, 3), (1000, 800), (0, -1));
            Assert.True(m.Ok);
            Assert.False(m.Inside);
        }

        [Fact]
        public void Match_StopsAtBulbTip_Inside_StillTouches()   // y = 3+16+5 = 24 ≤ 3+16+10
            => Assert.True(Match((1000, 24), (1000, 800), (0, 1)).Ok);

        [Fact]
        public void Match_Far_Rejected_Outside()                 // y=20 > 3+0+10
            => Assert.Equal("far", Match((1000, 20), (1000, 800), (0, -1)).Reject);

        [Fact]
        public void Match_EndpointOvershootsCenterline_StillMatchesSameSide()
        {
            var m = Match((1000, -2), (1000, 800), (0, 1));
            Assert.True(m.Ok);
            Assert.Equal(1.0, m.Normal.Y, 6);
        }

        [Fact]
        public void Match_Joint_Rejected()
        {
            // đầu-đầu thẳng hàng (gãy khúc tuyến HP)
            Assert.Equal("joint", Match((2010, 0), (2800, 0), (0, 1)).Reject);
            // vuông góc nhưng HP chạy chỉ thò −1mm qua mặt web HP đầu (góc chữ L khít)
            Assert.Equal("joint", Match((2, 3), (2, 800), (0, 1)).Reject);
        }

        // Vuông góc, chạy thò 7mm qua mặt web (≥ OVERHANG_MIN) ⇒ chữ T sát đầu, KHÔNG phải mối nối (kỹ sư 2026-11-04).
        [Fact]
        public void Match_TNearRunEnd_Overhang7_Ok()
            => Assert.True(Match((10, 3), (10, 800), (0, 1)).Ok);

        // s=1996: chạy chỉ thò 1mm qua mặt web; cách runB 28.3 (> JOINT_TOL), Dist 28 ≤ 3+16+10 ⇒ nearRunEnd
        [Fact]
        public void Match_NearRunEnd_Rejected()
            => Assert.Equal("nearRunEnd", Match((1996, 28), (1996, 800), (0, 1)).Reject);

        [Fact]
        public void Match_Oblique60_Ok_Oblique30_Angle()
        {
            var a60 = Match((1000, 3), (1000 + 800 * Math.Cos(Math.PI / 3), 3 + 800 * Math.Sin(Math.PI / 3)), (0, 1));
            Assert.True(a60.Ok);
            Assert.Equal(0.5, a60.CosTheta, 6);
            var a30 = Match((1000, 3), (1000 + 800 * Math.Cos(Math.PI / 6), 3 + 800 * Math.Sin(Math.PI / 6)), (0, 1));
            Assert.Equal("angle", a30.Reject);
        }

        [Fact]
        public void Match_BothDiagonal_RotatedFrame()   // xoay toàn bộ ca vuông INSIDE 37° — kết quả không đổi
        {
            double a = 37.0 * Math.PI / 180.0, ca = Math.Cos(a), sa = Math.Sin(a);
            (double, double) Rot((double X, double Y) p) => (p.X * ca - p.Y * sa, p.X * sa + p.Y * ca);
            var m = HpEndAtHpGeometry.Match(Rot((1000, 3)), Rot((1000, 800)), Rot((0, 0)), Rot((2000, 0)),
                                            6, Rot((0, 1)), 17, 5);
            Assert.True(m.Ok);
            Assert.True(m.Inside);
            Assert.Equal(0.0, m.CosTheta, 6);
            var n = Rot((0, 1));
            Assert.Equal(n.Item1, m.Normal.X, 6); Assert.Equal(n.Item2, m.Normal.Y, 6);
            Assert.Equal(ca, m.RunAxis.X, 6); Assert.Equal(sa, m.RunAxis.Y, 6);
        }

// Góc chữ L thật: HP chạy dừng NGANG mặt web HP đầu (thò 2mm < OVERHANG_MIN) — không cắt.
        [Fact]
        public void Match_LCorner_RunFlushWithWebFace_Rejected()
        {
            // đường tâm HP đầu ở s = 2000 − 5 (t/2=3 + thò 2) — cách runB 5.4 ⇒ joint
            var m = HpEndAtHpGeometry.Match((1995, 3), (1995, 800), (0, 0), (2000, 0), 6, (0, 1), 17, 5);
            Assert.Equal("joint", m.Reject);
            // cùng góc nhưng xa đầu mút hơn JOINT_TOL theo pháp tuyến (đầu mút dừng ở mép bulb) ⇒ nearRunEnd
            var m2 = HpEndAtHpGeometry.Match((1995, 26), (1995, 800), (0, 0), (2000, 0), 6, (0, 1), 17, 5);
            Assert.Equal("nearRunEnd", m2.Reject);
        }

        // Kỹ sư 2026-11-04: HP chạy thò ~15mm qua MẶT WEB HP đầu là HP end tại HP — kể cả khi bụng HP đầu quay về
        // đầu HP chạy (bulb gần ngang đầu HP chạy). HP200 c=28 r=8 t=10, thò qua mặt web 26 ⇒ cắt.
        [Fact]
        public void Match_BulbFlushWithRunEnd_WebOverhang26_Ok()
        {
            var m = HpEndAtHpGeometry.Match((1969, 5), (1969, 800), (0, 0), (2000, 0), 10, (0, 1), 28, 8);
            Assert.True(m.Ok, m.Reject);
        }

        // Review: đầu HP đầu dừng HỤT mặt web (vẽ tới mép bulb) — Gap = khoảng hở dọc pháp tuyến tới mặt tiếp xúc.
        [Fact]
        public void Match_Gap_SignedFromContactFace()
        {
            Assert.Equal(21.0, Match((1000, 24), (1000, 800), (0, 1)).Gap, 6);
            Assert.Equal(0.0, Match((1000, 3), (1000, 800), (0, 1)).Gap, 6);
            Assert.Equal(-5.0, Match((1000, -2), (1000, 800), (0, 1)).Gap, 6);
        }

        [Fact]
        public void FillLength_Perpendicular_Oblique_None()
        {
            Assert.Equal(21.0 + 2.0, HpEndAtHpGeometry.FillLength(21, 6, 0.0), 6);
            double s = Math.Sin(Math.PI / 3), c = 0.5;
            Assert.Equal(21.0 / s + 3.0 * c / s + 2.0, HpEndAtHpGeometry.FillLength(21, 6, c), 6);
            Assert.Equal(0.0, HpEndAtHpGeometry.FillLength(0.3, 6, 0.0), 6);
            Assert.Equal(0.0, HpEndAtHpGeometry.FillLength(-5, 6, 0.0), 6);
        }

        // Panel 7D-16C (kỹ sư 2026-11-04): khung HP nhỏ — HP chạy 1B640 THÒ 15mm qua mặt lưng HP đầu 1B63E
        // ⇒ là HP end tại HP (cắt R30), KHÔNG phải mối nối đầu-đầu dù đầu mút chỉ cách đầu HP chạy ~18mm.
        // Toạ độ block-local từ CAS.dump.json (7D-16C_Structure). 1B640 bụng +X (về 1B63E).
        [Fact]
        public void Match_RunOverhangs15_Panel7D16C_1B63E_Ok()
        {
            var m = HpEndAtHpGeometry.Match((268.4522, 10325.2785), (626.4522, 10325.2785),
                                            (265.4522, 10307.2785), (265.4522, 10483.4194), 6, (1, 0), 17, 5, tEnd: 6);
            Assert.True(m.Ok, m.Reject);
            Assert.True(m.Inside);
            Assert.Equal(0.0, m.Gap, 4);
        }

        // 1B641 tại 1B5CE (đỉnh, 1B5CE hơi xiên): bụng 1B641 quay VỀ đầu HP chạy, bụng 1B5CE quay RA ⇒ OUTSIDE.
        [Fact]
        public void Match_Panel7D16C_1B641_At_1B5CE_Outside_Ok()
        {
            var m = HpEndAtHpGeometry.Match((60.4492, 10465.4330), (262.4522, 10465.4326),
                                            (57.0034, 38.7449), (57.4502, 10483.433), 6, (-1, 0), 17, 5, tEnd: 6);
            Assert.True(m.Ok, m.Reject);
            Assert.False(m.Inside);
        }

        [Fact]
        public void Match_NoBelly_Rejected()
            => Assert.Equal("noBelly", Match((1000, 3), (1000, 800), (0, 0)).Reject);
    }
}
