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

        // ─── Section Detail ────────────────────────────────────────────────────

        [Fact]
        public void IsCommand_OnlyKnownDetailCommands()
        {
            Assert.True(DetailCommandRules.IsCommand("MCG_DetailPlan"));
            Assert.True(DetailCommandRules.IsCommand(" mcg_detailsection "));
            Assert.True(DetailCommandRules.IsCommand("MCG_DetailView"));
            Assert.False(DetailCommandRules.IsCommand(null));
            Assert.False(DetailCommandRules.IsCommand(""));
            Assert.False(DetailCommandRules.IsCommand("ERASE ALL"));
            Assert.False(DetailCommandRules.IsCommand("MCG_DetailPlan ERASE"));
        }

        [Fact]
        public void RegionFromData_RoundTripsRectangleAndCircle()
        {
            var rect = DetailCommandRules.RegionFromData(DetailCommandRules.RegionData(Rect(0, 0, 100, 40)), false);
            Assert.False(rect.IsCircle);
            Assert.Equal(4, rect.Corners.Length);
            Assert.True(rect.Contains(new P2(50, 20)));
            Assert.False(rect.Contains(new P2(150, 20)));
            var c = DetailCommandRules.RegionFromData(DetailCommandRules.RegionData(DetailRegion.FromCircle(new P2(5, 6), 10)), true);
            Assert.True(c.IsCircle);
            Assert.Equal(10, c.Radius, 6);
            Assert.Equal(5, c.Center.X, 6);
        }

        [Fact]
        public void RegionFromData_BadDataIsNull()
        {
            Assert.Null(DetailCommandRules.RegionFromData(null, false));
            Assert.Null(DetailCommandRules.RegionFromData(new List<double[]>(), false));
            Assert.Null(DetailCommandRules.RegionFromData(new List<double[]> { new[] { 0.0, 0 }, new[] { 1.0, 0 } }, false));
            Assert.Null(DetailCommandRules.RegionFromData(new List<double[]> { new[] { 0.0, 0, 0 } }, true));
            Assert.Null(DetailCommandRules.RegionFromData(new List<double[]> { new[] { 0.0, 0 } }, true));
        }

        [Fact]
        public void UsedLetters_SectionRecordsAndBlockNamesOfThatDetailOnly()
        {
            var recs = new[]
            {
                new DetailRecord { Kind = DetailKind.Section, Panel = "7D-16P", Number = 1, Letter = "A" },
                new DetailRecord { Kind = DetailKind.Section, Panel = "7D-16P", Number = 2, Letter = "B" },
                new DetailRecord { Kind = DetailKind.Section, Panel = "7D-16PX", Number = 1, Letter = "C" },
                new DetailRecord { Kind = DetailKind.Plan, Panel = "7D-16P", Number = 1, Letter = "D" },
                null,
            };
            var names = new[] { "7D-16P_DET.1_1E-1E", "7D-16P_DET.1_1F-1F_MCG", "7D-16P_DET.10_10G-10G", "7D-16PX_DET.1_1H-1H", "7D-16P_DET.1" };
            var used = new HashSet<string>(DetailCommandRules.UsedLetters("7D-16P", 1, recs, names));
            Assert.Equal(new HashSet<string> { "A", "E", "F" }, used);
            Assert.Equal("B", DetailNaming.NextSectionLetter(used));
        }

        [Fact]
        public void CutLineProblem_LengthAndCrossing()
        {
            var r = Rect(0, 0, 100, 40);
            Assert.Null(DetailCommandRules.CutLineProblem(new P2(-50, 20), new P2(500, 20), r));   // dài hơn vùng: cắt theo vùng
            Assert.Equal("The cut line has no length. Pick the end point again.",
                DetailCommandRules.CutLineProblem(new P2(10, 20), new P2(10, 20), r));
            Assert.Equal("The cut line does not cross the detail. Pick the end point again.",
                DetailCommandRules.CutLineProblem(new P2(10, 50), new P2(90, 50), r));
            var c = DetailRegion.FromCircle(new P2(0, 0), 10);
            Assert.Null(DetailCommandRules.CutLineProblem(new P2(-20, 5), new P2(20, 5), c));
            Assert.NotNull(DetailCommandRules.CutLineProblem(new P2(-20, 15), new P2(20, 15), c));
        }

        [Fact]
        public void FrameProblem_NamesWhatToPickAgain()
        {
            var r = Rect(0, 0, 100, 40);
            Assert.Null(DetailCommandRules.FrameProblem(new P2(10, 20), new P2(90, 20), new P2(50, 30), r, out _));

            Assert.NotNull(DetailCommandRules.FrameProblem(new P2(10, 20), new P2(10, 20), new P2(50, 30), r, out bool again));
            Assert.True(again);   // không dài ⇒ chọn lại điểm cuối

            string why = DetailCommandRules.FrameProblem(new P2(200, 20), new P2(300, 20), new P2(250, 30), r, out again);
            Assert.Equal("The cut line does not cross the detail. Pick the end point again.", why);
            Assert.True(again);

            why = DetailCommandRules.FrameProblem(new P2(10, 20), new P2(90, 20), new P2(50, 20.5), r, out again);
            Assert.Equal("The viewing side is on the cut line. Pick a point to one side of it.", why);
            Assert.False(again);
        }
    }
}
