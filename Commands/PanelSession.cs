using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using MCG_3DPanel.Models;
using MCG_3DPanel.Naming;
using MCG_3DPanel.Services;
using MCG_3DPanel.Services.Corrections;

namespace MCG_3DPanel.Commands
{
    /// <summary>
    /// Dòng của các tab theo từng drawing: build vừa dựng (<see cref="Set"/>), hoặc đọc lại từ XRecord MCG3D_PANEL khi mở file
    /// (<see cref="For"/>) — không rebuild, KHÔNG GHI gì (file đóng không hỏi Save): chỉ StartOpenCloseTransaction + ForRead,
    /// không cần LockDocument, không Regen.
    /// Khoá cache = con trỏ Database (như <see cref="CorrectionStore.PanelKey"/>): không giữ wrapper Database nào; ObjectId
    /// trong cache hết hiệu lực khi drawing đóng ⇒ <see cref="Forget"/> ở DocumentToBeDestroyed, TRƯỚC khi Database bị giải phóng.
    /// </summary>
    public static class PanelSession
    {
        private sealed class Entry
        {
            public List<BuildResult> Builds;
            public string Message;
        }

        private static readonly Dictionary<IntPtr, Entry> _cache = new Dictionary<IntPtr, Entry>();

        /// <summary>Panel của drawing (bản sao danh sách — tab sửa list của nó không đụng cache). Không có panel ⇒ rỗng, message null.
        /// <paramref name="message"/> = điều người dùng cần biết (solid bị bỏ, panel không đọc được, block 3D cũ). Không ném.</summary>
        public static List<BuildResult> For(Database db, out string message)
        {
            message = null;
            if (db == null) return new List<BuildResult>();
            IntPtr key = db.UnmanagedObject;
            if (_cache.TryGetValue(key, out Entry hit))
            {
                // Ctrl+Z 1 lần rebuild làm block trong cache bị xoá ⇒ đọc lại từ file.
                if (hit.Builds.All(b => !b.BlockRefId.IsNull && !b.BlockRefId.IsErased && !b.BtrId.IsErased))
                {
                    message = hit.Message;
                    return new List<BuildResult>(hit.Builds);
                }
                _cache.Remove(key);
            }

            var builds = new List<BuildResult>();
            var notes = new List<string>();
            try
            {
                foreach (PanelBlock panel in FindBlocks(db))
                {
                    BuildResult build = ReadPanel(db, panel, notes);
                    if (build != null) builds.Add(build);
                }
            }
            catch (System.Exception ex)
            {
                notes.Add($"The panels of this drawing could not be read: {ex.Message}");
            }
            message = notes.Count > 0 ? string.Join("  ·  ", notes) : null;
            // Danh sách rỗng KHÔNG nhớ: quét lại rẻ, và drawing 2D có thể có panel sau đó.
            if (builds.Count > 0) _cache[key] = new Entry { Builds = builds, Message = message };
            return new List<BuildResult>(builds);
        }

        /// <summary>Build / rebuild xong: các dòng của drawing này chính là <paramref name="builds"/>.</summary>
        public static void Set(Database db, List<BuildResult> builds)
        {
            if (db == null) return;
            if (builds == null || builds.Count == 0) { _cache.Remove(db.UnmanagedObject); return; }
            _cache[db.UnmanagedObject] = new Entry { Builds = new List<BuildResult>(builds) };
        }

        /// <summary>Rebuild 1 panel: thay đúng panel đó, giữ các panel khác của drawing.</summary>
        internal static void Replace(Database db, BuildResult old, BuildResult neu)
        {
            if (db == null || neu == null) return;
            var list = _cache.TryGetValue(db.UnmanagedObject, out Entry hit) ? new List<BuildResult>(hit.Builds) : new List<BuildResult>();
            int i = list.FindIndex(b => ReferenceEquals(b, old) || b.BlockName == neu.BlockName);
            if (i >= 0) list[i] = neu; else list.Add(neu);
            Set(db, list);
        }

        /// <summary>Bản vẽ sắp đóng: bỏ build của nó (ObjectId sắp không còn hợp lệ; con trỏ có thể bị Database mới dùng lại).</summary>
        public static void Forget(Database db)
        {
            if (db != null) _cache.Remove(db.UnmanagedObject);
        }

        // ─── Đọc panel từ file (không ghi) ────────────────────────────────

        private sealed class PanelBlock
        {
            public ObjectId RefId;
            public ObjectId BtrId;
            public string Name;
        }

        /// <summary>Block reference MCG3D_* trong Model Space, mỗi định nghĩa 1 lần (người dùng có thể copy block 3D).</summary>
        private static List<PanelBlock> FindBlocks(Database db)
        {
            var found = new List<PanelBlock>();
            var seen = new HashSet<ObjectId>();
            RXClass refClass = RXObject.GetClass(typeof(BlockReference));
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (!id.ObjectClass.IsDerivedFrom(refClass)) continue;
                    var br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
                    var def = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                    if (!def.Name.StartsWith(Panel3DFile.BLOCK_PREFIX, StringComparison.OrdinalIgnoreCase)) continue;
                    if (seen.Add(def.ObjectId)) found.Add(new PanelBlock { RefId = id, BtrId = def.ObjectId, Name = def.Name });
                }
                tr.Commit();
            }
            return found;
        }

        /// <summary>1 panel → BuildResult y như sau Build. Không đọc được ⇒ null + thêm lý do vào <paramref name="notes"/>; không ném.</summary>
        private static BuildResult ReadPanel(Database db, PanelBlock panel, List<string> notes)
        {
            try
            {
                PanelMeta meta;
                ObjectId srcRef, srcBtr;
                Matrix3d xform;
                using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    string json = CorrectionXRecord.Read(tr, panel.BtrId, CorrectionXRecord.PanelKey);
                    if (json == null) { notes.Add($"Old 3D block {panel.Name} — run Build 3D on its 2D block again."); return null; }
                    meta = PanelMeta.FromJson(json);
                    if (meta == null || !Enum.IsDefined(typeof(PanelPosition), meta.Position))
                    {
                        notes.Add($"The panel data of {panel.Name} could not be read — run Build 3D on its 2D block again.");
                        return null;
                    }
                    srcRef = Resolve(db, meta.SourceRefHandle);
                    srcBtr = Resolve(db, meta.SourceBtrHandle);
                    var br = srcRef.IsNull || srcRef.IsErased ? null : tr.GetObject(srcRef, OpenMode.ForRead) as BlockReference;
                    if (br == null || br.BlockTableRecord != srcBtr || srcBtr.IsErased)
                    {
                        notes.Add($"The 2D source of {panel.Name} is missing from this file — run Build 3D on its 2D block again.");
                        return null;
                    }
                    xform = br.BlockTransform;
                    CorrectionStore.EnsureLoaded(tr, srcBtr);
                    NameStore.EnsureLoaded(tr, srcBtr);
                    tr.Commit();
                }

                var position = (PanelPosition)meta.Position;
                ClassificationResult cls = new ClassificationService().Classify(srcRef, position, CorrectionStore.For(CorrectionStore.PanelKey(srcBtr)));
                var build = new BuildResult
                {
                    BlockRefId = panel.RefId,
                    BtrId = panel.BtrId,
                    BlockName = panel.Name,
                    PanelBlockName = cls.PanelBlockName,
                    Kept = true,
                    SourceRefId = srcRef,
                    SourceBtrId = srcBtr,
                    SourceTransform = xform,
                    Input = meta.Input,
                    Position = position,
                };
                build.ClassifiedParts.AddRange(cls.Parts);

                int dropped = AddSolids(db, panel, meta, build);
                build.SolidCount = build.Solids.Count(s => !s.Deleted);
                if (dropped > 0)
                    notes.Add($"{dropped} solid(s) of {panel.Name} are no longer in the file or no longer match the 2D block, so they are not listed — rebuild the panel to bring them back.");
                return build;
            }
            catch (System.Exception ex)
            {
                notes.Add($"Panel {panel.Name} could not be read: {ex.Message}");
                return null;
            }
        }

        /// <summary>Thêm solid đã lưu vào <paramref name="build"/>; trả số solid bị bỏ (đã mất khỏi file, hoặc part 2D không còn khớp).</summary>
        private static int AddSolids(Database db, PanelBlock panel, PanelMeta meta, BuildResult build)
        {
            var match = new PartMatcher<PanelPart>(build.ClassifiedParts, p => p.SourceKey, p => p.Type.ToString());
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                bool Exists(string handle)
                {
                    ObjectId id = Resolve(db, handle);
                    return !id.IsNull && !id.IsErased && tr.GetObject(id, OpenMode.ForRead) is Entity e && e.OwnerId == panel.BtrId;
                }

                List<PanelMetaSolid> live = meta.LiveSolids(Exists, out int dropped);
                foreach (PanelMetaSolid m in live)
                {
                    PanelPart part = null;
                    if (!string.IsNullOrEmpty(m.PartKey))
                    {
                        part = match.Take(m.PartKey, m.Type);
                        if (part == null) { dropped++; continue; }
                        part.SnipLo = m.SnipLo;   // builder ghi lúc dựng, phân loại không có — tab Result hiện theo đây
                        part.SnipHi = m.SnipHi;
                    }

                    var solid = new BuiltSolid
                    {
                        // Dòng Deleted: solid đã xoá không còn trong file đã lưu ⇒ SolidId null, dòng vẫn hiện để Restore.
                        SolidId = Resolve(db, m.Handle),
                        StoredHandle = m.Handle,
                        Part = part,
                        Type = m.Type,
                        // Meta ghi "" thay null — tab Result phân biệt null (vd FlipKey null = không đảo được).
                        FlipKey = NullIfEmpty(m.FlipKey),
                        DeleteKey = NullIfEmpty(m.DeleteKey),
                        Code = NullIfEmpty(m.Code),
                        Deleted = m.Deleted,
                    };
                    if (m.MergedCodes != null) solid.MergedCodes.AddRange(m.MergedCodes);
                    if (!m.Deleted) solid.Bounds = BoundsOf(tr, solid.SolidId);
                    build.Solids.Add(solid);
                }
                tr.Commit();
                return dropped;
            }
        }

        private static Extents3d BoundsOf(Transaction tr, ObjectId id)
        {
            try { return ((Entity)tr.GetObject(id, OpenMode.ForRead)).GeometricExtents; }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                // Như Build3DService: để Bounds rỗng — dòng vẫn highlight được, chỉ không zoom.
                Debug.WriteLine($"[PanelSession] GeometricExtents {id.Handle}: {ex.Message}");
                return new Extents3d();
            }
        }

        /// <summary>Handle hex → ObjectId; Null nếu handle hỏng hoặc không có trong file. Id trả về có thể đã xoá.</summary>
        private static ObjectId Resolve(Database db, string handle)
        {
            if (!long.TryParse(handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long value)) return ObjectId.Null;
            return db.TryGetObjectId(new Handle(value), out ObjectId id) ? id : ObjectId.Null;
        }

        private static string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;
    }
}
