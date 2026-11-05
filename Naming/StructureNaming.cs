using System;
using System.Collections.Generic;
using System.Linq;
using MCG_3DPanel.Models;

namespace MCG_3DPanel.Naming
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>Flange không thành dòng riêng — chỉ cho quy cách của tuyến Web nằm dưới nó.</summary>
    public enum NamingKind { Web, HP, FB, Flange }

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
        /// <summary>Web/HP/FB: chiều dày (2D; HP theo quy cách Input). Flange: BỀ RỘNG (cạnh ngắn 2D).</summary>
        public double Thickness { get; set; }
        /// <summary>Web: chiều cao (Input) · HP: chiều cao HP (Input) · FB: chiều cao riêng/Input · Flange: chiều dày (Input).</summary>
        public double Height { get; set; }

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
        /// <summary>Phần TÊN (tag) — tự sinh hoặc người dùng gõ, vd "T1" / "T6a".</summary>
        public string Name { get; set; }
        public bool IsUserName { get; set; }
        /// <summary>Quy cách của đoạn dài nhất, vd "WEB 374x6.0/FLANGE 100x20.0", "HP 120x6.0", "FB 80x6.0".</summary>
        public string Spec { get; set; }
        /// <summary>Tên đầy đủ hiển thị/xuất (kỹ sư revision 1): "T1 WEB 374x6.0/FLANGE 100x20.0".</summary>
        public string Label => string.IsNullOrEmpty(Spec) ? Name : Name + " " + Spec;
    }

    /// <summary>
    /// Tên kết cấu tự sinh (kỹ sư 2026-11-05, spec naming-tab): Web theo Y = T#, theo X = L#; HP theo Y = HT#, theo X = HL#;
    /// mọi Flat Bar = "BS". T/HT đếm lái→mũi (mũi ở X+ ⇒ X tăng); L/HL đếm tâm tàu→mạn: Center/Port Y tăng,
    /// Starboard Y giảm. Web+HP thẳng hàng (tâm lệch ≤ 15) là 1 tuyến, loại tuyến = loại dài hơn (HP 1B2E9 → L3).
    /// </summary>
    public static class StructureNaming
    {
        public const double LINE_TOL = 15;   // lệch đường tâm tối đa trong 1 tuyến (mm)
        public const string BS = "BS";

        /// <param name="deleted">SourceKey người dùng "Del Name": tuyến có đoạn thuộc tập này bị BỎ (không hiện, không
        /// đánh số) — các tuyến còn lại đánh số lại (kỹ sư revision 1).</param>
        public static IReadOnlyList<NamingLine> Lines(IEnumerable<NamingPiece> pieces, PanelPosition pos,
            ICollection<string> deleted = null)
        {
            // Đoạn đã Del Name bị bỏ TRƯỚC khi gom tuyến — 1 đoạn đã xoá nhập vào tuyến khác (sửa Kind, vẽ lại 2D) không
            // kéo theo ẩn tuyến đó (review R1 #3).
            var all = (pieces ?? Enumerable.Empty<NamingPiece>())
                .Where(p => p != null && (deleted == null || p.Key == null || !deleted.Contains(p.Key))).ToList();
            var flanges = all.Where(p => p.Kind == NamingKind.Flange).ToList();
            var lines = new List<NamingLine>();

            foreach (NamingDir dir in new[] { NamingDir.T, NamingDir.L })
            {
                // Web + HP
                foreach (var cl in Cluster(all.Where(p => (p.Kind == NamingKind.Web || p.Kind == NamingKind.HP) && p.Dir == dir)))
                {
                    double web = cl.Where(p => p.Kind == NamingKind.Web).Sum(p => p.Length);
                    double hp = cl.Where(p => p.Kind == NamingKind.HP).Sum(p => p.Length);
                    var kind = web >= hp ? NamingKind.Web : NamingKind.HP;
                    lines.Add(Make(kind, dir, cl, cl.Where(p => p.Kind == kind), flanges));
                }
                // FB: chỉ gộp FB với FB; thẳng hàng là 1 dòng dù cách xa (revision 1: BS 947 + BS 8428)
                foreach (var cl in Cluster(all.Where(p => p.Kind == NamingKind.FB && p.Dir == dir)))
                    lines.Add(Make(NamingKind.FB, dir, cl, cl, flanges));
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

        private static NamingLine Make(NamingKind kind, NamingDir dir, List<NamingPiece> pieces, IEnumerable<NamingPiece> main,
            List<NamingPiece> flanges)
        {
            var longest = main.OrderByDescending(p => p.Length).First();
            string spec;
            switch (kind)
            {
                case NamingKind.Web:
                    spec = $"WEB {H(longest.Height)}x{T(longest.Thickness)}";
                    // Flange của tuyến: cùng phương, tâm trong nửa bề rộng flange (+ LINE_TOL) quanh đường tâm web, chồng trục.
                    var fl = flanges.Where(f => f.Dir == dir && Math.Abs(f.Center - longest.Center) <= f.Thickness / 2 + LINE_TOL
                                                && pieces.Any(p => f.AxLo < p.AxHi && p.AxLo < f.AxHi))
                                    .OrderByDescending(f => f.Length).FirstOrDefault();
                    if (fl != null) spec += $"/FLANGE {H(fl.Thickness)}x{T(fl.Height)}";
                    break;
                case NamingKind.HP: spec = $"HP {H(longest.Height)}x{T(longest.Thickness)}"; break;
                default: spec = $"FB {H(longest.Height)}x{T(longest.Thickness)}"; break;
            }
            return new NamingLine
            {
                Kind = kind,
                Dir = dir,
                Center = longest.Center,
                Pieces = pieces.OrderBy(p => p.AxLo).ToList(),
                Length = pieces.Sum(p => p.Length),
                Spec = spec,
            };
        }

        private static string H(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        private static string T(double v) => v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>Người dùng chỉ sửa phần TÊN; quy cách luôn tự ghép. Chữ gõ kết thúc bằng quy cách hiện tại ⇒ bỏ phần đó.
        /// Trả "" nếu chỉ còn quy cách (⇒ về tên tự sinh).</summary>
        public static string StripSpec(string typed, string spec)
        {
            string t = (typed ?? "").Trim();
            if (!string.IsNullOrEmpty(spec) && t.EndsWith(spec, StringComparison.OrdinalIgnoreCase))
                t = t.Substring(0, t.Length - spec.Length).Trim();
            // Gõ quy cách KHÁC / xoá dở (review R1 #1): cắt từ token "WEB|HP|FB <số>" đầu tiên — quy cách luôn tự sinh.
            t = System.Text.RegularExpressions.Regex.Replace(t, @"(?:^|\s+)(?:WEB|HP|FB)\s+\d.*$", "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            return t;
        }
    }
}
