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
    }
}
