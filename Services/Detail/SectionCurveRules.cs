using System;
using System.Collections.Generic;

namespace MCG_3DPanel.Services.Detail
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.*.

    /// <summary>Hình học 2D cho bộ cắt section: chia cong theo sai số dây cung, gán đường thấy cho tấm, nối vết cắt thành vòng kín.</summary>
    public static class SectionCurveRules
    {
        /// <summary>Tham số chia đường cong [t0, t1]: chia đều <paramref name="seed"/> đoạn rồi chia đôi tới khi
        /// dev(ta, tm, tb) (khoảng cách điểm giữa tới dây cung) ≤ eps. Có cả t0 và t1.</summary>
        public static List<double> AdaptiveParams(double t0, double t1, Func<double, double, double, double> dev,
            double eps, int seed = 8, int maxDepth = 12)
        {
            var r = new List<double> { t0 };
            for (int i = 0; i < seed; i++)
            {
                double a = t0 + (t1 - t0) * i / seed, b = t0 + (t1 - t0) * (i + 1) / seed;
                Split(a, b, 0);
                r.Add(b);
            }
            return r;

            void Split(double a, double b, int depth)
            {
                double m = (a + b) / 2;
                if (depth >= maxDepth || dev(a, m, b) <= eps) return;
                Split(a, m, depth + 1);
                r.Add(m);
                Split(m, b, depth + 1);
            }
        }

        public static double DistanceToSegment(P2 p, P2 a, P2 b)
        {
            var ab = b - a;
            double l2 = ab.Dot(ab);
            if (l2 < 1e-24) return (p - a).Length;
            double t = Math.Max(0, Math.Min(1, (p - a).Dot(ab) / l2));
            return (p - (a + ab * t)).Length;
        }

        public static double DistanceToPolyline(P2 p, IList<P2> poly)
        {
            if (poly.Count == 1) return (p - poly[0]).Length;
            double d = double.MaxValue;
            for (int i = 1; i < poly.Count; i++) d = Math.Min(d, DistanceToSegment(p, poly[i - 1], poly[i]));
            return d;
        }

        /// <summary>Chèn điểm để mỗi đoạn ≤ step.</summary>
        public static List<P2> Densify(IList<P2> pts, double step)
        {
            var r = new List<P2>();
            if (pts.Count == 0) return r;
            r.Add(pts[0]);
            for (int i = 1; i < pts.Count; i++)
            {
                var a = pts[i - 1]; var b = pts[i];
                int n = Math.Max(1, (int)Math.Ceiling((b - a).Length / step));
                for (int k = 1; k <= n; k++) r.Add(a + (b - a) * ((double)k / n));
            }
            return r;
        }

        /// <summary>Khoảng cách lớn nhất từ các điểm tới tập polyline (mỗi điểm lấy polyline gần nhất). Tập rỗng ⇒ ∞.</summary>
        public static double MaxDistance(IList<P2> pts, IEnumerable<IList<P2>> polys, double cap = double.MaxValue)
        {
            double worst = 0;
            foreach (var p in pts)
            {
                double best = double.MaxValue;
                foreach (var poly in polys)
                {
                    if (poly.Count == 0) continue;
                    best = Math.Min(best, DistanceToPolyline(p, poly));
                    if (best == 0) break;
                }
                worst = Math.Max(worst, best);
                if (worst > cap) return worst;   // đã vượt ngưỡng, khỏi tính tiếp
            }
            return worst;
        }

        /// <summary>Mọi điểm nằm trong tol của tập polyline.</summary>
        public static bool Covers(IList<P2> pts, IEnumerable<IList<P2>> polys, double tol)
            => pts.Count > 0 && MaxDistance(pts, polys, tol) <= tol;

        /// <summary>Tấm (chỉ số) có cạnh chiếu phủ hết các điểm trong tol; nhiều tấm ⇒ tấm sát nhất; không có ⇒ -1.</summary>
        public static int Attribute(IList<P2> pts, IList<IList<IList<P2>>> members, double tol)
        {
            int best = -1;
            double bestD = double.MaxValue;
            for (int i = 0; i < members.Count; i++)
            {
                if (members[i] == null || pts.Count == 0) continue;
                double d = MaxDistance(pts, members[i], tol);
                if (d <= tol && d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>Nối các đoạn (đảo chiều khi cần) thành vòng; vòng kín không lặp điểm đầu. Chuỗi không khép ⇒ vẫn trả, đếm vào open.</summary>
        public static List<List<P2>> ChainLoops(List<List<P2>> pieces, double tol, out int open)
        {
            open = 0;
            var loops = new List<List<P2>>();
            var used = new bool[pieces.Count];
            for (int s = 0; s < pieces.Count; s++)
            {
                if (used[s] || pieces[s].Count == 0) { used[s] = true; continue; }
                used[s] = true;
                var chain = new List<P2>(pieces[s]);
                bool closed = Near(chain[0], chain[chain.Count - 1], tol) && chain.Count > 2;
                while (!closed)
                {
                    var end = chain[chain.Count - 1];
                    int hit = -1; bool rev = false;
                    for (int i = 0; i < pieces.Count && hit < 0; i++)
                    {
                        if (used[i] || pieces[i].Count == 0) continue;
                        if (Near(pieces[i][0], end, tol)) hit = i;
                        else if (Near(pieces[i][pieces[i].Count - 1], end, tol)) { hit = i; rev = true; }
                    }
                    if (hit < 0) break;
                    used[hit] = true;
                    var p = new List<P2>(pieces[hit]);
                    if (rev) p.Reverse();
                    for (int k = 1; k < p.Count; k++) chain.Add(p[k]);
                    closed = Near(chain[0], chain[chain.Count - 1], tol);
                }
                if (closed) chain.RemoveAt(chain.Count - 1);
                else open++;
                loops.Add(chain);
            }
            return loops;
        }

        static bool Near(P2 a, P2 b, double tol) => (a - b).Length <= tol;

        public static double SignedArea(IList<P2> p)
        {
            double a = 0;
            for (int i = 0; i < p.Count; i++)
            {
                var q = p[i]; var r = p[(i + 1) % p.Count];
                a += q.X * r.Y - r.X * q.Y;
            }
            return a / 2;
        }

        /// <summary>Hai hướng song song (cùng hoặc ngược chiều) trong deg độ.</summary>
        public static bool IsParallel(P2 a, P2 b, double deg)
        {
            double la = a.Length, lb = b.Length;
            if (la < 1e-12 || lb < 1e-12) return false;
            double cross = Math.Abs(a.X * b.Y - a.Y * b.X) / (la * lb);
            return cross <= Math.Sin(deg * Math.PI / 180);
        }
    }
}
