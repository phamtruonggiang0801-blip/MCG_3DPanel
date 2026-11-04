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
        public const double WEB_MAX_THK = 40.0;         // polygon AM_0 dày hơn ngưỡng này KHÔNG phải tôn web/plate
                                                        // thật (tôn kết cấu ≤ ~32mm) — gần như luôn là đường bao
                                                        // panel / nét dựng phụ lọt layer AM_0. Nhận nhầm là Web →
                                                        // dựng khối đặc khổng lồ + FootprintThickness sai lan sang
                                                        // WebHpCutoutBuilder & WebCollarPlateBuilder (collar dài
                                                        // hàng mét, lệch ra ngoài panel — bug 2026-09-04, panel
                                                        // 701S handle 6875E). Xem WebBracketSplitter.Split.
        public const double GE_MIN_LEN = 600.0;         // GE: mảnh dài ≥ ngưỡng…
        public const double GE_MIN_THK = 10.0;          // …và dày ≥ ngưỡng, KHÔNG collinear web dài
        public const double GB_TOE_PAST_HP = 100.0;     // GB: toe đáy cách đường tâm HP xuyên gần nhất 100mm (dim "100" trong CAS.dump.json)
        public const double GB_TOE_PAD = 60.0;          // GB: đoạn đáy thẳng thêm sau toe (chỗ R50 tiếp tuyến) — kẹp trong vùng GB
        public const double GE_MAX_L = 775.0;           // FALLBACK Max Stiff khi GirderEndDetector.HpMaxSpan=0 (775 = Max Stiff panel mẫu 7D-17C, DIM "L<775mm"); GE: L < Max Stiff, ≥ ⇒ GB
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

        /// <summary>Hướng cạnh DÀI NHẤT (đơn vị, chiếu XY) của polyline = trục dài member. XAxis nếu suy biến.
        /// Với web-tier vát 1:4 HOẶC tấm chữ nhật VÁT GÓC (26FFF/27046/27050…), cạnh dài nhất LUÔN là 1
        /// cạnh BÊN thật — đoạn vát/cap ngắn không bao giờ dài nhất — nên không cần biết thứ tự đỉnh.</summary>
        internal static Vector3d LongAxis(Point3d[] v)
        {
            if (v == null || v.Length < 2) return Vector3d.XAxis;
            double bestLen = -1.0;
            Vector3d best = Vector3d.XAxis;
            for (int i = 0; i < v.Length; i++)
            {
                Point3d a = v[i], b = v[(i + 1) % v.Length];
                var e = new Vector3d(b.X - a.X, b.Y - a.Y, 0);
                double L = e.Length;
                if (L > bestLen) { bestLen = L; best = e; }
            }
            return bestLen > 1e-9 ? best.GetNormal() : Vector3d.XAxis;
        }

        /// <summary>Bề rộng NHỎ NHẤT thật của đa giác (mm) = min qua MỌI cạnh của [span các đỉnh chiếu lên
        /// pháp tuyến cạnh đó]. Đúng cho strip mỏng mọi hình: chữ nhật, chữ L bẻ góc, vát 1:4, gusset —
        /// khác bbox <c>min(dx,dy)</c> vốn "phồng" cho member CHÉO / chữ L (vd web L dày 6mm bbox 2749×1777
        /// ⇒ bbox-min = 1777, SAI). Trả 0 nếu &lt; 3 đỉnh hoặc mọi cạnh suy biến.</summary>
        internal static double MinWidth(Point3d[] v)
        {
            if (v == null || v.Length < 3) return 0.0;
            int n = v.Length;
            double best = double.MaxValue;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double ex = v[i].X - v[j].X, ey = v[i].Y - v[j].Y;
                double elen = Math.Sqrt(ex * ex + ey * ey);
                if (elen < 1e-6) continue;                        // cạnh suy biến (2 đỉnh trùng)
                double nx = -ey / elen, ny = ex / elen;           // pháp tuyến đơn vị của cạnh
                double lo = double.MaxValue, hi = double.MinValue;
                for (int k = 0; k < n; k++)
                {
                    double proj = v[k].X * nx + v[k].Y * ny;
                    if (proj < lo) lo = proj;
                    if (proj > hi) hi = proj;
                }
                double w = hi - lo;
                if (w < best) best = w;
            }
            return best < double.MaxValue ? best : 0.0;
        }

        /// <summary>
        /// OBB (oriented bbox) của 1 member MỎNG từ <see cref="PanelPart.Vertices"/> THẬT: chiếu mọi đỉnh
        /// lên <see cref="LongAxis"/> và trục vuông góc.
        ///  - <paramref name="length"/> / <paramref name="thickness"/> = span theo 2 trục đó.
        ///  - <paramref name="endLo"/> / <paramref name="endHi"/> = 2 đầu mút, NẰM TRÊN ĐƯỜNG TÂM (giữa
        ///    bề dày), tại 2 mút của span trục dài.
        ///
        /// ⚠️ Thay 2 nhánh cũ của <see cref="Endpoints"/> ("so d01/d12" cho 4 đỉnh; "Mid(v0,v1)/Mid(v3,v4)"
        /// cho 5 đỉnh — xem skill panel-parts-reference Bug #11). Nhánh 5 đỉnh cũ ĐÚNG cho web-tier vát 1:4
        /// nhưng SAI cho tấm VÁT GÓC: ở đó `v0-v1` chỉ là NỬA cạnh đầu (snipe ăn mất nửa kia) → `Mid(v0,v1)`
        /// lệch ~5mm khỏi tâm thật → đường tâm NGHIÊNG → `27002`/`26E5A` (cùng hàn phẳng vào cạnh thẳng của
        /// `26FFF`) đo ra 11.8mm vs 13.3mm — nằm 2 bên ngưỡng `ADJ_TOL`=12 → 1 cái thành Bracket, 1 cái
        /// thành Web dù hình học y hệt. Chiếu lên trục vuông góc rồi lấy TÂM span thì snipe không ảnh hưởng.
        /// </summary>
        internal static void OrientedBox(Point3d[] v, out Vector3d axis, out double length,
            out double thickness, out Point3d endLo, out Point3d endHi)
        {
            axis = LongAxis(v);
            var perp = new Vector3d(-axis.Y, axis.X, 0);
            double aLo = double.MaxValue, aHi = double.MinValue, pLo = double.MaxValue, pHi = double.MinValue;
            foreach (var pt in v)
            {
                double ap = pt.X * axis.X + pt.Y * axis.Y;
                double pp = pt.X * perp.X + pt.Y * perp.Y;
                if (ap < aLo) aLo = ap;
                if (ap > aHi) aHi = ap;
                if (pp < pLo) pLo = pp;
                if (pp > pHi) pHi = pp;
            }
            length = aHi - aLo;
            thickness = pHi - pLo;
            double pMid = (pLo + pHi) / 2.0;
            endLo = new Point3d(axis.X * aLo + perp.X * pMid, axis.Y * aLo + perp.Y * pMid, 0);
            endHi = new Point3d(axis.X * aHi + perp.X * pMid, axis.Y * aHi + perp.Y * pMid, 0);
        }

        /// <summary>2 điểm mút của member (trên đường tâm, tại 2 mút trục dài) — <see cref="OrientedBox"/>
        /// từ <see cref="PanelPart.Vertices"/> THẬT (đúng cả member CHÉO / vát góc / vát 1:4).
        /// Fallback bbox nếu thiếu đỉnh.</summary>
        public static (Point3d, Point3d) Endpoints(PanelPart p)
        {
            var v = p.Vertices;
            if (v != null && v.Length >= 3)
            {
                OrientedBox(v, out _, out _, out _, out Point3d a, out Point3d b);
                return (a, b);
            }
            Extents3d b2 = p.Bounds;
            return p.IsHorizontal
                ? (new Point3d(b2.MinPoint.X, p.Centroid.Y, 0), new Point3d(b2.MaxPoint.X, p.Centroid.Y, 0))
                : (new Point3d(p.Centroid.X, b2.MinPoint.Y, 0), new Point3d(p.Centroid.X, b2.MaxPoint.Y, 0));
        }

        /// <summary>Khoảng cách 2D từ điểm tới ĐA GIÁC THẬT của member (0 nếu điểm nằm TRONG) qua
        /// <see cref="PanelPart.Vertices"/> — chính xác cho MỌI hình. Khác <see cref="DistToSegment"/>
        /// (đo tới ĐƯỜNG TÂM xấp xỉ): 1 part hàn phẳng vào mặt 1 tấm dày 20mm luôn cách đường tâm ~10mm
        /// (≈ hết <see cref="ADJ_TOL"/>=12) — chỉ cần đường tâm nghiêng nhẹ là "không chạm"; đo tới CẠNH
        /// thật thì phần nhô ra chỉ vài mm. Dùng cho quyết định chạm/không-chạm ở
        /// <see cref="IsBracketPattern"/> / <see cref="IsClosedEndsPattern"/> /
        /// <see cref="Builders.GeometryUtil.MinDistToWeb"/>. Fallback <see cref="DistToSegment"/> nếu thiếu đỉnh.</summary>
        public static double DistToPart(Point3d pt, PanelPart other)
        {
            var v = other.Vertices;
            if (v == null || v.Length < 3) return DistToSegment(pt, other);
            if (PointInPolygon(pt, v)) return 0.0;
            return DistToPolygonBoundary(pt, v);
        }

        /// <summary>Khoảng cách từ điểm tới ĐOẠN TÂM thật (xem <see cref="Endpoints"/>) của member —
        /// đúng cả member CHÉO, không bị bbox trục thẳng "phồng" như <see cref="DistToBox"/> (xem
        /// <see cref="IsDiagonalBloatedBbox"/>). Cho quyết định chạm/không-chạm, ưu tiên
        /// <see cref="DistToPart"/> (đo tới cạnh THẬT, không phải đường tâm).</summary>
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
        /// <see cref="DistToPart"/> (khoảng cách tới ĐA GIÁC thật, không phải đường tâm) nên xử lý đúng cả
        /// member CHÉO VÀ tấm neo DÀY 20mm (đo tới đường tâm luôn ~10mm ≈ hết ADJ_TOL — xem DistToPart).
        /// Kiểm chứng 7D-17C: khớp TUYỆT ĐỐI trên toàn bộ 108 part AM_0 (46 Bracket hiện có + tự sửa
        /// đúng cả 3 case sai từng phải vá bằng heuristic riêng: `38DF7`, `3916E`, `38DF2`).
        /// ⚠️ Sau khi đổi DistToSegment→DistToPart (2026-08-27) cần build lại 7D-17C xác nhận vẫn 108/108.
        /// </summary>
        public static bool IsBracketPattern(PanelPart p, IEnumerable<PanelPart> otherAm0,
            IEnumerable<PanelPart> hpParts, IEnumerable<PanelPart> fbParts, double tol)
        {
            var (e1, e2) = Endpoints(p);
            bool NearAm0(Point3d e) => otherAm0.Any(o => !ReferenceEquals(o, p) && DistToPart(e, o) <= tol);
            bool NearHpFb(Point3d e) => hpParts.Any(h => DistToPart(e, h) <= tol)
                                      || fbParts.Any(f => DistToPart(e, f) <= tol);
            return (NearAm0(e1) && NearHpFb(e2)) || (NearHpFb(e1) && NearAm0(e2));
        }

        /// <summary>
        /// <paramref name="p"/> khớp dáng "tấm kín" (ClosedPlate) theo 2 đầu mút — xác nhận kỹ sư
        /// 2026-08-26: <b>hoặc</b> CẢ HAI đầu không chạm gì (tấm cô lập hoàn toàn, chỉ nằm đè lên
        /// FacePlate), <b>hoặc</b> CẢ HAI đầu chạm member AM_0 khác (Web/Bracket — kẹp giữa 2 member).
        /// KHÔNG PHẢI ClosedPlate nếu: 1 đầu chạm HP/FlatBar (dù đầu kia là gì — đó là dáng Bracket/GE
        /// thật, xem <see cref="IsBracketPattern"/>), hoặc 1 đầu chạm AM_0 còn đầu kia không chạm gì
        /// (tấm "lửng lơ" 1 đầu — không phải "kín" theo đúng nghĩa cả 2 đầu đối xứng).
        /// Dùng <see cref="DistToPart"/> (khoảng cách tới ĐA GIÁC thật, không phải đường tâm) nên đúng cả
        /// member CHÉO VÀ tấm neo dày 20mm — cùng cặp hàm <c>NearAm0</c>/<c>NearHpFb</c> với
        /// <see cref="IsBracketPattern"/>. Bug thật: gusset chéo `2704E`/`27063` (nối tier ngang `27061`
        /// với tấm đứng vát góc `27050`) — đầu chạm `27050` đo tới đường tâm 5-đỉnh nghiêng ra ~18mm nên
        /// bị coi "lửng lơ 1 đầu" → rớt khỏi ClosedPlate → thành Web. Đo tới cạnh thật: ~4.5mm → đúng.
        /// </summary>
        public static bool IsClosedEndsPattern(PanelPart p, IEnumerable<PanelPart> otherAm0,
            IEnumerable<PanelPart> hpParts, IEnumerable<PanelPart> fbParts, double tol)
        {
            var (e1, e2) = Endpoints(p);
            bool NearAm0(Point3d e) => otherAm0.Any(o => !ReferenceEquals(o, p) && DistToPart(e, o) <= tol);
            bool NearHpFb(Point3d e) => hpParts.Any(h => DistToPart(e, h) <= tol)
                                      || fbParts.Any(f => DistToPart(e, f) <= tol);

            bool e1Am0 = NearAm0(e1), e1HpFb = NearHpFb(e1);
            bool e2Am0 = NearAm0(e2), e2HpFb = NearHpFb(e2);
            bool e1Free = !e1Am0 && !e1HpFb;
            bool e2Free = !e2Am0 && !e2HpFb;

            bool bothFree = e1Free && e2Free;
            bool bothAm0Only = e1Am0 && e2Am0 && !e1HpFb && !e2HpFb;
            return bothFree || bothAm0Only;
        }

        /// <summary>
        /// <paramref name="m"/> (HP hoặc member AM_0 khác — Web/tier/Bracket/ClosedPlate) ĐI XUYÊN NGANG
        /// qua thân <paramref name="p"/> (FlatBar) — không phải chỉ chạm ở 1 đầu mút, cũng không phải kết
        /// thúc TẠI FB (T-junction). Dùng cho <see cref="FlatBarSplitter"/>: người vẽ 2D kéo 1 polyline FB
        /// LIÊN TỤC xuyên qua HP/Web cho nhanh, nhưng THỰC TẾ FB đứt tại đó — kết thúc ở 2 MẶT của member
        /// cắt ngang, chừa khe = bề dày member (xác nhận kỹ sư 2026-08-27; xem skill sniped-end).
        ///
        /// <paramref name="tCross"/> = vị trí điểm cắt (giao 2 đường tâm) chiếu lên trục dài <paramref name="p"/>
        /// (từ đầu <c>e1</c>), đơn vị mm — <see cref="FlatBarSplitter"/> dùng làm tâm khe.
        ///
        /// Điều kiện: (1) bbox chồng lấn thật; (2) 2 member KHÔNG gần song song (|cos| ≤ 0.85 — Web chạy
        /// DỌC cạnh FB không phải "đi ngang qua"); (3) giao 2 đường tâm nằm cách 2 đầu <paramref name="p"/>
        /// ≥ <see cref="FB_SPLIT_MARGIN"/> (chạm-đầu → IsFreeEnd lo, không tách); (4) giao đó nằm TRONG
        /// <paramref name="m"/> (m kéo dài qua cả 2 phía FB — T-junction bị loại).
        /// </summary>
        public static bool CrossesThrough(PanelPart p, PanelPart m, out double tCross)
        {
            tCross = 0;
            var (e1, e2) = Endpoints(p);
            double L = e1.DistanceTo(e2);
            if (L < 1e-6) return false;
            Vector3d dir = (e2 - e1) / L;

            if (OverlapArea(p, m) <= 0.0) return false;

            var (m1, m2) = Endpoints(m);
            double Lm = m1.DistanceTo(m2);
            if (Lm < 1e-6) return false;
            Vector3d mdir = (m2 - m1) / Lm;

            double den = dir.X * mdir.Y - dir.Y * mdir.X;
            if (Math.Abs(den) < 1e-9) return false;                          // song song
            if (Math.Abs(dir.X * mdir.X + dir.Y * mdir.Y) > 0.85) return false; // gần song song

            double t = ((m1.X - e1.X) * mdir.Y - (m1.Y - e1.Y) * mdir.X) / den;
            if (t < FB_SPLIT_MARGIN || t > L - FB_SPLIT_MARGIN) return false;   // sát/ngoài đầu mút p
            double sx = e1.X + dir.X * t, sy = e1.Y + dir.Y * t;
            double s = (sx - m1.X) * mdir.X + (sy - m1.Y) * mdir.Y;
            double marg = Math.Max(p.FootprintThickness, 3.0);
            if (s < marg || s > Lm - marg) return false;                       // m kết thúc TẠI FB (T-junction)

            tCross = t;
            return true;
        }
    }
}
