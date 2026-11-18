using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;

namespace MCG_3DPanel.Services.Detail
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.*.

    /// <summary>Tấm bị mặt cắt cắt qua.</summary>
    public sealed class CutMember
    {
        public string Type;
        /// <summary>Khoảng solid dọc hướng nhìn, tương đối so với mặt phẳng cắt.</summary>
        public double MinSigned, MaxSigned;
        public double Thickness;
        /// <summary>Tâm vết cắt và trục dọc chiều cao tấm, toạ độ section.</summary>
        public P2 CutCenter, CutAxis;
        /// <summary>Chiều dài vết cắt dọc <see cref="CutAxis"/>.</summary>
        public double CutLength;
    }

    /// <summary>Tấm song song với mặt cắt (dùng đặt dấu $).</summary>
    public sealed class ParallelPlate
    {
        public string Type;
        public bool Visible;
        /// <summary>Đoạn viền nhìn thấy dài nhất, toạ độ section.</summary>
        public P2 OutlineA, OutlineB;
    }

    public static class SectionSymbolRules
    {
        /// <summary>Tấm liên tục: không phải HP và nằm cả hai phía mặt phẳng cắt.</summary>
        public static bool IsContinuous(CutMember m, double tol = 1.0)
            => m.Type != "HP" && m.MinSigned < -tol && m.MaxSigned > tol;

        /// <summary>Vết cắt đứng (trục tấm lệch phương đứng ≤ <paramref name="maxDeg"/>). Bản vẽ tay chỉ đặt Continuous cho tấm
        /// đứng (web, bracket…), mũi tên nằm ngang — không cho tấm boong / face plate (kỹ sư 2026-11-18).</summary>
        public static bool IsUpright(CutMember m, double maxDeg = 30)
        {
            double len = m.CutAxis.Length;
            return len > 1e-12 && Math.Abs(m.CutAxis.Y) / len >= Math.Cos(maxDeg * Math.PI / 180);
        }

        public static string ThicknessText(double t)
            => "(" + t.ToString("0.0", CultureInfo.InvariantCulture) + ")";

        /// <summary>Đặt ký hiệu tại tâm vết cắt, xoay vuông góc với tấm.</summary>
        public static (P2 at, double rotation) ContinuousPlacement(CutMember m)
        {
            // Trục quay lên (Y ≥ 0) ⇒ tấm đứng luôn ra 180° như mọi ký hiệu trên bản vẽ tay
            P2 ax = m.CutAxis.Y < 0 || (m.CutAxis.Y == 0 && m.CutAxis.X < 0) ? m.CutAxis * -1 : m.CutAxis;
            double r = Math.Atan2(ax.Y, ax.X) + Math.PI / 2;
            double twoPi = 2 * Math.PI;
            r %= twoPi;
            if (r < 0) r += twoPi;
            return (m.CutCenter, r);
        }

        /// <summary>Dấu $: tâm chữ ĐÚNG trung điểm nét A–B (nét đi xuyên qua chữ, như 26 dấu $ trên bản vẽ tay);
        /// <paramref name="gap"/> &gt; 0 đẩy sang bên phải chiều A→B. null nếu tấm ẩn.</summary>
        /// <summary>Loại tấm song song mặt cắt được đặt "$": không phải HP/FB và không phải tấm nằm ngang
        /// (tấm boong, face plate, flange) — kỹ sư 2026-11-18.</summary>
        public static bool TakesWeldTrace(string type) =>
            type != "HP" && type != "FlatBar" && type != "TopPlate" && type != "FacePlate" && type != "Flange";

        /// <summary>Nét đặt "$" (kỹ sư 2026-11-18): nét ĐỨNG (lệch trục Y ≤ maxDeg) của tấm song song — nét nối giữa hai tấm
        /// song song hoặc nét đầu tấm — trừ nét trùng cạnh HP/FB (<paramref name="blockers"/>). Nét cùng chỗ (trung điểm cách
        /// ≤ sameTol theo X — vd một nét nối bị che đứt làm hai khúc) chỉ giữ khúc dài nhất. Nét ngắn hơn minLen bị bỏ.</summary>
        public static List<(P2 A, P2 B)> WeldTraceLines(IEnumerable<IList<P2>> candidates, IList<IList<P2>> blockers,
            double tol = 0.5, double step = 2.0, double maxDeg = 30, double minLen = 20, double sameTol = 1.0)
        {
            double cosMax = Math.Cos(maxDeg * Math.PI / 180);
            var lines = new List<(P2 A, P2 B)>();
            foreach (var c in candidates)
            {
                if (c.Count < 2) continue;
                P2 a = c[0], b = c[c.Count - 1];
                var d = b - a;
                double len = d.Length;
                if (len < minLen || Math.Abs(d.Y) / len < cosMax) continue;
                if (blockers.Count > 0 && SectionCurveRules.Covers(SectionCurveRules.Densify(c, step), blockers, tol)) continue;
                lines.Add(a.Y <= b.Y ? (a, b) : (b, a));
            }
            var keep = new List<(P2 A, P2 B)>();
            foreach (var l in lines.OrderByDescending(l => (l.B - l.A).Length))
            {
                double mx = (l.A.X + l.B.X) / 2;
                bool dup = keep.Any(k => Math.Abs((k.A.X + k.B.X) / 2 - mx) <= sameTol);
                if (!dup) keep.Add(l);
            }
            return keep;
        }

        public static P2? WeldTracePlacement(ParallelPlate p, double gap = 0)
        {
            if (!p.Visible) return null;
            P2 mid = (p.OutlineA + p.OutlineB) * 0.5;
            P2 d = p.OutlineB - p.OutlineA;
            double len = d.Length;
            if (len < 1e-9) return mid;
            P2 right = new P2(d.Y / len, -d.X / len);
            return mid + right * gap;
        }
    }
}
