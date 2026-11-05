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
    /// (nếu D≥ngưỡng) **Flange 50×10** dọc free-edge — SOLID ĐỘC LẬP (ctx.Extras), Layer/Color theo Flange girder
    /// gốc, không union vào web → đặt vào mặt đứng (tái dụng <see cref="BracketBuilder"/>).
    ///
    /// L = chiều dài mảnh (web→lưng HP/FB); H = Web height; Lb = overhang−15 (kẹp ≤ L−R30);
    /// Dend = member − 15 (lưng) / − 30 (bụng); D = √((L−Lb)²+(H−Dend)²) ⇒ flange nếu ≥ 350(thk6)/600(thk10).
    /// CÓ flange ⇒ hình học dùng Lb = overhang (chân SÁT mép flange girder, toe sắc) — D vẫn theo Lb−15.
    /// Overhang &gt; L−R30 ("Flange đỡ dài hơn member") ⇒ squareCorner: bỏ R30, hạ thẳng đứng góc vuông
    /// 90° từ R50 xuống đáy thay vì "ôm" theo Lb (xem <see cref="GeProfileService.Solve"/>).
    /// Xem skill girder-end (đã chốt số bằng DIM + Attribute GE.dwg).
    ///
    /// Flange 50×10: snip 30–20–30 đầu cao, cắt vuông tại toe. Dend bụng (GE3/4) chờ mẫu.
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
                if ((part.Code ?? "").StartsWith("GB")) return BuildGb(ctx, part, touchHp);
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

                // D LUÔN tính với Lb = overhang−15 (ngưỡng đã kiểm chứng GE2/GE6 trong GE.dwg).
                double d = squareCorner ? 0.0 : Math.Sqrt((L - lb) * (L - lb) + (H - dend) * (H - dend));
                // Ngưỡng D có flange: thk6→350, thk10→600, thk20→1500 (kỹ sư xác nhận 2026-11-03).
                bool flanged = !squareCorner && d >= (thk >= 15.0 ? 1500.0 : thk >= 8.0 ? 600.0 : 350.0);

                // Đã CÓ flange → chân KHÔNG thụt 15 nữa mà đẩy SÁT mép flange girder (Lb = overhang),
                // toe sắc; flange 50×10 bám free-edge từ đúng chân này lên (kỹ sư xác nhận 2026-11-04).
                // Chỉ đổi hình học — quyết định flanged ở trên vẫn theo Lb−15.
                if (flanged) lb = Math.Min(rawLb + 15.0, L - 30.0);

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
                Solid3d flange = null;
                if (flanged)
                {
                    ctx.GeProfile.FreeEdge(true, L, H, lb, dend, out Point2d top, out Point2d bot);
                    flangeInfo = MakeFlangeSnip(outline, top, bot, thk, FlangeW, GeSnipTransverse, GeSnipAngleDeg, out flange);
                }

                sol.TransformBy(Matrix3d.Displacement(new Vector3d(0, 0, -thk / 2.0)));  // căn giữa dày
                sol.TransformBy(BracketBuilder.BracketPlacement(part, ctx.Cls));           // đặt mặt đứng
                if (flange != null) flangeInfo += " " + Detach(ctx, part, flange, thk);  // solid độc lập

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
        /// GIRDER BRACKET (GB): Dend = member − 30 (chừa chỗ hàn Flange lên bụng), R30 đầu, free-edge thẳng,
        /// toe SẮC tại (Lb=GB_TOE_PAD, −H) (vị trí theo HP, bỏ bo R50 — 2026-11-04), Flange 80×10 LUÔN có
        /// (snip 60°–20–60° ở đầu cao, cắt vuông tại toe). Xem skill girder-bracket.
        /// </summary>
        private Solid3d BuildGb(BuildContext ctx, PanelPart part, bool touchHp)
        {
            try
            {
                double L = part.MemberLength, H = ctx.Input.WebHeight, thk = part.FootprintThickness;
                if (L < 1e-6 || H < 1e-6 || thk < 1e-6) return null;
                double member = touchHp ? ctx.Input.HpHeight : ctx.Input.FlatBarHeight;
                double dend = member - 30.0;
                double lb = ClassifyUtil_GbPad;

                var outline = ctx.GeProfile.GbOutline(L, H, lb, dend, out Point2d top, out Point2d bot);
                Region reg = GeometryUtil.RegionFrom(outline);
                if (reg == null)
                {
                    ctx.Report.Add($"GB code={part.Code} L={L:0} H={H:0} thk={thk:0} Dend={dend:0} Lb={lb:0} " +
                        $"center=({part.Centroid.X:0},{part.Centroid.Y:0}) buildError: RegionFrom(outline)=null ({outline.Count} đỉnh)");
                    return null;
                }
                var sol = new Solid3d();
                sol.CreateExtrudedSolid(reg, new Vector3d(0, 0, thk), new SweepOptionsBuilder().ToSweepOptions());
                reg.Dispose();

                string flangeInfo = MakeFlangeSnip(outline, top, bot, thk, GbFlangeW, GbSnipTransverse, GbSnipAngleDeg, out Solid3d flange);

                sol.TransformBy(Matrix3d.Displacement(new Vector3d(0, 0, -thk / 2.0)));
                sol.TransformBy(BracketBuilder.BracketPlacement(part, ctx.Cls));
                if (flange != null) flangeInfo += " " + Detach(ctx, part, flange, thk);  // solid độc lập
                ctx.Report.Add($"GB code={part.Code} {(touchHp ? "HP" : "FB")} L={L:0} H={H:0} thk={thk:0} Dend={dend:0} " +
                    $"Lb={lb:0} flange={flangeInfo} center=({part.Centroid.X:0},{part.Centroid.Y:0}) lumps={GeometryUtil.CountLumps(sol)}");
                return sol;
            }
            catch (System.Exception ex)
            {
                ctx.Report.Add($"GB code={part.Code} buildError: {ex.Message}");
                return null;
            }
        }

        private const double GbFlangeW = 80.0, GbSnipTransverse = 30.0, GbSnipAngleDeg = 60.0;
        // GE: snip 15 (đo ngang) @30° mỗi bên → cạnh vát 15/sin30 = 30, phẳng 20 (30–20–30). Skill girder-end §4.
        private const double GeSnipTransverse = 15.0, GeSnipAngleDeg = 30.0;
        private static readonly double ClassifyUtil_GbPad = Services.Classify.ClassifyUtil.GB_TOE_PAD;

        /// <summary>
        /// Flange dọc free-edge (top→bot), rộng <paramref name="width"/> × dày 10 ra NGOÀI thân: footprint trong mặt
        /// (s dọc free-edge, z ngang) có SNIP đầu CAO (mỗi bên cắt <paramref name="transverse"/> ngang, góc
        /// <paramref name="angleDeg"/> so với trục flange); đầu THẤP dừng đúng tại toe và CẮT VUÔNG.
        /// KHÔNG union vào web — trả <paramref name="result"/> (hệ local web) để dựng thành solid ĐỘC LẬP.
        /// GB: 80, 30, 60° (60°–20–60°). GE: 50, 15, 30° (30–20–30).
        /// </summary>
        private static string MakeFlangeSnip(List<VertexBulge> outline, Point2d top, Point2d bot, double thk,
                                             double width, double transverse, double angleDeg, out Solid3d result)
        {
            result = null;
            Vector2d fdir = bot - top; double len = fdir.Length;
            if (len < 1e-6) return "skip(len0)";
            fdir = fdir / len;
            var fnorm = new Vector2d(-fdir.Y, fdir.X);
            double cx = 0, cy = 0; foreach (var v in outline) { cx += v.Pt.X; cy += v.Pt.Y; }
            cx /= outline.Count; cy /= outline.Count;
            double mx = (top.X + bot.X) / 2 - cx, my = (top.Y + bot.Y) / 2 - cy;
            if (fnorm.X * mx + fnorm.Y * my < 0) fnorm = fnorm.Negate();

            double half = width / 2.0;
            double flat = half - transverse;                                    // GB 40−30 / GE 25−15 = 10 → phẳng 20
            double a = transverse / Math.Tan(angleDeg * Math.PI / 180.0);       // dọc trục của cạnh vát
            var pl = new Polyline();
            var pts = new[] { new Point2d(0, -flat), new Point2d(0, flat), new Point2d(a, half),
                              new Point2d(len, half), new Point2d(len, -half), new Point2d(a, -half) };
            for (int i = 0; i < pts.Length; i++) pl.AddVertexAt(i, pts[i], 0, 0, 0);
            pl.Closed = true;

            DBObjectCollection regs;
            try { regs = Region.CreateFromCurves(new DBObjectCollection { pl }); }
            catch { pl.Dispose(); return "regionFail"; }
            pl.Dispose();
            if (regs.Count == 0) return "regionEmpty";
            var reg = (Region)regs[0];
            for (int i = 1; i < regs.Count; i++) ((Region)regs[i]).Dispose();

            // Hệ (s,z,n) → web local: s=fdir, z=Z, n=fdir×Z (phải trùng/ngược fnorm → đảo chiều extrude).
            var s3 = new Vector3d(fdir.X, fdir.Y, 0);
            var n3 = s3.CrossProduct(Vector3d.ZAxis);
            double sign = (n3.X * fnorm.X + n3.Y * fnorm.Y) >= 0 ? 1.0 : -1.0;

            var flange = new Solid3d();
            try { flange.CreateExtrudedSolid(reg, new Vector3d(0, 0, sign * FlangeT), new SweepOptionsBuilder().ToSweepOptions()); }
            catch { reg.Dispose(); flange.Dispose(); return "extrudeFail"; }
            reg.Dispose();
            flange.TransformBy(Matrix3d.AlignCoordinateSystem(
                Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                new Point3d(top.X, top.Y, thk / 2.0), s3, Vector3d.ZAxis, n3));

            result = flange;
            return $"{width:0}x10 snip{angleDeg:0}°/{transverse:0}(len={len:0} separate)";
        }

        /// <summary>
        /// Đặt flange (đang ở hệ local web) đúng chỗ bằng CÙNG transform với web rồi đưa vào
        /// <see cref="BuildContext.Extras"/> thành SOLID ĐỘC LẬP, Layer/Color theo Flange girder đỡ
        /// (<see cref="BracketBuilder.SupportPlate"/>; không tìm thấy → theo chính GE/GB).
        /// </summary>
        private static string Detach(BuildContext ctx, PanelPart part, Solid3d flange, double thk)
        {
            flange.TransformBy(Matrix3d.Displacement(new Vector3d(0, 0, -thk / 2.0)));
            flange.TransformBy(BracketBuilder.BracketPlacement(part, ctx.Cls));
            PanelPart sup = BracketBuilder.SupportPlate(part, ctx.Cls);
            flange.Layer = sup?.Layer ?? part.Layer;
            flange.ColorIndex = sup?.ColorIndex ?? part.ColorIndex;
            ctx.Extras.Add(new ExtraSolid { Solid = flange, Type = "GirderFlange", Code = part.Code ?? "" });
            return $"layer={flange.Layer} (từ {(sup != null ? sup.Type + " " + sup.Id.Handle : "GE/GB")})";
        }

        // ─── Suy tham số + đặt (giữ nguyên, tái dụng BracketBuilder) ───────
    }
}
