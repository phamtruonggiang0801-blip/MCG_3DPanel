using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Brep = Autodesk.AutoCAD.BoundaryRepresentation.Brep;
using MCG_3DPanel.Models;
using MCG_3DPanel.Services.Classify;

namespace MCG_3DPanel.Services.Builders
{
    /// <summary>
    /// Tiện ích hình học DÙNG CHUNG cho các builder (không gắn với loại đối tượng nào).
    /// Đặt riêng để builder từng đối tượng chỉ chứa logic của chính nó.
    /// </summary>
    internal static class GeometryUtil
    {
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

        /// <summary>Khoảng cách nhỏ nhất từ điểm tới ĐOẠN TÂM THẬT (không phải bbox) của MỌI
        /// <see cref="PartType.Web"/> — kể cả Web-tier ngắn; MaxValue nếu không có Web nào.
        ///
        /// ⚠️ PHẢI dùng đoạn tâm thật (<see cref="ClassifyUtil.DistToSegment"/>), KHÔNG phải bbox
        /// (<see cref="DistToBox"/>): Web CHÉO có bbox trục thẳng "phồng" rất lớn so với tiết diện thật
        /// (VD panel 7D-17C: Web chéo bbox 4115×8067 dù dày thật chỉ 6mm) — dùng bbox khiến CẢ HAI đầu
        /// của 1 bracket ngắn gần đó đều "gần Web" như nhau, làm <see cref="BracketBuilder.AnchorDir"/>
        /// chọn nhầm đầu neo (bracket dựng LẬT NGƯỢC) hoặc neo sai điểm (lưng không khớp mép Web thật).
        /// Kiểm chứng thật: TOÀN BỘ 13 bracket dựng lật/lệch trên panel 7D-17C đều neo vào 1 Web CHÉO.
        ///
        /// ⚠️ KHÔNG lọc theo độ dài Web (bản cũ có ngưỡng "web chính" ≥1000mm, bỏ web-tier ngắn) — 1
        /// Web-tier NGẮN (VD `38DF2`, dài 486.7mm, xem [[panel-parts-reference]] mục 4d) vẫn là chỗ hàn
        /// THẬT cho bracket. Lọc theo độ dài khiến `AnchorDir` không thấy Web-tier gần nhất, rơi về
        /// fallback "via=center" (đối xứng qua tâm TopPlate) — neo sai điểm dù chọn đúng đầu, gây
        /// "lưng bracket không nằm sát Web" (kiểm chứng thật: case `393B9`, neo đúng vào `38DF2`).</summary>
        public static double MinDistToWeb(Point3d p, ClassificationResult cls)
        {
            double best = double.MaxValue;
            foreach (var w in cls.OfType(PartType.Web))
            {
                double d = ClassifyUtil.DistToSegment(p, w);
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

        /// <summary>True nếu <paramref name="pt"/> KHÔNG nằm gần (trong <paramref name="tol"/>) bất kỳ
        /// member kết cấu nào khác (Web/Bracket/GirderEnd/HP/FlatBar, loại trừ chính <paramref name="self"/>)
        /// — dùng để xác định "đầu tự do" của 1 đoạn FlatBar cho <see cref="FlatBarBuilder"/> (xem skill
        /// sniped-end). Dùng <see cref="ClassifyUtil.DistToSegment"/> (đoạn tâm thật) nên đúng cả member CHÉO.</summary>
        public static bool IsFreeEnd(Point3d pt, PanelPart self, ClassificationResult cls, double tol)
        {
            foreach (var o in cls.Parts)
            {
                if (ReferenceEquals(o, self)) continue;
                switch (o.Type)
                {
                    case PartType.Web:
                    case PartType.Bracket:
                    case PartType.GirderEnd:
                    case PartType.HP:
                    case PartType.FlatBar:
                        if (ClassifyUtil.DistToSegment(pt, o) <= tol) return false;
                        break;
                }
            }
            return true;
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
