using System;
using System.Collections.Generic;
using System.Linq;

namespace MCG_3DPanel.Services.Builders
{
    /// <summary>
    /// Luật THUẦN (không AutoCAD — unit test được) cho <b>Tripping Bracket</b>. Kỹ sư 2026-11-05 (test 7D-16C):
    /// khoảng cách giữa các gối chống vặn xoắn dọc 1 đoạn dầm ≤ L = 3 × bước stiffener max ⇒ đi từ gối đầu theo
    /// CHIỀU ĐỔ, chọn HP xa nhất trong L, lặp lại (Center: 2 đầu vào tâm). Hình tấm bracket B dùng chung với
    /// <see cref="HpEndBracketBuilder"/> (skill sniped-end §5a). Xem skill tripping-bracket.
    /// </summary>
    public static class TrippingBracketRules
    {
        /// <summary>2 điểm cắt cách ≤ giá trị này ⇒ gộp làm 1.</summary>
        public const double TIE_TOL = 1.0;
        /// <summary>Dung sai so khoảng cách với L (7D-16C: 5332.6 − 3007.6 = 2325 = L đúng ngưỡng).</summary>
        public const double SPACING_TOL = 1.0;
        /// <summary>2 HP kề nhau coi là song song khi |cos| với trục dầm chênh ≤ giá trị này.</summary>
        public const double PAR_TOL = 0.02;

        // Bracket B (sniped-end §5a) — hằng số đã chốt.
        public const double R = 50.0, TOP_L = 50.0, E15 = 15.0, OFF2 = 30.0, BIG_OVERHANG = 50.0;
        public const double B90 = 0.41421356;   // |bulge| cung 90°

        /// <summary>Các ĐOẠN dầm = khoảng giữa 2 gối LIÊN TIẾP dọc trục run. Gối = điểm cắt (dầm liên tục có flange
        /// ngắt run) + 2 mút run (0 và <paramref name="runLen"/>) — mút chỉ thêm khi không có điểm cắt nào trong
        /// <paramref name="endTol"/>. Gối trùng (≤ <see cref="TIE_TOL"/>) gộp làm 1.</summary>
        public static List<(double lo, double hi)> Segments(IReadOnlyList<double> cuts, double runLen, double endTol)
        {
            var c = (cuts ?? new List<double>()).ToList();
            foreach (var e in new[] { 0.0, runLen })
                if (!c.Any(x => Math.Abs(x - e) <= endTol)) c.Add(e);
            c.Sort();
            var u = new List<double>();
            foreach (var x in c) if (u.Count == 0 || x - u[u.Count - 1] > TIE_TOL) u.Add(x);
            var res = new List<(double, double)>();
            for (int i = 1; i < u.Count; i++) res.Add((u[i - 1], u[i]));
            return res;
        }

        /// <summary>Bước stiffener max = khoảng lớn nhất giữa 2 HP kề nhau trên cùng 1 đoạn, lấy max toàn panel;
        /// 0 nếu không đoạn nào có ≥ 2 HP. Mỗi HP = (s dọc trục dầm, cos = |trục dầm · pháp tuyến HP|): bước THẬT =
        /// Δs × cos (dầm xiên KHÔNG làm phình L — review 2026-11-05); cặp HP không song song (cos lệch &gt;
        /// <see cref="PAR_TOL"/>) bỏ qua.</summary>
        public static double MaxSpacing(IEnumerable<IReadOnlyList<(double s, double cos)>> segmentHps)
        {
            double m = 0;
            foreach (var l in segmentHps ?? Enumerable.Empty<IReadOnlyList<(double s, double cos)>>())
            {
                if (l == null) continue;
                var h = l.OrderBy(x => x.s).ToList();
                for (int i = 1; i < h.Count; i++)
                {
                    if (Math.Abs(h[i].cos - h[i - 1].cos) > PAR_TOL) continue;
                    m = Math.Max(m, (h[i].s - h[i - 1].s) * Math.Abs(h[i].cos + h[i - 1].cos) / 2.0);
                }
            }
            return m;
        }

        /// <summary>Chỉ số HP (trong <paramref name="s"/> tăng dần, nằm trong (lo, hi)) được đặt tripping bracket:
        /// đi từ gối, chọn HP xa nhất cách gối ≤ <paramref name="L"/> (+<see cref="SPACING_TOL"/>), HP đó thành
        /// gối mới; dừng khi khoảng tới đầu kia ≤ L. Không HP nào trong L ⇒ lấy HP kế tiếp gần nhất.
        /// <paramref name="startSign"/> ≥ 0 ⇒ đi từ lo; &lt; 0 ⇒ từ hi. <paramref name="split"/> (Center, nằm trong
        /// (lo, hi)) ⇒ nửa s ≤ split đi từ lo, nửa s &gt; split đi từ hi, mỗi nửa chỉ chọn HP của mình.</summary>
        public static List<int> PickBySpacing(IReadOnlyList<double> s, double lo, double hi, double L, int startSign, double? split)
        {
            var res = new List<int>();
            if (s == null || s.Count == 0 || L <= 0) return res;
            var all = Enumerable.Range(0, s.Count).ToList();
            if (split is double sp && sp > lo && sp < hi)
            {
                Walk(s, all.Where(i => s[i] <= sp).ToList(), lo, hi, +1, L, res);
                Walk(s, all.Where(i => s[i] > sp).ToList(), hi, lo, -1, L, res);
            }
            else if (startSign >= 0) Walk(s, all, lo, hi, +1, L, res);
            else Walk(s, all, hi, lo, -1, L, res);
            return res.Distinct().OrderBy(i => i).ToList();
        }

        private static void Walk(IReadOnlyList<double> s, List<int> cand, double cur, double end, int dir, double L, List<int> res)
        {
            double lim = L + SPACING_TOL;
            while (Math.Abs(end - cur) > lim)
            {
                double c0 = cur;
                var ahead = cand.Where(i => (s[i] - c0) * dir > 0).OrderBy(i => (s[i] - c0) * dir).ToList();
                if (ahead.Count == 0) break;
                var within = ahead.Where(i => Math.Abs(s[i] - c0) <= lim).ToList();
                int pick = within.Count > 0 ? within[within.Count - 1] : ahead[0];
                res.Add(pick);
                cur = s[pick];
            }
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
