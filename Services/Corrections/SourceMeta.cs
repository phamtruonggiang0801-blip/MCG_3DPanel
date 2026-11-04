using System.Text.Json;
using MCG_3DPanel.Models;

namespace MCG_3DPanel.Services.Corrections
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>
    /// Metadata ghi lên BTR của block 3D (XRecord <c>MCG3D_SOURCE</c>) để rebuild được SAU KHI ĐÓNG/MỞ LẠI bản vẽ
    /// (spec 2026-11-04 §14): block reference 2D đã bị xoá khi Keep ⇒ chèn tạm định nghĩa 2D bằng ma trận này.
    /// </summary>
    public sealed class SourceMeta
    {
        public int Version { get; set; } = 1;
        /// <summary>Handle (hex) BTR định nghĩa block 2D nguồn.</summary>
        public string SourceBtrHandle { get; set; }
        /// <summary>Ma trận chèn 2D (Matrix3d.ToArray, 16 phần tử, row-major).</summary>
        public double[] Transform { get; set; }
        public PanelInput Input { get; set; }
        public PanelPosition Position { get; set; }

        public string ToJson() => JsonSerializer.Serialize(this);

        /// <summary>JSON hỏng / thiếu handle / ma trận sai kích thước ⇒ null (block 3D cũ, không rebuild được).</summary>
        public static SourceMeta FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var m = JsonSerializer.Deserialize<SourceMeta>(json);
                if (m == null || string.IsNullOrEmpty(m.SourceBtrHandle) || m.Transform == null || m.Transform.Length != 16)
                    return null;
                m.Input = m.Input ?? new PanelInput();
                return m;
            }
            catch (JsonException) { return null; }
        }
    }
}
