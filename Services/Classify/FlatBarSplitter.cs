using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCG_3DPanel.Models;

namespace MCG_3DPanel.Services.Classify
{
    /// <summary>
    /// Tách FlatBar (FB) tại các vị trí bị HP CẮT NGANG giữa thân (xem <see cref="ClassifyUtil.CrossesThrough"/>).
    /// Người vẽ 2D thường kéo 1 polyline FB LIÊN TỤC xuyên qua HP cho nhanh, nhưng THỰC TẾ FB không liên
    /// tục — nó kết thúc tại đúng vị trí giao với HP ở cả 2 phía (xác nhận kỹ sư, xem skill sniped-end).
    /// Phải tách polyline FB tại những chỗ này TRƯỚC khi tìm "đầu tự do" — nếu không, 2 đầu mới sinh ra ở
    /// đó vẫn nằm giữa 1 polyline duy nhất, không phải 1 trong 2 <see cref="ClassifyUtil.Endpoints"/> thật.
    ///
    /// Chạy SAU khi <c>hpParts</c> đã có đủ (route theo layer xong), TRƯỚC khi FB được thêm vào
    /// <see cref="ClassificationResult.Parts"/>. Không có HP nào cắt ngang thật → trả nguyên polyline gốc
    /// (hành vi không đổi so với trước khi có tính năng này).
    /// </summary>
    internal static class FlatBarSplitter
    {
        public static List<PanelPart> Split(List<PanelPart> fbParts, List<PanelPart> hpParts)
        {
            var result = new List<PanelPart>(fbParts.Count);
            foreach (var p in fbParts)
                result.AddRange(SplitOne(p, hpParts));
            return result;
        }

        private static List<PanelPart> SplitOne(PanelPart p, List<PanelPart> hpParts)
        {
            var (e1, e2) = ClassifyUtil.Endpoints(p);
            double L = e1.DistanceTo(e2);
            if (L < 1e-6) { p.FbTotalPlanLength = L; return new List<PanelPart> { p }; }
            Vector3d dir = (e2 - e1) / L;

            var cuts = new List<double>();
            foreach (var h in hpParts)
            {
                if (!ClassifyUtil.Adjacent(p, h, ClassifyUtil.ADJ_TOL)) continue;
                if (!ClassifyUtil.CrossesThrough(p, h)) continue;
                double t = (h.Centroid.X - e1.X) * dir.X + (h.Centroid.Y - e1.Y) * dir.Y;
                t = Math.Max(ClassifyUtil.FB_SPLIT_MARGIN, Math.Min(L - ClassifyUtil.FB_SPLIT_MARGIN, t));
                cuts.Add(t);
            }
            if (cuts.Count == 0) { p.FbTotalPlanLength = L; return new List<PanelPart> { p }; }

            cuts.Sort();
            // Gộp các điểm cắt quá gần nhau (nhiều HP chạm cùng 1 chỗ, hoặc dung sai vẽ) thành 1.
            var merged = new List<double>();
            foreach (var t in cuts)
                if (merged.Count == 0 || t - merged[merged.Count - 1] > ClassifyUtil.FB_SPLIT_MARGIN)
                    merged.Add(t);

            var segments = new List<PanelPart>(merged.Count + 1);
            double prev = 0;
            foreach (var t in merged)
            {
                segments.Add(BuildSegment(p, e1, dir, prev, t, L));
                prev = t;
            }
            segments.Add(BuildSegment(p, e1, dir, prev, L, L));
            return segments;
        }

        /// <summary>1 đoạn con [tStart,tEnd] dọc <paramref name="dir"/> từ <paramref name="origin"/> — giữ
        /// nguyên Layer/Color/Transform/Id của polyline gốc (chỉ để tham khảo/report). ⚠️ KHÔNG dùng để
        /// clone Region từ DB entity nữa (1 Id gốc giờ ứng với nhiều đoạn) — <see cref="Builders.FlatBarBuilder"/>
        /// luôn tự dựng outline từ tham số hình học (Vertices/MemberLength/FootprintThickness) thay vì
        /// clone entity gốc như <see cref="Builders.PlateBuilder"/> làm cho các loại không bị tách.
        /// <paramref name="totalLen"/> = TỔNG chiều dài đường vẽ gốc (TRƯỚC khi tách) — lưu riêng vào
        /// <see cref="PanelPart.FbTotalPlanLength"/>, KHÁC <see cref="PanelPart.MemberLength"/> (chiều
        /// dài của RIÊNG đoạn này) — xác nhận kỹ sư: ngưỡng "1 khoảng HP" so với TỔNG, không phải từng
        /// đoạn (xem skill sniped-end).</summary>
        private static PanelPart BuildSegment(PanelPart src, Point3d origin, Vector3d dir, double tStart, double tEnd, double totalLen)
        {
            Point3d a = origin + dir * tStart;
            Point3d b = origin + dir * tEnd;
            var perp = new Vector3d(-dir.Y, dir.X, 0) * (src.FootprintThickness / 2.0);

            var verts = new[] { a + perp, b + perp, b - perp, a - perp };
            double minX = verts.Min(v => v.X), maxX = verts.Max(v => v.X);
            double minY = verts.Min(v => v.Y), maxY = verts.Max(v => v.Y);
            var bounds = new Extents3d(new Point3d(minX, minY, 0), new Point3d(maxX, maxY, 0));
            var centroid = new Point3d((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0, 0);
            bool horiz = (maxX - minX) >= (maxY - minY);

            return new PanelPart
            {
                Id = src.Id,
                IdPath = src.IdPath,
                Type = PartType.FlatBar,
                Layer = src.Layer,
                ColorIndex = src.ColorIndex,
                VertexCount = 4,
                FootprintThickness = src.FootprintThickness,
                MemberLength = tEnd - tStart,
                FbTotalPlanLength = totalLen,
                Area = (tEnd - tStart) * src.FootprintThickness,
                Centroid = centroid,
                Bounds = bounds,
                Vertices = verts,
                Transform = src.Transform,
                AxisDir = dir,
                IsHorizontal = horiz,
                PerpCenter = horiz ? centroid.Y : centroid.X,
            };
        }
    }
}
