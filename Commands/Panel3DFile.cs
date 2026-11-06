using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

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

        private const string BLOCK_PREFIX = "MCG3D_";

        // Bản ghi hệ thống: Database.Purge không hứa loại các tên này ⇒ không đưa vào danh sách purge.
        private static readonly HashSet<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "0", "Defpoints", "Standard", "Continuous", "ByLayer", "ByBlock",
        };

        /// <summary>Ghi file 3D tại <paramref name="path"/>. Không ghi đè file có sẵn. Lỗi ⇒ ném exception thông điệp tiếng Anh.</summary>
        public static void Create(Database source, ObjectId ref2D, string path)
        {
            string name = Path.GetFileName(path);
            if (File.Exists(path)) throw new InvalidOperationException($"{name} already exists.");

            string file = source.Filename;
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
                EraseBuiltBlocks(copy);
                MoveToSourceLayer(copy, refId);
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

        private static void DeleteQuietly(string path)
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
