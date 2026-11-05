using System;
using System.Collections.Generic;
using System.Text.Json;

namespace MCG_3DPanel.Naming
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>Tên người dùng đặt (SourceKey → tên) ↔ JSON trong XRecord MCG3D_NAMES. JSON hỏng/rỗng ⇒ không có tên.</summary>
    public static class NamesJson
    {
        private sealed class Doc
        {
            public int Version { get; set; } = 1;
            public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>();
        }

        public static string Serialize(IReadOnlyDictionary<string, string> names)
        {
            var doc = new Doc();
            foreach (var kv in names ?? new Dictionary<string, string>())
                if (!string.IsNullOrEmpty(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value)) doc.Names[kv.Key] = kv.Value;
            return JsonSerializer.Serialize(doc);
        }

        public static Dictionary<string, string> Deserialize(string json)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(json)) return result;
            try
            {
                var doc = JsonSerializer.Deserialize<Doc>(json);
                if (doc?.Names != null)
                    foreach (var kv in doc.Names)
                        if (!string.IsNullOrEmpty(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value)) result[kv.Key] = kv.Value;
            }
            catch (JsonException) { /* JSON hỏng ⇒ coi như chưa đặt tên (giống CorrectionJson) */ }
            return result;
        }
    }
}
