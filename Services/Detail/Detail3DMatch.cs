using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MCG_3DPanel.Services.Detail
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>Một file 3D ứng viên (đọc từ meta panel).</summary>
    public sealed class Candidate3D
    {
        public string Path;
        public string SourcePath;
        public string SourceRefHandle;
        public string Fingerprint;
        public bool OpenUnsaved;
    }

    public enum StaleState { Fresh, Unknown, Changed2D, Unsaved3D }

    /// <summary>So khớp file 3D với bản vẽ 2D nguồn (phát hiện 3D cũ).</summary>
    public static class Detail3DMatch
    {
        static string Full(string p)
        {
            if (string.IsNullOrWhiteSpace(p)) return null;
            try { return System.IO.Path.GetFullPath(p); } catch { return p; }
        }

        /// <summary>Lọc ứng viên cùng file 2D nguồn (so full path, không phân biệt hoa thường) và cùng handle ref.</summary>
        public static List<Candidate3D> Matching(IEnumerable<Candidate3D> all, string drawingPath, string assyRefHandle)
        {
            string want = Full(drawingPath);
            var res = new List<Candidate3D>();
            if (want == null) return res;
            foreach (var c in all)
            {
                string src = Full(c.SourcePath);
                if (src != null
                    && string.Equals(src, want, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(c.SourceRefHandle, assyRefHandle, StringComparison.OrdinalIgnoreCase))
                    res.Add(c);
            }
            return res;
        }

        /// <summary>Lý do không có file 3D dùng được: file đọc được nhưng dựng từ bản vẽ / reference khác, file đọc lỗi (tên + lỗi),
        /// hoặc thật sự không có file. <paramref name="loaded"/> = ứng viên đọc được (đều không khớp).</summary>
        public static string NoMatchReason(string panel, IEnumerable<Candidate3D> loaded,
            IEnumerable<(string File, string Error)> readFailures, string drawingPath, string assyRefHandle)
        {
            string want = Full(drawingPath);
            var parts = new List<string>();
            foreach (var c in loaded ?? Enumerable.Empty<Candidate3D>())
            {
                string file = System.IO.Path.GetFileName(c.Path ?? "");
                string src = Full(c.SourcePath);
                if (src == null)
                    parts.Add($"Found {file} but it does not record which 2D drawing it was built from.");
                else if (want == null || !string.Equals(src, want, StringComparison.OrdinalIgnoreCase))
                    parts.Add($"Found {file} but it was built from {c.SourcePath}.");
                else
                    parts.Add($"Found {file} but it was built from another {panel}_Assy block reference in this drawing.");
            }
            bool found = parts.Count > 0;   // "Build 3D again" đứng cuối, sau lỗi đọc
            foreach (var f in readFailures ?? Enumerable.Empty<(string, string)>())
                parts.Add($"Could not read {f.File} ({f.Error}).");
            if (parts.Count == 0) return $"No 3D file of {panel} next to this drawing. Build 3D first.";
            if (found) parts.Add("Build 3D again from this drawing.");
            return string.Join(" ", parts);
        }

        /// <summary>Độ cũ của 3D: chưa lưu thắng tất cả; fingerprint rỗng ⇒ file cũ không biết; khác ⇒ 2D đã đổi.</summary>
        public static StaleState Stale(Candidate3D c, string current2DFingerprint)
        {
            if (c.OpenUnsaved) return StaleState.Unsaved3D;
            if (string.IsNullOrEmpty(c.Fingerprint)) return StaleState.Unknown;
            return string.Equals(c.Fingerprint, current2DFingerprint, StringComparison.Ordinal)
                ? StaleState.Fresh : StaleState.Changed2D;
        }

        /// <summary>Mẫu tên file 3D (gồm hậu tố _2, _3...). Truyền TÊN BLOCK _Assy (vd "7D-16P_Assy"): Build 3D đặt tên file theo nó
        /// (Panel3DPath.Target ⇒ "7D-16P_Assy_3D.dwg"). Làm sạch như Panel3DPath.Target.</summary>
        public static string CandidatePattern(string assyBlockName)
        {
            var name = assyBlockName ?? "";
            foreach (var ch in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
            return name + "_3D*.dwg";
        }

        /// <summary>Tên block trong record vân tay: tên hiệu dụng (block động ⇒ tên định nghĩa gốc); tên ẩn danh '*' (*U…) có thể
        /// đổi khi mở lại / purge ⇒ bỏ (chuỗi rỗng), tránh báo 3D cũ sai.</summary>
        public static string FingerprintName(string blockName, string dynamicName)
        {
            string n = string.IsNullOrEmpty(dynamicName) ? blockName ?? "" : dynamicName;
            return n.StartsWith("*", StringComparison.Ordinal) ? "" : n;
        }

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
