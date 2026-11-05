using System;

namespace MCG_3DPanel.Commands
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>
    /// Quét chọn trong Pick (kỹ sư 2026-11-05) như AutoCAD: kéo trái→phải = Window (chi tiết nằm TRỌN trong khung),
    /// phải→trái = Crossing (chi tiết CHẠM khung). Toạ độ trên mặt phẳng nhìn (DCS); chi tiết = hộp bao đã chiếu.
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
    }
}
