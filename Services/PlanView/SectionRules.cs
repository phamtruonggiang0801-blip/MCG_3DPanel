using System;
using System.Collections.Generic;
using System.Linq;

namespace MCG_3DPanel.Services.PlanView
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>Mặt cắt điển hình theo tờ mẫu 7D-17P (so sánh 2026-11-10):
    /// • đường cắt giữa panel (kỹ sư); gốc cục bộ = đầu tấm trên đường cắt; tấm dài tới mép tấm trên đường cắt;
    /// • X-X: gom thanh phương T, tấm 0…t, web treo dưới y = 0, profile chèn tại y = 0; Y-Y (xoay 90°): thanh phương L, tấm −t…0,
    ///   web dưới −t, profile tại −t;
    /// • profile HP chèn tại MẶT LƯNG (tâm ∓ t/2), lật (sx = −1) khi bầu ngược trục mặt cắt;
    /// • thk_sym tại mỗi thanh trên đường tấm; kích thước 3 mức: đầu mút (mũi tên xiên), chuỗi bước = nháp Structure, chuỗi girder
    ///   (nháp girder→girder, không có thì tự sinh từ đầu tấm) + chiều cao mặt cắt.</summary>
    public static class SectionRules
    {
        public const string Layer = "Mechanical-AM_5";
        public const string ThkBlock = "thk_sym";

        private sealed class Pt
        {
            public double X;          // cục bộ dọc mặt cắt
            public bool IsWeb;
            public bool Mirror;
        }

        public static PvSection Build(PlanViewInput input, char which, SheetContext s)
        {
            bool isX = which == 'X';
            int sc = s.Scale;
            var tp = input.TopPlate;
            double t = input.PlateThickness;
            string dir = isX ? "T" : "L";
            double tx = s.AssyTransform[4], ty = s.AssyTransform[5];

            // Đường cắt giữa panel.
            var outline = DraftDimRules.Outline(input);
            var (lo, hi) = isX ? PlateGeometry.Cut(outline, 'X', (tp.MinY + tp.MaxY) / 2, tp)
                               : PlateGeometry.Cut(outline, 'Y', (tp.MinX + tp.MaxX) / 2, tp);
            double len = hi - lo;

            var sec = new PvSection { BlockName = input.Panel + (isX ? "_X-X_MCG" : "_Y-Y_MCG") };
            if (isX) { sec.X = tx + lo; sec.Y = s.PlanMaxY + PlanViewLayout.SectionXAbove * sc; }
            else { sec.X = s.PlanMinX - PlanViewLayout.SectionYLeft * sc; sec.Y = ty + lo; sec.Rotation = Math.PI / 2; }

            double plateTop = isX ? t : 0, line = isX ? 0 : -t;     // đường tiếp xúc tấm–thanh (cục bộ y)
            sec.Plates.Add(new PvRect { X0 = 0, Y0 = plateTop - t, X1 = len, Y1 = plateTop });

            var lines = input.Members.Where(m => !m.Deleted && m.Dir == dir)
                .GroupBy(m => (m.Line, m.Kind)).Select(g => g.OrderByDescending(Len).First()).ToList();
            double Pos(PvMember m) => (isX ? (m.X0 + m.X1) / 2 : (m.Y0 + m.Y1) / 2) - lo;
            double mid = len / 2;

            var webs = lines.Where(m => m.Kind == "Web").OrderBy(Pos).ToList();
            var webH = webs.ToDictionary(m => m.Line, m => m.Height);
            double maxWeb = webs.Count > 0 ? webs.Max(m => m.Height) : 0;
            var pts = new List<Pt>();
            foreach (var w in webs)
            {
                sec.Plates.Add(new PvRect { X0 = Pos(w) - w.Thickness / 2, X1 = Pos(w) + w.Thickness / 2, Y0 = line - w.Height, Y1 = line });
                // mặt trong (về phía giữa mặt cắt)
                pts.Add(new Pt { X = Pos(w) < mid ? Pos(w) + w.Thickness / 2 : Pos(w) - w.Thickness / 2, IsWeb = true });
            }
            var flanges = new List<(PvMember f, PvRect r)>();
            foreach (var f in lines.Where(m => m.Kind == "Flange"))
            {
                double h = webH.TryGetValue(f.Line, out var wh) ? wh : maxWeb;
                var r = new PvRect { X0 = Pos(f) - f.Thickness / 2, X1 = Pos(f) + f.Thickness / 2, Y0 = line - h - f.Height, Y1 = line - h };
                sec.Plates.Add(r); flanges.Add((f, r));
            }
            foreach (var p in lines.Where(m => (m.Kind == "HP" || m.Kind == "FB") && !string.IsNullOrEmpty(m.ProfileBlock)).OrderBy(Pos))
            {
                double belly = isX ? p.BellyX : p.BellyY;
                bool mirror = belly < -0.5;
                double ins = mirror ? Pos(p) + p.Thickness / 2 : Pos(p) - p.Thickness / 2;   // mặt lưng
                sec.Profiles.Add(new PvInsert { Space = PvSpace.Block, Block = p.ProfileBlock, X = ins, Y = line, Scale = 1, ScaleIsAbsolute = true, MirrorX = mirror, Layer = "0" });
                pts.Add(new Pt { X = ins, Mirror = mirror });
            }
            pts = pts.OrderBy(p => p.X).ToList();

            // thk_sym tại mỗi thanh (tờ mẫu: X-X lật ở phía trước girder đầu, Y-Y lật trừ phía sau girder cuối).
            double firstWeb = pts.Where(p => p.IsWeb).Select(p => p.X).DefaultIfEmpty(double.NegativeInfinity).First();
            double lastWeb = pts.Where(p => p.IsWeb).Select(p => p.X).DefaultIfEmpty(double.PositiveInfinity).Last();
            foreach (var p in pts)
            {
                var (x, y) = ToSheet(sec, p.X, line);
                bool flip = isX ? p.X <= firstWeb + 1e-6 : p.X < lastWeb - 1e-6;
                sec.ThkSymbols.Add(new PvInsert { Space = PvSpace.Sheet, Block = ThkBlock, X = x, Y = y, Rotation = sec.Rotation, Scale = 1, MirrorX = flip, Layer = "Mechanical-AM_0" });
            }

            // ─── Kích thước ───
            double lvEnds = (isX ? PlanViewLayout.SecXEnds : PlanViewLayout.SecYEnds) * sc;
            double lvPitch = (isX ? PlanViewLayout.SecXPitch : PlanViewLayout.SecYPitch) * sc;
            double lvGirder = (isX ? PlanViewLayout.SecXGirder : PlanViewLayout.SecYGirder) * sc;
            void Dim(double a, double b, double level, bool oblique, bool tmove, string text = null)
            {
                var p1 = ToSheet(sec, a, line); var p2 = ToSheet(sec, b, line); var ln = ToSheet(sec, b, level);   // mức tính từ đường chèn (X-X: đáy tấm, Y-Y: mặt tấm)
                sec.Dims.Add(new PvDim { Space = PvSpace.Sheet, X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y, LineX = ln.X, LineY = ln.Y,
                                         Rotation = sec.Rotation, Layer = Layer, Oblique = oblique, Tmove = tmove, TextOverride = text });
            }

            if (pts.Count > 0)
            {
                Dim(0, pts[0].X, lvEnds, true, false);
                Dim(pts[pts.Count - 1].X, len, lvEnds, true, true);
            }
            var webCentres = webs.Select(w => (c: Pos(w), half: w.Thickness / 2 + 2)).ToList();
            bool OnWeb(double x) => webCentres.Any(w => Math.Abs(x - w.c) <= w.half);
            bool girderDraft = false;
            foreach (var d in input.Drafts.Where(d => d.Source == "Structure"))
            {
                bool along = isX ? Math.Abs(Math.Sin(d.Rotation)) < 0.01 : Math.Abs(Math.Cos(d.Rotation)) < 0.01;
                if (!along) continue;
                double a = (isX ? d.X1 : d.Y1) - lo, b = (isX ? d.X2 : d.Y2) - lo;
                if (Math.Min(a, b) < -1 || Math.Max(a, b) > len + 1) continue;
                bool girder = OnWeb(a) && OnWeb(b);
                girderDraft |= girder;
                Dim(a, b, girder ? lvGirder : lvPitch, d.Oblique, d.Tmove, string.IsNullOrEmpty(d.Text) ? null : d.Text);
            }
            if (!girderDraft)
            {
                var chain = new List<double> { 0 };
                chain.AddRange(pts.Where(p => p.IsWeb).Select(p => p.X));
                for (int k = 1; k < chain.Count; k++) Dim(chain[k - 1], chain[k], lvGirder, false, k > 1);
            }

            // Chiều cao mặt cắt: X-X bên trái đầu tấm (tới đáy flange girder đầu); Y-Y phía trên cuối tấm (tới đáy flange girder cuối).
            var fl = flanges.OrderBy(f => (f.r.X0 + f.r.X1) / 2).ToList();
            if (fl.Count > 0)
            {
                var r = isX ? fl[0].r : fl[fl.Count - 1].r;
                double ax = isX ? 0 : len, bx = isX ? r.X0 : r.X1, lx = isX ? -PlanViewLayout.SecXHeightLeft * sc : len + PlanViewLayout.SecYHeightAbove * sc;
                var p1 = ToSheet(sec, ax, plateTop); var p2 = ToSheet(sec, bx, r.Y0); var ln = ToSheet(sec, lx, plateTop);
                sec.Dims.Add(new PvDim { Space = PvSpace.Sheet, X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y, LineX = ln.X, LineY = ln.Y,
                                         Rotation = sec.Rotation + Math.PI / 2, Layer = Layer, Tmove = false });
            }
            return sec;
        }

        /// <summary>Ghi các block mặt cắt cần có sẵn trong bản vẽ (profile, thk_sym) vào model.</summary>
        public static void Require(PvSection s, PlanViewModel model)
        {
            foreach (var p in s.Profiles) model.Require(p.Block);
            if (s.ThkSymbols.Count > 0) model.Require(ThkBlock);
        }

        public static (double X, double Y) ToSheet(PvSection s, double lx, double ly)
        {
            double c = Math.Cos(s.Rotation), sn = Math.Sin(s.Rotation);
            return (s.X + lx * c - ly * sn, s.Y + lx * sn + ly * c);
        }

        private static double Len(PvMember m) => Math.Sqrt((m.X1 - m.X0) * (m.X1 - m.X0) + (m.Y1 - m.Y0) * (m.Y1 - m.Y0));
    }
}
