using System.Collections.Generic;
using MCG_3DPanel.Services.Detail;
using Xunit;

namespace MCG_3DPanel.Tests
{
    public class OcclusionRulesTests
    {
        static IList<P3> Seam(params double[] xyz)
        {
            var l = new List<P3>();
            for (int i = 0; i < xyz.Length; i += 3) l.Add(new P3(xyz[i], xyz[i + 1], xyz[i + 2]));
            return l;
        }

        [Fact]
        public void SeamNearness_TakesNearestParallelSeam()
        {
            // nét ngang y=83.3: một cạnh phía sau (Z=-100), một cạnh phía trước (Z=-10) cùng hình chiếu
            var seams = new[] { Seam(0, 83.3, -100, 500, 83.3, -100), Seam(0, 83.5, -10, 500, 83.5, -10) };
            Assert.Equal(-10, OcclusionRules.SeamNearness(new P2(200, 83.3), new P2(1, 0), seams, 0.5, 10));
        }

        [Fact]
        public void SeamNearness_IgnoresCrossingSeam()
        {
            // cạnh đứng của vách phía trước cắt ngang nét HP ⇒ không phải độ sâu của nét HP
            var seams = new[] { Seam(200, -180, -72, 200, 194, -72), Seam(0, 83.3, -150, 500, 83.3, -150) };
            Assert.Equal(-150, OcclusionRules.SeamNearness(new P2(200, 83.3), new P2(1, 0), seams, 0.5, 10));
        }

        [Fact]
        public void SeamNearness_InterpolatesSlopedSeam()
        {
            var seams = new[] { Seam(0, 0, 0, 100, 0, -100) };
            Assert.Equal(-25, OcclusionRules.SeamNearness(new P2(25, 0.2), new P2(1, 0), seams, 0.5, 10)!.Value, 6);
        }

        [Fact]
        public void SeamNearness_NoMatch_Null()
        {
            var seams = new[] { Seam(0, 10, 0, 100, 10, 0), Seam(5, 5, 0, 5, 5, -50) };   // xa + chiếu thành điểm
            Assert.Null(OcclusionRules.SeamNearness(new P2(5, 5), new P2(1, 0), seams, 0.5, 10));
        }

        [Fact]
        public void At_WalksPolyline()
        {
            var poly = new List<P2> { new P2(0, 0), new P2(10, 0), new P2(10, 10) };
            var (p, d) = OcclusionRules.At(poly, 15);
            Assert.Equal(10, p.X, 9); Assert.Equal(5, p.Y, 9);
            Assert.Equal(0, d.X, 9); Assert.Equal(1, d.Y, 9);
            Assert.Equal(20, OcclusionRules.Length(poly), 9);
        }

        [Fact]
        public void VisibleSpans_AllVisible_WholeCurve()
        {
            var r = OcclusionRules.VisibleSpans(100, s => true, 25, 0.5, 8, 1);
            Assert.Equal(new[] { (0.0, 100.0) }, r);
        }

        [Fact]
        public void VisibleSpans_AllHidden_Empty() =>
            Assert.Empty(OcclusionRules.VisibleSpans(100, s => false, 25, 0.5, 8, 1));

        [Fact]
        public void VisibleSpans_HiddenMiddle_SplitsNearBoundaries()
        {
            // khuất trong [30, 70] (vd một tấm phía trước che giữa nét)
            var r = OcclusionRules.VisibleSpans(100, s => s < 30 || s > 70, 25, 0.5, 10, 1);
            Assert.Equal(2, r.Count);
            Assert.Equal(0, r[0].S0, 9);
            Assert.Equal(30, r[0].S1, 1);
            Assert.Equal(70, r[1].S0, 1);
            Assert.Equal(100, r[1].S1, 9);
        }

        [Fact]
        public void VisibleSpans_ShortCurve_OneMidSample()
        {
            int calls = 0;
            var r = OcclusionRules.VisibleSpans(0.8, s => { calls++; return true; }, 25, 0.5, 8, 0.1);
            Assert.Equal(1, calls);
            Assert.Single(r);
        }

        [Fact]
        public void VisibleSpans_DropsTinyPieces()
        {
            var r = OcclusionRules.VisibleSpans(100, s => s > 99.6, 25, 0.1, 12, 1);
            Assert.Empty(r);
        }
    }
}
