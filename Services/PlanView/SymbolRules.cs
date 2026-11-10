using System;
using System.Collections.Generic;
using System.Linq;

namespace MCG_3DPanel.Services.PlanView
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>Ký hiệu trên mặt bằng (kỹ sư 2026-11-10): chữ B / S / Kind GE…·GB… / OB… tại điểm đánh dấu; CS_horl_up_right tại
    /// mỗi chỗ 1 thanh (HP, FB, Web) chạy LIÊN TỤC xuyên web của thanh khác (kể cả girder qua girder), xoay theo thanh chạy liên tục,
    /// KHÔNG lật (6/6 tờ mẫu: sx = 1).</summary>
    public static class SymbolRules
    {
        public const string LayerMarks = "Mechanical-AM_6";
        public const string LayerContinuous = "Mechanical-AM_0";
        public const string ContinuousBlock = "CS_horl_up_right";
        private const double EndTol = 1.0;   // mm: giao điểm tính là "trên web" khi lệch đầu web ≤ 1 + nửa dày thanh chạy qua

        /// <summary>Chữ căn giữa điểm đánh dấu (MiddleLeft như tờ tay). Mã bracket đẩy ra ngoài bracket, về phía mép tấm gần nhất.</summary>
        public static List<PvText> Marks(PlanViewInput input, int scale)
        {
            var tp = input.TopPlate;
            double cx = (tp.MinX + tp.MaxX) / 2, cy = (tp.MinY + tp.MaxY) / 2, hw = (tp.MaxX - tp.MinX) / 2, hh = (tp.MaxY - tp.MinY) / 2;
            return input.Marks.Select(m =>
            {
                if (m.AtHp)
                    return new PvText
                    {
                        Space = PvSpace.Block, Text = m.Text, Layer = LayerMarks, PaperHeight = PlanViewLayout.TextStiffener,
                        Justify = "CenterMiddleLeft", Category = "Symbol",
                        X = m.X + (m.DirX * PlanViewLayout.HpBAlong + m.PerpX * PlanViewLayout.HpBSide) * scale,
                        Y = m.Y + (m.DirY * PlanViewLayout.HpBAlong + m.PerpY * PlanViewLayout.HpBSide) * scale,
                    };
                double dx = m.DirX, dy = m.DirY, off = PlanViewLayout.MarkOffset;
                if (m.IsBracket)
                {
                    // Phương ra mép gần nhất (so theo tỉ lệ nửa kích thước tấm), tờ 7D-17P: OB ở T1 sang trái, ở T2 sang phải, hàng dưới xuống.
                    double ux = (m.X - cx) / hw, uy = (m.Y - cy) / hh;
                    if (Math.Abs(ux) >= Math.Abs(uy)) { dx = Math.Sign(ux); dy = 0; } else { dx = 0; dy = Math.Sign(uy); }
                    off = PlanViewLayout.BracketTextOut;
                }
                return new PvText
                {
                    Space = PvSpace.Block, Text = m.Text, Layer = LayerMarks, PaperHeight = PlanViewLayout.TextStiffener,
                    Justify = "CenterMiddleLeft", Category = "Symbol",
                    X = m.X + dx * off * scale, Y = m.Y + dy * off * scale,
                };
            }).ToList();
        }

        public static List<PvInsert> Continuous(PlanViewInput input)
        {
            var live = input.Members.Where(m => !m.Deleted && m.Kind != "Flange").ToList();
            var webs = live.Where(m => m.Kind == "Web").ToList();
            // Phạm vi cả TUYẾN (mọi đoạn cùng Line) theo trục tuyến — girder × girder chỉ ký hiệu ở đoạn giữa (kỹ sư 2026-11-10).
            var span = live.GroupBy(m => m.Line).ToDictionary(g => g.Key, g =>
            {
                var f = g.First(); double ux = f.X1 - f.X0, uy = f.Y1 - f.Y0, l = Math.Sqrt(ux * ux + uy * uy); ux /= l; uy /= l;
                var ps = g.SelectMany(p => new[] { p.X0 * ux + p.Y0 * uy, p.X1 * ux + p.Y1 * uy }).ToList();
                return (ux, uy, min: ps.Min(), max: ps.Max());
            });
            bool Inner(int line, double px, double py)
            {
                var s = span[line]; double p = px * s.ux + py * s.uy;
                return p - s.min >= PlanViewLayout.GirderCrossMin && s.max - p >= PlanViewLayout.GirderCrossMin;
            }
            var r = new List<PvInsert>();
            var seen = new HashSet<(int, long, long)>();

            foreach (var a in live)
            {
                double ax = a.X1 - a.X0, ay = a.Y1 - a.Y0, lenA = Math.Sqrt(ax * ax + ay * ay);
                if (lenA < 1e-6) continue;
                double rot = Math.Atan2(ay, ax);
                if (rot < 0) rot += Math.PI;
                if (rot >= Math.PI - 1e-9) rot -= Math.PI;

                foreach (var b in webs)
                {
                    if (b.Line == a.Line) continue;
                    double bx = b.X1 - b.X0, by = b.Y1 - b.Y0, lenB = Math.Sqrt(bx * bx + by * by);
                    double cross = ax * by - ay * bx;
                    if (lenB < 1e-6 || Math.Abs(cross) / (lenA * lenB) < 0.02) continue;   // song song
                    double wx = b.X0 - a.X0, wy = b.Y0 - a.Y0;
                    double t = (wx * by - wy * bx) / cross;   // trên A
                    double u = (wx * ay - wy * ax) / cross;   // trên B
                    double reach = EndTol + a.Thickness / 2;                                 // B dừng ở MẶT của A, không ở tâm
                    if (u * lenB < -reach || (1 - u) * lenB < -reach) continue;              // không cắt web B
                    double pass = Math.Max(b.Thickness, EndTol);
                    if (t * lenA <= pass || (1 - t) * lenA <= pass) continue;               // A dừng tại B
                    double px = a.X0 + t * ax, py = a.Y0 + t * ay;
                    if (a.Kind == "Web" && !(Inner(a.Line, px, py) && Inner(b.Line, px, py))) continue;   // girder × girder ở đầu mút: không ký hiệu
                    if (!seen.Add((a.Line, (long)Math.Round(px), (long)Math.Round(py)))) continue;
                    r.Add(new PvInsert
                    {
                        Space = PvSpace.Block, Block = ContinuousBlock, Layer = LayerContinuous,
                        X = px, Y = py, Rotation = rot, Scale = 1,
                    });
                }
            }
            return r;
        }
    }
}
