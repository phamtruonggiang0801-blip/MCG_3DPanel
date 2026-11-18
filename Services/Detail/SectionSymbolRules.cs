using System;
using System.Collections.Generic;
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
        /// <summary>Chiều dài vết cắt dọc <see cref="CutAxis"/>.</summary>
        public double CutLength;
    }

    /// <summary>Tấm đứng song song với mặt cắt (nhìn thấy mặt tấm): đặt "$" + "(t)" (kỹ sư 2026-11-18).</summary>
    public sealed class ParallelPlate
    {
        public string Type;
        public bool Visible;
        /// <summary>Chiều dày (khoảng dọc hướng nhìn).</summary>
        public double Thickness;
        /// <summary>Nét đặt "$": nét đầu tấm + nét nối với tấm song song khác (nét đứng), toạ độ section.</summary>
        public List<(P2 A, P2 B)> Marks = new List<(P2 A, P2 B)>();
        /// <summary>Khung bao mặt tấm, toạ độ section (đặt "(t)").</summary>
        public P2 FaceMin, FaceMax;
    }

    public static class SectionSymbolRules
    {
        /// <summary>Tấm liên tục: không phải HP và nằm cả hai phía mặt phẳng cắt.</summary>
        public static bool IsContinuous(CutMember m, double tol = 1.0)
            => m.Type != "HP" && m.MinSigned < -tol && m.MaxSigned > tol;

        /// <summary>Vết cắt đứng (trục tấm lệch phương đứng ≤ <paramref name="maxDeg"/>). Bản vẽ tay chỉ đặt Continuous cho tấm
        /// đứng (web, bracket…), mũi tên nằm ngang — không cho tấm boong / face plate (kỹ sư 2026-11-18).</summary>
        public static bool IsUpright(CutMember m, double maxDeg = 30)
        {
            double len = m.CutAxis.Length;
            return len > 1e-12 && Math.Abs(m.CutAxis.Y) / len >= Math.Cos(maxDeg * Math.PI / 180);
        }

        public static string ThicknessText(double t)
            => "(" + t.ToString("0.0", CultureInfo.InvariantCulture) + ")";

        /// <summary>Đặt ký hiệu tại tâm vết cắt, xoay vuông góc với tấm.</summary>
        public static (P2 at, double rotation) ContinuousPlacement(CutMember m)
        {
            // Trục quay lên (Y ≥ 0) ⇒ tấm đứng luôn ra 180° như mọi ký hiệu trên bản vẽ tay
            P2 ax = m.CutAxis.Y < 0 || (m.CutAxis.Y == 0 && m.CutAxis.X < 0) ? m.CutAxis * -1 : m.CutAxis;
            double r = Math.Atan2(ax.Y, ax.X) + Math.PI / 2;
            double twoPi = 2 * Math.PI;
            r %= twoPi;
            if (r < 0) r += twoPi;
            return (m.CutCenter, r);
        }

        /// <summary>Dấu $: tâm chữ ĐÚNG trung điểm nét A–B (nét đi xuyên qua chữ, như 26 dấu $ trên bản vẽ tay).</summary>
        public static P2 WeldTracePlacement(P2 a, P2 b) => (a + b) * 0.5;

        /// <summary>Tấm song song đứng (mặt tấm hiện trên section): cao hơn dày. Face plate / tấm boong nằm ngang ⇒ false
        /// (không đặt "$" / "(t)" — kỹ sư 2026-11-18).</summary>
        public static bool IsUprightPlate(double faceHeight, double thickness) => faceHeight > thickness;

        /// <summary>Đoạn A–B chạm một viền trong <paramref name="outlines"/> (đầu đoạn nằm trên viền, hoặc đỉnh viền nằm trên đoạn).
        /// Dùng bỏ "$" ở nét đầu tấm nối với HP / FB (kỹ sư 2026-11-18).</summary>
        public static bool Touches(P2 a, P2 b, IEnumerable<IList<P2>> outlines, double tol)
        {
            foreach (var o in outlines)
            {
                if (o.Count == 0) continue;
                if (SectionCurveRules.DistanceToPolyline(a, o) <= tol || SectionCurveRules.DistanceToPolyline(b, o) <= tol) return true;
                foreach (var p in o) if (SectionCurveRules.DistanceToSegment(p, a, b) <= tol) return true;
            }
            return false;
        }

        /// <summary>Nét được đặt "$" (nét đầu tấm / nét nối 2 tấm song song): nét đứng, lệch phương ngang ≥ <paramref name="minDeg"/>.</summary>
        public static bool IsMarkLine(P2 a, P2 b, double minDeg = 30)
        {
            P2 d = b - a;
            double len = d.Length;
            return len > 1e-9 && Math.Abs(d.Y) / len >= Math.Sin(minDeg * Math.PI / 180);
        }
    }
}
