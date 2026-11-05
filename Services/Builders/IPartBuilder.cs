using System.Collections.Generic;
using System.Linq;
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

        /// <summary>Chiều cao của 1 FB: riêng FB đó (tab Result, PartCorrection.FbHeight) nếu đã sửa, không thì theo tab
        /// Input (kỹ sư 2026-11-05 — chiều dày luôn theo 2D). Mọi chi tiết đi theo FB đọc qua đây.</summary>
        public double FbHeight(PanelPart fb)
        {
            if (fb?.SourceKey != null && Cls?.Corrections != null
                && Cls.Corrections.TryGetValue(fb.SourceKey, out var c) && c?.FbHeight is double h && h > 0)
                return h;
            return Input.FlatBarHeight;
        }

        /// <summary>Chiều cao FB mà <paramref name="part"/> (bracket BF, GE/GB-FB) chạm ở 1 đầu — FB gần nhất trong
        /// <see cref="Classify.ClassifyUtil.ADJ_TOL"/>. Không có FB nào sửa chiều cao ⇒ trả thẳng giá trị tab Input.
        /// Không tìm thấy FB ⇒ giá trị tab Input + ghi Report.</summary>
        public double FbHeightNear(PanelPart part, string who)
        {
            if (Cls?.Corrections == null || !Cls.Corrections.Values.Any(c => c?.FbHeight != null)) return Input.FlatBarHeight;
            var (a, b) = Classify.ClassifyUtil.Endpoints(part);
            PanelPart best = null;
            double bestD = double.MaxValue;
            foreach (var fb in Cls.OfType(PartType.FlatBar))
            {
                double d = System.Math.Min(Classify.ClassifyUtil.DistToPart(a, fb), Classify.ClassifyUtil.DistToPart(b, fb));
                if (d < bestD) { bestD = d; best = fb; }
            }
            if (best == null || bestD > Classify.ClassifyUtil.ADJ_TOL)
            {
                Report.Add($"[FbHeight] {who} {part.Id.Handle}: không thấy FB chạm đầu (≤ {Classify.ClassifyUtil.ADJ_TOL:0}mm) → dùng chiều cao tab Input {Input.FlatBarHeight:0}.");
                return Input.FlatBarHeight;
            }
            return FbHeight(best);
        }

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
