using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

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

        /// <summary>Leader chỉ vào biên như Leader Note (AMNOTE) trên 5 mẫu: mũi tên tại góc trên-phải của biên, chéo 45° ra ngoài
        /// <paramref name="rise"/> rồi đoạn ngang đỡ chữ dài bằng chữ + 2·<paramref name="gap"/>; chữ (trái-dưới) nằm trên đoạn ngang.</summary>
        public static (P2 Head, P2 Elbow, P2 End, P2 Text) LabelLeader(DetailRegion r, double textWidth, double rise, double gap)
        {
            P2 head = LabelAt(r, 0);
            P2 elbow = new P2(head.X + rise, head.Y + rise);
            return (head, elbow, new P2(elbow.X + textWidth + 2 * gap, elbow.Y), new P2(elbow.X + gap, elbow.Y + gap));
        }

        /// <summary>Tên gõ không có "%%U" (chữ ở leader); rỗng ⇒ "DETAIL n".</summary>
        public static string PlainName(string title, int n)
        {
            string t = string.IsNullOrWhiteSpace(title) ? DetailNaming.DetailTitle(n) : title.Trim();
            return t.StartsWith("%%U", StringComparison.OrdinalIgnoreCase) ? t.Substring(3) : t;
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
                    return $"{file3D} has unsaved changes. The detail is cut from the open, unsaved 3D model, so it will not match {file3D} on disk until you save it.\n\nContinue anyway?";
                case StaleState.Changed2D:
                    return $"The {panel} block changed in this drawing after {file3D} was built. The 3D file may be out of date (run Build/Update 3D first).\n\nContinue anyway?";
                default:
                    return null;
            }
        }

        /// <summary>Ghi chú status sau khi người dùng chọn tiếp tục với 3D chưa lưu / có thể cũ.</summary>
        public static string StaleNote(StaleState s, string file3D)
            => s == StaleState.Unsaved3D
                ? $"cut from the open, unsaved {file3D} (save it so the file matches this detail)"
                : $"{file3D} may be out of date";

        // ─── Section Detail ────────────────────────────────────────────────────

        /// <summary>Lệnh palette được phép gửi qua Request (chỉ đúng tên lệnh, không kèm gì khác).</summary>
        public static readonly string[] Commands = { "MCG_DetailPlan", "MCG_DetailSection", "MCG_DetailView", "MCG_DetailUpdate" };

        public static bool IsCommand(string command) =>
            !string.IsNullOrWhiteSpace(command) && Commands.Contains(command.Trim(), StringComparer.OrdinalIgnoreCase);

        /// <summary>Ngược của <see cref="RegionData"/>: 4 góc [x,y] (phải là chữ nhật) hoặc 1 bộ [cx,cy,r] (r &gt; 0). Hỏng ⇒ null.</summary>
        public static DetailRegion RegionFromData(List<double[]> data, bool circle)
        {
            if (data == null || data.Any(p => p == null)) return null;
            if (circle)
            {
                if (data.Count != 1 || data[0].Length != 3 || !(data[0][2] > 0)) return null;
                return DetailRegion.FromCircle(new P2(data[0][0], data[0][1]), data[0][2]);
            }
            if (data.Count != 4 || data.Any(p => p.Length != 2)) return null;
            return DetailRegion.FromPolyline(data.Select(p => (p[0], p[1], 0.0)).ToList(), true, out _);
        }

        /// <summary>Chữ section đã dùng của DET n: bản ghi Section cùng panel + số, và tên block "[NEW_]&lt;panel&gt;_DET.n_nL-nL…"
        /// (kể cả block vẽ tay / hậu tố _MCG).</summary>
        public static IEnumerable<string> UsedLetters(string panel, int n, IEnumerable<DetailRecord> records, IEnumerable<string> blockNames)
        {
            var used = new List<string>();
            foreach (var r in records ?? Enumerable.Empty<DetailRecord>())
                if (r != null && r.Kind == DetailKind.Section && r.Number == n && string.Equals(r.Panel, panel, StringComparison.Ordinal)
                    && !string.IsNullOrEmpty(r.Letter)) used.Add(r.Letter);
            if (!string.IsNullOrEmpty(panel))
            {
                var re = new Regex("^(?:NEW_)?" + Regex.Escape(panel) + @"_DET\." + n + "_" + n + "([A-Za-z]+)-" + n + @"\1(?:_|$)",
                    RegexOptions.CultureInvariant);
                foreach (var name in blockNames ?? Enumerable.Empty<string>())
                {
                    var m = name == null ? null : re.Match(name);
                    if (m != null && m.Success) used.Add(m.Groups[1].Value.ToUpperInvariant());
                }
            }
            return used.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Vì sao <see cref="SectionFrame.From"/> trả null (toạ độ plan); hợp lệ ⇒ null.
        /// <paramref name="pickEndAgain"/> = true ⇒ hỏi lại điểm cuối (rồi hướng nhìn), false ⇒ chỉ hỏi lại hướng nhìn.</summary>
        public static string FrameProblem(P2 start, P2 end, P2 side, DetailRegion region, out bool pickEndAgain)
        {
            pickEndAgain = false;
            if (SectionFrame.From(start, end, side, region) != null) return null;
            string line = CutLineProblem(start, end, region);
            if (line != null) { pickEndAgain = true; return line; }
            return "The viewing side is on the cut line. Pick a point to one side of it.";
        }

        /// <summary>Chiều sâu khung Section = khoảng cách điểm cạnh đối tới đường cắt a–b.</summary>
        public static double SectionDepth(P2 a, P2 b, P2 side)
        {
            var d = b - a;
            double l = d.Length;
            return l < 1e-9 ? 0 : Math.Abs(d.X * (side.Y - a.Y) - d.Y * (side.X - a.X)) / l;
        }

        /// <summary>Đường cắt (chưa có bên nhìn) dùng được không: không dài / không qua vùng ⇒ lý do; được ⇒ null.</summary>
        public static string CutLineProblem(P2 start, P2 end, DetailRegion region)
        {
            var d = end - start;
            if (d.Length < 1e-6) return "The cut line has no length. Pick the end point again.";
            // Bên nhìn đặt xa hẳn đường cắt mà vẫn null ⇒ đường cắt không qua vùng
            var far = new P2(-d.Y, d.X) * (1.0 / d.Length) * 1e3;
            if (SectionFrame.From(start, end, start + far, region) == null)
                return "The cut line does not cross the detail. Pick the end point again.";
            return null;
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
