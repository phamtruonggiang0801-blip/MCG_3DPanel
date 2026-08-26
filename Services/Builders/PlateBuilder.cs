using System;
using System.Diagnostics;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCG_3DPanel.Models;

namespace MCG_3DPanel.Services.Builders
{
    /// <summary>
    /// Dựng các bộ phận dạng TẤM/THANH prism: Top Plate, Web, Flange, Face Plate,
    /// Closed Plate, Backing Plate.
    /// Cơ chế: lấy chính LWPolyline gốc → Region → extrude theo Z → dịch cao độ.
    /// Datum = mặt DƯỚI Top Plate = Z0. Xem skill panel-parts-reference (mục 3, 4, 4d).
    /// (HP tách riêng sang <see cref="HpBuilder"/> vì có tiết diện bulb; Bracket sang <see cref="BracketBuilder"/>;
    /// CutOut KHÔNG qua đây — trừ boolean thẳng vào solid Top Plate, xem <see cref="CutOutBuilder"/>.
    /// ⚠️ FlatBar KHÔNG qua đây nữa — <see cref="FlatBarBuilder"/> tự dựng TOÀN BỘ (kể cả hộp phẳng
    /// không vát), vì clone entity gốc theo <c>part.Id</c> ở đây SẼ SAI cho đoạn FB đã bị
    /// <see cref="Classify.FlatBarSplitter"/> tách — 1 Id gốc giờ có thể ứng với NHIỀU đoạn, và
    /// <see cref="Extrude"/> luôn clone NGUYÊN VẸN polyline gốc, bỏ qua phần đã cắt.)
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
                case PartType.ClosedPlate:
                case PartType.BackingPlate:
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
                case PartType.HP: extrude = -input.HpHeight; return true;   // fallback khi HpBuilder không dựng được
                // FlatBar KHÔNG có ở đây nữa — xem ghi chú trên CanBuild (FlatBarBuilder tự dựng toàn bộ).
                case PartType.Flange: extrude = -input.FlangeThickness; moveZ = -input.WebHeight; return true;
                case PartType.FacePlate: extrude = -input.FlangeThickness; moveZ = -input.WebHeight; return true;
                case PartType.Bracket: extrude = -input.WebHeight; return true;
                // Fallback khi GirderEndBuilder không dựng được profile thật (VD outline tự cắt) — hộp
                // chữ nhật đơn giản như Web, còn hơn biến mất hoàn toàn (trước đây KHÔNG có case này).
                case PartType.GirderEnd: extrude = -input.WebHeight; return true;
                // Tấm kín — đứng thẳng như Web, cao hết chiều cao Web (xác nhận người dùng 2026-08-25).
                case PartType.ClosedPlate: extrude = -input.WebHeight; return true;
                // Tấm đệm hàn trên web — đứng thẳng như Web nhưng THỤT VÀO 30mm mỗi đầu (cách mặt dưới
                // Top Plate 30mm, cách Flange 30mm) — xác nhận người dùng 2026-08-25.
                case PartType.BackingPlate: extrude = -(input.WebHeight - 60.0); moveZ = -30.0; return true;
                default: return false;
            }
        }

        public Solid3d Build(BuildContext ctx, PanelPart part)
        {
            if (!ZParams(part.Type, ctx.Input, out double extrude, out double moveZ)) return null;
            if (Math.Abs(extrude) < 1e-6) return null;
            return Extrude(ctx.Tr, part, extrude, moveZ, ctx.Report);
        }

        /// <summary>Clone polyline gốc về WCS → Region → extrude dz → dịch moveZ. null nếu lỗi.
        /// <paramref name="report"/> (tuỳ chọn) nhận lý do lỗi THẬT — trước đây chỉ ghi
        /// <c>Debug.WriteLine</c> (không lên report JSON) nên thất bại (VD Top Plate) không để lại dấu
        /// vết gì ngoài dòng "thử builder kế" chung chung, không biết nguyên nhân thật.</summary>
        public static Solid3d Extrude(Transaction tr, PanelPart part, double extrude, double moveZ,
            System.Collections.Generic.List<string> report = null)
        {
            Entity clone = null;
            Region reg = null;
            try
            {
                var src = (Entity)tr.GetObject(part.Id, OpenMode.ForRead);
                clone = (Entity)src.Clone();
                clone.TransformBy(part.Transform); // local -> WCS

                // Polyline "nhìn kín" (đỉnh đầu≈cuối) nhưng flag Closed=false (xem PartCollector.
                // IsVisuallyClosed, skill panel-parts-reference Bug #8) — ép Closed=true trên CLONE
                // (an toàn, không đụng entity gốc trong database). ⚠️ PHẢI xoá đỉnh CUỐI trùng đỉnh ĐẦU
                // trước khi bật Closed — nếu không, AutoCAD vẽ THÊM 1 cạnh khép nối đỉnh cuối→đầu, dài
                // gần 0 (đúng bằng sai số trùng, VD 0.0003mm) chồng lên đúng chỗ đỉnh đầu → cạnh suy biến
                // khiến Region.CreateFromCurves báo `eInvalidInput`. Kiểm chứng thật: Top Plate panel
                // 7D-17C (`38DDC`) — bật Closed mà KHÔNG xoá đỉnh trùng vẫn lỗi `eInvalidInput` y hệt.
                if (clone is Polyline plClone && !plClone.Closed)
                {
                    int n = plClone.NumberOfVertices;
                    if (n >= 3 && plClone.GetPoint2dAt(0).GetDistanceTo(plClone.GetPoint2dAt(n - 1)) <= 1e-3)
                        plClone.RemoveVertexAt(n - 1);
                    plClone.Closed = true;
                }

                DBObjectCollection regions;
                try { regions = Region.CreateFromCurves(new DBObjectCollection { clone }); }
                catch (System.Exception rex)
                {
                    report?.Add($"{LOG} {part.Type} {part.Id.Handle}: Region.CreateFromCurves lỗi: {rex.Message}");
                    return null;
                }
                if (regions.Count == 0)
                {
                    report?.Add($"{LOG} {part.Type} {part.Id.Handle}: Region.CreateFromCurves trả về 0 region (outline có thể tự cắt/suy biến).");
                    return null;
                }

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
                report?.Add($"{LOG} {part.Type} {part.Id.Handle}: {ex.Message}");
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
