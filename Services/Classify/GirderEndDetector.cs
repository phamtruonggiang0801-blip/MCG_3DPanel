using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using MCG_3DPanel.Models;

namespace MCG_3DPanel.Services.Classify
{
    /// <summary>
    /// BƯỚC 2b — Tách GIRDER END (GE) khỏi nhóm "Bracket-candidate".
    /// GE = mảnh AM_0 ngắn chạm HP/FB **collinear + LIÊN TỤC với Web chính**
    /// (nối thẳng, khe &lt; tol; KHÔNG bị web vuông góc chặn). Xem skill girder-end.
    ///
    /// Chạy SAU <see cref="WebBracketSplitter"/> (mảnh đang là Bracket) và TRƯỚC <see cref="BracketCoder"/>.
    /// Mảnh nào là GE → đổi Type=GirderEnd + Code "GE-HP"/"GE-FB" (builder tinh chỉnh GE1/2/5/6 theo D).
    ///
    /// GB (collinear-bị-ngắt + L &gt; HPmaxspan) HIỆN CHƯA làm — để nguyên là Bracket.
    /// </summary>
    internal static class GirderEndDetector
    {
        private const double CONT_TOL = 3.0;   // khe tới web chính < tol ⇒ "liên tục" (không bị web vuông góc chặn)

        public static void Apply(ClassificationResult result, List<PanelPart> hpParts, List<PanelPart> fbParts)
        {
            var webs = result.OfType(PartType.Web)
                             .Where(w => w.MemberLength > ClassifyUtil.WEB_MIN_LEN).ToList();
            if (webs.Count == 0) return;

            foreach (var p in result.OfType(PartType.Bracket).ToList())
            {
                if (!IsContinuousWithWeb(p, webs)) continue;

                bool hp = hpParts.Any(h => ClassifyUtil.Adjacent(p, h, ClassifyUtil.ADJ_TOL));
                bool fb = fbParts.Any(f => ClassifyUtil.Adjacent(p, f, ClassifyUtil.ADJ_TOL));
                if (!hp && !fb) continue;

                p.Type = PartType.GirderEnd;
                p.Code = hp ? "GE-HP" : "GE-FB";
            }
        }

        /// <summary>Có 1 Web chính CÙNG PHƯƠNG, tim ngang trùng, và **liền** mảnh (khe &lt; CONT_TOL).</summary>
        private static bool IsContinuousWithWeb(PanelPart p, List<PanelPart> webs)
        {
            Extents3d pb = p.Bounds;
            foreach (var w in webs)
            {
                if (w.IsHorizontal != p.IsHorizontal) continue;
                double perp = Math.Abs(p.PerpCenter - w.PerpCenter);
                if (perp > (p.FootprintThickness + w.FootprintThickness) / 2 + 8) continue;

                Extents3d wb = w.Bounds;
                double gap = p.IsHorizontal
                    ? Math.Max(Math.Max(wb.MinPoint.X - pb.MaxPoint.X, pb.MinPoint.X - wb.MaxPoint.X), 0)
                    : Math.Max(Math.Max(wb.MinPoint.Y - pb.MaxPoint.Y, pb.MinPoint.Y - wb.MaxPoint.Y), 0);
                if (gap < CONT_TOL) return true;
            }
            return false;
        }

        /// <summary>
        /// HPmaxspan tự tính (KHÔNG cần user nhập): khoảng cách LỚN NHẤT giữa các đường HP song song liền kề,
        /// chỉ xét nhóm phương có ≥3 đường (bỏ cặp biên 2-HP). Dùng cho GB (giai đoạn sau). 0 nếu không đủ dữ liệu.
        /// </summary>
        public static double HpMaxSpan(List<PanelPart> hpParts)
        {
            double Max(IEnumerable<double> centers)
            {
                var lines = new List<double>();
                foreach (var c in centers.OrderBy(x => x))
                    if (lines.Count == 0 || Math.Abs(c - lines[lines.Count - 1]) > 50) lines.Add(c);
                if (lines.Count < 3) return 0;              // <3 đường → không đủ để có "span" (bỏ biên)
                double m = 0;
                for (int i = 1; i < lines.Count; i++) m = Math.Max(m, lines[i] - lines[i - 1]);
                return m;
            }
            var longHp = hpParts.Where(h => h.MemberLength > ClassifyUtil.HP_MIN_LEN).ToList();
            double h = Max(longHp.Where(x => x.IsHorizontal).Select(x => x.Centroid.Y));
            double v = Max(longHp.Where(x => !x.IsHorizontal).Select(x => x.Centroid.X));
            return Math.Max(h, v);
        }
    }
}
