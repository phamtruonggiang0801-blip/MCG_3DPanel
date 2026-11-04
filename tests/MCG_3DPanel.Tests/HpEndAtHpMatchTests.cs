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
            => Assert.Equal("joint", Match((10, 3), (10, 800), (0, 1)).Reject);

        // s=1990 > L−20, cách runB 29.7 (> JOINT_TOL), Dist 28 ≤ 3+16+10 ⇒ chỉ lý do nearRunEnd
        [Fact]
        public void Match_NearRunEnd_Rejected()
            => Assert.Equal("nearRunEnd", Match((1990, 28), (1990, 800), (0, 1)).Reject);

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

        [Fact]
        public void Match_NoBelly_Rejected()
            => Assert.Equal("noBelly", Match((1000, 3), (1000, 800), (0, 0)).Reject);
    }
}
