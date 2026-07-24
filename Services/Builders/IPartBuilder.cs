using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using MCG_3DPanel.Models;

namespace MCG_3DPanel.Services.Builders
{
    /// <summary>
    /// Dữ liệu dùng chung cho mọi builder trong 1 lần Build (transaction, kết quả phân loại,
    /// input người dùng, vị trí panel, các profile service, và dòng report để debug).
    /// Xem skill module-per-object.
    /// </summary>
    public class BuildContext
    {
        public Transaction Tr { get; set; }
        public ClassificationResult Cls { get; set; }
        public PanelInput Input { get; set; }
        public PanelPosition Position { get; set; }

        public IProfileService Profile { get; set; }
        public IHpProfileService HpProfile { get; set; }
        public IGeProfileService GeProfile { get; set; }

        /// <summary>Dòng chẩn đoán (builder tự thêm). PanelCommands quyết định có ghi ra file hay không.</summary>
        public List<string> Report { get; } = new List<string>();
    }

    /// <summary>
    /// Hợp đồng dựng 3D cho MỘT loại đối tượng panel. Mỗi loại = 1 file builder riêng
    /// (PlateBuilder / HpBuilder / BracketBuilder…) để dễ clean-code, bảo trì, debug.
    /// </summary>
    public interface IPartBuilder
    {
        /// <summary>Tên ngắn (log/report).</summary>
        string Name { get; }

        /// <summary>Builder này có nhận dựng <paramref name="part"/> không.</summary>
        bool CanBuild(PanelPart part);

        /// <summary>Dựng solid (WCS, đã đặt đúng cao độ). null = bỏ qua/không dựng được.</summary>
        Solid3d Build(BuildContext ctx, PanelPart part);
    }
}
