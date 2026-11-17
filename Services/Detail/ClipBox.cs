using System;
using System.Collections.Generic;

namespace MCG_3DPanel.Services.Detail
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.*.

    /// <summary>Hộp cắt của bộ cắt section, toạ độ XY + Z của file 3D: lăng trụ vùng (đa giác lồi / trụ tròn) ∩ slab (Section), Z∈[ZMin, ZMax].
    /// Mặt phẳng cắt không tính là mặt biên.</summary>
    public sealed class ClipBox
    {
        /// <summary>Mặt đứng: điểm trên mặt + pháp tuyến hướng vào trong (đơn vị).</summary>
        public sealed class Face { public P2 Point; public P2 Normal; public bool IsSection; }

        public readonly List<Face> Sides = new List<Face>();
        public bool HasCircle;
        public P2 Center;
        public double Radius;
        public double ZMin, ZMax;
        public bool TopIsSection;

        // AABB XY của từng thành phần (vùng, slab), hộp = giao
        readonly List<(double x0, double y0, double x1, double y1)> _boxes = new List<(double, double, double, double)>();

        const double CoplanarTol = 1e-3;

        /// <summary>Mặt cắt Detail Plan (WCS 3D) = đáy Top Plate thấp nhất − 1. Không có Top Plate ⇒ null (caller dùng −1 + cảnh báo).</summary>
        public static double? PlanCutZ(IEnumerable<double> topPlateMinZ)
        {
            double? lo = null;
            foreach (double z in topPlateMinZ ?? Array.Empty<double>()) lo = lo.HasValue ? Math.Min(lo.Value, z) : z;
            return lo.HasValue ? lo.Value - 1 : (double?)null;
        }

        /// <summary>Mặt cắt ngang lấy viền Top Plate (WCS 3D): đáy + t/2; không có độ dày ⇒ giữa khung bao Z.</summary>
        public static double TopPlateCutZ(double minZ, double maxZ, double thickness) =>
            thickness > 0 ? minZ + thickness / 2 : (minZ + maxZ) / 2;

        /// <summary>Detail Plan: lăng trụ vùng, mặt cắt = mặt trên Z = zMax.</summary>
        public static ClipBox Plan(DetailRegion region, double zMin, double zMax)
        {
            var b = new ClipBox { ZMin = zMin, ZMax = zMax, TopIsSection = true };
            b.AddRegion(region);
            return b;
        }

        /// <summary>Section: lăng trụ vùng ∩ slab (start..end, 0..depth theo look); mặt cắt = cạnh slab start→end.</summary>
        public static ClipBox Section(DetailRegion region, P2 start, P2 end, P2 look, double depth, double zMin, double zMax)
        {
            var b = new ClipBox { ZMin = zMin, ZMax = zMax };
            b.AddRegion(region);
            b.AddPolygon(Slab(start, end, look, depth));
            // mặt cắt = mặt qua start, pháp tuyến vào trong = look; kèm mọi mặt vùng trùng phẳng với nó
            double ll = look.Length;
            var n = look * (1 / ll);
            foreach (var f in b.Sides)
                if (f.Normal.Dot(n) > 1 - 1e-9 && Math.Abs((f.Point - start).Dot(n)) < CoplanarTol)
                    f.IsSection = true;
            return b;
        }

        /// <summary>4 góc slab: start, end, end + look·depth, start + look·depth.</summary>
        public static P2[] Slab(P2 start, P2 end, P2 look, double depth)
        {
            var d = look * depth;
            return new[] { start, end, end + d, start + d };
        }

        /// <summary>Tâm, trục u (đơn vị, cạnh đầu), dài theo u và theo v = trái của u; góc vào CW được đảo về CCW.</summary>
        public static (P2 center, P2 u, double lenU, double lenV) RectFrame(IList<P2> corners)
        {
            var c = Ccw(corners);
            var center = (c[0] + c[1] + c[2] + c[3]) * 0.25;
            var e0 = c[1] - c[0];
            double lenU = e0.Length;
            var u = e0 * (1 / lenU);
            double lenV = (c[3] - c[0]).Length;
            return (center, u, lenU, lenV);
        }

        static P2[] Ccw(IList<P2> pts)
        {
            var c = new P2[pts.Count];
            pts.CopyTo(c, 0);
            if (SectionCurveRules.SignedArea(c) < 0) Array.Reverse(c);
            return c;
        }

        void AddRegion(DetailRegion r)
        {
            if (r.IsCircle)
            {
                HasCircle = true; Center = r.Center; Radius = r.Radius;
                _boxes.Add((r.Center.X - r.Radius, r.Center.Y - r.Radius, r.Center.X + r.Radius, r.Center.Y + r.Radius));
            }
            else AddPolygon(r.Corners);
        }

        void AddPolygon(IList<P2> poly)
        {
            var c = Ccw(poly);
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            for (int i = 0; i < c.Length; i++)
            {
                var a = c[i]; var e = c[(i + 1) % c.Length] - a;
                double len = e.Length;
                if (len > 1e-12) Sides.Add(new Face { Point = a, Normal = new P2(-e.Y / len, e.X / len) });
                x0 = Math.Min(x0, a.X); y0 = Math.Min(y0, a.Y); x1 = Math.Max(x1, a.X); y1 = Math.Max(y1, a.Y);
            }
            _boxes.Add((x0, y0, x1, y1));
        }

        /// <summary>AABB XY của hộp (giao AABB vùng và slab).</summary>
        public (double minX, double minY, double maxX, double maxY) Bounds()
        {
            double x0 = double.MinValue, y0 = double.MinValue, x1 = double.MaxValue, y1 = double.MaxValue;
            foreach (var b in _boxes)
            {
                x0 = Math.Max(x0, b.x0); y0 = Math.Max(y0, b.y0);
                x1 = Math.Min(x1, b.x1); y1 = Math.Min(y1, b.y1);
            }
            return (x0, y0, x1, y1);
        }

        /// <summary>Hộp bao 3D (min..max) có chạm AABB của hộp cắt không.</summary>
        public bool Meets(double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
        {
            var b = Bounds();
            return !(maxX < b.minX || minX > b.maxX || maxY < b.minY || minY > b.maxY || maxZ < ZMin || minZ > ZMax);
        }

        /// <summary>Mọi điểm nằm trong tol của CÙNG một mặt hộp không phải mặt cắt ⇒ đường biên (break line).</summary>
        public bool OnSideFace(IList<(P2 xy, double z)> pts, double tol)
        {
            if (pts == null || pts.Count == 0) return false;
            bool All(Func<P2, double, bool> near)
            {
                foreach (var p in pts) if (!near(p.xy, p.z)) return false;
                return true;
            }
            foreach (var f in Sides)
            {
                if (f.IsSection) continue;
                if (All((xy, z) => Math.Abs((xy - f.Point).Dot(f.Normal)) <= tol)) return true;
            }
            if (HasCircle && All((xy, z) => Math.Abs((xy - Center).Length - Radius) <= tol)) return true;
            if (All((xy, z) => Math.Abs(z - ZMin) <= tol)) return true;
            if (!TopIsSection && All((xy, z) => Math.Abs(z - ZMax) <= tol)) return true;
            return false;
        }
    }
}
