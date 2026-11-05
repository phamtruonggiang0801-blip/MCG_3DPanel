using System;
using MCG_3DPanel.Services.Builders;
using Xunit;

namespace MCG_3DPanel.Tests
{
    /// <summary>Vát 1:4 mối nối web khác chiều dày (CAS.dump.json; kỹ sư 2026-11-05) — số thật panel 7D-16C.</summary>
    public class WebThicknessChamferTests
    {
        private static WebSeg S(double ax, double ay, double bx, double by, double t)
            => new WebSeg { Ax = ax, Ay = ay, Bx = bx, By = by, T = t };

        // 26965 (t10, X 13623.8–13973.8, Y 988.8–998.8) ↔ 26964 (t6, X 2583.8–13623.8, Y 992.8–998.8): flush mặt trên.
        private static readonly WebSeg W26965 = S(13623.8, 993.8, 13973.8, 993.8, 10);
        private static readonly WebSeg W26964 = S(2583.8, 995.8, 13623.8, 995.8, 6);

        [Fact]
        public void Match_HorizontalFlushTop()
        {
            var j = WebThicknessChamferGeometry.Match(W26965, W26964);
            Assert.NotNull(j);
            Assert.Equal(0, j.Thick);
            Assert.Equal(1, j.Thin);
            Assert.Equal(4.0, j.Dt, 6);
            Assert.Equal(16.0, j.L, 6);
            Assert.Equal(13623.8, j.Jx, 6);
            Assert.Equal(993.8, j.Jy, 6);
            Assert.Equal(1.0, j.Ux, 6);
            Assert.Equal(0.0, j.StepPlus, 6);   // +N = +Y: mặt 998.8 flush
            Assert.Equal(4.0, j.StepMinus, 6);  // −N: 992.8 vs 988.8
        }

        // 26976 (t10, X 2273.8–2283.8, Y 9268.9–9668.9) ↔ 269A0 (Web + GE-HP t20, X 2263.8–2283.8, Y 9668.9–10868.9).
        [Fact]
        public void Match_VerticalT20T10()
        {
            var j = WebThicknessChamferGeometry.Match(S(2278.8, 9268.9, 2278.8, 9668.9, 10), S(2273.8, 9668.9, 2273.8, 10868.9, 20));
            Assert.NotNull(j);
            Assert.Equal(1, j.Thick);
            Assert.Equal(40.0, j.L, 6);
            Assert.Equal(0.0, j.Ux, 6); Assert.Equal(1.0, j.Uy, 6);
            Assert.Equal(-1.0, j.Nx, 6); Assert.Equal(0.0, j.Ny, 6);
            Assert.Equal(10.0, j.StepPlus, 6);  // +N = −X: mặt ngoài 2263.8 vs 2273.8
            Assert.Equal(0.0, j.StepMinus, 6);  // mặt trong 2283.8 flush
        }

        [Fact]
        public void Match_Centred_StepsHalf()
        {
            var j = WebThicknessChamferGeometry.Match(S(0, 0, 100, 0, 10), S(100, 0, 300, 0, 6));
            Assert.Equal(2.0, j.StepPlus, 6);
            Assert.Equal(2.0, j.StepMinus, 6);
            Assert.Equal(16.0, j.L, 6);
        }

        [Fact] public void Match_DtBelow4_Null() => Assert.Null(WebThicknessChamferGeometry.Match(S(0, 0, 100, 0, 8), S(100, 0, 300, 0, 6)));
        [Fact] public void Match_SameThickness_Null() => Assert.Null(WebThicknessChamferGeometry.Match(S(0, 0, 100, 0, 10), S(100, 0, 300, 0, 10)));
        [Fact] public void Match_Perpendicular_Null() => Assert.Null(WebThicknessChamferGeometry.Match(S(0, 0, 100, 0, 10), S(100, 0, 100, 200, 6)));
        [Fact] public void Match_Gap10_Null() => Assert.Null(WebThicknessChamferGeometry.Match(S(0, 0, 100, 0, 10), S(110, 0, 300, 0, 6)));
        [Fact] public void Match_SideBySide_Null() => Assert.Null(WebThicknessChamferGeometry.Match(S(0, 0, 100, 0, 10), S(0, 8, 100, 8, 6)));
        [Fact] public void Match_LateralOffsetTooBig_Null() => Assert.Null(WebThicknessChamferGeometry.Match(S(0, 0, 100, 0, 10), S(100, 20, 300, 20, 6)));

        [Fact]
        public void Match_UsesNearestEnds()   // tấm dày vẽ ngược chiều: mối nối vẫn ở đầu chạm tấm mỏng
        {
            var j = WebThicknessChamferGeometry.Match(S(300, 0, 100, 0, 10), S(0, 0, 100, 0, 6));
            Assert.Equal(100.0, j.Jx, 6);
            Assert.Equal(1.0, j.Ux, 6);   // từ mối nối VÀO tấm dày (về phía x=300)
        }

        [Fact]
        public void Wedge_FlushTop_Points()
        {
            var j = WebThicknessChamferGeometry.Match(W26965, W26964);
            var w = WebThicknessChamferGeometry.Wedge(j, -1, 1.0);
            Assert.Equal(4, w.Count);
            // Đường vát đi qua (mối nối, mặt tấm mỏng 992.8) và (L=16 vào tấm dày, mặt tấm dày 988.8).
            Assert.Equal(13639.8, w[1].X, 6); Assert.Equal(988.8, w[1].Y, 6);
            double k = (w[1].Y - w[0].Y) / (w[1].X - w[0].X);
            Assert.Equal(992.8, w[0].Y + k * (13623.8 - w[0].X), 6);
            Assert.True(w[2].Y < 988.8 && w[3].Y < 988.8);   // phần đệm nằm NGOÀI mặt tấm dày
        }

        [Fact]
        public void Wedge_Rotated45()
        {
            double c = Math.Sqrt(0.5);
            (double, double) R(double x, double y) => (x * c - y * c, x * c + y * c);
            var (a1x, a1y) = R(0, 0); var (b1x, b1y) = R(100, 0);
            var (a2x, a2y) = R(100, 2); var (b2x, b2y) = R(300, 2);
            var j = WebThicknessChamferGeometry.Match(S(a1x, a1y, b1x, b1y, 10), S(a2x, a2y, b2x, b2y, 6));
            Assert.NotNull(j);
            Assert.Equal(4.0, j.StepPlus, 6);   // U = −x (vào tấm dày) ⇒ N = −y: mặt lệch y = −5 là +N
            var w = WebThicknessChamferGeometry.Wedge(j, +1, 1.0);
            var (ex, ey) = R(100 - 16, -5);   // mút vát trên mặt tấm dày (dày nằm phía x<100 sau khi xoay)
            Assert.Equal(ex, w[1].X, 6); Assert.Equal(ey, w[1].Y, 6);
        }
    }
}
