using System;
using System.Collections.Generic;

namespace MCG_3DPanel.Services.Detail
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.*.

    /// <summary>Điểm 3D trong hệ toạ độ ra: X, Y = mặt bản vẽ; Z = độ gần người nhìn (lớn hơn = gần hơn).</summary>
    public readonly struct P3
    {
        public readonly double X, Y, Z;
        public P3(double x, double y, double z) { X = x; Y = y; Z = z; }
    }

    /// <summary>Lọc nét khuất mà Section của AutoCAD bỏ sót (nét tiếp tuyến — vd nét bầu HP — không qua bước bỏ nét khuất):
    /// tìm độ sâu của nét theo cạnh Brep 3D chạy dọc nó, rồi bắn tia về phía người nhìn; trúng solid ⇒ khuất.</summary>
    public static class OcclusionRules
    {
        /// <summary>Độ gần (Z) của cạnh 3D GẦN người nhìn nhất chạy dọc nét tại <paramref name="q"/>: hình chiếu cạnh cách q ≤ tol
        /// và lệch hướng <paramref name="dir"/> ≤ maxDeg (cạnh chỉ cắt ngang nét không tính). Không có ⇒ null.</summary>
        public static double? SeamNearness(P2 q, P2 dir, IEnumerable<IList<P3>> seams, double tol, double maxDeg)
        {
            double dl = dir.Length;
            if (dl < 1e-12) return null;
            double cosMax = Math.Cos(maxDeg * Math.PI / 180);
            double? best = null;
            foreach (var s in seams)
                for (int i = 1; i < s.Count; i++)
                {
                    var a = new P2(s[i - 1].X, s[i - 1].Y);
                    var b = new P2(s[i].X, s[i].Y);
                    var ab = b - a;
                    double l2 = ab.Dot(ab);
                    if (l2 < 1e-12) continue;   // cạnh dọc hướng nhìn: chiếu thành điểm
                    if (Math.Abs(ab.Dot(dir)) / (Math.Sqrt(l2) * dl) < cosMax) continue;
                    if (SectionCurveRules.DistanceToSegment(q, a, b) > tol) continue;
                    double t = Math.Max(0, Math.Min(1, (q - a).Dot(ab) / l2));
                    double z = s[i - 1].Z + (s[i].Z - s[i - 1].Z) * t;
                    if (best == null || z > best) best = z;
                }
            return best;
        }

        /// <summary>Điểm và hướng tiếp tuyến tại độ dài cung <paramref name="s"/> trên polyline (kẹp trong [0, chiều dài]).</summary>
        public static (P2 P, P2 Dir) At(IList<P2> poly, double s)
        {
            if (poly.Count == 1) return (poly[0], new P2(1, 0));
            double acc = 0;
            for (int i = 1; i < poly.Count; i++)
            {
                var d = poly[i] - poly[i - 1];
                double l = d.Length;
                if (l < 1e-12) continue;
                if (s <= acc + l || i == poly.Count - 1)
                {
                    double t = Math.Max(0, Math.Min(1, (s - acc) / l));
                    return (poly[i - 1] + d * t, d * (1 / l));
                }
                acc += l;
            }
            return (poly[poly.Count - 1], new P2(1, 0));
        }

        public static double Length(IList<P2> poly)
        {
            double l = 0;
            for (int i = 1; i < poly.Count; i++) l += (poly[i] - poly[i - 1]).Length;
            return l;
        }

        /// <summary>Các khúc thấy [s0, s1] (theo độ dài cung) của nét dài <paramref name="length"/>. Thử điểm cách đều ≤ step,
        /// bỏ <paramref name="inset"/> ở hai đầu (đầu nét hay chạm mặt tấm khác ⇒ tia lướt mặt); chỗ đổi thấy/khuất chia đôi
        /// <paramref name="refine"/> lần. Khúc ngắn hơn minLen bị bỏ. Thấy hết ⇒ một khúc [0, length].</summary>
        public static List<(double S0, double S1)> VisibleSpans(double length, Func<double, bool> visibleAt,
            double step, double inset, int refine, double minLen)
        {
            var res = new List<(double, double)>();
            if (length <= 0) return res;
            var ss = new List<double>();
            if (length <= 2 * inset + 1e-9) ss.Add(length / 2);
            else
            {
                double a = inset, b = length - inset;
                int n = Math.Max(2, (int)Math.Ceiling((b - a) / step) + 1);
                for (int i = 0; i < n; i++) ss.Add(a + (b - a) * i / (n - 1));
            }
            var f = new bool[ss.Count];
            for (int i = 0; i < ss.Count; i++) f[i] = visibleAt(ss[i]);

            double? start = f[0] ? 0 : (double?)null;
            for (int i = 1; i < ss.Count; i++)
            {
                if (f[i] == f[i - 1]) continue;
                double lo = ss[i - 1], hi = ss[i];   // f(lo) = f[i-1], f(hi) = f[i]
                for (int k = 0; k < refine; k++)
                {
                    double m = (lo + hi) / 2;
                    if (visibleAt(m) == f[i - 1]) lo = m; else hi = m;
                }
                double cut = (lo + hi) / 2;
                if (f[i]) start = cut;
                else { if (start != null) res.Add((start.Value, cut)); start = null; }
            }
            if (start != null) res.Add((start.Value, length));
            res.RemoveAll(r => r.Item2 - r.Item1 < minLen);
            return res;
        }
    }
}
