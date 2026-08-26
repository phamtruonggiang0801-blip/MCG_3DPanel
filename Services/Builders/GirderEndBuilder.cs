using System;
using System.Collections.Generic;
using System.Diagnostics;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCG_3DPanel.Models;

namespace MCG_3DPanel.Services.Builders
{
    /// <summary>
    /// Dựng GIRDER END (GE): outline web (parametric, <see cref="IGeProfileService"/>) → extrude chiều dày →
    /// (nếu D≥ngưỡng) hàn **Flange 50×10** dọc free-edge → đặt vào mặt đứng (tái dụng <see cref="BracketBuilder"/>).
    ///
    /// L = chiều dài mảnh (web→lưng HP/FB); H = Web height; Lb = overhang−15 (kẹp ≤ L−R30);
    /// Dend = member − 15 (lưng) / − 30 (bụng); D = √((L−Lb)²+(H−Dend)²) ⇒ flange nếu ≥ 350(thk6)/600(thk10).
    /// Overhang &gt; L−R30 ("Flange đỡ dài hơn member") ⇒ squareCorner: bỏ R30, hạ thẳng đứng góc vuông
    /// 90° từ R50 xuống đáy thay vì "ôm" theo Lb (xem <see cref="GeProfileService.Solve"/>).
    /// Xem skill girder-end (đã chốt số bằng DIM + Attribute GE.dwg).
    ///
    /// ⚠️ v1 flange: bar 50×10 dọc free-edge (chưa snip 15@30). Dend bụng (GE3/4) chờ mẫu.
    /// </summary>
    public class GirderEndBuilder : IPartBuilder
    {
        private const string LOG = "[GirderEndBuilder]";
        private const double FlangeW = 50.0, FlangeT = 10.0;   // flange 50 (rộng, z) × 10 (dày, ⊥ free-edge)

        public string Name => "GirderEnd";

        public bool CanBuild(PanelPart part) => part.Type == PartType.GirderEnd;

        public Solid3d Build(BuildContext ctx, PanelPart part)
        {
            try
            {
                bool touchHp = (part.Code ?? "").Contains("HP");
                double L = part.MemberLength;
                double H = ctx.Input.WebHeight;
                double thk = part.FootprintThickness;
                if (L < 1e-6 || H < 1e-6 || thk < 1e-6) return null;

                double member = touchHp ? ctx.Input.HpHeight : ctx.Input.FlatBarHeight;
                double dend = member - 15.0;                          // lưng −15 (bụng −30: GE3/4, chờ)

                double rawLb = BracketBuilder.OverhangProj(part, ctx.Cls) - 15.0;
                // Flange/FacePlate đỡ vươn xa hơn cả member (overhang chạm/vượt trần L−30, "Flange dài
                // hơn"): không còn ý nghĩa "ôm" R30 theo Lb nữa — dựng góc vuông 90° thẳng đứng thay vì
                // R30 (xem GeProfileService.Solve, nhánh squareCorner). Bỏ luôn khả năng flanged (bar
                // 50×10) vì free-edge lúc này chỉ còn đoạn thẳng đứng ngắn, không phải cạnh chéo dài.
                bool squareCorner = rawLb > L - 30.0;
                double lb = Math.Min(rawLb, L - 30.0);
                if (lb < 15.0) lb = 15.0;

                double d = squareCorner ? 0.0 : Math.Sqrt((L - lb) * (L - lb) + (H - dend) * (H - dend));
                bool flanged = !squareCorner && d >= (thk >= 8.0 ? 600.0 : 350.0);

                var outline = ctx.GeProfile.GeOutline(flanged, L, H, lb, dend, squareCorner);
                Region reg = GeometryUtil.RegionFrom(outline);
                if (reg == null)
                {
                    // Ghi tham số TRƯỚC KHI bỏ — nếu không, thất bại sớm này hoàn toàn không để lại
                    // dấu vết trong report (khác exception, có buildError riêng ở catch bên dưới).
                    ctx.Report.Add(
                        $"GE code={part.Code} {(touchHp ? "HP" : "FB")} L={L:0} H={H:0} thk={thk:0} " +
                        $"Dend={dend:0} Lb={lb:0} D={d:0} flanged={flanged} squareCorner={squareCorner} " +
                        $"center=({part.Centroid.X:0},{part.Centroid.Y:0}) buildError: RegionFrom(outline)=null " +
                        $"(outline={outline.Count} đỉnh, có thể tự cắt/suy biến với tham số này)");
                    return null;
                }

                var sol = new Solid3d();
                var sob = new SweepOptionsBuilder();
                sol.CreateExtrudedSolid(reg, new Vector3d(0, 0, thk), sob.ToSweepOptions());
                reg.Dispose();

                string flangeInfo = "no";
                if (flanged)
                {
                    ctx.GeProfile.FreeEdge(true, L, H, lb, dend, out Point2d top, out Point2d bot);
                    flangeInfo = AddFlange(sol, outline, top, bot, thk);
                }

                sol.TransformBy(Matrix3d.Displacement(new Vector3d(0, 0, -thk / 2.0)));  // căn giữa dày
                sol.TransformBy(BracketBuilder.BracketPlacement(part, ctx.Cls));           // đặt mặt đứng

                ctx.Report.Add(
                    $"GE code={part.Code} {(touchHp ? "HP" : "FB")} L={L:0} H={H:0} thk={thk:0} " +
                    $"Dend={dend:0} Lb={lb:0} D={d:0} flanged={flanged} squareCorner={squareCorner} flange={flangeInfo} " +
                    $"center=({part.Centroid.X:0},{part.Centroid.Y:0}) lumps={GeometryUtil.CountLumps(sol)}");
                return sol;
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine($"{LOG} lỗi ({part.Id.Handle}): {ex.Message}");
                ctx.Report.Add($"GE code={part.Code} buildError: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Hàn Flange 50×10 dọc free-edge (top→bot): bar 50 rộng (dọc z, căn giữa web) × 10 dày (ra ngoài thân).
        /// v1: bar thẳng (chưa snip). Union vào web.
        /// </summary>
        private static string AddFlange(Solid3d web, List<VertexBulge> outline, Point2d top, Point2d bot, double thk)
        {
            Vector2d fdir = bot - top; double len = fdir.Length;
            if (len < 1e-6) return "skip(len0)";
            fdir = fdir / len;
            var fnorm = new Vector2d(-fdir.Y, fdir.X);         // pháp tuyến free-edge

            // fnorm hướng RA NGOÀI thân (xa tâm outline).
            double cx = 0, cy = 0; foreach (var v in outline) { cx += v.Pt.X; cy += v.Pt.Y; }
            cx /= outline.Count; cy /= outline.Count;
            double mx = (top.X + bot.X) / 2 - cx, my = (top.Y + bot.Y) / 2 - cy;
            if (fnorm.X * mx + fnorm.Y * my < 0) fnorm = fnorm.Negate();

            // Footprint bar trong x-y: dải rộng FlangeT dọc free-edge, từ free-edge ra ngoài fnorm.
            Point2d a = top, b = bot, cc = bot + fnorm * FlangeT, dd = top + fnorm * FlangeT;
            var pl = new Polyline();
            pl.AddVertexAt(0, a, 0, 0, 0); pl.AddVertexAt(1, b, 0, 0, 0);
            pl.AddVertexAt(2, cc, 0, 0, 0); pl.AddVertexAt(3, dd, 0, 0, 0);
            pl.Closed = true;

            DBObjectCollection regs;
            try { regs = Region.CreateFromCurves(new DBObjectCollection { pl }); }
            catch { pl.Dispose(); return "regionFail"; }
            pl.Dispose();
            if (regs.Count == 0) return "regionEmpty";
            var reg = (Region)regs[0];
            for (int i = 1; i < regs.Count; i++) ((Region)regs[i]).Dispose();

            var flange = new Solid3d();
            var sob = new SweepOptionsBuilder();
            try { flange.CreateExtrudedSolid(reg, new Vector3d(0, 0, FlangeW), sob.ToSweepOptions()); }
            catch { reg.Dispose(); return "extrudeFail"; }
            reg.Dispose();
            // Căn giữa flange theo z quanh trục giữa web (z=thk/2): z ∈ [thk/2−25, thk/2+25].
            flange.TransformBy(Matrix3d.Displacement(new Vector3d(0, 0, thk / 2.0 - FlangeW / 2.0)));

            bool threw = false;
            try { web.BooleanOperation(BooleanOperationType.BoolUnite, flange); }
            catch { threw = true; }
            finally { flange.Dispose(); }
            return $"50x10(len={len:0} unionThrew={threw})";
        }

        // ─── Suy tham số + đặt (giữ nguyên, tái dụng BracketBuilder) ───────
    }
}
