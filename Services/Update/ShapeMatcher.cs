using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MCG_3DPanel.Services.Detail;

namespace MCG_3DPanel.Services.Update
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.*.

    /// <summary>So khớp entity 2D cũ (block 2D trong file 3D cũ) với entity mới (block vừa DC từ Global) theo hình học, từng
    /// định nghĩa block (spec 2026-11-20 update-after-dc-block): bỏ phiếu độ lệch base point, rồi ghép trong dung sai.</summary>
    public static class ShapeMatcher
    {
        const double Grid = 0.5, AngleTol = 1e-3, BulgeTol = 1e-3, ScaleTol = 1e-6;

        public static MatchResult Match(IList<ShapeRecord> oldRecs, IList<ShapeRecord> newRecs, double tol = 0.5)
        {
            var res = new MatchResult();
            var newByBlock = newRecs.Where(r => !string.IsNullOrEmpty(r.Block))
                .GroupBy(r => r.Block, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            foreach (var g in oldRecs.Where(r => !string.IsNullOrEmpty(r.Block)).GroupBy(r => r.Block, StringComparer.OrdinalIgnoreCase))
            {
                if (!newByBlock.TryGetValue(g.Key, out var news)) continue;
                var olds = g.ToList();
                P2 off = Offset(olds, news);
                if (Math.Abs(off.X) > 1e-9 || Math.Abs(off.Y) > 1e-9) res.Shifts.Add((g.Key, off));
                MatchBlock(olds, news, off, tol, res.Map);
            }
            return res;
        }

        static void MatchBlock(List<ShapeRecord> olds, List<ShapeRecord> news, P2 off, double tol, Dictionary<string, string> map)
        {
            var used = new HashSet<ShapeRecord>();
            var byHandle = new Dictionary<string, ShapeRecord>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in news) if (!byHandle.ContainsKey(n.Handle)) byHandle[n.Handle] = n;
            var rest = new List<ShapeRecord>();
            foreach (var o in olds)   // handle không đổi + hình không đổi ⇒ chính nó
            {
                if (byHandle.TryGetValue(o.Handle, out var n) && !used.Contains(n) && Equal(o, n, off, tol))
                {
                    used.Add(n);
                    map[o.Handle] = n.Handle;
                }
                else rest.Add(o);
            }
            foreach (var o in rest)
            {
                // Handle cũ vẫn còn ⇒ cùng đối tượng, sửa tại chỗ: không được chuyển sang part khác (giữ như Update cũ — review 2026-11-20)
                if (byHandle.ContainsKey(o.Handle)) continue;
                foreach (var n in news)
                {
                    if (used.Contains(n) || !Equal(o, n, off, tol)) continue;
                    used.Add(n);
                    map[o.Handle] = n.Handle;
                    break;
                }
            }
        }

        /// <summary>Độ lệch base point: mỗi entity cũ bỏ phiếu cho các độ lệch (ô lưới 0.5) tới entity mới cùng loại/layer/cỡ. Một phiếu
        /// tính cho cả 8 ô kề (độ lệch rơi giữa 2 ô + nhiễu số thực không bị chia phiếu — review 2026-11-20). Ô thắng cần HƠN NỬA số
        /// entity cũ có ứng viên và hơn hẳn ô mạnh nhất ngoài vùng kề nó; trả trung bình độ lệch thật của các cặp trong vùng 3×3 đó.</summary>
        static P2 Offset(List<ShapeRecord> olds, List<ShapeRecord> news)
        {
            var cand = news.GroupBy(SizeKey).ToDictionary(g => g.Key, g => g.ToList());
            var pairs = new List<((long X, long Y) Bin, P2 D)>();
            var support = new Dictionary<(long, long), HashSet<int>>();
            int withCand = 0;
            for (int i = 0; i < olds.Count; i++)
            {
                if (!cand.TryGetValue(SizeKey(olds[i]), out var cs)) continue;
                withCand++;
                P2 a = MinCorner(olds[i]);
                foreach (var n in cs)
                {
                    P2 d = MinCorner(n) - a;
                    var bin = ((long)Math.Round(d.X / Grid), (long)Math.Round(d.Y / Grid));
                    pairs.Add((bin, d));
                    for (long dx = -1; dx <= 1; dx++)
                        for (long dy = -1; dy <= 1; dy++)
                        {
                            var nb = (bin.Item1 + dx, bin.Item2 + dy);
                            if (!support.TryGetValue(nb, out var set)) support[nb] = set = new HashSet<int>();
                            set.Add(i);
                        }
                }
            }
            if (withCand == 0 || support.Count == 0) return new P2(0, 0);
            var ranked = support.OrderByDescending(kv => kv.Value.Count).ToList();
            var best = ranked[0];
            if (best.Value.Count * 2 <= withCand) return new P2(0, 0);
            bool Near((long, long) a, (long, long) b, long r) => Math.Abs(a.Item1 - b.Item1) <= r && Math.Abs(a.Item2 - b.Item2) <= r;
            var rival = ranked.FirstOrDefault(kv => !Near(kv.Key, best.Key, 2));
            if (rival.Value != null && rival.Value.Count == best.Value.Count) return new P2(0, 0);
            var near = pairs.Where(p => Near(p.Bin, best.Key, 1)).ToList();
            var mean = new P2(near.Average(p => p.D.X), near.Average(p => p.D.Y));
            // Dưới 1 ô lưới (0.5 = dung sai so khớp) là nhiễu số thực, không phải dời base point (kỹ sư 2026-11-20: DC lại không dời
            // gì vẫn bị báo "moved by (-0, -0.1)").
            return mean.Length < Grid ? new P2(0, 0) : mean;
        }

        static string SizeKey(ShapeRecord r)
        {
            string size;
            switch (r.Type)
            {
                case "Polyline": case "Line": size = r.Points.Count + (r.Closed ? "c" : "o"); break;
                case "Circle": case "Arc": size = r.Scalars.Count > 0 ? Math.Round(r.Scalars[0], 1).ToString(CultureInfo.InvariantCulture) : "?"; break;
                case "BlockReference": size = r.Name ?? ""; break;
                default: size = "ext"; break;
            }
            return r.Type + "|" + r.Layer + "|" + size;
        }

        static P2 MinCorner(ShapeRecord r)
        {
            if (r.Points.Count == 0) return new P2(0, 0);
            return new P2(r.Points.Min(p => p.X), r.Points.Min(p => p.Y));
        }

        static bool Equal(ShapeRecord o, ShapeRecord n, P2 off, double tol)
        {
            if (!string.Equals(o.Type, n.Type, StringComparison.Ordinal)
                || !string.Equals(o.Layer, n.Layer, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(o.Name ?? "", n.Name ?? "", StringComparison.OrdinalIgnoreCase)
                || o.Closed != n.Closed || o.Points.Count != n.Points.Count || o.Scalars.Count != n.Scalars.Count) return false;
            if (!ScalarsEqual(o, n, tol)) return false;
            var np = n.Points.Select(p => p - off).ToList();
            int c = o.Points.Count;
            if (c == 0) return true;
            bool reversible = o.Type == "Polyline" || o.Type == "Line";
            bool cyclic = o.Type == "Polyline" && o.Closed;
            for (int dir = 0; dir < (reversible ? 2 : 1); dir++)
            {
                var (pts, bul) = dir == 0 ? (o.Points, Bulges(o)) : Reverse(o);
                for (int s = 0; s < (cyclic ? c : 1); s++)
                    if (SameSequence(pts, bul, np, Bulges(n), s, cyclic, tol)) return true;
            }
            return false;
        }

        static bool ScalarsEqual(ShapeRecord o, ShapeRecord n, double tol)
        {
            for (int i = 0; i < o.Scalars.Count; i++)
            {
                double t;
                switch (o.Type)
                {
                    case "Arc": t = i == 0 ? tol : AngleTol; break;
                    case "BlockReference": t = i == 0 ? AngleTol : ScaleTol; break;
                    default: t = tol; break;
                }
                if (Math.Abs(o.Scalars[i] - n.Scalars[i]) > t) return false;
            }
            return true;
        }

        static List<double> Bulges(ShapeRecord r)
        {
            var b = new List<double>(r.Bulges);
            while (b.Count < r.Points.Count) b.Add(0);
            return b;
        }

        /// <summary>Đảo chiều polyline: đỉnh ngược thứ tự; bulge đoạn k mới = −bulge đoạn (c−2−k) cũ (mod c khi kín).</summary>
        static (List<P2>, List<double>) Reverse(ShapeRecord r)
        {
            int c = r.Points.Count;
            var b = Bulges(r);
            var pts = new List<P2>(c);
            var bul = new List<double>(c);
            for (int k = 0; k < c; k++)
            {
                pts.Add(r.Points[c - 1 - k]);
                int j = ((c - 2 - k) % c + c) % c;
                bul.Add(r.Closed || k < c - 1 ? -b[j] : 0);
            }
            return (pts, bul);
        }

        static bool SameSequence(List<P2> a, List<double> ab, List<P2> b, List<double> bb, int start, bool cyclic, double tol)
        {
            int c = a.Count;
            int segs = cyclic ? c : c - 1;
            for (int k = 0; k < c; k++)
            {
                int i = (start + k) % c;
                if ((a[i] - b[k]).Length > tol) return false;
                if (k < segs && Math.Abs(ab[i] - bb[k]) > BulgeTol) return false;
            }
            return true;
        }
    }
}
