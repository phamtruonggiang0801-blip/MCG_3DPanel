using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MCG_3DPanel.Services.Detail
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>So khớp file 3D với bản vẽ 2D nguồn (phát hiện 3D cũ).</summary>
    public static class Detail3DMatch
    {
        /// <summary>Dấu vân tay nguồn 2D: SHA-256 (hex) của các record sắp ordinal, nối bằng '\n' ⇒ không phụ thuộc thứ tự duyệt.</summary>
        public static string Fingerprint(IEnumerable<string> records)
        {
            var sorted = records.ToList();
            sorted.Sort(StringComparer.Ordinal);
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", sorted)));
            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
