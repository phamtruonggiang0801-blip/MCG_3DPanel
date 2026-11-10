using System;
using System.Collections.Generic;
using System.Linq;

namespace MCG_3DPanel.Services.PlanView
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>Kích thước theo tờ mẫu 7D-17P (so sánh 2026-11-10):
    /// • mặt bằng chính CHỈ có 2 kích thước bao (dài dưới, rộng trái, chữ ±4 TOP PLATE);
    /// • nháp Lashing Hole chỉ ghi trên mặt bằng lỗ lashing: GIỮ điểm đo của nháp, đường kích thước ra chuỗi trái / trên tấm;
    ///   nháp có 1 đầu nằm trên đường bao tấm ⇒ AM_5, còn lại AM_4; + Ø lỗ lashing;
    /// • nháp Structure ghi trên mặt cắt — xem <see cref="SectionRules"/>.</summary>
    public static class DraftDimRules
    {
        public const string LayerLashing = "Mechanical-AM_4";
        public const string LayerStructure = "Mechanical-AM_5";
        public const string OverallText = "<>{}{\\H0.71x;\\C3;%%p4}{{}}TOP PLATE";
        public const string LashingText = "<>-LASHING HOLES";

        /// <summary>Kích thước bao trên mặt bằng chính (hệ block). Đường bao rỗng ⇒ góc khung bao.</summary>
        public static List<PvDim> Overall(PlanViewInput input, int scale)
        {
            var tp = input.TopPlate;
            var o = Outline(input);
            var (l1, l2) = PlateGeometry.OverallLength(o);
            var (w1, w2) = PlateGeometry.OverallWidth(o);
            return new List<PvDim>
            {
                new PvDim
                {
                    Space = PvSpace.Block, X1 = l1.Item1, Y1 = l1.Item2, X2 = l2.Item1, Y2 = l2.Item2,
                    LineX = l2.Item1, LineY = tp.MinY - PlanViewLayout.OverallBelow * scale,
                    Rotation = 0, Layer = LayerStructure, TextOverride = OverallText, Tmove = false,
                },
                new PvDim
                {
                    Space = PvSpace.Block, X1 = w1.Item1, Y1 = w1.Item2, X2 = w2.Item1, Y2 = w2.Item2,
                    LineX = tp.MinX - PlanViewLayout.OverallLeft * scale, LineY = w2.Item2,
                    Rotation = Math.PI / 2, Layer = LayerStructure, TextOverride = OverallText, Tmove = false,
                },
            };
        }

        /// <summary>Kích thước mặt bằng lỗ lashing (hệ block, dời <paramref name="shift"/> = độ lệch giữa lần chèn thứ 2 và _Assy).</summary>
        public static List<PvDim> Lashing(PlanViewInput input, int scale, (double dx, double dy) shift)
        {
            var tp = input.TopPlate;
            var o = Outline(input);
            var r = new List<PvDim>();
            var lash = input.Drafts.Where(x => x.Source == "LashingHole").ToList();
            double lineTop = tp.MaxY + PlanViewLayout.LashChainTop * scale, lineLeft = tp.MinX - PlanViewLayout.LashChainLeft * scale;
            double cx = (tp.MinX + tp.MaxX) / 2, cy = (tp.MinY + tp.MaxY) / 2;
            // Cột / hàng lỗ (để đặt kích thước cục bộ giữa 2 cột/hàng): từ tâm lỗ, không có thì từ đầu mút nháp.
            var holes = input.Lashing?.Holes.Count > 0 ? input.Lashing.Holes.ToList()
                      : lash.SelectMany(d => new[] { (X: d.X1, Y: d.Y1), (X: d.X2, Y: d.Y2) }).ToList();
            var cols = Distinct(holes.Select(h => h.X)); var rows = Distinct(holes.Select(h => h.Y));
            PvDim Add(double x1, double y1, double x2, double y2, double lx, double ly, bool horiz, string layer, string text, bool tmove, bool oblique = false)
            {
                var dim = new PvDim
                {
                    Space = PvSpace.Block, X1 = x1 + shift.dx, Y1 = y1 + shift.dy, X2 = x2 + shift.dx, Y2 = y2 + shift.dy,
                    LineX = lx + shift.dx, LineY = ly + shift.dy, Rotation = horiz ? 0 : Math.PI / 2,
                    Layer = layer, TextOverride = text, Tmove = tmove, Oblique = oblique,
                };
                r.Add(dim);
                return dim;
            }

            var chainH = new List<PvDraft>(); var chainV = new List<PvDraft>();
            foreach (var d in lash)
            {
                bool horiz = Math.Abs(Math.Sin(d.Rotation)) < 0.01, vert = Math.Abs(Math.Cos(d.Rotation)) < 0.01;
                if (!horiz && !vert) continue;
                // Nháp "cục bộ": chồng lên 1 nháp DÀI hơn cùng trục (tờ 7D-17P: 480, 395.5, 470) ⇒ trong tấm, đường kích thước GIỮA
                // hàng/cột lỗ của nó và hàng/cột lỗ kế tiếp phía trong tấm; AM_5, "<>".
                var iv = Interval(d, horiz);
                bool local = lash.Any(e => e != d && SameAxis(e, horiz) && Overlap(iv, Interval(e, horiz)) > Touch && Len(e) > Len(d) + Touch);
                if (local)
                {
                    double at = horiz ? d.Y1 : d.X1, inward = horiz ? Math.Sign(cy - at) : Math.Sign(cx - at);
                    var next = (horiz ? rows : cols).Where(v => (v - at) * inward > Touch).OrderBy(v => Math.Abs(v - at)).ToList();
                    double mid = next.Count > 0 ? (at + next[0]) / 2 : (horiz ? d.LineY : d.LineX);
                    Add(d.X1, d.Y1, d.X2, d.Y2, horiz ? d.X2 : mid, horiz ? mid : d.Y2, horiz, LayerStructure, "<>", false);
                    continue;
                }
                bool edge = PlateGeometry.OnOutline(o, d.X1, d.Y1) || PlateGeometry.OnOutline(o, d.X2, d.Y2);
                (horiz ? chainH : chainV).Add(d);
                Add(d.X1, d.Y1, d.X2, d.Y2, horiz ? d.X2 : lineLeft, horiz ? lineTop : d.Y2, horiz,
                    edge ? LayerStructure : LayerLashing, string.IsNullOrEmpty(d.Text) ? null : d.Text, d.Tmove, d.Oblique);
            }

            // Chuỗi chưa tới mép tấm ⇒ kích thước THAM KHẢO "(<>)" từ lỗ cuối tới mép (tờ 7D-17P: 290 bên phải chuỗi ngang).
            // Đoạn cuối chuỗi tới mép phải / mép dưới ghi "(<>)" (tờ tay: 290, 139.9).
            void Close(List<PvDraft> chain, bool horiz)
            {
                if (chain.Count == 0) return;
                double row = horiz ? chain[0].Y1 : chain[0].X1;
                var ends = chain.SelectMany(d => horiz ? new[] { d.X1, d.X2 } : new[] { d.Y1, d.Y2 }).ToList();
                double lo = ends.Min(), hi = ends.Max();
                var (edgeLo, edgeHi) = PlateGeometry.Cut(o, horiz ? 'X' : 'Y', row, tp);
                var dims = r.Where(x => x.Kind == "Rotated" && chain.Any(d => Math.Abs(d.X1 + shift.dx - x.X1) < 1e-6 && Math.Abs(d.Y1 + shift.dy - x.Y1) < 1e-6
                                                                          && Math.Abs(d.X2 + shift.dx - x.X2) < 1e-6 && Math.Abs(d.Y2 + shift.dy - x.Y2) < 1e-6)).ToList();
                double lineX = horiz ? 0 : lineLeft, lineY = horiz ? lineTop : 0;
                // phía "cuối": phải (ngang) / dưới (dọc)
                if (horiz ? hi < edgeHi - Touch : lo > edgeLo + Touch)
                {
                    double from = horiz ? hi : lo, to = horiz ? edgeHi : edgeLo;
                    Add(horiz ? from : row, horiz ? row : from, horiz ? to : row, horiz ? 0 : to,
                        horiz ? to : lineX, horiz ? lineY : to, horiz, LayerStructure, "(<>)", false);
                }
                else
                {
                    double endV = horiz ? hi : lo;
                    var last = dims.FirstOrDefault(x => Math.Abs((horiz ? Math.Max(x.X1, x.X2) : Math.Min(x.Y1, x.Y2)) - (endV + (horiz ? shift.dx : shift.dy))) < Touch);
                    if (last != null && last.Layer == LayerStructure) { last.TextOverride = "(<>)"; last.Tmove = false; }
                }
            }
            Close(chainH, true);
            Close(chainV, false);

            // COG: đo từ góc tham chiếu (mép phải tấm, y 0 của block) tới vòng tròn COG — dọc bên phải, ngang ở chuỗi ngoài phía trên.
            if (input.Cog is (double gx, double gy, double gr))
            {
                Add(tp.MaxX, 0, gx + gr, gy, tp.MaxX + PlanViewLayout.LashCogRight * scale, gy, false, LayerStructure, "<>", false);
                Add(tp.MaxX, 0, gx, gy + gr, gx, tp.MaxY + PlanViewLayout.LashCogTop * scale, true, LayerStructure, null, false);
            }

            if (input.Lashing != null)
            {
                var h = input.Lashing; double rad = h.Diameter / 2;
                // Lỗ ghi Ø: gần (tâm + 2.6% bề rộng, đáy + 16.7% chiều cao) — tờ 7D-17P ghi ở lỗ giữa-dưới; chữ xiên 28°.
                double hx = h.X, hy = h.Y;
                if (h.Holes.Count > 0)
                {
                    double px = cx + PlanViewLayout.DiaPickX * (tp.MaxX - tp.MinX), py = tp.MinY + PlanViewLayout.DiaPickY * (tp.MaxY - tp.MinY);
                    var best = h.Holes.OrderBy(p => (p.X - px) * (p.X - px) + (p.Y - py) * (p.Y - py)).First();
                    hx = best.X; hy = best.Y;
                }
                double a = PlanViewLayout.DiaAngleDeg * Math.PI / 180, ux = Math.Cos(a) * rad, uy = Math.Sin(a) * rad;
                r.Add(new PvDim
                {
                    Space = PvSpace.Block, Kind = "Diametric",
                    X1 = hx + ux + shift.dx, Y1 = hy + uy + shift.dy, X2 = hx - ux + shift.dx, Y2 = hy - uy + shift.dy,
                    Layer = LayerStructure, DimStyle = "AM_ISO$3", TextOverride = LashingText, Tmove = false,
                });
            }
            return r;
        }

        private const double Touch = 1.0;
        private static List<double> Distinct(IEnumerable<double> v)
        {
            var r = new List<double>();
            foreach (var x in v.OrderBy(x => x)) if (r.Count == 0 || x - r[r.Count - 1] > Touch) r.Add(x);
            return r;
        }
        private static (double Lo, double Hi) Interval(PvDraft d, bool horiz)
            => horiz ? (Math.Min(d.X1, d.X2), Math.Max(d.X1, d.X2)) : (Math.Min(d.Y1, d.Y2), Math.Max(d.Y1, d.Y2));
        private static bool SameAxis(PvDraft e, bool horiz)
            => horiz ? Math.Abs(Math.Sin(e.Rotation)) < 0.01 : Math.Abs(Math.Cos(e.Rotation)) < 0.01;
        private static double Overlap((double Lo, double Hi) a, (double Lo, double Hi) b) => Math.Min(a.Hi, b.Hi) - Math.Max(a.Lo, b.Lo);
        private static double Len(PvDraft d) => Math.Sqrt((d.X2 - d.X1) * (d.X2 - d.X1) + (d.Y2 - d.Y1) * (d.Y2 - d.Y1));

        internal static IReadOnlyList<(double X, double Y)> Outline(PlanViewInput input)
        {
            if (input.PlateOutline.Count >= 3) return input.PlateOutline;
            var tp = input.TopPlate;
            return new List<(double X, double Y)> { (tp.MinX, tp.MinY), (tp.MaxX, tp.MinY), (tp.MaxX, tp.MaxY), (tp.MinX, tp.MaxY) };
        }
    }
}
