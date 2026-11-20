using System.Collections.Generic;

namespace MCG_3DPanel.Services.Update
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.*.

    /// <summary>Câu báo cuối của Update từ bản 2D (spec 2026-11-20 3d-controls-in-2d): 3D → Plan View → Details; phần nào lỗi nêu tên,
    /// phần nào chưa chạy vì lỗi trước nói rõ.</summary>
    public static class UpdateChainRules
    {
        /// <param name="planView">Panel có Plan View (đã chạy vẽ lại).</param>
        /// <param name="detail">Panel có Detail (đã / sẽ chạy Update Details).</param>
        /// <param name="applied">Số sửa chờ từ bản 2D đã áp vào 3D.</param>
        public static string Summary(bool planView, string planErr, bool detail, string detailErr, int applied)
        {
            string s;
            if (planView && planErr != null)
                s = $"3D updated; Plan View failed: {planErr}." + (detail ? " Details were not updated." : "");
            else
            {
                var done = new List<string> { "3D" };
                if (planView) done.Add("Plan View");
                if (detail && detailErr == null) done.Add("Details");
                s = Join(done) + " updated" + (detail && detailErr != null ? $"; Details failed: {detailErr}." : ".");
            }
            if (applied > 0) s += $" {applied} edit{(applied > 1 ? "s" : "")} from the 2D drawing applied.";
            return s;
        }

        static string Join(List<string> items) =>
            items.Count == 1 ? items[0] : string.Join(", ", items.GetRange(0, items.Count - 1)) + " and " + items[items.Count - 1];
    }
}
