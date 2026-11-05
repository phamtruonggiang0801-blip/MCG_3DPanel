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
    /// Bracket đầu HP — mã "B" (`Bracket B.dwg`). Nơi HP **kết thúc cách 1 Web ngang đúng 30mm** (KHÔNG
    /// xuyên qua) → 1 gusset knee nối Web ngang + Flange, đỡ đầu HP. Thay cho Sniped End (đã bỏ ở
    /// <see cref="HpBuilder"/>). SOLID MỚI (như Bracket) — Build3DService append thẳng.
    ///
    /// Hình học + tham số: skill sniped-end §5 (xác nhận kỹ sư 2026-09-04).
    ///   thk = dày HP · Ht = WebHeight − b · reach = max(180, 30 + 1.5·b) · gap = 30 · R = topL = 50 · e15 = 15
    ///   d1: overhang ≥ 50 → brkt_1, d1 = overhang − 15, đáy trên Flange
    ///       overhang < 50 → brkt_2, d1 = 15, đáy nâng off2 = 30
    ///   mặt đặt: có mã đối xứng (HP/Web/mã kết thúc ở mặt bên kia cùng Web ngang) → đồng phẳng lưng HP;
    ///            không → lùi thêm 1·thk sau lưng HP.
    /// ⚠️ CHƯA test trực quan.
    /// </summary>
    internal static class HpEndBracketBuilder
    {
        private const string LOG = "[HpEndBracketBuilder]";

        private const double GAP = 30.0;          // khe HP → mặt gần Web ngang (đúng 30, xác nhận kỹ sư)
        private const double GAP_TOL = 16.0;      // dung sai phát hiện "End 30" (Web XIÊN: gap dọc trục ≠ ⟂)
        private const double PERP_COS = 0.70;     // |cos góc trục| ≤ ⇒ chấp nhận (Web XIÊN tới ~45°); loại ~song song
        private const double R = 50.0;            // cung toe lõm
        private const double E15 = 15.0;          // mép đầu tự do đứng
        private const double MIN_REACH = 180.0;
        private const double KNEE_ON_HP_TOL = 25.0;   // 1 đầu bracket OB/IB coi là "ngồi trên HP" nếu cách HP ≤ ngưỡng
        // Tia từ đầu HP phải cắt ĐOẠN Web thật (± ngưỡng TUYỆT ĐỐI) — trước đây ±5% chiều dài Web: Web 9.6m
        // → nới ~480mm, nhận nhầm đầu HP nằm VƯỢT đầu Web ~400mm là "End 30" (bracket B 209FE, 2026-11-04).
        private const double WEB_END_TOL = 20.0;
        // Đầu HP trùng đầu 1 HP KHÁC (mối nối/gãy khúc tuyến HP, vd 1B610 → 1B61D xiên) ⇒ không phải đầu
        // kết thúc: không bracket B. < 2·GAP + dày Web (~66) để không nhầm 2 đầu HP 2 bên 1 Web.
        private const double HP_JOINT_TOL = 25.0;

        /// <summary>Chiều cao "reach" (= vươn dọc trục) của bracket B theo chiều cao HP.</summary>
        public static double Reach(double b) => Math.Max(MIN_REACH, 30.0 + 1.5 * b);

        /// <summary>Các loại member mà đầu HP có thể "kết thúc cách 30mm" để nhận bracket B: Web, đầu Web
        /// (GirderEnd), VÀ ClosedPlate — ở vài bản vẽ (vd 4D-03P) vách/cột biên nơi HP dừng lại được vẽ
        /// thành cụm tấm kín, `PlateDetector` xếp `ClosedPlate` chứ không phải `Web`. Nếu chỉ xét `Web`
        /// thì đầu HP đó bị coi là "tự do" → Sniped End thay vì bracket B (kỹ sư báo 2026-09-04).</summary>
        private static readonly PartType[] TRANSVERSE_TYPES =
            { PartType.Web, PartType.GirderEnd, PartType.ClosedPlate };

        private static List<PanelPart> TransverseMembers(BuildContext ctx)
            => ctx.Cls.Parts.Where(p => Array.IndexOf(TRANSVERSE_TYPES, p.Type) >= 0).ToList();

        /// <summary>Đầu HP <paramref name="he"/> có phải chỗ "End 30" (cách 1 Web ~⟂ đúng 30mm) VÀ còn đủ
        /// chỗ (≥ reach) từ bracket OB/IB gần nhất tới đầu HP để đặt bracket B?
        ///   true  → dựng bracket B, KHÔNG Sniped End.
        ///   false → (nếu là End 30 nhưng bracket OB/IB quá gần) HpBuilder tự Snipe End đầu này.
        /// <paramref name="isEnd30"/> phân biệt "không phải End 30" với "End 30 nhưng chật".</summary>
        public static bool WantsEndBracket(BuildContext ctx, PanelPart hp, Point3d he, Point3d other, out bool isEnd30)
        {
            isEnd30 = false;
            var webs = TransverseMembers(ctx);
            if (webs.Count == 0) return false;

            Vector3d axis = hp.AxisDir;
            if (axis.Length < 1e-9) { var (a0, a1) = ClassifyUtil.Endpoints(hp); axis = a1 - a0; }
            axis = new Vector3d(axis.X, axis.Y, 0);
            if (axis.Length < 1e-9) return false;
            axis = axis.GetNormal();

            Vector3d outward = he - other;
            if (outward.Length < 1e-6) return false;
            outward = new Vector3d(outward.X, outward.Y, 0).GetNormal();

            if (JoinsOtherHp(ctx, hp, he)) return false;   // mối nối HP–HP: không End 30
            if (!FindTransverseWeb(webs, he, outward, axis, out _, out _, out double nearFace)) return false;
            if (Math.Abs(nearFace - GAP) > GAP_TOL) return false;
            isEnd30 = true;

            double reach = Reach(ctx.Input.HpHeight);
            return NearestKneeBracketDist(ctx, hp, he) >= reach;   // đủ chỗ → bracket B; chật → snipe
        }

        /// <summary>Đầu <paramref name="he"/> trùng (≤ <see cref="HP_JOINT_TOL"/>) 1 đầu của HP KHÁC — mối nối /
        /// gãy khúc tuyến HP, không phải đầu kết thúc.</summary>
        private static bool JoinsOtherHp(BuildContext ctx, PanelPart hp, Point3d he)
        {
            foreach (var o in ctx.Cls.OfType(PartType.HP))
            {
                if (ReferenceEquals(o, hp)) continue;
                var (o1, o2) = ClassifyUtil.Endpoints(o);
                if (Flat(o1).DistanceTo(Flat(he)) <= HP_JOINT_TOL || Flat(o2).DistanceTo(Flat(he)) <= HP_JOINT_TOL)
                    return true;
            }
            return false;
        }

        private static Point3d Flat(Point3d p) => new Point3d(p.X, p.Y, 0);

        /// <summary>Khoảng cách từ đầu-bám-HP của bracket OB/IB gần nhất (ngồi trên <paramref name="hp"/>)
        /// tới đầu HP <paramref name="he"/>. MaxValue nếu không có bracket nào → luôn đủ chỗ.</summary>
        private static double NearestKneeBracketDist(BuildContext ctx, PanelPart hp, Point3d he)
        {
            double best = double.MaxValue;
            foreach (var brk in ctx.Cls.OfType(PartType.Bracket))
            {
                var (be1, be2) = ClassifyUtil.Endpoints(brk);
                double d1 = ClassifyUtil.DistToPart(be1, hp), d2 = ClassifyUtil.DistToPart(be2, hp);
                double dm = Math.Min(d1, d2);
                if (dm > KNEE_ON_HP_TOL) continue;                 // bracket không bám HP này
                Point3d onHp = d1 <= d2 ? be1 : be2;
                double dist = onHp.DistanceTo(he);
                if (dist < best) best = dist;
            }
            return best;
        }

        public static List<(Solid3d sol, string tag, string key)> BuildHpEndBrackets(BuildContext ctx)
        {
            var outSolids = new List<(Solid3d, string, string)>();
            if (ctx?.Cls == null) return outSolids;

            var hps = ctx.Cls.OfType(PartType.HP).ToList();
            if (hps.Count == 0) return outSolids;
            var webs = TransverseMembers(ctx);   // Web + GirderEnd + ClosedPlate (xem TRANSVERSE_TYPES)
            if (webs.Count == 0) return outSolids;
            var supports = ctx.Cls.OfType(PartType.Flange).Concat(ctx.Cls.OfType(PartType.FacePlate)).ToList();   // tấm đỡ chân bracket B

            double b = ctx.Input.HpHeight;
            double H = ctx.Input.WebHeight;
            if (b <= 0 || H <= 0) { ctx.Report.Add($"{LOG} b/WebHeight không hợp lệ → bỏ qua."); return outSolids; }
            double Ht = H - b;
            if (Ht <= R + E15) { ctx.Report.Add($"{LOG} Ht={Ht:0.#} quá thấp → bỏ qua."); return outSolids; }
            double reach = Math.Max(MIN_REACH, 30.0 + 1.5 * b);

            int nEnds = 0;
            foreach (var hp in hps)
            {
                double t = hp.FootprintThickness;
                if (t <= 1e-6) continue;

                Vector3d axis = hp.AxisDir;
                if (axis.Length < 1e-9) { var (a0, a1) = ClassifyUtil.Endpoints(hp); axis = a1 - a0; }
                axis = new Vector3d(axis.X, axis.Y, 0);
                if (axis.Length < 1e-9) continue;
                axis = axis.GetNormal();

                Vector3d belly = hp.BellyDir;
                if (belly.Length < 1e-9) continue;
                belly = new Vector3d(belly.X, belly.Y, 0);
                belly = belly - axis * belly.DotProduct(axis);
                if (belly.Length < 1e-9) continue;
                belly = belly.GetNormal();

                var (e1, e2) = ClassifyUtil.Endpoints(hp);
                foreach (var (he, other) in new[] { (e1, e2), (e2, e1) })
                {
                    Vector3d outward = he - other;
                    if (outward.Length < 1e-6) continue;
                    outward = new Vector3d(outward.X, outward.Y, 0).GetNormal();

                    // Tab Result ÉP VÁT đầu này ⇒ không bracket B ("End 30: bracket HOẶC vát"). Ép VUÔNG không đổi
                    // gì ở đây: đầu End 30 bị vát tự động vốn đã không có bracket B ⇒ để vuông (kỹ sư 2026-11-04).
                    if (SnipOverride.Forced(ctx.Cls, hp, he) == true)
                    {
                        ctx.Report.Add($"{LOG} HP {hp.Id.Handle} end@({he.X:0},{he.Y:0}): Sniped End ép tay (tab Result) → không bracket B.");
                        continue;
                    }

                    if (JoinsOtherHp(ctx, hp, he))
                    {
                        ctx.Report.Add($"{LOG} HP {hp.Id.Handle} end@({he.X:0},{he.Y:0}): nối tiếp đầu HP khác → không bracket B.");
                        continue;
                    }
                    if (!FindTransverseWeb(webs, he, outward, axis, out PanelPart web, out Point3d ixWeb, out double nearFace))
                        continue;
                    if (Math.Abs(nearFace - GAP) > GAP_TOL) continue;   // không phải "End 30"
                    nEnds++;

                    double dKnee = NearestKneeBracketDist(ctx, hp, he);
                    if (dKnee < reach)                                  // bracket OB/IB quá gần → HpBuilder tự Snipe End
                    {
                        ctx.Report.Add($"{LOG} HP {hp.Id.Handle} end@({he.X:0},{he.Y:0}): bracket OB/IB cách {dKnee:0} < reach {reach:0} → SnipedEnd (không bracket B).");
                        continue;
                    }

                    // Gốc dọc trục = điểm mặt gần Web ngang, trên tuyến tâm HP.
                    Point3d P = new Point3d(he.X + outward.X * nearFace, he.Y + outward.Y * nearFace, 0.0);

                    // sym quyết định MẶT bản thép (Build: sym → [−t/2, +t/2] quanh tuyến HP; không → [+t/2, +3t/2]
                    // về phía bụng) — phải biết TRƯỚC để đo overhang đúng tại 2 mặt đó.
                    bool sym = HasOppositeTermination(ctx, hp, ixWeb, outward, axis, web);
                    double[] faces = sym ? new[] { -t / 2.0, t / 2.0 } : new[] { t / 2.0, 1.5 * t };

                    double overhang = FlangeOverhang(supports, P, outward, belly, faces, out bool haveFlange);
                    var (big, d1, baseY) = TrippingBracketRules.SelectBracketB(overhang, haveFlange, reach);

                    string tag = $"HP {hp.Id.Handle} end@({he.X:0},{he.Y:0}) × {web.Type} {web.Id.Handle} · " +
                                 $"{(big ? "brkt_1" : "brkt_2")} overhang={overhang:0} d1={d1:0} sym={sym}";

                    Solid3d sol = null;
                    try
                    {
                        sol = BuildPlate(P, -outward, belly, t, reach, Ht, d1, baseY, H, sym);
                        if (sol == null) { ctx.Report.Add($"{LOG} Không dựng được: {tag}."); continue; }
                        sol.Layer = web.Layer;                       // cùng Layer với Web ngang (xác nhận kỹ sư)
                        sol.ColorIndex = web.ColorIndex;
                        // Khoá Delete ỔN ĐỊNH giữa các lần rebuild: HP nguồn + đầu lo/hi (không dùng overhang/d1… vốn đổi theo hình).
                        outSolids.Add((sol, tag, $"HpEndBracketB:{hp.SourceKey}@{(SnipOverride.IsLo(hp, he) ? "lo" : "hi")}"));
                        ctx.Report.Add($"{LOG} {tag}.");
                    }
                    catch (System.Exception ex)
                    {
                        Debug.WriteLine($"{LOG} lỗi ({tag}): {ex.Message}");
                        ctx.Warn($"{LOG} Lỗi: {tag}: {ex.Message}.");
                        sol?.Dispose();
                    }
                }
            }

            ctx.Report.Add($"{LOG} {hps.Count} HP · {nEnds} đầu 'End 30' → {outSolids.Count} bracket B.");
            return outSolids;
        }

        /// <summary>Web mà tuyến tâm HP cắt qua, phía <paramref name="outward"/> của đầu HP.
        /// <paramref name="nearFace"/> = khoảng cách DỌC TRỤC HP từ đầu HP tới MẶT GẦN của Web
        /// (hiệu chỉnh Web XIÊN: bề dày biểu kiến dọc trục = <c>wThk / |outward × wAxis|</c>).</summary>
        private static bool FindTransverseWeb(List<PanelPart> webs, Point3d he, Vector3d outward, Vector3d axis,
            out PanelPart web, out Point3d ix, out double nearFace)
        {
            web = null; ix = Point3d.Origin; nearFace = double.MaxValue;
            double bestCenter = double.MaxValue;
            foreach (var w in webs)
            {
                double wThk = w.FootprintThickness;
                if (wThk <= 1e-6) continue;
                var (w1, w2) = ClassifyUtil.Endpoints(w);
                Vector3d wAxis = w2 - w1;
                if (wAxis.Length < 1e-9) continue;
                wAxis = new Vector3d(wAxis.X, wAxis.Y, 0).GetNormal();
                if (Math.Abs(wAxis.DotProduct(axis)) > PERP_COS) continue;   // ~song song → loại

                if (!LineSegX(he, outward, w1, w2, out Point3d p, out double tW)) continue;
                double segLen = (w2 - w1).Length;
                double tolT = WEB_END_TOL / Math.Max(segLen, 1.0);              // nới TUYỆT ĐỐI, không theo %
                if (tW < -tolT || tW > 1.0 + tolT) continue;
                double dCenter = (p - he).DotProduct(outward);
                if (dCenter <= 1.0) continue;                                 // Web ở sau đầu HP
                if (dCenter >= bestCenter) continue;

                double sinPhi = Math.Abs(outward.X * wAxis.Y - outward.Y * wAxis.X);   // |outward × wAxis|
                double apparentHalf = wThk / 2.0 / Math.Max(sinPhi, 0.2);
                bestCenter = dCenter; ix = p; web = w; nearFace = dCenter - apparentHalf;
            }
            return web != null;
        }

        /// <summary>
        /// Overhang của tấm đỡ (Flange + FacePlate) dưới bracket B: ray-cast từ <paramref name="p"/> (mặt gần Web
        /// ngang, trên tuyến tâm HP) theo hướng VÀO khoang (−<paramref name="outward"/>), lấy mép ngoài của dải tấm
        /// đỡ liền nhau chứa gốc tia — CÙNG phép đo với OB/IB (<see cref="BracketBuilder.RayUnionHit"/>).
        /// ⚠️ Bản cũ chỉ xét flange CHẠY DỌC HP (|fAxis·hpAxis| ≥ 0.7) nên LOẠI chính Flange girder của Web ngang
        /// (⟂ HP) mà bracket B đứng lên ⇒ overhang=0 → brkt_2 nâng 30 oan (Flange 1B631 rộng 220, 32/35 bracket B
        /// panel 7D-16C, 2026-11-04). Ray-cast đo đúng dọc tuyến HP nên không tái lỗi bbox "overhang=1500".
        /// Đo tại 2 MẶT thật của bản thép (lệch <paramref name="faces"/> theo <paramref name="belly"/>), lấy MIN —
        /// flange XIÊN: mép tại mặt bản thép gần hơn tại tuyến tâm HP (bracket B 20E72 toe sát mép, 2026-11-04).
        /// </summary>
        internal static double FlangeOverhang(List<PanelPart> supports, Point3d p, Vector3d outward, Vector3d belly,
                                             double[] faces, out bool have)
        {
            double hit = BracketBuilder.FaceMinHit(p, -outward, belly, faces, supports);
            have = hit > 1e-6;
            return have ? hit : 0.0;
        }

        /// <summary>Có 1 HP/FlatBar/Web/Bracket/GE khác KẾT THÚC ở MẶT ĐỐI DIỆN của <paramref name="web"/>
        /// (~thẳng hàng với trục HP) → có backing → cặp bracket đối xứng (tấm nằm giữa thân HP).</summary>
        private static bool HasOppositeTermination(BuildContext ctx, PanelPart self, Point3d ixWeb,
            Vector3d outward, Vector3d hpAxis, PanelPart web)
        {
            double wThk = web.FootprintThickness;
            double reachOpp = 2.0 * GAP + wThk + 25.0;                        // đối diện có thể cũng lùi 30
            double latTol = wThk + 30.0;
            Vector3d oppDir = -outward;
            foreach (var o in ctx.Cls.Parts)
            {
                if (ReferenceEquals(o, self) || ReferenceEquals(o, web)) continue;
                switch (o.Type)
                {
                    case PartType.HP: case PartType.FlatBar: case PartType.Web:
                    case PartType.Bracket: case PartType.GirderEnd: break;
                    default: continue;
                }
                var (a, bpt) = ClassifyUtil.Endpoints(o);
                foreach (var e in new[] { a, bpt })
                {
                    Vector3d d = e - ixWeb;
                    double along = d.DotProduct(oppDir);
                    if (along < -wThk || along > reachOpp) continue;         // ở phía đối diện Web
                    double lat = (d - oppDir * along).Length;
                    if (lat <= latTol) return true;
                }
            }
            return false;
        }

        /// <summary>Dựng tấm bracket B (polygon <see cref="TrippingBracketRules.BracketBPolygon"/>) — DÙNG CHUNG cho
        /// bracket B đầu HP ("End 30") và Tripping Bracket. <paramref name="p"/> = mặt web (ngang / dầm) trên tuyến
        /// tâm HP; <paramref name="localX"/> = dọc trục HP, RA XA web (+reach); <paramref name="thkDir"/> = bề dày
        /// (⟂ localX, thường = bụng HP); <paramref name="centered"/> ⇒ tấm căn giữa tuyến HP, ngược lại lệch 1·t
        /// về <paramref name="thkDir"/>. Đáy local (y=0) = mặt trên flange <c>Z = −H</c>.</summary>
        internal static Solid3d BuildPlate(Point3d p, Vector3d localX, Vector3d thkDir,
            double t, double reach, double Ht, double d1, double baseY, double H, bool centered)
        {
            var poly = TrippingBracketRules.BracketBPolygon(reach, Ht, d1, baseY)
                .Select(v => new VertexBulge(v.X, v.Y, v.Bulge)).ToList();
            Region reg = GeometryUtil.RegionFrom(poly);
            if (reg == null) return null;

            // Mặt đặt (xác nhận kỹ sư 2026-09-04): có backing mặt đối diện → tấm NẰM NGAY DƯỚI HP
            // (đồng phẳng thân HP, tâm trùng tuyến HP). Không backing → lệch 1·thk về phía BỤNG.
            Point3d o = p - thkDir * (t / 2.0);                          // centered: căn giữa thân HP
            if (!centered) o = o + thkDir * t;                           // không backing: lệch 1·thk về bụng
            o = new Point3d(o.X, o.Y, -H);                               // mặt trên Flange = Z = −WebHeight

            reg.TransformBy(Matrix3d.AlignCoordinateSystem(
                Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                o, localX, Vector3d.ZAxis, thkDir));

            var sol = new Solid3d();
            var sob = new SweepOptionsBuilder();
            try { sol.CreateExtrudedSolid(reg, thkDir * t, sob.ToSweepOptions()); }
            catch { reg.Dispose(); sol.Dispose(); return null; }
            reg.Dispose();
            return sol;
        }

        /// <summary>Giao đường (a, dir) với đoạn (s1,s2). <paramref name="tSeg"/> ∈ [0,1] nếu trong đoạn.</summary>
        private static bool LineSegX(Point3d a, Vector3d dir, Point3d s1, Point3d s2, out Point3d ix, out double tSeg)
        {
            ix = Point3d.Origin; tSeg = 0;
            double dsx = s2.X - s1.X, dsy = s2.Y - s1.Y;
            double den = dir.X * dsy - dir.Y * dsx;
            if (Math.Abs(den) < 1e-9) return false;
            tSeg = (dir.X * (s1.Y - a.Y) - dir.Y * (s1.X - a.X)) / -den;
            ix = new Point3d(s1.X + tSeg * dsx, s1.Y + tSeg * dsy, 0);
            return true;
        }
    }
}
