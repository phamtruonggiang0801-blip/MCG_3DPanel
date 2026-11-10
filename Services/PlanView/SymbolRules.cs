using System;
using System.Collections.Generic;
using System.Linq;

namespace MCG_3DPanel.Services.PlanView
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>Ký hiệu trên mặt bằng (kỹ sư 2026-11-10): chữ B / S / Kind GE…·GB… / OB… tại điểm đánh dấu; CS_horl_up_right tại
    /// mỗi chỗ 1 thanh (HP, FB, Web) chạy LIÊN TỤC xuyên web của thanh khác, đặt về phía chiều đổ kết cấu (Position).</summary>
    public static class SymbolRules
    {
        public const string LayerMarks = "Mechanical-AM_6";
        public const string LayerContinuous = "Mechanical-AM_0";
        public const string ContinuousBlock = "CS_horl_up_right";
        private const double EndTol = 1.0;   // mm: giao điểm tính là "trên web" khi lệch đầu web ≤ 1

        public static List<PvText> Marks(PlanViewInput input, int scale)
            => input.Marks.Select(m => new PvText
            {
                Space = PvSpace.Block, Text = m.Text, Layer = LayerMarks, PaperHeight = PlanViewLayout.TextStiffener,
                Justify = "MiddleCenter", Category = "Symbol",
                X = m.X + m.DirX * PlanViewLayout.MarkOffset * scale, Y = m.Y + m.DirY * PlanViewLayout.MarkOffset * scale,
            }).ToList();

        public static List<PvInsert> Continuous(PlanViewInput input)
        {
            var tp = input.TopPlate;
            double centreY = (tp.MinY + tp.MaxY) / 2;
            var live = input.Members.Where(m => !m.Deleted && m.Kind != "Flange").ToList();
            var webs = live.Where(m => m.Kind == "Web").ToList();
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
                    if (!seen.Add((a.Line, (long)Math.Round(px), (long)Math.Round(py)))) continue;

                    // Phía chiều đổ: web T (đứng) ⇒ +X; web L ⇒ Port −Y, Starboard +Y, Center về tâm panel.
                    double wantX, wantY;
                    if (b.Dir == "T") { wantX = 1; wantY = 0; }
                    else { wantX = 0; wantY = input.Position == 1 ? -1 : input.Position == 2 ? 1 : (py <= centreY ? 1 : -1); }
                    bool mirror = Math.Cos(rot) * wantX + Math.Sin(rot) * wantY < 0;

                    r.Add(new PvInsert
                    {
                        Space = PvSpace.Block, Block = ContinuousBlock, Layer = LayerContinuous,
                        X = px, Y = py, Rotation = rot, Scale = 1, MirrorX = mirror,
                    });
                }
            }
            return r;
        }
    }
}
