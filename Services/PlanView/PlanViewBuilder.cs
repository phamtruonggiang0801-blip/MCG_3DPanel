using System.Collections.Generic;
using System.Linq;

namespace MCG_3DPanel.Services.PlanView
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>Ghép toàn bộ tờ Plan View từ ảnh chụp panel + bối cảnh tờ (spec 2026-11-10 mục "What is drawn").</summary>
    public static class PlanViewBuilder
    {
        public static PlanViewModel Build(PlanViewInput input, SheetContext s)
        {
            var m = new PlanViewModel();
            int sc = s.Scale;

            // 1. Kích thước: nháp + bao + Ø lỗ lashing.
            var drafts = DraftDimRules.FromDrafts(input, sc);
            var overall = DraftDimRules.Overall(input, sc, drafts);
            m.Dims.AddRange(drafts);
            m.Dims.AddRange(overall);
            var all = drafts.Concat(overall).ToList();
            double topOuter = DraftDimRules.OuterOffset(all, input, "top", sc);
            double leftOuter = DraftDimRules.OuterOffset(all, input, "left", sc);
            double bottomOuter = DraftDimRules.OuterOffset(all, input, "bottom", sc);

            // 2. Nhãn + ký hiệu.
            m.Texts.AddRange(LabelRules.Names(input, sc, topOuter, leftOuter));
            m.Texts.AddRange(SymbolRules.Marks(input, sc));
            var cs = SymbolRules.Continuous(input);
            m.Inserts.AddRange(cs);
            if (cs.Count > 0) m.Require(SymbolRules.ContinuousBlock);

            // 3. Mặt cắt, đặt ngoài hàng nhãn.
            var (_, rowGirder, colLeft) = LabelRules.Rows(topOuter, leftOuter);
            var x = SectionRules.Build(input, 'X', s.PlanMinX, s.PlanMinY, s.PlanMaxX, s.PlanMaxY, sc, rowGirder + PlanViewLayout.TextGirder);
            var y = SectionRules.Build(input, 'Y', s.PlanMinX, s.PlanMinY, s.PlanMaxX, s.PlanMaxY, sc, colLeft);
            foreach (var sec in new[] { x, y }) { m.Sections.Add(sec); SectionRules.Require(sec, m); }

            // 4. Bảng quy cách, mục cố định, tiêu đề, khung đo kiểm.
            m.Texts.Add(LabelRules.MemberList(input, s.FrameX, s.FrameY, sc));
            foreach (var f in SheetItemRules.Fixed(s.FrameX, s.FrameY, sc)) { m.Inserts.Add(f); m.Require(f.Block); }
            m.Texts.Add(SheetItemRules.Title(SheetItemRules.TitlePlan, s.PlanMinX, s.PlanMaxX, s.PlanMinY, sc, bottomOuter));
            var boxes = SheetItemRules.ToleranceBoxes(overall, sc);
            m.Inserts.AddRange(boxes);
            if (boxes.Count > 0) m.Require(SheetItemRules.ToleranceBox);

            // 5. Mặt bằng lỗ lashing (chèn lại Top Plate + Lashing Hole) + kích thước AM_4 của nó + M.D1/M.D2 + tiêu đề.
            if (input.HasLashingHoleBlock)
            {
                var plan = SheetItemRules.LashingPlan(input, s);
                foreach (var p in plan) { m.Inserts.Add(p); m.Require(p.Block); }
                var (lx, ly) = SheetItemRules.LashingInsertion(input, s);
                var lashOnly = new PlanViewInput { Panel = input.Panel, TopPlate = input.TopPlate };
                lashOnly.Drafts.AddRange(input.Drafts.Where(d => d.Source == "LashingHole"));
                // Kích thước ở hệ block, dời bằng độ lệch giữa 2 lần chèn (reference _Assy không xoay — ledger Task 7).
                m.Dims.AddRange(DraftDimRules.FromDrafts(lashOnly, sc, (lx - s.AssyTransform[4], ly - s.AssyTransform[5])));
                var tp = input.TopPlate;
                m.Texts.Add(SheetItemRules.Title(SheetItemRules.TitleLashing, lx + tp.MinX, lx + tp.MaxX, ly + tp.MinY, sc,
                    DraftDimRules.OuterOffset(DraftDimRules.FromDrafts(lashOnly, sc), lashOnly, "bottom", sc)));
            }
            return m;
        }
    }
}
