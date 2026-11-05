using System;
using System.Collections.Generic;
using System.Linq;

namespace MCG_3DPanel.UI
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests để unit test).

    /// <summary>Kết quả lập kế hoạch sửa hàng loạt: dòng sẽ đổi + số dòng bỏ qua theo lý do.</summary>
    public sealed class BulkPlan<T>
    {
        public List<T> Targets { get; set; } = new List<T>();
        /// <summary>Dòng đang chọn nhưng không có control này (vd Tripping trên dòng Web).</summary>
        public int Unsupported { get; set; }
        /// <summary>Dòng thuộc drawing KHÁC drawing đang mở — không lưu gì (ux-guardrails luật 1).</summary>
        public int OtherDrawing { get; set; }
    }

    /// <summary>
    /// Sửa hàng loạt kiểu Excel (kỹ sư 2026-11-05): đổi 1 control trên dòng THUỘC vùng chọn ⇒ CÙNG giá trị cho mọi dòng
    /// đang chọn hỗ trợ control đó (như Ctrl+Enter); dòng NGOÀI vùng chọn ⇒ chỉ dòng đó.
    /// </summary>
    public static class BulkEdit
    {
        public static BulkPlan<T> Plan<T>(T clicked, IReadOnlyList<T> selected, Func<T, bool> supports, Func<T, bool> inActiveDrawing)
            where T : class
        {
            var plan = new BulkPlan<T>();
            var scope = selected != null && selected.Count > 1 && selected.Contains(clicked)
                ? selected
                : (IReadOnlyList<T>)new List<T> { clicked };
            foreach (var r in scope)
            {
                if (r == null) continue;
                if (!supports(r)) { plan.Unsupported++; continue; }
                if (!inActiveDrawing(r)) { plan.OtherDrawing++; continue; }
                plan.Targets.Add(r);
            }
            return plan;
        }

        /// <summary>"Tripping = On on 5 rows · 2 skipped (not applicable) · 1 skipped (other drawing)".</summary>
        public static string Describe<T>(BulkPlan<T> plan, string what)
        {
            int n = plan?.Targets?.Count ?? 0;
            var s = $"{what} on {n} row{(n == 1 ? "" : "s")}";
            if (plan != null && plan.Unsupported > 0) s += $" · {plan.Unsupported} skipped (not applicable)";
            if (plan != null && plan.OtherDrawing > 0) s += $" · {plan.OtherDrawing} skipped (other drawing)";
            return s;
        }

        /// <summary>Giữ phần tử ĐẦU TIÊN của mỗi khoá (thứ tự giữ nguyên); <paramref name="dropped"/> = số bị bỏ.
        /// Tab Naming: 2 lần chèn cùng 1 block 2D dùng chung 1 bộ tên ⇒ chỉ liệt kê 1 lần (review I2).</summary>
        public static List<T> OnePer<T>(IEnumerable<T> items, Func<T, string> key, out int dropped)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var kept = new List<T>();
            dropped = 0;
            foreach (var it in items ?? Enumerable.Empty<T>())
            {
                if (seen.Add(key(it) ?? "")) kept.Add(it);
                else dropped++;
            }
            return kept;
        }
    }
}
