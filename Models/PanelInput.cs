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

        // FB: CHỈ chiều cao (kỹ sư 2026-11-05 — chiều dày theo 2D), lưu dạng số "120"; vẫn đọc spec cũ "120*6".
        // HP: quy cách chuẩn "cao*dày" (chọn từ HP.csv).
        public string FlatBarSpec { get; set; } = "120";
        public string HpSpec { get; set; } = "120*6";

        // Chiều cao (extrude) suy từ spec; chiều dày lấy theo Plan nên chỉ dùng để kiểm tra.
        public double FlatBarHeight => ParseHeight(FlatBarSpec);
        public double HpHeight => ParseHeight(HpSpec);

        private static double ParseHeight(string spec) => TryParseHeight(spec, out double h) ? h : 0;

        /// <summary>Chiều cao từ 1 SỐ ("120", "100.5") hoặc spec cũ "cao*dày" ("120*6" / "120x6" / "120×6" — chỉ lấy
        /// chiều cao). Văn hoá Invariant rồi văn hoá hiện tại (dấu thập phân phẩy). Trả false nếu không phải số &gt; 0.</summary>
        public static bool TryParseHeight(string s, out double height)
        {
            height = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            string t = s.Trim();
            if (TryNum(t, out height)) return height > 0;
            return TryParseSpec(t, out height, out _);
        }

        /// <summary>"120*6" → "120 × 6" (hiển thị; giữ nguyên chuỗi khác).</summary>
        public static string FormatSpec(string spec)
            => TryParseSpec(spec, out double h, out double t) ? $"{Fmt(h)} × {Fmt(t)}" : (spec ?? "");

        public static string Fmt(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        private static bool TryNum(string s, out double v)
            => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)
               || double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out v);

        /// <summary>
        /// Parse quy cách "cao*dày" (chấp nhận *, x, X, ×). Trả false nếu không hợp lệ.
        /// Ví dụ: "120*6" => height=120, thickness=6.
        /// </summary>
        public static bool TryParseSpec(string spec, out double height, out double thickness)
        {
            height = 0;
            thickness = 0;
            if (string.IsNullOrWhiteSpace(spec)) return false;

            string[] parts = spec.Replace('x', '*').Replace('X', '*').Replace('×', '*').Split('*');
            if (parts.Length != 2) return false;

            bool okH = double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out height);
            bool okT = double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out thickness);
            return okH && okT && height > 0 && thickness > 0;
        }
    }
}
