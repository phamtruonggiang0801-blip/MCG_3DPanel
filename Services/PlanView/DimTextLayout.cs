using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace MCG_3DPanel.Services.PlanView
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>Chữ kích thước không lọt giữa 2 đường gióng (chuỗi bước dày — tờ 7D-17C: 350, 395, 470…): mặc định AutoCAD đẩy chữ ra
    /// NGOÀI dọc đường kích thước ⇒ đè chữ kích thước bên cạnh (67 cặp chồng chữ ở lần chạy 2). Tờ tay: DIMTMOVE = 1, chữ ở giữa khoảng,
    /// nâng ra phía ngoài (phía đường kích thước so với điểm đo) 4.1–6.5 mm. Quy tắc: chữ chật ⇒ giữa khoảng, nâng 6.5 mm; 2 chữ nâng
    /// liền nhau còn chạm ⇒ chữ sau lên tầng 2 (+3.8 mm) — so le, không chồng.</summary>
    public static class DimTextLayout
    {
        public const double TextHeight = 3.0;     // DIMTXT của AM_ISO$0 (mm giấy)
        public const double CharWidth = 0.8;      // × cao chữ, font STANDARD
        public const double Lift = 6.5;           // mm giấy: tâm chữ tầng 1 (trên hàng chữ "vừa khoảng" của AutoCAD ở ~2.5 — tờ tay 6.0–6.5)
        public const double Step = 3.8;           // mm giấy: tầng 2 cao hơn tầng 1 (cao chữ 3 + khe 0.8)
        public const double Clear = 0.8;          // mm giấy: khe tối thiểu giữa 2 chữ cùng tầng

        public static void Arrange(IEnumerable<PvDim> dims, int scale)
        {
            var rows = dims.Where(d => d.Kind == "Rotated" && (IsH(d) || IsV(d)))
                           .GroupBy(d => (d.Space, IsH(d), Math.Round((IsH(d) ? d.LineY : d.LineX) / scale, 1), Out(d)));
            foreach (var row in rows)
            {
                bool h = row.Key.Item2;
                var end = new[] { 0.0, double.NegativeInfinity, double.NegativeInfinity };   // mép cuối chữ đã đặt ở tầng 1, 2
                foreach (var d in row.OrderBy(d => Mid(d, h)))
                {
                    double span = Math.Abs(h ? d.X2 - d.X1 : d.Y2 - d.Y1) / scale;
                    double w = Width(d);
                    if (span >= w) continue;   // lọt ⇒ để AutoCAD đặt trong khoảng
                    double c = Mid(d, h) / scale, start = c - w / 2;
                    int level = start >= end[1] + Clear ? 1 : start >= end[2] + Clear ? 2 : (end[1] <= end[2] ? 1 : 2);
                    end[level] = c + w / 2;
                    double lift = (Lift + (level - 1) * Step) * scale * row.Key.Item4;
                    if (h) { d.TextX = Mid(d, true); d.TextY = d.LineY + lift; }
                    else { d.TextX = d.LineX + lift; d.TextY = Mid(d, false); }
                }
            }
        }

        /// <summary>Bề rộng chữ (mm giấy) theo chuỗi hiển thị: "&lt;&gt;" = giá trị đo (làm tròn 0.1, bỏ ".0"), bỏ mã định dạng MText.</summary>
        public static double Width(PvDim d)
        {
            double meas = Math.Abs(IsH(d) ? d.X2 - d.X1 : d.Y2 - d.Y1);
            string v = Math.Round(meas, 1).ToString("0.#", CultureInfo.InvariantCulture);
            string t = string.IsNullOrEmpty(d.TextOverride) ? v : d.TextOverride.Replace("<>", v);
            t = Regex.Replace(t, @"\[A-Za-z][^;]*;|[{}]|%%[a-zA-Z]", "");
            return t.Length * TextHeight * CharWidth;
        }

        private static bool IsH(PvDim d) => Math.Abs(Math.Sin(d.Rotation)) < 0.01;
        private static bool IsV(PvDim d) => Math.Abs(Math.Cos(d.Rotation)) < 0.01;
        private static double Mid(PvDim d, bool h) => h ? (d.X1 + d.X2) / 2 : (d.Y1 + d.Y2) / 2;
        /// <summary>Phía ngoài: từ điểm đo về phía đường kích thước (+1 / −1).</summary>
        private static int Out(PvDim d) => IsH(d) ? (d.LineY >= Math.Max(d.Y1, d.Y2) - 1e-6 ? 1 : -1) : (d.LineX >= Math.Max(d.X1, d.X2) - 1e-6 ? 1 : -1);
    }
}
