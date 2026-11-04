using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;

namespace MCG_3DPanel.Services.Corrections
{
    /// <summary>
    /// Kho correction. NGUỒN GỐC = XRecord <see cref="CorrectionXRecord.CorrectionsKey"/> trên ĐỊNH NGHĨA block 2D
    /// (lưu theo bản vẽ, Ctrl+S là lưu — spec 2026-11-04 §13); bộ nhớ chỉ là cache trong phiên.
    /// Khoá: panelKey (Database + BTR 2D, xem <see cref="PanelKey"/>) → SourceKey của part 2D.
    /// Mọi lần chèn của cùng block 2D dùng chung correction.
    /// </summary>
    public static class CorrectionStore
    {
        private static readonly Dictionary<string, Dictionary<string, PartCorrection>> _store =
            new Dictionary<string, Dictionary<string, PartCorrection>>();
        private static readonly HashSet<string> _loaded = new HashSet<string>();

        private static readonly IReadOnlyDictionary<string, PartCorrection> Empty = new Dictionary<string, PartCorrection>();

        /// <summary>Khoá panel = Database đang mở + handle ĐỊNH NGHĨA block 2D (handle trùng được giữa 2 bản vẽ).</summary>
        public static string PanelKey(ObjectId btr2D) =>
            btr2D.IsNull ? "" : $"{btr2D.Database.UnmanagedObject.ToInt64():X}:{btr2D.Handle}";

        /// <summary>Lần đầu gặp panel trong phiên: nạp correction từ XRecord của BTR 2D vào cache.</summary>
        public static void EnsureLoaded(Transaction tr, ObjectId btr2D)
        {
            string key = PanelKey(btr2D);
            if (key == "" || !_loaded.Add(key)) return;
            CorrectionJson.Deserialize(CorrectionXRecord.Read(tr, btr2D, CorrectionXRecord.CorrectionsKey),
                out var parts, out var deleted);
            if (parts.Count > 0) _store[key] = parts;
            if (deleted.Count > 0) _deleted[key] = deleted;
        }

        /// <summary>Ghi cache của panel xuống XRecord BTR 2D. Trả thông báo lỗi (null nếu OK) — không ném.</summary>
        public static string Save(Transaction tr, ObjectId btr2D)
        {
            string key = PanelKey(btr2D);
            if (key == "") return null;
            try
            {
                // Chưa nạp (vd sau khi mở lại bản vẽ) ⇒ nạp TRƯỚC, nếu không sẽ ghi đè XRecord đã lưu bằng cache rỗng.
                EnsureLoaded(tr, btr2D);
                var btr = tr.GetObject(btr2D, OpenMode.ForRead) as BlockTableRecord;
                if (btr != null && (btr.IsFromExternalReference || btr.IsDependent))
                    return "Corrections could not be saved in the drawing (the 2D block is an xref).";
                bool any = For(key).Count > 0 || Deleted(key).Count > 0;
                CorrectionXRecord.Write(tr, btr2D, CorrectionXRecord.CorrectionsKey,
                    any ? CorrectionJson.Serialize(For(key), Deleted(key)) : null);
                return null;
            }
            catch (System.Exception ex)
            {
                return "Corrections could not be saved in the drawing (" + ex.Message + ").";
            }
        }

        public static IReadOnlyDictionary<string, PartCorrection> For(string panelKey) =>
            panelKey != null && _store.TryGetValue(panelKey, out var d) ? d : Empty;

        public static PartCorrection Get(string panelKey, string sourceKey) =>
            sourceKey != null && For(panelKey).TryGetValue(sourceKey, out var c) ? c : null;

        public static void Set(string panelKey, string sourceKey, PartCorrection c)
        {
            if (string.IsNullOrEmpty(panelKey) || string.IsNullOrEmpty(sourceKey)) return;
            if (c == null || c.IsEmpty) { Clear(panelKey, sourceKey); return; }
            if (!_store.TryGetValue(panelKey, out var d)) _store[panelKey] = d = new Dictionary<string, PartCorrection>();
            d[sourceKey] = c;
        }

        // ─── Delete / Restore solid (theo DeleteKey của BuiltSolid) ───
        private static readonly Dictionary<string, HashSet<string>> _deleted = new Dictionary<string, HashSet<string>>();

        public static IReadOnlyCollection<string> Deleted(string panelKey) =>
            panelKey != null && _deleted.TryGetValue(panelKey, out var s) ? s : (IReadOnlyCollection<string>)new string[0];

        public static void SetDeleted(string panelKey, string deleteKey, bool deleted)
        {
            if (string.IsNullOrEmpty(panelKey) || string.IsNullOrEmpty(deleteKey)) return;
            if (!_deleted.TryGetValue(panelKey, out var s)) _deleted[panelKey] = s = new HashSet<string>();
            if (deleted) s.Add(deleteKey); else s.Remove(deleteKey);
        }

        /// <summary>Bản vẽ sắp đóng: bỏ mọi correction của Database đó (con trỏ có thể bị DB mới dùng lại).</summary>
        public static void ClearDatabase(Database db)
        {
            if (db == null) return;
            string prefix = $"{db.UnmanagedObject.ToInt64():X}:";
            foreach (var k in new List<string>(_store.Keys))
                if (k.StartsWith(prefix)) _store.Remove(k);
            _loaded.RemoveWhere(k => k.StartsWith(prefix));
            foreach (var k in new List<string>(_deleted.Keys))
                if (k.StartsWith(prefix)) _deleted.Remove(k);
        }

        public static void Clear(string panelKey, string sourceKey)
        {
            if (panelKey != null && sourceKey != null && _store.TryGetValue(panelKey, out var d)) d.Remove(sourceKey);
        }
    }
}
