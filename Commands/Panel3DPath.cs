using System;
using System.IO;

namespace MCG_3DPanel.Commands
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>Quy tắc đường dẫn file 3D riêng: &lt;folder&gt;\&lt;panel&gt;_3D.dwg, tránh trùng bằng hậu tố _2, _3...</summary>
    public static class Panel3DPath
    {
        public static string Target(string folder, string panelName)
        {
            var name = panelName ?? "";
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return Path.Combine(folder, name + "_3D.dwg");
        }

        /// <summary>Trả target nếu chưa bị chiếm, ngược lại thử _2, _3... (taken gồm cả đường dẫn đã giữ chỗ trong cùng đợt).</summary>
        public static string NextFree(string target, Func<string, bool> taken)
        {
            if (!taken(target)) return target;
            var dir = Path.GetDirectoryName(target);
            var stem = Path.GetFileNameWithoutExtension(target);
            var ext = Path.GetExtension(target);
            for (int i = 2; ; i++)
            {
                var p = Path.Combine(dir, stem + "_" + i + ext);
                if (!taken(p)) return p;
            }
        }

        /// <summary>Tên file sao lưu cạnh target: &lt;stem&gt;.yyyyMMdd_HHmmss.bak (thay đuôi .dwg); trùng thì thêm _2, _3...</summary>
        public static string Backup(string target, DateTime now, Func<string, bool> taken)
        {
            var dir = Path.GetDirectoryName(target);
            var stem = Path.GetFileNameWithoutExtension(target) + "." + now.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            var p = Path.Combine(dir, stem + ".bak");
            for (int i = 2; taken(p); i++) p = Path.Combine(dir, stem + "_" + i + ".bak");
            return p;
        }

        /// <summary>Lý do từ chối Update (null = được phép): file không có meta, hoặc dựng từ block 2D khác — khác handle reference VÀ khác
        /// tên block panel (không phân biệt hoa/thường). Cùng tên block ⇒ cùng panel đã chèn lại, vd DC từ Global (spec 2026-11-20).</summary>
        public static string UpdateRefusal(string fileName, MCG_3DPanel.Services.Corrections.PanelMeta meta, string selectedRefHandle,
            string selectedBlockName = null, string oldBlockName = null)
        {
            if (meta == null) return fileName + " has no panel data; use New copy.";
            bool sameRef = string.Equals(meta.SourceRefHandle, selectedRefHandle, StringComparison.OrdinalIgnoreCase);
            bool sameBlock = !string.IsNullOrEmpty(selectedBlockName) && !string.IsNullOrEmpty(oldBlockName)
                             && string.Equals(selectedBlockName, oldBlockName, StringComparison.OrdinalIgnoreCase);
            if (!sameRef && !sameBlock) return fileName + " was built from another 2D block; use New copy.";
            return null;
        }

        /// <summary>Đếm key part gốc dạng {hex}#{số} không còn trong partKeys; key sinh ra (CollarPlate:..., con "a#0/b#0"...) bỏ qua.</summary>
        public static int CountUnmatched(System.Collections.Generic.IEnumerable<string> storedKeys, System.Collections.Generic.ICollection<string> partKeys)
        {
            int n = 0;
            foreach (var k in storedKeys)
                if (k != null && System.Text.RegularExpressions.Regex.IsMatch(k, @"^[0-9A-Fa-f]+#[0-9]+$") && !partKeys.Contains(k)) n++;
            return n;
        }
    }
}
