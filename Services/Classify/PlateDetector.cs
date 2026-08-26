using System.Collections.Generic;
using System.Linq;
using MCG_3DPanel.Models;

namespace MCG_3DPanel.Services.Classify
{
    /// <summary>
    /// Nhận diện 2 loại TẤM trên layer Web (AM_0) mà không phải Web/Bracket:
    ///  - <b>BackingPlate</b>: tấm đệm hàn TRÊN web — footprint NHỎ, nằm ĐÈ (chứa trong) 1 web dài, không đè faceplate.
    ///  - <b>ClosedPlate</b>: tấm kín chữ nhật, không dài như girder, đè lên FacePlate, KHÔNG phải tier của girder.
    /// (CutOut xử lý riêng trong ClassificationService: cùng block Top Plate + nằm hẳn trong biên.)
    /// Kiểm chứng 7D-17C (Center). Xem skill panel-parts-reference.
    /// </summary>
    internal static class PlateDetector
    {
        /// <summary>Tấm đệm: AM_0, footprint nhỏ (≤80mm), nằm ĐÈ (≥80% diện tích) HOẶC TIẾP GIÁP MÉP
        /// (Adjacent, vd tấm gắn ở ĐẦU/CẠNH web thay vì chồng lên mặt) 1 web dài, KHÔNG đè faceplate.</summary>
        public static bool IsBackingPlate(PanelPart p, List<PanelPart> faceplates, List<PanelPart> longWebs)
        {
            if (p.Layer != ClassifyUtil.LAYER_WEB) return false;
            if (ClassifyUtil.BoxMaxDim(p) > 80.0) return false;
            double area = p.Area;
            if (area < 1e-6) return false;
            bool onWeb = longWebs.Any(w => !ReferenceEquals(w, p)
                && (ClassifyUtil.OverlapArea(p, w) >= 0.8 * area || ClassifyUtil.Adjacent(p, w, ClassifyUtil.ADJ_TOL)));
            bool onFp = faceplates.Any(fp => ClassifyUtil.OverlapArea(p, fp) >= 0.3 * area);
            return onWeb && !onFp;
        }

        /// <summary>Tấm kín: AM_0 chữ nhật, len &lt; WEB_MIN_LEN, đè ≥25% faceplate, KHÔNG (strip mỏng collinear web dài = tier).
        /// Trần độ dài = WEB_MIN_LEN (không phải GE_MIN_LEN) — tấm CÔ LẬP (không nối chuỗi collinear với
        /// web nào) có thể dài hơn 1 GE thật (kiểm chứng 7D-17C: tấm 657mm); phép loại-tier bên dưới đã
        /// tự bảo vệ pieces THẬT SỰ nối chuỗi (vd 750/800mm nhưng collinear với web dài — vẫn bị loại đúng).
        /// ⚠️ CHỈ áp dụng nhánh `!touches` (xem <see cref="IsClosedPlateStrict"/> cho nhánh chạm yếu —
        /// nhánh đó PHẢI giữ trần GE_MIN_LEN, nới ra sẽ cướp nhầm GE thật, xem ClassificationService).</summary>
        public static bool IsClosedPlate(PanelPart p, List<PanelPart> faceplates, List<PanelPart> longWebs)
        {
            if (p.Layer != ClassifyUtil.LAYER_WEB) return false;
            if (!ClassifyUtil.IsRectangle(p)) return false;
            if (p.MemberLength >= ClassifyUtil.WEB_MIN_LEN) return false;

            // Loại TIER girder: strip MỎNG (bbox cạnh ngắn ≤ 50) mà collinear với web dài.
            if (ClassifyUtil.BboxMinDim(p) <= 50.0 && ClassifyUtil.CollinearWithLongWeb(p, longWebs)) return false;

            double need = 0.25 * p.Area;                                     // ≥25% footprint đè lên faceplate
            return faceplates.Any(fp => ClassifyUtil.OverlapArea(p, fp) >= need);
        }

        /// <summary>
        /// Biến thể NGHIÊM NGẶT của <see cref="IsClosedPlate"/> — dùng cho mảnh CÓ vẻ "chạm" HP/FB
        /// nhưng chạm đó KHÔNG đáng tin (<see cref="ClassifyUtil.HasOnlyWeakTouch"/>): đòi hỏi p nằm
        /// GẦN NHƯ TRỌN (≥90%) trong 1 FacePlate THẬT — đa giác thật (<see cref="ClassifyUtil.PolygonOverlapFraction"/>),
        /// KHÔNG phải bbox (bbox FacePlate loe rộng hơn nhiều hình dạng thật — 25% bbox của
        /// <see cref="IsClosedPlate"/> sẽ cướp nhầm rất nhiều Bracket thật đang ở gần 1 FacePlate).
        /// Kiểm chứng 7D-17C: đúng 5/5 ClosedPlate thật (đều 100%), không cướp nhầm ~24 Bracket khác
        /// đang overlap bbox 14–57% với FacePlate gần đó.
        /// </summary>
        public static bool IsClosedPlateStrict(PanelPart p, List<PanelPart> faceplates)
        {
            if (p.Layer != ClassifyUtil.LAYER_WEB) return false;
            if (!ClassifyUtil.IsRectangle(p)) return false;
            if (p.MemberLength >= ClassifyUtil.GE_MIN_LEN) return false;

            const double OVERLAP_NEED = 0.90;
            return faceplates.Any(fp => ClassifyUtil.PolygonOverlapFraction(p, fp) >= OVERLAP_NEED);
        }
    }
}
