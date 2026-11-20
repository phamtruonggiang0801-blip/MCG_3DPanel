using System;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using MCG_3DPanel.Commands.Detail;
using MCG_3DPanel.Services;
using MCG_3DPanel.Services.Update;

namespace MCG_3DPanel.Commands
{
    /// <summary>Điều khiển 3D từ bản vẽ 2D (spec 2026-11-20 3d-controls-in-2d; kỹ sư 2026-11-20 tách Update thành 2 việc):
    /// <list type="bullet">
    /// <item>Update Drawing — vẽ lại Plan View + Details từ file 3D hiện có, KHÔNG mở file 3D, áp tên đang chờ; sửa hình học vẫn chờ.</item>
    /// <item>Update 3D Model — dựng lại file 3D có áp mọi sửa chờ, không vẽ lại bản vẽ.</item>
    /// <item>Create Drawing ▸ Plan View — như Update Drawing nhưng chỉ Plan View (kể cả khi chưa có).</item>
    /// </list>
    /// Chạy THẲNG từ nút palette (ngữ cảnh ứng dụng, như Build 3D), không qua lệnh của bản 2D (lệnh trong bản 2D mở / đóng file 3D giữa
    /// chừng làm phần ghi vào bản 2D bị mất — 2026-11-20).</summary>
    public static class UpdateChain
    {
        /// <summary>(thông báo, lỗi?) khi việc kết thúc.</summary>
        public static event Action<string, bool> Finished;

        /// <summary>Đang chạy: mở / đóng / chuyển document ⇒ các tab KHÔNG tự nạp (như <see cref="PanelCommands.IsBuilding"/>); câu báo
        /// cuối bắn sau khi cờ tắt để tab nạp lại 1 lần.</summary>
        public static bool Running { get; private set; }
        private static (string Msg, bool Error)? _deferred;

        // Chờ Detail Update (lệnh Modal của bản 2D chạy sau, báo qua DetailCommands.Finished).
        private static bool _waitDetail;
        private static bool _planView;
        private static string _planErr;
        private static int _names, _waiting;

        public static void RequestPlanView(BuildResult remote) => Run(remote, "Plan View", () => PlanViewOnly(remote));
        public static void RequestDrawing(BuildResult remote) => Run(remote, "Update Drawing", () => Drawing(remote));
        public static void RequestModel(BuildResult remote) => Run(remote, "Update 3D Model", () => Model(remote));

        private static void Run(BuildResult remote, string what, Action body)
        {
            if (remote?.Remote == null || Application.DocumentManager.MdiActiveDocument == null) return;
            if (PanelCommands.IsBuilding || Running) { Finish("A build or update is running — try again when it finishes.", true); return; }
            DetailCommands.Finished -= OnDetailFinished;   // lần trước Detail không báo về ⇒ bỏ chờ
            _waitDetail = false;
            Running = true;
            _deferred = null;
            try { body(); }
            catch (System.Exception ex)
            {
                try { Application.DocumentManager.MdiActiveDocument?.Editor?.WriteMessage($"\n[MCG_3DPanel] {what} error detail:\n" + ex + "\n"); } catch { }
                Finish($"{what} failed: " + ex.Message + " (details on the command line)", true);
            }
            finally
            {
                Running = false;
                var d = _deferred;
                _deferred = null;
                if (d != null) Finish(d.Value.Msg, d.Value.Error);
            }
        }

        /// <summary>Bản vẽ lấy từ file 3D hiện có: block 2D lệch file 3D / file 3D đang mở ⇒ không vẽ (sai), nói lý do.</summary>
        private static string DrawingRefusal(RemoteLink r) =>
            !r.Locked ? null
            : r.LockReason == Remote3DSession.Changed2D ? Remote3DSession.Changed2D
            : r.LockReason;

        private static void PlanViewOnly(BuildResult req)
        {
            string refusal = DrawingRefusal(req.Remote);
            if (refusal != null) { Finish(refusal, true); return; }
            Document doc2D = DocOf(req.Remote.AssyRef2D.Database) ?? throw new InvalidOperationException("the 2D drawing is closed");
            string msg = PlanViewCommand.CreateFrom2D(req, doc2D, out bool err);
            Finish((err ? "Plan View failed: " : "") + msg + Waiting(req.Remote), err);
        }

        private static void Drawing(BuildResult req)
        {
            RemoteLink r = req.Remote;
            string refusal = DrawingRefusal(r);
            if (refusal != null) { Finish(refusal, true); return; }
            Document doc2D = DocOf(r.AssyRef2D.Database) ?? throw new InvalidOperationException("the 2D drawing is closed");
            var (names, geometry) = PendingEdits.Counts(Remote3DSession.ReadPending(doc2D.Database, r.AssyBtr2D));
            bool planView = PlanViewWriter.HasPlanView(doc2D.Database, r.AssyBtr2D.Handle.ToString());
            bool detail = DetailCommands.HasPanelWith3DFile(doc2D, out bool hasDetail) && hasDetail;

            string planErr = null;
            if (planView)
            {
                string msg = PlanViewCommand.CreateFrom2D(req, doc2D, out bool err);
                if (err) planErr = msg;
            }
            if (detail && planErr == null)
            {
                _waitDetail = true;
                _planView = planView; _planErr = planErr; _names = names; _waiting = geometry;
                DetailCommands.Finished += OnDetailFinished;
                DetailCommands.Request(DetailCommands.UpdateCommand);
                return;
            }
            Finish(UpdateChainRules.DrawingSummary(planView, planErr, detail, null, planView ? names : 0, geometry),
                   planErr != null || (!planView && !detail));
        }

        private static void Model(BuildResult req)
        {
            RemoteLink r = req.Remote;
            Document doc2D = DocOf(r.AssyRef2D.Database) ?? throw new InvalidOperationException("the 2D drawing is closed");
            string name2D = Path.GetFileName(doc2D.Name);
            if (r.Locked && r.LockReason != Remote3DSession.Changed2D) { Finish(r.LockReason, true); return; }   // file 3D đang mở

            // Update đọc block 2D từ file trên đĩa ⇒ bản 2D phải đã lưu (vừa ghi sửa chờ nên thường là chưa).
            if (!PanelCommands.IsSavedOnDisk(doc2D))
            {
                if (!MCG_3DPanel.UI.UnsavedFileDialog.AskSave(name2D)) { Finish($"Update cancelled: {name2D} has unsaved changes.", true); return; }
                string err = PanelCommands.SaveDrawing(doc2D);
                if (err != null) { Finish(err, true); return; }
            }

            string file3D = r.File3D;
            bool wasOpen = PanelCommands.FindOpen(file3D) != null;
            var run = new BuildRun();
            BuildResult build = PanelCommands.RunUpdate(doc2D, r.AssyRef2D, file3D, run, out Document doc3D);
            if (build == null)
            {
                // 3D không đổi ⇒ sửa chờ còn nguyên trong bản 2D.
                string why = string.Join(" · ", new[] { run.Stop }.Concat(run.Problems).Where(s => !string.IsNullOrEmpty(s)));
                Finish(why.Length > 0 ? why : "Update 3D Model failed — see the command line.", true);
                return;
            }

            if (!wasOpen && doc3D != null && DocOf(doc3D.Database) != null)
            {
                try
                {
                    if (Application.DocumentManager.MdiActiveDocument == doc3D) PanelCommands.Activate(doc2D);
                    doc3D.CloseAndDiscard();   // BuildAndSave đã lưu
                }
                catch (System.Exception ex) { run.Problems.Add($"Could not close {Path.GetFileName(file3D)}: {ex.Message}"); }
            }
            try { if (Application.DocumentManager.MdiActiveDocument != doc2D) PanelCommands.Activate(doc2D); }
            catch (System.Exception ex) { run.Problems.Add($"Could not switch back to {name2D}: {ex.Message}"); }

            bool drawings = PlanViewWriter.HasPlanView(doc2D.Database, r.AssyBtr2D.Handle.ToString())
                            || (DetailCommands.HasPanelWith3DFile(doc2D, out bool hasDetail) && hasDetail);
            var tail = run.Notes.Concat(run.Problems).ToList();   // đỏ chỉ khi có Problems
            string extra = tail.Count > 0 ? " " + string.Join(" · ", tail) : "";
            Finish(UpdateChainRules.Summary(false, null, false, null, run.PendingApplied)
                   + (drawings ? " Run Update Drawing to redraw the Plan View and details." : "") + extra,
                   run.Problems.Count > 0);
        }

        private static string Waiting(RemoteLink r)
        {
            Document doc2D = DocOf(r.AssyRef2D.Database);
            int geometry = doc2D == null ? 0 : PendingEdits.Counts(Remote3DSession.ReadPending(doc2D.Database, r.AssyBtr2D)).Geometry;
            return geometry > 0 ? $" {geometry} edit{(geometry > 1 ? "s" : "")} still wait{(geometry > 1 ? "" : "s")} for Update 3D Model." : "";
        }


        private static void OnDetailFinished(string msg, bool error)
        {
            DetailCommands.Finished -= OnDetailFinished;
            if (!_waitDetail) return;
            _waitDetail = false;
            Finish(UpdateChainRules.DrawingSummary(_planView, _planErr, true, error ? msg : null, _planView ? _names : 0, _waiting), error);
        }

        private static void Finish(string msg, bool error)
        {
            if (Running) { _deferred = (msg, error); return; }   // bắn sau khi cờ tắt (tab nạp lại được)
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
