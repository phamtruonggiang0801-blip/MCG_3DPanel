using System;
using System.Collections.Generic;

namespace MCG_3DPanel.Services.Detail
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.*.

    /// <summary>Vùng detail: hình chữ nhật (có thể xoay) hoặc hình tròn.</summary>
    public sealed class DetailRegion
    {
        public bool IsCircle;
        public P2 Center;
        public double Radius;
        public P2[] Corners;   // 4 góc CCW

        const double Tol = 1e-6;

        public static DetailRegion FromCircle(P2 c, double r) =>
            new DetailRegion { IsCircle = true, Center = c, Radius = r };

        public static DetailRegion FromPolyline(IList<(double x, double y, double bulge)> v, bool closed, out string why)
        {
            why = null;
            int n = v == null ? 0 : v.Count;
            // 5 đỉnh mà đỉnh cuối lặp đỉnh đầu = đã đóng
            bool repeated = n == 5 && Math.Abs(v[4].x - v[0].x) < Tol && Math.Abs(v[4].y - v[0].y) < Tol;
            if (repeated) n = 4;
            if (!closed && !repeated) { why = "not closed"; return null; }
            for (int i = 0; i < n; i++)
                if (Math.Abs(v[i].bulge) >= 1e-9) { why = "has arcs"; return null; }
            if (n != 4) { why = "not a rectangle"; return null; }

            var p = new P2[4];
            for (int i = 0; i < 4; i++) p[i] = new P2(v[i].x, v[i].y);
            var e = new P2[4];
            for (int i = 0; i < 4; i++) e[i] = p[(i + 1) % 4] - p[i];
            double cosTol = Math.Sin(0.5 * Math.PI / 180);
            for (int i = 0; i < 4; i++)
            {
                double li = e[i].Length, lj = e[(i + 1) % 4].Length;
                if (li < Tol || lj < Tol) { why = "not a rectangle"; return null; }
                // cạnh kề vuông góc
                if (Math.Abs(e[i].Dot(e[(i + 1) % 4])) / (li * lj) > cosTol) { why = "not a rectangle"; return null; }
            }
            // cạnh đối song song (cùng độ dài trong sai số) — vuông góc liên tiếp đã suy ra song song,
            // kiểm thêm độ dài để loại hình không khép kín thật sự
            if (Math.Abs(e[0].Length - e[2].Length) > 1e-3 * Math.Max(e[0].Length, 1) ||
                Math.Abs(e[1].Length - e[3].Length) > 1e-3 * Math.Max(e[1].Length, 1))
            { why = "not a rectangle"; return null; }

            double area = 0;
            for (int i = 0; i < 4; i++) area += p[i].X * p[(i + 1) % 4].Y - p[(i + 1) % 4].X * p[i].Y;
            if (area < 0) Array.Reverse(p);
            return new DetailRegion { Corners = p };
        }

        public bool Contains(P2 pt)
        {
            if (IsCircle) return (pt - Center).Length <= Radius + Tol;
            for (int i = 0; i < 4; i++)
            {
                var a = Corners[i]; var b = Corners[(i + 1) % 4];
                var ab = b - a; var ap = pt - a;
                double cross = ab.X * ap.Y - ab.Y * ap.X;   // CCW: bên trong ⇒ cross ≥ 0
                if (cross / ab.Length < -Tol) return false;
            }
            return true;
        }


        /// <summary>Khoảng cách tới biên vùng (cạnh chữ nhật / đường tròn).</summary>
        public double DistanceToBoundary(P2 pt)
        {
            if (IsCircle) return Math.Abs((pt - Center).Length - Radius);
            double d = double.MaxValue;
            for (int i = 0; i < 4; i++) d = Math.Min(d, SectionCurveRules.DistanceToSegment(pt, Corners[i], Corners[(i + 1) % 4]));
            return d;
        }
        public P2[] Outline(int circleSegments = 72)
        {
            if (!IsCircle) return (P2[])Corners.Clone();
            var o = new P2[circleSegments];
            for (int i = 0; i < circleSegments; i++)
            {
                double a = 2 * Math.PI * i / circleSegments;
                o[i] = new P2(Center.X + Radius * Math.Cos(a), Center.Y + Radius * Math.Sin(a));
            }
            return o;
        }
    }
}
