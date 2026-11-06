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
    }
}
