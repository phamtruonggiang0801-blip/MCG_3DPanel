using System;

namespace MCG_3DPanel.Services.Detail
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.*.

    /// <summary>Luật dọn đối tượng tạm của bộ cắt section.</summary>
    public static class SectionCleanupRules
    {
        /// <summary>Chỉ đối tượng có handle ≥ Handseed ghi trước khi chạy mới là của lần chạy này — không bao giờ xoá bản ghi có sẵn.</summary>
        public static bool CreatedThisRun(long handseed, long handle) => handle >= handseed;

        /// <summary>Lỗi gốc + lỗi dọn dẹp trong một thông báo.</summary>
        public static string FailureMessage(string original, string cleanup) =>
            original + " (cleanup also failed: " + cleanup + ")";

        /// <summary>"n text" khi n &gt; 0, ngược lại null.</summary>
        public static string Count(int n, string text) => n > 0 ? n + " " + text : null;

        /// <summary>Kết quả gộp (union) hợp lý: thể tích sau nằm trong [max(trước, tấm), trước + tấm] (± sai số tương đối
        /// <paramref name="rel"/>). DET.19 (2026-11-19): một lần gộp lỗi làm khối gộp mất cả flange + HP ⇒ thể tích tụt.
        /// Không đo được thể tích sau ⇒ khối hỏng; không đo được trước / tấm ⇒ không kiểm.</summary>
        public static bool UnionPlausible(double? before, double? part, double? after, double rel = 1e-3)
        {
            if (after == null || double.IsNaN(after.Value) || after.Value <= 0) return false;
            if (before == null || part == null) return true;
            double b = before.Value, p = part.Value, a = after.Value;
            double eps = rel * (b + p) + 1.0;
            return a >= Math.Max(b, p) - eps && a <= b + p + eps;
        }
    }
}
