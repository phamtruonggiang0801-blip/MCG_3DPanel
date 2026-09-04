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
    /// "Cutout on Web cho HP" (chi tiết **CO B — Cut Out Bulb-profile**, CAS.dump.json) — chỗ HP xuyên
    /// NGANG qua 1 tấm Web thì web phải có LỖ để HP đi qua. Giống <see cref="CutOutBuilder"/>: KHÔNG tự
    /// dựng solid độc lập — dựng "dao cắt" hình tiết diện HP rồi TRỪ (BoolSubtract) thẳng vào solid Web
    /// đã dựng. Gọi tường minh từ <see cref="Build3DService"/> SAU vòng lặp dựng chính (cần solid Web sẵn).
    ///
    /// Quy cách (xác nhận kỹ sư 2026-08-28) — xem <see cref="HpProfileService.HpCutoutSection"/>:
    ///   • mép LƯNG lỗ trùng mặt LƯNG HP (gap = 0 phía lưng); phía BỤNG mở tới W = (c+r) + R;
    ///   • chân "foot" = offset đoạn đáy HP ra bán kính R (TABLE 1 — <see cref="HpProfileService.LookupCoRadius"/>);
    ///   • cắt XUYÊN SUỐT chiều dày web (extrude = webThk + 2·MARGIN);
    ///   • HP KẾT THÚC tại web (không đi xuyên) → GIỮ NGUYÊN web, không khoét.
    /// LUG (tấm collar, "LUG B1R" — DET.A vát mép hàn) NGOÀI phạm vi bản dựng này.
    /// Xem skill web-hp-cutout.
    /// </summary>
    internal static class WebHpCutoutBuilder
    {
        private const string LOG = "[WebHpCutoutBuilder]";

        // Margin extrude 2 đầu dọc trục HP — tránh mặt trùng khít (coplanar) khi trừ đúng khít chiều
        // dày web (cùng lý do MARGIN của CutOutBuilder).
        private const double MARGIN = 5.0;

        /// <summary>Trừ lỗ HP-xuyên-web ra khỏi từng solid Web trong <paramref name="webSolids"/>
        /// (sửa TRỰC TIẾP). Lỗi ở 1 cặp (Web,HP) không làm hỏng cặp khác — ghi Report, web coi như chưa
        /// khoét. Trả danh sách khoá <c>"webHandle~hpHandle"</c> đã trừ THÀNH CÔNG.</summary>
        public static List<string> ApplyWebCutouts(BuildContext ctx, IReadOnlyList<(PanelPart web, Solid3d sol)> webSolids)
        {
            var done = new List<string>();
            if (webSolids == null || webSolids.Count == 0) return done;

            var hps = ctx.Cls.OfType(PartType.HP).ToList();
            if (hps.Count == 0) return done;

            double b = ctx.Input.HpHeight;
            if (b <= 0)
            {
                ctx.Report.Add($"{LOG} HpHeight ({b:0.#}) không hợp lệ → bỏ qua toàn bộ lỗ khoét web.");
                return done;
            }
            HpProfileService.LookupCR(b, out double c, out double r, out bool est);
            HpProfileService.LookupCoRadius(b, out double coR);

            foreach (var (web, sol) in webSolids)
            {
                if (sol == null) continue;
                double webThk = web.FootprintThickness;
                if (webThk <= 1e-6) continue;
                // Web phân loại sai cho FootprintThickness hàng nghìn mm → dao cắt khổng lồ (span = webThk
                // + 2·MARGIN dọc trục HP). WebBracketSplitter (WEB_MAX_THK) lẽ ra đã chặn — lớp chắn cuối.
                if (webThk > ClassifyUtil.WEB_MAX_THK)
                {
                    ctx.Report.Add($"{LOG} Bỏ khoét Web {web.Id.Handle}: bề dày {webThk:0.#}mm > " +
                        $"{ClassifyUtil.WEB_MAX_THK:0} — nghi phân loại sai, web giữ nguyên.");
                    continue;
                }

                foreach (var hp in hps)
                {
                    if (!ClassifyUtil.CrossesThrough(hp, web, out double tCross)) continue;   // HP không xuyên NGANG web này

                    var (e1, e2) = ClassifyUtil.Endpoints(hp);
                    double L = e1.DistanceTo(e2);
                    if (L < 1e-6) continue;

                    // HP KẾT THÚC tại web (đầu mút quá gần chỗ giao) → GIỮ NGUYÊN web (xác nhận kỹ sư 2026-08-28).
                    if (tCross < b * 0.5 || tCross > L - b * 0.5) continue;

                    Solid3d cutter = null;
                    try
                    {
                        cutter = BuildCutter(ctx, hp, e1, e2, tCross, b, c, r, coR, webThk);
                        if (cutter == null)
                        {
                            ctx.Report.Add($"{LOG} Không dựng được dao cắt HP {hp.Id.Handle} × Web {web.Id.Handle} " +
                                           $"(thiếu BellyDir / tiết diện lỗi) → web giữ nguyên.");
                            continue;
                        }
                        sol.BooleanOperation(BooleanOperationType.BoolSubtract, cutter);
                        cutter = null;   // BoolSubtract đã tiêu thụ cutter
                        done.Add($"{web.Id.Handle}~{hp.Id.Handle}");
                        ctx.Report.Add($"{LOG} Khoét Web {web.Id.Handle} cho HP {hp.Id.Handle}: " +
                                       $"b={b:0} t={hp.FootprintThickness:0.#} R={coR:0} webThk={webThk:0.#}" +
                                       $"{(est ? " (c/r NỘI SUY)" : "")}.");
                    }
                    catch (System.Exception ex)
                    {
                        Debug.WriteLine($"{LOG} lỗi ({web.Id.Handle}×{hp.Id.Handle}): {ex.Message}");
                        ctx.Report.Add($"{LOG} Lỗi khoét Web {web.Id.Handle} × HP {hp.Id.Handle}: {ex.Message} → web giữ nguyên.");
                    }
                    finally
                    {
                        cutter?.Dispose();
                    }
                }
            }
            return done;
        }

        /// <summary>Dao cắt = tiết diện lỗ CO (ôm sát HP + bo góc đáy R) đặt tại chỗ HP giao Web, quét
        /// dọc trục HP xuyên suốt chiều dày web. CÙNG hệ trục local với <see cref="HpBuilder"/>
        /// (local X → belly, local Y → +Z thế giới, local Z → trục HP) để lỗ khớp đúng HP solid.</summary>
        private static Solid3d BuildCutter(BuildContext ctx, PanelPart hp, Point3d e1, Point3d e2,
            double tCross, double b, double c, double r, double coR, double webThk)
        {
            double t = hp.FootprintThickness;
            if (t <= 1e-6) return null;

            Vector3d axis = hp.AxisDir;
            if (axis.Length < 1e-9) axis = e2 - e1;
            axis = new Vector3d(axis.X, axis.Y, 0);
            if (axis.Length < 1e-9) return null;
            axis = axis.GetNormal();

            Vector3d belly = hp.BellyDir;
            if (belly.Length < 1e-9) return null;                          // chưa xác định bụng → bỏ (web giữ nguyên)
            belly = new Vector3d(belly.X, belly.Y, 0);
            belly = belly - axis * belly.DotProduct(axis);                 // ép ⟂ axis (AlignCoordinateSystem đòi trực giao)
            if (belly.Length < 1e-9) return null;
            belly = belly.GetNormal();

            var section = ctx.HpProfile.HpCutoutSection(b, t, c, r, coR);
            Region reg = GeometryUtil.RegionFrom(section);
            if (reg == null) return null;

            // Điểm giao 2 đường tâm (plan) — chiếu từ e1 dọc trục HP đúng chiều Endpoints.
            Vector3d axisE = (e2 - e1).GetNormal();
            Point3d cross = e1 + axisE * tCross;

            double span = webThk + 2.0 * MARGIN;
            // Gốc tiết diện = mép LƯNG HP (local x=0) tại chỗ giao, Z=0, lùi ½·span dọc trục để quét đối xứng qua web.
            Point3d o = new Point3d(cross.X, cross.Y, 0.0) - belly * (t / 2.0) - axis * (span / 2.0);

            reg.TransformBy(Matrix3d.AlignCoordinateSystem(
                Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                o, belly, Vector3d.ZAxis, axis));

            var cutter = new Solid3d();
            var sob = new SweepOptionsBuilder();
            try { cutter.CreateExtrudedSolid(reg, axis * span, sob.ToSweepOptions()); }
            catch { reg.Dispose(); cutter.Dispose(); return null; }
            reg.Dispose();
            return cutter;
        }
    }
}
