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
            /// <summary>Nhóm Merge / khoá Split thêm hoặc bỏ so với file 3D — nhóm / khoá khác của file 3D giữ nguyên (review 2026-11-20).</summary>
            public List<List<string>> MergesAdded { get; set; } = new List<List<string>>();
            public List<List<string>> MergesRemoved { get; set; } = new List<List<string>>();
            public List<string> SplitsAdded { get; set; } = new List<string>();
            public List<string> SplitsRemoved { get; set; } = new List<string>();
            internal bool Empty => Set.Count == 0 && Cleared.Count == 0 && Deleted.Count == 0 && Undeleted.Count == 0
                                   && MergesAdded.Count == 0 && MergesRemoved.Count == 0 && SplitsAdded.Count == 0 && SplitsRemoved.Count == 0;
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
            foreach (var g in cm) if (!bm.Any(x => x.SetEquals(g))) names.MergesAdded.Add(Sorted(g));
            foreach (var g in bm) if (!cm.Any(x => x.SetEquals(g))) names.MergesRemoved.Add(Sorted(g));
            foreach (var k in cs) if (!bs.Contains(k)) names.SplitsAdded.Add(k);
            foreach (var k in bs) if (!cs.Contains(k)) names.SplitsRemoved.Add(k);
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
                foreach (var g in doc.Names.MergesRemoved) merges.RemoveAll(x => x.SetEquals(g));
                foreach (var g in doc.Names.MergesAdded)
                    if (!merges.Any(x => x.SetEquals(g))) merges.Add(new HashSet<string>(g, StringComparer.Ordinal));
                foreach (var k in doc.Names.SplitsRemoved) splits.Remove(k);
                foreach (var k in doc.Names.SplitsAdded) splits.Add(k);
                namesOut = NamesJson.Serialize(n, deleted, merges.Select(g => (IEnumerable<string>)g).ToList(), splits);
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
                foreach (var g in doc.Names.MergesAdded.Concat(doc.Names.MergesRemoved)) r.UnionWith(g);
                r.UnionWith(doc.Names.SplitsAdded);
                r.UnionWith(doc.Names.SplitsRemoved);
            }
            r.RemoveWhere(string.IsNullOrEmpty);
            return r;
        }

        /// <summary>(số khoá tên — phần Names, số khoá hình học — phần Corr): Update Drawing chỉ áp được tên (kỹ sư 2026-11-20).</summary>
        public static (int Names, int Geometry) Counts(string pending)
        {
            Doc doc = Read(pending);
            if (doc == null) return (0, 0);
            var g = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (doc.Corr != null)
            {
                g.UnionWith(doc.Corr.Set.Keys); g.UnionWith(doc.Corr.Cleared);
                g.UnionWith(doc.Corr.Deleted); g.UnionWith(doc.Corr.Restored);
            }
            var n = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (doc.Names != null)
            {
                n.UnionWith(doc.Names.Set.Keys); n.UnionWith(doc.Names.Cleared);
                n.UnionWith(doc.Names.Deleted); n.UnionWith(doc.Names.Undeleted);
                foreach (var grp in doc.Names.MergesAdded.Concat(doc.Names.MergesRemoved)) n.UnionWith(grp);
                n.UnionWith(doc.Names.SplitsAdded); n.UnionWith(doc.Names.SplitsRemoved);
            }
            n.RemoveWhere(string.IsNullOrEmpty);
            g.RemoveWhere(string.IsNullOrEmpty);
            return (n.Count, g.Count);
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

        static List<string> Sorted(IEnumerable<string> g) => g.OrderBy(x => x, StringComparer.Ordinal).ToList();
    }
}
