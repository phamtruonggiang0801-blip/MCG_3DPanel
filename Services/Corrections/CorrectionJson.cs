using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace MCG_3DPanel.Services.Corrections
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>
    /// Tuần tự hoá correction của 1 panel (theo SourceKey) + danh sách solid đã Delete thành JSON, để ghi vào
    /// XRecord của định nghĩa block 2D (spec 2026-11-04 §13). JSON hỏng/rỗng ⇒ coi như không có correction.
    /// </summary>
    public static class CorrectionJson
    {
        private sealed class Doc
        {
            public int Version { get; set; } = 1;
            public Dictionary<string, PartCorrection> Parts { get; set; } = new Dictionary<string, PartCorrection>();
            public List<string> Deleted { get; set; } = new List<string>();
        }

        private static readonly JsonSerializerOptions Opt = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        public static string Serialize(IReadOnlyDictionary<string, PartCorrection> parts, IEnumerable<string> deleted)
        {
            var doc = new Doc
            {
                Parts = (parts ?? new Dictionary<string, PartCorrection>())
                    .Where(kv => kv.Value != null && !kv.Value.IsEmpty)
                    .ToDictionary(kv => kv.Key, kv => kv.Value),
                Deleted = (deleted ?? Enumerable.Empty<string>()).Distinct().ToList(),
            };
            return JsonSerializer.Serialize(doc, Opt);
        }

        public static void Deserialize(string json, out Dictionary<string, PartCorrection> parts, out HashSet<string> deleted)
        {
            parts = new Dictionary<string, PartCorrection>();
            deleted = new HashSet<string>();
            if (string.IsNullOrWhiteSpace(json)) return;
            try
            {
                var doc = JsonSerializer.Deserialize<Doc>(json, Opt);
                if (doc?.Parts != null)
                    foreach (var kv in doc.Parts.Where(kv => kv.Value != null)) parts[kv.Key] = kv.Value;
                if (doc?.Deleted != null)
                    foreach (var k in doc.Deleted.Where(k => !string.IsNullOrEmpty(k))) deleted.Add(k);
            }
            catch (JsonException) { /* dữ liệu hỏng ⇒ bỏ, không chặn build */ }
        }

        /// <summary>Cắt chuỗi thành khúc ≤ <paramref name="size"/> ký tự (mỗi khúc = 1 TypedValue mã 1 của XRecord).</summary>
        public static List<string> Chunk(string s, int size)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(s)) return list;
            for (int i = 0; i < s.Length; i += size)
                list.Add(s.Substring(i, System.Math.Min(size, s.Length - i)));
            return list;
        }
    }
}
