using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCG_3DPanel.Models;
using MCG_3DPanel.Services.Classify;

namespace MCG_3DPanel.Services.Builders
{
    /// <summary>
    /// <b>Tripping Bracket</b> (kỹ sư 2026-11-05, test 7D-16C): trên mỗi ĐOẠN DẦM (web có flange, giữa 2 gối = dầm
    /// liên tục có flange cắt ngang hoặc mút dầm) chọn HP xuyên web (có CO) sao cho khoảng giữa các gối ≤ L = 3 ×
    /// bước stiffener max (max toàn panel), đi từ đầu CHIỀU ĐỔ (Center: 2 đầu vào tâm). Mỗi HP chọn ⇒ 1 bracket "B"
    /// (hình y hệt <see cref="HpEndBracketBuilder"/>), phía chiều đổ, tấm đồng phẳng thân HP. Tab Result: ⇄ đảo
    /// phía (FlipDir theo khoá solid), Tripping Auto/On/Off theo HP (PartCorrection.Tripping, không tính lại Auto).
    /// Luật thuần: <see cref="TrippingBracketRules"/>. Xem skill tripping-bracket.
    ///
    /// ⚠️ <see cref="ClassificationResult.WebRuns"/> gom web thẳng hàng khe ≤ <c>TOL_GAP</c> (20) ⇒ 2 đoạn dầm 2
    /// bên 1 dầm liên tục (khe = dày dầm, vd 6) bị gộp 1 run — builder TỰ CẮT run tại chỗ dầm liên tục đi qua.
    /// </summary>
    internal static class TrippingBracketBuilder
    {
        private const string LOG = "[TrippingBracket]";
        private const double END_TOL = 20.0;     // giao điểm được phép vượt mút span (đầu đoạn chạm mặt dầm liên tục)
        private const double PERP_COS = 0.70;    // dầm liên tục cắt ngang: |cos| ≤ (~≥ 45°)
        private const double PAR_COS = 0.70;     // flange ∥ web / dầm cùng phương: |cos| ≥
        private const double MIN_AWAY_COS = 0.30; // HP quá xiên so với web dầm ⇒ bỏ

        private sealed class Span
        {
            public List<PanelPart> Parts;
            public Point3d P1; public Vector3d Axis; public double Len, Thk; public Point3d Center;
            public Vector3d Normal => new Vector3d(-Axis.Y, Axis.X, 0);
        }

        private sealed class SegRec
        {
            public Span Beam; public double Lo, Hi; public Point3d MidPt; public string Tag;
            public List<(PanelPart hp, PanelPart web, Point3d x, double s)> Xs;
        }

        public static List<(Solid3d sol, string tag, string key)> Build(BuildContext ctx)
        {
            var outSolids = new List<(Solid3d, string, string)>();
            if (ctx?.Cls == null) return outSolids;

            double b = ctx.Input.HpHeight, H = ctx.Input.WebHeight;
            if (b <= 0 || H <= 0) { ctx.Report.Add($"{LOG} b/WebHeight không hợp lệ → bỏ qua."); return outSolids; }
            double Ht = H - b;
            if (Ht <= TrippingBracketRules.R + TrippingBracketRules.E15) { ctx.Report.Add($"{LOG} Ht={Ht:0.#} quá thấp → bỏ qua."); return outSolids; }
            double reach = HpEndBracketBuilder.Reach(b);

            var hps = ctx.Cls.OfType(PartType.HP).ToList();
            var flanges = ctx.Cls.OfType(PartType.Flange).ToList();
            if (hps.Count == 0 || flanges.Count == 0) return outSolids;
            var supports = flanges.Concat(ctx.Cls.OfType(PartType.FacePlate)).ToList();

            var spans = ctx.Cls.WebRuns().Where(r => r != null && r.Count > 0).Select(MakeSpan).Where(s => s != null).ToList();
            // Dầm = run có flange tại tâm 1 PART bất kỳ (KHÔNG chỉ tâm run: run gộp xuyên dầm liên tục có tâm rơi
            // ngay dầm đó, flange dầm dừng ở mép flange CG ⇒ cả dầm bị loại âm thầm — review 2026-11-04). Từng
            // đoạn vẫn kiểm flange tại điểm giữa đoạn.
            var flanged = spans.Where(s => s.Parts.Any(p => FlangeAt(flanges, s.Axis, PartMid(p)))).ToList();
            if (flanged.Count == 0) return outSolids;

            Point3d center = ctx.Cls.TopPlate?.Centroid ?? Point3d.Origin;

            // ── Lượt 1: đoạn dầm (gối = dầm liên tục có flange cắt ngang + 2 mút run) và HP xuyên web từng đoạn.
            // KHÔNG còn loại "dầm ngoài cùng": web biên không có HP xuyên qua ⇒ tự không có bracket (R1, 2026-11-05).
            var recs = new List<SegRec>();
            foreach (var beam in flanged)
            {
                var cuts = new List<double>();
                foreach (var cg in flanged)
                {
                    if (ReferenceEquals(cg, beam)) continue;
                    if (Math.Abs(cg.Axis.DotProduct(beam.Axis)) > PERP_COS) continue;
                    if (!LineIntersect(beam, cg, out Point3d ix)) continue;
                    double sB = (ix - beam.P1).DotProduct(beam.Axis), sC = (ix - cg.P1).DotProduct(cg.Axis);
                    if (sB < -END_TOL || sB > beam.Len + END_TOL || sC < -END_TOL || sC > cg.Len + END_TOL) continue;
                    if (!Covers(cg, ix, beam.Thk) || Covers(beam, ix, cg.Thk)) continue;
                    if (!FlangeAt(flanges, cg.Axis, ix)) continue;
                    cuts.Add(sB);
                }

                foreach (var (lo, hi) in TrippingBracketRules.Segments(cuts, beam.Len, END_TOL))
                {
                    Point3d midPt = beam.P1 + beam.Axis * ((lo + hi) / 2.0);
                    if (!FlangeAt(flanges, beam.Axis, midPt)) continue;          // đoạn không có flange ⇒ không phải dầm

                    var xs = new List<(PanelPart hp, PanelPart web, Point3d x, double s)>();
                    foreach (var part in beam.Parts)
                        foreach (var hp in hps)
                        {
                            if (!ClassifyUtil.CrossesThrough(hp, part, out double tCross)) continue;
                            var (h1, h2) = ClassifyUtil.Endpoints(hp);
                            double hLen = h1.DistanceTo(h2);
                            if (hLen < 1e-6 || tCross < b * 0.5 || tCross > hLen - b * 0.5) continue;
                            Point3d x = h1 + (h2 - h1) / hLen * tCross;
                            double s = (x - beam.P1).DotProduct(beam.Axis);
                            if (s <= lo || s >= hi || xs.Any(q => ReferenceEquals(q.hp, hp))) continue;
                            xs.Add((hp, part, x, s));
                        }
                    recs.Add(new SegRec
                    {
                        Beam = beam, Lo = lo, Hi = hi, MidPt = midPt, Xs = xs.OrderBy(q => q.s).ToList(),
                        Tag = $"dầm {Spine(beam).Id.Handle} đoạn s=[{lo:0},{hi:0}]",
                    });
                }
            }

            double sMax = TrippingBracketRules.MaxSpacing(recs.Select(r => (IReadOnlyList<double>)r.Xs.Select(q => q.s).ToList()));
            double L = 3.0 * sMax;
            ctx.Report.Add($"{LOG} s_max={sMax:0.#} → L=3×s_max={L:0.#} ({recs.Count} đoạn dầm).");
            if (sMax <= 0) return outSolids;

            // ── Lượt 2: chọn HP theo khoảng cách ≤ L từ đầu CHIỀU ĐỔ (Center: 2 đầu vào tâm) + ép tay theo HP.
            var done = new HashSet<string>();
            foreach (var r in recs)
            {
                var beam = r.Beam;
                var sList = r.Xs.Select(q => q.s).ToList();
                int startSign = Math.Sign(Doz(ctx.Position, r.MidPt, center).DotProduct(beam.Axis));
                if (startSign == 0) startSign = 1;
                double? split = null;
                if (ctx.Position == PanelPosition.Center && Math.Abs(beam.Axis.Y) > 1e-6)
                    split = (center.Y - beam.P1.Y) / beam.Axis.Y;

                var auto = new HashSet<int>(TrippingBracketRules.PickBySpacing(sList, r.Lo, r.Hi, L, startSign, split));
                var final = new SortedSet<int>();
                for (int i = 0; i < r.Xs.Count; i++)
                {
                    bool? force = Force(ctx, r.Xs[i].hp);
                    if (force == true || (force == null && auto.Contains(i))) final.Add(i);
                }
                if (final.Count == 0)
                {
                    double len = r.Hi - r.Lo;
                    ctx.Report.Add($"{LOG} SKIP {r.Tag}: không cần ({r.Xs.Count} HP, dài {len:0} {(len <= L + TrippingBracketRules.SPACING_TOL ? "≤" : ">")} L).");
                    continue;
                }

                foreach (int i in final)
                {
                    var (hpSel, web, X, sSel) = r.Xs[i];
                    string key = $"TrippingBracket:{web.SourceKey}~{hpSel.SourceKey}";
                    if (!done.Add(key)) continue;
                    int dir = split is double sp && sp > r.Lo && sp < r.Hi ? (sSel <= sp ? 1 : -1) : startSign;
                    double delta = Delta(sSel, dir, r.Lo, r.Hi, final.Select(k => sList[k]));
                    string how = auto.Contains(i) ? "auto" : "tay On";

                    Solid3d sol = null;
                    try
                    {
                        Vector3d doz = Doz(ctx.Position, X, center);
                        sol = BuildOne(ctx, beam, web, hpSel, X, doz, key, supports, reach, Ht, H, out string place);
                        if (sol == null) { ctx.Report.Add($"{LOG} Không dựng được: {r.Tag} HP {hpSel.Id.Handle}."); continue; }
                        string tag = $"{r.Tag} · HP {hpSel.Id.Handle} s={sSel:0} Δ={delta:0}/L={L:0} ({how}) · {place}";
                        outSolids.Add((sol, tag, key));
                        ctx.Report.Add($"{LOG} {tag}.");
                    }
                    catch (System.Exception ex)
                    {
                        Debug.WriteLine($"{LOG} lỗi ({r.Tag}): {ex.Message}");
                        ctx.Report.Add($"{LOG} Lỗi {r.Tag}: {ex.Message}.");
                        sol?.Dispose();
                    }
                }
            }

            ctx.Report.Add($"{LOG} {flanged.Count} dầm có flange · {recs.Count} đoạn → {outSolids.Count} tripping bracket.");
            return outSolids;
        }

        /// <summary>Ép tay theo HP (tab Result, PartCorrection.Tripping): null = Auto.</summary>
        private static bool? Force(BuildContext ctx, PanelPart hp)
            => hp?.SourceKey != null && ctx.Cls.Corrections != null && ctx.Cls.Corrections.TryGetValue(hp.SourceKey, out var c)
                ? c?.Tripping : null;

        /// <summary>Khoảng từ gối TRƯỚC (theo chiều đi <paramref name="dir"/>: bracket khác hoặc mút đoạn) tới
        /// <paramref name="s"/> — chỉ để report.</summary>
        private static double Delta(double s, int dir, double lo, double hi, IEnumerable<double> picks)
        {
            double prev = dir > 0 ? lo : hi;
            foreach (var p in picks)
                if ((s - p) * dir > 1e-9 && Math.Abs(s - p) < Math.Abs(s - prev)) prev = p;
            return Math.Abs(s - prev);
        }

        private static Solid3d BuildOne(BuildContext ctx, Span beam, PanelPart web, PanelPart hp, Point3d X, Vector3d doz,
            string key, List<PanelPart> supports, double reach, double Ht, double H, out string place)
        {
            place = "";
            double t = hp.FootprintThickness, tW = web.FootprintThickness;
            if (t <= 1e-6 || tW <= 1e-6) return null;

            Vector3d hpAxis = hp.AxisDir;
            if (hpAxis.Length < 1e-9) { var (a0, a1) = ClassifyUtil.Endpoints(hp); hpAxis = a1 - a0; }
            hpAxis = new Vector3d(hpAxis.X, hpAxis.Y, 0);
            if (hpAxis.Length < 1e-9) return null;
            hpAxis = hpAxis.GetNormal();

            bool flip = ctx.Cls.Corrections != null && ctx.Cls.Corrections.TryGetValue(key, out var corr) && corr?.FlipDir == true;
            Vector3d n = beam.Normal.GetNormal();
            int side = TrippingBracketRules.Side(doz.DotProduct(n), flip);
            Vector3d sideN = n * side;

            Vector3d away = hpAxis.DotProduct(sideN) >= 0 ? hpAxis : -hpAxis;   // dọc trục HP, ra xa web dầm
            double cosA = away.DotProduct(sideN);
            if (cosA < MIN_AWAY_COS) return null;

            Vector3d belly = hp.BellyDir;
            belly = new Vector3d(belly.X, belly.Y, 0);
            belly = belly - hpAxis * belly.DotProduct(hpAxis);
            belly = belly.Length < 1e-9 ? new Vector3d(-hpAxis.Y, hpAxis.X, 0) : belly.GetNormal();

            Point3d P = new Point3d(X.X, X.Y, 0) + away * (tW / 2.0 / cosA);    // mặt web dầm phía bracket, trên tuyến HP
            double overhang = HpEndBracketBuilder.FlangeOverhang(supports, P, -away, belly, new[] { -t / 2.0, t / 2.0 }, out bool have);
            var (big, d1, baseY) = TrippingBracketRules.SelectBracketB(overhang, have, reach);

            var sol = HpEndBracketBuilder.BuildPlate(P, away, belly, t, reach, Ht, d1, baseY, H, centered: true);
            if (sol == null) return null;
            sol.Layer = web.Layer;                 // cùng Layer với web đoạn dầm (như bracket B theo web)
            sol.ColorIndex = web.ColorIndex;
            place = $"side={(side > 0 ? "+" : "−")}n{(flip ? " (đảo tay)" : "")} {(big ? "brkt_1" : "brkt_2")} overhang={overhang:0} d1={d1:0}";
            return sol;
        }

        /// <summary>Chiều đổ kết cấu tại <paramref name="p"/>: X luôn +1; Y Port −1 / Starboard +1 / Center về tâm
        /// (cùng luật OB/IB — skill bracket-identification).</summary>
        private static Vector3d Doz(PanelPosition pos, Point3d p, Point3d center)
        {
            double y = pos == PanelPosition.Port ? -1.0 : pos == PanelPosition.Starboard ? 1.0 : (p.Y <= center.Y ? 1.0 : -1.0);
            return new Vector3d(1.0, y, 0);
        }

        private static Span MakeSpan(List<PanelPart> run)
        {
            PanelPart spine = run.OrderByDescending(p => p.MemberLength).First();
            var (a, bpt) = ClassifyUtil.Endpoints(spine);
            Vector3d ax = bpt - a;
            ax = new Vector3d(ax.X, ax.Y, 0);
            if (ax.Length < 1e-9) return null;
            ax = ax.GetNormal();
            double lo = double.MaxValue, hi = double.MinValue;
            foreach (var p in run)
            {
                var (e1, e2) = ClassifyUtil.Endpoints(p);
                foreach (var e in new[] { e1, e2 })
                {
                    double s = (e - a).DotProduct(ax);
                    lo = Math.Min(lo, s); hi = Math.Max(hi, s);
                }
            }
            Point3d p1 = new Point3d(a.X, a.Y, 0) + ax * lo;
            return new Span
            {
                Parts = run, P1 = p1, Axis = ax, Len = hi - lo, Thk = spine.FootprintThickness,
                Center = p1 + ax * ((hi - lo) / 2.0),
            };
        }

        private static Point3d PartMid(PanelPart p)
        {
            var (e1, e2) = ClassifyUtil.Endpoints(p);
            return new Point3d((e1.X + e2.X) / 2.0, (e1.Y + e2.Y) / 2.0, 0);
        }

        private static PanelPart Spine(Span s) => s.Parts.OrderByDescending(p => p.MemberLength).First();

        /// <summary>Có Flange ∥ <paramref name="axis"/> mà dải flange (rộng = FootprintThickness) chứa điểm.</summary>
        private static bool FlangeAt(List<PanelPart> flanges, Vector3d axis, Point3d pt)
        {
            foreach (var f in flanges)
            {
                var (f1, f2) = ClassifyUtil.Endpoints(f);
                Vector3d fa = f2 - f1;
                if (fa.Length < 1e-9) continue;
                fa = new Vector3d(fa.X, fa.Y, 0).GetNormal();
                if (Math.Abs(fa.DotProduct(axis)) < PAR_COS) continue;
                if (ClassifyUtil.DistToSegment(pt, f) <= f.FootprintThickness / 2.0 + 1.0) return true;
            }
            return false;
        }

        /// <summary>1 part của run chứa <paramref name="ix"/> ở THÂN (cách 2 mút ≥ <paramref name="margin"/>, sát
        /// đường tâm) ⇒ run LIÊN TỤC tại đó.</summary>
        private static bool Covers(Span run, Point3d ix, double margin)
        {
            foreach (var p in run.Parts)
            {
                var (e1, e2) = ClassifyUtil.Endpoints(p);
                Vector3d d = e2 - e1;
                double L = d.Length;
                if (L < 1e-6) continue;
                d = d / L;
                double s = (ix - e1).DotProduct(d);
                if (s < margin || s > L - margin) continue;
                if ((ix - (e1 + d * s)).Length <= p.FootprintThickness / 2.0 + 2.0) return true;
            }
            return false;
        }


        private static bool LineIntersect(Span a, Span c, out Point3d ix)
        {
            ix = Point3d.Origin;
            double den = a.Axis.X * c.Axis.Y - a.Axis.Y * c.Axis.X;
            if (Math.Abs(den) < 1e-9) return false;
            double dx = c.P1.X - a.P1.X, dy = c.P1.Y - a.P1.Y;
            double tA = (dx * c.Axis.Y - dy * c.Axis.X) / den;
            ix = a.P1 + a.Axis * tA;
            return true;
        }
    }
}
