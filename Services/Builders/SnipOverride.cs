using Autodesk.AutoCAD.Geometry;
using MCG_3DPanel.Services.Classify;
using MCG_3DPanel.Services.Corrections;
using MCG_3DPanel.UI;

namespace MCG_3DPanel.Services.Builders
{
    /// <summary>
    /// Sniped End theo từng đầu (tab Result, kỹ sư 2026-11-04): correction lo/hi của part THẮNG quyết định tự động.
    /// lo/hi theo <see cref="ResultCatalog.EndLabels"/> (ngang: L/R theo X · đứng: B/T theo Y).
    /// </summary>
    internal static class SnipOverride
    {
        /// <summary>Đầu <paramref name="end"/> là đầu "lo" của part (X nhỏ khi thanh ngang, Y nhỏ khi thanh đứng).</summary>
        public static bool IsLo(PanelPart p, Point3d end)
        {
            var (a, b) = ClassifyUtil.Endpoints(p);
            Point3d other = end.DistanceTo(a) <= end.DistanceTo(b) ? b : a;
            double dx = b.X - a.X, dy = b.Y - a.Y;
            bool horiz = ResultCatalog.EndLabels(dx, dy).lo == "L";
            return horiz ? end.X <= other.X : end.Y <= other.Y;
        }

        /// <summary>Override của người dùng cho đầu này (null = không ép).</summary>
        public static bool? Forced(ClassificationResult cls, PanelPart p, Point3d end)
        {
            if (cls?.Corrections == null || p.SourceKey == null || !cls.Corrections.TryGetValue(p.SourceKey, out PartCorrection c))
                return null;
            return IsLo(p, end) ? c.SnipLo : c.SnipHi;
        }

        /// <summary>Vát hay không: override ?? <paramref name="auto"/>; ghi kết quả thực tế vào part.SnipLo/SnipHi.</summary>
        public static bool Decide(ClassificationResult cls, PanelPart p, Point3d end, bool auto)
        {
            bool snip = Forced(cls, p, end) ?? auto;
            if (IsLo(p, end)) p.SnipLo = snip; else p.SnipHi = snip;
            return snip;
        }
    }
}
