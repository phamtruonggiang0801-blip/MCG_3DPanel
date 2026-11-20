using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MCG_3DPanel.Naming;
using MCG_3DPanel.Services.Corrections;

namespace MCG_3DPanel.Services.Update
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.*.

    /// <summary>Đổi khoá correction / tên từ handle 2D cũ sang handle mới (spec 2026-11-20 update-after-dc-block). Khoá chứa handle
    /// dạng <c>{hex}#{n}</c> — đứng riêng hoặc trong khoá sinh ra (<c>CollarPlate:2B7B3#0~2B53D#1</c>).</summary>
    public static class KeyRemap
    {
        // Không nhận đuôi hex của một từ (GirderFlange#0 ⇒ "e#0" không phải handle)
        static readonly Regex Token = new Regex(@"(?<![0-9A-Za-z])([0-9A-Fa-f]+)#(\d+)", RegexOptions.Compiled);

        /// <summary>Thay mọi token có handle trong <paramref name="map"/>; có token không map được ⇒ null; không có token ⇒ giữ nguyên.</summary>
        public static string Remap(string key, IReadOnlyDictionary<string, string> map)
        {
            if (string.IsNullOrEmpty(key)) return key;
            bool missing = false;
            string r = Token.Replace(key, m =>
            {
                if (TryMap(map, m.Groups[1].Value, out string to)) return to + "#" + m.Groups[2].Value;
                missing = true;
                return m.Value;
            });
            return missing ? null : r;
        }

        static bool TryMap(IReadOnlyDictionary<string, string> map, string hex, out string to)
        {
            if (map.TryGetValue(hex, out to)) return true;
            foreach (var kv in map)
                if (string.Equals(kv.Key, hex, System.StringComparison.OrdinalIgnoreCase)) { to = kv.Value; return true; }
            return false;
        }

        /// <summary>JSON MCG3D_CORRECTIONS với khoá mới; đếm theo từng khoá lưu (Parts + Deleted). Rỗng ⇒ trả nguyên.</summary>
        public static string Corrections(string json, IReadOnlyDictionary<string, string> map, ref int kept, ref int dropped)
        {
            if (string.IsNullOrWhiteSpace(json)) return json;
            CorrectionJson.Deserialize(json, out var parts, out var deleted);
            var newParts = new Dictionary<string, PartCorrection>();
            foreach (var kv in parts)
            {
                string k = Remap(kv.Key, map);
                if (k == null) { dropped++; continue; }
                kept++;
                newParts[k] = kv.Value;
            }
            var newDeleted = Keys(deleted, map, ref kept, ref dropped);
            return CorrectionJson.Serialize(newParts, newDeleted);
        }

        /// <summary>JSON MCG3D_NAMES với khoá mới; đếm theo từng khoá lưu (Names + Deleted + từng thành viên Merge + Splits). Rỗng ⇒ trả nguyên.</summary>
        public static string Names(string json, IReadOnlyDictionary<string, string> map, ref int kept, ref int dropped)
        {
            if (string.IsNullOrWhiteSpace(json)) return json;
            var names = NamesJson.Deserialize(json, out var deleted, out var merges, out var splits);
            var newNames = new Dictionary<string, string>();
            foreach (var kv in names)
            {
                string k = Remap(kv.Key, map);
                if (k == null) { dropped++; continue; }
                kept++;
                newNames[k] = kv.Value;
            }
            var newDeleted = Keys(deleted, map, ref kept, ref dropped);
            var newMerges = new List<List<string>>();
            foreach (var g in merges) newMerges.Add(Keys(g, map, ref kept, ref dropped));
            var newSplits = Keys(splits, map, ref kept, ref dropped);
            return NamesJson.Serialize(newNames, newDeleted, newMerges, newSplits);   // nhóm Merge còn < 2 khoá tự bị bỏ
        }

        /// <summary>Dòng báo sau khi đổi khoá: "giữ / bỏ" chỉ khi có chỉnh sửa (kept + dropped &gt; 0), mỗi base point dời 1 dòng;
        /// không có gì ⇒ null (không báo "0 edits…" — ux-quality-bar).</summary>
        public static string Note(int kept, int dropped, IEnumerable<(string Block, MCG_3DPanel.Services.Detail.P2 Offset)> shifts)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var lines = new List<string>();
            if (kept + dropped > 0) lines.Add($"{kept} edits kept on unchanged parts; {dropped} dropped because their 2D part changed.");
            foreach (var (block, off) in shifts ?? Enumerable.Empty<(string, MCG_3DPanel.Services.Detail.P2)>())
                lines.Add($"The base point of {block} moved by ({off.X.ToString("0.#", inv)}, {off.Y.ToString("0.#", inv)}).");
            return lines.Count == 0 ? null : string.Join("\n", lines);
        }

        static List<string> Keys(IEnumerable<string> keys, IReadOnlyDictionary<string, string> map, ref int kept, ref int dropped)
        {
            var r = new List<string>();
            foreach (var key in keys ?? Enumerable.Empty<string>())
            {
                string k = Remap(key, map);
                if (k == null) { dropped++; continue; }
                kept++;
                r.Add(k);
            }
            return r;
        }
    }
}
