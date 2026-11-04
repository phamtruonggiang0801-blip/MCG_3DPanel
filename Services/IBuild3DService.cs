using Autodesk.AutoCAD.DatabaseServices;
using MCG_3DPanel.Models;

namespace MCG_3DPanel.Services
{
    /// <summary>Dựng 3D Solid từ kết quả phân loại + thông số người dùng.</summary>
    public interface IBuild3DService
    {
        /// <summary>
        /// Dựng các Solid3d (Region→Extrude→Move theo bảng Z), gom vào 1 Block 3D mới,
        /// chèn BlockReference vào Model Space. Tự quản lý Transaction.
        /// <paramref name="position"/> quyết định chiều đổ (hướng gấp lip bracket).
        /// </summary>
        BuildResult Build(Database db, ClassificationResult classification, PanelInput input, PanelPosition position);
    }

    /// <summary>Kết quả build để preview/undo.</summary>
    public class BuildResult
    {
        public ObjectId BlockRefId { get; set; }   // BlockReference 3D vừa chèn
        public ObjectId BtrId { get; set; }         // định nghĩa block 3D
        public int SolidCount { get; set; }
        public string BlockName { get; set; }
        /// <summary>Tên block 2D nguồn (Build nhiều panel 1 lúc → để biết solid thuộc panel nào).</summary>
        public string PanelBlockName { get; set; }
        public bool Kept { get; set; }
        /// <summary>File report JSON đã ghi sau Build (không tự mở) — null nếu ghi lỗi.</summary>
        public string ReportPath { get; set; }
        public System.Collections.Generic.List<string> Report { get; } = new System.Collections.Generic.List<string>();

        /// <summary>Từng part đã dựng hay bị bỏ qua — dùng để xuất report JSON đọc lại kết quả build.</summary>
        public System.Collections.Generic.List<BuiltPartInfo> Parts { get; } = new System.Collections.Generic.List<BuiltPartInfo>();

        /// <summary>Từng solid CÒN LẠI trong block 3D (đã trừ GE hợp nhất vào Web) — nguồn của tab Result.</summary>
        public System.Collections.Generic.List<BuiltSolid> Solids { get; } = new System.Collections.Generic.List<BuiltSolid>();
    }

    /// <summary>1 solid trong block 3D: id để highlight/zoom + part nguồn để gắn nhãn loại.</summary>
    public class BuiltSolid
    {
        public ObjectId SolidId { get; set; }
        /// <summary>Part 2D nguồn; null với solid sinh thêm (Collar Plate, Bracket B đầu HP).</summary>
        public PanelPart Part { get; set; }
        /// <summary>Loại khi <see cref="Part"/> null: "CollarPlate" / "HpEndBracketB" / "GirderFlange".</summary>
        public string Type { get; set; }
        /// <summary>Code part sinh ra solid phụ (vd "GE-HP" cho GirderFlange); rỗng nếu không có.</summary>
        public string Code { get; set; }
        /// <summary>Bbox WCS SAU mọi boolean (CutOut/CO…) — block 3D chèn tại gốc, không xoay/scale.</summary>
        public Extents3d Bounds { get; set; }
        /// <summary>Thể tích solid cuối (null nếu AutoCAD không tính được) — cho report JSON.</summary>
        public double? Volume { get; set; }
        /// <summary>Code các GE/GB đã hợp nhất vào solid Web này (vd "GE-HP") — để tab Result vẫn ghi rõ GE/GB.</summary>
        public System.Collections.Generic.List<string> MergedCodes { get; } = new System.Collections.Generic.List<string>();
    }

    /// <summary>1 dòng trạng thái build của 1 <see cref="Services.PanelPart"/> — dùng cho report JSON.</summary>
    public class BuiltPartInfo
    {
        public string Handle { get; set; }
        public string Type { get; set; }
        public string Code { get; set; }
        public bool Built { get; set; }

        // Bbox + thể tích solid THẬT (null nếu không lấy được) — để chẩn đoán "built=true nhưng
        // thực ra rỗng/lệch vị trí" (VD Top Plate bị CutOut khoét lố, part build sai chỗ…).
        public double? MinX { get; set; }
        public double? MinY { get; set; }
        public double? MinZ { get; set; }
        public double? MaxX { get; set; }
        public double? MaxY { get; set; }
        public double? MaxZ { get; set; }
        public double? Volume { get; set; }
    }
}
