using System.Collections.Generic;
using System.Linq;
using MCG_3DPanel.Models;

namespace MCG_3DPanel.Services.Classify
{
    /// <summary>
    /// Nhận diện 2 loại TẤM trên layer Web (AM_0) mà không phải Web/Bracket:
    ///  - <b>BackingPlate</b>: tấm đệm hàn trên web — có SNIP 2 đầu nên KHÔNG chữ nhật (6–12 đỉnh).
    ///  - <b>ClosedPlate</b>: tấm kín chữ nhật, KHÔNG quá dài, nằm ĐÈ lên một FacePlate.
    /// (CutOut xử lý riêng trong ClassificationService: cùng block Top Plate + nằm hẳn trong biên.)
    /// v1 heuristic — chờ người dùng kiểm tra để tinh chỉnh ngưỡng. Xem skill panel-parts-reference.
    /// </summary>
    internal static class PlateDetector
    {
        /// <summary>AM_0, KHÔNG chữ nhật (snip 2 đầu → 6–12 đỉnh). Web/GE ở 2D là chữ nhật nên loại được.</summary>
        public static bool IsBackingPlate(PanelPart p)
        {
            return p.Layer == ClassifyUtil.LAYER_WEB
                && p.VertexCount >= 6 && p.VertexCount <= 12
                && !ClassifyUtil.IsRectangle(p);
        }

        /// <summary>AM_0 chữ nhật, không dài như girder, chồng lấn đáng kể lên một FacePlate.</summary>
        public static bool IsClosedPlate(PanelPart p, List<PanelPart> faceplates)
        {
            if (p.Layer != ClassifyUtil.LAYER_WEB) return false;
            if (!ClassifyUtil.IsRectangle(p)) return false;
            if (p.MemberLength >= ClassifyUtil.WEB_MIN_LEN) return false;   // dài = Web, không phải tấm
            double need = 0.25 * p.Area;                                     // ≥25% footprint đè lên faceplate
            return faceplates.Any(fp => ClassifyUtil.OverlapArea(p, fp) >= need);
        }
    }
}
