using System;
using System.Collections.Generic;

namespace MCG_3DPanel.Commands
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>
    /// Quét chọn trong Pick (kỹ sư 2026-11-05) như AutoCAD: kéo trái→phải = Window (chi tiết nằm TRỌN trong khung),
    /// phải→trái = Crossing (chi tiết CHẠM khung). Toạ độ trên mặt phẳng nhìn (DCS); solid so theo cạnh thật, polyline 2D theo hộp bao.
    /// </summary>
    public static class PickWindow
    {
        /// <summary>Góc 2 ở bên TRÁI góc 1 (theo màn hình) ⇒ Crossing.</summary>
        public static bool IsCrossing(double firstX, double secondX) => secondX < firstX;

        /// <summary>Hộp [bx0..bx1]×[by0..by1] có bị chọn bởi khung (wx0,wy0)-(wx1,wy1) không (góc theo thứ tự bất kỳ).</summary>
        public static bool Hit(double wx0, double wy0, double wx1, double wy1, bool crossing,
            double bx0, double by0, double bx1, double by1)
        {
            double l = Math.Min(wx0, wx1), r = Math.Max(wx0, wx1), b = Math.Min(wy0, wy1), t = Math.Max(wy0, wy1);
            double bl = Math.Min(bx0, bx1), br = Math.Max(bx0, bx1), bb = Math.Min(by0, by1), bt = Math.Max(by0, by1);
            return crossing
                ? bl <= r && br >= l && bb <= t && bt >= b
                : bl >= l && br <= r && bb >= b && bt <= t;
        }

        /// <summary>So khung với CẠNH THẬT đã chiếu (như AutoCAD chọn khung): Window = mọi cạnh trọn trong khung; Crossing = có cạnh
        /// cắt hoặc nằm trong khung. Khung nằm giữa 1 mặt, không chạm cạnh nào ⇒ KHÔNG chọn (top plate phủ cả panel).</summary>
        public static bool HitEdges(double wx0, double wy0, double wx1, double wy1, bool crossing,
            IReadOnlyList<(double X0, double Y0, double X1, double Y1)> edges)
        {
            if (edges == null || edges.Count == 0) return false;
            double l = Math.Min(wx0, wx1), r = Math.Max(wx0, wx1), b = Math.Min(wy0, wy1), t = Math.Max(wy0, wy1);
            foreach (var e in edges)
            {
                if (crossing) { if (Clips(e.X0, e.Y0, e.X1, e.Y1, l, b, r, t)) return true; }
                else if (!In(e.X0, e.Y0, l, b, r, t) || !In(e.X1, e.Y1, l, b, r, t)) return false;
            }
            return !crossing;
        }

        private static bool In(double x, double y, double l, double b, double r, double t) => x >= l && x <= r && y >= b && y <= t;

        /// <summary>Đoạn thẳng có phần nằm trong hình chữ nhật (Liang–Barsky).</summary>
        private static bool Clips(double x0, double y0, double x1, double y1, double l, double b, double r, double t)
        {
            double t0 = 0, t1 = 1, dx = x1 - x0, dy = y1 - y0;
            bool Side(double p, double q)
            {
                if (p == 0) return q >= 0;
                double u = q / p;
                if (p < 0) { if (u > t1) return false; if (u > t0) t0 = u; }
                else { if (u < t0) return false; if (u < t1) t1 = u; }
                return true;
            }
            return Side(-dx, x0 - l) && Side(dx, r - x0) && Side(-dy, y0 - b) && Side(dy, t - y0);
        }
    }
}
