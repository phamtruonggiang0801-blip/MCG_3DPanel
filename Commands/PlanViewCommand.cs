using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using MCG_3DPanel.Services;
using MCG_3DPanel.Services.PlanView;
using MCG_3DPanel.UI;

[assembly: CommandClass(typeof(MCG_3DPanel.Commands.PlanViewCommand))]

namespace MCG_3DPanel.Commands
{
    /// <summary>Create Drawing ▸ Plan View (spec 2026-11-10): từ file 3D đang mở, ghi nhãn, ký hiệu, kích thước, mặt cắt X-X/Y-Y,
    /// mặt bằng lỗ lashing và các mục cố định vào bản vẽ 2D đã dựng ra panel; chuyển sang bản vẽ đó, KHÔNG lưu.</summary>
    public class PlanViewCommand
    {
        public static event Action<string, bool> Finished;
        private static Document _doc3D;

        /// <summary>Gọi từ palette: chạy lệnh trong ngữ cảnh lệnh của document 3D đang active.</summary>
        public static void Request()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            _doc3D = doc;
            doc.SendStringToExecute("MCG_PlanView ", true, false, false);
        }

        [CommandMethod("MCG_PlanView", CommandFlags.Session)]
        public void Run()
        {
            Document doc3D = _doc3D ?? Application.DocumentManager.MdiActiveDocument;
            _doc3D = null;
            if (doc3D == null) return;
            try { Finish(Create(doc3D), false); }
            catch (StopException stop) { Finish(stop.Message, true); }
            catch (System.Exception ex) { Finish("Plan View failed: " + ex.Message, true); }
        }

        /// <summary>Trả thông báo trạng thái; lỗi người dùng cần biết ⇒ ném <see cref="StopException"/>.</summary>
        private static string Create(Document doc3D)
        {
            var build = PanelSession.For(doc3D.Database, out _).FirstOrDefault(b => b.Kept && !b.BlockRefId.IsNull);
            if (build == null) throw new StopException("Open a 3D panel file first.");
            string file3D = Path.GetFileName(doc3D.Name);

            // 1. File 3D phải đã lưu (Source2DLocator có thể ghi đường dẫn mới vào nó).
            if (!PanelCommands.IsSavedOnDisk(doc3D))
            {
                if (!UnsavedFileDialog.AskSave(file3D)) throw new StopException($"Create Drawing cancelled: {file3D} has unsaved changes.");
                string err = PanelCommands.SaveOpenTarget(doc3D, file3D);
                if (err != null) throw new StopException(err);
            }

            // 2–4. Ảnh chụp từ file 3D, rồi tìm bản vẽ 2D.
            PlanViewInput input;
            using (doc3D.LockDocument()) input = PlanViewSnapshot.Read(doc3D.Database, build, out string snapErr) ?? throw new StopException(snapErr);
            string path = Source2DLocator.Find(doc3D, build, out string why) ?? throw new StopException(why);
            string refH = build.SourceRefId.Handle.ToString(), btrH = build.SourceBtrId.Handle.ToString();

            // 3. Mở / kích hoạt bản vẽ 2D. Từ đây KHÔNG dùng Editor của file 3D.
            Document doc2D = PanelCommands.FindOpen(path) ?? Application.DocumentManager.Open(path, false);
            if (doc2D.IsReadOnly) throw new StopException($"{Path.GetFileName(path)} is read-only or in use.");
            PanelCommands.Activate(doc2D);
            var db = doc2D.Database;

            // 1 khoá mang tên lệnh ⇒ Undo gỡ cả lần chạy trong 1 bước (review 2026-11-10).
            using (doc2D.LockDocument(DocumentLockMode.Write, "MCG_PlanView", "MCG_PlanView", false))
            {
                // 5–6. Tờ + mô hình + tài nguyên có sẵn.
                SheetContext sheet = PlanViewWriter.Sheet(db, refH, input, out var assyRef, out string sheetErr) ?? throw new StopException(sheetErr);
                PlanViewModel model = PlanViewBuilder.Build(input, sheet);
                var missing = PlanViewWriter.Missing(db, model);
                var blocks = PlanViewBlockSource.Prepare(db, model.RequiredBlocks);   // chỉ đọc
                missing.AddRange(blocks.Unavailable.Select(b => "block " + b));
                if (blocks.Unavailable.Count > 0 && !blocks.TemplateFound) missing.Add("(template not found: " + PlanViewBlockSource.TemplatePath + ")");
                if (missing.Count > 0)
                    throw new StopException($"Plan View stopped; the 2D drawing is missing: {string.Join(", ", missing)}. Nothing was drawn.");

                // 8. Phần vẽ tay — chỉ HỎI ở đây, chưa ghi gì (Cancel thì giữ nguyên tờ).
                var choice = HandDrawnChoice.Cancel;
                List<ObjectId> hand = null;
                if (!PlanViewWriter.HasKeepMarker(db, btrH))
                {
                    hand = PlanViewWriter.FindHandDrawn(db, input.Panel, assyRef, sheet, model, btrH);
                    if (hand.Count > 0)
                    {
                        choice = HandDrawnDialog.Ask(hand.Count, input.Panel);
                        if (choice == HandDrawnChoice.Cancel) throw new StopException("Create Drawing cancelled. Nothing was drawn.");
                    }
                }

                // 7, 9. Xoá phần vẽ tay (Replace) + phần tool vẽ lần trước, rồi vẽ lại — TẤT CẢ trong 1 transaction ngoài:
                // lỗi ở bất kỳ bước nào ⇒ huỷ hết, tờ giữ nguyên như trước khi chạy (review 2026-11-10).
                int labels, dims;
                List<string> addedTables;
                using (Transaction outer = db.TransactionManager.StartTransaction())
                {
                    addedTables = PlanViewBlockSource.Apply(db, blocks);
                    if (choice == HandDrawnChoice.Replace) PlanViewWriter.EraseAll(db, hand);
                    if (choice == HandDrawnChoice.KeepBoth) PlanViewWriter.SetKeepMarker(db, btrH, true);
                    PlanViewWriter.EraseOwn(db, btrH);
                    (labels, dims) = PlanViewWriter.Write(db, model, sheet, btrH);
                    outer.Commit();
                }
                Application.UpdateScreen();
                string added = "";
                if (blocks.FromTemplate.Count > 0) added += " Added from CAS.dwt: " + string.Join(", ", blocks.FromTemplate) + ".";
                if (blocks.Drawn.Count > 0) added += " Drew profile blocks: " + string.Join(", ", blocks.Drawn) + ".";
                if (addedTables.Count > 0) added += " New with the blocks: " + string.Join(", ", addedTables) + ".";
                return $"Plan View: {labels} labels, {dims} dimensions, sections X-X / Y-Y.{added} {Path.GetFileName(path)} is not saved.";
            }
        }

        private static void Finish(string msg, bool error)
        {
            if (msg == null) return;
            Finished?.Invoke(msg, error);
            try { Application.DocumentManager.MdiActiveDocument?.Editor?.WriteMessage("\n[MCG_3DPanel] " + msg + "\n"); } catch { }
        }

        /// <summary>Dừng có lý do người dùng cần đọc (không phải lỗi lập trình).</summary>
        private sealed class StopException : System.Exception
        {
            public StopException(string message) : base(message) { }
        }
    }
}
