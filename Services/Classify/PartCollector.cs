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
                    var childPrefix = new List<ObjectId>(pathPrefix) { nested.ObjectId };
                    Collect(tr, nested, accum * nested.BlockTransform, outParts, childPrefix);
                    continue;
                }

                if (ent is Polyline pl && pl.Closed)
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
                }
            }
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

        /// <summary>
        /// Trục dài THẬT của member = hướng cạnh DÀI hơn trong 2 cạnh kề đầu tiên (rectangle 4/5 đỉnh).
        /// Đúng cả khi member nằm CHÉO — bbox không suy ra được. Fallback: theo bbox (X/Y).
        /// </summary>
        private static Vector3d AxisOf(List<Point3d> pts, bool horiz)
        {
            int n = pts.Count;
            if (n == 4 || n == 5)
            {
                Vector3d e0 = pts[1] - pts[0];
                Vector3d e1 = pts[2 % n] - pts[1];
                Vector3d pick = e0.Length >= e1.Length ? e0 : e1;
                var flat = new Vector3d(pick.X, pick.Y, 0);
                if (flat.Length > 1e-9) return flat.GetNormal();
            }
            return horiz ? Vector3d.XAxis : Vector3d.YAxis;
        }

        /// <summary>Rectangle (4/5 đỉnh) → lấy cạnh từ khoảng cách đỉnh; ngược lại dùng bbox.</summary>
        private static void RectDims(List<Point3d> pts, Extents3d bounds, out double thickness, out double length)
        {
            int n = pts.Count;
            if (n == 4 || n == 5)
            {
                double s0 = pts[0].DistanceTo(pts[1]);
                double s1 = pts[1].DistanceTo(pts[2 % n]);
                thickness = Math.Min(s0, s1);
                length = Math.Max(s0, s1);
                if (thickness > 1e-6) return;
            }
            double dx = bounds.MaxPoint.X - bounds.MinPoint.X;
            double dy = bounds.MaxPoint.Y - bounds.MinPoint.Y;
            thickness = Math.Min(dx, dy);
            length = Math.Max(dx, dy);
        }
    }
}
