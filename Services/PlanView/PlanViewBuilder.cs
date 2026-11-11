using System;
using System.Collections.Generic;
using System.Linq;

namespace MCG_3DPanel.Services.PlanView
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>Ghép toàn bộ tờ Plan View theo chuẩn tờ 7D-17C (+ 7D-17P; so sánh 2026-11-10).</summary>
    public static class PlanViewBuilder
    {
        /// <summary>Khung trong của A1 (mm giấy từ gốc khung): đường AM_BOR x 16…816, y 8…576.</summary>
        public const double BorderX0 = 16, BorderY0 = 8, BorderX1 = 816, BorderY1 = 576;

        /// <summary>Dựng theo bộ Chuẩn (7D-17C); nội dung lọt khỏi khung trong ⇒ dựng lại theo bộ Gọn (7D-17P), lấy bộ tràn ít hơn.</summary>
        public static PlanViewModel Build(PlanViewInput input, SheetContext s)
        {
            PlanViewModel best = null;
            foreach (var p in new[] { LayoutProfile.Standard, LayoutProfile.Compact })
            {
                PlanViewModel m;
                using (PlanViewLayout.Use(p)) m = BuildWith(input, s);
                m.Layout = p.Name;
                m.Overflow = Overflow(m, s);
                if (best == null || m.Overflow < best.Overflow - 1e-6) best = m;
                if (best.Overflow <= 0) break;
            }
            return best;
        }

        /// <summary>Phần nội dung lọt ra ngoài khung trong (mm giấy, lớn nhất trên 4 cạnh); ≤ 0 = nằm trọn trong khung.</summary>
        public static double Overflow(PlanViewModel m, SheetContext s)
        {
            int sc = s.Scale;
            double tx = s.AssyTransform[4], ty = s.AssyTransform[5];
            var pts = new List<(double X, double Y)>();
            void P(PvSpace sp, double x, double y) => pts.Add(sp == PvSpace.Block ? (x + tx, y + ty) : (x, y));
            const double TextPad = 5;   // mm giấy: chữ kích thước nằm ngoài đường kích thước
            foreach (var d in m.Dims.Concat(m.Sections.SelectMany(x => x.Dims)))
            {
                P(d.Space, d.X1, d.Y1); P(d.Space, d.X2, d.Y2);
                if (d.Kind == "Diametric") continue;
                bool h = Math.Abs(Math.Sin(d.Rotation)) < 0.01;
                if (h) { P(d.Space, d.X1, d.LineY + TextPad * sc); P(d.Space, d.X1, d.LineY - TextPad * sc); }
                else { P(d.Space, d.LineX + TextPad * sc, d.Y1); P(d.Space, d.LineX - TextPad * sc, d.Y1); }
                if (d.TextX.HasValue) P(d.Space, d.TextX.Value, d.TextY.Value);
            }
            foreach (var t in m.Texts)
            {
                double w = (t.IsMText ? t.PaperWidth : (t.Text ?? "").Length * t.PaperHeight * 0.8) * sc;
                double hgt = t.PaperHeight * sc;
                bool left = t.Justify == "MiddleLeft" || t.Justify == "TopLeft";
                P(t.Space, left ? t.X : t.X - w / 2, t.Y - hgt); P(t.Space, left ? t.X + w : t.X + w / 2, t.Y + hgt / 2);
            }
            foreach (var i in m.Inserts) P(i.Space, i.X, i.Y);
            foreach (var sec in m.Sections)
            {
                foreach (var r in sec.Plates) { var a = SectionRules.ToSheet(sec, r.X0, r.Y0); var b = SectionRules.ToSheet(sec, r.X1, r.Y1); pts.Add(a); pts.Add(b); }
                foreach (var t in sec.ThkSymbols) pts.Add((t.X, t.Y));
            }
            foreach (var l in m.Lines) { P(l.Space, l.X1, l.Y1); P(l.Space, l.X2, l.Y2); }
            foreach (var pl in m.Polylines) foreach (var q in pl.Points) P(pl.Space, q.X, q.Y);
            foreach (var ld in m.Leaders) foreach (var q in ld.Points) P(ld.Space, q.X, q.Y);
            if (pts.Count == 0) return 0;
            double x0 = s.FrameX + BorderX0 * sc, y0 = s.FrameY + BorderY0 * sc, x1 = s.FrameX + BorderX1 * sc, y1 = s.FrameY + BorderY1 * sc;
            return new[] { x0 - pts.Min(p => p.X), pts.Max(p => p.X) - x1, y0 - pts.Min(p => p.Y), pts.Max(p => p.Y) - y1 }.Max() / sc;
        }

        internal static PlanViewModel BuildWith(PlanViewInput input, SheetContext s)
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
            m.Texts.AddRange(SymbolRules.ContinueGirder(input, sc));
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
            m.Texts.Add(SheetItemRules.PlanTitle(input, s));

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
            // Chữ kích thước chật ⇒ đặt trên đường kích thước, so le (không chồng — kỹ sư 2026-11-10).
            DimTextLayout.Arrange(m.Dims.Concat(m.Sections.SelectMany(x => x.Dims)), sc);
            return m;
        }
    }
}
