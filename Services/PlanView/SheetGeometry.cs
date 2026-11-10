using System;
using System.Collections.Generic;

namespace MCG_3DPanel.Services.PlanView
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>Tỉ lệ tờ, khung A1 chứa panel, đổi toạ độ block 2D → Model Space.</summary>
    public static class SheetGeometry
    {
        /// <summary>Khung A1 chèn với scale = tỉ lệ tờ (50, 75…); scale âm (lật) vẫn là cùng tỉ lệ.</summary>
        public static int ScaleFromFrame(double frameScaleX) => (int)Math.Round(Math.Abs(frameScaleX));

        /// <summary>Khung (bao WCS) chứa điểm chèn _Assy; nhiều khung chứa ⇒ khung nhỏ nhất; không có ⇒ null.</summary>
        public static int? PickFrame(IReadOnlyList<(double MinX, double MinY, double MaxX, double MaxY, double Scale)> frames,
            double assyX, double assyY)
        {
            int? best = null;
            double bestArea = double.MaxValue;
            for (int i = 0; i < frames.Count; i++)
            {
                var f = frames[i];
                if (assyX < f.MinX || assyX > f.MaxX || assyY < f.MinY || assyY > f.MaxY) continue;
                double area = (f.MaxX - f.MinX) * (f.MaxY - f.MinY);
                if (area < bestArea) { bestArea = area; best = i; }
            }
            return best;
        }

        /// <summary>Affine 2D [a, b, c, d, tx, ty]: x' = a·x + b·y + tx, y' = c·x + d·y + ty.</summary>
        public static (double X, double Y) ToSheet(double bx, double by, double[] t)
            => (t[0] * bx + t[1] * by + t[4], t[2] * bx + t[3] * by + t[5]);

        public static double ModelHeight(double paperMm, int scale) => paperMm * scale;
    }
}
