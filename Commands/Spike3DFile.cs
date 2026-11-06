using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using MCG_3DPanel.Models;
using MCG_3DPanel.Services;
using MCG_3DPanel.Services.Corrections;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace MCG_3DPanel.Commands
{
    /// <summary>
    /// SPIKE TẠM THỜI (xoá ở Task 7): kiểm 3 giả thiết trước khi làm bản vẽ 3D riêng —
    /// (1) bản đọc lại bằng ReadDwgFile giữ nguyên handle, (2) mở file từ ngữ cảnh palette, (3) Classify trên bản copy ra cùng số part.
    /// Không sửa bản vẽ nguồn.
    /// </summary>
    internal static class Spike3DFile
    {
        private const string LAYER = "MCG3D_2D_SOURCE";

        /// <summary>So danh tính 2 entity: BlockReference theo tên block + vị trí; còn lại theo layer + extents.
        /// Trả về null nếu khớp, hoặc lý do lệch.</summary>
        private static string Identity(Transaction trS, Entity s, Transaction trC, Entity c)
        {
            const double tol = 0.001;
            if (s is BlockReference bs)
            {
                var bc = (BlockReference)c;
                string ns = ((BlockTableRecord)trS.GetObject(bs.BlockTableRecord, OpenMode.ForRead)).Name;
                string nc = ((BlockTableRecord)trC.GetObject(bc.BlockTableRecord, OpenMode.ForRead)).Name;
                if (ns != nc) return $"block '{ns}' vs '{nc}'";
                if (bs.Position.DistanceTo(bc.Position) > tol) return "position differs";
                return null;
            }
            string ls = ((LayerTableRecord)trS.GetObject(s.LayerId, OpenMode.ForRead)).Name;
            string lc = ((LayerTableRecord)trC.GetObject(c.LayerId, OpenMode.ForRead)).Name;
            if (ls != lc) return $"layer '{ls}' vs '{lc}'";
            Autodesk.AutoCAD.DatabaseServices.Extents3d? es = Ext(s), ec = Ext(c);
            if (es.HasValue != ec.HasValue) return "extents present on one side only";
            if (es.HasValue && (es.Value.MinPoint.DistanceTo(ec.Value.MinPoint) > tol
                             || es.Value.MaxPoint.DistanceTo(ec.Value.MaxPoint) > tol)) return "extents differ";
            return null;
        }

        private static Autodesk.AutoCAD.DatabaseServices.Extents3d? Ext(Entity e)
        {
            try { return e.GeometricExtents; } catch { return null; } // entity không có extents
        }

        /// <summary>Chạy từ nút Spike (ngữ cảnh palette). <paramref name="status"/> in ra status của tab Input.</summary>
        public static void Run(Action<string, bool> status)
        {
            TracePath = Path.Combine(Path.GetTempPath(), "mcg_spike_trace.txt");
            try { File.WriteAllText(TracePath, ""); } catch { }
            Trace("Run start");
            // Chặn mọi exception thoát khỏi Click handler (WPF) — nếu không AutoCAD chết.
            void Safe(string m, bool err) { try { status(m, err); } catch (System.Exception sx) { Trace("status callback threw: " + sx); } }
            try { RunBody(Safe); }
            catch (System.Exception ex) { Trace("UNHANDLED: " + ex); Safe("Spike failed: " + ex.Message + "  (trace: " + TracePath + ")", true); }
            Trace("Run end");
        }

        private static string TracePath;

        /// <summary>Ghi 1 dòng có giờ, flush ngay; tự nuốt lỗi.</summary>
        private static void Trace(string s)
        {
            try { File.AppendAllText(TracePath ?? Path.Combine(Path.GetTempPath(), "mcg_spike_trace.txt"), DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s + Environment.NewLine); } catch { }
        }

        private static void RunBody(Action<string, bool> status)
        {
            Document src = AcadApp.DocumentManager.MdiActiveDocument;
            if (src == null) { status("Spike: no active drawing.", true); return; }
            Editor ed = src.Editor;
            var lines = new List<string>();
            bool anyFail = false;

            // In ra command line ngay + gom lại để hiện trên status — không im lặng.
            void Log(string s, bool fail = false)
            {
                ed.WriteMessage("\n[Spike] " + s);
                lines.Add(s);
                if (fail) anyFail = true;
            }

            // Tiền điều kiện: bản vẽ đã lưu và không còn thay đổi chưa lưu (đường dẫn .dwt vẫn được).
            string srcFile = src.Database.Filename;
            bool dirty = false;
            try { dirty = Convert.ToInt32(AcadApp.GetSystemVariable("DBMOD")) != 0; } catch { dirty = true; }
            if (string.IsNullOrEmpty(srcFile) || !File.Exists(srcFile) || dirty)
            {
                Log("Save this drawing first, then run Spike again.", true);
                status("Save this drawing first, then run Spike again.", true);
                return;
            }

            var peo = new PromptEntityOptions("\nSpike: select a 2D panel block reference: ");
            peo.SetRejectMessage("\nNot a block reference.");
            peo.AddAllowedClass(typeof(BlockReference), true);
            Trace("pick: start");
            PromptEntityResult pr = ed.GetEntity(peo);
            Trace("pick: end " + pr.Status);
            if (pr.Status != PromptStatus.OK) { status("Spike: cancelled.", true); return; }

            ObjectId srcRefId = pr.ObjectId;
            Handle refHandle = srcRefId.Handle;
            // Tên file duy nhất mỗi lần chạy — tránh Open trả về document cũ còn mở.
            string path = Path.Combine(Path.GetTempPath(), $"mcg_spike_3D_{DateTime.Now:yyyyMMdd_HHmmss}.dwg");
            Database copy = null;
            Document copyDoc = null;
            bool saved = false;

            Trace("check 1: start");
            // Check 1: bản đọc lại từ file giữ handle + danh tính (cả block lồng nhau)
            try
            {
                // Nạp lại file nguồn trên đĩa (Wblock làm mất handle). Không ghi vào nguồn.
                copy = new Database(false, true);
                Trace("ReadDwgFile: start " + srcFile);
                copy.ReadDwgFile(srcFile, FileOpenMode.OpenForReadAndAllShare, true, null);
                Trace("ReadDwgFile: end");
                copy.CloseInput(true);
                var seen = new HashSet<ObjectId>();
                var missing = new List<string>();
                int total = 0, kept = 0;
                using (var trS = src.Database.TransactionManager.StartOpenCloseTransaction())
                using (var trC = copy.TransactionManager.StartOpenCloseTransaction())
                {
                    void Check(DBObject o)
                    {
                        total++;
                        string why = null;
                        if (!copy.TryGetObjectId(o.Handle, out ObjectId cid) || cid.IsNull) why = "no such handle";
                        else
                        {
                            var c = trC.GetObject(cid, OpenMode.ForRead, true);
                            if (c.GetType() != o.GetType()) why = "type " + c.GetType().Name;
                            else why = Identity(trS, (Entity)o, trC, (Entity)c);
                        }
                        if (why == null) kept++;
                        else missing.Add(o.Handle + ":" + o.GetType().Name + " (" + why + ")");
                    }
                    void Walk(ObjectId btrId)
                    {
                        if (!seen.Add(btrId)) return;
                        var btr = (BlockTableRecord)trS.GetObject(btrId, OpenMode.ForRead);
                        foreach (ObjectId id in btr)
                        {
                            var e = (Entity)trS.GetObject(id, OpenMode.ForRead, true);
                            Check(e);
                            if (e is BlockReference br) Walk(br.BlockTableRecord);
                        }
                    }
                    var top = (BlockReference)trS.GetObject(srcRefId, OpenMode.ForRead);
                    Check(top);
                    Walk(top.BlockTableRecord);
                }
                string miss = missing.Count > 0 ? "  missing: " + string.Join(", ", missing.GetRange(0, Math.Min(10, missing.Count))) : "";
                Log($"1) handles kept: {kept} / {total}{miss}", kept != total);
            }
            catch (System.Exception ex) { Log("CHECK 1 (ReadDwgFile/handles) FAILED: " + ex.Message, true); copy?.Dispose(); copy = null; }

            Trace("check 1: end");
            Trace("check 2: start");
            // Check 2: layer đóng băng + khoá, chuyển ref sang layer đó, SaveAs
            try
            {
                if (copy == null) throw new InvalidOperationException("no copy database (check 1 failed)");
                if (!copy.TryGetObjectId(refHandle, out ObjectId cref)) throw new InvalidOperationException("reference handle not found in copy");
                using (var tr = copy.TransactionManager.StartTransaction())
                {
                    var lt = (LayerTable)tr.GetObject(copy.LayerTableId, OpenMode.ForWrite);
                    ObjectId lid;
                    if (lt.Has(LAYER))
                    {
                        lid = lt[LAYER];
                        var old = (LayerTableRecord)tr.GetObject(lid, OpenMode.ForWrite);
                        old.IsFrozen = true; old.IsLocked = true; // layer có sẵn vẫn phải ép trạng thái
                    }
                    else
                    {
                        var ltr = new LayerTableRecord { Name = LAYER, IsFrozen = true, IsLocked = true };
                        lid = lt.Add(ltr);
                        tr.AddNewlyCreatedDBObject(ltr, true);
                    }
                    var e = (Entity)tr.GetObject(cref, OpenMode.ForWrite);
                    e.LayerId = lid;
                    tr.Commit();
                }
                if (File.Exists(path)) File.Delete(path);
                Trace("SaveAs: start " + path);
                copy.SaveAs(path, DwgVersion.Current);
                Trace("SaveAs: end");
                saved = true;
                Log("2) saved: " + path);
            }
            catch (System.Exception ex) { Log("CHECK 2 (layer/SaveAs) FAILED: " + ex.Message, true); }
            finally { copy?.Dispose(); copy = null; }

            Trace("check 2: end");
            Trace("check 3: start");
            // Check 3: mở file từ ngữ cảnh palette (không phải CommandMethod)
            try
            {
                if (!saved) throw new InvalidOperationException("file was not saved (check 2 failed)");
                Trace("DocumentManager.Open: start");
                copyDoc = AcadApp.DocumentManager.Open(path, false);
                Trace("DocumentManager.Open: end");
                Trace("set MdiActiveDocument: start");
                AcadApp.DocumentManager.MdiActiveDocument = copyDoc;
                Trace("set MdiActiveDocument: end");
                bool active = AcadApp.DocumentManager.MdiActiveDocument == copyDoc;
                Log("3) opened: " + (active ? "yes" : "no (opened but did not become active)"), !active);
            }
            catch (System.Exception ex) { Log("CHECK 3 (open from palette) FAILED: opened: no - " + ex.Message, true); }

            Trace("check 3: end");
            Trace("check 4: start");
            // Check 4: Classify nguồn vs copy — cùng handle, Center, không correction
            try
            {
                var empty = new Dictionary<string, PartCorrection>();
                var svc = new ClassificationService();
                Trace("Classify source: start");
                int srcParts = svc.Classify(srcRefId, PanelPosition.Center, empty).Parts.Count;
                Trace("Classify source: end");
                if (copyDoc == null) throw new InvalidOperationException("copy not open (check 3 failed); source parts = " + srcParts);
                if (!copyDoc.Database.TryGetObjectId(refHandle, out ObjectId cid)) throw new InvalidOperationException("reference handle not found in opened copy");
                int copyParts;
                Trace("LockDocument copy: start");
                using (copyDoc.LockDocument())
                {
                    Trace("LockDocument copy: end; Classify copy: start");
                    copyParts = svc.Classify(cid, PanelPosition.Center, empty).Parts.Count;
                    Trace("Classify copy: end");
                }
                // Đọc lại layer trong file đã mở: phải frozen + locked và ref nằm trên layer đó.
                Trace("layer read-back: start");
                string layerInfo; bool layerOk;
                using (var tr = copyDoc.Database.TransactionManager.StartOpenCloseTransaction())
                {
                    var r = (BlockReference)tr.GetObject(cid, OpenMode.ForRead, true);
                    var l = (LayerTableRecord)tr.GetObject(r.LayerId, OpenMode.ForRead);
                    layerOk = l.Name == LAYER && l.IsFrozen && l.IsLocked;
                    layerInfo = $"ref layer '{l.Name}', frozen={l.IsFrozen}, locked={l.IsLocked}";
                }
                Trace("layer read-back: end");
                Log($"4) parts: copy {copyParts} / source {srcParts}; {layerInfo}", copyParts != srcParts || !layerOk);
            }
            catch (System.Exception ex) { Log("CHECK 4 (Classify) FAILED: " + ex.Message, true); }

            // Bản copy vẫn mở để kỹ sư xem — đóng tay, không cần lưu.
            Trace("check 4: end");
            Log(anyFail ? "RESULT: GATE NOT PASSED" : "RESULT: gate passed");
            Trace("final status");
            status("Spike - " + string.Join("  |  ", lines), anyFail);
        }
    }
}
