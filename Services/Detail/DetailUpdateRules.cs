using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MCG_3DPanel.Services.Detail
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>Quy tắc của Update Details: chữ ký nội dung block (đếm + khung bao làm tròn), nhận nhãn DET.n / tiêu đề,
    /// section của 1 DET, dời nhãn, giữ gốc block section, thứ tự, thông báo.</summary>
    public static class DetailUpdateRules
    {
        /// <summary>Chữ ký nội dung block: số entity + (loại|layer|khung bao làm tròn 1 mm) đã sắp xếp — không phụ thuộc thứ tự.
        /// <c>ext</c> = [minX, minY, maxX, maxY] hoặc null (entity không có khung bao).</summary>
        public static string Signature(IEnumerable<(string kind, string layer, double[] ext)> ents)
        {
            var keys = (ents ?? Enumerable.Empty<(string, string, double[])>())
                .Select(e => (e.kind ?? "") + "|" + (e.layer ?? "").ToUpperInvariant() + "|"
                    + (e.ext == null ? "-" : string.Join(",", e.ext.Select(v => Math.Round(v).ToString("0", CultureInfo.InvariantCulture)))))
                .OrderBy(k => k, StringComparer.Ordinal).ToList();
            return keys.Count.ToString(CultureInfo.InvariantCulture) + ";" + string.Join(";", keys);
        }

        public static bool Changed(string before, string after) => !string.Equals(before, after, StringComparison.Ordinal);

        /// <summary>DBText / Leader là nhãn của <paramref name="det"/> (Plan/View): bản ghi Plan/View cùng panel + số.</summary>
        public static bool IsLabelOf(DetailRecord textRec, DetailRecord det)
        {
            if (textRec == null || det == null || det.Kind == DetailKind.Section || textRec.Kind == DetailKind.Section) return false;
            // Không so chữ: tên do người dùng gõ (kỹ sư 2026-11-18); nhãn = DBText / Leader trong Model Space mang bản ghi
            return string.Equals(textRec.Panel, det.Panel, StringComparison.Ordinal) && textRec.Number == det.Number;
        }

        /// <summary>Cùng 1 detail: loại, panel, số, chữ (section).</summary>
        public static bool SameDetail(DetailRecord a, DetailRecord b) =>
            a != null && b != null && a.Kind == b.Kind && a.Number == b.Number
            && string.Equals(a.Panel, b.Panel, StringComparison.Ordinal)
            && string.Equals(a.Letter ?? "", b.Letter ?? "", StringComparison.OrdinalIgnoreCase);

        /// <summary>Bản ghi Section của DET <paramref name="det"/> (cùng panel + số), mỗi chữ 1 lần, theo chữ.</summary>
        public static List<DetailRecord> SectionsOf(IEnumerable<DetailRecord> recs, DetailRecord det) =>
            (recs ?? Enumerable.Empty<DetailRecord>())
                .Where(r => r != null && r.Kind == DetailKind.Section && r.Number == det.Number
                    && string.Equals(r.Panel, det.Panel, StringComparison.Ordinal) && !string.IsNullOrEmpty(r.Letter))
                .GroupBy(r => r.Letter.ToUpperInvariant()).Select(g => g.First())
                .OrderBy(r => r.Letter.Length).ThenBy(r => r.Letter, StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>Độ dời nhãn DET.n: chỗ nhãn theo vùng mới − chỗ nhãn theo vùng cũ (giữ khoảng người dùng đã dời thêm).</summary>
        public static P2 LabelShift(DetailRegion oldRegion, DetailRegion newRegion, double gap) =>
            DetailCommandRules.LabelAt(newRegion, gap) - DetailCommandRules.LabelAt(oldRegion, gap);

        /// <summary>Gốc block section mới (toạ độ section) giữ nguyên ánh xạ plan → block khi đầu trái đường cắt dời theo vùng:
        /// x mới = x cũ + (Start cũ − Start mới)·Right; y (cao độ) giữ. Thiếu khung ⇒ null (dùng tâm hình).</summary>
        public static P2? SectionBase(P2 oldBase, SectionFrame oldFrame, SectionFrame newFrame)
        {
            if (oldFrame == null || newFrame == null) return null;
            return new P2(oldBase.X + (oldFrame.Start - newFrame.Start).Dot(newFrame.Right), oldBase.Y);
        }

        /// <summary>Tên trong thông báo: "DETAIL n" (Plan/View) hoặc "nL-nL".</summary>
        public static string DisplayName(DetailRecord r) => r.Kind == DetailKind.Section
            ? DetailNaming.SectionTitle(r.Number, r.Letter, false)
            : "DETAIL " + r.Number.ToString(CultureInfo.InvariantCulture);

        /// <summary>Thứ tự dựng: Plan/View trước (section cần vùng DET), rồi section; trong mỗi nhóm theo panel, số, chữ.</summary>
        public static List<T> Order<T>(IEnumerable<T> items, Func<T, DetailRecord> rec) =>
            (items ?? Enumerable.Empty<T>())
                .OrderBy(i => rec(i).Kind == DetailKind.Section ? 1 : 0)
                .ThenBy(i => rec(i).Panel, StringComparer.Ordinal)
                .ThenBy(i => rec(i).Number)
                .ThenBy(i => (rec(i).Letter ?? "").Length)
                .ThenBy(i => rec(i).Letter ?? "", StringComparer.OrdinalIgnoreCase).ToList();

        public static string ConfirmQuestion(int n) =>
            $"Update {n} {Plural(n)}? Changes you made inside these blocks will be replaced; everything outside them is kept.";

        public static string ErasedNote(int n) => $"boundary of DETAIL {n} was erased — used the stored region";

        public static string UnusableNote(int n, string why) => $"boundary of DETAIL {n} is not usable ({why}) — used the stored region";

        public static string EmptySectionNote(DetailRecord r) => DisplayName(r) + ": the cut line no longer crosses structure";

        /// <summary>"Updated N details (changed: …). Check dimensions on changed details." + ghi chú.</summary>
        public static string Status(int n, IEnumerable<string> changed, IEnumerable<string> notes)
        {
            var ch = (changed ?? Enumerable.Empty<string>()).ToList();
            string s = $"Updated {n} {Plural(n)} " + (ch.Count == 0
                ? "(no changes)."
                : "(changed: " + string.Join(", ", ch) + "). Check dimensions on changed details.");
            string tail = DetailCommandRules.Notes(notes);
            return tail.Length > 0 ? s + " " + tail : s;
        }

        static string Plural(int n) => n == 1 ? "detail" : "details";
    }
}
