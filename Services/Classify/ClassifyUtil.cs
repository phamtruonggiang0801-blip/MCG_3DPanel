using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCG_3DPanel.Models;

namespace MCG_3DPanel.Services.Classify
{
    /// <summary>
    /// Hằng số nhận diện (layer/màu/dung sai) + tiện ích hình học dùng chung cho các bước phân loại.
    /// Đặt tập trung để đổi quy ước bản vẽ chỉ sửa 1 nơi. Xem skill panel-parts-reference.
    /// </summary>
    internal static class ClassifyUtil
    {
        public const string LAYER_TOPPLATE = "0";
        public const string LAYER_WEB = "Mechanical-AM_0";
        public const string LAYER_FLANGE = "Mechanical-AM_11";
        public const string LAYER_FBHP = "Mechanical-AM_3";
        public const int COLOR_HP = 30;                 // AM_3 màu 30 = HP; khác = Flat Bar

        public const double WEB_MIN_LEN = 1000.0;       // AM_0 dài hơn = Web chính
        public const double WEB_TIER_LEN = 470.0;       // mảnh ngắn nhưng ≥ ngưỡng + collinear web dài = Web tier (không phải bracket)
        public const double GE_MIN_LEN = 600.0;         // GE: mảnh dài ≥ ngưỡng…
        public const double GE_MIN_THK = 10.0;          // …và dày ≥ ngưỡng, KHÔNG collinear web dài
        public const double HP_MIN_LEN = 500.0;         // HP đủ dài mới xét bụng/lưng
        public const double EDGE_TOL = 50.0;            // dung sai xác định HP "ngoài cùng" (so member dài khác)
        public const double EDGE_POLY_TOL = 150.0;      // dung sai xác định HP "ngoài cùng" so BIÊN THẬT Top Plate
        public const double ADJ_TOL = 12.0;             // dung sai kề nhau (bracket chạm HP/FB)
        public const double TOL_GAP = 20.0;             // khe hở dọc trục khi gom Web collinear
        public const double TOL_PERP = 8.0;             // lệch ngang khi gom Web collinear
        public const double FB_SPLIT_MARGIN = 20.0;     // FlatBarSplitter: điểm cắt HP phải cách 2 đầu mút FB ít nhất chừng này (mới coi là "cắt ngang giữa thân", không phải chạm đầu)
        public const double FB_FREE_END_TOL = 30.0;     // FlatBarBuilder: đầu FB cách member khác (Web/HP/FB) ≤ 30mm mới coi là "không tự do" (không vát) — xác nhận kỹ sư, xem skill sniped-end

        /// <summary>Khoảng cách 2D từ điểm tới bbox (0 nếu nằm trong).</summary>
        public static double DistToBox(Point3d p, Extents3d b)
        {
            double dx = Math.Max(Math.Max(b.MinPoint.X - p.X, p.X - b.MaxPoint.X), 0);
            double dy = Math.Max(Math.Max(b.MinPoint.Y - p.Y, p.Y - b.MaxPoint.Y), 0);
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>2 part kề nhau theo bbox (nới rộng tol).</summary>
        public static bool Adjacent(PanelPart a, PanelPart b, double tol)
        {
            Extents3d A = a.Bounds, B = b.Bounds;
            return A.MinPoint.X - tol <= B.MaxPoint.X && A.MaxPoint.X + tol >= B.MinPoint.X &&
                   A.MinPoint.Y - tol <= B.MaxPoint.Y && A.MaxPoint.Y + tol >= B.MinPoint.Y;
        }

        /// <summary>Polyline là rectangle (Flange) hay không (Face Plate nếu không).</summary>
        public static bool IsRectangle(PanelPart p) => p.VertexCount == 4 || p.VertexCount == 5;

        /// <summary>Diện tích chồng lấn bbox 2D của 2 part (0 nếu rời nhau).</summary>
        public static double OverlapArea(PanelPart a, PanelPart b)
        {
            Extents3d A = a.Bounds, B = b.Bounds;
            double ox = Math.Min(A.MaxPoint.X, B.MaxPoint.X) - Math.Max(A.MinPoint.X, B.MinPoint.X);
            double oy = Math.Min(A.MaxPoint.Y, B.MaxPoint.Y) - Math.Max(A.MinPoint.Y, B.MinPoint.Y);
            return (ox > 0 && oy > 0) ? ox * oy : 0.0;
        }

        /// <summary>2 member CÙNG PHƯƠNG, thẳng hàng (lệch ngang nhỏ), chồng/nối theo trục dài.</summary>
        public static bool Collinear(PanelPart a, PanelPart b)
        {
            if (a.IsHorizontal != b.IsHorizontal) return false;
            if (Math.Abs(a.PerpCenter - b.PerpCenter) > (a.FootprintThickness + b.FootprintThickness) / 2 + TOL_PERP) return false;
            Extents3d ba = a.Bounds, bb = b.Bounds;
            if (a.IsHorizontal)
                return ba.MinPoint.X - TOL_GAP <= bb.MaxPoint.X && ba.MaxPoint.X + TOL_GAP >= bb.MinPoint.X;
            return ba.MinPoint.Y - TOL_GAP <= bb.MaxPoint.Y && ba.MaxPoint.Y + TOL_GAP >= bb.MinPoint.Y;
        }

        /// <summary>Mảnh <paramref name="p"/> là TIER của 1 Web dài (&gt; WEB_MIN_LEN): collinear +
        /// (đủ dài ≥ WEB_TIER_LEN — tier "chunky" bình thường, HOẶC dày KHÁC hẳn web đó — chuyển bậc
        /// thật dù ngắn). Kiểm chứng 7D-17C: bắt đúng tier dày10→6 dài chỉ 400mm mà ngưỡng dài đơn
        /// thuần (cũ) bỏ sót; không phát sinh sai lệch nào khác trên toàn panel (97 part AM_0).</summary>
        public static bool CollinearWithLongWeb(PanelPart p, IEnumerable<PanelPart> webs)
            => webs.Any(w => w.MemberLength > WEB_MIN_LEN && !ReferenceEquals(w, p) && Collinear(p, w)
                           && (p.MemberLength >= WEB_TIER_LEN
                               || Math.Abs(w.FootprintThickness - p.FootprintThickness) > 1e-6));

        /// <summary>Cạnh NGẮN của bbox (min của w,h) — để phân biệt strip mỏng vs tấm.</summary>
        public static double BboxMinDim(PanelPart p)
            => Math.Min(p.Bounds.MaxPoint.X - p.Bounds.MinPoint.X, p.Bounds.MaxPoint.Y - p.Bounds.MinPoint.Y);

        /// <summary>Cạnh DÀI của bbox.</summary>
        public static double BoxMaxDim(PanelPart p)
            => Math.Max(p.Bounds.MaxPoint.X - p.Bounds.MinPoint.X, p.Bounds.MaxPoint.Y - p.Bounds.MinPoint.Y);

        /// <summary>bbox của <paramref name="inner"/> nằm HẲN trong bbox <paramref name="outer"/> (lùi vào ≥ tol mỗi cạnh).</summary>
        public static bool StrictlyInside(PanelPart inner, PanelPart outer, double tol)
        {
            Extents3d i = inner.Bounds, o = outer.Bounds;
            return i.MinPoint.X > o.MinPoint.X + tol && i.MaxPoint.X < o.MaxPoint.X - tol &&
                   i.MinPoint.Y > o.MinPoint.Y + tol && i.MaxPoint.Y < o.MaxPoint.Y - tol;
        }

        /// <summary>Khoảng cách nhỏ nhất từ điểm tới BIÊN đa giác (mọi cạnh, đóng vòng ngầm định) —
        /// dùng để xác định "gần mép ngoài cùng THẬT" của 1 tấm (VD Top Plate) khi biên có nhiều
        /// khấc/bậc cục bộ (notch/step) — khác <see cref="StrictlyInside"/> (chỉ so bbox, không thấy
        /// khấc) hay so sánh với vị trí các member DÀI khác (lệch hàng trăm mm tại chỗ có khấc).</summary>
        public static double DistToPolygonBoundary(Point3d pt, Point3d[] poly)
        {
            if (poly == null || poly.Length < 2) return double.MaxValue;
            double best = double.MaxValue;
            int n = poly.Length;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Point3d a = poly[j], b = poly[i];
                double dx = b.X - a.X, dy = b.Y - a.Y;
                double len2 = dx * dx + dy * dy;
                double t = len2 > 1e-9 ? ((pt.X - a.X) * dx + (pt.Y - a.Y) * dy) / len2 : 0.0;
                if (t < 0) t = 0; else if (t > 1) t = 1;
                double cx = a.X + t * dx, cy = a.Y + t * dy;
                double d = Math.Sqrt((pt.X - cx) * (pt.X - cx) + (pt.Y - cy) * (pt.Y - cy));
                if (d < best) best = d;
            }
            return best;
        }

        /// <summary>
        /// Pháp tuyến NGOÀI THẬT tại cạnh biên đa giác gần <paramref name="pt"/> nhất — đúng cả tại góc
        /// LÕM/khấc (notch), khác hẳn xấp xỉ "xa tâm panel" (<c>centroid − center</c>) vốn CHỈ đúng khi
        /// biên LỒI đơn giản: tại 1 khấc cắt vào tấm, "xa tâm panel" vẫn trỏ ra ngoài panel nói chung
        /// nhưng có thể trỏ VÀO vật liệu thật (khấc bẻ ngược biên cục bộ). Cách làm: tìm cạnh gần nhất,
        /// lấy 1 trong 2 pháp tuyến của cạnh đó, thử bước nhỏ (2mm) từ điểm chân vuông góc trên cạnh —
        /// nếu bước đó vẫn NẰM TRONG đa giác (<see cref="PointInPolygon"/>) thì đổi dấu (đó là hướng VÀO
        /// vật liệu, không phải ra ngoài).
        /// </summary>
        public static Vector3d LocalOutwardNormal(Point3d pt, Point3d[] poly)
        {
            if (poly == null || poly.Length < 3) return new Vector3d(0, 0, 0);
            double best = double.MaxValue;
            double footX = 0, footY = 0, edgeDx = 0, edgeDy = 0;
            int n = poly.Length;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Point3d a = poly[j], b = poly[i];
                double dx = b.X - a.X, dy = b.Y - a.Y;
                double len2 = dx * dx + dy * dy;
                double t = len2 > 1e-9 ? ((pt.X - a.X) * dx + (pt.Y - a.Y) * dy) / len2 : 0.0;
                if (t < 0) t = 0; else if (t > 1) t = 1;
                double cx = a.X + t * dx, cy = a.Y + t * dy;
                double d = Math.Sqrt((pt.X - cx) * (pt.X - cx) + (pt.Y - cy) * (pt.Y - cy));
                if (d < best) { best = d; footX = cx; footY = cy; edgeDx = dx; edgeDy = dy; }
            }
            double elen = Math.Sqrt(edgeDx * edgeDx + edgeDy * edgeDy);
            if (elen < 1e-9) return new Vector3d(0, 0, 0);
            double nx = -edgeDy / elen, ny = edgeDx / elen;
            const double PROBE = 2.0;
            var probe = new Point3d(footX + nx * PROBE, footY + ny * PROBE, 0);
            bool probeInside = PointInPolygon(probe, poly);
            return probeInside ? new Vector3d(-nx, -ny, 0) : new Vector3d(nx, ny, 0);
        }

        /// <summary>Point-in-polygon (ray casting). <paramref name="poly"/> là đỉnh WCS, đóng vòng ngầm định.</summary>
        public static bool PointInPolygon(Point3d pt, Point3d[] poly)
        {
            if (poly == null || poly.Length < 3) return false;
            bool inside = false;
            int n = poly.Length;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = poly[i].X, yi = poly[i].Y;
                double xj = poly[j].X, yj = poly[j].Y;
                if ((yi > pt.Y) != (yj > pt.Y) && pt.X < (xj - xi) * (pt.Y - yi) / (yj - yi) + xi)
                    inside = !inside;
            }
            return inside;
        }

        /// <summary>Tỉ lệ diện tích bbox <paramref name="p"/> nằm trong đa giác THẬT của <paramref name="fp"/>
        /// (lấy mẫu lưới 7×7, point-in-polygon) — chính xác hơn <see cref="OverlapArea"/> (bbox-vs-bbox)
        /// khi fp KHÔNG phải chữ nhật (VD FacePlate loe: bbox rộng hơn nhiều hình dạng thật). Dùng cho
        /// ngưỡng nghiêm ngặt ở <see cref="PlateDetector.IsClosedPlateStrict"/>.</summary>
        public static double PolygonOverlapFraction(PanelPart p, PanelPart fp)
        {
            if (fp.Vertices == null || fp.Vertices.Length < 3) return 0.0;
            Extents3d b = p.Bounds;
            const int STEPS = 6;
            int inside = 0, total = 0;
            for (int gx = 0; gx <= STEPS; gx++)
                for (int gy = 0; gy <= STEPS; gy++)
                {
                    double px = b.MinPoint.X + (b.MaxPoint.X - b.MinPoint.X) * gx / STEPS;
                    double py = b.MinPoint.Y + (b.MaxPoint.Y - b.MinPoint.Y) * gy / STEPS;
                    total++;
                    if (PointInPolygon(new Point3d(px, py, 0), fp.Vertices)) inside++;
                }
            return total > 0 ? (double)inside / total : 0.0;
        }

        /// <summary>Member CÓ bbox "phồng" hẳn so với dày thật — dấu hiệu member CHÉO: bbox trục thẳng
        /// của 1 đoạn chéo dài rộng hơn nhiều tiết diện thật, khiến <see cref="Adjacent"/> (bbox-vs-bbox)
        /// dễ báo "chạm" giả cho bất cứ thứ gì rơi vào vùng bbox thừa đó.</summary>
        public static bool IsDiagonalBloatedBbox(PanelPart m)
        {
            double minDim = Math.Min(m.Bounds.MaxPoint.X - m.Bounds.MinPoint.X, m.Bounds.MaxPoint.Y - m.Bounds.MinPoint.Y);
            return minDim > m.FootprintThickness * 1.5 + 5;
        }

        /// <summary>
        /// "Chạm YẾU" = mọi HP/FB khớp <see cref="Adjacent"/> với <paramref name="p"/> đều KHÔNG đáng tin:
        /// hoặc bbox chéo bị phồng (chạm giả — xem <see cref="IsDiagonalBloatedBbox"/>), hoặc bản thân
        /// member đó NGẮN HƠN <paramref name="p"/> (phi lý làm "chỗ hàn" cho 1 bracket — bracket thật
        /// luôn hàn vào member DÀI hơn nó, vd HP/FB chạy dọc panel). Trả về false nếu KHÔNG chạm gì cả
        /// (gọi hàm này chỉ có ý nghĩa khi đã biết p có chạm).
        /// Kiểm chứng 7D-17C: đúng 5/5 ClosedPlate thật có chạm yếu; case gần nhất (overlap FacePlate
        /// cũng cao) nhưng chạm THẬT (member dài hơn hẳn) đúng vẫn ở lại Bracket.</summary>
        public static bool HasOnlyWeakTouch(PanelPart p, IEnumerable<PanelPart> hpParts,
            IEnumerable<PanelPart> fbParts, double tol)
        {
            bool anyMatch = false;
            foreach (var m in hpParts.Concat(fbParts))
            {
                if (!Adjacent(p, m, tol)) continue;
                anyMatch = true;
                bool strong = !IsDiagonalBloatedBbox(m) && m.MemberLength > p.MemberLength;
                if (strong) return false;
            }
            return anyMatch;
        }

        /// <summary>2 điểm mút (trung điểm 2 cạnh NGẮN) của member hình chữ nhật, lấy từ
        /// <see cref="PanelPart.Vertices"/> THẬT — đúng cả member CHÉO (không như <see cref="Adjacent"/>/
        /// <see cref="Collinear"/> vốn dựa bbox trục thẳng X/Y). Fallback theo bbox nếu thiếu đỉnh.</summary>
        public static (Point3d, Point3d) Endpoints(PanelPart p)
        {
            var v = p.Vertices;
            if (v != null && v.Length >= 4)
            {
                double d01 = v[0].DistanceTo(v[1]);
                double d12 = v[1].DistanceTo(v[2]);
                Point3d Mid(Point3d a, Point3d b) => new Point3d((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0, 0);
                return d01 >= d12
                    ? (Mid(v[1], v[2]), Mid(v[3], v[0]))
                    : (Mid(v[0], v[1]), Mid(v[2], v[3]));
            }
            Extents3d b2 = p.Bounds;
            return p.IsHorizontal
                ? (new Point3d(b2.MinPoint.X, p.Centroid.Y, 0), new Point3d(b2.MaxPoint.X, p.Centroid.Y, 0))
                : (new Point3d(p.Centroid.X, b2.MinPoint.Y, 0), new Point3d(p.Centroid.X, b2.MaxPoint.Y, 0));
        }

        /// <summary>Khoảng cách từ điểm tới ĐOẠN TÂM thật (xem <see cref="Endpoints"/>) của member —
        /// đúng cả member CHÉO, không bị bbox trục thẳng "phồng" như <see cref="DistToBox"/> (xem
        /// <see cref="IsDiagonalBloatedBbox"/>).</summary>
        public static double DistToSegment(Point3d pt, PanelPart other)
        {
            var (a, b) = Endpoints(other);
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len2 = dx * dx + dy * dy;
            if (len2 < 1e-9) return pt.DistanceTo(a);
            double t = ((pt.X - a.X) * dx + (pt.Y - a.Y) * dy) / len2;
            t = Math.Max(0, Math.Min(1, t));
            return pt.DistanceTo(new Point3d(a.X + t * dx, a.Y + t * dy, 0));
        }

        /// <summary>
        /// <paramref name="p"/> khớp đúng dáng BRACKET: đúng 1 đầu mút chạm 1 member AM_0 khác
        /// (<paramref name="otherAm0"/> — Web/tier/Plate/Bracket khác…), đầu KIA chạm HP hoặc Flat Bar.
        /// Định nghĩa OB/IB/FB thật: "1 đầu Web, 1 đầu HP/FB" — KHÔNG phải cứ chạm HP/FB ở đâu đó trên
        /// thân là đủ. Nếu CẢ HAI đầu đều chạm member AM_0 khác (không đầu nào chạm HP/FB) ⇒ chỉ là
        /// đoạn NỐI TIẾP của Web, không phải Bracket — bất kể collinear hay chênh dày ra sao. Dùng
        /// <see cref="DistToSegment"/> (đoạn tâm thật) nên xử lý đúng cả member CHÉO (VD góc vát panel).
        /// Kiểm chứng 7D-17C: khớp TUYỆT ĐỐI trên toàn bộ 108 part AM_0 (46 Bracket hiện có + tự sửa
        /// đúng cả 3 case sai từng phải vá bằng heuristic riêng: `38DF7`, `3916E`, `38DF2`).
        /// </summary>
        public static bool IsBracketPattern(PanelPart p, IEnumerable<PanelPart> otherAm0,
            IEnumerable<PanelPart> hpParts, IEnumerable<PanelPart> fbParts, double tol)
        {
            var (e1, e2) = Endpoints(p);
            bool NearAm0(Point3d e) => otherAm0.Any(o => !ReferenceEquals(o, p) && DistToSegment(e, o) <= tol);
            bool NearHpFb(Point3d e) => hpParts.Any(h => DistToSegment(e, h) <= tol)
                                      || fbParts.Any(f => DistToSegment(e, f) <= tol);
            return (NearAm0(e1) && NearHpFb(e2)) || (NearHpFb(e1) && NearAm0(e2));
        }

        /// <summary>
        /// <paramref name="h"/> (HP) CẮT NGANG THẬT qua thân <paramref name="p"/> (FlatBar) — không phải
        /// chỉ chạm ở 1 đầu mút. Dùng cho <see cref="FlatBarSplitter"/>: người vẽ 2D thường kéo 1 polyline
        /// FB LIÊN TỤC xuyên qua HP cho nhanh, nhưng THỰC TẾ FB không liên tục — nó kết thúc tại đúng vị
        /// trí giao với HP ở cả 2 phía (xác nhận kỹ sư, xem skill sniped-end). Phải tách polyline FB tại
        /// những chỗ này trước khi tìm "đầu tự do" — nếu không, code sẽ không nhận ra 2 đầu mới sinh ra ở
        /// đó (chúng vẫn nằm giữa 1 polyline duy nhất, không phải Vertices/Endpoints thật).
        ///
        /// Điều kiện: bbox <paramref name="p"/>/<paramref name="h"/> có chồng lấn THẬT (không chỉ kề —
        /// <see cref="Adjacent"/> nới rộng tol nên không đủ chặt ở đây), và điểm chồng lấn đó chiếu lên
        /// trục dài của <paramref name="p"/> rơi NGHIÊM NGẶT vào khoảng giữa thân (cách 2 đầu mút ít nhất
        /// <see cref="FB_SPLIT_MARGIN"/>) — nếu rơi sát 1 đầu mút, đó là chạm-đầu bình thường (đã có sẵn
        /// cơ chế IsFreeEnd xử lý), không cần tách.
        /// </summary>
        public static bool CrossesThrough(PanelPart p, PanelPart h)
        {
            var (e1, e2) = Endpoints(p);
            double L = e1.DistanceTo(e2);
            if (L < 1e-6) return false;
            Vector3d dir = (e2 - e1) / L;

            if (OverlapArea(p, h) <= 0.0) return false;

            Extents3d hb = h.Bounds;
            double tmin = double.MaxValue, tmax = double.MinValue;
            var corners = new[]
            {
                new Point3d(hb.MinPoint.X, hb.MinPoint.Y, 0), new Point3d(hb.MaxPoint.X, hb.MinPoint.Y, 0),
                new Point3d(hb.MinPoint.X, hb.MaxPoint.Y, 0), new Point3d(hb.MaxPoint.X, hb.MaxPoint.Y, 0),
            };
            foreach (var c in corners)
            {
                double t = (c.X - e1.X) * dir.X + (c.Y - e1.Y) * dir.Y;
                if (t < tmin) tmin = t;
                if (t > tmax) tmax = t;
            }
            // Toàn bộ vùng chồng lấn của h nằm gọn ở 1 đầu mút p → chạm đầu, KHÔNG PHẢI cắt ngang giữa thân.
            if (tmax <= FB_SPLIT_MARGIN || tmin >= L - FB_SPLIT_MARGIN) return false;
            return true;
        }
    }
}
