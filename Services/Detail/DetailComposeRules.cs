using System;
using System.Collections.Generic;
using System.Linq;

namespace MCG_3DPanel.Services.Detail
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.*.

    /// <summary>Mũi tên ký hiệu mặt cắt tại 1 đầu đường cắt (toạ độ plan / block DET).</summary>
    public sealed class MarkArrow
    {
        public P2 BarA, BarB;          // vạch đậm nằm trên đường cắt, tâm tại đầu mút
        public P2 Base, Neck, Tip;     // thân Base→Neck, đầu mũi tên Neck→Tip (theo hướng nhìn)
        public P2 Text;                // tâm chữ "nA"
    }

    /// <summary>Hình học thuần cho bộ dựng block Detail: gộp đoạn trùng, vạch AM_8, CutMember từ vết cắt, mũi tên mặt cắt.</summary>
    public static class DetailComposeRules
    {
        // Block mẫu A$C1A6307D4 (dump 7D-16P), chèn tỉ lệ 0.4 như mọi bản vẽ mẫu.
        const double MarkScale = 0.4;
        public const double BarHalf = 62.3669 * MarkScale;
        public const double BarWidth = 9.6 * MarkScale;
        public const double NeckLength = 133.4212 * MarkScale;
        public const double TipLength = 276.9148 * MarkScale;
        /// <summary>Dump không ghi bề rộng từng đỉnh ⇒ giả định đầu mũi tên rộng = 1/3 chiều dài (kiểu closed filled).</summary>
        public const double HeadWidth = (276.9148 - 133.4212) / 3 * MarkScale;
        // Chữ "1B" mẫu: cách đầu mút 36.2 ra ngoài, 86.9 theo hướng nhìn.
        const double TextOut = 36.19, TextLook = 86.9;
        // Chữ "(t)" cách tâm ký hiệu Continuous dọc trục ký hiệu (ký hiệu dài −59…41.5).
        const double ThicknessTextGap = 120;

        static double Cross(P2 a, P2 b) => a.X * b.Y - a.Y * b.X;

        /// <summary>Gộp đoạn thẳng cùng đường (lệch ≤ tol) chồng/chạm nhau thành một; bỏ phần trùng <paramref name="cover"/>
        /// (vd cạnh vết cắt đã vẽ); bỏ đoạn ≤ tol.</summary>
        public static List<(P2 A, P2 B)> MergeCollinear(IEnumerable<(P2 A, P2 B)> segs, IEnumerable<(P2 A, P2 B)> cover, double tol = 0.05)
        {
            var groups = new List<(P2 O, P2 D, List<(double lo, double hi)> keep, List<(double lo, double hi)> cut)>();
            bool On(P2 o, P2 d, P2 p) => Math.Abs(Cross(d, p - o)) <= tol;
            (double, double) Range(P2 o, P2 d, P2 a, P2 b)
            {
                double ta = (a - o).Dot(d), tb = (b - o).Dot(d);
                return (Math.Min(ta, tb), Math.Max(ta, tb));
            }

            foreach (var s in segs ?? Enumerable.Empty<(P2, P2)>())
            {
                var v = s.B - s.A;
                if (v.Length <= tol) continue;
                int k = groups.FindIndex(g => On(g.O, g.D, s.A) && On(g.O, g.D, s.B));
                if (k < 0)
                {
                    groups.Add((s.A, v * (1 / v.Length), new List<(double, double)>(), new List<(double, double)>()));
                    k = groups.Count - 1;
                }
                groups[k].keep.Add(Range(groups[k].O, groups[k].D, s.A, s.B));
            }
            foreach (var c in cover ?? Enumerable.Empty<(P2, P2)>())
            {
                if ((c.B - c.A).Length <= tol) continue;
                foreach (var g in groups)
                    if (On(g.O, g.D, c.A) && On(g.O, g.D, c.B)) g.cut.Add(Range(g.O, g.D, c.A, c.B));
            }

            var res = new List<(P2, P2)>();
            foreach (var g in groups)
            {
                var cuts = Union(g.cut, tol);
                foreach (var iv in Union(g.keep, tol))
                {
                    double lo = iv.lo;
                    foreach (var c in cuts)
                    {
                        if (c.hi <= lo || c.lo >= iv.hi) continue;
                        if (c.lo - lo > tol) res.Add((g.O + g.D * lo, g.O + g.D * c.lo));
                        lo = Math.Max(lo, c.hi);
                    }
                    if (iv.hi - lo > tol) res.Add((g.O + g.D * lo, g.O + g.D * iv.hi));
                }
            }
            return res;
        }

        static List<(double lo, double hi)> Union(List<(double lo, double hi)> iv, double tol)
        {
            var r = new List<(double lo, double hi)>();
            foreach (var x in iv.OrderBy(q => q.lo))
            {
                if (r.Count > 0 && x.lo <= r[r.Count - 1].hi + tol)
                    r[r.Count - 1] = (r[r.Count - 1].lo, Math.Max(r[r.Count - 1].hi, x.hi));
                else r.Add(x);
            }
            return r;
        }


        /// <summary>Cạnh i (outline[i]→outline[i+1]) là break khi nằm trên biên vùng/hộp cắt (đầu, giữa, ¼, ¾ cách biên ≤ tol)
        /// hoặc bị các đoạn break thẳng phủ kín.</summary>
        public static List<bool> BreakEdges(IList<P2> outline, Func<P2, double> boundaryDist, IEnumerable<(P2 A, P2 B)> breaks, double tol)
        {
            var res = new List<bool>();
            var brk = (breaks ?? Enumerable.Empty<(P2, P2)>()).ToList();
            int n = outline?.Count ?? 0;
            for (int i = 0; i < n; i++)
            {
                P2 a = outline[i], b = outline[(i + 1) % n];
                bool onBoundary = boundaryDist != null
                    && new[] { 0, 0.25, 0.5, 0.75, 1 }.All(k => boundaryDist(a + (b - a) * k) <= tol);
                bool covered = false;
                if (!onBoundary && brk.Count > 0)
                {
                    if ((b - a).Length > tol) covered = MergeCollinear(new[] { (a, b) }, brk, tol).Count == 0;
                    else covered = new[] { a, b }.All(p => brk.Any(s => SectionCurveRules.DistanceToSegment(p, s.Item1, s.Item2) <= tol));
                }
                res.Add(onBoundary || covered);
            }
            return res;
        }

        /// <summary>Gom cạnh liên tiếp cùng loại thành đường: tất cả cùng loại ⇒ 1 vòng kín (đỉnh như outline); không thì các đoạn hở.</summary>
        public static List<(List<P2> Pts, bool Break, bool Closed)> Runs(IList<P2> outline, IList<bool> brk)
        {
            var res = new List<(List<P2>, bool, bool)>();
            int n = outline?.Count ?? 0;
            if (n == 0) return res;
            int s = -1;
            for (int i = 0; i < n; i++) if (brk[i] != brk[(i + n - 1) % n]) { s = i; break; }
            if (s < 0) { res.Add((outline.ToList(), brk[0], true)); return res; }
            List<P2> cur = null; bool kind = false;
            for (int k = 0; k < n; k++)
            {
                int i = (s + k) % n;
                if (cur == null || brk[i] != kind)
                {
                    if (cur != null) res.Add((cur, kind, false));
                    cur = new List<P2> { outline[i] }; kind = brk[i];
                }
                cur.Add(outline[(i + 1) % n]);
            }
            res.Add((cur, kind, false));
            return res;
        }
        /// <summary>Phần của đường thẳng vô hạn p + t·d nằm trong đa giác (kín, có thể lõm; không lặp đỉnh đầu).</summary>
        public static List<(P2 A, P2 B)> ClipLine(P2 p, P2 d, IList<P2> poly)
        {
            var res = new List<(P2, P2)>();
            int n = poly?.Count ?? 0;
            if (n < 3 || d.Length < 1e-12) return res;
            d = d * (1 / d.Length);
            var ts = new List<double>();
            for (int i = 0; i < n; i++)
            {
                P2 a = poly[i], e = poly[(i + 1) % n] - a;
                double den = Cross(d, e);
                if (Math.Abs(den) < 1e-12)
                {
                    if (Math.Abs(Cross(d, a - p)) < 1e-9) { ts.Add((a - p).Dot(d)); ts.Add((a + e - p).Dot(d)); }
                    continue;
                }
                double s = Cross(a - p, d) / den;
                if (s < -1e-9 || s > 1 + 1e-9) continue;
                ts.Add(Cross(a - p, e) / den);
            }
            ts.Sort();
            for (int i = ts.Count - 1; i > 0; i--) if (ts[i] - ts[i - 1] < 1e-9) ts.RemoveAt(i);
            double? open = null;
            for (int i = 1; i < ts.Count; i++)
            {
                bool inside = Inside(p + d * ((ts[i - 1] + ts[i]) / 2), poly);
                if (inside && open == null) open = ts[i - 1];
                if (!inside && open != null) { res.Add((p + d * open.Value, p + d * ts[i - 1])); open = null; }
                if (inside && i == ts.Count - 1) { res.Add((p + d * open.Value, p + d * ts[i])); open = null; }
            }
            return res;
        }

        /// <summary>Chẵn-lẻ (ray casting).</summary>
        public static bool Inside(P2 q, IList<P2> poly)
        {
            bool c = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                P2 a = poly[i], b = poly[j];
                if ((a.Y > q.Y) != (b.Y > q.Y) && q.X < (b.X - a.X) * (q.Y - a.Y) / (b.Y - a.Y) + a.X) c = !c;
            }
            return c;
        }

        public static double Area(IList<P2> poly)
        {
            double s = 0;
            for (int i = 0; i < poly.Count; i++) s += Cross(poly[i], poly[(i + 1) % poly.Count]);
            return Math.Abs(s) / 2;
        }

        /// <summary>Vạch AM_8 trong vết cắt dạng tấm: song song trục dài, tại <see cref="SectionMarkRules.Offsets"/> tính từ một mặt,
        /// cắt theo viền. Không phải dạng tấm ⇒ rỗng.</summary>
        public static List<(P2 A, P2 B)> MarkLines(IList<P2> outline)
        {
            var res = new List<(P2, P2)>();
            if (outline == null || outline.Count < 3) return res;
            var r = SectionMarkRules.MinAreaRect(outline);
            if (r.width < 1e-6 || !SectionMarkRules.IsPlateLike(Area(outline), r.width, r.length)) return res;
            var n = new P2(-r.axis.Y, r.axis.X);
            double m0 = outline.Min(q => q.Dot(n));
            foreach (double off in SectionMarkRules.Offsets(r.width))
                res.AddRange(ClipLine(n * (m0 + off), r.axis, outline));
            return res;
        }

        /// <summary>Tấm nhận ký hiệu Continuous trên section: mỗi tấm (handle; rỗng ⇒ mỗi vết riêng) lấy vết cắt lớn nhất
        /// (≥ 3 điểm), giữ những tấm <see cref="SectionSymbolRules.IsContinuous"/>. Rỗng ⇒ không cần block "Continious symbol".</summary>
        /// <summary>Ghi chú khi phụ kiện chép vào detail có thuộc tính (bản chép reference không mang theo AttributeReference):
        /// tên block không trùng, sắp ordinal. Không có ⇒ null.</summary>
        public static string AttributesNote(IEnumerable<string> blockNames)
        {
            var names = (blockNames ?? Enumerable.Empty<string>()).Where(n => !string.IsNullOrEmpty(n))
                .Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToList();
            return names.Count == 0 ? null
                : $"attribute values of block(s) {string.Join(", ", names)} are not shown in the detail (attributes are not copied)";
        }

        public static List<CutMember> ContinuousMembers(
            IEnumerable<(string Handle, string Type, IList<P2> Outline, double MinSigned, double MaxSigned)> cuts)
        {
            if (cuts == null) return new List<CutMember>();
            return cuts.Where(c => c.Outline != null && c.Outline.Count >= 3)
                .GroupBy(c => string.IsNullOrEmpty(c.Handle) ? Guid.NewGuid().ToString() : c.Handle)
                .Select(g => g.OrderByDescending(c => Area(c.Outline)).First())
                .Select(c => ToCutMember(c.Type, c.Outline, c.MinSigned, c.MaxSigned))
                .Where(m => SectionSymbolRules.IsContinuous(m)).ToList();
        }

        /// <summary>CutMember từ 1 vết cắt: dày = bề rộng hình chữ nhật bao nhỏ nhất, tâm = tâm hình chữ nhật, trục = trục dài.</summary>
        public static CutMember ToCutMember(string type, IList<P2> outline, double minSigned, double maxSigned)
        {
            var r = SectionMarkRules.MinAreaRect(outline);
            var n = new P2(-r.axis.Y, r.axis.X);
            double a0 = outline.Min(q => q.Dot(r.axis)), a1 = outline.Max(q => q.Dot(r.axis));
            double n0 = outline.Min(q => q.Dot(n)), n1 = outline.Max(q => q.Dot(n));
            return new CutMember
            {
                Type = type, MinSigned = minSigned, MaxSigned = maxSigned, Thickness = r.width,
                CutCenter = r.axis * ((a0 + a1) / 2) + n * ((n0 + n1) / 2), CutAxis = r.axis,
            };
        }

        /// <summary>2 mũi tên ở 2 đầu đường cắt [start, end], chĩa theo <paramref name="look"/> (đơn vị).</summary>
        public static MarkArrow[] SectionArrows(P2 start, P2 end, P2 look)
        {
            var d = end - start;
            var u = d * (1 / d.Length);
            MarkArrow At(P2 e, P2 outward) => new MarkArrow
            {
                BarA = e + outward * BarHalf, BarB = e - outward * BarHalf,
                Base = e, Neck = e + look * NeckLength, Tip = e + look * TipLength,
                Text = e + outward * TextOut + look * TextLook,
            };
            return new[] { At(start, u * -1), At(end, u) };
        }

        /// <summary>Tâm chữ "(t)" dọc trục ký hiệu Continuous (góc <paramref name="rotation"/>), phía đầu +X của ký hiệu.</summary>
        public static P2 ThicknessTextAt(P2 at, double rotation) =>
            at + new P2(Math.Cos(rotation), Math.Sin(rotation)) * ThicknessTextGap;
    }
}
