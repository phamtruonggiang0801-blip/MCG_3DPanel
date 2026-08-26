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
        public bool Kept { get; set; }
        public System.Collections.Generic.List<string> Report { get; } = new System.Collections.Generic.List<string>();

        /// <summary>Từng part đã dựng hay bị bỏ qua — dùng để xuất report JSON đọc lại kết quả build.</summary>
        public System.Collections.Generic.List<BuiltPartInfo> Parts { get; } = new System.Collections.Generic.List<BuiltPartInfo>();
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
