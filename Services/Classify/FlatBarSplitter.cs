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
        /// <param name="crossMembers">HP + MỌI member AM_0 (Web/tier/Bracket/ClosedPlate — xác nhận kỹ sư
        /// 2026-08-27) có thể ĐI XUYÊN NGANG qua FB. Tại thời điểm gọi (trong <c>ClassificationService</c>,
        /// trước bước phân loại AM_0) các part này còn <see cref="PartType.Unknown"/> — chỉ dùng hình học
        /// (<see cref="ClassifyUtil.CrossesThrough"/> tự lọc T-junction / gần song song).</param>
        public static List<PanelPart> Split(List<PanelPart> fbParts, List<PanelPart> hpParts,
            List<PanelPart> crossMembers)
        {
            var all = new List<PanelPart>(hpParts.Count + crossMembers.Count);
            all.AddRange(hpParts);
            all.AddRange(crossMembers);
            var result = new List<PanelPart>(fbParts.Count);
            foreach (var p in fbParts)
                result.AddRange(SplitOne(p, all));
            return result;
        }

        private static List<PanelPart> SplitOne(PanelPart p, List<PanelPart> crossers)
        {
            var (e1, e2) = ClassifyUtil.Endpoints(p);
            double L = e1.DistanceTo(e2);
            if (L < 1e-6) { p.FbTotalPlanLength = L; return new List<PanelPart> { p }; }
            Vector3d dir = (e2 - e1) / L;

            // (t = tâm khe dọc trục FB, half = nửa bề dày member cắt ngang → khe = 2·half).
            var cuts = new List<(double t, double half)>();
            foreach (var m in crossers)
            {
                if (ReferenceEquals(m, p)) continue;
                if (!ClassifyUtil.Adjacent(p, m, ClassifyUtil.ADJ_TOL)) continue;
                if (!ClassifyUtil.CrossesThrough(p, m, out double t)) continue;
                t = Math.Max(ClassifyUtil.FB_SPLIT_MARGIN, Math.Min(L - ClassifyUtil.FB_SPLIT_MARGIN, t));
                cuts.Add((t, Math.Max(0.5, m.FootprintThickness / 2.0)));
            }
            if (cuts.Count == 0) { p.FbTotalPlanLength = L; return new List<PanelPart> { p }; }

            cuts.Sort((a, b) => a.t.CompareTo(b.t));
            // Gộp các điểm cắt quá gần nhau (nhiều member chạm cùng 1 chỗ, hoặc dung sai vẽ) — giữ khe LỚN nhất.
            var merged = new List<(double t, double half)>();
            foreach (var c in cuts)
                if (merged.Count == 0 || c.t - merged[merged.Count - 1].t > ClassifyUtil.FB_SPLIT_MARGIN)
                    merged.Add(c);
                else if (c.half > merged[merged.Count - 1].half)
                    merged[merged.Count - 1] = (merged[merged.Count - 1].t, c.half);

            var segments = new List<PanelPart>(merged.Count + 1);
            double prev = 0;
            foreach (var c in merged)
            {
                double segEnd = c.t - c.half;                 // dừng tại MẶT member (chừa khe)
                if (segEnd - prev > 1.0) segments.Add(BuildSegment(p, e1, dir, prev, segEnd, L));
                prev = c.t + c.half;                          // đoạn kế bắt đầu ở mặt kia
            }
            if (L - prev > 1.0) segments.Add(BuildSegment(p, e1, dir, prev, L, L));
            if (segments.Count == 0) { p.FbTotalPlanLength = L; return new List<PanelPart> { p }; }
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
