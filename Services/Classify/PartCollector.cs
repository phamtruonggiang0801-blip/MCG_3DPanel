using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCG_3DPanel.Models;

namespace MCG_3DPanel.Services.Classify
{
    /// <summary>
    /// BƯỚC 1 — Đọc DWG: duyệt đệ quy block reference, lấy MỌI Closed LWPolyline và
    /// dựng <see cref="PanelPart"/> với hình học đã quy về WCS (bbox, tâm, dày/dài, transform).
    /// KHÔNG quyết định loại (Type) — việc đó ở các bước sau.
    /// </summary>
    internal static class PartCollector
    {
        /// <summary>Duyệt đệ quy blockRef, gom part vào <paramref name="outParts"/>.</summary>
        public static void Collect(Transaction tr, BlockReference blockRef, Matrix3d accum, List<PanelPart> outParts)
        {
            Collect(tr, blockRef, accum, outParts, new List<ObjectId> { blockRef.ObjectId });
        }

        /// <summary><paramref name="pathPrefix"/> = chuỗi ObjectId các blockRef container đã duyệt
        /// (từ ngoài cùng), để dựng FullSubentityPath cho highlight entity lồng.</summary>
        private static void Collect(Transaction tr, BlockReference blockRef, Matrix3d accum,
                                    List<PanelPart> outParts, List<ObjectId> pathPrefix)
        {
            var btr = (BlockTableRecord)tr.GetObject(blockRef.BlockTableRecord, OpenMode.ForRead);
            foreach (ObjectId id in btr)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                if (ent is BlockReference nested)
                {
                    // Block chú thích (VD "COG" — mốc trọng tâm: Hatch + 2 Line + Circle r250 TRÊN LAYER
                    // "0", cùng layer Top Plate) KHÔNG phải hình học tấm/lỗ thật — bỏ qua hẳn, không đệ
                    // quy vào. Nếu không loại, Circle bên trong (đã nhận từ khi thêm hỗ trợ Circle) lọt
                    // vào topCands/CutOut vì cùng owner block với biên Top Plate → tạo lỗ khoét ẢO tại vị
                    // trí mốc COG. Xác nhận kỹ sư 2026-08-25: "Block COG không được đưa vào đây, bỏ qua".
                    if (string.Equals(GetName(tr, nested), "COG", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var childPrefix = new List<ObjectId>(pathPrefix) { nested.ObjectId };
                    Collect(tr, nested, accum * nested.BlockTransform, outParts, childPrefix);
                    continue;
                }

                if (ent is Polyline pl && (pl.Closed || IsVisuallyClosed(pl)))
                {
                    var part = BuildPart(pl, accum);
                    if (part != null)
                    {
                        var full = new ObjectId[pathPrefix.Count + 1];
                        pathPrefix.CopyTo(full);
                        full[pathPrefix.Count] = pl.ObjectId;
                        part.IdPath = full;
                        outParts.Add(part);
                    }
                    continue;
                }

                // Lỗ khoét tròn (VD Ø80 trong block Top Plate) vẽ bằng Circle, không phải Polyline —
                // trước đây bị bỏ sót HOÀN TOÀN (không lỗi, không cảnh báo). Xem skill panel-parts-reference.
                if (ent is Circle cir)
                {
                    var part = BuildPartFromCircle(cir, accum);
                    if (part != null)
                    {
                        var full = new ObjectId[pathPrefix.Count + 1];
                        pathPrefix.CopyTo(full);
                        full[pathPrefix.Count] = cir.ObjectId;
                        part.IdPath = full;
                        outParts.Add(part);
                    }
                }
            }
        }

        /// <summary>
        /// Polyline có `Closed=false` nhưng đỉnh ĐẦU trùng đỉnh CUỐI (người vẽ tự khép vòng bằng cách
        /// snap lại điểm đầu, thay vì bật flag Closed) — vẫn là 1 biên KÍN thật, phải nhận như Closed.
        /// Bug thật đã xảy ra: biên **Top Plate** panel 7D-17C (`38DDC`, layer "0", 65 đỉnh) bị bỏ sót
        /// HOÀN TOÀN vì `Closed=false` — đỉnh đầu (15.3534,−197.4544) và đỉnh cuối (15.3531,−197.4544)
        /// lệch **0.0003mm** (làm tròn nổi) — khiến `topCands` chỉ còn 2 mảnh nhỏ không liên quan (lỗ/
        /// gia cường cạnh đó), TopPlate dựng ra bé tí thay vì cả tấm. Xem skill panel-parts-reference.
        /// </summary>
        private static bool IsVisuallyClosed(Polyline pl)
        {
            const double TOL = 1e-3;
            int n = pl.NumberOfVertices;
            if (n < 3) return false;
            Point2d a = pl.GetPoint2dAt(0);
            Point2d b = pl.GetPoint2dAt(n - 1);
            return a.GetDistanceTo(b) <= TOL;
        }

        /// <summary>Tên block (ưu tiên dynamic block gốc). "&lt;unknown&gt;" nếu lỗi.</summary>
        public static string GetName(Transaction tr, BlockReference br)
        {
            try
            {
                var id = br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord;
                return ((BlockTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name;
            }
            catch { return "<unknown>"; }
        }

        private static PanelPart BuildPart(Polyline pl, Matrix3d accum)
        {
            try
            {
                var pts = new List<Point3d>(pl.NumberOfVertices);
                for (int i = 0; i < pl.NumberOfVertices; i++)
                    pts.Add(pl.GetPoint3dAt(i).TransformBy(accum));

                Extents3d bounds = pl.GeometricExtents;
                bounds.TransformBy(accum);

                double dx = bounds.MaxPoint.X - bounds.MinPoint.X;
                double dy = bounds.MaxPoint.Y - bounds.MinPoint.Y;
                bool horiz = dx >= dy;

                var c = new Point3d(
                    (bounds.MinPoint.X + bounds.MaxPoint.X) / 2.0,
                    (bounds.MinPoint.Y + bounds.MaxPoint.Y) / 2.0,
                    (bounds.MinPoint.Z + bounds.MaxPoint.Z) / 2.0);

                RectDims(pts, bounds, out double thickness, out double length);

                return new PanelPart
                {
                    AxisDir = AxisOf(pts, horiz),
                    Id = pl.ObjectId,
                    Type = PartType.Unknown,
                    Layer = pl.Layer,
                    ColorIndex = pl.Color.ColorIndex,
                    VertexCount = pl.NumberOfVertices,
                    Area = SafeArea(pl),
                    FootprintThickness = thickness,
                    MemberLength = length,
                    Centroid = c,
                    Bounds = bounds,
                    Vertices = pts.ToArray(),
                    Transform = accum,
                    IsHorizontal = horiz,
                    PerpCenter = horiz ? c.Y : c.X,
                };
            }
            catch { return null; }
        }

        private static double SafeArea(Polyline pl)
        {
            try { return pl.Area; } catch { return 0; }
        }

        /// <summary>Circle → PanelPart đơn giản (không Vertices — StrictlyInside/CutOut chỉ cần Bounds;
        /// PlateBuilder.Extrude dựng Region thẳng từ chính entity Circle gốc, không cần Vertices).</summary>
        private static PanelPart BuildPartFromCircle(Circle cir, Matrix3d accum)
        {
            try
            {
                Extents3d bounds = cir.GeometricExtents;
                bounds.TransformBy(accum);
                var c = new Point3d(
                    (bounds.MinPoint.X + bounds.MaxPoint.X) / 2.0,
                    (bounds.MinPoint.Y + bounds.MaxPoint.Y) / 2.0,
                    (bounds.MinPoint.Z + bounds.MaxPoint.Z) / 2.0);
                double d = cir.Radius * 2.0;

                return new PanelPart
                {
                    AxisDir = Vector3d.XAxis,
                    Id = cir.ObjectId,
                    Type = PartType.Unknown,
                    Layer = cir.Layer,
                    ColorIndex = cir.Color.ColorIndex,
                    VertexCount = 0,
                    Area = Math.PI * cir.Radius * cir.Radius,
                    FootprintThickness = d,
                    MemberLength = d,
                    Centroid = c,
                    Bounds = bounds,
                    Vertices = null,
                    Transform = accum,
                    IsHorizontal = true,
                    PerpCenter = c.Y,
                };
            }
            catch { return null; }
        }

        /// <summary>
        /// Trục dài THẬT của member = <see cref="ClassifyUtil.LongAxis"/> (hướng cạnh DÀI NHẤT) cho
        /// rectangle 4/5 đỉnh — đúng cả member CHÉO VÀ 5 đỉnh vát góc (đoạn vát ngắn không bao giờ là
        /// cạnh dài nhất; nhánh cũ "2 cạnh kề đầu tiên" chọn nhầm đoạn vát 41mm làm trục). Fallback bbox.
        /// </summary>
        private static Vector3d AxisOf(List<Point3d> pts, bool horiz)
        {
            int n = pts.Count;
            if (n == 4 || n == 5)
            {
                ClassifyUtil.OrientedBox(pts.ToArray(), out Vector3d axis, out _, out _, out _, out _);
                if (axis.Length > 1e-9) return axis;
            }
            return horiz ? Vector3d.XAxis : Vector3d.YAxis;
        }

        /// <summary>Rectangle (4/5 đỉnh) → span theo trục dài / trục vuông góc (<see cref="ClassifyUtil.OrientedBox"/>);
        /// ngược lại dùng bbox cho <paramref name="length"/> và <see cref="ClassifyUtil.MinWidth"/> cho
        /// <paramref name="thickness"/>. Nhánh cũ (d(v0,v1) vs d(v1,v2)) đọc SAI tấm 5 đỉnh vát góc `26FFF` —
        /// ra thk=10/len=41.2 thay vì 20/500 (đoạn vát v1→v2 dài 41.2 bị chọn làm "cạnh"). Với polygon &gt;5
        /// đỉnh (web bẻ góc chữ L, tier vát nhiều bậc), bbox <c>min(dx,dy)</c> "phồng" — vd web L 6mm bbox
        /// 2749×1777 ⇒ thickness = 1777 (SAI, bug collar 2026-09-04, panel 701S handle 6303E). </summary>
        private static void RectDims(List<Point3d> pts, Extents3d bounds, out double thickness, out double length)
        {
            int n = pts.Count;
            if (n == 4 || n == 5)
            {
                ClassifyUtil.OrientedBox(pts.ToArray(), out _, out double len, out double thk, out _, out _);
                if (thk > 1e-6) { thickness = thk; length = len; return; }
            }
            double dx = bounds.MaxPoint.X - bounds.MinPoint.X;
            double dy = bounds.MaxPoint.Y - bounds.MinPoint.Y;
            length = Math.Max(dx, dy);
            double bboxThk = Math.Min(dx, dy);
            double mw = ClassifyUtil.MinWidth(pts.ToArray());
            thickness = (mw > 1e-6) ? Math.Min(mw, bboxThk) : bboxThk;
        }
    }
}
