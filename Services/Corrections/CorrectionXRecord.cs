using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;

namespace MCG_3DPanel.Services.Corrections
{
    /// <summary>
    /// Đọc/ghi 1 chuỗi (JSON) vào XRecord trong ExtensionDictionary của 1 BlockTableRecord.
    /// Chuỗi chia khúc ≤ 250 ký tự, mỗi khúc 1 TypedValue mã 1 (giới hạn độ dài chuỗi của XRecord).
    /// Phải gọi trong Transaction + document lock (lệnh AutoCAD).
    /// </summary>
    internal static class CorrectionXRecord
    {
        public const string CorrectionsKey = "MCG3D_CORRECTIONS";   // trên BTR 2D nguồn
        public const string NamesKey = "MCG3D_NAMES";               // trên BTR 2D nguồn — tên người dùng (tab Naming)
        public const string PendingKey = "MCG3D_PENDING";           // trên BTR _Assy trong BẢN VẼ 2D — sửa chờ Update (PendingEdits)
        public const string PanelKey = "MCG3D_PANEL";              // trên BTR 3D — dữ liệu panel để mở lại file vẫn điền được tab
        private const int CHUNK = 250;

        public static string Read(Transaction tr, ObjectId btrId, string key)
        {
            if (btrId.IsNull || btrId.IsErased) return null;
            var btr = tr.GetObject(btrId, OpenMode.ForRead) as DBObject;
            if (btr == null || btr.ExtensionDictionary.IsNull) return null;
            var dict = (DBDictionary)tr.GetObject(btr.ExtensionDictionary, OpenMode.ForRead);
            if (!dict.Contains(key)) return null;
            var xr = tr.GetObject(dict.GetAt(key), OpenMode.ForRead) as Xrecord;
            if (xr?.Data == null) return null;
            var sb = new StringBuilder();
            foreach (TypedValue tv in xr.Data.AsArray())
                if (tv.TypeCode == (short)DxfCode.Text) sb.Append(tv.Value as string);
            return sb.ToString();
        }

        /// <summary>Ghi <paramref name="text"/>; null/rỗng ⇒ xoá XRecord.</summary>
        public static void Write(Transaction tr, ObjectId btrId, string key, string text)
        {
            var obj = tr.GetObject(btrId, OpenMode.ForWrite);
            if (obj.ExtensionDictionary.IsNull)
            {
                if (string.IsNullOrEmpty(text)) return;
                obj.CreateExtensionDictionary();
            }
            var dict = (DBDictionary)tr.GetObject(obj.ExtensionDictionary, OpenMode.ForWrite);
            if (string.IsNullOrEmpty(text))
            {
                if (dict.Contains(key)) tr.GetObject(dict.GetAt(key), OpenMode.ForWrite).Erase();
                return;
            }
            var values = new List<TypedValue>();
            values.AddRange(CorrectionJson.Chunk(text, CHUNK).Select(s => new TypedValue((int)DxfCode.Text, s)));
            var data = new ResultBuffer(values.ToArray());
            if (dict.Contains(key))
            {
                var xr = (Xrecord)tr.GetObject(dict.GetAt(key), OpenMode.ForWrite);
                xr.Data = data;
            }
            else
            {
                var xr = new Xrecord { Data = data };
                dict.SetAt(key, xr);
                tr.AddNewlyCreatedDBObject(xr, true);
            }
        }
    }
}
