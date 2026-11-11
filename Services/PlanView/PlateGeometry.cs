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
        private const double GroupTol = 10.0;

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

        /// <summary>Cạnh thẳng CHÍNH mỗi phía: nhóm cạnh song song trục cùng toạ độ (±1 mm), lấy nhóm dài nhất (tổng) ở mỗi nửa khung bao.
        /// Tờ 7D-17C (tấm xiên góc dưới-trái): trái x 164.6 (bỏ tai 158.5), phải 376.9, dưới y 181.9 (bỏ vấu 176.4), trên 481.4
        /// ⇒ mặt cắt X-X dài 15925, Y-Y 22465.5, đường chéo đo kiểm cùng hình chữ nhật này. Tờ 7D-17P: x 0…15939.8, y −12891.3…61.</summary>
        public static Edges MainEdges(IReadOnlyList<(double X, double Y)> outline, (double MinX, double MinY, double MaxX, double MaxY) box = default)
        {
            if (outline == null || outline.Count < 3)
                return new Edges { Left = (box.MinX, 0), Right = (box.MaxX, 0), Bottom = (box.MinY, 0), Top = (box.MaxY, 0) };
            double minX = outline.Min(p => p.X), maxX = outline.Max(p => p.X), minY = outline.Min(p => p.Y), maxY = outline.Max(p => p.Y);
            double mx = (minX + maxX) / 2, my = (minY + maxY) / 2;
            var vert = new List<(double At, double Len)>(); var hor = new List<(double At, double Len)>();
            for (int i = 0; i < outline.Count; i++)
            {
                var a = outline[i]; var b = outline[(i + 1) % outline.Count];
                double dx = Math.Abs(b.X - a.X), dy = Math.Abs(b.Y - a.Y);
                if (dx <= Tol && dy > Tol) vert.Add(((a.X + b.X) / 2, dy));
                else if (dy <= Tol && dx > Tol) hor.Add(((a.Y + b.Y) / 2, dx));
            }
            // Nhóm: cạnh cách nhau ≤ 10 mm (bậc vát) là 1 cạnh; toạ độ = cạnh NGOÀI CÙNG của nhóm (tờ 17C: đáy −22460.5 / −22465.5 ⇒ 22465.5).
            (double, double) Best(IEnumerable<(double At, double Len)> segs, double fallback, bool outerIsMin)
            {
                var groups = new List<(double Lo, double Hi, double Len)>();
                foreach (var s in segs.OrderBy(s => s.At))
                    if (groups.Count > 0 && s.At - groups[groups.Count - 1].Hi <= GroupTol)
                        groups[groups.Count - 1] = (groups[groups.Count - 1].Lo, s.At, groups[groups.Count - 1].Len + s.Len);
                    else groups.Add((s.At, s.At, s.Len));
                if (groups.Count == 0) return (fallback, 0);
                var g = groups.OrderByDescending(x => x.Len).First();
                return (outerIsMin ? g.Lo : g.Hi, g.Len);
            }
            return new Edges
            {
                Left = Best(vert.Where(v => v.At < mx), minX, true), Right = Best(vert.Where(v => v.At > mx), maxX, false),
                Bottom = Best(hor.Where(v => v.At < my), minY, true), Top = Best(hor.Where(v => v.At > my), maxY, false),
                MinX = minX, MaxX = maxX, MinY = minY, MaxY = maxY,
            };
        }

        public sealed class Edges
        {
            public (double At, double Len) Left, Right, Bottom, Top;
            public double MinX, MaxX, MinY, MaxY;
            /// <summary>Kích thước dài đặt TRÊN khi cạnh dưới gãy (cạnh thẳng chính &lt; 50 % bề rộng) — tờ 17C; mặc định dưới (17P).</summary>
            public bool LengthOnTop => Bottom.Len < PlanViewLayout.BrokenEdge * (MaxX - MinX) && Top.Len > Bottom.Len;
            /// <summary>Kích thước rộng (và chuỗi dọc lỗ lashing) bên PHẢI khi cạnh trái gãy — tờ 17C; mặc định trái (17P).</summary>
            public bool WidthOnRight => Left.Len < PlanViewLayout.BrokenEdge * (MaxY - MinY) && Right.Len > Left.Len;
        }

        /// <summary>Kích thước bao dài = khung bao theo X, đo giữa đỉnh ngoài cùng trái / phải gần phía đặt kích thước nhất.</summary>
        public static ((double, double) P1, (double, double) P2) OverallLength(IReadOnlyList<(double X, double Y)> outline, bool top)
        {
            double minX = outline.Min(p => p.X), maxX = outline.Max(p => p.X);
            (double, double) Pick(IEnumerable<(double X, double Y)> ps) => top ? ps.OrderByDescending(p => p.Y).First() : ps.OrderBy(p => p.Y).First();
            return (Pick(outline.Where(p => p.X <= minX + Tol)), Pick(outline.Where(p => p.X >= maxX - Tol)));
        }

        /// <summary>Kích thước bao rộng, đo ở cạnh ngoài cùng phía đặt: từ cạnh chính dưới tới cạnh chính trên, mở rộng tới đỉnh của
        /// cạnh ngoài cùng nếu nó vượt ra (tờ 17P: tai trái lên 464.9; tờ 17C: bỏ vấu 176.4 vì không nằm trên cạnh phải ngoài cùng).</summary>
        public static ((double, double) P1, (double, double) P2) OverallWidth(IReadOnlyList<(double X, double Y)> outline, bool right)
        {
            var e = MainEdges(outline);
            double x = right ? e.MaxX : e.MinX;
            var side = outline.Where(p => Math.Abs(p.X - x) <= Tol).ToList();
            double lo = Math.Min(e.Bottom.At, side.Min(p => p.Y)), hi = Math.Max(e.Top.At, side.Max(p => p.Y));
            return ((x, lo), (x, hi));
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
    }
}
