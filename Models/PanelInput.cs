using System.Globalization;

namespace MCG_3DPanel.Models
{
    /// <summary>
    /// Thông số người dùng nhập (chung cho cả Panel). POCO thuần, không phụ thuộc AutoCAD.
    /// Đơn vị: mm. Quy tắc dùng: xem skill panel-parts-reference.
    /// </summary>
    public class PanelInput
    {
        public double TopPlateThickness { get; set; } = 6;
        public double WebHeight { get; set; } = 374;
        public double FlangeThickness { get; set; } = 20;

        // Quy cách "cao*dày" (vd "120*6") — thô, chưa parse.
        public string FlatBarSpec { get; set; } = "120*6";
        public string HpSpec { get; set; } = "120*6";

        // Chiều cao (extrude) suy từ spec; chiều dày lấy theo Plan nên chỉ dùng để kiểm tra.
        public double FlatBarHeight => ParseHeight(FlatBarSpec);
        public double HpHeight => ParseHeight(HpSpec);

        private static double ParseHeight(string spec)
        {
            TryParseSpec(spec, out double height, out _);
            return height;
        }

        /// <summary>
        /// Parse quy cách "cao*dày" (chấp nhận '*', 'x', 'X'). Trả false nếu không hợp lệ.
        /// Ví dụ: "120*6" => height=120, thickness=6.
        /// </summary>
        public static bool TryParseSpec(string spec, out double height, out double thickness)
        {
            height = 0;
            thickness = 0;
            if (string.IsNullOrWhiteSpace(spec)) return false;

            string[] parts = spec.Replace('x', '*').Replace('X', '*').Split('*');
            if (parts.Length != 2) return false;

            bool okH = double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out height);
            bool okT = double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out thickness);
            return okH && okT && height > 0 && thickness > 0;
        }
    }
}
