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
    /// Vát <b>1:4</b> tại mối nối 2 web thẳng hàng khác chiều dày (CAS.dwt; kỹ sư 2026-11-05): trừ khối nêm (mặt bằng
    /// = <see cref="WebThicknessChamferGeometry.Wedge"/>, cao suốt web) khỏi solid tấm DÀY trên mỗi mặt bị lệch bậc.
    /// Chạy SAU khi hợp nhất GE/GB vào web ⇒ part GE/GB dùng solid web nó đã gộp vào. Phạm vi: Web, Closed Plate,
    /// GirderEnd (GE/GB). Lỗi boolean ⇒ <see cref="BuildContext.Warn"/>, web giữ nguyên (ux-guardrails).
    /// Hình học thuần + test: <see cref="WebThicknessChamferGeometry"/>. Xem skill web-thickness-chamfer.
    /// </summary>
    internal static class WebChamferBuilder
    {
        private const string LOG = "[WebChamfer]";
        private const double MARGIN = 1.0;    // đệm khối nêm ra ngoài mặt tấm dày + qua mối nối (mm)
        private const double Z_PAD = 10.0;    // khối nêm cao hơn web mỗi phía (mm)

        /// <summary>Trừ vát vào các solid trong <paramref name="webs"/> (mỗi phần tử = part 2D + solid CUỐI chứa nó).
        /// Trả tập solid đã bị cắt (để caller cập nhật bbox/thể tích).</summary>
        public static HashSet<Solid3d> Apply(BuildContext ctx, List<(PanelPart part, Solid3d sol)> webs)
        {
            var changed = new HashSet<Solid3d>();
            if (webs == null || webs.Count < 2) return changed;
            double H = ctx.Input.WebHeight;
            if (H <= 0) return changed;

            var segs = webs.Select(w =>
            {
                var (a, b) = ClassifyUtil.Endpoints(w.part);
                return new WebSeg { Ax = a.X, Ay = a.Y, Bx = b.X, By = b.Y, T = w.part.FootprintThickness };
            }).ToList();

            var done = new HashSet<string>();
            int n = 0;
            for (int i = 0; i < webs.Count; i++)
                for (int k = i + 1; k < webs.Count; k++)
                {
                    if (ReferenceEquals(webs[i].sol, webs[k].sol)) continue;   // GE ↔ chính web của nó
                    var j = WebThicknessChamferGeometry.Match(segs[i], segs[k]);
                    if (j == null) continue;

                    var thick = j.Thick == 0 ? webs[i] : webs[k];
                    var thin = j.Thick == 0 ? webs[k] : webs[i];
                    string key = $"{thick.part.SourceKey}@{Math.Round(j.Jx)},{Math.Round(j.Jy)}";
                    if (!done.Add(key)) continue;

                    string tag = $"{thick.part.Type} {thick.part.Id.Handle} t{thick.part.FootprintThickness:0.#} ↔ " +
                                 $"{thin.part.Type} {thin.part.Id.Handle} t{thin.part.FootprintThickness:0.#}: " +
                                 $"Δt={j.Dt:0.#} L={j.L:0.#}";
                    var sides = new List<string>();
                    foreach (int side in new[] { +1, -1 })
                    {
                        var poly = WebThicknessChamferGeometry.Wedge(j, side, MARGIN);
                        if (poly.Count == 0) continue;
                        double step = side > 0 ? j.StepPlus : j.StepMinus;
                        try
                        {
                            if (Cut(thick.sol, poly, -H - Z_PAD, H + 2 * Z_PAD))
                            {
                                changed.Add(thick.sol);
                                sides.Add($"{(side > 0 ? "+" : "−")}N step={step:0.#}");
                            }
                            else ctx.Warn($"{LOG} Không tạo được khối vát: {tag} ({(side > 0 ? "+" : "−")}N) → giữ nguyên.");
                        }
                        catch (System.Exception ex)
                        {
                            Debug.WriteLine($"{LOG} lỗi ({tag}): {ex.Message}");
                            ctx.Warn($"{LOG} Lỗi vát {tag}: {ex.Message} → giữ nguyên.");
                        }
                    }
                    if (sides.Count > 0) { n++; ctx.Report.Add($"{LOG} {tag} · {string.Join(", ", sides)}."); }
                }
            ctx.Report.Add($"{LOG} {n} mối nối web khác chiều dày được vát 1:4.");
            return changed;
        }

        /// <summary>Đùn đa giác mặt bằng từ <paramref name="z0"/> lên <paramref name="height"/> rồi BoolSubtract khỏi
        /// <paramref name="target"/>. false nếu không tạo được region/solid.</summary>
        private static bool Cut(Solid3d target, List<(double X, double Y)> poly, double z0, double height)
        {
            using (var pl = new Polyline())
            {
                for (int i = 0; i < poly.Count; i++) pl.AddVertexAt(i, new Point2d(poly[i].X, poly[i].Y), 0, 0, 0);
                pl.Closed = true;
                pl.Elevation = z0;

                DBObjectCollection regs = Region.CreateFromCurves(new DBObjectCollection { pl });
                if (regs.Count == 0) return false;
                var reg = (Region)regs[0];
                for (int i = 1; i < regs.Count; i++) regs[i].Dispose();

                var cutter = new Solid3d();
                try
                {
                    cutter.CreateExtrudedSolid(reg, new Vector3d(0, 0, height), new SweepOptionsBuilder().ToSweepOptions());
                }
                catch { cutter.Dispose(); reg.Dispose(); throw; }
                reg.Dispose();
                target.BooleanOperation(BooleanOperationType.BoolSubtract, cutter);   // cutter bị tiêu thụ
                cutter.Dispose();
                return true;
            }
        }
    }
}
