using System;
using System.Collections.Generic;
using System.Linq;

namespace MCG_3DPanel.Services.PlanView
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>Ký hiệu trên mặt bằng (kỹ sư 2026-11-10): chữ B / S / Kind GE…·GB… / OB… tại điểm đánh dấu; CS_symbol (chuẩn tờ 7D-17C,
    /// AM_6) tại mỗi chỗ 1 thanh (HP, FB, Web) chạy LIÊN TỤC xuyên web của thanh khác (kể cả girder qua girder, chỉ đoạn giữa), xoay theo
    /// thanh chạy liên tục (0° / 90°), KHÔNG lật; + chữ CG (Continue Girder) cạnh mỗi chỗ girder liên tục qua girder.</summary>
    public static class SymbolRules
    {
        public const string LayerMarks = "Mechanical-AM_6";
        public const string LayerContinuous = "Mechanical-AM_6";
        public const string ContinuousBlock = "CS_symbol";
        public const string ContinueGirderText = "CG";
        private const double EndTol = 1.0;   // mm: giao điểm tính là "trên web" khi lệch đầu web ≤ 1 + nửa dày thanh chạy qua
        private const double SameCross = 50;   // mm: 2 giao điểm cùng tuyến gần hơn ⇒ 1

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
            => Crossings(input).Select(c => new PvInsert
            {
                Space = PvSpace.Block, Block = ContinuousBlock, Layer = LayerContinuous, X = c.X, Y = c.Y, Rotation = c.Rot, Scale = 1,
            }).ToList();

        /// <summary>Chữ CG cạnh mỗi chỗ girder liên tục cắt qua girder khác (kỹ sư 2026-11-10). Tờ 7D-17C: dịch 0.9 mm dọc girder, lệch
        /// 3.2 mm (giữa chữ) về phía girder SONG SONG gần nhất — L2 ghi phía trên, L3 phía dưới.</summary>
        public static List<PvText> ContinueGirder(PlanViewInput input, int scale)
        {
            var webs = input.Members.Where(m => !m.Deleted && m.Kind == "Web").ToList();
            var r = new List<PvText>();
            foreach (var c in Crossings(input).Where(c => c.Girder))
            {
                double ux = Math.Cos(c.Rot), uy = Math.Sin(c.Rot), nx = -uy, ny = ux;   // dọc / pháp tuyến girder liên tục
                double side = 1, best = double.MaxValue;
                foreach (var w in webs.Where(w => w.Line != c.Line))
                {
                    double wx = w.X1 - w.X0, wy = w.Y1 - w.Y0, l = Math.Sqrt(wx * wx + wy * wy);
                    if (l < 1e-6 || Math.Abs(wx * uy - wy * ux) / l > 0.02) continue;   // không song song
                    double d = (w.X0 - c.X) * nx + (w.Y0 - c.Y) * ny;
                    if (Math.Abs(d) > 1 && Math.Abs(d) < best) { best = Math.Abs(d); side = Math.Sign(d); }
                }
                double off = PlanViewLayout.CgSide * scale * side, along = PlanViewLayout.CgAlong * scale;
                r.Add(new PvText
                {
                    Space = PvSpace.Block, Text = ContinueGirderText, Layer = LayerMarks, PaperHeight = PlanViewLayout.TextStiffener,
                    Justify = "MiddleLeft", Category = "Symbol",
                    X = c.X + ux * along + nx * off, Y = c.Y + uy * along + ny * off,
                });
            }
            return r;
        }

        private static List<(int Line, double X, double Y, double Rot, bool Girder)> Crossings(PlanViewInput input)
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
            var r = new List<(int Line, double X, double Y, double Rot, bool Girder)>();

            // Web tuyến A bị tách đoạn TẠI web B (tờ 17C: L2 tách ở T4) vẫn là liên tục khi FLANGE cùng tuyến chạy suốt qua giao điểm
            // (web 2 tuyến đều tách thì không phân biệt được — flange quyết định).
            var flanges = input.Members.Where(m => !m.Deleted && m.Kind == "Flange").ToList();
            bool FlangeThrough(PvMember a, double px, double py, double bThk)
            {
                double ax = a.X1 - a.X0, ay = a.Y1 - a.Y0, l = Math.Sqrt(ax * ax + ay * ay); ax /= l; ay /= l;
                double need = bThk + 20, p = px * ax + py * ay;
                return flanges.Any(c => c.Line == a.Line
                    && Math.Abs(-(c.X0 - px) * ay + (c.Y0 - py) * ax) <= 20 && Math.Abs(-(c.X1 - px) * ay + (c.Y1 - py) * ax) <= 20
                    && Math.Min(c.X0 * ax + c.Y0 * ay, c.X1 * ax + c.Y1 * ay) <= p - need && Math.Max(c.X0 * ax + c.Y0 * ay, c.X1 * ax + c.Y1 * ay) >= p + need);
            }

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
                    if ((t * lenA <= pass || (1 - t) * lenA <= pass) && !(a.Kind == "Web" && FlangeThrough(a, a.X0 + t * ax, a.Y0 + t * ay, b.Thickness))) continue;   // A dừng tại B (trừ khi flange A chạy suốt qua)
                    double px = a.X0 + t * ax, py = a.Y0 + t * ay;
                    if (a.Kind == "Web" && !(Inner(a.Line, px, py) && Inner(b.Line, px, py))) continue;   // girder × girder ở đầu mút: không ký hiệu
                    if (r.Any(q => q.Line == a.Line && (q.X - px) * (q.X - px) + (q.Y - py) * (q.Y - py) < SameCross * SameCross)) continue;   // tờ 17C: web tách đoạn lệch 7.5 mm ⇒ 1 ký hiệu
                    r.Add((a.Line, px, py, rot, a.Kind == "Web" && b.Kind == "Web"));
                }
            }
            return r;
        }
    }
}
