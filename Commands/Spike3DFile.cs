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
    /// (1) Wblock giữ nguyên handle, (2) mở file từ ngữ cảnh palette, (3) Classify trên bản copy ra cùng số part.
    /// Không sửa bản vẽ nguồn.
    /// </summary>
    internal static class Spike3DFile
    {
        private const string LAYER = "MCG3D_2D_SOURCE";

        /// <summary>Chạy từ nút Spike (ngữ cảnh palette). <paramref name="status"/> in ra status của tab Input.</summary>
        public static void Run(Action<string, bool> status)
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

            var peo = new PromptEntityOptions("\nSpike: select a 2D panel block reference: ");
            peo.SetRejectMessage("\nNot a block reference.");
            peo.AddAllowedClass(typeof(BlockReference), true);
            PromptEntityResult pr = ed.GetEntity(peo);
            if (pr.Status != PromptStatus.OK) { status("Spike: cancelled.", true); return; }

            ObjectId srcRefId = pr.ObjectId;
            Handle refHandle = srcRefId.Handle;
            string path = Path.Combine(Path.GetTempPath(), "mcg_spike_3D.dwg");
            Database copy = null;
            Document copyDoc = null;
            bool saved = false;

            // Check 1: Wblock giữ handle (cả block lồng nhau)
            try
            {
                copy = src.Database.Wblock();
                var seen = new HashSet<ObjectId>();
                var missing = new List<string>();
                int total = 0, kept = 0;
                using (var trS = src.Database.TransactionManager.StartOpenCloseTransaction())
                using (var trC = copy.TransactionManager.StartOpenCloseTransaction())
                {
                    void Check(DBObject o)
                    {
                        total++;
                        if (copy.TryGetObjectId(o.Handle, out ObjectId cid) && !cid.IsNull
                            && trC.GetObject(cid, OpenMode.ForRead, true).GetType() == o.GetType()) kept++;
                        else missing.Add(o.Handle + ":" + o.GetType().Name);
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
            catch (System.Exception ex) { Log("CHECK 1 (Wblock/handles) FAILED: " + ex.Message, true); }

            // Check 2: layer đóng băng + khoá, chuyển ref sang layer đó, SaveAs
            try
            {
                if (copy == null) throw new InvalidOperationException("no copy database (check 1 failed)");
                if (!copy.TryGetObjectId(refHandle, out ObjectId cref)) throw new InvalidOperationException("reference handle not found in copy");
                using (var tr = copy.TransactionManager.StartTransaction())
                {
                    var lt = (LayerTable)tr.GetObject(copy.LayerTableId, OpenMode.ForWrite);
                    ObjectId lid;
                    if (lt.Has(LAYER)) lid = lt[LAYER];
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
                copy.SaveAs(path, DwgVersion.Current);
                saved = true;
                Log("2) saved: " + path);
            }
            catch (System.Exception ex) { Log("CHECK 2 (layer/SaveAs) FAILED: " + ex.Message, true); }
            finally { copy?.Dispose(); copy = null; }

            // Check 3: mở file từ ngữ cảnh palette (không phải CommandMethod)
            try
            {
                if (!saved) throw new InvalidOperationException("file was not saved (check 2 failed)");
                copyDoc = AcadApp.DocumentManager.Open(path, false);
                AcadApp.DocumentManager.MdiActiveDocument = copyDoc;
                bool active = AcadApp.DocumentManager.MdiActiveDocument == copyDoc;
                Log("3) opened: " + (active ? "yes" : "no (opened but did not become active)"), !active);
            }
            catch (System.Exception ex) { Log("CHECK 3 (open from palette) FAILED: opened: no - " + ex.Message, true); }

            // Check 4: Classify nguồn vs copy — cùng handle, Center, không correction
            try
            {
                var empty = new Dictionary<string, PartCorrection>();
                var svc = new ClassificationService();
                int srcParts = svc.Classify(srcRefId, PanelPosition.Center, empty).Parts.Count;
                if (copyDoc == null) throw new InvalidOperationException("copy not open (check 3 failed); source parts = " + srcParts);
                if (!copyDoc.Database.TryGetObjectId(refHandle, out ObjectId cid)) throw new InvalidOperationException("reference handle not found in opened copy");
                int copyParts;
                using (copyDoc.LockDocument())
                    copyParts = svc.Classify(cid, PanelPosition.Center, empty).Parts.Count;
                Log($"4) parts: copy {copyParts} / source {srcParts}", copyParts != srcParts);
            }
            catch (System.Exception ex) { Log("CHECK 4 (Classify) FAILED: " + ex.Message, true); }

            // Bản copy vẫn mở để kỹ sư xem — đóng tay, không cần lưu.
            Log(anyFail ? "RESULT: GATE NOT PASSED" : "RESULT: gate passed");
            status("Spike - " + string.Join("  |  ", lines), anyFail);
        }
    }
}
