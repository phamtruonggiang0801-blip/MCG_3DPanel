using System;
using System.Linq;
using MCG_3DPanel.Services.PlanView;
using Xunit;

namespace MCG_3DPanel.Tests
{
    /// <summary>Ghép toàn bộ tờ Plan View: nhãn, ký hiệu, kích thước, mặt cắt, mặt bằng lỗ lashing, mục cố định.</summary>
    public class PlanViewBuilderTests
    {
        private static int _line;

        private static PvMember M(string name, string kind, double x0, double y0, double x1, double y1, double t, double h, string spec = "", string profile = null, int? line = null)
            => new PvMember { Line = line ?? ++_line, Name = name, Kind = kind, Dir = Math.Abs(x1 - x0) >= Math.Abs(y1 - y0) ? "L" : "T",
                              X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Thickness = t, Height = h, Spec = spec, ProfileBlock = profile };

        /// <summary>7D-17P tổng hợp: T1, T2; L1…L7; 19 HT chạy từ L1 tới L7; 1 nháp lashing; 1 tripping B.</summary>
        private static PlanViewInput Panel7D17P()
        {
            var i = new PlanViewInput { Panel = "7D-17P", TopPlate = (0, 0, 16000, 13000), PlateThickness = 6, HasLashingHoleBlock = true,
                                        Lashing = new PvLashing { Diameter = 55, X = 700, Y = 700 } };
            i.Members.Add(M("T1", "Web", 300, 0, 300, 13000, 6, 374, "WEB 374x6.0/FLANGE 380x20.0"));
            i.Members.Add(M("T2", "Web", 15700, 0, 15700, 13000, 6, 374, "WEB 374x6.0/FLANGE 380x20.0"));
            double[] ys = { 400, 2450, 4500, 6500, 8600, 10600, 12600 };
            // web L dừng ở mặt web T (303 … 15697) ⇒ T chạy liên tục qua L, không ngược lại
            for (int k = 0; k < 7; k++) i.Members.Add(M("L" + (k + 1), "Web", 303, ys[k], 15697, ys[k], 6, 374, "WEB 374x6.0/FLANGE 170x20.0"));
            for (int k = 0; k < 19; k++) { double x = 1000 + 775 * k; i.Members.Add(M("HT" + (k + 1), "HP", x, 403, x, 12597, 6, 120, "HP 120x6.0", "HP120x6")); }
            i.Members.Add(M("BS", "FB", 0, 1400, 16000, 1400, 6, 80, "FB 80x6.0", "BS 80x6"));
            i.Drafts.Add(new PvDraft { Source = "LashingHole", X1 = 709.9, Y1 = 700, X2 = 1349.9, Y2 = 700 });
            i.Marks.Add(new PvMark { Text = "B", X = 5000, Y = 3000 });
            return i;
        }

        private static SheetContext Sheet(int scale) => new SheetContext
        {
            Scale = scale, FrameX = 0, FrameY = 0, PlanMinX = 5000, PlanMinY = 9000, PlanMaxX = 21000, PlanMaxY = 22000,
            AssyTransform = new[] { 1.0, 0, 0, 1, 5000, 9000 },
        };

        [Fact] public void Build_7D17P_Counts()
        {
            var m = PlanViewBuilder.Build(Panel7D17P(), Sheet(50));
            Assert.Equal(9, m.Texts.Count(t => t.Category == "Name" && t.Layer == "Mechanical-AM_1"));
            Assert.Equal(20, m.Texts.Count(t => t.Category == "Name" && t.Layer == "Mechanical-AM_6"));   // 19 HT + 1 BS
            // 19 HT qua 5 web L trong (95) + T1, T2 qua 7 web L (14) + BS qua T1, T2 (2)
            Assert.Equal(95 + 14 + 2, m.Inserts.Count(x => x.Block == "CS_horl_up_right"));
            Assert.Equal(2, m.Sections.Count);
            foreach (var b in new[] { "M.D1", "M.D2", "Measurements_Tolerances vFuture" }) Assert.Single(m.Inserts, x => x.Block == b);
            Assert.Equal(2, m.Inserts.Count(x => x.Block == "Rec. for tolerance"));
            Assert.Single(m.Inserts, x => x.Block == "7D-17P_Top Plate");
            Assert.Single(m.Inserts, x => x.Block == "7D-17P_Lashing Hole");
            Assert.Single(m.Texts, t => t.Category == "MemberList");
            Assert.Equal(2, m.Texts.Count(t => t.Category == "Title"));
            Assert.Single(m.Texts, t => t.Category == "Symbol" && t.Text == "B");
            foreach (var b in new[] { "CS_horl_up_right", "thk_sym", "HP120x6", "BS 80x6", "M.D1", "M.D2", "Rec. for tolerance", "Measurements_Tolerances vFuture", "7D-17P_Top Plate", "7D-17P_Lashing Hole" })
                Assert.Contains(b, m.RequiredBlocks);
            Assert.Equal(2, m.Dims.Count(d => d.Layer == "Mechanical-AM_4"));   // nháp lashing: mặt bằng chính + mặt bằng lỗ lashing
        }

        [Fact] public void Fixed_ToleranceNote_At1to75()
        {
            var n = SheetItemRules.Fixed(0, 0, 75).Single(x => x.Block == "Measurements_Tolerances vFuture");
            Assert.Equal(2402.32, n.X, 1);
            Assert.Equal(8228.57, n.Y, 1);
            Assert.Equal("Mechanical-AM_6", n.Layer);
            Assert.Equal(PvSpace.Sheet, n.Space);
        }

        [Fact] public void LashingPlan_RightOfPlan_SameY()
        {
            var ins = SheetItemRules.LashingPlan(Panel7D17P(), Sheet(50));
            var tp = ins.Single(x => x.Block == "7D-17P_Top Plate");
            Assert.Equal(21000 + PlanViewLayout.LashingPlanGap * 50 - 0, tp.X, 3);   // plate min X (block) = 0
            Assert.Equal(9000, tp.Y, 3);
            var md1 = ins.Single(x => x.Block == "M.D1");
            Assert.Equal(tp.X + PlanViewLayout.MdOffsetX * 50, md1.X, 3);
            Assert.Equal(tp.Y + 13000 + PlanViewLayout.MdOffsetY * 50, md1.Y, 3);
            Assert.Equal(md1.X + PlanViewLayout.MdSpacing * 50, ins.Single(x => x.Block == "M.D2").X, 3);
        }

        [Fact] public void NoLashingBlock_NoLashingPlan()
        {
            var i = Panel7D17P(); i.HasLashingHoleBlock = false;
            var m = PlanViewBuilder.Build(i, Sheet(50));
            Assert.DoesNotContain(m.Inserts, x => x.Block == "M.D1" || x.Block == "7D-17P_Lashing Hole");
            Assert.Single(m.Texts, t => t.Category == "Title");
        }

        [Fact] public void ToleranceBoxes_AtOverallDims()
        {
            var i = Panel7D17P();
            var overall = DraftDimRules.Overall(i, 50, DraftDimRules.FromDrafts(i, 50)).Where(d => d.Kind == "Rotated").ToList();
            var boxes = SheetItemRules.ToleranceBoxes(overall, 50);
            Assert.Equal(2, boxes.Count);
            Assert.All(boxes, b => { Assert.True(b.ScaleIsAbsolute); Assert.Equal(50, b.Scale); Assert.Equal(PvSpace.Block, b.Space); });
        }
    }
}
