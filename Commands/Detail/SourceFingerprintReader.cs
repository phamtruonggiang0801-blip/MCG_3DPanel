using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;

namespace MCG_3DPanel.Commands.Detail
{
    /// <summary>Đọc "dấu vân tay" cấu trúc của block 2D nguồn: mỗi entity 1 record, KHÔNG gồm handle
    /// (re-save không sửa gì thì không đổi) và bỏ layer Mechanical-AM_9 (kích thước nháp không đổi kết cấu).</summary>
    public static class SourceFingerprintReader
    {
        const string DraftDimLayer = "Mechanical-AM_9";

        public static List<string> Records(Transaction tr, ObjectId assyBtrId)
        {
            var list = new List<string>();
            Walk(tr, assyBtrId, list, new HashSet<ObjectId>());
            return list;
        }

        static void Walk(Transaction tr, ObjectId btrId, List<string> list, HashSet<ObjectId> seen)
        {
            if (!seen.Add(btrId)) return;   // mỗi định nghĩa block chỉ duyệt 1 lần
            var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
            foreach (ObjectId id in btr)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null || ent.Layer == DraftDimLayer) continue;
                list.Add(string.Join("|", btr.Name, ent.GetType().Name, ent.Layer, Ext(ent)));
                if (ent is BlockReference br) Walk(tr, br.BlockTableRecord, list, seen);
            }
        }

        static string Ext(Entity e)
        {
            try
            {
                var x = e.GeometricExtents;
                return string.Format(CultureInfo.InvariantCulture, "{0:0.00},{1:0.00},{2:0.00},{3:0.00},{4:0.00},{5:0.00}",
                    R(x.MinPoint.X), R(x.MinPoint.Y), R(x.MinPoint.Z), R(x.MaxPoint.X), R(x.MaxPoint.Y), R(x.MaxPoint.Z));
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return "noext"; }   // entity không có extents
        }

        static double R(double v) => Math.Round(v, 2) + 0.0;   // +0.0: -0.00 → 0.00
    }
}
