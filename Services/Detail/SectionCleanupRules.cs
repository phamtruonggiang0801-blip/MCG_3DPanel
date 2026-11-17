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
    }
}
