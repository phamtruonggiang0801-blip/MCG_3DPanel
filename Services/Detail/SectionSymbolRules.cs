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

    /// <summary>Chữ chiều dày "(t)" của một tấm song song mặt cắt nhìn thấy: tâm ứng viên (toạ độ section) theo thứ tự ưu tiên,
    /// engine đã kiểm mỗi chỗ đều nằm trên mặt tấm thấy được (không bị tấm khác che).</summary>
    public sealed class ThicknessLabel
    {
        public string Text;
        public List<P2> Spots = new List<P2>();
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
        /// song song hoặc nét đầu tấm — trừ đầu tấm nối HP/FB hay tì vào tấm bị cắt (<paramref name="blockers"/> = cạnh HP/FB +
        /// viền vết cắt): nét trùng cạnh đó, hoặc thẳng hàng và chạm cạnh đó
        /// (vd đầu vách chạy dưới HP treo ở 9A-9A). Nét gần nhau (trung điểm cách ≤ nearTol theo X — vd nét nối bị che đứt,
        /// hay nét bắt đầu vát 1:4 cách mối nối 16 mm ở 9B-9B) chỉ giữ một: ưu tiên nét NỐI (<c>Joint</c> = trùng cạnh tấm
        /// song song khác), rồi nét dài nhất. Nét ngắn hơn minLen bị bỏ.
        /// <para>Mẫu tay CAS-0062239 (dump 2026-11-19, 7 dấu "$"): nét luôn ĐỨNG và chạy SUỐT từ tấm trên xuống tấm dưới.
        /// <paramref name="supports"/> (viền vết cắt + cạnh tấm nằm ngang) ≠ null ⇒ 2 đầu nét (sau khi nối các khúc thẳng hàng
        /// bị che đứt) đều phải chạm một support — loại mép tự do xiên của bracket, nét ngắn dưới HP. Hai nét cách ≤ nearTol
        /// (mặc định 65: vát 1:4 tới chênh dày 15 mm) chỉ một "$".</para></summary>
        public static List<(P2 A, P2 B)> WeldTraceLines(IEnumerable<(IList<P2> Pts, bool Joint)> candidates,
            IList<IList<P2>> blockers, double tol = 0.5, double step = 2.0, double maxDeg = 10, double minLen = 20,
            double nearTol = 65.0, IList<IList<P2>> supports = null, double supportTol = 1.0, double joinGap = 30.0)
        {
            double cosMax = Math.Cos(maxDeg * Math.PI / 180);
            var pieces = new List<(P2 A, P2 B, bool Joint)>();
            foreach (var (c, joint) in candidates)
            {
                if (c.Count < 2) continue;
                P2 a = c[0], b = c[c.Count - 1];
                var d = b - a;
                double len = d.Length;
                if (len < 1e-9 || Math.Abs(d.Y) / len < cosMax) continue;
                if (blockers.Count > 0 && SectionCurveRules.Covers(SectionCurveRules.Densify(c, step), blockers, tol)) continue;
                if (blockers.Any(bl => TouchesInLine(a, b, bl, tol))) continue;
                pieces.Add(a.Y <= b.Y ? (a, b, joint) : (b, a, joint));
            }
            // Nối các khúc thẳng hàng (cùng X ± 2, khe ≤ joinGap) — nét bị che đứt vẫn là một nét suốt
            var lines = new List<(P2 A, P2 B, bool Joint)>();
            foreach (var p in pieces.OrderBy(p => p.A.X).ThenBy(p => p.A.Y))
            {
                int k = lines.FindIndex(l => Math.Abs((l.A.X + l.B.X) / 2 - (p.A.X + p.B.X) / 2) <= 2.0
                                             && p.A.Y <= l.B.Y + joinGap && p.B.Y >= l.A.Y - joinGap);
                if (k < 0) { lines.Add(p); continue; }
                var l0 = lines[k];
                lines[k] = (l0.A.Y <= p.A.Y ? l0.A : p.A, l0.B.Y >= p.B.Y ? l0.B : p.B, l0.Joint || p.Joint);
            }
            lines = lines.Where(l => (l.B - l.A).Length >= minLen).ToList();
            if (supports != null)
                lines = lines.Where(l => supports.Any(s => SectionCurveRules.DistanceToPolyline(l.A, s) <= supportTol)
                                      && supports.Any(s => SectionCurveRules.DistanceToPolyline(l.B, s) <= supportTol)).ToList();
            var keep = new List<(P2 A, P2 B)>();
            foreach (var l in lines.OrderByDescending(l => l.Joint).ThenByDescending(l => (l.B - l.A).Length))
            {
                double mx = (l.A.X + l.B.X) / 2;
                bool dup = keep.Any(k => Math.Abs((k.A.X + k.B.X) / 2 - mx) <= nearTol);
                if (!dup) keep.Add((l.A, l.B));
            }
            return keep;
        }

        /// <summary>Đoạn a–b nằm trên cùng đường thẳng với một đoạn của <paramref name="poly"/> (cách ≤ tol) và chạm/chồng
        /// lên đoạn đó (khe ≤ tol).</summary>
        static bool TouchesInLine(P2 a, P2 b, IList<P2> poly, double tol)
        {
            var ab = b - a;
            double l = ab.Length;
            if (l < 1e-9) return false;
            var u = ab * (1 / l);
            var n = new P2(-u.Y, u.X);
            for (int i = 1; i < poly.Count; i++)
            {
                P2 p = poly[i - 1], q = poly[i];
                if ((q - p).Length < 1e-9) continue;
                if (Math.Abs((p - a).Dot(n)) > tol || Math.Abs((q - a).Dot(n)) > tol) continue;
                double s0 = (p - a).Dot(u), s1 = (q - a).Dot(u);
                if (Math.Max(s0, s1) >= -tol && Math.Min(s0, s1) <= l + tol) return true;
            }
            return false;
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
