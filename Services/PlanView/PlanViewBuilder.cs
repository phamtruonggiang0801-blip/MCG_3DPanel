using System.Linq;

namespace MCG_3DPanel.Services.PlanView
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>Ghép toàn bộ tờ Plan View theo tờ mẫu 7D-17P (so sánh 2026-11-10).</summary>
    public static class PlanViewBuilder
    {
        public static PlanViewModel Build(PlanViewInput input, SheetContext s)
        {
            var m = new PlanViewModel();
            int sc = s.Scale;

            // 1. Mặt bằng chính: chỉ kích thước bao + khung đo kiểm.
            var overall = DraftDimRules.Overall(input, sc);
            m.Dims.AddRange(overall);
            var boxes = SheetItemRules.ToleranceBoxes(overall, sc);
            m.Inserts.AddRange(boxes);
            if (boxes.Count > 0) m.Require(SheetItemRules.ToleranceBox);

            // 2. Nhãn + ký hiệu.
            m.Texts.AddRange(LabelRules.Names(input, sc));
            m.Texts.AddRange(SymbolRules.Marks(input, sc));
            var cs = SymbolRules.Continuous(input);
            m.Inserts.AddRange(cs);
            if (cs.Count > 0) m.Require(SymbolRules.ContinuousBlock);

            // 3. Mặt cắt (kèm kích thước nháp Structure + thk_sym).
            foreach (var which in new[] { 'X', 'Y' })
            {
                var sec = SectionRules.Build(input, which, s);
                m.Sections.Add(sec);
                SectionRules.Require(sec, m);
            }

            // 4. Member list + khung AM_8, ghi chú dung sai, tiêu đề mặt bằng.
            m.Texts.Add(LabelRules.MemberList(input, s.FrameX, s.FrameY, sc));
            var (frames, rules) = SheetItemRules.MemberFrame(s.FrameX, s.FrameY, sc, LabelRules.MemberListLines(input));
            m.Polylines.AddRange(frames);
            m.Lines.AddRange(rules);
            foreach (var f in SheetItemRules.Fixed(s.FrameX, s.FrameY, sc)) { m.Inserts.Add(f); m.Require(f.Block); }
            m.Texts.Add(SheetItemRules.Title(SheetItemRules.TitlePlan, s.PlanMinX, s.PlanMaxX, s.PlanMinY, sc, PlanViewLayout.TitleWidthPlan));

            // 5. Mặt bằng lỗ lashing: chèn lại Top Plate + Lashing Hole, kích thước nháp lashing, M.D1/M.D2, đường chéo + leader, tiêu đề.
            if (input.HasLashingHoleBlock)
            {
                foreach (var p in SheetItemRules.LashingPlan(input, s)) { m.Inserts.Add(p); m.Require(p.Block); }
                var (lx, ly) = SheetItemRules.LashingInsertion(input, s);
                // Hệ block, dời bằng độ lệch giữa 2 lần chèn (reference _Assy không xoay — ledger Task 7).
                m.Dims.AddRange(DraftDimRules.Lashing(input, sc, (lx - s.AssyTransform[4], ly - s.AssyTransform[5])));
                var (lines, leaders) = SheetItemRules.Diagonals(input, s);
                m.Lines.AddRange(lines);
                m.Leaders.AddRange(leaders);
                m.Texts.Add(SheetItemRules.LashingTitle(input, s));
            }
            return m;
        }
    }
}
