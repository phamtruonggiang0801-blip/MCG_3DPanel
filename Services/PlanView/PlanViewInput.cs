using System.Collections.Generic;

namespace MCG_3DPanel.Services.PlanView
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>1 đoạn thanh còn sống (từ tab Naming), toạ độ block 2D. Kind "Web" | "HP" | "FB" | "Flange"; Dir "T" | "L".</summary>
    public sealed class PvMember
    {
        /// <summary>Số tuyến (dòng tab Naming); đoạn của tuyến gộp chung 1 số. Nhãn: 1 cái mỗi tuyến (nhiều tuyến cùng "BS").</summary>
        public int Line { get; set; }
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
        /// <summary>Hướng bầu HP (đơn vị, hệ block 2D); (0,0) với Web/FB/Flange. Mặt cắt lật block profile khi bầu ngược trục.</summary>
        public double BellyX { get; set; }
        public double BellyY { get; set; }
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
        /// <summary>Chữ ghi đè của nháp ("" = giá trị đo) + override DIMTMOVE=1 / mũi tên xiên — bản chép giữ nguyên.</summary>
        public string Text { get; set; } = "";
        public bool Tmove { get; set; } = true;
        public bool Oblique { get; set; }
        /// <summary>Điểm đường kích thước của nháp (hệ block) — kích thước "cục bộ" (chồng lên nháp dài hơn cùng trục) giữ nguyên chỗ này.</summary>
        public double LineX { get; set; }
        public double LineY { get; set; }
    }

    /// <summary>Ký hiệu chữ (B, S, GE…, GB…, OB…) tại điểm đánh dấu. (DirX, DirY) = hướng đẩy chữ ra khỏi thanh (đơn vị; 0,0 = không đẩy).</summary>
    public sealed class PvMark
    {
        public string Text { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double DirX { get; set; }
        public double DirY { get; set; }
        /// <summary>Mã bracket (OB, OB1…): chữ đẩy ra ngoài bracket về phía mép tấm.</summary>
        public bool IsBracket { get; set; }
        /// <summary>Chữ B neo tại HP (giao điểm HP × web của tripping, hoặc đầu HP của bracket đầu HP): chữ lệch (Dir × 4.25 mm) về phía
        /// bracket dọc HP + (Perp × 3.1 mm) sang bên HP — như tờ tay 7D-17P.</summary>
        public bool AtHp { get; set; }
        public double PerpX { get; set; }
        public double PerpY { get; set; }
    }

    /// <summary>1 lỗ lashing đại diện — để ghi kích thước đường kính.</summary>
    public sealed class PvLashing
    {
        public double Diameter { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        /// <summary>Tâm mọi lỗ lashing (hệ block) — chọn lỗ ghi Ø và cột/hàng lỗ kế tiếp cho kích thước cục bộ. Rỗng ⇒ dùng (X, Y).</summary>
        public List<(double X, double Y)> Holes { get; } = new List<(double X, double Y)>();
    }

    /// <summary>Ảnh chụp dữ liệu 1 panel (đọc từ file 3D) — đầu vào của mọi quy tắc Plan View. Toạ độ block 2D.</summary>
    public sealed class PlanViewInput
    {
        public string Panel { get; set; }
        /// <summary>PanelPosition dạng int: 0 Center, 1 Port, 2 Starboard.</summary>
        public int Position { get; set; }
        public double PlateThickness { get; set; }
        public (double MinX, double MinY, double MaxX, double MaxY) TopPlate { get; set; }
        /// <summary>Đường bao Top Plate (hệ block 2D) — đường cắt giữa panel + điểm đo kích thước bao. Rỗng ⇒ dùng khung bao.</summary>
        public List<(double X, double Y)> PlateOutline { get; } = new List<(double X, double Y)>();
        public List<PvMember> Members { get; } = new List<PvMember>();
        public List<PvDraft> Drafts { get; } = new List<PvDraft>();
        public List<PvMark> Marks { get; } = new List<PvMark>();
        public PvLashing Lashing { get; set; }
        /// <summary>Bản vẽ có block &lt;panel&gt;_Lashing Hole ⇒ vẽ mặt bằng lỗ lashing + M.D1/M.D2 + tiêu đề của nó.</summary>
        public bool HasLashingHoleBlock { get; set; }
        /// <summary>Block COG trong &lt;panel&gt;_Top Plate (tâm + bán kính vòng tròn, hệ block); null = không có.</summary>
        public (double X, double Y, double R)? Cog { get; set; }
    }
}
