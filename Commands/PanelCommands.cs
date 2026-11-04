using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using MCG_3DPanel.Models;
using MCG_3DPanel.Services;
using MCG_3DPanel.Services.Corrections;
using MCG_3DPanel.UI;

[assembly: ExtensionApplication(typeof(MCG_3DPanel.Commands.PanelCommands))]
[assembly: CommandClass(typeof(MCG_3DPanel.Commands.PanelCommands))]

namespace MCG_3DPanel.Commands
{
    /// <summary>
    /// Vòng đời plugin + ĐIỀU PHỐI thao tác Build của Palette.
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

        /// <summary>
        /// Quét chọn 1 hoặc NHIỀU Block Panel → mỗi panel: classify → dựng 3D (panel lỗi không chặn panel khác)
        /// → hỏi Keep/Discard 1 LẦN cho cả lô. Trả về danh sách BuildResult (null nếu huỷ chọn).
        /// Luôn ghi 1 report JSON cho cả lô (solid 3D, cùng handle với tab Result/CSV) nhưng KHÔNG tự mở.
        /// </summary>
        public static List<BuildResult> RunBuild(PanelInput input, PanelPosition position)
        {
            if (!TryPickPanels(out Document doc, out ObjectId[] ids)) return null;
            Editor ed = doc.Editor;
            Database db = doc.Database;
            var builds = new List<BuildResult>();
            var sources = new Dictionary<BuildResult, ObjectId>();   // build → block 2D nguồn (xoá khi Keep)

            using (doc.LockDocument())
            {
                for (int i = 0; i < ids.Length; i++)
                {
                    string tag = $"[{i + 1}/{ids.Length}]";
                    try
                    {
                        BuildResult build = BuildOne(db, ids[i], input, position, out ClassificationResult cls);
                        builds.Add(build);
                        sources[build] = ids[i];

                        ed.WriteMessage($"\n{LOG_PREFIX} {tag} {cls.PanelBlockName}: {build.SolidCount} solid ({cls.Summary()}). Block: {build.BlockName}\n");
                        if (build.SolidCount == 0)
                        {
                            ed.WriteMessage($"\n{LOG_PREFIX} {tag} Không dựng được solid nào — bỏ.\n");
                            EraseBuild(db, build);
                        }
                    }
                    catch (System.Exception ex)
                    {
                        ed.WriteMessage($"\n{LOG_PREFIX} {tag} Lỗi khi dựng panel {ids[i].Handle}: {ex.Message} — bỏ qua.\n");
                    }
                }
                ed.Regen();

                var withSolids = builds.Where(b => b.SolidCount > 0).ToList();
                if (withSolids.Count > 0)
                {
                    bool keep = AskKeep(ed, withSolids.Count);
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        foreach (var build in withSolids)
                        {
                            if (keep)
                            {
                                ((Entity)tr.GetObject(sources[build], OpenMode.ForWrite)).Erase();   // xoá block 2D
                                build.Kept = true;
                            }
                            else
                            {
                                ((Entity)tr.GetObject(build.BlockRefId, OpenMode.ForWrite)).Erase();
                                try { ((BlockTableRecord)tr.GetObject(build.BtrId, OpenMode.ForWrite)).Erase(); }
                                catch { /* block còn tham chiếu nơi khác -> để purge sau */ }
                            }
                        }
                        tr.Commit();
                    }
                    ed.Regen();
                    ed.WriteMessage(keep ? $"\nĐã giữ 3D ({withSolids.Count} panel), xoá block 2D.\n" : "\nĐã bỏ mô hình 3D.\n");
                }

                SaveBuildReport(builds, ed);
                return builds;
            }
        }

        /// <summary>Ghi 1 report Build cho cả lô ra Desktop (KHÔNG mở). Lỗi ghi file không làm hỏng Build.</summary>
        private static void SaveBuildReport(List<BuildResult> builds, Editor ed)
        {
            if (builds.Count == 0) return;
            try
            {
                string outPath = ReportOutput.BuildOutputPath("Build");
                ReportOutput.WriteBuildReport(builds, outPath);
                foreach (var b in builds) b.ReportPath = outPath;
                ed.WriteMessage($"\n{LOG_PREFIX} Build report (debug): {outPath}\n");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n{LOG_PREFIX} Không ghi được build report: {ex.Message}\n");
            }
        }

        // ─── Tiện ích tương tác ───────────────────────────────────────────

        private static bool AskKeep(Editor ed, int panelCount)
        {
            var pko = new PromptKeywordOptions(panelCount > 1
                ? $"\nGiữ mô hình 3D của {panelCount} panel và xoá block 2D?"
                : "\nGiữ mô hình 3D và xoá block 2D?");
            pko.Keywords.Add("Yes");
            pko.Keywords.Add("No");
            pko.Keywords.Default = "Yes";
            pko.AllowArbitraryInput = false;
            PromptResult pr = ed.GetKeywords(pko);
            return pr.Status == PromptStatus.OK && pr.StringResult == "Yes";
        }

        /// <summary>Phân loại (kèm correction tab Result của panel này) + dựng 3D cho 1 block 2D. Cần LockDocument.</summary>
        internal static BuildResult BuildOne(Database db, ObjectId src2D, PanelInput input, PanelPosition position,
            out ClassificationResult cls)
        {
            var sw = Stopwatch.StartNew();
            cls = new ClassificationService().Classify(src2D, position, CorrectionStore.For(CorrectionStore.PanelKey(src2D)));
            BuildResult build = new Build3DService().Build(db, cls, input, position);
            build.PanelBlockName = cls.PanelBlockName;
            build.SourceRefId = src2D;
            build.Input = input;
            build.Position = position;
            build.BuildSeconds = sw.Elapsed.TotalSeconds;
            return build;
        }

        // ─── Rebuild 1 panel từ tab Result (kỹ sư 2026-11-04: kết quả NGAY, không thao tác thêm) ────

        /// <summary>Báo tab Result: (bản cũ, bản mới — null nếu rebuild thất bại, thông báo).</summary>
        public static event System.Action<BuildResult, BuildResult, string> Rebuilt;

        private static BuildResult _pendingRebuild;

        /// <summary>
        /// Gọi từ Palette (modeless): đẩy yêu cầu rồi chạy lệnh nội bộ <c>MCG_Rebuild3DPanel</c> — chạy như 1 LỆNH
        /// nên cả lần rebuild là 1 bước Undo, và AutoCAD tự khoá document.
        /// </summary>
        public static void RequestRebuild(BuildResult current)
        {
            if (current == null) return;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null || current.SourceRefId.IsNull || current.SourceRefId.Database != doc.Database)
            {
                Rebuilt?.Invoke(current, null, "Switch to the drawing of this panel to rebuild it.");
                return;
            }
            _pendingRebuild = current;
            doc.SendStringToExecute("MCG_Rebuild3DPanel ", true, false, false);
        }

        [CommandMethod("MCG_Rebuild3DPanel", CommandFlags.NoHistory)]
        public void RebuildCommand()
        {
            BuildResult old = _pendingRebuild;
            _pendingRebuild = null;
            if (old == null) return;

            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            if (!SourceStillExists(db, old.SourceRefId))
            {
                Rebuilt?.Invoke(old, null, "Source 2D block no longer exists — cannot rebuild.");
                return;
            }

            BuildResult neu;
            try
            {
                neu = BuildOne(db, old.SourceRefId, old.Input, old.Position, out _);
            }
            catch (System.Exception ex)
            {
                Rebuilt?.Invoke(old, null, "Rebuild failed: " + ex.Message + " — previous 3D kept.");
                return;
            }
            if (neu.SolidCount == 0)
            {
                EraseBuild(db, neu);
                Rebuilt?.Invoke(old, null, "Rebuild produced no solids — previous 3D kept.");
                return;
            }

            // Thay bản cũ: xoá block 3D cũ, đặt tên block mới về tên cũ (cột Panel / tên block giữ nguyên).
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    if (!old.BlockRefId.IsErased) ((Entity)tr.GetObject(old.BlockRefId, OpenMode.ForWrite)).Erase();
                    if (!old.BtrId.IsErased) ((BlockTableRecord)tr.GetObject(old.BtrId, OpenMode.ForWrite)).Erase();
                    if (!string.IsNullOrEmpty(old.BlockName))
                    {
                        ((BlockTableRecord)tr.GetObject(neu.BtrId, OpenMode.ForWrite)).Name = old.BlockName;
                        neu.BlockName = old.BlockName;
                    }
                }
                catch (System.Exception ex)
                {
                    doc.Editor.WriteMessage($"\n{LOG_PREFIX} Rebuild: không thay được block cũ ({ex.Message}).\n");
                }
                tr.Commit();
            }
            neu.Kept = true;
            neu.ReportPath = old.ReportPath;
            doc.Editor.Regen();
            Rebuilt?.Invoke(old, neu, $"Rebuilt {neu.PanelBlockName} in {neu.BuildSeconds:0.0} s.");
        }

        /// <summary>Block reference 2D nguồn còn đọc được (kể cả đã xoá khi Keep) và định nghĩa block chưa bị PURGE.</summary>
        private static bool SourceStillExists(Database db, ObjectId src)
        {
            if (src.IsNull || src.Database != db) return false;
            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    var br = tr.GetObject(src, OpenMode.ForRead, true) as BlockReference;
                    bool ok = br != null && !br.BlockTableRecord.IsNull && !br.BlockTableRecord.IsErased;
                    tr.Commit();
                    return ok;
                }
            }
            catch { return false; }
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

        /// <summary>Quét chọn (click / window / crossing) 1 hoặc nhiều Block Reference — filter INSERT nên
        /// đối tượng khác tự bị bỏ. GetSelection gọi NGOÀI LockDocument (interactive).</summary>
        private static bool TryPickPanels(out Document doc, out ObjectId[] ids)
        {
            ids = new ObjectId[0];
            doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return false;
            Editor ed = doc.Editor;

            var pso = new PromptSelectionOptions { MessageForAdding = "\nChọn (các) Block Panel:" };
            // Bỏ luôn block 3D đã dựng (MCG3D_*) — quét window qua vùng đã build không bị dựng lại.
            var filter = new SelectionFilter(new[]
            {
                new TypedValue((int)DxfCode.Start, "INSERT"),
                new TypedValue((int)DxfCode.BlockName, "~MCG3D_*"),
            });

            PromptSelectionResult psr = ed.GetSelection(pso, filter);
            if (psr.Status != PromptStatus.OK || psr.Value == null || psr.Value.Count == 0)
            {
                ed.WriteMessage("\nĐã huỷ.\n");
                return false;
            }
            ids = psr.Value.GetObjectIds();
            ed.WriteMessage($"\n{LOG_PREFIX} Đã chọn {ids.Length} block panel.\n");
            return true;
        }
    }
}
