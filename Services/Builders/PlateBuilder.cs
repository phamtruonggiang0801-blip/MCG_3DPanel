using System;
using System.Diagnostics;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCG_3DPanel.Models;

namespace MCG_3DPanel.Services.Builders
{
    /// <summary>
    /// Dựng các bộ phận dạng TẤM/THANH prism: Top Plate, Web, Flange, Face Plate, Flat Bar.
    /// Cơ chế: lấy chính LWPolyline gốc → Region → extrude theo Z → dịch cao độ.
    /// Datum = mặt DƯỚI Top Plate = Z0. Xem skill panel-parts-reference (mục 3, 4).
    /// (HP tách riêng sang <see cref="HpBuilder"/> vì có tiết diện bulb; Bracket sang <see cref="BracketBuilder"/>.)
    /// </summary>
    public class PlateBuilder : IPartBuilder
    {
        private const string LOG = "[PlateBuilder]";

        public string Name => "Plate";

        public bool CanBuild(PanelPart part)
        {
            switch (part.Type)
            {
                case PartType.TopPlate:
                case PartType.Web:
                case PartType.Flange:
                case PartType.FacePlate:
                case PartType.FlatBar:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Khoảng extrude (Z) và dịch chuyển (Z) theo loại. false = bỏ qua.</summary>
        public static bool ZParams(PartType type, PanelInput input, out double extrude, out double moveZ)
        {
            extrude = 0; moveZ = 0;
            switch (type)
            {
                case PartType.TopPlate: extrude = +input.TopPlateThickness; return true;
                case PartType.Web: extrude = -input.WebHeight; return true;
                case PartType.FlatBar: extrude = -input.FlatBarHeight; return true;
                case PartType.HP: extrude = -input.HpHeight; return true;   // fallback khi HpBuilder không dựng được
                case PartType.Flange: extrude = -input.FlangeThickness; moveZ = -input.WebHeight; return true;
                case PartType.FacePlate: extrude = -input.FlangeThickness; moveZ = -input.WebHeight; return true;
                case PartType.Bracket: extrude = -input.WebHeight; return true;
                default: return false;
            }
        }

        public Solid3d Build(BuildContext ctx, PanelPart part)
        {
            if (!ZParams(part.Type, ctx.Input, out double extrude, out double moveZ)) return null;
            if (Math.Abs(extrude) < 1e-6) return null;
            return Extrude(ctx.Tr, part, extrude, moveZ);
        }

        /// <summary>Clone polyline gốc về WCS → Region → extrude dz → dịch moveZ. null nếu lỗi.</summary>
        public static Solid3d Extrude(Transaction tr, PanelPart part, double extrude, double moveZ)
        {
            Entity clone = null;
            Region reg = null;
            try
            {
                var src = (Entity)tr.GetObject(part.Id, OpenMode.ForRead);
                clone = (Entity)src.Clone();
                clone.TransformBy(part.Transform); // local -> WCS

                DBObjectCollection regions;
                try { regions = Region.CreateFromCurves(new DBObjectCollection { clone }); }
                catch { return null; }
                if (regions.Count == 0) return null;

                reg = (Region)regions[0];
                for (int i = 1; i < regions.Count; i++) ((Region)regions[i]).Dispose();

                var sol = new Solid3d();
                var sob = new SweepOptionsBuilder();
                sol.CreateExtrudedSolid(reg, new Vector3d(0, 0, extrude), sob.ToSweepOptions());
                if (Math.Abs(moveZ) > 1e-6)
                    sol.TransformBy(Matrix3d.Displacement(new Vector3d(0, 0, moveZ)));
                return sol;
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine($"{LOG} lỗi ({part.Type} {part.Id.Handle}): {ex.Message}");
                return null;
            }
            finally
            {
                reg?.Dispose();
                clone?.Dispose();
            }
        }
    }
}
