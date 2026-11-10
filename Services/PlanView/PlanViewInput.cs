using System.Collections.Generic;

namespace MCG_3DPanel.Services.PlanView
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>1 đoạn thanh còn sống (từ tab Naming), toạ độ block 2D. Kind "Web" | "HP" | "FB" | "Flange"; Dir "T" | "L".</summary>
    public sealed class PvMember
    {
        public string Name { get; set; }
        public string Kind { get; set; }
        public string Dir { get; set; }
        public double X0 { get; set; }
        public double Y0 { get; set; }
        public double X1 { get; set; }
        public double Y1 { get; set; }
        /// <summary>Web/HP/FB: chiều dày · Flange: BỀ RỘNG.</summary>
        public double Thickness { get; set; }
        /// <summary>Web: chiều cao · HP/FB: chiều cao profile · Flange: chiều dày.</summary>
        public double Height { get; set; }
        public string Spec { get; set; }
        /// <summary>Block profile cho mặt cắt ("HP120x6", "BS 80x6"); null với Web/Flange.</summary>
        public string ProfileBlock { get; set; }
        public bool Deleted { get; set; }
    }

    /// <summary>Kích thước nháp AM_9. Source "LashingHole" | "Structure". Rotation = hướng đo.</summary>
    public sealed class PvDraft
    {
        public string Source { get; set; }
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
        public double Rotation { get; set; }
    }

    /// <summary>Ký hiệu chữ (B, S, GE…, GB…, OB…) tại điểm đánh dấu. (DirX, DirY) = hướng đẩy chữ ra khỏi thanh (đơn vị; 0,0 = không đẩy).</summary>
    public sealed class PvMark
    {
        public string Text { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double DirX { get; set; }
        public double DirY { get; set; }
    }

    /// <summary>1 lỗ lashing đại diện — để ghi kích thước đường kính.</summary>
    public sealed class PvLashing
    {
        public double Diameter { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
    }

    /// <summary>Ảnh chụp dữ liệu 1 panel (đọc từ file 3D) — đầu vào của mọi quy tắc Plan View. Toạ độ block 2D.</summary>
    public sealed class PlanViewInput
    {
        public string Panel { get; set; }
        /// <summary>PanelPosition dạng int: 0 Center, 1 Port, 2 Starboard.</summary>
        public int Position { get; set; }
        public double PlateThickness { get; set; }
        public (double MinX, double MinY, double MaxX, double MaxY) TopPlate { get; set; }
        public List<PvMember> Members { get; } = new List<PvMember>();
        public List<PvDraft> Drafts { get; } = new List<PvDraft>();
        public List<PvMark> Marks { get; } = new List<PvMark>();
        public PvLashing Lashing { get; set; }
    }
}
