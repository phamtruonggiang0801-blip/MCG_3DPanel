using System;
using System.Globalization;

namespace MCG_3DPanel.Services.Detail
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.*.

    /// <summary>Tấm bị mặt cắt cắt qua.</summary>
    public sealed class CutMember
    {
        public string Type;
        /// <summary>Khoảng solid dọc hướng nhìn, tương đối so với mặt phẳng cắt.</summary>
        public double MinSigned, MaxSigned;
        public double Thickness;
        /// <summary>Tâm vết cắt và trục dọc chiều cao tấm, toạ độ section.</summary>
        public P2 CutCenter, CutAxis;
    }

    /// <summary>Tấm song song với mặt cắt (dùng đặt dấu $).</summary>
    public sealed class ParallelPlate
    {
        public string Type;
        public bool Visible;
        /// <summary>Đoạn viền nhìn thấy dài nhất, toạ độ section.</summary>
        public P2 OutlineA, OutlineB;
    }

    public static class SectionSymbolRules
    {
        /// <summary>Tấm liên tục: không phải HP và nằm cả hai phía mặt phẳng cắt.</summary>
        public static bool IsContinuous(CutMember m, double tol = 1.0)
            => m.Type != "HP" && m.MinSigned < -tol && m.MaxSigned > tol;

        public static string ThicknessText(double t)
            => "(" + t.ToString("0.0", CultureInfo.InvariantCulture) + ")";

        /// <summary>Đặt ký hiệu tại tâm vết cắt, xoay vuông góc với tấm.</summary>
        public static (P2 at, double rotation) ContinuousPlacement(CutMember m)
        {
            double r = Math.Atan2(m.CutAxis.Y, m.CutAxis.X) + Math.PI / 2;
            double twoPi = 2 * Math.PI;
            r %= twoPi;
            if (r < 0) r += twoPi;
            return (m.CutCenter, r);
        }

        /// <summary>Dấu $: trung điểm A–B đẩy gap sang bên phải chiều A→B; null nếu tấm ẩn.</summary>
        public static P2? WeldTracePlacement(ParallelPlate p, double gap = 15)
        {
            if (!p.Visible) return null;
            P2 mid = (p.OutlineA + p.OutlineB) * 0.5;
            P2 d = p.OutlineB - p.OutlineA;
            double len = d.Length;
            if (len < 1e-9) return mid;
            P2 right = new P2(d.Y / len, -d.X / len);
            return mid + right * gap;
        }
    }
}
