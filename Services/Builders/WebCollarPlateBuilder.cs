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
    /// "Collar Plate / LUG B1R" (Hatch <c>1F3F2</c> trong CAS.dwt) — tấm bù hàn quanh lỗ CO nơi HP xuyên
    /// Web. Khác <see cref="WebHpCutoutBuilder"/> (chỉ TRỪ boolean): đây là SOLID MỚI (như Bracket) —
    /// build xong Build3DService append thẳng vào block 3D.
    ///
    /// Quy tắc VỊ TRÍ (xác nhận kỹ sư 2026-08-28 — xem skill web-hp-cutout §7): 3 dòng note = 3 rule
    /// CỘNG THÊM, ĐỘC LẬP (KHÔNG phải cổng chung):
    ///   • HP (chạy ngang) xuyên qua "Web dọc". Tại mỗi chỗ 1 "Web ngang" (~⟂ web dọc) giao vào web dọc,
    ///     chỉ 2 lỗ HP GẦN điểm giao đó nhất được collar — LUÔN áp dụng, KHÔNG phụ thuộc chiều cao web.
    ///     HP xa hơn: chỉ có cutout, không collar.
    ///   • "web &lt; 300" (rule 1): tool hiện 1 H chung + kỹ sư chốt "vẫn chỉ 2 HP quanh junction" ⇒ hiện
    ///     KHÔNG thêm gì so với rule trên. (Tương lai — khi có H riêng từng web: web &lt; 300 → collar MỌI lỗ.)
    ///   • note 3 (partial transverse web ∥ corner support) — BỎ tạm (Plan 2D không có corner support).
    ///
    /// Hình học: tấm phẳng trong MẶT PHẲNG WEB, phía BỤNG HP, dày <c>tl = webThk</c>, KHÔNG có "váy" B1,
    /// KHÔNG chồng thân/bầu HP → không cần trừ lỗ. Tiết diện: <see cref="HpProfileService.CollarPlateSection"/>.
    /// </summary>
    internal static class WebCollarPlateBuilder
    {
        private const string LOG = "[WebCollarPlateBuilder]";

        // Rule 1 "web < 300" KHÔNG dùng làm gate (kỹ sư 2026-08-28: 3 rule cộng thêm độc lập; rule
        // "2 HP quanh junction" luôn áp dụng). Ghi lại cho tương lai: khi có H riêng từng web →
        // web < 300 thì collar MỌI lỗ HP của web đó.

        // |cos(góc trục)| ≤ ngưỡng ⇒ coi 2 web "vuông góc" (web ngang ⟂ web dọc). ~70°.
        private const double PERP_COS = 0.34;

        // Số lỗ HP MỖI PHÍA của mỗi junction web-ngang được collar ("OUTMOST 2 HP SLOTS" — kỹ sư
        // 2026-08-28: 2 lỗ mỗi bên, không phải 1).
        private const int SLOTS_PER_SIDE = 2;

        // Web ngang phải ĐI XUYÊN web dọc (điểm giao nằm TRONG thân web ngang, không phải ở đầu mút nó
        // = web ngang KẾT THÚC tại web dọc). Lề đo theo mm dọc trục web ngang.
        private const double NGANG_THROUGH_MARGIN = 60.0;

        // Lỗ HP chỉ nhận collar nếu cách junction ≤ ngưỡng này (bội của bước HP median trên web dọc) —
        // tránh "lỗ gần nhất" thực ra cách junction hàng mét (web dọc gần như không có lỗ ở đoạn đó).
        private const double SLOT_MAX_PITCH_FACTOR = 2.6;
        private const double SLOT_MAX_ABS = 2200.0;   // fallback khi < 2 lỗ để tính bước

        /// <summary>Dựng mọi Collar Plate cần thiết. Solid đã set Layer/Color theo web dọc — Build3DService
        /// chỉ việc <c>AppendEntity</c>. <c>tag</c> = mô tả xuất xứ (web dọc × HP × web ngang × phía) để
        /// truy vết "vì sao có collar này". Lỗi ở 1 tấm không làm hỏng tấm khác (ghi Report).</summary>
        public static List<(Solid3d sol, string tag)> BuildCollarPlates(BuildContext ctx)
        {
            var outSolids = new List<(Solid3d sol, string tag)>();
            if (ctx?.Cls == null) return outSolids;

            var runs = ctx.Cls.WebRuns().Where(r => r != null && r.Count > 0).ToList();
            if (runs.Count == 0) return outSolids;

            var hps = ctx.Cls.OfType(PartType.HP).ToList();
            if (hps.Count == 0) return outSolids;

            double b = ctx.Input.HpHeight;
            if (b <= 0)
            {
                ctx.Report.Add($"{LOG} HpHeight ({b:0.#}) không hợp lệ → bỏ qua Collar Plate.");
                return outSolids;
            }
            HpProfileService.LookupCR(b, out double c, out double r, out bool est);
            HpProfileService.LookupCoRadius(b, out double coR);

            // Trục + đoạn tâm ĐẦY ĐỦ của từng web run (gộp mọi tier).
            var spans = runs.Select(BuildRunSpan).ToList();

            var done = new HashSet<string>();   // "webPartHandle~hpHandle" — 1 cặp gần 2 junction chỉ dựng 1 lần
            int totCands = 0, totJunctions = 0;

            for (int i = 0; i < runs.Count; i++)
            {
                var run = runs[i];
                var span = spans[i];
                if (span.len < 1e-6) continue;

                // 1) Các lỗ CO trên web dọc này: (part bị xuyên, HP, điểm giao, vị trí dọc trục run).
                var cands = new List<(PanelPart webPart, PanelPart hp, Point3d cross, double s)>();
                foreach (var part in run)
                {
                    foreach (var hp in hps)
                    {
                        if (!ClassifyUtil.CrossesThrough(hp, part, out double tCross)) continue;
                        var (h1, h2) = ClassifyUtil.Endpoints(hp);
                        double hLen = h1.DistanceTo(h2);
                        if (hLen < 1e-6) continue;
                        if (tCross < b * 0.5 || tCross > hLen - b * 0.5) continue;   // HP kết thúc tại web → không khoét/không collar
                        Vector3d hAxis = (h2 - h1) / hLen;
                        Point3d cross = h1 + hAxis * tCross;
                        double s = (cross - span.p1).DotProduct(span.axis);
                        cands.Add((part, hp, cross, s));
                    }
                }
                totCands += cands.Count;
                if (cands.Count == 0) continue;

                // 2) Junction với "web ngang": web run KHÁC, ~⟂, đoạn tâm cắt đoạn tâm run này.
                var junctions = new List<(double pos, RunSpan wn)>();
                for (int j = 0; j < runs.Count; j++)
                {
                    if (j == i) continue;
                    var o = spans[j];
                    if (o.len < 1e-6) continue;
                    if (Math.Abs(span.axis.DotProduct(o.axis)) > PERP_COS) continue;   // không vuông góc → không phải web ngang
                    if (!LineIntersect(span.p1, span.p2, o.p1, o.p2, out Point3d ix, out double tRun)) continue;
                    if (tRun < -0.03 || tRun > 1.03) continue;                          // giao ngoài web dọc
                    // Điểm giao phải nằm TRONG thân web ngang (không phải ở đầu mút web ngang = web ngang
                    // KẾT THÚC tại web dọc, VD girder ngang chạm khung biên) — cùng lý lẽ T-junction của
                    // ClassifyUtil.CrossesThrough.
                    double sNgang = (ix - o.p1).DotProduct(o.axis);
                    if (sNgang < NGANG_THROUGH_MARGIN || sNgang > o.len - NGANG_THROUGH_MARGIN) continue;
                    junctions.Add((tRun * span.len, o));
                }
                totJunctions += junctions.Count;
                if (junctions.Count == 0) continue;

                // 3) Mỗi junction → SLOTS_PER_SIDE lỗ HP liền kề MỖI PHÍA ("OUTMOST 2 HP SLOTS").
                //    KHÔNG phải "N gần nhất theo |Δ|" — 2 web ngang sát nhau ⇒ cách đó bỏ sót HP ngoài.
                var ordered = cands.OrderBy(x => x.s).ToList();
                double slotCap = SlotCap(ordered);
                foreach (var (jpos, wn) in junctions)
                {
                    int iRight = ordered.FindIndex(x => x.s > jpos + 1e-6);
                    if (iRight < 0) iRight = ordered.Count;                  // junction sau mọi lỗ
                    var pick = new List<(PanelPart webPart, PanelPart hp, Point3d cross, double s, string side)>();
                    for (int m = iRight - 1, n = 1; m >= 0 && m >= iRight - SLOTS_PER_SIDE; m--, n++)   // TRÁI
                        if (Math.Abs(ordered[m].s - jpos) <= slotCap)
                            pick.Add((ordered[m].webPart, ordered[m].hp, ordered[m].cross, ordered[m].s, "L" + n));
                    for (int m = iRight, n = 1; m < ordered.Count && m < iRight + SLOTS_PER_SIDE; m++, n++) // PHẢI
                        if (Math.Abs(ordered[m].s - jpos) <= slotCap)
                            pick.Add((ordered[m].webPart, ordered[m].hp, ordered[m].cross, ordered[m].s, "R" + n));

                    foreach (var k in pick)
                    {
                        string key = k.webPart.Id.Handle + "~" + k.hp.Id.Handle;
                        if (!done.Add(key)) continue;

                        string tag = $"webDọc {k.webPart.Id.Handle} × HP {k.hp.Id.Handle} ← junction webNgang " +
                                     $"{wn.spineHandle} (phía {k.side}, jpos={jpos:0} s={k.s:0})";

                        Solid3d sol = null;
                        try
                        {
                            sol = BuildOne(ctx, k.webPart, k.hp, k.cross, wn, b, c, r, coR);
                            if (sol == null)
                            {
                                ctx.Report.Add($"{LOG} Không dựng được collar: {tag} (thiếu BellyDir / tiết diện lỗi).");
                                continue;
                            }
                            sol.Layer = k.webPart.Layer;
                            sol.ColorIndex = k.webPart.ColorIndex;
                            outSolids.Add((sol, tag));
                            ctx.Report.Add($"{LOG} Collar: {tag} · tl={k.webPart.FootprintThickness:0.#} R={coR:0}{(est ? " (c/r NỘI SUY)" : "")}.");
                        }
                        catch (System.Exception ex)
                        {
                            Debug.WriteLine($"{LOG} lỗi ({tag}): {ex.Message}");
                            ctx.Report.Add($"{LOG} Lỗi collar: {tag}: {ex.Message}.");
                            sol?.Dispose();
                        }
                    }
                }
            }

            ctx.Report.Add($"{LOG} {runs.Count} web run · {totCands} lỗ CO · {totJunctions} junction web-ngang → {outSolids.Count} Collar Plate.");
            return outSolids;
        }

        /// <summary>1 tấm collar: tiết diện <see cref="HpProfileService.CollarPlateSection"/> đặt trong mặt
        /// phẳng web, phía BỤNG HP, áp lên MẶT web về phía <paramref name="wn"/> (web ngang), extrude
        /// <c>tl = webThk</c> ra ngoài.</summary>
        private static Solid3d BuildOne(BuildContext ctx, PanelPart web, PanelPart hp, Point3d cross,
            RunSpan wn, double b, double c, double r, double coR)
        {
            double t = hp.FootprintThickness;
            double tl = web.FootprintThickness;
            if (t <= 1e-6 || tl <= 1e-6) return null;

            Vector3d axis = hp.AxisDir;
            if (axis.Length < 1e-9) axis = ClassifyUtil.Endpoints(hp).Item2 - ClassifyUtil.Endpoints(hp).Item1;
            axis = new Vector3d(axis.X, axis.Y, 0);
            if (axis.Length < 1e-9) return null;
            axis = axis.GetNormal();

            Vector3d belly = hp.BellyDir;
            if (belly.Length < 1e-9) return null;
            belly = new Vector3d(belly.X, belly.Y, 0);
            belly = belly - axis * belly.DotProduct(axis);        // ép ⟂ axis (AlignCoordinateSystem đòi trực giao)
            if (belly.Length < 1e-9) return null;
            belly = belly.GetNormal();

            var section = ctx.HpProfile.CollarPlateSection(b, t, c, r, coR);
            Region reg = GeometryUtil.RegionFrom(section);
            if (reg == null) return null;

            // Mặt web nào (dọc trục HP): về phía web ngang (nơi truyền lực). ⚠️ giả định — cần test trực quan.
            double side = (wn.center - cross).DotProduct(axis) >= 0 ? 1.0 : -1.0;

            // Gốc tiết diện (x=0) = mặt LƯNG thân HP tại chỗ giao; đặt tại MẶT web phía `side`, Z=0.
            Point3d luru = new Point3d(cross.X, cross.Y, 0.0) - belly * (t / 2.0);
            Point3d o = luru + axis * (side * tl / 2.0);

            // local X→belly, local Y→+Z thế giới, local Z→axis (KHÔNG lật theo side — tránh mirror tiết diện).
            reg.TransformBy(Matrix3d.AlignCoordinateSystem(
                Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                o, belly, Vector3d.ZAxis, axis));

            var sol = new Solid3d();
            var sob = new SweepOptionsBuilder();
            try { sol.CreateExtrudedSolid(reg, axis * (side * tl), sob.ToSweepOptions()); }
            catch { reg.Dispose(); sol.Dispose(); return null; }
            reg.Dispose();
            return sol;
        }

        // ---- hình học web run ----

        private struct RunSpan
        {
            public Point3d p1, p2;      // 2 mút đoạn tâm ĐẦY ĐỦ (gộp mọi tier)
            public Vector3d axis;       // p1→p2 chuẩn hoá
            public double len;
            public double thk;          // FootprintThickness của tier dài nhất
            public Point3d center;
            public string spineHandle;  // handle tier dài nhất (truy vết report)
        }

        private static RunSpan RunSpanEmpty => new RunSpan { axis = Vector3d.XAxis, spineHandle = "?" };

        private static RunSpan BuildRunSpan(List<PanelPart> run)
        {
            PanelPart spine = run.OrderByDescending(p => p.MemberLength).First();
            var (a, bpt) = ClassifyUtil.Endpoints(spine);
            Vector3d ax = bpt - a;
            if (ax.Length < 1e-9) return RunSpanEmpty;
            ax = ax.GetNormal();

            double lo = double.MaxValue, hi = double.MinValue;
            foreach (var p in run)
            {
                var (e1, e2) = ClassifyUtil.Endpoints(p);
                foreach (var e in new[] { e1, e2 })
                {
                    double s = (e - a).DotProduct(ax);
                    if (s < lo) lo = s;
                    if (s > hi) hi = s;
                }
            }
            Point3d p1 = a + ax * lo, p2 = a + ax * hi;
            return new RunSpan
            {
                p1 = p1,
                p2 = p2,
                axis = ax,
                len = hi - lo,
                thk = spine.FootprintThickness,
                center = new Point3d((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2, 0),
                spineHandle = spine.Id.Handle.ToString(),
            };
        }

        /// <summary>Giao 2 đường thẳng (vô hạn) qua a1a2 và b1b2. <paramref name="tA"/> = tham số trên a1→a2.
        /// false nếu song song.</summary>
        private static bool LineIntersect(Point3d a1, Point3d a2, Point3d b1, Point3d b2, out Point3d ix, out double tA)
        {
            ix = Point3d.Origin; tA = 0;
            double dax = a2.X - a1.X, day = a2.Y - a1.Y;
            double dbx = b2.X - b1.X, dby = b2.Y - b1.Y;
            double den = dax * dby - day * dbx;
            if (Math.Abs(den) < 1e-9) return false;
            tA = ((b1.X - a1.X) * dby - (b1.Y - a1.Y) * dbx) / den;
            ix = new Point3d(a1.X + tA * dax, a1.Y + tA * day, 0);
            return true;
        }

        /// <summary>Ngưỡng khoảng cách junction→lỗ HP để nhận collar = <see cref="SLOT_MAX_PITCH_FACTOR"/>×
        /// bước HP median (khoảng cách 2 lỗ liên tiếp dọc trục web dọc), kẹp sàn <see cref="SLOT_MAX_ABS"/>.</summary>
        private static double SlotCap(List<(PanelPart webPart, PanelPart hp, Point3d cross, double s)> ordered)
        {
            if (ordered.Count < 2) return SLOT_MAX_ABS;
            var gaps = new List<double>(ordered.Count - 1);
            for (int i = 1; i < ordered.Count; i++) gaps.Add(ordered[i].s - ordered[i - 1].s);
            gaps.Sort();
            double median = gaps[gaps.Count / 2];
            return Math.Max(SLOT_MAX_ABS, median * SLOT_MAX_PITCH_FACTOR);
        }

        private static double DistPointToSeg(Point3d p, Point3d a, Point3d b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len2 = dx * dx + dy * dy;
            if (len2 < 1e-9) return p.DistanceTo(a);
            double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2;
            t = Math.Max(0, Math.Min(1, t));
            return p.DistanceTo(new Point3d(a.X + t * dx, a.Y + t * dy, 0));
        }
    }
}
