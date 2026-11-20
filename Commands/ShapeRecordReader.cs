using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using MCG_3DPanel.Services.Detail;
using MCG_3DPanel.Services.Update;

namespace MCG_3DPanel.Commands
{
    /// <summary>Đọc entity 2D của block panel + mọi block con thành <see cref="ShapeRecord"/> để Update so khớp theo hình
    /// (spec 2026-11-20 update-after-dc-block). Mỗi định nghĩa duyệt 1 lần, toạ độ của chính định nghĩa đó; bỏ layer
    /// Mechanical-AM_9; định nghĩa ẩn danh *U… ⇒ Block rỗng (không so khớp). Chỉ đọc.</summary>
    public static class ShapeRecordReader
    {
        const string DraftDimLayer = "Mechanical-AM_9";

        public static List<ShapeRecord> Records(Transaction tr, ObjectId panelBtrId)
        {
            var list = new List<ShapeRecord>();
            Walk(tr, panelBtrId, null, list, new HashSet<ObjectId>());
            return list;
        }

        static void Walk(Transaction tr, ObjectId btrId, string dynamicName, List<ShapeRecord> list, HashSet<ObjectId> seen)
        {
            if (!seen.Add(btrId)) return;
            var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
            string block = BlockName(btr.Name, dynamicName);
            foreach (ObjectId id in btr)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null || string.Equals(ent.Layer, DraftDimLayer, StringComparison.OrdinalIgnoreCase)) continue;
                var r = Record(tr, ent, block);
                if (r != null) list.Add(r);
                if (ent is BlockReference br) Walk(tr, br.BlockTableRecord, DynamicName(tr, br), list, seen);
            }
        }

        static string BlockName(string name, string dynamicName)
        {
            string n = string.IsNullOrEmpty(dynamicName) ? name ?? "" : dynamicName;
            return n.StartsWith("*", StringComparison.Ordinal) ? "" : n;
        }

        static ShapeRecord Record(Transaction tr, Entity ent, string block)
        {
            var r = new ShapeRecord { Handle = ent.Handle.ToString(), Block = block, Layer = ent.Layer ?? "" };
            switch (ent)
            {
                case Polyline pl:
                    r.Type = "Polyline";
                    r.Closed = pl.Closed;
                    for (int i = 0; i < pl.NumberOfVertices; i++)
                    {
                        var p = pl.GetPoint2dAt(i);
                        r.Points.Add(new P2(p.X, p.Y));
                        r.Bulges.Add(pl.GetBulgeAt(i));
                    }
                    return r;
                case Line ln:
                    r.Type = "Line";
                    r.Points.Add(new P2(ln.StartPoint.X, ln.StartPoint.Y));
                    r.Points.Add(new P2(ln.EndPoint.X, ln.EndPoint.Y));
                    return r;
                case Arc a:
                    r.Type = "Arc";
                    r.Points.Add(new P2(a.Center.X, a.Center.Y));
                    r.Scalars.AddRange(new[] { a.Radius, a.StartAngle, a.EndAngle });
                    return r;
                case Circle c:
                    r.Type = "Circle";
                    r.Points.Add(new P2(c.Center.X, c.Center.Y));
                    r.Scalars.Add(c.Radius);
                    return r;
                case BlockReference br:
                    r.Type = "BlockReference";
                    r.Name = EffectiveName(tr, br);
                    r.Points.Add(new P2(br.Position.X, br.Position.Y));
                    r.Scalars.AddRange(new[] { br.Rotation, br.ScaleFactors.X, br.ScaleFactors.Y });
                    return r;
                default:
                    r.Type = ent.GetType().Name;
                    try
                    {
                        var x = ent.GeometricExtents;
                        r.Points.Add(new P2(x.MinPoint.X, x.MinPoint.Y));
                        r.Points.Add(new P2(x.MaxPoint.X, x.MaxPoint.Y));
                        return r;
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception) { return null; }   // không có extents ⇒ bỏ
            }
        }

        static string EffectiveName(Transaction tr, BlockReference br)
        {
            string dyn = DynamicName(tr, br);
            if (!string.IsNullOrEmpty(dyn)) return dyn;
            return ((BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead)).Name;
        }

        static string DynamicName(Transaction tr, BlockReference br)
        {
            try
            {
                if (!br.IsDynamicBlock || br.DynamicBlockTableRecord == br.BlockTableRecord) return null;
                return ((BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead)).Name;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return null; }
        }
    }
}
