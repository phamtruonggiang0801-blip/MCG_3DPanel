using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using MCG_3DPanel.Services.Corrections;

namespace MCG_3DPanel.Naming
{
    /// <summary>
    /// Tên người dùng đặt trên tab Naming. NGUỒN GỐC = XRecord <see cref="CorrectionXRecord.NamesKey"/> trên ĐỊNH NGHĨA
    /// block 2D (Ctrl+S là lưu); bộ nhớ chỉ là cache trong phiên. Khoá panel giống <see cref="CorrectionStore.PanelKey"/>.
    /// Chỉ lưu tên người dùng GÕ — tên tự sinh tính lại mỗi lần nạp. Đổi tên KHÔNG rebuild.
    /// </summary>
    public static class NameStore
    {
        private static readonly Dictionary<string, Dictionary<string, string>> _store =
            new Dictionary<string, Dictionary<string, string>>();
        private static readonly HashSet<string> _loaded = new HashSet<string>();
        private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

        public static void EnsureLoaded(Transaction tr, ObjectId btr2D)
        {
            string key = CorrectionStore.PanelKey(btr2D);
            if (key == "" || !_loaded.Add(key)) return;
            var d = NamesJson.Deserialize(CorrectionXRecord.Read(tr, btr2D, CorrectionXRecord.NamesKey));
            if (d.Count == 0) return;
            if (_store.TryGetValue(key, out var cache)) NamesJson.MergeLoaded(cache, d);   // giữ tên vừa sửa trước lần nạp
            else _store[key] = d;
        }

        public static IReadOnlyDictionary<string, string> For(string panelKey) =>
            panelKey != null && _store.TryGetValue(panelKey, out var d) ? d : Empty;

        /// <summary>Đặt <paramref name="name"/> cho MỌI đoạn của tuyến; null/trắng ⇒ xoá (về tên tự sinh).</summary>
        public static void SetLine(string panelKey, NamingLine line, string name)
        {
            if (string.IsNullOrEmpty(panelKey) || line == null) return;
            if (!_store.TryGetValue(panelKey, out var d)) _store[panelKey] = d = new Dictionary<string, string>();
            string n = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            foreach (var p in line.Pieces)
            {
                if (string.IsNullOrEmpty(p.Key)) continue;
                if (n == null) d.Remove(p.Key); else d[p.Key] = n;
            }
        }

        /// <summary>Ghi cache xuống XRecord BTR 2D. Trả thông báo lỗi (null nếu OK) — không ném.</summary>
        public static string Save(Transaction tr, ObjectId btr2D)
        {
            string key = CorrectionStore.PanelKey(btr2D);
            if (key == "") return "Names could not be saved (the 2D block definition is missing).";
            try
            {
                EnsureLoaded(tr, btr2D);   // chưa nạp ⇒ nạp TRƯỚC, không ghi đè XRecord đã lưu bằng cache rỗng
                var btr = tr.GetObject(btr2D, OpenMode.ForRead) as BlockTableRecord;
                if (btr != null && (btr.IsFromExternalReference || btr.IsDependent))
                    return "Names could not be saved in the drawing (the 2D block is an xref).";
                var names = For(key);
                CorrectionXRecord.Write(tr, btr2D, CorrectionXRecord.NamesKey, names.Count > 0 ? NamesJson.Serialize(names) : null);
                return null;
            }
            catch (System.Exception ex)
            {
                return "Names could not be saved in the drawing (" + ex.Message + ").";
            }
        }

        /// <summary>Bản vẽ sắp đóng: bỏ cache của Database đó (con trỏ có thể bị DB mới dùng lại).</summary>
        public static void ClearDatabase(Database db)
        {
            if (db == null) return;
            string prefix = $"{db.UnmanagedObject.ToInt64():X}:";
            foreach (var k in new List<string>(_store.Keys))
                if (k.StartsWith(prefix)) _store.Remove(k);
            _loaded.RemoveWhere(k => k.StartsWith(prefix));
        }
    }
}
