using System.Collections.Generic;
using MCG_3DPanel.Services.Detail;

namespace MCG_3DPanel.Services.Update
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.*.

    /// <summary>Một entity 2D trong định nghĩa block chứa nó, toạ độ CỦA CHÍNH định nghĩa đó (spec 2026-11-20 update-after-dc-block).
    /// Polyline: <see cref="Points"/> = đỉnh, <see cref="Bulges"/> cùng số lượng, <see cref="Closed"/>. Line: đầu, cuối.
    /// Circle: tâm, Scalars = r. Arc: tâm, Scalars = r, góc đầu, góc cuối. BlockReference: điểm chèn, Scalars = góc xoay, sx, sy,
    /// <see cref="Name"/> = tên block hiệu dụng. Loại khác: Points = góc min, góc max của extents.</summary>
    public sealed class ShapeRecord
    {
        public string Handle = "", Block = "", Type = "", Layer = "", Name = "";
        public bool Closed;
        public List<P2> Points = new List<P2>();
        public List<double> Bulges = new List<double>();
        public List<double> Scalars = new List<double>();
    }

    /// <summary>Kết quả so khớp: handle cũ → handle mới; độ lệch base point của các định nghĩa đã dời (≠ 0).</summary>
    public sealed class MatchResult
    {
        public Dictionary<string, string> Map = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        public List<(string Block, P2 Offset)> Shifts = new List<(string Block, P2 Offset)>();
    }
}
