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
        }

        public static string Serialize(IReadOnlyDictionary<string, string> names, IEnumerable<string> deleted = null)
        {
            var doc = new Doc();
            foreach (var kv in names ?? new Dictionary<string, string>())
                if (!string.IsNullOrEmpty(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value)) doc.Names[kv.Key] = kv.Value;
            foreach (var k in deleted ?? new string[0])
                if (!string.IsNullOrEmpty(k) && !doc.Deleted.Contains(k)) doc.Deleted.Add(k);
            return JsonSerializer.Serialize(doc);
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
