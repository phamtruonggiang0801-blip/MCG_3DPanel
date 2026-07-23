using System.Diagnostics;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using MCG_3DPanel.Models;
using MCG_3DPanel.Services;
using MCG_3DPanel.UI;

[assembly: ExtensionApplication(typeof(MCG_3DPanel.Commands.PanelCommands))]
[assembly: CommandClass(typeof(MCG_3DPanel.Commands.PanelCommands))]

namespace MCG_3DPanel.Commands
{
    /// <summary>
    /// Vòng đời plugin + ĐIỀU PHỐI 3 thao tác của Palette (Inspect / Classify / Build).
    /// Chỉ 1 lệnh cho người dùng: <c>MCG_Build3DPanel</c> (mở Palette).
    /// Việc phụ tách riêng: xuất file → <see cref="ReportOutput"/>; dump bản vẽ → <see cref="DrawingDumpCommand"/>.
    /// Xem skill module-per-object.
    /// </summary>
    public class PanelCommands : IExtensionApplication
    {
        private const string LOG_PREFIX = "[MCG_3DPanel]";

        public void Initialize()
        {
            var ed = Application.DocumentManager?.MdiActiveDocument?.Editor;
            ed?.WriteMessage($"\n{LOG_PREFIX} Đã nạp. Gõ MCG_Build3DPanel để mở bảng điều khiển.\n");
            Debug.WriteLine($"{LOG_PREFIX} ===== Plugin kích hoạt THÀNH CÔNG =====");
        }

        public void Terminate() => Debug.WriteLine($"{LOG_PREFIX} Plugin đóng.");

        /// <summary>Mở WPF Palette chính.</summary>
        [CommandMethod("MCG_Build3DPanel")]
        public void ShowPalette() => PaletteManager.Instance.Show();

        // ─── Thao tác từ Palette ──────────────────────────────────────────

        /// <summary>Pick Block Panel → xuất JSON dữ liệu thô. Trả về đường dẫn (null nếu huỷ).</summary>
        public static string RunInspect()
        {
            if (!TryPickPanel(out Document doc, out ObjectId id)) return null;
            Editor ed = doc.Editor;

            using (doc.LockDocument())   // đọc DB từ context modeless => cần lock
            {
                IInspectorService inspector = new InspectorService();
                string outPath = ReportOutput.BuildOutputPath("Inspect");
                inspector.InspectToJson(id, outPath);

                ed.WriteMessage($"\n{LOG_PREFIX} Đã xuất dữ liệu ra:\n  {outPath}\n");
                ReportOutput.OpenFile(outPath);
                return outPath;
            }
        }

        /// <summary>Pick Block Panel → phân loại → xuất report JSON. Trả về kết quả (null nếu huỷ).</summary>
        public static ClassificationResult RunClassify(PanelPosition position)
        {
            if (!TryPickPanel(out Document doc, out ObjectId id)) return null;
            Editor ed = doc.Editor;

            using (doc.LockDocument())
            {
                IClassificationService classifier = new ClassificationService();
                ClassificationResult result = classifier.Classify(id, position);

                string outPath = ReportOutput.BuildOutputPath("Classify");
                ReportOutput.WriteClassifyReport(result, outPath);

                ed.WriteMessage($"\n{LOG_PREFIX} Phân loại: {result.Summary()}\n  Report: {outPath}\n");
                ReportOutput.OpenFile(outPath);
                return result;
            }
        }

        /// <summary>Pick Block Panel → classify → dựng 3D → hỏi Keep/Discard. Trả về BuildResult (null nếu huỷ).</summary>
        public static BuildResult RunBuild(PanelInput input, PanelPosition position)
        {
            if (!TryPickPanel(out Document doc, out ObjectId id)) return null;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            using (doc.LockDocument())
            {
                ClassificationResult cls = new ClassificationService().Classify(id, position);
                BuildResult build = new Build3DService().Build(db, cls, input, position);

                ed.WriteMessage($"\n{LOG_PREFIX} Đã dựng {build.SolidCount} solid ({cls.Summary()}). Block: {build.BlockName}\n");
                ed.Regen();

                // Report chẩn đoán: TẮT. Bật khi cần debug:
                // ReportOutput.WriteBuildReport(build, ed);

                if (build.SolidCount == 0)
                {
                    ed.WriteMessage($"\n{LOG_PREFIX} Không dựng được solid nào — bỏ.\n");
                    EraseBuild(db, build);
                    return build;
                }

                bool keep = AskKeep(ed);
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    if (keep)
                    {
                        ((Entity)tr.GetObject(id, OpenMode.ForWrite)).Erase();   // xoá block 2D
                        build.Kept = true;
                    }
                    else
                    {
                        ((Entity)tr.GetObject(build.BlockRefId, OpenMode.ForWrite)).Erase();
                        try { ((BlockTableRecord)tr.GetObject(build.BtrId, OpenMode.ForWrite)).Erase(); }
                        catch { /* block còn tham chiếu nơi khác -> để purge sau */ }
                    }
                    tr.Commit();
                }
                ed.Regen();
                ed.WriteMessage(keep ? "\nĐã giữ 3D, xoá block 2D.\n" : "\nĐã bỏ mô hình 3D.\n");
                return build;
            }
        }

        // ─── Tiện ích tương tác ───────────────────────────────────────────

        private static bool AskKeep(Editor ed)
        {
            var pko = new PromptKeywordOptions("\nGiữ mô hình 3D và xoá block 2D?");
            pko.Keywords.Add("Yes");
            pko.Keywords.Add("No");
            pko.Keywords.Default = "Yes";
            pko.AllowArbitraryInput = false;
            PromptResult pr = ed.GetKeywords(pko);
            return pr.Status == PromptStatus.OK && pr.StringResult == "Yes";
        }

        private static void EraseBuild(Database db, BuildResult build)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    ((Entity)tr.GetObject(build.BlockRefId, OpenMode.ForWrite)).Erase();
                    ((BlockTableRecord)tr.GetObject(build.BtrId, OpenMode.ForWrite)).Erase();
                }
                catch { }
                tr.Commit();
            }
        }

        /// <summary>Pick 1 Block Reference. GetEntity gọi NGOÀI LockDocument (interactive).</summary>
        private static bool TryPickPanel(out Document doc, out ObjectId id)
        {
            id = ObjectId.Null;
            doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return false;
            Editor ed = doc.Editor;

            var peo = new PromptEntityOptions("\nChọn Block Panel:");
            peo.SetRejectMessage("\nĐối tượng phải là 1 Block Reference (INSERT).");
            peo.AddAllowedClass(typeof(BlockReference), exactMatch: false);

            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\nĐã huỷ.\n");
                return false;
            }
            id = per.ObjectId;
            return true;
        }
    }
}
