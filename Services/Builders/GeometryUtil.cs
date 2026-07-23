using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Brep = Autodesk.AutoCAD.BoundaryRepresentation.Brep;
using MCG_3DPanel.Models;

namespace MCG_3DPanel.Services.Builders
{
    /// <summary>
    /// Tiện ích hình học DÙNG CHUNG cho các builder (không gắn với loại đối tượng nào).
    /// Đặt riêng để builder từng đối tượng chỉ chứa logic của chính nó.
    /// </summary>
    internal static class GeometryUtil
    {
        public const double WebMainMinLen = 1000.0;   // web "chính" (bỏ web-tier ngắn)

        /// <summary>Khoảng cách 2D từ điểm tới bbox (0 nếu nằm trong).</summary>
        public static double DistToBox(Point3d p, Extents3d b)
        {
            double dx = Math.Max(Math.Max(b.MinPoint.X - p.X, p.X - b.MaxPoint.X), 0);
            double dy = Math.Max(Math.Max(b.MinPoint.Y - p.Y, p.Y - b.MaxPoint.Y), 0);
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public static double Dist2d(Point3d a, Point3d b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Khoảng cách nhỏ nhất từ điểm tới bbox các Web DÀI; MaxValue nếu không có.</summary>
        public static double MinDistToWeb(Point3d p, ClassificationResult cls)
        {
            double best = double.MaxValue;
            foreach (var w in cls.OfType(PartType.Web))
            {
                if (w.MemberLength < WebMainMinLen) continue;
                double d = DistToBox(p, w.Bounds);
                if (d < best) best = d;
            }
            return best;
        }

        /// <summary>Số khối rời (complex) của solid — &gt;1 nghĩa là union KHÔNG dính. -1 nếu lỗi Brep.</summary>
        public static int CountLumps(Solid3d sol)
        {
            try
            {
                using (var brep = new Brep(sol))
                {
                    int c = 0;
                    foreach (var cx in brep.Complexes) { c++; }
                    return c;
                }
            }
            catch { return -1; }
        }

        /// <summary>Bán kính cung từ 2 đỉnh + bulge (0 nếu bulge ~ 0).</summary>
        public static double ArcRadius(Point2d p, Point2d q, double bulge)
        {
            if (Math.Abs(bulge) < 1e-9) return 0;
            double chord = p.GetDistanceTo(q);
            double theta = 4.0 * Math.Atan(Math.Abs(bulge));
            return chord / (2.0 * Math.Sin(theta / 2.0));
        }

        /// <summary>Dựng Region kín từ danh sách đỉnh+bulge (local 2D). null nếu thất bại.</summary>
        public static Region RegionFrom(List<VertexBulge> outline)
        {
            var pl = new Polyline();
            for (int i = 0; i < outline.Count; i++)
                pl.AddVertexAt(i, outline[i].Pt, outline[i].Bulge, 0, 0);
            pl.Closed = true;

            DBObjectCollection regions;
            try { regions = Region.CreateFromCurves(new DBObjectCollection { pl }); }
            catch { pl.Dispose(); return null; }
            pl.Dispose();
            if (regions.Count == 0) return null;

            var reg = (Region)regions[0];
            for (int i = 1; i < regions.Count; i++) ((Region)regions[i]).Dispose();
            return reg;
        }
    }
}
