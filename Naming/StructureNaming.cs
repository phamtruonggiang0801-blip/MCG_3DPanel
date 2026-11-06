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

        /// <summary>Góc đường tâm so với trục X, 0 ≤ a &lt; 180 (đoạn không có hướng).</summary>
        internal double AngleDeg
        {
            get
            {
                double a = Math.Atan2(Y1 - Y0, X1 - X0) * 180 / Math.PI;
                if (a < 0) a += 180;
                return a >= 180 ? 0 : a;
            }
        }

        /// <summary>Xiên = lệch hơn ANGLE_TOL so với cả 0°, 90° và 180°.</summary>
        internal bool IsSkew
        {
            get
            {
                double a = AngleDeg;
                return a > StructureNaming.ANGLE_TOL && Math.Abs(a - 90) > StructureNaming.ANGLE_TOL
                       && a < 180 - StructureNaming.ANGLE_TOL;
            }
        }

        internal bool Parallel(NamingPiece o)
        {
            double d = Math.Abs(AngleDeg - o.AngleDeg);
            return Math.Min(d, 180 - d) <= StructureNaming.ANGLE_TOL;
        }

        /// <summary>Hình chiếu của o và của đoạn này lên trục đoạn này có chồng nhau không.</summary>
        internal bool OverlapsAlong(NamingPiece o)
        {
            double len = Length;
            if (len < 1e-9) return false;
            double ux = (X1 - X0) / len, uy = (Y1 - Y0) / len;
            double a0 = 0, a1 = len;
            double b0 = (o.X0 - X0) * ux + (o.Y0 - Y0) * uy, b1 = (o.X1 - X0) * ux + (o.Y1 - Y0) * uy;
            return Math.Min(b0, b1) < a1 && a0 < Math.Max(b0, b1);
        }

        /// <summary>Khoảng cách từ trung điểm của o tới đường thẳng vô hạn chứa đoạn này.</summary>
        internal double PerpDist(NamingPiece o)
        {
            double mx = (o.X0 + o.X1) / 2 - X0, my = (o.Y0 + o.Y1) / 2 - Y0;
            double len = Length;
            if (len < 1e-9) return Math.Sqrt(mx * mx + my * my);
            return Math.Abs((X1 - X0) * my - (Y1 - Y0) * mx) / len;
        }
    }

    /// <summary>1 tuyến (1 dòng tab Naming): mọi đoạn thẳng hàng mang chung 1 tên.</summary>
    public sealed class NamingLine
    {
        public NamingKind Kind { get; set; }
        public NamingDir Dir { get; set; }
        /// <summary>Toạ độ tâm theo phương ngang tuyến (X với T, Y với L) — của đoạn dài nhất thuộc loại chính; riêng tuyến toàn đoạn xiên thì là trung điểm phạm vi cả tuyến.</summary>
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
        /// <summary>Flange nằm trên tuyến web (highlight cùng web — revision 2). Rỗng với HP/FB.</summary>
        public IReadOnlyList<NamingPiece> Flanges { get; set; } = new NamingPiece[0];
        /// <summary>Tuyến gộp từ nhiều tuyến bằng Merge (revision 2).</summary>
        public bool IsMerged { get; set; }
    }

    /// <summary>
    /// Tên kết cấu tự sinh (kỹ sư 2026-11-05, spec naming-tab): Web theo Y = T#, theo X = L#; HP theo Y = HT#, theo X = HL#;
    /// mọi Flat Bar = "BS". T/HT đếm lái→mũi (mũi ở X+ ⇒ X tăng); L/HL đếm tâm tàu→mạn: Center/Port Y tăng,
    /// Starboard Y giảm. Web+HP thẳng hàng (tâm lệch ≤ 15) là 1 tuyến, loại tuyến = loại dài hơn (HP 1B2E9 → L3).
    /// </summary>
    public static class StructureNaming
    {
        public const double LINE_TOL = 15;   // lệch đường tâm tối đa trong 1 tuyến (mm)
        public const double ANGLE_TOL = 1.0; // lệch góc tối đa (độ): ≤ ⇒ song song / coi là trục
        public const string BS = "BS";

        /// <param name="deleted">SourceKey người dùng "Del Name": tuyến có đoạn thuộc tập này bị BỎ (không hiện, không
        /// đánh số) — các tuyến còn lại đánh số lại (kỹ sư revision 1).</param>
        /// <param name="merges">Nhóm SourceKey người dùng "Merge": các tuyến CÙNG loại + CÙNG phương có đoạn trong 1 nhóm
        /// gộp thành 1 tuyến (1 tên, 1 số) — revision 2. Nhóm trộn loại/phương khác ⇒ chỉ gộp phần cùng loại+phương.</param>
        public static IReadOnlyList<NamingLine> Lines(IEnumerable<NamingPiece> pieces, PanelPosition pos,
            ICollection<string> deleted = null, IEnumerable<ICollection<string>> merges = null,
            ICollection<string> splits = null)
        {
            // Đoạn đã Del Name bị bỏ TRƯỚC khi gom tuyến — 1 đoạn đã xoá nhập vào tuyến khác (sửa Kind, vẽ lại 2D) không
            // kéo theo ẩn tuyến đó (review R1 #3).
            var all = (pieces ?? Enumerable.Empty<NamingPiece>())
                .Where(p => p != null && (deleted == null || p.Key == null || !deleted.Contains(p.Key))).ToList();
            var flanges = all.Where(p => p.Kind == NamingKind.Flange).ToList();
            // Đoạn Split (Web/HP/FB có key trong splits) rời khỏi gom tuyến tự động — mỗi key 1 tuyến riêng (flange ở lại).
            var splitPieces = splits == null || splits.Count == 0 ? new List<NamingPiece>()
                : all.Where(p => p.Kind != NamingKind.Flange && p.Key != null && splits.Contains(p.Key)).ToList();
            if (splitPieces.Count > 0) all = all.Where(p => !splitPieces.Contains(p)).ToList();
            var lines = new List<NamingLine>();

            foreach (NamingDir dir in new[] { NamingDir.T, NamingDir.L })
            {
                // Web + HP
                foreach (var cl in Cluster(all.Where(p => (p.Kind == NamingKind.Web || p.Kind == NamingKind.HP) && !p.IsSkew && p.Dir == dir)))
                {
                    var kind = WebOrHp(cl);
                    lines.Add(Make(kind, dir, cl, cl.Where(p => p.Kind == kind), flanges));
                }
                // FB: chỉ gộp FB với FB; thẳng hàng là 1 dòng dù cách xa (revision 1: BS 947 + BS 8428)
                foreach (var cl in Cluster(all.Where(p => p.Kind == NamingKind.FB && !p.IsSkew && p.Dir == dir)))
                    lines.Add(Make(NamingKind.FB, dir, cl, cl, flanges));
            }

            // Xiên: gom theo đường thẳng, không phân T/L (đường 45° không bị tách đôi); không bao giờ nhập tuyến trục.
            foreach (var cl in ClusterSkew(all.Where(p => (p.Kind == NamingKind.Web || p.Kind == NamingKind.HP) && p.IsSkew)))
            {
                var kind = WebOrHp(cl);
                lines.Add(Make(kind, cl.OrderByDescending(p => p.Length).First().Dir, cl, cl.Where(p => p.Kind == kind), flanges));
            }
            foreach (var cl in ClusterSkew(all.Where(p => p.Kind == NamingKind.FB && p.IsSkew)))
                lines.Add(Make(NamingKind.FB, cl.OrderByDescending(p => p.Length).First().Dir, cl, cl, flanges));

            // Mỗi key Split = 1 tuyến (các đoạn cùng key ở cùng nhau); loại/phương theo đoạn dài nhất.
            foreach (var g in splitPieces.GroupBy(p => p.Key))
            {
                var cl = g.ToList();
                var longest = cl.OrderByDescending(p => p.Length).First();
                var kind = longest.Kind == NamingKind.FB ? NamingKind.FB : WebOrHp(cl);
                lines.Add(Make(kind, longest.Dir, cl, cl.Where(p => p.Kind == kind), flanges));
            }

            foreach (var group in merges ?? Enumerable.Empty<ICollection<string>>())
            {
                if (group == null || group.Count == 0) continue;
                var hit = lines.Where(l => l.Pieces.Any(p => p.Key != null && group.Contains(p.Key))).ToList();
                foreach (var bucket in hit.GroupBy(l => (l.Kind, l.Dir)).Where(g => g.Count() > 1))
                {
                    var members = bucket.ToList();
                    var all2 = members.SelectMany(l => l.Pieces).ToList();
                    var merged = Make(bucket.Key.Kind, bucket.Key.Dir, all2, all2.Where(p => p.Kind == bucket.Key.Kind), flanges);
                    merged.IsMerged = true;
                    int at = lines.IndexOf(members[0]);
                    lines.RemoveAll(members.Contains);
                    lines.Insert(Math.Min(at, lines.Count), merged);
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
                g = dir == NamingDir.L && lDesc
                    ? g.OrderByDescending(l => l.Center).ThenBy(l => l.Pieces.Min(p => Math.Min(p.Y0, p.Y1))).ThenBy(l => l.Pieces.Min(p => Math.Min(p.X0, p.X1)))
                    : g.OrderBy(l => l.Center).ThenBy(l => l.Pieces.Min(p => Math.Min(p.Y0, p.Y1))).ThenBy(l => l.Pieces.Min(p => Math.Min(p.X0, p.X1)));   // hoà tâm (xiên) ⇒ theo hình học, không theo thứ tự nhập
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

        /// <summary>Mọi SourceKey của tuyến TỰ ĐỘNG (không merge, không split) có chứa một trong <paramref name="keys"/> —
        /// dùng cho Reset to auto: bỏ Split của CẢ tuyến để các đoạn nhập lại.</summary>
        public static HashSet<string> AutoLineKeys(IEnumerable<NamingPiece> pieces, PanelPosition pos,
            ICollection<string> deleted, IEnumerable<string> keys)
        {
            var want = new HashSet<string>(keys ?? Enumerable.Empty<string>());
            var res = new HashSet<string>();
            if (want.Count == 0) return res;
            foreach (var l in Lines(pieces, pos, deleted))
                if (l.Pieces.Any(p => p.Key != null && want.Contains(p.Key)))
                    foreach (var p in l.Pieces) if (p.Key != null) res.Add(p.Key);
            return res;
        }

        /// <summary>Tuyến tách được (Split): không phải tuyến Merge và có từ 2 SourceKey khác nhau trở lên.</summary>
        public static bool CanSplit(NamingLine line)
            => line != null && !line.IsMerged && line.Pieces.Where(p => p.Key != null).Select(p => p.Key).Distinct().Skip(1).Any();

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

        // Xiên: seed = đoạn dài nhất còn lại; nhập nếu song song + đường tâm lệch ≤ LINE_TOL. Không chuỗi trôi.
        private static IEnumerable<List<NamingPiece>> ClusterSkew(IEnumerable<NamingPiece> pieces)
        {
            var groups = new List<List<NamingPiece>>();
            foreach (var p in pieces.OrderByDescending(p => p.Length))
            {
                var g = groups.FirstOrDefault(c => c[0].Parallel(p) && c[0].PerpDist(p) <= LINE_TOL);
                if (g != null) g.Add(p); else groups.Add(new List<NamingPiece> { p });
            }
            return groups;
        }

        /// <summary>Loại tuyến Web+HP: tổng chiều dài Web >= HP ⇒ Web, ngược lại HP.</summary>
        private static NamingKind WebOrHp(List<NamingPiece> cl)
            => cl.Where(p => p.Kind == NamingKind.Web).Sum(p => p.Length) >= cl.Where(p => p.Kind == NamingKind.HP).Sum(p => p.Length)
                ? NamingKind.Web : NamingKind.HP;

        private static NamingLine Make(NamingKind kind, NamingDir dir, List<NamingPiece> pieces, IEnumerable<NamingPiece> main,
            List<NamingPiece> flanges)
        {
            var mainList = main.ToList();
            var longest = mainList.OrderByDescending(p => p.Length).First();
            string spec;
            var lineFlanges = new List<NamingPiece>();
            switch (kind)
            {
                case NamingKind.Web:
                    spec = $"WEB {H(longest.Height)}x{T(longest.Thickness)}";
                    // Flange của tuyến: cùng phương, tâm trong nửa bề rộng flange (+ LINE_TOL) quanh đường tâm 1 đoạn web,
                    // chồng trục với đoạn đó. Mọi flange khớp ⇒ highlight cùng web (revision 2).
                    // Xiên chỉ gắn web xiên, thẳng chỉ gắn web thẳng (không chéo nhau); xiên: song song (ANGLE_TOL),
                    // khoảng cách vuông góc trong nửa bề rộng (+ LINE_TOL), chồng theo trục web.
                    bool On(NamingPiece f, NamingPiece w)
                    {
                        if (f.IsSkew != w.IsSkew) return false;
                        if (f.IsSkew) return w.Parallel(f) && w.PerpDist(f) <= f.Thickness / 2 + LINE_TOL && w.OverlapsAlong(f);
                        return f.Dir == w.Dir && Math.Abs(f.Center - w.Center) <= f.Thickness / 2 + LINE_TOL
                               && f.AxLo < w.AxHi && w.AxLo < f.AxHi;
                    }
                    lineFlanges = flanges.Where(f => mainList.Any(w => On(f, w))).ToList();
                    // Quy cách: flange trên đoạn DÀI NHẤT trước, không có thì flange dài nhất của tuyến.
                    var fl = lineFlanges.Where(f => On(f, longest)).OrderByDescending(f => f.Length).FirstOrDefault()
                             ?? lineFlanges.OrderByDescending(f => f.Length).FirstOrDefault();
                    if (fl != null) spec += $"/FLANGE {H(fl.Thickness)}x{T(fl.Height)}";
                    break;
                case NamingKind.HP: spec = $"HP {H(longest.Height)}x{T(longest.Thickness)}"; break;
                default: spec = $"FB {H(longest.Height)}x{T(longest.Thickness)}"; break;
            }
            // Tuyến toàn đoạn xiên: tâm = giữa min..max của cả tuyến (đoạn dài nhất không đại diện được).
            double center = longest.Center;
            if (mainList.All(p => p.IsSkew))
            {
                var coords = pieces.SelectMany(p => dir == NamingDir.T ? new[] { p.X0, p.X1 } : new[] { p.Y0, p.Y1 }).ToList();
                center = (coords.Min() + coords.Max()) / 2;
            }
            return new NamingLine
            {
                Kind = kind,
                Dir = dir,
                Center = center,
                Pieces = pieces.OrderBy(p => p.AxLo).ToList(),
                Length = pieces.Sum(p => p.Length),
                Spec = spec,
                Flanges = lineFlanges,
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
