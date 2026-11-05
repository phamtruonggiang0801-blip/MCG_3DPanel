using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using MCG_3DPanel.Services.Corrections;

namespace MCG_3DPanel.Naming
{
    /// <summary>
    /// Tên người dùng đặt + đoạn đã "Del Name" trên tab Naming. NGUỒN GỐC = XRecord <see cref="CorrectionXRecord.NamesKey"/>
    /// trên ĐỊNH NGHĨA block 2D (Ctrl+S là lưu); bộ nhớ chỉ là cache trong phiên. Khoá panel giống
    /// <see cref="CorrectionStore.PanelKey"/>. Chỉ lưu tên người dùng GÕ — tên tự sinh tính lại mỗi lần nạp. Không rebuild.
    /// </summary>
    public static class NameStore
    {
        private static readonly Dictionary<string, Dictionary<string, string>> _store =
            new Dictionary<string, Dictionary<string, string>>();
        private static readonly Dictionary<string, HashSet<string>> _deleted = new Dictionary<string, HashSet<string>>();
        private static readonly HashSet<string> _loaded = new HashSet<string>();
        private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

        public static void EnsureLoaded(Transaction tr, ObjectId btr2D)
        {
            string key = CorrectionStore.PanelKey(btr2D);
            if (key == "" || !_loaded.Add(key)) return;
            var d = NamesJson.Deserialize(CorrectionXRecord.Read(tr, btr2D, CorrectionXRecord.NamesKey), out var del, out var merges);
            foreach (var g in merges) AddGroup(key, g);
            if (d.Count > 0)
            {
                if (_store.TryGetValue(key, out var cache)) NamesJson.MergeLoaded(cache, d);   // giữ tên vừa sửa trước lần nạp
                else _store[key] = d;
            }
            if (del.Count > 0) DeletedSet(key).UnionWith(del);
        }

        public static IReadOnlyDictionary<string, string> For(string panelKey) =>
            panelKey != null && _store.TryGetValue(panelKey, out var d) ? d : Empty;

        public static ICollection<string> Deleted(string panelKey) =>
            panelKey != null && _deleted.TryGetValue(panelKey, out var s) ? s : (ICollection<string>)new string[0];

        private static HashSet<string> DeletedSet(string panelKey)
        {
            if (!_deleted.TryGetValue(panelKey, out var s)) _deleted[panelKey] = s = new HashSet<string>();
            return s;
        }

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

        /// <summary>"Del Name": tuyến không hiện, không đánh số (các tuyến khác đánh số lại).</summary>
        public static void DeleteLine(string panelKey, NamingLine line)
        {
            if (string.IsNullOrEmpty(panelKey) || line == null) return;
            var s = DeletedSet(panelKey);
            foreach (var p in line.Pieces) if (!string.IsNullOrEmpty(p.Key)) s.Add(p.Key);
        }

        // ─── Merge (revision 2): nhóm SourceKey gộp thành 1 tuyến ─────────
        private static readonly Dictionary<string, List<HashSet<string>>> _merges = new Dictionary<string, List<HashSet<string>>>();

        public static IReadOnlyList<HashSet<string>> Merges(string panelKey) =>
            panelKey != null && _merges.TryGetValue(panelKey, out var l) ? l : (IReadOnlyList<HashSet<string>>)new HashSet<string>[0];

        /// <summary>Gộp các tuyến thành 1 nhóm; nhóm cũ nào chạm vào thì nhập luôn (1 đoạn chỉ thuộc 1 nhóm).</summary>
        public static void Merge(string panelKey, IEnumerable<NamingLine> lines)
        {
            if (string.IsNullOrEmpty(panelKey) || lines == null) return;
            AddGroup(panelKey, lines.SelectMany(l => l.Pieces).Select(p => p.Key).Where(k => !string.IsNullOrEmpty(k)));
        }

        private static void AddGroup(string panelKey, IEnumerable<string> keys)
        {
            var g = new HashSet<string>(keys);
            if (g.Count < 2) return;
            if (!_merges.TryGetValue(panelKey, out var list)) _merges[panelKey] = list = new List<HashSet<string>>();
            foreach (var old in list.Where(o => o.Overlaps(g)).ToList()) { g.UnionWith(old); list.Remove(old); }
            list.Add(g);
        }

        /// <summary>Tách tuyến gộp: bỏ mọi nhóm chứa đoạn của <paramref name="line"/>. Trả true nếu có bỏ.</summary>
        public static bool Unmerge(string panelKey, NamingLine line)
        {
            if (panelKey == null || line == null || !_merges.TryGetValue(panelKey, out var list)) return false;
            var keys = new HashSet<string>(line.Pieces.Select(p => p.Key).Where(k => k != null));
            return list.RemoveAll(g => g.Overlaps(keys)) > 0;
        }

        /// <summary>Lấy lại MỌI tuyến đã Del Name của panel. Trả số đoạn được trả lại.</summary>
        public static int RestoreAll(string panelKey)
        {
            if (panelKey == null || !_deleted.TryGetValue(panelKey, out var s)) return 0;
            int n = s.Count;
            s.Clear();
            return n;
        }

        /// <summary>Ghi cache xuống XRecord BTR 2D. Trả thông báo lỗi (null nếu OK) — không ném.</summary>
        public static string Save(Transaction tr, ObjectId btr2D)
        {
            string key = CorrectionStore.PanelKey(btr2D);
            if (key == "") return "Names could not be saved (the 2D block definition is missing).";
            try
            {
                EnsureLoaded(tr, btr2D);   // chưa nạp ⇒ gộp bản trong drawing vào TRƯỚC (không ghi đè bằng cache thiếu)
                var btr = tr.GetObject(btr2D, OpenMode.ForRead) as BlockTableRecord;
                if (btr != null && (btr.IsFromExternalReference || btr.IsDependent))
                    return "Names could not be saved in the drawing (the 2D block is an xref).";
                var names = For(key);
                var del = Deleted(key);
                var merges = Merges(key);
                CorrectionXRecord.Write(tr, btr2D, CorrectionXRecord.NamesKey,
                    names.Count > 0 || del.Count > 0 || merges.Count > 0 ? NamesJson.Serialize(names, del.ToList(), merges) : null);
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
            foreach (var k in new List<string>(_deleted.Keys))
                if (k.StartsWith(prefix)) _deleted.Remove(k);
            foreach (var k in new List<string>(_merges.Keys))
                if (k.StartsWith(prefix)) _merges.Remove(k);
            _loaded.RemoveWhere(k => k.StartsWith(prefix));
        }
    }
}
