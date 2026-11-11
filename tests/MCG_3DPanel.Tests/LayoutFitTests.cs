using System;
using System.Collections.Generic;
using System.Linq;
using MCG_3DPanel.Services.PlanView;
using Xunit;

namespace MCG_3DPanel.Tests
{
    /// <summary>Chọn bộ khoảng cách theo chỗ trống trên tờ (lỗi 2026-11-10: tờ 7D-17P dùng bộ Chuẩn 17C ⇒ mặt cắt Y-Y ở x 4.4 mm,
    /// chuỗi kích thước ở x −7.7 / −22 — ngoài khung trong x 16).</summary>
    public class LayoutFitTests
    {
        private static PvMember M(int line, string kind, double x0, double y0, double x1, double y1, double h, string prof = null)
            => new PvMember { Line = line, Name = kind == "Web" ? "G" + line : "H" + line, Kind = kind, Dir = Math.Abs(x1 - x0) >= Math.Abs(y1 - y0) ? "L" : "T",
                              X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Thickness = 6, Height = h, ProfileBlock = prof };

        /// <summary>7D-17P đúng vị trí trên tờ CAS-0062242 (A1 ×50 tại 0,0; _Assy tại 5019.76, 22476.66).</summary>
        private static (PlanViewInput, SheetContext) Panel7D17P()
        {
            var i = new PlanViewInput { Panel = "7D-17P", TopPlate = (-359.06, -12891.26, 15939.84, 500.84), PlateThickness = 6, HasLashingHoleBlock = true };
            i.PlateOutline.AddRange(new List<(double, double)>
            {
                (-359.06, -12891.26), (15939.84, -12891.26), (15939.84, 61), (15939.84, 500.84), (14000, 500.84), (14000, 61),
                (264.8, 61), (264.8, 464.9), (-359.06, 464.9), (-359.06, 61), (0, 61), (0, -12500), (-359.06, -12500),
            });
            i.Members.Add(M(1, "Web", 287, -12891, 287, 61, 374)); i.Members.Add(M(2, "Web", 15403, -12891, 15403, 61, 374));
            for (int k = 0; k < 7; k++) i.Members.Add(M(10 + k, "Web", 0, -12482 + 2045 * k, 15939, -12482 + 2045 * k, 374));
            for (int k = 0; k < 20; k++) i.Members.Add(M(30 + k, "HP", 793 + 775 * k, -12800, 793 + 775 * k, 0, 120, "HP120x6"));
            var s = new SheetContext
            {
                Scale = 50, FrameX = 0, FrameY = 0, PlanMinX = 5019.76 - 359.06, PlanMinY = 22476.66 - 12891.26, PlanMaxX = 5019.76 + 15939.84, PlanMaxY = 22476.66 + 500.84,
                AssyTransform = new[] { 1.0, 0, 0, 1, 5019.76, 22476.66 },
            };
            return (i, s);
        }

        [Fact] public void Panel7D17P_StandardOverflows_CompactFits()
        {
            var (i, s) = Panel7D17P();
            using (PlanViewLayout.Use(LayoutProfile.Standard))
                Assert.True(PlanViewBuilder.Overflow(PlanViewBuilder.BuildWith(i, s), s) > 10);   // bộ Chuẩn tràn khung (tờ thật: Y-Y ở x 4.4)
            var m = PlanViewBuilder.Build(i, s);
            Assert.Equal(LayoutProfile.Compact.Name, m.Layout);
            Assert.True(m.Overflow <= 0, $"còn tràn {m.Overflow:0.0} mm");
            var y = m.Sections.Single(x => x.BlockName.EndsWith("_Y-Y_MCG"));
            Assert.Equal((5019.76 - 359.06) / 50 - 45.4, y.X / 50, 1);                       // tờ tay: Y-Y cách tấm 45.3
            Assert.All(m.Sections.SelectMany(x => x.Dims), d => Assert.True(Math.Min(d.LineX, d.X1) / 50 > PlanViewBuilder.BorderX0));
        }

        [Fact] public void Panel7D17C_FitsWithStandard()
        {
            var i = PlanView7D17CTests.Input();
            i.Members.Add(M(1, "Web", 300, -22000, 300, 0, 374)); i.Members.Add(M(2, "Web", 15500, -22000, 15500, 0, 374));
            i.Members.Add(M(3, "Web", 0, -10000, 15940, -10000, 374)); i.Members.Add(M(4, "HP", 0, -5000, 15940, -5000, 120, "HP120x6"));
            var m = PlanViewBuilder.Build(i, PlanView7D17CTests.Sheet());
            Assert.Equal(LayoutProfile.Standard.Name, m.Layout);
            Assert.True(m.Overflow <= 0, $"còn tràn {m.Overflow:0.0} mm");
        }

        [Fact] public void Overflow_MeasuresOutsideBorder()
        {
            var m = new PlanViewModel();
            m.Inserts.Add(new PvInsert { Space = PvSpace.Sheet, X = 4.4 * 50, Y = 300 * 50 });
            var s = new SheetContext { Scale = 50, AssyTransform = new[] { 1.0, 0, 0, 1, 0, 0 } };
            Assert.Equal(16 - 4.4, PlanViewBuilder.Overflow(m, s), 6);
        }
    }
}
