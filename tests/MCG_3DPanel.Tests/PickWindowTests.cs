using MCG_3DPanel.Commands;
using Xunit;

namespace MCG_3DPanel.Tests
{
    /// <summary>Quét chọn trong Pick (kỹ sư 2026-11-05): Window (trái→phải) = nằm TRỌN trong khung; Crossing (phải→trái) =
    /// CHẠM khung. So theo hộp bao đã chiếu lên mặt phẳng nhìn.</summary>
    public class PickWindowTests
    {
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
