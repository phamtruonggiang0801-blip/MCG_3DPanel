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

        /// <summary>Lỗi/cảnh báo người dùng PHẢI thấy (1 chi tiết phụ dựng hỏng, khoét lỗi…) — tab Result hiện số lượng
        /// trên thanh trạng thái, report JSON liệt kê. KHÔNG catch im lặng ở builder: gọi <see cref="Warn"/> (ux-guardrails).</summary>
        public List<string> Warnings { get; } = new List<string>();

        /// <summary>Ghi 1 cảnh báo: vào cả <see cref="Report"/> lẫn <see cref="Warnings"/>.</summary>
        public void Warn(string message)
        {
            Report.Add("⚠ " + message);
            Warnings.Add(message);
        }

        /// <summary>Solid PHỤ builder sinh kèm part đang dựng (vd flange GE/GB) — đã đặt đúng chỗ + Layer/Color.
        /// Build3DService gom vào block ngay sau part đó rồi xoá danh sách.</summary>
        public List<ExtraSolid> Extras { get; } = new List<ExtraSolid>();
    }

    /// <summary>1 solid phụ độc lập (không union vào solid chính).</summary>
    public class ExtraSolid
    {
        public Solid3d Solid { get; set; }
        /// <summary>Loại cho tab Result/report, vd "GirderFlange".</summary>
        public string Type { get; set; }
        /// <summary>Code của part sinh ra nó, vd "GE-HP" / "GB-FB".</summary>
        public string Code { get; set; }
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
