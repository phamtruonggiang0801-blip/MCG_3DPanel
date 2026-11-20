using System;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using MCG_3DPanel.Commands.Detail;
using MCG_3DPanel.Services;
using MCG_3DPanel.Services.Update;

[assembly: CommandClass(typeof(MCG_3DPanel.Commands.UpdateChain))]

namespace MCG_3DPanel.Commands
{
    /// <summary>Nút Update trên bản vẽ 2D (spec 2026-11-20 3d-controls-in-2d): Update file 3D (áp sửa chờ) → vẽ lại Plan View nếu đã có →
    /// đóng file 3D nếu trước đó chưa mở → Update Details nếu đã có Detail → về bản 2D. Phần lỗi nêu tên trong câu báo cuối.</summary>
    public class UpdateChain
    {
        public const string Command = "MCG_UpdateFrom2D";

        /// <summary>(thông báo, lỗi?) khi chuỗi kết thúc.</summary>
        public static event Action<string, bool> Finished;

        private static BuildResult _request;
        private static bool _planViewRequested;   // Create Drawing ▸ Plan View trên bản 2D: luôn vẽ Plan View (kể cả chưa có)

        // Trạng thái chờ Detail Update (lệnh Modal chạy sau, báo qua DetailCommands.Finished).
        private static bool _waitDetail;
        private static bool _planView;
        private static string _planErr;
        private static int _applied;
        private static string _extra;

        /// <summary>Gọi từ palette (bản vẽ 2D đang active): chạy lệnh trong ngữ cảnh lệnh của document đó.</summary>
        public static void Request(BuildResult remote) => Request(remote, false);

        /// <summary>Create Drawing ▸ Plan View từ bản 2D: không có sửa chờ ⇒ chỉ mở file 3D vẽ Plan View rồi đóng; có ⇒ Update đầy đủ
        /// và luôn vẽ Plan View (Plan View không được thiếu sửa chờ).</summary>
        public static void RequestPlanView(BuildResult remote) => Request(remote, true);

        private static void Request(BuildResult remote, bool planView)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null || remote?.Remote == null) return;
            if (PanelCommands.IsBuilding) { Finish("A build is running — try again when it finishes.", true); return; }
            _request = remote;
            _planViewRequested = planView;
            doc.SendStringToExecute(Command + " ", true, false, false);
        }

        [CommandMethod(Command, CommandFlags.Session)]
        public void Run()
        {
            BuildResult req = _request;
            bool planOnly = _planViewRequested;
            _request = null;
            _planViewRequested = false;
            if (req?.Remote == null) return;
            try
            {
                if (planOnly && req.Remote.PendingKeys.Count == 0) PlanViewOnly(req);
                else Start(req, planOnly);
            }
            catch (System.Exception ex)
            {
                try { Application.DocumentManager.MdiActiveDocument?.Editor?.WriteMessage("\n[MCG_3DPanel] Update error detail:\n" + ex + "\n"); } catch { }
                Finish("Update failed: " + ex.Message + " (details on the command line)", true);
            }
        }

        /// <summary>Plan View từ bản 2D khi không có sửa chờ: mở file 3D (nếu chưa mở), vẽ Plan View (tự về bản 2D), đóng file 3D nếu
        /// trước đó chưa mở.</summary>
        private static void PlanViewOnly(BuildResult req)
        {
            RemoteLink r = req.Remote;
            if (r.Locked) { Finish(r.LockReason, true); return; }   // block lệch file 3D: Plan View sẽ sai ⇒ Update trước
            Document doc2D = DocOf(r.AssyRef2D.Database) ?? throw new InvalidOperationException("the 2D drawing is closed");
            string file3D = r.File3D;
            Document doc3D = PanelCommands.FindOpen(file3D);
            bool wasOpen = doc3D != null;
            if (doc3D == null) doc3D = Application.DocumentManager.Open(file3D, false);
            PanelCommands.Activate(doc3D);
            string msg = PlanViewCommand.CreateFor(doc3D, out bool err);
            string extra = "";
            if (!wasOpen && DocOf(doc3D.Database) != null)
            {
                try
                {
                    if (Application.DocumentManager.MdiActiveDocument == doc3D) PanelCommands.Activate(doc2D);
                    if (!PanelCommands.IsSavedOnDisk(doc3D)) PanelCommands.SaveOpenTarget(doc3D, Path.GetFileName(file3D));
                    doc3D.CloseAndDiscard();
                }
                catch (System.Exception ex) { extra = $" Could not close {Path.GetFileName(file3D)}: {ex.Message}"; }
            }
            try { if (Application.DocumentManager.MdiActiveDocument != doc2D) PanelCommands.Activate(doc2D); }
            catch (System.Exception ex) { extra += $" Could not switch back to {Path.GetFileName(doc2D.Name)}: {ex.Message}"; }
            Finish((err ? "Plan View failed: " : "") + msg + extra, err || extra.Length > 0);
        }

        private static void Start(BuildResult req, bool forcePlanView = false)
        {
            // Lần trước Detail Update không báo về ⇒ bỏ chờ, không để câu báo cũ hiện ở lệnh Detail sau (review minor).
            DetailCommands.Finished -= OnDetailFinished;
            _waitDetail = false;
            RemoteLink r = req.Remote;
            Document doc2D = DocOf(r.AssyRef2D.Database) ?? throw new InvalidOperationException("the 2D drawing is closed");
            string name2D = Path.GetFileName(doc2D.Name);
            if (r.Locked && r.LockReason != Remote3DSession.Changed2D) { Finish(r.LockReason, true); return; }   // file 3D đang mở

            // Update đọc block 2D từ file trên đĩa ⇒ bản 2D phải đã lưu (vừa ghi sửa chờ nên thường là chưa).
            if (!PanelCommands.IsSavedOnDisk(doc2D))
            {
                if (!MCG_3DPanel.UI.UnsavedFileDialog.AskSave(name2D)) { Finish($"Update cancelled: {name2D} has unsaved changes.", true); return; }
                string err = Save2D(doc2D);
                if (err != null) { Finish(err, true); return; }
            }

            string file3D = r.File3D;
            bool wasOpen = PanelCommands.FindOpen(file3D) != null;
            string btrH = r.AssyBtr2D.Handle.ToString();
            bool planView = forcePlanView || PlanViewWriter.HasPlanView(doc2D.Database, btrH);
            bool detail = DetailCommands.HasPanelWith3DFile(doc2D, out bool hasDetail) && hasDetail;

            var run = new BuildRun();
            BuildResult build = PanelCommands.RunUpdate(doc2D, r.AssyRef2D, file3D, run, out Document doc3D);
            if (build == null)
            {
                // 3D không đổi ⇒ sửa chờ còn nguyên trong bản 2D.
                Finish(string.Join(" · ", new[] { run.Stop }.Concat(run.Problems).Where(s => !string.IsNullOrEmpty(s)))
                       is string s && s.Length > 0 ? s : "Update failed — see the command line.", true);
                return;
            }

            string planErr = null;
            if (planView)
            {
                string msg = PlanViewCommand.CreateFor(doc3D, out bool err);
                if (err) planErr = msg;
            }

            if (!wasOpen && DocOf(doc3D.Database) != null)
            {
                try
                {
                    if (Application.DocumentManager.MdiActiveDocument == doc3D) PanelCommands.Activate(doc2D);
                    if (!PanelCommands.IsSavedOnDisk(doc3D)) PanelCommands.SaveOpenTarget(doc3D, Path.GetFileName(file3D));
                    doc3D.CloseAndDiscard();
                }
                catch (System.Exception ex) { run.Problems.Add($"Could not close {Path.GetFileName(file3D)}: {ex.Message}"); }
            }
            try { if (Application.DocumentManager.MdiActiveDocument != doc2D) PanelCommands.Activate(doc2D); }
            catch (System.Exception ex) { run.Problems.Add($"Could not switch back to {name2D}: {ex.Message}"); }

            string extra = run.Problems.Count > 0 ? " " + string.Join(" · ", run.Problems) : "";
            if (detail && planErr == null)
            {
                // Detail Update là lệnh Modal của bản 2D: chạy sau lệnh này, báo kết quả qua DetailCommands.Finished.
                _waitDetail = true;
                _planView = planView; _planErr = planErr; _applied = run.PendingApplied; _extra = extra;
                DetailCommands.Finished -= OnDetailFinished;
                DetailCommands.Finished += OnDetailFinished;
                DetailCommands.Request(DetailCommands.UpdateCommand);
                return;
            }
            Finish(UpdateChainRules.Summary(planView, planErr, detail, null, run.PendingApplied) + extra, planErr != null || run.Problems.Count > 0);
        }

        /// <summary>Lưu bản vẽ 2D chính thức như QSAVE: giữ phiên bản DWG gốc, .bak thường (review 2026-11-20 — không dùng đường lưu file 3D).
        /// Trả lỗi hoặc null.</summary>
        private static string Save2D(Document doc)
        {
            try
            {
                using (doc.LockDocument())
                {
                    Database db = doc.Database;
                    db.SaveAs(doc.Name, true, db.OriginalFileVersion, db.SecurityParameters);
                    try { Autodesk.AutoCAD.Internal.Utils.SetDbmod(0, db); }
                    catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine("[UpdateChain] SetDbmod: " + ex.Message); }
                }
                return null;
            }
            catch (System.Exception ex) { return $"Could not save {Path.GetFileName(doc.Name)}: {ex.Message}. Nothing was updated."; }
        }

        private static void OnDetailFinished(string msg, bool error)
        {
            DetailCommands.Finished -= OnDetailFinished;
            if (!_waitDetail) return;
            _waitDetail = false;
            Finish(UpdateChainRules.Summary(_planView, _planErr, true, error ? msg : null, _applied) + _extra, error || _extra.Length > 0);
        }

        private static void Finish(string msg, bool error)
        {
            try { Finished?.Invoke(msg, error); }
            catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine("[UpdateChain] Finished: " + ex.Message); }
            try { Application.DocumentManager.MdiActiveDocument?.Editor?.WriteMessage("\n[MCG_3DPanel] " + msg + "\n"); } catch { }
        }

        private static Document DocOf(Database db)
        {
            foreach (Document d in Application.DocumentManager) if (d.Database == db) return d;
            return null;
        }
    }
}
