using System;
using System.Collections.Generic;
using System.Diagnostics;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCG_3DPanel.Models;
using MCG_3DPanel.Services.Classify;

namespace MCG_3DPanel.Services.Builders
{
    /// <summary>
    /// Dựng HP (bulb flat) bằng TIẾT DIỆN THẬT: lấy tiết diện từ <see cref="IHpProfileService"/>
    /// rồi extrude dọc trục HP trong plan.
    ///
    /// Đặt tiết diện (local tiết diện: gốc = góc LƯNG–ĐẦU, +X → BỤNG, −Y xuống theo b):
    ///   local X → <c>belly</c> (hướng bulb, lấy từ <see cref="PanelPart.BellyDir"/> do ClassificationService gán)
    ///   local Y → +Z thế giới  (tiết diện có y ≤ 0 nên thân HP thõng xuống dưới Z0 = mặt dưới Top Plate)
    ///   local Z → <c>axis</c>  (trục dài HP) → hướng extrude
    /// Gốc đặt tại: tâm HP − ½·len·axis − ½·t·belly, ở Z = 0.
    ///
    /// Nếu thiếu dữ liệu (không có BellyDir / spec sai) → trả null để Build3DService fallback
    /// sang <see cref="PlateBuilder"/> (hộp chữ nhật) — không làm vỡ bản dựng.
    /// Xem skill hp-profile.
    /// </summary>
    public class HpBuilder : IPartBuilder
    {
        private const string LOG = "[HpBuilder]";

        // Sniped End cho HP — CÙNG quy tắc + CÙNG FLAT_TOP = 15mm với FlatBar (xác nhận kỹ sư 2026-08-27,
        // sửa từ 30mm). Vát góc DƯỚI-NGOÀI ở đầu HP THỰC SỰ TỰ DO: từ cạnh hàn (Z=0) giữ thẳng đứng đúng
        // 15mm, rồi vát chéo 30°/60° (cố định) xuống mép đáy bulb (Z=−b).
        private const double SNIP_FLAT = 15.0;
        private static readonly double SNIP_RUN_FACTOR = Math.Tan(60.0 * Math.PI / 180.0);
        private const double SNIP_MARGIN = 2.0;

        public string Name => "HP";

        public bool CanBuild(PanelPart part) => part.Type == PartType.HP;

        public Solid3d Build(BuildContext ctx, PanelPart part)
        {
            Region reg = null;
            try
            {
                double b = ctx.Input.HpHeight;                 // chiều cao HP (từ spec "120*6")
                double t = part.FootprintThickness;            // dày web: lấy theo hình học THẬT trong plan
                double len = part.MemberLength;
                if (b <= 0 || t <= 0 || len <= 0) return null;

                // Trục dài THẬT (đúng cả HP CHÉO — bbox/IsHorizontal không suy ra được).
                Vector3d axis = part.AxisDir;
                if (axis.Length < 1e-9) axis = part.IsHorizontal ? Vector3d.XAxis : Vector3d.YAxis;
                axis = new Vector3d(axis.X, axis.Y, 0).GetNormal();

                Vector3d belly = part.BellyDir;
                if (belly.Length < 1e-9) return null;           // chưa xác định hướng bụng → fallback
                belly = new Vector3d(belly.X, belly.Y, 0);
                // Ép belly VUÔNG GÓC axis: AlignCoordinateSystem đòi hệ trực giao, lệch nhỏ cũng ném lỗi
                // → trước đây HP chéo rơi hết về fallback hộp chữ nhật.
                belly = belly - axis * belly.DotProduct(axis);
                if (belly.Length < 1e-9) return null;
                belly = belly.GetNormal();

                HpProfileService.LookupCR(b, out double c, out double r, out bool estimated);
                var section = ctx.HpProfile.HpSection(b, t, c, r);
                reg = GeometryUtil.RegionFrom(section);
                if (reg == null) return null;

                // Gốc tiết diện = mép LƯNG, đầu trục, tại Z0.
                Point3d o = part.Centroid - axis * (len / 2.0) - belly * (t / 2.0);
                o = new Point3d(o.X, o.Y, 0);

                reg.TransformBy(Matrix3d.AlignCoordinateSystem(
                    Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                    o, belly, Vector3d.ZAxis, axis));

                var sol = new Solid3d();
                var sob = new SweepOptionsBuilder();
                sol.CreateExtrudedSolid(reg, axis * len, sob.ToSweepOptions());

                // Sniped End: vát đầu HP THỰC SỰ TỰ DO (không chạm Web/Bracket/GE/HP/FB nào khác).
                var (e1, e2) = ClassifyUtil.Endpoints(part);           // e1 ở s=0 (đầu 'o'), e2 ở s=len
                bool free1 = GeometryUtil.IsFreeEnd(e1, part, ctx.Cls, ClassifyUtil.FB_FREE_END_TOL);
                bool free2 = GeometryUtil.IsFreeEnd(e2, part, ctx.Cls, ClassifyUtil.FB_FREE_END_TOL);
                int freeCount = (free1 ? 1 : 0) + (free2 ? 1 : 0);
                double run = Math.Max(0.0, b - SNIP_FLAT) * SNIP_RUN_FACTOR;
                if (freeCount > 0 && run * freeCount > len) run = len / 2.0;   // HP quá ngắn → kẹp an toàn
                if (free1) SnipHpEnd(sol, part.Centroid - axis * (len / 2.0),  axis, belly, b, run);
                if (free2) SnipHpEnd(sol, part.Centroid + axis * (len / 2.0), -axis, belly, b, run);

                ctx.Report.Add($"HP len={len:0} b={b:0} t={t:0} c={c:0.#} r={r:0.#}" +
                               $"{(estimated ? " (c/r NỘI SUY - ngoài bảng)" : "")} " +
                               $"free1={free1} free2={free2} snipRun={run:0.#} " +
                               $"belly=({belly.X:0.##},{belly.Y:0.##}) center=({part.Centroid.X:0},{part.Centroid.Y:0})");
                return sol;
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine($"{LOG} lỗi ({part.Id.Handle}): {ex.Message}");
                return null;
            }
            finally { reg?.Dispose(); }
        }

        /// <summary>
        /// Trừ (BoolSubtract) 1 nêm tam giác khỏi <paramref name="sol"/> ở đầu HP tự do — bỏ góc
        /// DƯỚI-NGOÀI. Hệ local: x = <paramref name="inward"/> (dọc trục, VÀO thân HP), y = Z thế giới
        /// (0 = cạnh hàn, −b = mép đáy bulb). Tam giác `(−M,−30) → (−M,−b−M) → (run,−b−M)`: cạnh đóng
        /// `(run,−b−M)→(−M,−30)` là đường vát 30° (khớp FlatBar, xem skill sniped-end). Quét theo
        /// <paramref name="belly"/> qua HẾT bề rộng tiết diện (kể cả bulb).
        /// </summary>
        private static void SnipHpEnd(Solid3d sol, Point3d baseC, Vector3d inward, Vector3d belly, double b, double run)
        {
            if (run < 1e-6 || b <= SNIP_FLAT) return;
            const double M = SNIP_MARGIN;
            var tri = new List<VertexBulge>
            {
                new VertexBulge(-M, -SNIP_FLAT, 0),
                new VertexBulge(-M, -b - M, 0),
                new VertexBulge(run, -b - M, 0),
            };
            Region reg = GeometryUtil.RegionFrom(tri);
            if (reg == null) return;

            double sweep = b + 2 * M;                              // ≥ bề rộng mọi tiết diện HP (b ≫ rộng)
            Point3d origin = baseC - belly * (b / 2.0 + M);        // lùi 1 nửa để quét đối xứng qua tiết diện
            reg.TransformBy(Matrix3d.AlignCoordinateSystem(
                Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                origin, inward, Vector3d.ZAxis, belly));

            var cutter = new Solid3d();
            var sob = new SweepOptionsBuilder();
            try { cutter.CreateExtrudedSolid(reg, belly * sweep, sob.ToSweepOptions()); }
            catch { reg.Dispose(); cutter.Dispose(); return; }
            reg.Dispose();

            try { sol.BooleanOperation(BooleanOperationType.BoolSubtract, cutter); }
            catch { cutter.Dispose(); }
        }
    }
}
