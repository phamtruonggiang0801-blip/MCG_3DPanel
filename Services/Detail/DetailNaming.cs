using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MCG_3DPanel.Services.Detail
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>Quy tắc đặt tên/đánh số Detail: số DET, chữ section, tên block, tiêu đề.</summary>
    public static class DetailNaming
    {
        /// <summary>Số DET kế tiếp = max n của "[NEW_]&lt;panel&gt;_DET.n" / "_DETn_…" + 1; không có ⇒ 1.</summary>
        public static int NextDetailNumber(string panel, IEnumerable<string> blockNames)
        {
            int max = 0;
            if (string.IsNullOrEmpty(panel) || blockNames == null) return 1;
            // Neo đầu chuỗi, khớp panel chính xác (không IgnoreCase) để 7D-16C ≠ 7D-16CX.
            var re = new Regex("^(?:NEW_)?" + Regex.Escape(panel) + @"_DET\.?(\d+)(?:_|$)",
                RegexOptions.CultureInvariant);
            foreach (var name in blockNames)
            {
                if (name == null) continue;
                var m = re.Match(name);
                if (m.Success && int.TryParse(m.Groups[1].Value, out int n) && n > max) max = n;
            }
            return max + 1;
        }

        /// <summary>Chữ section chưa dùng: A..Z, AA, AB…</summary>
        public static string NextSectionLetter(IEnumerable<string> usedLetters)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (usedLetters != null)
                foreach (var s in usedLetters) if (!string.IsNullOrEmpty(s)) used.Add(s);
            for (int i = 1; ; i++)
            {
                string L = Letters(i);
                if (!used.Contains(L)) return L;
            }
        }

        // 1→A, 26→Z, 27→AA (kiểu cột Excel)
        static string Letters(int i)
        {
            var sb = new StringBuilder();
            while (i > 0)
            {
                i--;
                sb.Insert(0, (char)('A' + i % 26));
                i /= 26;
            }
            return sb.ToString();
        }

        public static string DetailBlock(string panel, int n) => $"{panel}_DET.{n}";
        public static string SectionBlock(string panel, int n, string L) => $"{panel}_DET.{n}_{n}{L}-{n}{L}";
        public static string SectionMark(int n, string L) => $"{n}{L}";
        public static string SectionTitle(int n, string L, bool vertical) =>
            $"{n}{L}-{n}{L}" + (vertical ? "  90°ROTATED" : "");
        public static string DetailTitle(int n) => $"%%UDETAIL {n}";
        public static string PlanLabel(int n) => $"DET.{n}";
    }
}
