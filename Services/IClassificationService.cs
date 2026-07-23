using Autodesk.AutoCAD.DatabaseServices;
using MCG_3DPanel.Models;

namespace MCG_3DPanel.Services
{
    /// <summary>
    /// Nhận diện các bộ phận (Top Plate/Web/Flange/Flat Bar/HP/Bracket) trong 1 Block Panel.
    /// Quy tắc: skill panel-parts-reference + bracket-identification.
    /// </summary>
    public interface IClassificationService
    {
        /// <summary>
        /// Duyệt đệ quy block, phân loại các Closed LWPolyline. Cần đang trong Transaction/lock.
        /// <paramref name="position"/> quyết định chiều đổ để chia OB/IB.
        /// </summary>
        ClassificationResult Classify(ObjectId blockRefId, PanelPosition position);
    }
}
