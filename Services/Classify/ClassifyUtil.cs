using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

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
        public const double HP_MIN_LEN = 500.0;         // HP đủ dài mới xét bụng/lưng
        public const double EDGE_TOL = 50.0;            // dung sai xác định HP "ngoài cùng"
        public const double ADJ_TOL = 12.0;             // dung sai kề nhau (bracket chạm HP/FB)
        public const double TOL_GAP = 20.0;             // khe hở dọc trục khi gom Web collinear
        public const double TOL_PERP = 8.0;             // lệch ngang khi gom Web collinear

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
    }
}
