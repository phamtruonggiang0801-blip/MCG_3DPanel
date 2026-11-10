using System;
using System.Collections.Generic;
using System.Linq;

namespace MCG_3DPanel.Services.PlanView
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>Hình học đường bao Top Plate (hệ block 2D): đường cắt giữa panel cho mặt cắt (kỹ sư 2026-11-10) và điểm đo kích thước bao
    /// (tờ 7D-17P đo rộng 13356.2 tới đỉnh mép trên sát cạnh trái, không phải khung bao).</summary>
    public static class PlateGeometry
    {
        private const double Tol = 1.0;

        /// <summary>Đoạn tấm trên đường cắt: <paramref name="axis"/> 'X' = đường ngang y = <paramref name="at"/> (trả khoảng x),
        /// 'Y' = đường đứng x = <paramref name="at"/> (trả khoảng y). Đường bao rỗng / không cắt ⇒ khung bao <paramref name="box"/>.</summary>
        public static (double Lo, double Hi) Cut(IReadOnlyList<(double X, double Y)> outline, char axis, double at,
            (double MinX, double MinY, double MaxX, double MaxY) box = default)
        {
            var hits = new List<double>();
            for (int i = 0; i < (outline?.Count ?? 0); i++)
            {
                var a = outline[i]; var b = outline[(i + 1) % outline.Count];
                double a1 = axis == 'X' ? a.Y : a.X, b1 = axis == 'X' ? b.Y : b.X;     // toạ độ vuông góc đường cắt
                double a2 = axis == 'X' ? a.X : a.Y, b2 = axis == 'X' ? b.X : b.Y;     // toạ độ dọc đường cắt
                if (Math.Abs(b1 - a1) < 1e-9) continue;                               // cạnh song song đường cắt
                double t = (at - a1) / (b1 - a1);
                if (t < 0 || t > 1) continue;
                hits.Add(a2 + t * (b2 - a2));
            }
            if (hits.Count >= 2) return (hits.Min(), hits.Max());
            return axis == 'X' ? (box.MinX, box.MaxX) : (box.MinY, box.MaxY);
        }

        /// <summary>Kích thước bao dài: góc dưới-trái → góc dưới-phải (đỉnh thấp nhất trong dải 5% sát mỗi cạnh đứng).</summary>
        public static ((double, double) P1, (double, double) P2) OverallLength(IReadOnlyList<(double X, double Y)> outline)
        {
            var (minX, maxX, w) = Span(outline, true);
            var left = outline.Where(p => p.X <= minX + PlanViewLayout.OverallEdgeBand * w).OrderBy(p => p.Y).ThenBy(p => p.X).First();
            var right = outline.Where(p => p.X >= maxX - PlanViewLayout.OverallEdgeBand * w).OrderBy(p => p.Y).ThenByDescending(p => p.X).First();
            return (left, right);
        }

        /// <summary>Kích thước bao rộng: góc dưới-trái → đỉnh CAO NHẤT trong dải 5% sát cạnh trái.</summary>
        public static ((double, double) P1, (double, double) P2) OverallWidth(IReadOnlyList<(double X, double Y)> outline)
        {
            var (minX, _, w) = Span(outline, true);
            var band = outline.Where(p => p.X <= minX + PlanViewLayout.OverallEdgeBand * w).ToList();
            var bottom = band.OrderBy(p => p.Y).ThenBy(p => p.X).First();
            var top = band.OrderByDescending(p => p.Y).ThenBy(p => p.X).First();
            return (bottom, top);
        }

        /// <summary>Điểm nằm trên cạnh đường bao (≤ 1 mm).</summary>
        public static bool OnOutline(IReadOnlyList<(double X, double Y)> outline, double x, double y)
        {
            for (int i = 0; i < (outline?.Count ?? 0); i++)
            {
                var a = outline[i]; var b = outline[(i + 1) % outline.Count];
                double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
                double t = l2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, ((x - a.X) * dx + (y - a.Y) * dy) / l2));
                double px = a.X + t * dx - x, py = a.Y + t * dy - y;
                if (px * px + py * py <= Tol * Tol) return true;
            }
            return false;
        }

        private static (double Min, double Max, double Size) Span(IReadOnlyList<(double X, double Y)> o, bool x)
        {
            double mn = o.Min(p => x ? p.X : p.Y), mx = o.Max(p => x ? p.X : p.Y);
            return (mn, mx, mx - mn);
        }
    }
}
