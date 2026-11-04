using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using MCG_3DPanel.Services;

namespace MCG_3DPanel.Commands
{
    /// <summary>
    /// Export STEP (tab Result, spec 2026-11-04 §15): MỖI PANEL 1 FILE <c>&lt;Panel&gt;.stp</c> qua lệnh STEPOUT của
    /// AutoCAD Mechanical (Acadm/atf_step_out.arx). Mỗi panel: explode TẠM 1 bản sao block 3D ra Model Space
    /// (STEPOUT làm việc trên solid, không chắc nhận block reference) → chọn đúng các solid đó → STEPOUT → xoá bản sao.
    /// ⚠️ Chuỗi prompt của STEPOUT CHƯA kiểm trong AutoCAD Mechanical — xem SESSION_LOG.
    /// </summary>
    public class StepExport
    {
        private const string LOG = "[StepExport]";

        /// <summary>Báo tab Result kết quả (thông điệp, lỗi?).</summary>
        public static event System.Action<string, bool> Finished;

        private static List<BuildResult> _builds = new List<BuildResult>();
        private static string _dir;

        /// <summary>Có lệnh STEPOUT không (chỉ khi chạy profile AutoCAD Mechanical).</summary>
        public static bool IsAvailable()
        {
            // CommandTypeFlags 0 = tên lệnh chưa được đăng ký.
            try { return (int)Autodesk.AutoCAD.Internal.Utils.IsCommandNameInUse("STEPOUT") != 0; }
            catch { return false; }
        }

        /// <summary>Gọi từ Palette: ghi yêu cầu rồi chạy lệnh nội bộ (cần command context cho ed.Command).</summary>
        public static void Request(IEnumerable<BuildResult> builds, string dir)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            _builds = builds.Where(b => b.Kept && !b.BlockRefId.IsNull && b.BlockRefId.Database == doc.Database).ToList();
            _dir = dir;
            doc.SendStringToExecute("MCG_ExportStep ", true, false, false);
        }

        [CommandMethod("MCG_ExportStep", CommandFlags.NoHistory)]
        public void ExportCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;
            var builds = _builds; _builds = new List<BuildResult>();
            if (builds.Count == 0 || string.IsNullOrEmpty(_dir)) return;

            object fileDia = Application.GetSystemVariable("FILEDIA");
            var done = new List<string>();
            var skipped = new List<string>();
            try
            {
                Application.SetSystemVariable("FILEDIA", 0);
                foreach (var b in builds)
                {
                    string name = PanelName(b);
                    var temp = ExplodeCopy(db, b);
                    if (temp.Count == 0) { skipped.Add(name + " (no solids)"); continue; }
                    string path = Path.Combine(_dir, Sanitize(name) + ".stp");
                    try
                    {
                        if (File.Exists(path)) File.Delete(path);   // tránh prompt ghi đè của STEPOUT
                        var ss = SelectionSet.FromObjectIds(temp.ToArray());
                        ed.Command("_.STEPOUT", ss, "", path);
                        if (File.Exists(path)) done.Add(name); else skipped.Add(name + " (STEPOUT wrote no file)");
                    }
                    catch (System.Exception ex)
                    {
                        skipped.Add($"{name} ({ex.Message})");
                        ed.WriteMessage($"\n{LOG} {name}: {ex.Message}\n");
                    }
                    finally { EraseAll(db, temp); }
                }
            }
            finally { Application.SetSystemVariable("FILEDIA", fileDia); }

            string msg = $"Exported {done.Count} STEP file(s) to {_dir}";
            if (skipped.Count > 0) msg += "  ·  skipped: " + string.Join(", ", skipped);
            Finished?.Invoke(msg, done.Count == 0);
        }

        /// <summary>Explode 1 bản sao block 3D (không đụng block gốc) ra Model Space; trả id các solid tạm.</summary>
        private static List<ObjectId> ExplodeCopy(Database db, BuildResult b)
        {
            var ids = new List<ObjectId>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                if (b.BlockRefId.IsErased) { tr.Commit(); return ids; }
                var br = (BlockReference)tr.GetObject(b.BlockRefId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var parts = new DBObjectCollection();
                br.Explode(parts);
                foreach (DBObject o in parts)
                {
                    if (o is Solid3d s) { ids.Add(ms.AppendEntity(s)); tr.AddNewlyCreatedDBObject(s, true); }
                    else o.Dispose();
                }
                tr.Commit();
            }
            return ids;
        }

        private static void EraseAll(Database db, List<ObjectId> ids)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in ids)
                    try { tr.GetObject(id, OpenMode.ForWrite).Erase(); } catch { }
                tr.Commit();
            }
        }

        private static string PanelName(BuildResult b)
        {
            string n = b.BlockName ?? b.PanelBlockName ?? "panel";
            return n.StartsWith("MCG3D_") ? n.Substring(6) : n;
        }

        private static string Sanitize(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    }
}
