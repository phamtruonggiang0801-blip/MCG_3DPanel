using System;
using System.Collections.Generic;
using System.Text.Json;
using MCG_3DPanel.Models;

namespace MCG_3DPanel.Services.Corrections
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>Một solid đã dựng trong file 3D: handle + khoá để map ngược về bản vẽ nguồn.</summary>
    public class PanelMetaSolid
    {
        public string Handle { get; set; } = "";
        public string Type { get; set; } = "";
        public string PartKey { get; set; } = "";
        public string FlipKey { get; set; } = "";
        public string DeleteKey { get; set; } = "";
        public string Code { get; set; } = "";
        public List<string> MergedCodes { get; set; } = new List<string>();
        public bool Deleted { get; set; }
        /// <summary>Đầu nào đã vát Sniped End (builder quyết định khi dựng, phân loại không tính lại được).</summary>
        public bool SnipLo { get; set; }
        public bool SnipHi { get; set; }

        /// <summary>Solid dựng theo <see cref="Type"/> (loại part lúc dựng) nhưng part khớp được nay là loại khác: dòng và solid lệch nhau.
        /// Solid đã Delete không còn gì để lệch; file không lưu loại thì không đếm.</summary>
        public bool ClassifiedDifferently(string partType) => !Deleted && !string.IsNullOrEmpty(Type) && Type != partType;
    }

    /// <summary>Một part 2D không ra solid: đủ để dựng lại dòng "N 2D part(s) not built (Type Handle)" của tab Result.</summary>
    public class PanelMetaNotBuilt
    {
        public string Handle { get; set; } = "";
        public string Type { get; set; } = "";
        public string Code { get; set; } = "";
    }

    /// <summary>Dữ liệu lưu cùng mỗi panel 3D (JSON): nguồn 2D, input đã dùng, vị trí, danh sách solid, cảnh báo + part không dựng được.
    /// Position là int để lớp độc lập với enum. JSON hỏng/rỗng ⇒ FromJson trả null. File dựng trước khi có Warnings/NotBuilt ⇒ 2 danh sách rỗng.</summary>
    public class PanelMeta
    {
        public string SourceRefHandle { get; set; } = "";
        public string SourceBtrHandle { get; set; } = "";
        public PanelInput Input { get; set; } = new PanelInput();
        public int Position { get; set; }
        public List<PanelMetaSolid> Solids { get; set; } = new List<PanelMetaSolid>();
        public List<string> Warnings { get; set; } = new List<string>();
        public List<PanelMetaNotBuilt> NotBuilt { get; set; } = new List<PanelMetaNotBuilt>();

        public string ToJson() => JsonSerializer.Serialize(this);

        public static PanelMeta FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var m = JsonSerializer.Deserialize<PanelMeta>(json);
                // JSON thiếu/null trường bắt buộc ⇒ coi là hỏng (tránh object nửa vời gây NullReference về sau).
                if (m == null || m.Input == null || m.Solids == null || m.Solids.Contains(null)
                    || string.IsNullOrEmpty(m.SourceRefHandle) || string.IsNullOrEmpty(m.SourceBtrHandle)) return null;
                // Hai trường báo cáo, không bắt buộc: thiếu/null ⇒ rỗng, panel vẫn đọc được.
                m.Warnings = m.Warnings ?? new List<string>();
                m.NotBuilt = m.NotBuilt ?? new List<PanelMetaNotBuilt>();
                m.Warnings.RemoveAll(w => w == null);
                m.NotBuilt.RemoveAll(p => p == null);
                return m;
            }
            catch (JsonException) { return null; }
        }

        /// <summary>Solid còn dùng được: giữ dòng Deleted (đã xoá chủ ý) và solid còn tồn tại; bỏ solid mất khỏi bản vẽ (đếm vào dropped).</summary>
        public List<PanelMetaSolid> LiveSolids(Func<string, bool> exists, out int dropped)
        {
            dropped = 0;
            var r = new List<PanelMetaSolid>();
            foreach (var s in Solids)
            {
                if (s.Deleted || exists(s.Handle)) r.Add(s);
                else dropped++;
            }
            return r;
        }
    }

    /// <summary>Tìm lại part 2D của 1 solid đã lưu. <c>SourceKey</c> KHÔNG duy nhất: GE/GB tách từ 1 tấm Web dùng chung khoá
    /// với phần Web còn lại ⇒ ưu tiên part chưa dùng, cùng loại, theo thứ tự phân loại (solid được dựng đúng thứ tự đó).</summary>
    public sealed class PartMatcher<T> where T : class
    {
        private readonly Dictionary<string, List<T>> _byKey = new Dictionary<string, List<T>>();
        private readonly HashSet<T> _used = new HashSet<T>();
        private readonly Func<T, string> _typeOf;

        public PartMatcher(IEnumerable<T> parts, Func<T, string> keyOf, Func<T, string> typeOf)
        {
            _typeOf = typeOf;
            foreach (T p in parts)
            {
                string key = keyOf(p);
                if (string.IsNullOrEmpty(key)) continue;
                if (!_byKey.TryGetValue(key, out var list)) _byKey[key] = list = new List<T>();
                list.Add(p);
            }
        }

        /// <summary>Part của solid; null = khoá không còn trong bản phân loại.</summary>
        public T Take(string key, string type)
        {
            if (key == null || !_byKey.TryGetValue(key, out var list)) return null;
            T hit = list.Find(p => !_used.Contains(p) && _typeOf(p) == type)
                    ?? list.Find(p => !_used.Contains(p))
                    ?? list[0];
            _used.Add(hit);
            return hit;
        }
    }
}
