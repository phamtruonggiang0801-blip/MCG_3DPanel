using System;
using System.Collections.Generic;
using System.Linq;

namespace MCG_3DPanel.Services.Detail
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>Quy tắc của lệnh Detail Plan / Detail View: gốc block, dữ liệu vùng cho bản ghi, chỗ đặt nhãn DET.n,
    /// tiêu đề Detail View, câu hỏi 3D cũ, dòng ghi chú cho status.</summary>
    public static class DetailCommandRules
    {
        /// <summary>Gốc block DET (toạ độ plan) = tâm tròn / trọng tâm 4 góc.</summary>
        public static P2 Centre(DetailRegion r)
        {
            if (r.IsCircle) return r.Center;
            double x = 0, y = 0;
            foreach (var c in r.Corners) { x += c.X; y += c.Y; }
            return new P2(x / r.Corners.Length, y / r.Corners.Length);
        }

        /// <summary>DetailRecord.Region: 4 góc [x,y] hoặc 1 bộ [cx,cy,r].</summary>
        public static List<double[]> RegionData(DetailRegion r) => r.IsCircle
            ? new List<double[]> { new[] { r.Center.X, r.Center.Y, r.Radius } }
            : r.Corners.Select(c => new[] { c.X, c.Y }).ToList();

        /// <summary>Điểm trái-dưới của nhãn DET.n: góc trên-phải của biên (x+y lớn nhất, hoà ⇒ y cao hơn; tròn ⇒ điểm 45°)
        /// dời thêm <paramref name="gap"/> theo +X, +Y.</summary>
        public static P2 LabelAt(DetailRegion r, double gap)
        {
            P2 corner;
            if (r.IsCircle)
            {
                double k = r.Radius / Math.Sqrt(2);
                corner = new P2(r.Center.X + k, r.Center.Y + k);
            }
            else
            {
                const double tol = 1e-6;
                corner = r.Corners[0];
                foreach (var c in r.Corners.Skip(1))
                {
                    double d = (c.X + c.Y) - (corner.X + corner.Y);
                    if (d > tol || (Math.Abs(d) <= tol && c.Y > corner.Y)) corner = c;
                }
            }
            return new P2(corner.X + gap, corner.Y + gap);
        }

        /// <summary>Tiêu đề Detail View: tên gõ (rỗng ⇒ "DETAIL n") có "%%U" phía trước (không lặp nếu đã gõ).</summary>
        public static string ViewTitle(string typed, int n)
        {
            if (string.IsNullOrWhiteSpace(typed)) return DetailNaming.DetailTitle(n);
            return typed.StartsWith("%%U", StringComparison.OrdinalIgnoreCase) ? typed : "%%U" + typed;
        }

        /// <summary>Câu hỏi Continue/Cancel khi file 3D có thể đã cũ; Fresh/Unknown ⇒ null (không hỏi).</summary>
        public static string StaleQuestion(StaleState s, string file3D, string panel)
        {
            switch (s)
            {
                case StaleState.Unsaved3D:
                    return $"{file3D} has unsaved changes. The detail is cut from the saved file, so it may not match what you see in 3D.\n\nContinue anyway?";
                case StaleState.Changed2D:
                    return $"The {panel} block changed in this drawing after {file3D} was built. The 3D file may be out of date (run Build/Update 3D first).\n\nContinue anyway?";
                default:
                    return null;
            }
        }

        /// <summary>Ghi chú status: các cảnh báo khác nhau, nối "; ", kết thúc bằng "."; không có ⇒ "".</summary>
        public static string Notes(IEnumerable<string> warnings)
        {
            var list = (warnings ?? Enumerable.Empty<string>())
                .Where(w => !string.IsNullOrWhiteSpace(w))
                .Select(w => w.Trim().TrimEnd('.'))
                .Distinct(StringComparer.Ordinal).ToList();
            return list.Count == 0 ? "" : "Notes: " + string.Join("; ", list) + ".";
        }
    }
}
