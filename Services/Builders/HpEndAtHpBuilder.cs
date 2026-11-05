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
    /// <b>"HP TO HP END"</b> (CAS.dwt → CAS.dump.json, xác nhận kỹ sư 2026-11-04): đầu 1 HP (<i>HP đầu</i>)
    /// kết thúc chạm THÂN 1 HP khác (<i>HP chạy</i>). Giống <see cref="WebHpCutoutBuilder"/>: không tự dựng
    /// solid — dựng "dao cắt" rồi TRỪ (BoolSubtract) thẳng vào solid HP đầu đã dựng. Gọi từ
    /// <see cref="Build3DService"/> SAU vòng lặp dựng chính.
    ///
    /// Dao cắt định nghĩa trong MẶT CẮT HP CHẠY (phương án A): local X → pháp tuyến mặt tiếp xúc hướng về
    /// HP đầu, local Y → +Z, local Z → trục HP chạy (= X × Y, hệ thuận tay); extrude dọc trục HP chạy
    /// ⇒ đúng cả HP XIÊN trong mặt bằng (khe hở tới bulb luôn đúng R=30 đo ⟂ HP chạy). Biên dạng:
    /// <see cref="HpEndAtHpGeometry.CutterOutline"/>; nhận diện: <see cref="HpEndAtHpGeometry.Match"/>.
    /// Không vát 5×5, không override tab Result (kỹ sư). Xem skill hp-end-at-hp.
    /// </summary>
    internal static class HpEndAtHpBuilder
    {
        private const string LOG = "[HpEndAtHp]";

        /// <summary>Trừ dao cắt "HP TO HP END" khỏi các solid HP đầu trong <paramref name="hpSolids"/> (sửa
        /// TRỰC TIẾP). Lỗi 1 cặp → bỏ cặp, ghi Report. Trả khoá <c>"hpHandle~runHandle"</c> đã trừ thành công.</summary>
        public static List<string> Apply(BuildContext ctx, IReadOnlyList<(PanelPart hp, Solid3d sol)> hpSolids)
        {
            var done = new List<string>();
            if (hpSolids == null || hpSolids.Count == 0) return done;

            var hps = Runs(ctx, report: true);
            if (hps.Count < 2) return done;   // cần ≥ 1 HP đầu + ≥ 1 member chạy (HP/FB)

            double b = ctx.Input.HpHeight;
            if (b <= 0)
            {
                ctx.Report.Add($"{LOG} HpHeight ({b:0.#}) không hợp lệ → bỏ qua HP end tại HP/FB.");
                return done;
            }
            HpProfileService.LookupCR(b, out double c, out double r, out bool est);

            foreach (var (h, sol) in hpSolids)
            {
                if (sol == null) continue;
                var (e1, e2) = ClassifyUtil.Endpoints(h);
                foreach (var (he, ho) in new[] { (e1, e2), (e2, e1) })
                {
                    HpEndMatch best = FindRun(ctx, hps, h, he, ho, c, r, out PanelPart run,
                                              out HpEndMatch angleSkip, out PanelPart angleRun, report: true);
                    if (best == null)
                    {
                        if (angleSkip != null)
                            ctx.Report.Add($"{LOG} SKIP angle h={h.Id.Handle} m={angleRun.Id.Handle} " +
                                           $"θ={Deg(angleSkip.CosTheta):0}° < 45° — giữ đầu vuông.");
                        continue;
                    }

                    Solid3d cutter = null;
                    try
                    {
                        // Dựng dao cắt TRƯỚC — không có dao thì KHÔNG nối dài (khối nối thừa sẽ không được xén;
                        // review 2026-11-04).
                        cutter = BuildCutter(ctx, h, run, best, b, c, r);
                        if (cutter == null)
                        {
                            ctx.Report.Add($"{LOG} Không dựng được dao cắt h={h.Id.Handle} m={run.Id.Handle} " +
                                           $"(tiết diện lỗi / không còn mặt hàn) → HP giữ nguyên.");
                            continue;
                        }
                        // Vẽ DỪNG HỤT mặt web (vd tới mép bulb) → nối dài HP đầu tới mặt tiếp xúc trước khi cắt
                        // — phần nối là dải web t_h×b; phần lấn qua mặt tiếp xúc + phần dưới bị dao cắt xén lại.
                        double fill = HpEndAtHpGeometry.FillLength(best.Gap, h.FootprintThickness, best.CosTheta);
                        if (fill > 0)
                        {
                            using (var ext = BuildFill(he, ho, h.FootprintThickness, b, fill))
                                if (ext != null) sol.BooleanOperation(BooleanOperationType.BoolUnite, ext);
                        }
                        sol.BooleanOperation(BooleanOperationType.BoolSubtract, cutter);
                        cutter = null;   // BoolSubtract đã tiêu thụ cutter
                        done.Add($"{h.Id.Handle}~{run.Id.Handle}");
                        double tRun = run.FootprintThickness;
                        double d = HpEndAtHpGeometry.BulbHeight(b, tRun, c, r);
                        string tail = $"{(fill > 0 ? $" nối dài={fill:0.#}" : "")}{(est ? " (c/r NỘI SUY)" : "")}";
                        if (run.Type == PartType.FlatBar)
                        {
                            double hFb = ctx.Input.FlatBarHeight, weld = hFb - HpEndAtHpGeometry.FB_WELD_CLEAR;
                            bool r30 = weld >= b;
                            ctx.Report.Add($"{LOG} HP-end-at-FB h={h.Id.Handle} m={run.Id.Handle} θ={Deg(best.CosTheta):0} " +
                                           $"hfb={hFb:0} weld={(r30 ? b - d - HpEndAtHpGeometry.R : weld):0.#} " +
                                           $"mode={(r30 ? "R30" : "CHAMFER")}{tail}");
                        }
                        else
                        {
                            double p = best.Inside ? HpEndAtHpGeometry.BulbProtrusion(c, r, tRun) : 0.0;
                            ctx.Report.Add($"{LOG} HP-end-at-HP h={h.Id.Handle} m={run.Id.Handle} " +
                                           $"{(best.Inside ? "INSIDE" : "OUTSIDE")} θ={Deg(best.CosTheta):0} p={p:0.#} d={d:0.##}{tail}");
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Debug.WriteLine($"{LOG} lỗi ({h.Id.Handle}~{run.Id.Handle}): {ex.Message}");
                        ctx.Warn($"{LOG} Lỗi cắt h={h.Id.Handle} m={run.Id.Handle}: {ex.Message} → HP giữ nguyên.");
                    }
                    finally { cutter?.Dispose(); }
                }
            }
            return done;
        }

        /// <summary>Đầu <paramref name="he"/> của <paramref name="hp"/> có chạm THÂN 1 HP khác theo quy cách này
        /// không (Match Ok) — <see cref="HpBuilder"/> dùng để KHÔNG vát Sniped End đầu đó (tránh vát + cắt R30
        /// chồng nhau khi dung sai Match &gt; FB_FREE_END_TOL, HP ≥ 140; review 2026-11-04).</summary>
        public static bool TouchesRunningHp(BuildContext ctx, PanelPart hp, Point3d he, Point3d other)
        {
            double b = ctx.Input.HpHeight;
            if (b <= 0) return false;
            var hps = Runs(ctx, report: false);
            if (hps.Count < 2) return false;
            HpProfileService.LookupCR(b, out double c, out double r, out _);
            return FindRun(ctx, hps, hp, he, other, c, r, out _, out _, out _, report: false) != null;
        }

        /// <summary>Ứng viên member CHẠY: mọi HP + mọi FlatBar (các đoạn sau FlatBarSplitter) — FB chỉ khi
        /// <c>FlatBarHeight &gt; 0</c> ("HP TO FB END" cần h_fb).</summary>
        private static List<PanelPart> Runs(BuildContext ctx, bool report)
        {
            var list = ctx.Cls.OfType(PartType.HP).ToList();
            var fbs = ctx.Cls.OfType(PartType.FlatBar).ToList();
            if (fbs.Count == 0) return list;
            if (ctx.Input.FlatBarHeight > 0) list.AddRange(fbs);
            else if (report) ctx.Report.Add($"{LOG} SKIP FB: FlatBarHeight=0 (spec FB trống) → không xét HP end tại FB.");
            return list;
        }

        /// <summary>Member chạy (HP hoặc FB) hợp lệ GẦN NHẤT cho đầu <paramref name="he"/> (null nếu không có). Ghi
        /// lại 1 ứng viên bị loại vì góc để Report.</summary>
        private static HpEndMatch FindRun(BuildContext ctx, List<PanelPart> hps, PanelPart h, Point3d he, Point3d ho,
                                          double c, double r, out PanelPart run,
                                          out HpEndMatch angleSkip, out PanelPart angleRun, bool report)
        {
            run = null; angleSkip = null; angleRun = null;
            HpEndMatch best = null;
            foreach (var m in hps)
            {
                if (ReferenceEquals(m, h)) continue;
                var (ra, rb) = ClassifyUtil.Endpoints(m);
                var bd = m.BellyDir;
                var res = HpEndAtHpGeometry.Match((he.X, he.Y), (ho.X, ho.Y), (ra.X, ra.Y), (rb.X, rb.Y),
                                                  m.FootprintThickness, (bd.X, bd.Y), c, r, tEnd: h.FootprintThickness,
                                                  runIsFb: m.Type == PartType.FlatBar);
                if (res.Ok) { if (best == null || res.Dist < best.Dist) { best = res; run = m; } }
                else if (res.Reject == "angle" && angleSkip == null) { angleSkip = res; angleRun = m; }
                else if (report && res.Reject == "noBelly" &&
                         DistToRun(he, m) <= m.FootprintThickness / 2.0 + HpEndAtHpGeometry.TOL_TOUCH)
                    ctx.Report.Add($"{LOG} SKIP noBelly h={h.Id.Handle} m={m.Id.Handle} — HP chạy chưa có hướng bụng.");
            }
            return best;
        }

        /// <summary>Khối nối dài: hộp (dài <paramref name="len"/> × dày web <paramref name="tEnd"/> × cao b) tâm trên
        /// đường tâm HP đầu, từ đầu mút <paramref name="he"/> hướng về HP chạy (ngược <paramref name="ho"/>), Z ∈ [−b, 0].</summary>
        private static Solid3d BuildFill(Point3d he, Point3d ho, double tEnd, double b, double len)
        {
            var dir = new Vector3d(he.X - ho.X, he.Y - ho.Y, 0);
            if (dir.Length < 1e-9 || tEnd <= 1e-6) return null;
            dir = dir.GetNormal();
            Vector3d side = Vector3d.ZAxis.CrossProduct(dir);                 // hệ (dir, side, Z) thuận tay
            var box = new Solid3d();
            box.CreateBox(len, tEnd, b);                                       // tâm tại gốc
            Point3d ctr = new Point3d(he.X, he.Y, 0) + dir * (len / 2.0) - Vector3d.ZAxis * (b / 2.0);
            box.TransformBy(Matrix3d.AlignCoordinateSystem(
                Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                ctr, dir, side, Vector3d.ZAxis));
            return box;
        }

        private static Solid3d BuildCutter(BuildContext ctx, PanelPart h, PanelPart run, HpEndMatch mt,
                                           double b, double c, double r)
        {
            double tRun = run.FootprintThickness, tEnd = h.FootprintThickness;
            if (tRun <= 1e-6 || tEnd <= 1e-6) return null;

            var section = run.Type == PartType.FlatBar
                ? ctx.HpProfile.HpEndAtFbCutter(b, ctx.Input.FlatBarHeight, tRun, c, r)   // "HP TO FB END"
                : ctx.HpProfile.HpEndAtHpCutter(b, tRun, c, r, mt.Inside);
            if (section == null) return null;
            double xMax = section.Max(v => Math.Abs(v.Pt.X));                 // vát FB vươn ~100mm ⇒ span theo dao thật
            Region reg = GeometryUtil.RegionFrom(section);
            if (reg == null) return null;

            var n = new Vector3d(mt.Normal.X, mt.Normal.Y, 0).GetNormal();
            Vector3d zl = n.CrossProduct(Vector3d.ZAxis);                   // ±trục member chạy, hệ (n, Z, zl) thuận tay
            double half = HpEndAtHpGeometry.HalfSpanX(tEnd, c, r, xMax, mt.CosTheta);

            // Gốc = mặt tiếp xúc (đường tâm HP chạy + n·tRun/2) tại chân vuông góc đầu HP đầu, Z=0, lùi half dọc trục.
            Point3d o = new Point3d(mt.Foot.X, mt.Foot.Y, 0) + n * (tRun / 2.0) - zl * half;

            reg.TransformBy(Matrix3d.AlignCoordinateSystem(
                Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                o, n, Vector3d.ZAxis, zl));

            var cutter = new Solid3d();
            var sob = new SweepOptionsBuilder();
            try { cutter.CreateExtrudedSolid(reg, zl * (2.0 * half), sob.ToSweepOptions()); }
            catch { reg.Dispose(); cutter.Dispose(); return null; }
            reg.Dispose();
            return cutter;
        }

        private static double DistToRun(Point3d pt, PanelPart m) => ClassifyUtil.DistToSegment(pt, m);

        private static double Deg(double cosTheta) => Math.Acos(Math.Min(1.0, Math.Abs(cosTheta))) * 180.0 / Math.PI;
    }
}
