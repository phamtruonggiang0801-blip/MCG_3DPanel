using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using MCG_3DPanel.Models;
using MCG_3DPanel.Services.Corrections;

namespace MCG_3DPanel.Commands
{
    /// <summary>
    /// File 3D riêng của 1 panel: bản sao file nguồn ĐÃ LƯU, rút còn đúng 1 block 2D (giữ nguyên handle — SourceKey,
    /// correction, tên đều theo handle). Spec 2026-11-06 "File creation". Chỉ làm việc trên database phụ,
    /// KHÔNG BAO GIỜ ghi vào Database/Document nguồn.
    /// </summary>
    public static class Panel3DFile
    {
        /// <summary>Layer đóng băng + khoá chứa block 2D nguồn trong file 3D.</summary>
        public const string SourceLayer = "MCG3D_2D_SOURCE";

        internal const string BLOCK_PREFIX = "MCG3D_";

        // Bản ghi hệ thống: Database.Purge không hứa loại các tên này ⇒ không đưa vào danh sách purge.
        private static readonly HashSet<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "0", "Defpoints", "Standard", "Continuous", "ByLayer", "ByBlock",
        };

        /// <summary>Drawing đang mở là file 3D (layer <see cref="SourceLayer"/> có block reference 2D nguồn). Chỉ đọc, không cần LockDocument.</summary>
        public static bool HoldsSourceReference(Database db)
        {
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                if (!lt.Has(SourceLayer)) return false;
                ObjectId layer = lt[SourceLayer];
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                    if (tr.GetObject(id, OpenMode.ForRead) is BlockReference br && br.LayerId == layer) return true;
                return false;
            }
        }

        /// <summary>Ghi file 3D tại <paramref name="path"/> từ file nguồn đã lưu <paramref name="file"/> (Document.Name, KHÔNG dùng Database.Filename:
        /// sau autosave có thể là file .sv$ trong %TEMP%). Không ghi đè file có sẵn. Lỗi ⇒ ném exception thông điệp tiếng Anh.
        /// <paramref name="carry"/> (Update): correction và tên của file 3D cũ, thay cho bản đến từ file nguồn.</summary>
        public static void Create(string file, ObjectId ref2D, string path, OldPanelData carry = null)
        {
            string name = Path.GetFileName(path);
            if (File.Exists(path)) throw new InvalidOperationException($"{name} already exists.");

            Handle handle = ref2D.Handle;
            // Ghi ra tên tạm rồi Move KHÔNG ghi đè: file xuất hiện giữa lúc kiểm và lúc ghi vẫn được giữ, đích không bao giờ dở dang.
            string temp = $"{path}.tmp{Guid.NewGuid():N}.dwg";
            Database copy = null;
            try
            {
                // Đọc lại từ đĩa: Wblock() làm mất toàn bộ handle (spike 0/3631), ReadDwgFile giữ đủ (3631/3631).
                copy = new Database(false, true);
                copy.ReadDwgFile(file, FileOpenMode.OpenForReadAndAllShare, true, null);
                copy.CloseInput(true);

                if (!copy.TryGetObjectId(handle, out ObjectId refId) || refId.IsNull || refId.IsErased)
                    throw new InvalidOperationException("the panel is not in the saved file. Save the drawing and try again");

                EraseModelSpaceExcept(copy, refId);
                ErasePaperSpace(copy);
                EraseBuiltBlocks(copy);
                MoveToSourceLayer(copy, refId);
                if (carry != null) WriteCarry(copy, refId, carry);
                PurgeUnused(copy);

                copy.SaveAs(temp, DwgVersion.Current);
                DisposeQuietly(ref copy);   // nhả file tạm trước khi Move
                File.Move(temp, path, false);   // đích đã có ⇒ ném, không ghi đè
            }
            catch (System.Exception ex)
            {
                DeleteQuietly(temp);   // chỉ file tạm của lần này — không bao giờ xoá path
                throw new InvalidOperationException($"Could not create {name}: {ex.Message}", ex);
            }
            finally
            {
                DisposeQuietly(ref copy);
            }
        }

        /// <summary>Dữ liệu người dùng đọc từ file 3D cũ để Update mang sang file mới.</summary>
        public sealed class OldPanelData
        {
            public PanelMeta Meta;
            public string CorrectionsJson;
            public string NamesJson;
        }

        /// <summary>Đọc file 3D cũ từ đĩa bằng database phụ (chỉ đọc, chia sẻ), luôn giải phóng database. Meta null ⇒ file không có dữ liệu panel
        /// dùng được (kể cả Position lạ). File hỏng, hoặc mất block 2D mà meta trỏ tới ⇒ ném: correction không đọc được thì không được coi là không có.</summary>
        public static OldPanelData ReadOld(string path)
        {
            Database db = null;
            try
            {
                db = new Database(false, true);
                db.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, null);
                db.CloseInput(true);

                var old = new OldPanelData();
                using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    old.Meta = ReadMeta(tr, db);
                    if (old.Meta == null) return old;
                    ObjectId btr2D = ResolveBtr(db, old.Meta.SourceBtrHandle);
                    old.CorrectionsJson = CorrectionXRecord.Read(tr, btr2D, CorrectionXRecord.CorrectionsKey);
                    old.NamesJson = CorrectionXRecord.Read(tr, btr2D, CorrectionXRecord.NamesKey);
                }
                return old;
            }
            finally
            {
                DisposeQuietly(ref db);
            }
        }

        /// <summary>MCG3D_PANEL trên định nghĩa MCG3D_* đầu tiên có bản ghi này; không có hoặc không đọc được ⇒ null.</summary>
        private static PanelMeta ReadMeta(Transaction tr, Database db)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId id in bt)
            {
                var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                if (!btr.Name.StartsWith(BLOCK_PREFIX, StringComparison.OrdinalIgnoreCase)) continue;
                string json = CorrectionXRecord.Read(tr, id, CorrectionXRecord.PanelKey);
                if (json == null) continue;
                PanelMeta meta = PanelMeta.FromJson(json);
                return meta != null && Enum.IsDefined(typeof(PanelPosition), meta.Position) ? meta : null;
            }
            return null;
        }

        private static ObjectId ResolveBtr(Database db, string hex)
        {
            if (long.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out long value)
                && db.TryGetObjectId(new Handle(value), out ObjectId id) && !id.IsNull && !id.IsErased)
                return id;
            throw new InvalidOperationException("the 2D block is missing from this file");
        }

        /// <summary>Ghi correction + tên của file cũ lên định nghĩa block 2D của bản sao; JSON rỗng ⇒ xoá bản đến từ file nguồn.</summary>
        private static void WriteCarry(Database db, ObjectId refId, OldPanelData carry)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ObjectId btr2D = ((BlockReference)tr.GetObject(refId, OpenMode.ForRead)).BlockTableRecord;
                CorrectionXRecord.Write(tr, btr2D, CorrectionXRecord.CorrectionsKey, carry.CorrectionsJson);
                CorrectionXRecord.Write(tr, btr2D, CorrectionXRecord.NamesKey, carry.NamesJson);
                tr.Commit();
            }
        }

        /// <summary>Dispose không được đè exception gốc.</summary>
        private static void DisposeQuietly(ref Database db)
        {
            try { db?.Dispose(); }
            catch (System.Exception ex) { Debug.WriteLine($"[Panel3DFile] Dispose: {ex.Message}"); }
            db = null;
        }

        /// <summary>Gọi TRƯỚC lần lưu đầu của file vừa tạo: SaveAs với bBakAndRename sẽ đè .bak cùng tên, mà .bak đó có thể là bản cuối
        /// của người dùng ⇒ dời sang &lt;name&gt;_3D.&lt;yyyyMMdd_HHmmss&gt;.bak. Trả tên mới, null nếu không có .bak. Không dời được ⇒ ném, không được lưu.</summary>
        public static string MoveOldBakAside(string path)
        {
            string bak = Path.ChangeExtension(path, ".bak");
            if (!File.Exists(bak)) return null;
            string kept = Panel3DPath.NextFree(Path.ChangeExtension(path, $".{DateTime.Now:yyyyMMdd_HHmmss}.bak"), File.Exists);
            try { File.Move(bak, kept, false); }
            catch (System.Exception ex)
            {
                throw new InvalidOperationException($"Could not keep the existing {Path.GetFileName(bak)} ({ex.Message}), so the 3D file was not saved. Move that file away and build again.", ex);
            }
            return Path.GetFileName(kept);
        }

        /// <summary>Lưu file 3D đang mở (document vẫn mở) rồi đưa DBMOD về 0 để đóng không bị hỏi lưu.
        /// Trả false nếu file đã lưu nhưng DBMOD chưa về 0. Ghi file lỗi ⇒ ném.</summary>
        public static bool Save(Document doc)
        {
            doc.Database.SaveAs(doc.Name, true, DwgVersion.Current, doc.Database.SecurityParameters);
            // Database.SaveAs không phải QSAVE ⇒ không dựa vào nó để xoá DBMOD.
            try { return Autodesk.AutoCAD.Internal.Utils.SetDbmod(0, doc.Database); }
            catch (System.Exception ex)
            {
                Debug.WriteLine($"[Panel3DFile] SetDbmod: {ex.Message}");
                return false;
            }
        }

        /// <summary>Bỏ file 3D vừa tạo trong lần Build này: đóng không lưu (<paramref name="doc"/> có thể null), xoá file + file kèm.
        /// <paramref name="ownBak"/> = đã qua <see cref="MoveOldBakAside"/>, nên .bak (nếu có) do lần lưu này sinh ra; ngược lại .bak không phải của lần này ⇒ giữ.
        /// Gọi NGOÀI LockDocument. Trả thông báo lỗi (null = OK), không ném.</summary>
        public static string Discard(Document doc, string path, bool ownBak)
        {
            try
            {
                doc?.CloseAndDiscard();
                var files = new List<string> { path, Path.ChangeExtension(path, ".dwl"), Path.ChangeExtension(path, ".dwl2") };
                if (ownBak) files.Add(Path.ChangeExtension(path, ".bak"));
                foreach (string f in files)
                    if (File.Exists(f)) File.Delete(f);
                return null;
            }
            catch (System.Exception ex)
            {
                return $"Could not remove {Path.GetFileName(path)}: {ex.Message}. Delete it by hand.";
            }
        }

        internal static void DeleteQuietly(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (System.Exception ex) { Debug.WriteLine($"[Panel3DFile] Delete {path}: {ex.Message}"); }
        }

        private static void EraseModelSpaceExcept(Database db, ObjectId keep)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                var ids = new List<ObjectId>();
                foreach (ObjectId id in ms) if (id != keep) ids.Add(id);
                foreach (ObjectId id in ids) tr.GetObject(id, OpenMode.ForWrite, false, true).Erase();   // kể cả trên layer khoá
                tr.Commit();
            }
        }

        /// <summary>Xoá nội dung Paper Space của mọi layout (kỹ sư 2026-11-09): file 3D chỉ mang panel. Giữ viewport đầu của mỗi
        /// layout (viewport chính của layout). Trước Purge để block chỉ Paper Space dùng cũng được dọn. Không xoá được 1 đối
        /// tượng thì bỏ qua, ghi Debug: phần còn sót không ảnh hưởng 3D, không đáng làm hỏng cả lần Build.</summary>
        private static void ErasePaperSpace(Database db)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                ObjectId model = bt[BlockTableRecord.ModelSpace];
                foreach (ObjectId btrId in bt)
                {
                    var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                    if (!btr.IsLayout || btrId == model) continue;
                    var ids = new List<ObjectId>();
                    foreach (ObjectId id in btr) ids.Add(id);
                    bool keptViewport = false;
                    foreach (ObjectId id in ids)
                    {
                        try
                        {
                            var e = tr.GetObject(id, OpenMode.ForWrite, false, true);
                            if (e is Viewport && !keptViewport) { keptViewport = true; continue; }
                            e.Erase();
                        }
                        catch (Autodesk.AutoCAD.Runtime.Exception ex) { Debug.WriteLine($"[Panel3DFile] Paper Space {id.Handle}: {ex.Message}"); }
                    }
                }
                tr.Commit();
            }
        }


        /// <summary>Xoá block 3D cũ (MCG3D_*) còn trong file nguồn, kèm mọi reference của chúng.</summary>
        private static void EraseBuiltBlocks(Database db)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId id in bt)
                {
                    var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (!btr.Name.StartsWith(BLOCK_PREFIX, StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (ObjectId r in btr.GetBlockReferenceIds(true, true))
                        if (!r.IsErased) tr.GetObject(r, OpenMode.ForWrite, false, true).Erase();
                    btr.UpgradeOpen();
                    btr.Erase();
                }
                tr.Commit();
            }
        }

        private static void MoveToSourceLayer(Database db, ObjectId refId)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
                ObjectId lid;
                if (lt.Has(SourceLayer))
                {
                    lid = lt[SourceLayer];
                    if (db.Clayer == lid) db.Clayer = db.LayerZero;   // layer hiện hành không đóng băng được
                    var old = (LayerTableRecord)tr.GetObject(lid, OpenMode.ForWrite);
                    old.IsFrozen = true;
                    old.IsLocked = true;
                }
                else
                {
                    var ltr = new LayerTableRecord { Name = SourceLayer, IsFrozen = true, IsLocked = true };
                    lid = lt.Add(ltr);
                    tr.AddNewlyCreatedDBObject(ltr, true);
                }
                var br = tr.GetObject(refId, OpenMode.ForWrite, false, true) as BlockReference;
                if (br == null) throw new InvalidOperationException("the selected object is not a block reference in the saved file");
                br.LayerId = lid;
                tr.Commit();
            }
        }

        /// <summary>Purge lặp đến khi không xoá thêm được gì: xoá 1 block mới lộ ra block/layer con của nó.</summary>
        private static void PurgeUnused(Database db)
        {
            var refused = new HashSet<ObjectId>();
            bool erased = true;
            while (erased)
            {
                erased = false;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    var ids = new ObjectIdCollection();
                    foreach (ObjectId tableId in new[] { db.BlockTableId, db.LayerTableId, db.LinetypeTableId, db.TextStyleTableId, db.DimStyleTableId })
                        AddPurgeCandidates(tr, tableId, ids);
                    db.Purge(ids);
                    foreach (ObjectId id in ids)
                    {
                        if (refused.Contains(id)) continue;
                        try
                        {
                            tr.GetObject(id, OpenMode.ForWrite).Erase();
                            erased = true;
                        }
                        catch (Autodesk.AutoCAD.Runtime.Exception ex)
                        {
                            // Dọn dẹp, không mất dữ liệu: bản ghi còn lại chỉ làm file nặng hơn. Nhớ lại để vòng lặp kết thúc.
                            refused.Add(id);
                            Debug.WriteLine($"[Panel3DFile] Purge {id.Handle}: {ex.Message}");
                        }
                    }
                    tr.Commit();
                }
            }
        }

        private static void AddPurgeCandidates(Transaction tr, ObjectId tableId, ObjectIdCollection ids)
        {
            foreach (ObjectId id in (SymbolTable)tr.GetObject(tableId, OpenMode.ForRead))
            {
                var rec = (SymbolTableRecord)tr.GetObject(id, OpenMode.ForRead);
                if (Reserved.Contains(rec.Name) || rec.IsDependent) continue;
                if (rec is BlockTableRecord btr && btr.IsLayout) continue;
                ids.Add(id);
            }
        }
    }
}
