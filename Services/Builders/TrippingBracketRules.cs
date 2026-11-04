using System;
using System.Collections.Generic;
using System.Linq;

namespace MCG_3DPanel.Services.Builders
{
    /// <summary>
    /// Luật THUẦN (không AutoCAD — unit test được) cho <b>Tripping Bracket</b> (CAS.dump.json, kỹ sư 2026-11-04):
    /// "ADD 1 TRIPPING BRACKET BELOW HP IF 3 OR MORE CONTINUOUS STIFFENER IN BEAM SEGMENT. ADD THIS BRACKET
    /// CLOSEST TO BEAM SEGMENT MIDDLE. FOR DETAIL SEE HP BRACKET DETAIL." + hình tấm bracket B dùng chung với
    /// <see cref="HpEndBracketBuilder"/> (skill sniped-end §5a). Xem skill tripping-bracket.
    /// </summary>
    public static class TrippingBracketRules
    {
        /// <summary>2 HP cách giữa đoạn chênh ≤ giá trị này ⇒ hoà (chọn theo chiều đổ).</summary>
        public const double TIE_TOL = 1.0;
        /// <summary>Đoạn dầm cần ≥ bấy nhiêu HP liên tục xuyên qua mới thêm bracket.</summary>
        public const int MIN_HP = 3;

        // Bracket B (sniped-end §5a) — hằng số đã chốt.
        public const double R = 50.0, TOP_L = 50.0, E15 = 15.0, OFF2 = 30.0, BIG_OVERHANG = 50.0;
        public const double B90 = 0.41421356;   // |bulge| cung 90°

        /// <summary>Các ĐOẠN dầm = khoảng giữa 2 điểm cắt LIÊN TIẾP (dọc trục run). Điểm cắt = chỗ run bị 1 dầm
        /// liên tục có flange ngắt. Chỉ đoạn có CẢ 2 đầu là điểm cắt (R2) — mút run tự do không tạo đoạn. Cắt
        /// trùng (≤ <see cref="TIE_TOL"/>) gộp làm 1.</summary>
        public static List<(double lo, double hi)> Segments(IReadOnlyList<double> cuts)
        {
            var c = (cuts ?? new List<double>()).OrderBy(x => x).ToList();
            var u = new List<double>();
            foreach (var x in c) if (u.Count == 0 || x - u[u.Count - 1] > TIE_TOL) u.Add(x);
            var res = new List<(double, double)>();
            for (int i = 1; i < u.Count; i++) res.Add((u[i - 1], u[i]));
            return res;
        }

        /// <summary>Chỉ số HP (trong <paramref name="s"/> = vị trí giao dọc trục dầm) gần <paramref name="mid"/>
        /// nhất; null nếu &lt; <see cref="MIN_HP"/>. Hoà (≤ <see cref="TIE_TOL"/>) ⇒ HP về phía dấu
        /// <paramref name="tieSign"/> (chiều đổ chiếu lên trục dầm).</summary>
        public static int? PickHp(IReadOnlyList<double> s, double mid, double tieSign)
        {
            if (s == null || s.Count < MIN_HP) return null;
            int best = 0;
            for (int i = 1; i < s.Count; i++)
            {
                double di = Math.Abs(s[i] - mid), db = Math.Abs(s[best] - mid);
                if (di < db - TIE_TOL) best = i;
                else if (Math.Abs(di - db) <= TIE_TOL && Math.Sign(s[i] - mid) == Math.Sign(tieSign) && tieSign != 0)
                    best = i;
            }
            return best;
        }

        /// <summary>Phía web dầm đặt bracket: +1 = theo pháp tuyến n, −1 = ngược. Theo chiều đổ (<c>doz·n ≥ 0</c>
        /// ⇒ +1); <paramref name="flip"/> (tab Result) ⇒ đảo.</summary>
        public static int Side(double dozDotN, bool flip)
        {
            int s = dozDotN >= 0 ? 1 : -1;
            return flip ? -s : s;
        }

        /// <summary>Selector bracket B theo phần flange nhô (<paramref name="overhang"/>, đo từ mặt web dầm phía
        /// bracket): ≥ 50 ⇒ brkt_1 (<c>d1 = clamp(overhang−15, 15, reach−15)</c>, đáy y=0); ngược lại brkt_2
        /// (<c>d1 = 15</c>, đáy nâng 30).</summary>
        public static (bool big, double d1, double baseY) SelectBracketB(double overhang, bool haveFlange, double reach)
        {
            bool big = haveFlange && overhang >= BIG_OVERHANG;
            double d1 = big ? Math.Min(Math.Max(E15, overhang - E15), reach - E15) : E15;
            return (big, d1, big ? 0.0 : OFF2);
        }

        /// <summary>Polygon bracket B (6 đỉnh, local x = ra xa web, y = lên từ mặt trên flange) — THỨ TỰ y hệt
        /// bản gốc <c>HpEndBracketBuilder.Build</c>: cung ¼ R lõm từ (0, Ht−R) lên (topL, Ht).</summary>
        public static List<(double X, double Y, double Bulge)> BracketBPolygon(double reach, double Ht, double d1, double baseY)
            => new List<(double X, double Y, double Bulge)>
            {
                (reach, Ht - E15, 0),
                (d1,    baseY,    0),
                (0,     baseY,    0),
                (0,     Ht - R,   B90),
                (TOP_L, Ht,       0),
                (reach, Ht,       0),
            };
    }
}
