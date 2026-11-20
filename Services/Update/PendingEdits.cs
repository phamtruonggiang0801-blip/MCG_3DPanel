using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MCG_3DPanel.Naming;
using MCG_3DPanel.Services.Corrections;

namespace MCG_3DPanel.Services.Update
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.*.

    /// <summary>Sửa trên bản vẽ 2D chờ Update (spec 2026-11-20 3d-controls-in-2d): phần KHÁC giữa chỉnh sửa đọc từ file 3D (base) và
    /// trạng thái sau khi người dùng sửa (cur). Lưu XRecord MCG3D_PENDING trong bản 2D; Update phủ lên chỉnh sửa của file 3D — trùng
    /// khoá thì sửa chờ thắng, khoá pending không đụng thì giữ của file 3D.</summary>
    public static class PendingEdits
    {
        sealed class CorrPart
        {
            public Dictionary<string, PartCorrection> Set { get; set; } = new Dictionary<string, PartCorrection>();
            public List<string> Cleared { get; set; } = new List<string>();
            public List<string> Deleted { get; set; } = new List<string>();
            public List<string> Restored { get; set; } = new List<string>();
            internal bool Empty => Set.Count == 0 && Cleared.Count == 0 && Deleted.Count == 0 && Restored.Count == 0;
        }

        sealed class NamesPart
        {
            public Dictionary<string, string> Set { get; set; } = new Dictionary<string, string>();
            public List<string> Cleared { get; set; } = new List<string>();
            public List<string> Deleted { get; set; } = new List<string>();
            public List<string> Undeleted { get; set; } = new List<string>();
            /// <summary>Giá trị nguyên khối: null = không đổi (giữ của file 3D).</summary>
            public List<List<string>> Merges { get; set; }
            public List<string> Splits { get; set; }
            internal bool Empty => Set.Count == 0 && Cleared.Count == 0 && Deleted.Count == 0 && Undeleted.Count == 0 && Merges == null && Splits == null;
        }

        sealed class Doc
        {
            public int Version { get; set; } = 1;
            public CorrPart Corr { get; set; }
            public NamesPart Names { get; set; }
        }

        static readonly JsonSerializerOptions Opt = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        /// <summary>Phần khác giữa base và cur (JSON MCG3D_CORRECTIONS / MCG3D_NAMES); không khác ⇒ null.</summary>
        public static string Diff(string baseCorr, string curCorr, string baseNames, string curNames)
        {
            var doc = new Doc();

            CorrectionJson.Deserialize(baseCorr, out var bp, out var bd);
            CorrectionJson.Deserialize(curCorr, out var cp, out var cd);
            var corr = new CorrPart();
            foreach (var kv in cp)
                if (!bp.TryGetValue(kv.Key, out var old) || Json(old) != Json(kv.Value)) corr.Set[kv.Key] = kv.Value;
            foreach (var k in bp.Keys) if (!cp.ContainsKey(k)) corr.Cleared.Add(k);
            foreach (var k in cd) if (!bd.Contains(k)) corr.Deleted.Add(k);
            foreach (var k in bd) if (!cd.Contains(k)) corr.Restored.Add(k);
            if (!corr.Empty) doc.Corr = corr;

            var bn = NamesJson.Deserialize(baseNames, out var bnd, out var bm, out var bs);
            var cn = NamesJson.Deserialize(curNames, out var cnd, out var cm, out var cs);
            var names = new NamesPart();
            foreach (var kv in cn)
                if (!bn.TryGetValue(kv.Key, out var old) || old != kv.Value) names.Set[kv.Key] = kv.Value;
            foreach (var k in bn.Keys) if (!cn.ContainsKey(k)) names.Cleared.Add(k);
            foreach (var k in cnd) if (!bnd.Contains(k)) names.Deleted.Add(k);
            foreach (var k in bnd) if (!cnd.Contains(k)) names.Undeleted.Add(k);
            if (!SameGroups(bm, cm)) names.Merges = cm.Select(g => g.OrderBy(x => x, StringComparer.Ordinal).ToList()).ToList();
            if (!bs.SetEquals(cs)) names.Splits = cs.OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (!names.Empty) doc.Names = names;

            return doc.Corr == null && doc.Names == null ? null : JsonSerializer.Serialize(doc, Opt);
        }

        /// <summary>Phủ <paramref name="pending"/> lên chỉnh sửa <paramref name="corr"/> / <paramref name="names"/> (của file 3D).
        /// Pending null/rỗng/hỏng ⇒ trả nguyên.</summary>
        public static void Apply(string pending, string corr, string names, out string corrOut, out string namesOut)
        {
            corrOut = corr;
            namesOut = names;
            Doc doc = Read(pending);
            if (doc == null) return;

            if (doc.Corr != null)
            {
                CorrectionJson.Deserialize(corr, out var parts, out var deleted);
                foreach (var k in doc.Corr.Cleared) parts.Remove(k);
                foreach (var kv in doc.Corr.Set) parts[kv.Key] = kv.Value;
                foreach (var k in doc.Corr.Restored) deleted.Remove(k);
                foreach (var k in doc.Corr.Deleted) deleted.Add(k);
                corrOut = CorrectionJson.Serialize(parts, deleted);
            }

            if (doc.Names != null)
            {
                var n = NamesJson.Deserialize(names, out var deleted, out var merges, out var splits);
                foreach (var k in doc.Names.Cleared) n.Remove(k);
                foreach (var kv in doc.Names.Set) n[kv.Key] = kv.Value;
                foreach (var k in doc.Names.Undeleted) deleted.Remove(k);
                foreach (var k in doc.Names.Deleted) deleted.Add(k);
                IEnumerable<IEnumerable<string>> m = doc.Names.Merges != null
                    ? doc.Names.Merges.Select(g => (IEnumerable<string>)g).ToList()
                    : merges.Select(g => (IEnumerable<string>)g).ToList();
                IEnumerable<string> s = doc.Names.Splits ?? (IEnumerable<string>)splits;
                namesOut = NamesJson.Serialize(n, deleted, m, s);
            }
        }

        /// <summary>Mọi khoá pending đụng tới (Set / Cleared / Deleted / Restored của cả 2 phần, thành viên Merges / Splits đã đổi).</summary>
        public static ISet<string> Keys(string pending)
        {
            var r = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Doc doc = Read(pending);
            if (doc == null) return r;
            if (doc.Corr != null)
            {
                r.UnionWith(doc.Corr.Set.Keys);
                r.UnionWith(doc.Corr.Cleared);
                r.UnionWith(doc.Corr.Deleted);
                r.UnionWith(doc.Corr.Restored);
            }
            if (doc.Names != null)
            {
                r.UnionWith(doc.Names.Set.Keys);
                r.UnionWith(doc.Names.Cleared);
                r.UnionWith(doc.Names.Deleted);
                r.UnionWith(doc.Names.Undeleted);
                foreach (var g in doc.Names.Merges ?? new List<List<string>>()) r.UnionWith(g);
                r.UnionWith(doc.Names.Splits ?? new List<string>());
            }
            r.RemoveWhere(string.IsNullOrEmpty);
            return r;
        }

        /// <summary>Số khoá đang chờ (0 nếu không có).</summary>
        public static int Count(string pending) => Keys(pending).Count;

        static Doc Read(string pending)
        {
            if (string.IsNullOrWhiteSpace(pending)) return null;
            try { return JsonSerializer.Deserialize<Doc>(pending); }
            catch (JsonException) { return null; }
        }

        static string Json(PartCorrection c) => JsonSerializer.Serialize(c, Opt);

        static bool SameGroups(List<HashSet<string>> a, List<HashSet<string>> b)
        {
            if (a.Count != b.Count) return false;
            var used = new bool[b.Count];
            foreach (var g in a)
            {
                int i = -1;
                for (int j = 0; j < b.Count && i < 0; j++) if (!used[j] && b[j].SetEquals(g)) i = j;
                if (i < 0) return false;
                used[i] = true;
            }
            return true;
        }
    }
}
