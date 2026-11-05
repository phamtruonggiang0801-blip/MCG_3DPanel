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
        // ─── Phục vụ rebuild từ tab Result (2026-11-04) ───
        /// <summary>Block reference 2D nguồn (Keep xoá nó — rebuild mở lại bằng openErased).</summary>
        public ObjectId SourceRefId { get; set; }
        /// <summary>ĐỊNH NGHĨA block 2D nguồn — nơi lưu correction (XRecord) và dùng để chèn tạm khi rebuild sau mở lại.</summary>
        public ObjectId SourceBtrId { get; set; }
        /// <summary>Ma trận chèn block 2D nguồn (WCS) — chèn tạm lại đúng chỗ khi block reference 2D không còn.</summary>
        public Autodesk.AutoCAD.Geometry.Matrix3d SourceTransform { get; set; }
        public PanelInput Input { get; set; }
        public PanelPosition Position { get; set; }
        /// <summary>Thời gian phân loại + dựng (giây) — hiện trên thanh trạng thái sau rebuild.</summary>
        public double BuildSeconds { get; set; }
        /// <summary>File report JSON đã ghi sau Build (không tự mở) — null nếu ghi lỗi.</summary>
        public string ReportPath { get; set; }
        public System.Collections.Generic.List<string> Report { get; } = new System.Collections.Generic.List<string>();
        /// <summary>Cảnh báo người dùng phải thấy (BuildContext.Warn) — tab Result hiện số lượng.</summary>
        public System.Collections.Generic.List<string> Warnings { get; } = new System.Collections.Generic.List<string>();

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
        /// <summary>Loại khi <see cref="Part"/> null: "CollarPlate" / "HpEndBracketB" / "GirderFlange" / "TrippingBracket".</summary>
        public string Type { get; set; }
        /// <summary>Khoá correction cho solid SINH THÊM đảo được trên tab Result (Tripping Bracket: đảo phía) —
        /// thay <see cref="PanelPart.SourceKey"/> khi <see cref="Part"/> null. null = không đảo được.</summary>
        public string FlipKey { get; set; }
        /// <summary>Code part sinh ra solid phụ (vd "GE-HP" cho GirderFlange); rỗng nếu không có.</summary>
        public string Code { get; set; }
        /// <summary>Bbox WCS SAU mọi boolean (CutOut/CO…) — block 3D chèn tại gốc, không xoay/scale.</summary>
        public Extents3d Bounds { get; set; }
        /// <summary>Thể tích solid cuối (null nếu AutoCAD không tính được) — cho report JSON.</summary>
        public double? Volume { get; set; }
        /// <summary>Code các GE/GB đã hợp nhất vào solid Web này (vd "GE-HP") — để tab Result vẫn ghi rõ GE/GB.</summary>
        public System.Collections.Generic.List<string> MergedCodes { get; } = new System.Collections.Generic.List<string>();
        /// <summary>Khoá Delete/Restore (tab Result): part = SourceKey; solid phụ = "{SourceKey}/{Type}#{i}";
        /// Collar = "CollarPlate:{tag}"; Bracket B = "HpEndBracketB:{tag}".</summary>
        public string DeleteKey { get; set; }
        /// <summary>Đã xoá theo correction Delete (solid erased, dòng vẫn hiện để Restore).</summary>
        public bool Deleted { get; set; }
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
