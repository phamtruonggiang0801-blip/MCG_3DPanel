using MCG_3DPanel.Commands;
using Xunit;

namespace MCG_3DPanel.Tests
{
    /// <summary>Quét chọn trong Pick (kỹ sư 2026-11-05): Window (trái→phải) = nằm TRỌN trong khung; Crossing (phải→trái) =
    /// CHẠM khung. So theo hộp bao đã chiếu lên mặt phẳng nhìn.</summary>
    public class PickWindowTests
    {
        // Khung (400,400)-(500,500) nằm giữa top plate 0..1000: Crossing KHÔNG chọn tấm vì không cạnh nào vào khung (hộp bao thì có).
        private static readonly (double, double, double, double)[] Plate =
            { (0, 0, 1000, 0), (1000, 0, 1000, 1000), (1000, 1000, 0, 1000), (0, 1000, 0, 0) };

        [Fact] public void Edges_Crossing_WindowInsidePlate_NoHit() => Assert.False(PickWindow.HitEdges(500, 500, 400, 400, true, Plate));
        [Fact] public void Edges_Crossing_EdgeThroughWindow_Hit() => Assert.True(PickWindow.HitEdges(450, 500, 400, 400, true, new[] { (0.0, 450.0, 1000.0, 450.0) }));
        [Fact] public void Edges_Crossing_EdgeInsideWindow_Hit() => Assert.True(PickWindow.HitEdges(500, 500, 400, 400, true, new[] { (420.0, 420.0, 480.0, 480.0) }));

        [Fact] public void Edges_Crossing_DiagonalMissesCorner_NoHit()
        {
            // Flange xiên: hộp bao 0..1000 chứa khung, đoạn thật đi qua góc khác.
            Assert.False(PickWindow.HitEdges(900, 200, 800, 100, true, new[] { (0.0, 0.0, 1000.0, 1000.0) }));
        }

        [Fact] public void Edges_Window_AllInside_Hit() => Assert.True(PickWindow.HitEdges(400, 400, 500, 500, false, new[] { (420.0, 420.0, 480.0, 480.0) }));
        [Fact] public void Edges_Window_OneEndOutside_NoHit() => Assert.False(PickWindow.HitEdges(400, 400, 500, 500, false, new[] { (420.0, 420.0, 480.0, 480.0), (480.0, 480.0, 600.0, 480.0) }));
        [Fact] public void Edges_Empty_NoHit() => Assert.False(PickWindow.HitEdges(0, 0, 10, 10, true, new (double, double, double, double)[0]));

        [Fact]
        public void IsCrossing_RightToLeft()
        {
            Assert.False(PickWindow.IsCrossing(0, 100));
            Assert.True(PickWindow.IsCrossing(100, 0));
        }

        [Theory]
        [InlineData(10, 10, 20, 20, true)]     // trọn trong
        [InlineData(-5, 10, 20, 20, false)]    // lòi ra trái
        [InlineData(10, 10, 120, 20, false)]   // lòi ra phải
        [InlineData(200, 200, 300, 300, false)]
        public void Window_FullyInside(double x0, double y0, double x1, double y1, bool hit)
            => Assert.Equal(hit, PickWindow.Hit(0, 0, 100, 100, crossing: false, x0, y0, x1, y1));

        [Theory]
        [InlineData(10, 10, 20, 20, true)]
        [InlineData(-5, 10, 20, 20, true)]     // chạm
        [InlineData(-50, -50, 200, 200, true)] // bao trùm khung
        [InlineData(200, 200, 300, 300, false)]
        [InlineData(101, 0, 150, 50, false)]   // sát ngoài
        public void Crossing_Touches(double x0, double y0, double x1, double y1, bool hit)
            => Assert.Equal(hit, PickWindow.Hit(0, 0, 100, 100, crossing: true, x0, y0, x1, y1));

        [Fact]
        public void Window_CornersInAnyOrder()
        {
            Assert.True(PickWindow.Hit(100, 100, 0, 0, crossing: false, 10, 10, 20, 20));
            Assert.True(PickWindow.Hit(0, 100, 100, 0, crossing: true, 90, 90, 150, 150));
        }
    }
}
