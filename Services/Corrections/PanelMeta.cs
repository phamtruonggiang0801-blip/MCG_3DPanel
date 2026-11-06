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
    }

    /// <summary>Dữ liệu lưu cùng mỗi panel 3D (JSON): nguồn 2D, input đã dùng, vị trí, danh sách solid.
    /// Position là int để lớp độc lập với enum. JSON hỏng/rỗng ⇒ FromJson trả null.</summary>
    public class PanelMeta
    {
        public string SourceRefHandle { get; set; } = "";
        public string SourceBtrHandle { get; set; } = "";
        public PanelInput Input { get; set; } = new PanelInput();
        public int Position { get; set; }
        public List<PanelMetaSolid> Solids { get; set; } = new List<PanelMetaSolid>();

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
}
