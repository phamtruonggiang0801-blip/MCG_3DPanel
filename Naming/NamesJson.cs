using System;
using System.Collections.Generic;
using System.Text.Json;

namespace MCG_3DPanel.Naming
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>Tên người dùng đặt (SourceKey → tên) + đoạn đã "Del Name" ↔ JSON trong XRecord MCG3D_NAMES.
    /// JSON hỏng/rỗng ⇒ không có tên.</summary>
    public static class NamesJson
    {
        private sealed class Doc
        {
            public int Version { get; set; } = 1;
            public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>();
            public List<string> Deleted { get; set; } = new List<string>();
            /// <summary>Nhóm Merge (revision 2): mỗi nhóm = SourceKey các đoạn gộp thành 1 tuyến.</summary>
            public List<List<string>> Merges { get; set; } = new List<List<string>>();
        }

        public static string Serialize(IReadOnlyDictionary<string, string> names, IEnumerable<string> deleted = null,
            IEnumerable<IEnumerable<string>> merges = null)
        {
            var doc = new Doc();
            foreach (var kv in names ?? new Dictionary<string, string>())
                if (!string.IsNullOrEmpty(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value)) doc.Names[kv.Key] = kv.Value;
            foreach (var k in deleted ?? new string[0])
                if (!string.IsNullOrEmpty(k) && !doc.Deleted.Contains(k)) doc.Deleted.Add(k);
            foreach (var g in merges ?? new IEnumerable<string>[0])
            {
                var list = new List<string>();
                foreach (var k in g ?? new string[0]) if (!string.IsNullOrEmpty(k) && !list.Contains(k)) list.Add(k);
                if (list.Count > 1) doc.Merges.Add(list);
            }
            return JsonSerializer.Serialize(doc);
        }

        public static Dictionary<string, string> Deserialize(string json, out HashSet<string> deleted, out List<HashSet<string>> merges)
        {
            merges = new List<HashSet<string>>();
            var r = Deserialize(json, out deleted);
            if (string.IsNullOrWhiteSpace(json)) return r;
            try
            {
                var doc = JsonSerializer.Deserialize<Doc>(json);
                foreach (var g in doc?.Merges ?? new List<List<string>>())
                    if (g != null && g.Count > 1) merges.Add(new HashSet<string>(g, StringComparer.Ordinal));
            }
            catch (JsonException) { /* như trên */ }
            return r;
        }

        /// <summary>Gộp tên đọc từ XRecord vào cache: chỉ thêm khoá cache CHƯA có — tên vừa sửa trước lần nạp không bị
        /// ghi đè bởi bản cũ trong drawing (review I3).</summary>
        public static void MergeLoaded(Dictionary<string, string> cache, IReadOnlyDictionary<string, string> loaded)
        {
            if (cache == null || loaded == null) return;
            foreach (var kv in loaded)
                if (!cache.ContainsKey(kv.Key)) cache[kv.Key] = kv.Value;
        }

        public static Dictionary<string, string> Deserialize(string json) => Deserialize(json, out _);

        public static Dictionary<string, string> Deserialize(string json, out HashSet<string> deleted)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            deleted = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(json)) return result;
            try
            {
                var doc = JsonSerializer.Deserialize<Doc>(json);
                if (doc?.Names != null)
                    foreach (var kv in doc.Names)
                        if (!string.IsNullOrEmpty(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value)) result[kv.Key] = kv.Value;
                if (doc?.Deleted != null)
                    foreach (var k in doc.Deleted) if (!string.IsNullOrEmpty(k)) deleted.Add(k);
            }
            catch (JsonException) { /* JSON hỏng ⇒ coi như chưa đặt tên (giống CorrectionJson) */ }
            return result;
        }
    }
}
