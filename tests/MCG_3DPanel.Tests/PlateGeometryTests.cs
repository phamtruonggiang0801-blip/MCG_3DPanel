using System.Collections.Generic;
using MCG_3DPanel.Services.PlanView;
using Xunit;

namespace MCG_3DPanel.Tests
{
    /// <summary>Đường bao Top Plate: đường cắt giữa panel (tờ 7D-17P: tấm trên X-X dài 15939.8 từ x = 0) và điểm đo kích thước bao
    /// (rộng 13356.2 đo tới đỉnh mép trên sát cạnh trái, không phải khung bao 13392.3).</summary>
    public class PlateGeometryTests
    {
        /// <summary>Kiểu 7D-17P (hệ block): thân x 0…15939.8, y −12891.3…61; tai trái x −359…0 ở dưới và trên; mép trên trái hạ xuống 464.9.</summary>
        private static readonly List<(double, double)> Plate = new List<(double, double)>
        {
            (-359.06, -12891.26), (15939.84, -12891.26), (15939.84, 61), (15939.84, 500.84), (14000, 500.84), (14000, 61),
            (264.8, 61), (264.8, 464.9), (-359.06, 464.9), (-359.06, 61), (0, 61), (0, -12500), (-359.06, -12500),
        };

        [Fact] public void Cut_X_AtMidHeight_StartsAtZero()
        {
            var (lo, hi) = PlateGeometry.Cut(Plate, 'X', -6215.2);
            Assert.Equal(0, lo, 3);
            Assert.Equal(15939.84, hi, 3);
        }

        [Fact] public void Cut_Y_AtMidWidth()
        {
            var (lo, hi) = PlateGeometry.Cut(Plate, 'Y', 7790.4);
            Assert.Equal(-12891.26, lo, 3);
            Assert.Equal(61, hi, 3);
        }

        [Fact] public void Cut_EmptyOutline_FallsBackToBox()
            => Assert.Equal((-1.0, 9.0), PlateGeometry.Cut(new List<(double, double)>(), 'X', 0, (-1, -5, 9, 5)));

        [Fact] public void Overall_Width_ToTopVertexNearLeftEdge()
        {
            var (p1, p2) = PlateGeometry.OverallWidth(Plate, false);
            Assert.Equal((-359.06, -12891.26), p1);
            Assert.Equal(464.9, p2.Item2, 3);   // không phải 500.84 (khung bao)
        }

        [Fact] public void Overall_Length_BottomCorners()
        {
            var (p1, p2) = PlateGeometry.OverallLength(Plate, false);
            Assert.Equal((-359.06, -12891.26), p1);
            Assert.Equal((15939.84, -12891.26), p2);
        }

        [Fact] public void OnOutline_WithinOneMillimetre()
        {
            Assert.True(PlateGeometry.OnOutline(Plate, 0, -6000));          // cạnh x = 0
            Assert.True(PlateGeometry.OnOutline(Plate, 15940.5, -100));
            Assert.False(PlateGeometry.OnOutline(Plate, 140, -6000));
        }
    }
}
