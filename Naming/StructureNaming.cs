using System;
using System.Collections.Generic;
using System.Linq;
using MCG_3DPanel.Models;

namespace MCG_3DPanel.Naming
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    public enum NamingKind { Web, HP, FB }

    /// <summary>T = chạy theo Y của block (transverse), L = chạy theo X (longitudinal).</summary>
    public enum NamingDir { T, L }

    /// <summary>1 đoạn (part 2D) cần đặt tên: đường tâm trong HỆ BLOCK 2D (không phải WCS).</summary>
    public sealed class NamingPiece
    {
        public string Key { get; set; }          // SourceKey của part
        public NamingKind Kind { get; set; }
        public double X0 { get; set; }
        public double Y0 { get; set; }
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double Thickness { get; set; }
        public string Spec { get; set; }

        internal NamingDir Dir => Math.Abs(X1 - X0) >= Math.Abs(Y1 - Y0) ? NamingDir.L : NamingDir.T;
        internal double Center => Dir == NamingDir.T ? (X0 + X1) / 2 : (Y0 + Y1) / 2;
        internal double AxLo => Dir == NamingDir.T ? Math.Min(Y0, Y1) : Math.Min(X0, X1);
        internal double AxHi => Dir == NamingDir.T ? Math.Max(Y0, Y1) : Math.Max(X0, X1);
        internal double Length => Math.Sqrt((X1 - X0) * (X1 - X0) + (Y1 - Y0) * (Y1 - Y0));
    }

    /// <summary>1 tuyến (1 dòng tab Naming): mọi đoạn thẳng hàng mang chung 1 tên.</summary>
    public sealed class NamingLine
    {
        public NamingKind Kind { get; set; }
        public NamingDir Dir { get; set; }
        /// <summary>Toạ độ tâm theo phương ngang tuyến (X với T, Y với L) — của đoạn dài nhất thuộc loại chính.</summary>
        public double Center { get; set; }
        public IReadOnlyList<NamingPiece> Pieces { get; set; }
        public double Length { get; set; }
        public string AutoName { get; set; }
        public string Name { get; set; }
        public bool IsUserName { get; set; }
    }

    /// <summary>
    /// Tên kết cấu tự sinh (kỹ sư 2026-11-05, spec naming-tab): Web theo Y = T#, theo X = L#; HP theo Y = HT#, theo X = HL#;
    /// mọi Flat Bar = "BS". T/HT đếm lái→mũi (mũi ở X+ ⇒ X tăng); L/HL đếm tâm tàu→mạn: Center/Port Y tăng,
    /// Starboard Y giảm. Web+HP thẳng hàng (tâm lệch ≤ 15) là 1 tuyến, loại tuyến = loại dài hơn (HP 1B2E9 → L3).
    /// </summary>
    public static class StructureNaming
    {
        public const double LINE_TOL = 15;   // lệch đường tâm tối đa trong 1 tuyến (mm)
        public const double FB_GAP = 30;     // khe tối đa giữa 2 đoạn của 1 thanh FB (tách tại HP)
        public const string BS = "BS";

        public static IReadOnlyList<NamingLine> Lines(IEnumerable<NamingPiece> pieces, PanelPosition pos)
        {
            var all = (pieces ?? Enumerable.Empty<NamingPiece>()).Where(p => p != null).ToList();
            var lines = new List<NamingLine>();

            foreach (NamingDir dir in new[] { NamingDir.T, NamingDir.L })
            {
                // Web + HP
                foreach (var cl in Cluster(all.Where(p => p.Kind != NamingKind.FB && p.Dir == dir)))
                {
                    double web = cl.Where(p => p.Kind == NamingKind.Web).Sum(p => p.Length);
                    double hp = cl.Where(p => p.Kind == NamingKind.HP).Sum(p => p.Length);
                    var kind = web >= hp ? NamingKind.Web : NamingKind.HP;
                    lines.Add(Make(kind, dir, cl, cl.Where(p => p.Kind == kind)));
                }
                // FB: chỉ gộp FB với FB, và chỉ khi liền (khe ≤ FB_GAP)
                foreach (var cl in Cluster(all.Where(p => p.Kind == NamingKind.FB && p.Dir == dir)))
                {
                    var run = new List<NamingPiece>();
                    double hi = double.NegativeInfinity;
                    foreach (var p in cl.OrderBy(p => p.AxLo))
                    {
                        if (run.Count > 0 && p.AxLo - hi > FB_GAP) { lines.Add(Make(NamingKind.FB, dir, run, run)); run = new List<NamingPiece>(); }
                        run.Add(p);
                        hi = run.Count == 1 ? p.AxHi : Math.Max(hi, p.AxHi);
                    }
                    if (run.Count > 0) lines.Add(Make(NamingKind.FB, dir, run, run));
                }
            }

            bool lDesc = pos == PanelPosition.Starboard;
            var ordered = new List<NamingLine>();
            foreach (var (kind, dir, prefix) in new[]
            {
                (NamingKind.Web, NamingDir.T, "T"), (NamingKind.Web, NamingDir.L, "L"),
                (NamingKind.HP, NamingDir.T, "HT"), (NamingKind.HP, NamingDir.L, "HL"),
            })
            {
                var g = lines.Where(l => l.Kind == kind && l.Dir == dir);
                g = dir == NamingDir.L && lDesc ? g.OrderByDescending(l => l.Center) : g.OrderBy(l => l.Center);
                int i = 0;
                foreach (var l in g) { l.AutoName = prefix + (++i); ordered.Add(l); }
            }
            foreach (var l in lines.Where(l => l.Kind == NamingKind.FB)
                                   .OrderBy(l => l.Dir).ThenBy(l => l.Center).ThenBy(l => l.Pieces.Min(p => p.AxLo)))
            {
                l.AutoName = BS;
                ordered.Add(l);
            }
            foreach (var l in ordered) { l.Name = l.AutoName; l.IsUserName = false; }
            return ordered;
        }

        /// <summary>Gắn tên người dùng đã lưu (theo SourceKey từng đoạn). Nhiều tên khác nhau ⇒ tên nhiều đoạn nhất
        /// (hoà ⇒ theo thứ tự đoạn). Không có ⇒ về tên tự sinh. Khoá không còn đoạn nào ⇒ bỏ qua.</summary>
        public static void Resolve(IReadOnlyList<NamingLine> lines, IReadOnlyDictionary<string, string> stored)
        {
            foreach (var l in lines ?? new NamingLine[0])
            {
                string best = null;
                int bestN = 0;
                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var p in l.Pieces)
                {
                    if (p.Key == null || stored == null || !stored.TryGetValue(p.Key, out var n) || string.IsNullOrWhiteSpace(n)) continue;
                    counts[n] = counts.TryGetValue(n, out int c) ? c + 1 : 1;
                    if (counts[n] > bestN) { bestN = counts[n]; best = n; }
                }
                l.IsUserName = best != null;
                l.Name = best ?? l.AutoName;
            }
        }

        private static IEnumerable<List<NamingPiece>> Cluster(IEnumerable<NamingPiece> pieces)
        {
            var cur = new List<NamingPiece>();
            double last = double.NaN;
            foreach (var p in pieces.OrderBy(p => p.Center))
            {
                if (cur.Count > 0 && p.Center - last > LINE_TOL) { yield return cur; cur = new List<NamingPiece>(); }
                cur.Add(p);
                last = p.Center;
            }
            if (cur.Count > 0) yield return cur;
        }

        private static NamingLine Make(NamingKind kind, NamingDir dir, List<NamingPiece> pieces, IEnumerable<NamingPiece> main)
        {
            var longest = main.OrderByDescending(p => p.Length).First();
            return new NamingLine
            {
                Kind = kind,
                Dir = dir,
                Center = longest.Center,
                Pieces = pieces.OrderBy(p => p.AxLo).ToList(),
                Length = pieces.Sum(p => p.Length),
            };
        }
    }
}
