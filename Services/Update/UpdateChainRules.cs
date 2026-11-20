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

        /// <summary>Câu báo Update Drawing (kỹ sư 2026-11-20): vẽ lại Plan View + Details từ 3D hiện có, áp tên đang chờ; sửa hình học
        /// vẫn chờ Update 3D Model.</summary>
        /// <param name="names">Số tên chờ đã đưa lên bản vẽ.</param>
        /// <param name="stillWaiting">Số sửa hình học còn chờ Update 3D Model.</param>
        public static string DrawingSummary(bool planView, string planErr, bool detail, string detailErr, int names, int stillWaiting)
        {
            if (!planView && !detail) return "This panel has no Plan View or details yet — use Create Drawing.";
            string s;
            if (planView && planErr != null)
                s = $"Plan View failed: {planErr}." + (detail ? " Details were not updated." : "");
            else
            {
                var done = new List<string>();
                if (planView) done.Add("Plan View");
                if (detail && detailErr == null) done.Add("Details");
                string with = names > 0 ? $" with {names} waiting name{(names > 1 ? "s" : "")}" : "";
                s = done.Count > 0 ? Join(done) + " updated" + with : "";
                if (detail && detailErr != null) s += (s.Length > 0 ? "; " : "") + $"Details failed: {detailErr}";
                s += ".";
            }
            if (stillWaiting > 0)
                s += $" {stillWaiting} edit{(stillWaiting > 1 ? "s" : "")} still wait{(stillWaiting > 1 ? "" : "s")} for Update 3D Model.";
            return s;
        }

        static string Join(List<string> items) =>
            items.Count == 1 ? items[0] : string.Join(", ", items.GetRange(0, items.Count - 1)) + " and " + items[items.Count - 1];
    }
}
