using System;
using System.Collections.Generic;
using System.Linq;

namespace MCG_3DPanel.Services.Detail
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.*.

    /// <summary>Luật vạch mặt cắt AM_8 và hình chữ nhật bao nhỏ nhất.</summary>
    public static class SectionMarkRules
    {
        /// <summary>Khoảng cách các vạch tính từ một mặt; số vạch = max(1, round(t/6)), chia đều t.</summary>
        public static double[] Offsets(double t)
        {
            int n = Math.Max(1, (int)Math.Round(t / 6.0, MidpointRounding.AwayFromZero));
            var r = new double[n];
            for (int k = 1; k <= n; k++) r[k - 1] = t * k / (n + 1);
            return r;
        }

        /// <summary>Tấm phẳng: lấp đầy ≥95% hình chữ nhật bao và dài ≥ 2× rộng.</summary>
        public static bool IsPlateLike(double area, double width, double length) =>
            area >= 0.95 * width * length && length >= 2 * width;

        public static (P2 origin, P2 axis, double width, double length) MinAreaRect(IList<P2> pts)
        {
            var hull = Hull(pts);
            double best = double.MaxValue;
            (P2, P2, double, double) res = (default(P2), new P2(1, 0), 0, 0);
            int n = hull.Count;
            for (int i = 0; i < n; i++)
            {
                var e = hull[(i + 1) % n] - hull[i];
                double len = e.Length;
                if (len < 1e-12) continue;
                var u = e * (1 / len);
                var v = new P2(-u.Y, u.X);
                double u0 = double.MaxValue, u1 = double.MinValue, v0 = double.MaxValue, v1 = double.MinValue;
                foreach (var h in hull)
                {
                    double a = h.Dot(u), b = h.Dot(v);
                    if (a < u0) u0 = a; if (a > u1) u1 = a;
                    if (b < v0) v0 = b; if (b > v1) v1 = b;
                }
                double du = u1 - u0, dv = v1 - v0, area = du * dv;
                if (area >= best) continue;
                best = area;
                var o = u * u0 + v * v0;
                res = du >= dv ? (o, u, dv, du) : (o, v, du, dv);
            }
            return res;
        }

        // Bao lồi (monotone chain), CCW, không lặp điểm đầu.
        static List<P2> Hull(IList<P2> pts)
        {
            var s = pts.OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
            if (s.Count < 3) return s;
            double Cross(P2 o, P2 a, P2 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
            var h = new List<P2>();
            foreach (var p in s)
            {
                while (h.Count >= 2 && Cross(h[h.Count - 2], h[h.Count - 1], p) <= 0) h.RemoveAt(h.Count - 1);
                h.Add(p);
            }
            int lower = h.Count + 1;
            for (int i = s.Count - 2; i >= 0; i--)
            {
                var p = s[i];
                while (h.Count >= lower && Cross(h[h.Count - 2], h[h.Count - 1], p) <= 0) h.RemoveAt(h.Count - 1);
                h.Add(p);
            }
            h.RemoveAt(h.Count - 1);
            return h;
        }
    }
}
