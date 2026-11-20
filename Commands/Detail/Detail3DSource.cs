using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCG_3DPanel.Models;
using MCG_3DPanel.Services.Classify;
using MCG_3DPanel.Services.Corrections;
using MCG_3DPanel.Services.Detail;

namespace MCG_3DPanel.Commands.Detail
{
    /// <summary>Một solid của block MCG3D_*: Id nằm trong <see cref="Detail3DSource.SourceDb"/> (KHÔNG phải bản sao), Extents ở WCS của file 3D.</summary>
    public sealed class SolidItem
    {
        public ObjectId Id;
        public Extents3d Extents;
        public string Type;
        public string Handle;
    }

    /// <summary>Tìm file &lt;panel&gt;_3D*.dwg cạnh bản vẽ 2D, đọc nền (database phụ, hoặc database của document đang mở) và đưa solid cho bộ cắt.
    /// Không bao giờ Clone solid: bộ cắt tự chép bằng WblockCloneObjects.</summary>
    public sealed class Detail3DSource : IDisposable
    {
        private Database _ownedDb;   // chỉ database phụ do lớp này tạo; null khi dùng database của document đang mở

        public StaleState Stale { get; private set; }
        public string FilePath { get; private set; }
        public Matrix3d PlanTo3D { get; private set; }
        public double TopPlateThickness { get; private set; }
        public Database SourceDb { get; private set; }
        public Matrix3d BlockToWcs { get; private set; }
        public IReadOnlyList<SolidItem> Solids { get; private set; } = new List<SolidItem>();

        private Detail3DSource() { }

        /// <summary>1 file ứng viên đã đọc xong (database còn mở).</summary>
        private sealed class Loaded
        {
            public Candidate3D Info;
            public Database Db;
            public bool Owned;    // database phụ do ta tạo
            public bool Taken;    // đã giao cho Detail3DSource
            public PanelMeta Meta;
            public ObjectId BlockRefId;
        }

        /// <summary>choose(paths) chọn 1 file khi có nhiều file khớp (null ⇒ huỷ). Trả null kèm <paramref name="why"/> khi không có file dùng được.</summary>
        public static Detail3DSource Open(Database db2D, string drawingPath, ObjectId assyRefId,
            Func<IList<string>, string> choose, out string why)
        {
            why = null;
            string panel, assyName, fingerprint;
            Matrix3d assyXform;
            using (Transaction tr = db2D.TransactionManager.StartOpenCloseTransaction())
            {
                var assy = (BlockReference)tr.GetObject(assyRefId, OpenMode.ForRead);
                string name = PartCollector.GetName(tr, assy);
                assyName = name;
                panel = name.EndsWith("_Assy", StringComparison.Ordinal) ? name.Substring(0, name.Length - 5) : name;
                fingerprint = Detail3DMatch.Fingerprint(SourceFingerprintReader.Records(tr, assy.BlockTableRecord));
                assyXform = assy.BlockTransform;
            }
            string refHandle = assyRefId.Handle.ToString();

            string folder = string.IsNullOrEmpty(drawingPath) ? null : Path.GetDirectoryName(drawingPath);
            string[] files = folder == null || !Directory.Exists(folder)
                ? new string[0]
                : Directory.GetFiles(folder, Detail3DMatch.CandidatePattern(assyName)); // Build 3D đặt tên theo tên block _Assy

            var loaded = new List<Loaded>();
            string current = null;   // file đang đọc (lỗi catch-all nêu tên)
            try
            {
                var failures = new List<(string File, string Error)>();
                foreach (string file in files)
                {
                    try
                    {
                        Loaded l = Load(file);
                        if (l != null) loaded.Add(l);   // null = file không phải panel (không có meta)
                    }
                    catch (System.Exception ex)
                    {
                        failures.Add((Path.GetFileName(file), ex.Message));
                        CommandLine($"Could not read {file}:\n{ex}");   // lỗi đầy đủ ra dòng lệnh
                    }
                }

                var matches = Detail3DMatch.Matching(loaded.Select(l => l.Info), drawingPath, refHandle)
                    // Nhiều file khớp (vd bản chép "…_3D (2).dwg" giữ nguyên meta) ⇒ mới lưu nhất đứng đầu = mặc định khi hỏi
                    .OrderByDescending(m => SafeWriteTime(m.Path)).ToList();
                if (matches.Count == 0)
                {
                    // Không báo "chưa có file" sai sự thật: nói file nào dựng từ bản vẽ khác / đọc lỗi.
                    why = Detail3DMatch.NoMatchReason(panel, loaded.Select(l => l.Info), failures, drawingPath, refHandle);
                    return null;
                }

                Candidate3D pick = matches[0];
                if (matches.Count > 1 && choose != null)   // không có choose (đọc nền cho tab) ⇒ file lưu mới nhất
                {
                    string path = choose?.Invoke(matches.Select(m => m.Path).ToList());
                    pick = path == null ? null : matches.FirstOrDefault(m => string.Equals(m.Path, path, StringComparison.OrdinalIgnoreCase));
                    if (pick == null) { why = "Cancelled."; return null; }
                }
                Loaded chosen = loaded.First(l => l.Info == pick);
                current = Path.GetFileName(chosen.Info.Path);

                var src = new Detail3DSource
                {
                    FilePath = chosen.Info.Path,
                    Stale = Detail3DMatch.Stale(chosen.Info, fingerprint),
                    SourceDb = chosen.Db,
                };
                src.Fill(chosen, assyXform);
                // Fill ném thì chosen chưa Taken ⇒ finally giải phóng database phụ.
                src._ownedDb = chosen.Owned ? chosen.Db : null;
                chosen.Taken = true;
                return src;
            }
            catch (System.Exception ex)
            {
                // Lỗi bất ngờ: tên file + lỗi lên status, ex.ToString() đầy đủ ra dòng lệnh (Global Constraint)
                why = $"Could not read the 3D file{(current != null ? " " + current : "")}: {ex.Message} (details on the command line).";
                CommandLine($"Could not read the 3D file{(current != null ? " " + current : "")}:\n{ex}");
                return null;
            }
            finally
            {
                foreach (Loaded l in loaded)
                    if (l.Owned && !l.Taken) DisposeQuietly(l.Db);
            }
        }

        /// <summary>Đọc meta + block reference của 1 file. Null ⇒ file không có dữ liệu panel. Lỗi đọc ⇒ ném (database phụ đã được giải phóng).</summary>
        private static Loaded Load(string path)
        {
            var l = new Loaded();
            var doc = PanelCommands.FindOpen(path);
            bool ok = false;
            try
            {
                if (doc != null) l.Db = doc.Database;
                else
                {
                    l.Db = new Database(false, true);
                    l.Owned = true;
                    l.Db.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, null);
                    l.Db.CloseInput(true);
                }

                using (Transaction tr = l.Db.TransactionManager.StartOpenCloseTransaction())
                {
                    ObjectId btrId = ObjectId.Null;
                    var bt = (BlockTable)tr.GetObject(l.Db.BlockTableId, OpenMode.ForRead);
                    foreach (ObjectId id in bt)
                    {
                        var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                        if (!btr.Name.StartsWith(Panel3DFile.BLOCK_PREFIX, StringComparison.OrdinalIgnoreCase)) continue;
                        string json = CorrectionXRecord.Read(tr, id, CorrectionXRecord.PanelKey);
                        if (json == null) continue;
                        l.Meta = PanelMeta.FromJson(json);
                        btrId = id;
                        break;
                    }
                    if (l.Meta == null) return null;

                    l.BlockRefId = ModelSpaceRef(tr, l.Db, btrId);
                    if (l.BlockRefId.IsNull) throw new InvalidOperationException("the panel block is not in Model Space");
                }

                l.Info = new Candidate3D
                {
                    Path = path,
                    SourcePath = l.Meta.SourcePath,
                    SourceRefHandle = l.Meta.SourceRefHandle,
                    Fingerprint = l.Meta.SourceFingerprint,
                    OpenUnsaved = doc != null && !PanelCommands.IsSavedOnDisk(doc),
                };
                ok = true;
                return l;
            }
            finally
            {
                // Trả null (không phải panel) hay ném đều không để database phụ mồ côi.
                if (!ok && l.Owned) DisposeQuietly(l.Db);
            }
        }

        private static DateTime SafeWriteTime(string path)
        {
            try { return File.GetLastWriteTime(path); } catch { return DateTime.MinValue; }
        }

        private static ObjectId ModelSpaceRef(Transaction tr, Database db, ObjectId btrId)
        {
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
                if (tr.GetObject(id, OpenMode.ForRead) is BlockReference br && br.BlockTableRecord == btrId) return id;
            return ObjectId.Null;
        }

        private static ObjectId Resolve(Database db, string handle)
        {
            if (!long.TryParse(handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long v)) return ObjectId.Null;
            return db.TryGetObjectId(new Handle(v), out ObjectId id) ? id : ObjectId.Null;
        }

        /// <summary>PlanTo3D, BlockToWcs, solid và độ dày Top Plate từ file đã chọn.</summary>
        private void Fill(Loaded l, Matrix3d assyXform2D)
        {
            PanelMeta meta = l.Meta;
            using (Transaction tr = l.Db.TransactionManager.StartOpenCloseTransaction())
            {
                var blockRef = (BlockReference)tr.GetObject(l.BlockRefId, OpenMode.ForRead);
                BlockToWcs = blockRef.BlockTransform;

                ObjectId srcRefId = Resolve(l.Db, meta.SourceRefHandle);
                if (srcRefId.IsNull || srcRefId.IsErased || !(tr.GetObject(srcRefId, OpenMode.ForRead) is BlockReference srcRef))
                    throw new InvalidOperationException("the 2D source block is missing from the 3D file");
                PlanTo3D = srcRef.BlockTransform * assyXform2D.Inverse();

                var list = new List<SolidItem>();
                double plate = 0;
                foreach (PanelMetaSolid m in meta.Solids)
                {
                    if (m.Deleted) continue;
                    ObjectId id = Resolve(l.Db, m.Handle);
                    if (id.IsNull || id.IsErased) continue;
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null || ent.OwnerId != blockRef.BlockTableRecord) continue;
                    Extents3d ext;
                    try { ext = ent.GeometricExtents; }
                    catch (Autodesk.AutoCAD.Runtime.Exception) { continue; }   // solid không có extents: không cắt được
                    ext.TransformBy(BlockToWcs);
                    list.Add(new SolidItem { Id = id, Extents = ext, Type = m.Type, Handle = m.Handle });
                    if (m.Type == nameof(PartType.TopPlate)) plate = Math.Max(plate, ext.MaxPoint.Z - ext.MinPoint.Z);
                }
                Solids = list;
                TopPlateThickness = plate;
                tr.Commit();
            }
        }

        /// <summary>Chỉ giải phóng database phụ; database của document đang mở không bao giờ bị đụng.</summary>
        public void Dispose()
        {
            Database db = _ownedDb;
            _ownedDb = null;
            SourceDb = null;
            DisposeQuietly(db);
        }

        /// <summary>Ghi ra dòng lệnh của document đang hoạt động (bỏ qua nếu không có).</summary>
        private static void CommandLine(string msg)
        {
            try { Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Editor?.WriteMessage("\n[MCG_3DPanel] " + msg + "\n"); }
            catch { }
        }

        private static void DisposeQuietly(Database db)
        {
            try { db?.Dispose(); }
            catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine($"[Detail3DSource] Dispose: {ex.Message}"); }
        }
    }
}
