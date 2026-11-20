using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using MCG_3DPanel.Commands.Detail;
using MCG_3DPanel.Naming;
using MCG_3DPanel.Services;
using MCG_3DPanel.Services.Classify;
using MCG_3DPanel.Services.Corrections;
using MCG_3DPanel.Services.Detail;
using MCG_3DPanel.Services.Update;

namespace MCG_3DPanel.Commands
{
    /// <summary>Panel của bản vẽ 2D đọc nền từ file &lt;panel&gt;_3D*.dwg (spec 2026-11-20 3d-controls-in-2d). Database phụ giữ mở theo
    /// bản vẽ 2D (dòng tab dùng ObjectId của nó); file 3D lưu lại / bản 2D đóng ⇒ giải phóng. Sửa trên tab ⇒ phần khác so với file 3D
    /// ghi XRecord MCG3D_PENDING lên block _Assy của bản 2D (<see cref="SavePending"/>), không rebuild.</summary>
    public static class Remote3DSession
    {
        public const string Changed2D = "The 2D panel changed since the last Update — press Update first.";

        private sealed class Entry
        {
            public Detail3DSource Src;
            public DateTime WriteTime;
            public List<BuildResult> Builds;   // null ⇒ đọc lại từ Src (store đã giữ sửa)
            public string Notes;
            public bool OpenAsDoc;             // file 3D đang mở như document khi đọc
            public string Fingerprint;         // vân tay block 2D lúc đọc — DC lại trong phiên ⇒ đọc lại, khoá sửa
            public string BaseCorr, BaseNames; // chỉnh sửa của file 3D lúc đọc — gốc tính pending
        }

        // Khoá: Database 2D → handle định nghĩa _Assy → entry
        private static readonly Dictionary<IntPtr, Dictionary<string, Entry>> _cache = new Dictionary<IntPtr, Dictionary<string, Entry>>();

        /// <summary>Panel của mọi reference *_Assy trong Model Space có file 3D đọc được. Không ném; lý do panel không đọc được vào
        /// <paramref name="message"/>.</summary>
        public static List<BuildResult> For2D(Document doc2D, out string message)
        {
            message = null;
            var result = new List<BuildResult>();
            if (doc2D == null) return result;
            Database db = doc2D.Database;
            var notes = new List<string>();
            try
            {
                if (!_cache.TryGetValue(db.UnmanagedObject, out var entries))
                    _cache[db.UnmanagedObject] = entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
                var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (refId, btrId) in AssyRefs(db))
                {
                    string key = btrId.Handle.ToString();
                    live.Add(key);
                    Entry e = Fresh(entries, key, doc2D, refId, btrId, notes);
                    if (e == null) continue;
                    if (e.Notes != null) notes.Add(e.Notes);
                    result.AddRange(e.Builds);
                }
                foreach (var k in entries.Keys.Where(k => !live.Contains(k)).ToList()) Drop(entries, k);
            }
            catch (System.Exception ex)
            {
                notes.Add($"The 3D files of this drawing could not be read: {ex.Message}");
            }
            message = notes.Count > 0 ? string.Join("  ·  ", notes) : null;
            return result;
        }

        private static Entry Fresh(Dictionary<string, Entry> entries, string key, Document doc2D, ObjectId refId, ObjectId btrId, List<string> notes)
        {
            string fp = Fingerprint(doc2D.Database, btrId);
            if (entries.TryGetValue(key, out Entry e))
            {
                bool same = e.Src != null && !e.Src.SourceDb.IsDisposed && SafeWriteTime(e.Src.FilePath) == e.WriteTime
                            && (PanelCommands.FindOpen(e.Src.FilePath) != null) == e.OpenAsDoc
                            && fp == e.Fingerprint;   // DC lại block trong phiên ⇒ đọc lại để Stale tính lại (review 2026-11-20)
                if (same)
                {
                    if (e.Builds == null) e.Builds = Read(e, doc2D, refId, btrId, applyPending: false);
                    return e;
                }
                Drop(entries, key);
            }

            TraceLog.Write($"Remote3DSession: read 3D file for {doc2D.Name} ({(e == null ? "first" : "changed")})");
            // Luôn database phụ riêng, kể cả khi file 3D đang mở (document đó đóng ⇒ database của nó giải phóng giữa chừng).
            Detail3DSource src = Detail3DSource.Open(doc2D.Database, doc2D.Name, refId, null, out string why, useOpenDocument: false);
            TraceLog.Write($"Remote3DSession: {(src == null ? "no file: " + why : "read " + src.FilePath)}");
            if (src == null)
            {
                if (!string.IsNullOrEmpty(why)) notes.Add(why);
                return null;
            }
            e = new Entry { Src = src, WriteTime = SafeWriteTime(src.FilePath), OpenAsDoc = PanelCommands.FindOpen(src.FilePath) != null, Fingerprint = fp };
            entries[key] = e;
            e.Builds = Read(e, doc2D, refId, btrId, applyPending: true);
            return e;
        }

        /// <summary>Đọc panel từ database phụ. <paramref name="applyPending"/>: lần đầu — nạp chỉnh sửa của file 3D, chụp gốc, phủ pending
        /// của bản 2D vào store TRƯỚC khi phân loại (Kind chờ hiện ngay).</summary>
        private static List<BuildResult> Read(Entry e, Document doc2D, ObjectId refId, ObjectId btrId, bool applyPending)
        {
            Database side = e.Src.SourceDb;
            string file = Path.GetFileName(e.Src.FilePath);
            string lockReason = PanelCommands.FindOpen(e.Src.FilePath) != null
                ? $"The 3D file {file} is open — edit there, or close it to edit here."
                : e.Src.Stale == StaleState.Changed2D ? Changed2D : null;

            ObjectId srcBtr = Resolve(side, btrId.Handle.ToString());   // file 3D giữ nguyên handle block 2D
            string panelKey = srcBtr.IsNull ? "" : CorrectionStore.PanelKey(srcBtr);
            string pending = ReadPending(doc2D.Database, btrId);
            if (applyPending && panelKey != "")
            {
                using (Transaction tr = side.TransactionManager.StartOpenCloseTransaction())
                {
                    CorrectionStore.EnsureLoaded(tr, srcBtr);
                    NameStore.EnsureLoaded(tr, srcBtr);
                    tr.Commit();
                }
                e.BaseCorr = CorrectionStore.Snapshot(panelKey);
                e.BaseNames = NameStore.Snapshot(panelKey);
                // File 3D đang mở: store là của document đó — không phủ pending lên (bảng chỉ đọc).
                if (lockReason == null || lockReason == Changed2D)
                {
                    PendingEdits.Apply(pending, e.BaseCorr, e.BaseNames, out string c, out string n);
                    if (pending != null && lockReason == null)
                    {
                        CorrectionStore.LoadJson(panelKey, c);
                        NameStore.LoadJson(panelKey, n);
                    }
                }
            }

            var notes = new List<string>();
            var builds = PanelSession.ReadAll(side, notes);
            e.Notes = notes.Count > 0 ? string.Join("  ·  ", notes) : null;
            var keys = PendingEdits.Keys(pending);
            foreach (var b in builds)
                b.Remote = new RemoteLink
                {
                    File3D = e.Src.FilePath,
                    SideDb = side,
                    AssyRef2D = refId,
                    AssyBtr2D = btrId,
                    LockReason = lockReason,
                    BaseCorr = e.BaseCorr,
                    BaseNames = e.BaseNames,
                    PendingKeys = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase),
                };
            return builds;
        }

        /// <summary>Ghi phần sửa chờ của panel (so với file 3D) vào bản 2D; dòng của panel sẽ được đọc lại. Trả lỗi hoặc null.</summary>
        public static string SavePending(BuildResult b)
        {
            RemoteLink r = b?.Remote;
            if (r == null) return null;
            if (r.Locked) return r.LockReason;
            try
            {
                string panelKey = CorrectionStore.PanelKey(b.SourceBtrId);
                string diff = PendingEdits.Diff(r.BaseCorr, CorrectionStore.Snapshot(panelKey), r.BaseNames, NameStore.Snapshot(panelKey));
                Database db2D = r.AssyBtr2D.Database;
                Document doc = DocOf(db2D) ?? throw new InvalidOperationException("the 2D drawing is not open");
                using (doc.LockDocument())
                using (Transaction tr = db2D.TransactionManager.StartTransaction())
                {
                    CorrectionXRecord.Write(tr, r.AssyBtr2D, CorrectionXRecord.PendingKey, diff);
                    tr.Commit();
                }
                r.PendingKeys = PendingEdits.Keys(diff);
                if (_cache.TryGetValue(db2D.UnmanagedObject, out var entries) && entries.TryGetValue(r.AssyBtr2D.Handle.ToString(), out Entry e))
                    e.Builds = null;   // đọc lại (Kind / tên mới) ở lần For2D sau, store giữ nguyên sửa
                return null;
            }
            catch (System.Exception ex)
            {
                return "The edit could not be saved in the 2D drawing (" + ex.Message + ").";
            }
        }

        /// <summary>Chạy <paramref name="f"/> trên panel đọc lại từ database phụ với correction CỦA FILE 3D (bỏ sửa hình học đang chờ — 3D
        /// chưa dựng lại) nhưng giữ tên đang chờ trong NameStore: Update Drawing / Plan View từ bản 2D (kỹ sư 2026-11-20). Trả store về như cũ.</summary>
        public static T WithFileCorrections<T>(BuildResult remote, Func<BuildResult, T> f)
        {
            RemoteLink r = remote?.Remote ?? throw new InvalidOperationException("not a panel read from a 3D file");
            string key = CorrectionStore.PanelKey(remote.SourceBtrId);
            string current = CorrectionStore.Snapshot(key);
            CorrectionStore.LoadJson(key, r.BaseCorr);
            try
            {
                var notes = new List<string>();
                var build = PanelSession.ReadAll(r.SideDb, notes).FirstOrDefault(b => b.BlockName == remote.BlockName)
                            ?? throw new InvalidOperationException(notes.FirstOrDefault() ?? "the panel could not be read from the 3D file");
                build.Remote = r;
                return f(build);
            }
            finally { CorrectionStore.LoadJson(key, current); }
        }

        /// <summary>Pending (JSON) trên định nghĩa _Assy của bản 2D; không có ⇒ null.</summary>
        public static string ReadPending(Database db2D, ObjectId assyBtr2D)
        {
            if (db2D == null || assyBtr2D.IsNull) return null;
            using (Transaction tr = db2D.TransactionManager.StartOpenCloseTransaction())
                return CorrectionXRecord.Read(tr, assyBtr2D, CorrectionXRecord.PendingKey);
        }

        /// <summary>Pending của block mà reference <paramref name="refId"/> (bản vẽ 2D) chèn; không có ⇒ null.</summary>
        public static string ReadPendingForRef(ObjectId refId)
        {
            if (refId.IsNull) return null;
            ObjectId btr;
            using (Transaction tr = refId.Database.TransactionManager.StartOpenCloseTransaction())
                btr = ((BlockReference)tr.GetObject(refId, OpenMode.ForRead)).BlockTableRecord;
            return ReadPending(refId.Database, btr);
        }

        /// <summary>Như <see cref="ClearPending"/>, theo reference _Assy.</summary>
        public static string ClearPendingForRef(Document doc2D, ObjectId refId)
        {
            try
            {
                ObjectId btr;
                using (Transaction tr = doc2D.Database.TransactionManager.StartOpenCloseTransaction())
                    btr = ((BlockReference)tr.GetObject(refId, OpenMode.ForRead)).BlockTableRecord;
                return ClearPending(doc2D, btr);
            }
            catch (System.Exception ex) { return "The waiting edits could not be cleared from the 2D drawing (" + ex.Message + ")."; }
        }

        /// <summary>Xoá pending của panel trong bản 2D (sau Update thành công) và bỏ database phụ của nó. Trả lỗi hoặc null.</summary>
        public static string ClearPending(Document doc2D, ObjectId assyBtr2D)
        {
            try
            {
                using (doc2D.LockDocument())
                using (Transaction tr = doc2D.Database.TransactionManager.StartTransaction())
                {
                    CorrectionXRecord.Write(tr, assyBtr2D, CorrectionXRecord.PendingKey, null);
                    tr.Commit();
                }
                if (_cache.TryGetValue(doc2D.Database.UnmanagedObject, out var entries)) Drop(entries, assyBtr2D.Handle.ToString());
                return null;
            }
            catch (System.Exception ex) { return "The waiting edits could not be cleared from the 2D drawing (" + ex.Message + ")."; }
        }

        /// <summary>Đường dẫn highlight trong bản 2D: đổi từng id (database phụ) theo handle, gốc = reference _Assy. Thiếu ⇒ null.</summary>
        public static ObjectId[] PathIn2D(BuildResult b, ObjectId[] idPath3D)
        {
            RemoteLink r = b?.Remote;
            if (r == null || idPath3D == null || idPath3D.Length == 0) return null;
            // Block 2D đã DC lại / sửa sau lần Update cuối: handle của file 3D có thể đã thuộc đối tượng KHÁC ⇒ không dò (zoom sai chỗ —
            // kỹ sư 2026-11-20); người dùng chạy Update 3D Model để khớp lại.
            if (r.LockReason == Changed2D) return null;
            Database db2D = r.AssyRef2D.Database;
            var path = new ObjectId[idPath3D.Length];
            path[0] = r.AssyRef2D;
            for (int i = 1; i < idPath3D.Length; i++)
            {
                path[i] = Resolve(db2D, idPath3D[i].Handle.ToString());
                if (path[i].IsNull || path[i].IsErased) return null;
            }
            return OwnedChain(path) ? path : null;
        }

        /// <summary>Mỗi phần tử của đường dẫn nằm đúng trong định nghĩa block của reference ngay trước nó — handle trùng tình cờ với đối
        /// tượng khác thì chuỗi gãy ⇒ không dùng.</summary>
        private static bool OwnedChain(ObjectId[] path)
        {
            try
            {
                using (Transaction tr = path[0].Database.TransactionManager.StartOpenCloseTransaction())
                    for (int i = 1; i < path.Length; i++)
                    {
                        if (!(tr.GetObject(path[i - 1], OpenMode.ForRead) is BlockReference parent)) return false;
                        if (tr.GetObject(path[i], OpenMode.ForRead).OwnerId != parent.BlockTableRecord) return false;
                    }
                return true;
            }
            catch (System.Exception) { return false; }
        }

        /// <summary>Khung WCS của entity cuối <paramref name="path"/> trong bản 2D (qua transform các reference bọc ngoài); lỗi ⇒ null.</summary>
        public static Extents3d? BoundsIn2D(ObjectId[] path)
        {
            if (path == null || path.Length == 0) return null;
            try
            {
                using (Transaction tr = path[0].Database.TransactionManager.StartOpenCloseTransaction())
                {
                    var ent = (Entity)tr.GetObject(path[path.Length - 1], OpenMode.ForRead);
                    Extents3d e = ent.GeometricExtents;
                    for (int i = path.Length - 2; i >= 0; i--)
                        if (tr.GetObject(path[i], OpenMode.ForRead) is BlockReference br) e.TransformBy(br.BlockTransform);
                    return e;
                }
            }
            catch (System.Exception) { return null; }
        }

        /// <summary>Đường dẫn 2D cho khoá (dòng sinh thêm: Collar, Tripping…): part 2D đầu tiên có handle trong khoá.</summary>
        public static ObjectId[] PathForKey(BuildResult b, string key)
        {
            if (b == null || string.IsNullOrEmpty(key)) return null;
            foreach (string h in KeyRemap.Handles(key))
            {
                var part = b.ClassifiedParts.FirstOrDefault(p => p.IdPath != null && p.IdPath.Length > 0
                    && string.Equals(p.IdPath[p.IdPath.Length - 1].Handle.ToString(), h, StringComparison.OrdinalIgnoreCase));
                var path = part == null ? null : PathIn2D(b, part.IdPath);
                if (path != null) return path;
            }
            return null;
        }

        /// <summary>Entity 2D của handle (trong định nghĩa block thuộc reference _Assy); không có ⇒ Null.</summary>
        public static ObjectId EntityIn2D(BuildResult b, string handle) =>
            b?.Remote == null ? ObjectId.Null : Resolve(b.Remote.AssyRef2D.Database, handle);

        /// <summary>Bản vẽ 2D sắp đóng: giải phóng database phụ của nó.</summary>
        public static void Forget(Database db2D)
        {
            if (db2D == null) return;
            // File 3D đang mở như document sắp đóng: entry nào đọc từ database của nó phải bỏ (dòng trỏ vào database sắp giải phóng).
            foreach (var entries in _cache.Values)
                foreach (var k in entries.Where(kv => ReferenceEquals(kv.Value.Src?.SourceDb, db2D)).Select(kv => kv.Key).ToList())
                    Drop(entries, k);
            if (!_cache.TryGetValue(db2D.UnmanagedObject, out var own)) return;
            foreach (var k in own.Keys.ToList()) Drop(own, k);
            _cache.Remove(db2D.UnmanagedObject);
        }

        private static void Drop(Dictionary<string, Entry> entries, string key)
        {
            if (!entries.TryGetValue(key, out Entry e)) return;
            entries.Remove(key);
            try
            {
                Database side = e.Src?.SourceDb;
                // Cache store của database phụ phải bỏ TRƯỚC khi giải phóng (con trỏ có thể bị database mới dùng lại). Xét database
                // LÚC ĐỌC là của ta (không phải document đang mở) — không xét file đang mở BÂY GIỜ (review 2026-11-20: Update mở lại file 3D).
                // Database phụ luôn là bản riêng (useOpenDocument: false) ⇒ luôn dọn cache của nó.
                if (side != null && !side.IsDisposed)
                {
                    CorrectionStore.ClearDatabase(side);
                    NameStore.ClearDatabase(side);
                }
                e.Src?.Dispose();
            }
            catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine("[Remote3DSession] Drop: " + ex.Message); }
        }

        private static List<(ObjectId Ref, ObjectId Btr)> AssyRefs(Database db)
        {
            var list = new List<(ObjectId, ObjectId)>();
            var seen = new HashSet<ObjectId>();
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is BlockReference br)) continue;
                    string n = PartCollector.GetName(tr, br);
                    if (n == null || n.Length <= 5 || !n.EndsWith("_Assy", StringComparison.Ordinal)) continue;
                    if (seen.Add(br.BlockTableRecord)) list.Add((id, br.BlockTableRecord));
                }
            }
            return list;
        }

        private static string Fingerprint(Database db2D, ObjectId btrId)
        {
            try
            {
                using (Transaction tr = db2D.TransactionManager.StartOpenCloseTransaction())
                    return Detail3DMatch.Fingerprint(SourceFingerprintReader.Records(tr, btrId));
            }
            catch (System.Exception) { return null; }   // không đọc được ⇒ coi như đổi (đọc lại)
        }

        private static Document DocOf(Database db)
        {
            foreach (Document d in Application.DocumentManager) if (d.Database == db) return d;
            return null;
        }

        private static DateTime SafeWriteTime(string path)
        {
            try { return File.GetLastWriteTimeUtc(path); } catch { return DateTime.MinValue; }
        }

        private static ObjectId Resolve(Database db, string handle)
        {
            if (db == null || !long.TryParse(handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long v)) return ObjectId.Null;
            return db.TryGetObjectId(new Handle(v), out ObjectId id) ? id : ObjectId.Null;
        }
    }
}
