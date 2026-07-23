using System;
using System.Diagnostics;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCG_3DPanel.Models;

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

                ctx.Report.Add($"HP len={len:0} b={b:0} t={t:0} c={c:0.#} r={r:0.#}" +
                               $"{(estimated ? " (c/r NỘI SUY - ngoài bảng)" : "")} " +
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
    }
}
