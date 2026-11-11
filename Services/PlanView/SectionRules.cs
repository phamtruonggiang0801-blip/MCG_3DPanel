using System;
using System.Collections.Generic;
using System.Linq;

namespace MCG_3DPanel.Services.PlanView
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>Mặt cắt điển hình (tờ 7D-17P + chuẩn 7D-17C, so sánh 2026-11-10):
    /// • tấm giữa 2 cạnh thẳng chính (dây cung dài nhất — kỹ sư); gốc cục bộ = cạnh chính thấp; mọi kích thước chuỗi tick xiên (17C);
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
            public bool IsFb;
        }

        public static PvSection Build(PlanViewInput input, char which, SheetContext s)
        {
            bool isX = which == 'X';
            int sc = s.Scale;
            double Gap = sc;   // 1 mm giấy
            var tp = input.TopPlate;
            double t = input.PlateThickness;
            string dir = isX ? "T" : "L";
            double tx = s.AssyTransform[4], ty = s.AssyTransform[5];

            // Tấm trên mặt cắt = giữa 2 cạnh thẳng chính (dây cung dài nhất — kỹ sư 2026-11-10; tấm chữ nhật = cắt giữa).
            var outline = DraftDimRules.Outline(input);
            var edges = PlateGeometry.MainEdges(outline, tp);
            double lo = isX ? edges.Left.At : edges.Bottom.At, hi = isX ? edges.Right.At : edges.Top.At;
            double len = hi - lo;

            var sec = new PvSection { BlockName = input.Panel + (isX ? "_X-X_MCG" : "_Y-Y_MCG") };
            if (isX) { sec.X = tx + lo; sec.Y = s.PlanMaxY + PlanViewLayout.SectionXAbove * sc; }
            else { sec.X = s.PlanMinX - YLeft(input) * sc; sec.Y = ty + lo; sec.Rotation = Math.PI / 2; }

            // Tờ 7D-17C: CẢ 2 mặt cắt tấm 0…t, thanh treo dưới y = 0, profile tại y = 0 (17P cũ: Y-Y −t…0 — lệch 1 chiều dày khi xoay 90°).
            double plateTop = t, line = 0;     // đường tiếp xúc tấm–thanh (cục bộ y)
            sec.Plates.Add(new PvRect { X0 = 0, Y0 = plateTop - t, X1 = len, Y1 = plateTop });

            // Mỗi (tuyến, loại) lấy đoạn THẲNG TRỤC dài nhất (bỏ đoạn xiên — tờ 17C: girder biên T1 chạy dọc cạnh xiên, mặt cắt vẽ đoạn
            // đứng của nó ở x 267.7, không phải trung điểm đoạn xiên).
            bool Straight(PvMember m) { double a = Math.Abs(m.X1 - m.X0), b = Math.Abs(m.Y1 - m.Y0); return isX ? a <= 0.02 * (a + b) : b <= 0.02 * (a + b); }
            var lines = input.Members.Where(m => !m.Deleted && m.Dir == dir && Straight(m))
                .GroupBy(m => (m.Line, m.Kind)).Select(g => g.OrderByDescending(Len).First()).ToList();
            double Pos(PvMember m) => (isX ? (m.X0 + m.X1) / 2 : (m.Y0 + m.Y1) / 2) - lo;
            double mid = len / 2;

            // 2 web cách nhau ≤ 1 mm giấy (đoạn vát lệch 5…40 mm) ⇒ chỉ giữ web dài hơn (tờ 17C: 1 web ở 8382, không vẽ thêm 8422).
            var webs = new List<PvMember>();
            foreach (var w in lines.Where(m => m.Kind == "Web").OrderByDescending(Len))
                if (!webs.Any(k => Math.Abs(Pos(k) - Pos(w)) <= Gap)) webs.Add(w);
            webs = webs.OrderBy(Pos).ToList();
            var keptWebLines = new HashSet<int>(webs.Select(w => w.Line));
            lines = lines.Where(m => m.Kind == "Web" ? webs.Contains(m) : m.Kind != "Flange" || keptWebLines.Contains(m.Line)).ToList();
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
                pts.Add(new Pt { X = ins, Mirror = mirror, IsFb = p.Kind == "FB" });
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

            // Tờ tay 7D-17P: chữ "<>" + không DIMTMOVE cho kích thước kỹ sư tự ghi; "" + DIMTMOVE cho phần tử lặp của chuỗi.
            const string V = "<>";
            if (pts.Count > 0)
            {
                if (pts[0].X > 1) Dim(0, pts[0].X, lvEnds, true, false, V);                              // đầu tấm → thanh đầu (60)
                if (len - pts[pts.Count - 1].X > 1) Dim(pts[pts.Count - 1].X, len, lvEnds, true, true, V);   // thanh cuối → cuối tấm (59.9)
            }
            var webPts = pts.Where(p => p.IsWeb).Select(p => p.X).ToList();
            double wFirst = webPts.Count > 0 ? webPts[0] : double.NaN, wLast = webPts.Count > 0 ? webPts[webPts.Count - 1] : double.NaN;

            var profilesHere = pts.Where(p => !p.IsWeb).ToList();
            bool hpSection = profilesHere.Count > 0 && profilesHere.Count(p => !p.IsFb) * 2 >= profilesHere.Count;
            bool girderDraft = false;
            if (hpSection && webPts.Count >= 2)
            {
                // Nẹp HP: chuỗi bước tự sinh giữa 2 girder ngoài cùng — tờ tay X-X: 500, 775…, 660.5 (không dùng nháp).
                var chain = new List<double> { wFirst };
                chain.AddRange(pts.Where(p => !p.IsFb && p.X > wFirst + 1e-6 && p.X < wLast - 1e-6).Select(p => p.X));
                chain.Add(wLast);
                chain = Dedupe(chain, Gap);
                for (int k = 1; k < chain.Count; k++) Dim(chain[k - 1], chain[k], lvPitch, true, k > 1, k > 1 ? null : V);
                // FB giữa 2 girder: tới mặt girder gần nhất, mức đầu mút (tờ tay: 310.5).
                foreach (var fb in pts.Where(p => p.IsFb && p.X > wFirst && p.X < wLast))
                {
                    double w = webPts.OrderBy(x => Math.Abs(x - fb.X)).First();
                    Dim(w, fb.X, lvEnds, true, true, V);
                }
            }
            else
            {
                // Nẹp FB: chép nháp Structure dọc mặt cắt (tờ tay Y-Y: 500, 2045), bỏ nháp đo bề rộng flange.
                var webCentres = webs.Select(w => (c: Pos(w), half: w.Thickness / 2 + 2)).ToList();
                bool OnWeb(double x) => webCentres.Any(w => Math.Abs(x - w.c) <= w.half);
                bool OnMember(double x) => x <= 2 || x >= len - 2 || OnWeb(x) || pts.Any(p => Math.Abs(x - p.X) <= 10);   // mép tấm / web / profile (mặt lưng ± dày)
                var flangeSpans = flanges.Select(f => (lo: f.r.X0, hi: f.r.X1)).ToList();
                foreach (var d in input.Drafts.Where(d => d.Source == "Structure"))
                {
                    bool along = isX ? Math.Abs(Math.Sin(d.Rotation)) < 0.01 : Math.Abs(Math.Cos(d.Rotation)) < 0.01;
                    if (!along) continue;
                    double a = (isX ? d.X1 : d.Y1) - lo, b = (isX ? d.X2 : d.Y2) - lo;
                    if (Math.Min(a, b) < -1 || Math.Max(a, b) > len + 1) continue;
                    if (flangeSpans.Any(f => Math.Abs(Math.Min(a, b) - f.lo) < 1 && Math.Abs(Math.Max(a, b) - f.hi) < 1)) continue;   // bề rộng flange
                    bool girder = OnWeb(a) && OnWeb(b);
                    girderDraft |= girder;
                    if (Math.Abs(b - a) <= Gap) continue;
                    if (!OnMember(a) || !OnMember(b)) continue;   // nháp đo tới thứ không có trên mặt cắt (tờ 17C: 800 tới x 359.1) — bỏ
                    if (girder) Dim(a, b, lvGirder, !isX, d.Tmove, string.IsNullOrEmpty(d.Text) ? null : d.Text);
                    else Dim(a, b, lvPitch, true, false, V);
                }
                // Nẹp nằm ngoài girder biên: tới mặt girder biên, mức girder (tờ tay Y-Y: 350.2).
                if (girderDraft && webPts.Count > 0)
                    foreach (var p in pts.Where(p => !p.IsWeb && (p.X < wFirst || p.X > wLast)))
                        Dim(p.X < wFirst ? wFirst : wLast, p.X, lvGirder, !isX, false, V);
            }
            if (!girderDraft && webPts.Count > 0)
            {
                // Chuỗi girder tự sinh từ đầu tấm (tờ tay X-X: 290, 15110.5).
                var chain = new List<double> { 0 };
                chain.AddRange(webPts);
                chain = Dedupe(chain, Gap);
                for (int k = 1; k < chain.Count; k++) Dim(chain[k - 1], chain[k], lvGirder, !isX, k > 1, k > 1 ? null : V);   // tờ 17C: X-X không xiên, Y-Y xiên
            }

            // Chiều cao mặt cắt: X-X bên trái đầu tấm (tới đáy flange girder đầu); Y-Y phía trên cuối tấm (tới đáy flange girder cuối).
            // + thk_sym ở 2 đầu kích thước này (tờ tay: X-X xoay 90°, lật ở đầu mặt tấm; Y-Y xoay 0°, lật ở đầu đáy flange).
            var fl = flanges.OrderBy(f => (f.r.X0 + f.r.X1) / 2).ToList();
            if (fl.Count > 0)
            {
                var r = isX ? fl[0].r : fl[fl.Count - 1].r;
                double ax = isX ? 0 : len, bx = isX ? r.X0 : r.X1, lx = isX ? -PlanViewLayout.SecXHeightLeft * sc : len + PlanViewLayout.SecYHeightAbove * sc;
                var p1 = ToSheet(sec, ax, plateTop); var p2 = ToSheet(sec, bx, r.Y0); var ln = ToSheet(sec, lx, plateTop);
                sec.Dims.Add(new PvDim { Space = PvSpace.Sheet, X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y, LineX = ln.X, LineY = ln.Y,
                                         Rotation = sec.Rotation + Math.PI / 2, Layer = Layer, Tmove = false, TextOverride = V });
                double outward = (isX ? -2.4 : 2.0) * sc;   // ra ngoài đầu tấm (X-X: trái, Y-Y: lên)
                var e1 = ToSheet(sec, ax + outward, plateTop); var e2 = ToSheet(sec, ax + outward, r.Y0);
                double rot = isX ? Math.PI / 2 : 0;
                sec.ThkSymbols.Add(new PvInsert { Space = PvSpace.Sheet, Block = ThkBlock, X = e1.X, Y = e1.Y, Rotation = rot, Scale = 1, MirrorX = isX, Layer = "Mechanical-AM_0" });
                sec.ThkSymbols.Add(new PvInsert { Space = PvSpace.Sheet, Block = ThkBlock, X = e2.X, Y = e2.Y, Rotation = rot, Scale = 1, MirrorX = !isX, Layer = "Mechanical-AM_0" });
            }
            return sec;
        }

        /// <summary>Điểm chuỗi cách nhau ≤ 1 mm GIẤY gộp làm 1 (tờ 17C: 2 đoạn web lệch 5 mm do vát sinh kích thước rác 5.0).</summary>
        private static List<double> Dedupe(List<double> xs, double gap)
        {
            var r = new List<double>();
            foreach (var x in xs.OrderBy(x => x)) if (r.Count == 0 || x - r[r.Count - 1] > gap) r.Add(x);
            return r;
        }

        /// <summary>Mặt cắt Y-Y cách mép trái Top Plate (mm giấy): kích thước rộng nằm bên TRÁI ⇒ dời thêm để nhãn L/HL không đè nó.</summary>
        public static double YLeft(PlanViewInput input)
            => PlanViewLayout.SectionYLeft
               + (PlateGeometry.MainEdges(DraftDimRules.Outline(input), input.TopPlate).WidthOnRight ? 0 : PlanViewLayout.LeftDimShift);

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
