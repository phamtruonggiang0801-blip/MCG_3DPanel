using System.Collections.Generic;
using MCG_3DPanel.Services.Detail;
using Xunit;

namespace MCG_3DPanel.Tests
{
    public class DetailCommandRulesTests
    {
        static DetailRegion Rect(double x0, double y0, double x1, double y1)
        {
            var v = new List<(double, double, double)> { (x0, y0, 0), (x1, y0, 0), (x1, y1, 0), (x0, y1, 0) };
            return DetailRegion.FromPolyline(v, true, out _);
        }

        [Fact]
        public void Centre_RectangleAndCircle()
        {
            var c = DetailCommandRules.Centre(Rect(0, 0, 100, 40));
            Assert.Equal(50, c.X, 6); Assert.Equal(20, c.Y, 6);
            var k = DetailCommandRules.Centre(DetailRegion.FromCircle(new P2(5, 6), 10));
            Assert.Equal(5, k.X, 6); Assert.Equal(6, k.Y, 6);
        }

        [Fact]
        public void RegionData_RectangleCornersCircleTriple()
        {
            var r = DetailCommandRules.RegionData(Rect(0, 0, 100, 40));
            Assert.Equal(4, r.Count);
            Assert.All(r, p => Assert.Equal(2, p.Length));
            var c = DetailCommandRules.RegionData(DetailRegion.FromCircle(new P2(5, 6), 10));
            Assert.Single(c);
            Assert.Equal(new[] { 5.0, 6.0, 10.0 }, c[0]);
        }

        [Fact]
        public void LabelAt_RectangleTopRightPlusGap()
        {
            var p = DetailCommandRules.LabelAt(Rect(0, 0, 100, 40), 50);
            Assert.Equal(150, p.X, 6); Assert.Equal(90, p.Y, 6);
        }

        [Fact]
        public void LabelAt_RotatedRectangleUsesUpperRightCorner()
        {
            // hình thoi (vuông xoay 45°): góc trên-phải = góc có x+y lớn nhất, hoà ⇒ y cao hơn
            var v = new List<(double, double, double)> { (0, -10, 0), (10, 0, 0), (0, 10, 0), (-10, 0, 0) };
            var r = DetailRegion.FromPolyline(v, true, out _);
            var p = DetailCommandRules.LabelAt(r, 0);
            Assert.Equal(0, p.X, 6); Assert.Equal(10, p.Y, 6);
        }

        [Fact]
        public void LabelAt_CircleAt45Degrees()
        {
            var p = DetailCommandRules.LabelAt(DetailRegion.FromCircle(new P2(0, 0), 100), 10);
            Assert.Equal(100 / System.Math.Sqrt(2) + 10, p.X, 6);
            Assert.Equal(100 / System.Math.Sqrt(2) + 10, p.Y, 6);
        }

        [Fact]
        public void ViewTitle_TypedOrDefault()
        {
            Assert.Equal("%%UDETAIL 3", DetailCommandRules.ViewTitle("", 3));
            Assert.Equal("%%UDETAIL 3", DetailCommandRules.ViewTitle("   ", 3));
            Assert.Equal("%%UDETAIL A (1:5)", DetailCommandRules.ViewTitle("DETAIL A (1:5)", 3));
            Assert.Equal("%%Ufoo", DetailCommandRules.ViewTitle("%%Ufoo", 3));
        }

        [Fact]
        public void StaleQuestion_OnlyForStaleStates()
        {
            Assert.Null(DetailCommandRules.StaleQuestion(StaleState.Fresh, "P_3D.dwg", "P"));
            Assert.Null(DetailCommandRules.StaleQuestion(StaleState.Unknown, "P_3D.dwg", "P"));
            Assert.Contains("P_3D.dwg has unsaved changes", DetailCommandRules.StaleQuestion(StaleState.Unsaved3D, "P_3D.dwg", "P"));
            Assert.Contains("The P block changed in this drawing after P_3D.dwg was built",
                DetailCommandRules.StaleQuestion(StaleState.Changed2D, "P_3D.dwg", "P"));
        }

        [Fact]
        public void Notes_DistinctSentences()
        {
            Assert.Equal("", DetailCommandRules.Notes(null));
            Assert.Equal("", DetailCommandRules.Notes(new[] { "", null, "  " }));
            Assert.Equal("Notes: 2 solid(s) could not be copied; line type missing.",
                DetailCommandRules.Notes(new[] { "2 solid(s) could not be copied", "line type missing.", "2 solid(s) could not be copied" }));
        }
    }
}
