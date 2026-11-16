using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using MCG_3DPanel.Models;
using MCG_3DPanel.Services;
using MCG_3DPanel.Services.Classify;
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
        private const int DBMOD_VIEW_ONLY = 8 | 16;

        public void Initialize()
        {
            var ed = Application.DocumentManager?.MdiActiveDocument?.Editor;
            ed?.WriteMessage($"\n{LOG_PREFIX} Loaded. Type MCG_Build3DPanel to open the palette.\n");
            Debug.WriteLine($"{LOG_PREFIX} ===== Plugin kích hoạt THÀNH CÔNG =====");
        }

        public void Terminate() => Debug.WriteLine($"{LOG_PREFIX} Plugin đóng.");

        /// <summary>Mở WPF Palette chính.</summary>
        [CommandMethod("MCG_Build3DPanel")]
        public void ShowPalette() => PaletteManager.Instance.Show();

        // ─── Thao tác từ Palette ──────────────────────────────────────────

        /// <summary>Mỗi file 3D dựng + lưu xong: (Database của file đó, build của file đó).</summary>
        public static event System.Action<Database, List<BuildResult>> FileBuilt;

        /// <summary>Đang chạy <see cref="RunBuild"/>: document đổi liên tục, file đang dựng dở chưa có dữ liệu panel ⇒ các tab không tự nạp.</summary>
        public static bool IsBuilding { get; private set; }

        /// <summary>RunBuild kết thúc (kể cả huỷ hoặc lỗi), <see cref="IsBuilding"/> đã tắt: các tab nạp lại 1 lần theo document đang active.</summary>
        public static event System.Action BuildFinished;

        /// <summary>Xem <see cref="RunBuildCore"/>. Bọc cờ <see cref="IsBuilding"/>.</summary>
        public static List<BuildResult> RunBuild(PanelInput input, PanelPosition position, out BuildRun run)
        {
            run = new BuildRun();
            IsBuilding = true;
            try { return RunBuildCore(input, position, run); }
            finally
            {
                IsBuilding = false;
                try { BuildFinished?.Invoke(); }
                catch (System.Exception ex) { Say($"\n{LOG_PREFIX} The tabs could not be updated: {ex.Message}\n"); }
            }
        }

        /// <summary>
        /// Quét chọn 1 hoặc NHIỀU Block Panel → mỗi panel 1 file 3D riêng (<see cref="Panel3DFile"/>), mở và dựng trong file đó
        /// (panel lỗi không chặn panel khác). File đích đã có ⇒ hỏi 1 LẦN cho cả lô. Bản vẽ nguồn KHÔNG bị ghi.
        /// Trả build của file mở cuối (rỗng nếu không dựng được gì), null nếu dừng trước khi tạo file
        /// (<see cref="BuildRun.Stop"/> nói lý do; null = người dùng huỷ).
        /// Từ lúc đổi document: KHÔNG dùng lại Editor của document cũ (AutoCAD crash) — mọi thông báo qua <see cref="Say"/>.
        /// </summary>
        private static List<BuildResult> RunBuildCore(PanelInput input, PanelPosition position, BuildRun run)
        {
            Document active = Application.DocumentManager.MdiActiveDocument;
            if (active == null) return null;
            // File 3D không còn panel đọc được: reference 2D nằm trên layer đóng băng, prompt không chọn được ⇒ dừng trước khi hỏi.
            if (Panel3DFile.HoldsSourceReference(active.Database))
            {
                run.Stop = "This is a 3D panel file with no panel to rebuild. Open the source 2D drawing to Build 3D.";
                return null;
            }
            if (!IsSavedOnDisk(active)) { run.Stop = "Save this drawing first, then Build 3D."; return null; }
            if (!TryPickPanels(out Document src, out ObjectId[] ids)) return null;

            Database source = src.Database;
            if (ids.Any(id => !Panel3DFile.IsInModelSpace(source, id))) { run.Stop = Panel3DFile.ModelSpaceOnly; return null; }
            string sourceFile = src.Name;
            string lastGood = null;   // ĐƯỜNG DẪN file tốt cuối cùng (mở hoặc dựng) — cuối lần chạy phải là file active. Không giữ Document: có thể bị đóng trong lúc chạy
            List<PanelJob> jobs = PlanJobs(source, sourceFile, ids);

            var existing = new HashSet<string>(jobs.Select(j => j.Target).Where(IsTaken), System.StringComparer.OrdinalIgnoreCase);
            if (existing.Count > 0)
            {
                ExistingChoice choice = ExistingFileDialog.Ask(existing.Select(Path.GetFileName));
                if (choice == ExistingChoice.Cancel) return null;
                if (choice == ExistingChoice.Open)
                {
                    // Mở trước, dựng sau: file active cuối cùng là file vừa dựng, khớp dòng trên các tab.
                    foreach (string path in existing) lastGood = OpenExisting(path, run)?.Name ?? lastGood;
                    jobs.RemoveAll(j => existing.Contains(j.Target));
                }
                else if (choice == ExistingChoice.Update)
                {
                    foreach (PanelJob job in jobs) job.Update = existing.Contains(job.Target);
                }
            }

            // NextFree giữ chỗ trong cùng lô: New copy, và 2 bản chèn của cùng 1 block ra 2 file khác nhau. Job Update giữ nguyên tên file.
            var reserved = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (PanelJob job in jobs)
            {
                if (!job.Update) job.Target = Panel3DPath.NextFree(job.Target, p => IsTaken(p) || reserved.Contains(p));
                reserved.Add(job.Target);
            }

            var last = new List<BuildResult>();
            for (int i = 0; i < jobs.Count; i++)
            {
                BuildResult build = BuildInNewFile(sourceFile, jobs[i],$"[{i + 1}/{jobs.Count}]", input, position, run, out Document fileDoc);
                if (build == null) continue;

                lastGood = fileDoc.Name;
                run.Files++;
                run.Solids += build.SolidCount;
                last = new List<BuildResult> { build };
                SaveBuildReport(last);
                try { FileBuilt?.Invoke(fileDoc.Database, last); }
                catch (System.Exception ex) { Say($"\n{LOG_PREFIX} {Path.GetFileName(jobs[i].Target)} was built, but the tabs could not be updated: {ex.Message}\n"); }
            }
            Reactivate(lastGood, run);
            return last;
        }

        /// <summary>Tạo file, mở, dựng, lưu 1 panel. Trả null nếu file không được giữ (không solid / lỗi): file vừa tạo bị bỏ, đã báo người dùng.</summary>
        private static BuildResult BuildInNewFile(string sourceFile, PanelJob job,string tag, PanelInput input, PanelPosition position,
            BuildRun run, out Document fileDoc)
        {
            fileDoc = null;
            if (job.Update) return UpdateFile(sourceFile, job, tag, run, out fileDoc);

            string file = Path.GetFileName(job.Target);
            Document doc = null;
            bool created = false;   // CHỈ bỏ file do chính lần này tạo — không bao giờ xoá file có sẵn của người dùng
            bool saving = false;    // đã dời .bak cũ ⇒ .bak đứng ở tên đó từ đây là do lần lưu này sinh ra
            string keptBak = null;  // tên mới của .bak cũ, để báo nếu lần lưu sau đó lỗi
            try
            {
                Panel3DFile.Create(sourceFile, job.RefId, job.Target);
                created = true;
                doc = Application.DocumentManager.Open(job.Target, false);
                BuildResult build = BuildAndSave(doc, job, tag, input, position, sourceFile, ref saving, ref keptBak);
                if (build.Solids.Count > 0)
                {
                    Panel3DFile.DeleteOwnBak(job.Target, saving);
                    fileDoc = doc;
                    return build;
                }
                run.Empty++;
                Say($"\n{LOG_PREFIX} {tag} {job.Name}: no solid could be built — {file} was not kept.\n");
            }
            catch (System.Exception ex)
            {
                run.Failed++;
                Say($"\n{LOG_PREFIX} {tag} Error building panel {job.Name}: {ex.Message} — skipped.\n");
                if (keptBak != null) run.Problems.Add($"The existing .bak of {file} is now {keptBak}.");
            }

            if (created)   // ngoài using: document đang khoá thì không đóng được
            {
                string error = Panel3DFile.Discard(doc, job.Target, saving);
                if (error != null)
                {
                    run.Problems.Add(error);
                    Say($"\n{LOG_PREFIX} {error}\n");
                }
            }
            return null;
        }

        /// <summary>Đưa file đang mở lên active, dựng panel, và lưu nếu có solid. Build không có solid ⇒ chưa lưu gì, người gọi quyết định.
        /// <paramref name="saving"/> = đã dời .bak cũ, .bak đứng ở tên đó từ đây là do lần lưu này sinh ra.
        /// <paramref name="keptBak"/> = tên mới của .bak cũ (null nếu không có), còn đúng cả khi lần lưu sau đó ném.</summary>
        private static BuildResult BuildAndSave(Document doc, PanelJob job, string tag, PanelInput input, PanelPosition position, string sourceFile, ref bool saving, ref string keptBak)
        {
            string file = Path.GetFileName(job.Target);
            Activate(doc);

            Database db = doc.Database;
            if (!db.TryGetObjectId(job.RefId.Handle, out ObjectId src2D))
                throw new System.InvalidOperationException("the panel is missing from the new file");

            BuildResult build;
            using (doc.LockDocument())
            {
                build = BuildOne(db, src2D, input, position, sourceFile, out ClassificationResult cls);
                Say($"\n{LOG_PREFIX} {tag} {cls.PanelBlockName}: {build.SolidCount} solid ({cls.Summary()}). Block: {build.BlockName}\n");
                // "Không dựng được" = không có solid nào ĐƯỢC DỰNG (kể cả solid đã Delete theo correction —
                // panel xoá hết solid vẫn là kết quả hợp lệ, còn Restore được).
                if (build.Solids.Count > 0)
                {
                    build.Kept = true;
                    RegenActive();
                    keptBak = Panel3DFile.MoveOldBakAside(job.Target);
                    saving = true;
                    if (keptBak != null) Say($"\n{LOG_PREFIX} The existing .bak of {file} was kept as {keptBak}.\n");
                    if (!Panel3DFile.Save(doc)) Say($"\n{LOG_PREFIX} {file} is saved, but AutoCAD may still ask to save it on close.\n");
                }
            }
            return build;
        }

        // ─── Update: lấy 2D đã lưu, giữ chỉnh sửa của file 3D cũ (spec 2026-11-09) ─────────────
        // LUẬT TRÊN HẾT: ở mọi điểm lỗi, người dùng vẫn còn .dwg cũ ở đúng tên hoặc ở .bak.

        /// <summary>Thay file 3D cũ bằng bản dựng từ 2D đã lưu, mang theo correction + tên, dùng Input/Position đã lưu trong file cũ.
        /// Trả null nếu panel dừng hoặc lỗi (đã đếm Failed và báo). Thành công ⇒ giữ bản sao lưu có giờ vĩnh viễn.</summary>
        private static BuildResult UpdateFile(string sourceFile, PanelJob job, string tag, BuildRun run, out Document fileDoc)
        {
            fileDoc = null;
            string file = Path.GetFileName(job.Target);
            string newFile = $"{job.Target}.new{System.Guid.NewGuid():N}.dwg";
            string before = Application.DocumentManager.MdiActiveDocument?.Name;   // panel dừng ⇒ quay lại drawing này

            Panel3DFile.OldPanelData carry = null;
            bool closed = false;   // file đích đang mở đã được đóng: file cũ còn/được trả về đúng tên ⇒ mở lại cho người dùng
            string stop;
            try { stop = PrepareUpdate(sourceFile, job, newFile, out carry, out closed); }
            catch (System.Exception ex) { stop = $"Could not update {file}: {ex.Message}. The old file was not changed."; }
            if (stop != null)
            {
                Panel3DFile.DeleteQuietly(newFile);   // chỉ file tạm của lần này; có thể chưa được tạo
                StopPanel(run, tag, stop);
                LeaveStoppedJob(job.Target, false, before);
                return null;
            }

            // File mới đã đủ trên đĩa: từ đây mới đụng file cũ.
            string backup;
            try
            {
                backup = Panel3DPath.Backup(job.Target, System.DateTime.Now, File.Exists);
                File.Move(job.Target, backup, false);
            }
            catch (System.Exception ex)
            {
                Panel3DFile.DeleteQuietly(newFile);
                StopPanel(run, tag, $"Could not replace {file}: {ex.Message}. The old file was not changed.");
                LeaveStoppedJob(job.Target, closed, before);
                return null;
            }

            try { File.Move(newFile, job.Target, false); }
            catch (System.Exception ex)
            {
                string notBack = MoveBackup(backup, job.Target);
                if (notBack == null) Panel3DFile.DeleteQuietly(newFile);   // chưa trả được file cũ thì không xoá gì thêm
                StopPanel(run, tag, notBack ?? $"Could not replace {file}: {ex.Message}. The old file was not changed.");
                LeaveStoppedJob(job.Target, closed && notBack == null, before);
                return null;
            }

            Document doc = null;
            bool saving = false;
            string keptBak = null;
            BuildResult build = null;
            string why = "no solid could be built";
            try
            {
                doc = Application.DocumentManager.Open(job.Target, false);
                build = BuildAndSave(doc, job, tag, carry.Meta.Input, (PanelPosition)carry.Meta.Position, sourceFile, ref saving, ref keptBak);
            }
            catch (System.Exception ex)
            {
                build = null;
                why = ex.Message;
            }

            if (build != null && build.Solids.Count > 0)
            {
                Panel3DFile.DeleteOwnBak(job.Target, saving);
                Say($"\n{LOG_PREFIX} The previous version of {file} was kept as {Path.GetFileName(backup)}.\n");
                AddUnmatchedNote(carry, build, run);
                fileDoc = doc;
                return build;
            }

            bool restored = UndoSwap(doc, job.Target, backup, sourceFile, saving, keptBak, why, tag, run);
            LeaveStoppedJob(job.Target, closed && restored, before);
            return null;
        }

        /// <summary>Panel dừng: mở lại file đích nếu nó đã bị đóng và file cũ đang ở đúng tên, rồi quay lại drawing trước job. Cố hết sức, không ném.</summary>
        private static void LeaveStoppedJob(string target, bool reopen, string before)
        {
            if (reopen)
            {
                try { Application.DocumentManager.Open(target, false); }
                catch (System.Exception ex) { Say($"\n{LOG_PREFIX} Could not reopen {Path.GetFileName(target)}: {ex.Message}\n"); }
            }
            try
            {
                Document doc = FindOpen(before);
                if (doc != null && Application.DocumentManager.MdiActiveDocument != doc) Activate(doc);
            }
            catch (System.Exception ex) { Say($"\n{LOG_PREFIX} Could not switch back to {Path.GetFileName(before)}: {ex.Message}\n"); }
        }

        /// <summary>Bước chưa đụng file cũ: kiểm file đích, đọc file cũ và kiểm đúng panel KHI FILE VẪN ĐANG MỞ, tạo file mới ở <paramref name="newFile"/>,
        /// sau cùng mới đóng file đích đang mở. Lô có 2 bản chèn cùng 1 block: job sau bị từ chối mà không đóng file job trước vừa dựng.
        /// Trả lý do dừng (null = đi tiếp). Dừng ở đây thì file cũ còn nguyên và vẫn mở như trước.</summary>
        private static string PrepareUpdate(string sourceFile, PanelJob job, string newFile, out Panel3DFile.OldPanelData carry, out bool closed)
        {
            carry = null;
            closed = false;
            string file = Path.GetFileName(job.Target);
            // Panel tên trùng tên file nguồn (X_3D.dwg chứa panel X): đích chính là bản vẽ nguồn, không bao giờ được đóng hay thay.
            if (string.Equals(job.Target, sourceFile, System.StringComparison.OrdinalIgnoreCase))
                return $"{file} is the drawing being built from; use New copy.";

            Document open = FindOpen(job.Target);
            if (open != null)
            {
                Activate(open);   // DBMOD đọc theo document active
                if (!IsSavedOnDisk(open))
                {
                    // Hỏi ngay thay vì từ chối (kỹ sư 2026-11-10): lưu ⇒ thay đổi chưa lưu được mang sang bản Update.
                    if (!MCG_3DPanel.UI.UnsavedFileDialog.AskSave(file)) return $"{file} was skipped: it has unsaved changes.";
                    string saveError = SaveOpenTarget(open, file);
                    if (saveError != null) return saveError;
                }
            }

            try { carry = Panel3DFile.ReadOld(job.Target); }
            catch (System.Exception ex) { return $"Could not read {file} ({ex.Message}). Use New copy."; }

            string refusal = Panel3DPath.UpdateRefusal(file, carry.Meta, job.RefId.Handle.ToString());
            if (refusal != null) return refusal;

            try { Panel3DFile.Create(sourceFile, job.RefId, newFile, carry); }
            catch (System.Exception ex)   // Create nói tên file tạm trong Message ⇒ lấy lý do gốc
            {
                return $"Could not create the new {file}: {(ex.InnerException ?? ex).Message}. The old file was not changed.";
            }

            if (open != null)
            {
                ActivateAnother(open, sourceFile);
                open.CloseAndDiscard();
                closed = true;
            }
            return null;
        }

        /// <summary>"Save and Update": lưu file 3D đang mở như lệnh Save (giữ .bak cũ sang tên có giờ, như Build). Trả lý do dừng (null = đã lưu). Không ném.</summary>
        internal static string SaveOpenTarget(Document open, string file)
        {
            try
            {
                using (open.LockDocument())
                {
                    string keptBak = Panel3DFile.MoveOldBakAside(open.Name);
                    if (keptBak != null) Say($"\n{LOG_PREFIX} The existing .bak of {file} was kept as {keptBak}.\n");
                    Panel3DFile.Save(open);   // DBMOD chưa về 0 không sao: file sắp được đóng không lưu, bản trên đĩa đã đủ
                }
                return null;
            }
            catch (System.Exception ex)
            {
                return $"Could not save {file}: {ex.Message}. The file was not updated and is still open with its changes.";
            }
        }

        /// <summary>Lỗi sau khi đã thay file: đóng không lưu, xoá file của lần này, trả file cũ về tên cũ. Trả true nếu file cũ đã về đúng tên. Không ném.</summary>
        private static bool UndoSwap(Document doc, string target, string backup, string sourceFile, bool saving, string keptBak, string why, string tag, BuildRun run)
        {
            string file = Path.GetFileName(target);
            string bakNote = keptBak == null ? "" : $" The existing .bak of {file} is now {keptBak}.";
            try { ActivateAnother(doc, sourceFile); }
            catch (System.Exception ex) { Debug.WriteLine($"{LOG_PREFIX} ActivateAnother: {ex.Message}"); }

            string notRemoved = Panel3DFile.Discard(doc, target, saving);
            string notBack = MoveBackup(backup, target);
            if (notBack == null)
            {
                // Đã trả file cũ về đúng tên: thông báo "Delete it by hand" của Discard sẽ chỉ nhầm vào file cũ ⇒ không hiện cho người dùng.
                if (notRemoved != null) Debug.WriteLine($"{LOG_PREFIX} {notRemoved}");
                StopPanel(run, tag, $"Update of {file} failed: {why}. The old file was restored.{bakNote}");
                return true;
            }

            if (notRemoved != null) Say($"\n{LOG_PREFIX} {notRemoved}\n");
            StopPanel(run, tag, File.Exists(target)
                ? $"Update of {file} failed: {why}. The new file is still at {target}, and the old file is safe at {backup}. Close the new file if it is open, delete it, then rename the old file to {file}.{bakNote}"
                : $"Update of {file} failed: {why}. {notBack}{bakNote}");
            return false;
        }

        /// <summary>Trả bản sao lưu về tên cũ, không ghi đè. Trả thông báo kèm đường dẫn đầy đủ của bản sao lưu nếu không trả được (null = OK). Không ném.</summary>
        private static string MoveBackup(string backup, string target)
        {
            try
            {
                File.Move(backup, target, false);
                return null;
            }
            catch (System.Exception ex)
            {
                return $"The old file is safe at {backup} but could not be moved back ({ex.Message}). Rename it to {Path.GetFileName(target)} by hand.";
            }
        }

        /// <summary>Document sắp đóng đang active ⇒ đưa bản vẽ nguồn (hoặc drawing khác) lên trước khi đóng. Ném nếu không chuyển được.</summary>
        private static void ActivateAnother(Document closing, string sourceFile)
        {
            if (closing == null || Application.DocumentManager.MdiActiveDocument != closing) return;
            Document other = FindOpen(sourceFile);
            if (other == null || other == closing)
            {
                other = null;
                foreach (Document d in Application.DocumentManager)
                    if (d != closing) { other = d; break; }
            }
            if (other != null) Activate(other);
        }

        /// <summary>Panel dừng: đếm lỗi, đưa lý do lên thanh trạng thái và dòng lệnh; các panel khác vẫn chạy.</summary>
        private static void StopPanel(BuildRun run, string tag, string text)
        {
            run.Failed++;
            run.Problems.Add(text);
            Say($"\n{LOG_PREFIX} {tag} {text}\n");
        }

        /// <summary>Chỉnh sửa cũ không còn part 2D tương ứng ⇒ báo số lượng. Chạy SAU khi đã lưu: lỗi ở đây không được làm hỏng lần Update.</summary>
        private static void AddUnmatchedNote(Panel3DFile.OldPanelData carry, BuildResult build, BuildRun run)
        {
            try
            {
                CorrectionJson.Deserialize(carry.CorrectionsJson, out var parts, out var deleted);
                var stored = new HashSet<string>(parts.Keys);
                stored.UnionWith(deleted);
                var names = MCG_3DPanel.Naming.NamesJson.Deserialize(carry.NamesJson, out var deletedNames, out var merges, out var splits);
                stored.UnionWith(names.Keys);
                stored.UnionWith(deletedNames);
                stored.UnionWith(merges.SelectMany(g => g));
                stored.UnionWith(splits);
                var partKeys = new HashSet<string>(build.ClassifiedParts.Select(p => p.SourceKey).Where(k => k != null));
                int n = Panel3DPath.CountUnmatched(stored, partKeys);
                if (n > 0) run.Problems.Add($"{n} edits no longer match the 2D drawing.");
            }
            catch (System.Exception ex)
            {
                run.Problems.Add($"Could not count the edits that no longer match the 2D drawing: {ex.Message}");
            }
        }

        /// <summary>Tuỳ chọn Open: mở file 3D có sẵn, hoặc chuyển sang nếu đang mở. Không dựng.</summary>
        private static Document OpenExisting(string path, BuildRun run)
        {
            try
            {
                Document doc = FindOpen(path) ?? Application.DocumentManager.Open(path, false);
                Activate(doc);
                run.Opened++;
                return doc;
            }
            catch (System.Exception ex)
            {
                run.Failed++;
                Say($"\n{LOG_PREFIX} Could not open {Path.GetFileName(path)}: {ex.Message}\n");
                return null;
            }
        }

        /// <summary>Đưa document lên active, ném nếu không thành công. Sau đó KHÔNG dùng Editor của document cũ.</summary>
        internal static void Activate(Document doc)
        {
            Application.DocumentManager.MdiActiveDocument = doc;
            if (Application.DocumentManager.MdiActiveDocument != doc)
                throw new System.InvalidOperationException($"{Path.GetFileName(doc.Name)} did not become the active drawing");
            HostApplicationServices.WorkingDatabase = doc.Database;   // entity mới lấy mặc định từ đây
        }

        /// <summary>File cuối bị bỏ làm AutoCAD chuyển sang drawing khác ⇒ đưa file tốt cuối cùng trở lại active (spec: file cuối là file active).</summary>
        private static void Reactivate(string path, BuildRun run)
        {
            if (path == null) return;
            try
            {
                Document doc = FindOpen(path);   // tìm lại theo đường dẫn: file đã đóng thì thôi, không chạm Document đã huỷ
                if (doc != null && Application.DocumentManager.MdiActiveDocument != doc) Activate(doc);
            }
            catch (System.Exception ex)
            {
                string error = $"Could not switch back to {Path.GetFileName(path)}: {ex.Message}";
                run.Problems.Add(error);
                Say($"\n{LOG_PREFIX} {error}\n");
            }
        }

        /// <summary>Bản trên đĩa khớp bản đang mở: đã đặt tên, là .dwg/.dwt, file còn đó, không có thay đổi chưa lưu.
        /// Dùng Document.Name, KHÔNG dùng Database.Filename: bản vẽ mới có Filename = file .dwt, sau autosave có thể là .sv$ trong %TEMP%.</summary>
        internal static bool IsSavedOnDisk(Document doc)
        {
            string file = doc.Name;
            if (!doc.IsNamedDrawing || string.IsNullOrEmpty(file) || !File.Exists(file)) return false;
            string ext = Path.GetExtension(file);
            if (!ext.Equals(".dwg", System.StringComparison.OrdinalIgnoreCase) && !ext.Equals(".dwt", System.StringComparison.OrdinalIgnoreCase)) return false;
            // Bit 8 (window) và 16 (view): file 3D đọc từ đĩa nên đổi khung nhìn (zoom, pan) không ảnh hưởng nội dung.
            try { return (System.Convert.ToInt32(Application.GetSystemVariable("DBMOD")) & ~DBMOD_VIEW_ONLY) == 0; }
            catch (System.Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} DBMOD: {ex.Message}");
                return false;   // không đọc được ⇒ coi như chưa lưu, người dùng vẫn thấy thông báo
            }
        }

        /// <summary>Đọc tên block của từng panel từ bản vẽ nguồn (chỉ đọc) và tính file đích, TRƯỚC khi đổi document.</summary>
        private static List<PanelJob> PlanJobs(Database source, string sourceFile, ObjectId[] ids)
        {
            string folder = Path.GetDirectoryName(sourceFile);
            var jobs = new List<PanelJob>();
            using (Transaction tr = source.TransactionManager.StartOpenCloseTransaction())
            {
                foreach (ObjectId id in ids)
                {
                    string name = PartCollector.GetName(tr, (BlockReference)tr.GetObject(id, OpenMode.ForRead));
                    jobs.Add(new PanelJob { RefId = id, Name = name, Target = Panel3DPath.Target(folder, name) });
                }
            }
            return jobs;
        }

        private static bool IsTaken(string path) => File.Exists(path) || FindOpen(path) != null;

        internal static Document FindOpen(string path)
        {
            foreach (Document d in Application.DocumentManager)
                if (string.Equals(d.Name, path, System.StringComparison.OrdinalIgnoreCase)) return d;
            return null;
        }

        /// <summary>1 panel trong lô Build.</summary>
        private sealed class PanelJob
        {
            public ObjectId RefId;   // thuộc bản vẽ nguồn — sang file 3D chỉ dùng Handle
            public string Name;
            public string Target;
            public bool Update;      // file đích đang có sẽ được thay (giữ chỉnh sửa, file cũ thành .bak)
        }

        /// <summary>Ghi dòng lệnh của drawing ĐANG active, lấy Editor mới mỗi lần; không bao giờ ném.</summary>
        private static void Say(string message)
        {
            try { Application.DocumentManager.MdiActiveDocument?.Editor?.WriteMessage(message); }
            catch (System.Exception ex) { Debug.WriteLine($"{LOG_PREFIX} WriteMessage: {ex.Message}"); }
        }

        private static void RegenActive()
        {
            try { Application.DocumentManager.MdiActiveDocument?.Editor?.Regen(); }
            catch (System.Exception ex) { Say($"\n{LOG_PREFIX} Regen failed: {ex.Message}\n"); }
        }

        /// <summary>Ghi 1 report Build ra Desktop (KHÔNG mở). Lỗi ghi file không làm hỏng Build. Báo trên Editor đang active.</summary>
        private static void SaveBuildReport(List<BuildResult> builds)
        {
            if (builds.Count == 0) return;
            try
            {
                string outPath = ReportOutput.BuildOutputPath("Build");
                ReportOutput.WriteBuildReport(builds, outPath);
                foreach (var b in builds) b.ReportPath = outPath;
                Say($"\n{LOG_PREFIX} Build report (debug): {outPath}\n");
            }
            catch (System.Exception ex)
            {
                Say($"\n{LOG_PREFIX} Could not write the build report: {ex.Message}\n");
            }
        }

        /// <summary>Phân loại (kèm correction tab Result của panel này) + dựng 3D cho 1 block 2D. Cần LockDocument.</summary>
        internal static BuildResult BuildOne(Database db, ObjectId src2D, PanelInput input, PanelPosition position, string sourcePath,
            out ClassificationResult cls)
        {
            var sw = Stopwatch.StartNew();
            // Correction gắn với ĐỊNH NGHĨA block 2D (XRecord) — nạp vào cache lần đầu gặp trong phiên.
            ObjectId btr2D;
            Matrix3d xform;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var br = (BlockReference)tr.GetObject(src2D, OpenMode.ForRead, true);
                btr2D = br.BlockTableRecord;
                xform = br.BlockTransform;
                CorrectionStore.EnsureLoaded(tr, btr2D);
                tr.Commit();
            }
            string panelKey = CorrectionStore.PanelKey(btr2D);
            cls = new ClassificationService().Classify(src2D, position, CorrectionStore.For(panelKey));
            cls.DeletedKeys = CorrectionStore.Deleted(panelKey);
            BuildResult build = new Build3DService().Build(db, cls, input, position);
            build.PanelBlockName = cls.PanelBlockName;
            build.ClassifiedParts.AddRange(cls.Parts);
            build.SourceRefId = src2D;
            build.SourceBtrId = btr2D;
            build.SourceTransform = xform;
            build.Input = input;
            build.Position = position;
            build.SourcePath = sourcePath ?? "";
            WritePanelMeta(db, build);   // SAU khi có Input/Position/SourceRefId — dữ liệu mở-lại-file cần đủ
            build.BuildSeconds = sw.Elapsed.TotalSeconds;
            return build;
        }

        // ─── Rebuild 1 panel từ tab Result (kỹ sư 2026-11-04: kết quả NGAY, không thao tác thêm) ────

        /// <summary>Báo tab Result: (bản cũ, bản mới — null nếu rebuild thất bại, thông báo).</summary>
        public static event System.Action<BuildResult, BuildResult, string> Rebuilt;

        // Hàng đợi rebuild: sửa 2 panel liên tiếp trước khi lệnh kịp chạy KHÔNG được mất yêu cầu nào.
        // Mỗi panel (SourceRefId) chỉ giữ bản build mới nhất mà palette đang hiển thị.
        private static readonly List<BuildResult> _pending = new List<BuildResult>();
        private static bool _sendScheduled;

        /// <summary>
        /// Gọi từ Palette (modeless): xếp yêu cầu rồi chạy lệnh nội bộ <c>MCG_Rebuild3DPanel</c> — chạy như 1 LỆNH
        /// nên cả lần rebuild là 1 bước Undo, và AutoCAD tự khoá document. Đang có lệnh khác chạy ⇒ chờ
        /// Application.Idle rồi mới gửi (gửi ngay sẽ gõ chữ vào prompt của lệnh đang chạy).
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
            // Mỗi PANEL (block 3D — tên ổn định qua các lần rebuild) giữ 1 yêu cầu mới nhất. KHÔNG gộp theo SourceBtrId:
            // 2 bản chèn của CÙNG 1 block 2D là 2 panel khác nhau (sửa hàng loạt 2 panel ⇒ phải rebuild cả 2 — review).
            _pending.RemoveAll(b => ReferenceEquals(b, current) || (!string.IsNullOrEmpty(b.BlockName) && b.BlockName == current.BlockName
                                                                   && b.SourceRefId.Database == current.SourceRefId.Database));
            _pending.Add(current);
            ScheduleSend();
        }

        /// <summary>Bản vẽ sắp đóng: bỏ yêu cầu rebuild còn treo của nó (ObjectId sắp không còn hợp lệ).</summary>
        internal static void ForgetDatabase(Database db) => _pending.RemoveAll(b => b.SourceRefId.Database == db);

        private static void ScheduleSend()
        {
            if (_sendScheduled) return;
            _sendScheduled = true;
            Application.Idle += SendWhenIdle;
        }

        private static void SendWhenIdle(object sender, System.EventArgs e)
        {
            if (_pending.Count == 0)   // drawing của yêu cầu đã đóng ⇒ không còn gì để gửi, thôi chờ
            {
                Application.Idle -= SendWhenIdle;
                _sendScheduled = false;
                return;
            }
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            // Yêu cầu thuộc drawing khác ⇒ chờ người dùng quay lại: lệnh chạy ở document active, gửi nhầm chỗ thì không rebuild gì.
            if (!_pending.Any(b => b.SourceRefId.Database == doc.Database)) return;
            if (!string.IsNullOrEmpty(doc.CommandInProgress)) return;   // còn lệnh khác → chờ Idle sau
            // Đang Pick / Editor đang chờ nhập (prompt KHÔNG phải lệnh ⇒ CommandInProgress rỗng): gửi lúc này thì chuỗi lệnh bị
            // gõ VÀO prompt và mất — lỗi 2026-11-05 "Pick, sửa Tripping, Enter ⇒ 3D không đổi". Chờ prompt xong.
            if (CadPick.IsPicking || !doc.Editor.IsQuiescent) return;
            Application.Idle -= SendWhenIdle;
            _sendScheduled = false;
            doc.SendStringToExecute("MCG_Rebuild3DPanel ", true, false, false);
        }

        [CommandMethod("MCG_Rebuild3DPanel", CommandFlags.NoHistory)]
        public void RebuildCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            var batch = _pending.Where(b => b.SourceRefId.Database == doc.Database).ToList();
            _pending.RemoveAll(batch.Contains);
            foreach (var old in batch) RebuildOne(doc, old);
            if (_pending.Count > 0) ScheduleSend();   // còn yêu cầu của drawing khác: tiếp tục chờ drawing đó
        }

        private static void RebuildOne(Document doc, BuildResult old)
        {
            Database db = doc.Database;
            // Block reference 2D nguồn nằm trong chính file 3D. Đã xoá (vd LAYDEL) hoặc định nghĩa bị purge ⇒ báo lỗi, không rebuild.
            if (!SourceStillExists(db, old.SourceRefId))
            {
                Rebuilt?.Invoke(old, null, "Source 2D block no longer exists — cannot rebuild.");
                return;
            }

            // Ghi correction xuống DWG (XRecord BTR 2D) TRƯỚC khi build — cùng bước Undo với lần rebuild.
            // Save tự EnsureLoaded: sau khi mở lại bản vẽ cache còn trống, KHÔNG được ghi đè XRecord bằng rỗng.
            string saveError = doc.IsReadOnly
                ? "Corrections could not be saved in the drawing (drawing is read-only)."
                : null;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                saveError = CorrectionStore.Save(tr, old.SourceBtrId) ?? saveError;
                tr.Commit();
            }
            string Tail(string m) => saveError == null ? m : m + "  " + saveError;

            BuildResult neu;
            try
            {
                neu = BuildOne(db, old.SourceRefId, old.Input, old.Position, old.SourcePath, out _);
            }
            catch (System.Exception ex)
            {
                Rebuilt?.Invoke(old, null, Tail("Rebuild failed: " + ex.Message + " — previous 3D kept."));
                return;
            }
            if (neu.Solids.Count == 0)   // xoá hết solid (Delete) vẫn hợp lệ — chỉ coi là lỗi khi KHÔNG dựng được gì
            {
                EraseBuild(db, neu);
                Rebuilt?.Invoke(old, null, Tail("Rebuild produced no solids — previous 3D kept."));
                return;
            }

            // Thay bản cũ: xoá block 3D ĐANG SỐNG của panel — tìm theo TÊN, không tin id đã lưu (sau Ctrl+Z
            // undo 1 lần rebuild, id palette giữ đã bị xoá còn block gốc sống lại). Rồi đặt tên block mới về tên cũ.
            string replaceError = null;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    var toErase = new List<ObjectId>();
                    if (!old.BtrId.IsNull && !old.BtrId.IsErased) toErase.Add(old.BtrId);
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    if (!string.IsNullOrEmpty(old.BlockName) && bt.Has(old.BlockName))
                    {
                        ObjectId live = bt[old.BlockName];
                        if (live != neu.BtrId && !toErase.Contains(live)) toErase.Add(live);
                    }
                    foreach (ObjectId btrId in toErase)
                    {
                        var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForWrite);
                        foreach (ObjectId refId in btr.GetBlockReferenceIds(true, false))
                            tr.GetObject(refId, OpenMode.ForWrite).Erase();
                        btr.Erase();
                    }
                    if (!old.BlockRefId.IsNull && !old.BlockRefId.IsErased)
                        tr.GetObject(old.BlockRefId, OpenMode.ForWrite).Erase();

                    if (!string.IsNullOrEmpty(old.BlockName))
                    {
                        ((BlockTableRecord)tr.GetObject(neu.BtrId, OpenMode.ForWrite)).Name = old.BlockName;
                        neu.BlockName = old.BlockName;
                    }
                }
                catch (System.Exception ex)
                {
                    replaceError = ex.Message;
                    doc.Editor.WriteMessage($"\n{LOG_PREFIX} Rebuild: could not replace the old block ({ex.Message}).\n");
                }
                tr.Commit();
            }
            WritePanelMeta(db, neu);   // sau khi neu có BlockName/BtrId cuối; ghi lại để dữ liệu khớp bản vừa thay
            neu.Kept = true;
            PanelSession.Replace(db, old, neu);
            doc.Editor.Regen();
            // Có part hỏng / cảnh báo ⇒ ghi report riêng cho lần rebuild này để thông báo "see …json" trỏ đúng file
            // chứa lỗi (review I1). Không lỗi ⇒ không sinh file mới trên Desktop mỗi lần sửa.
            if (neu.Warnings.Count > 0 || neu.Parts.Any(p => !p.Built && p.Type != "Unknown"))
                SaveBuildReport(new List<BuildResult> { neu });
            string msg = replaceError == null
                ? $"Rebuilt {neu.PanelBlockName} in {neu.BuildSeconds:0.0} s."
                : $"Rebuilt {neu.PanelBlockName}, but the previous 3D block could not be replaced: {replaceError}";
            Rebuilt?.Invoke(old, neu, Tail(msg));
        }
        // ─── Dữ liệu panel lưu trong block 3D (XRecord MCG3D_PANEL) ─────────────

        /// <summary>Dấu vân tay block 2D nguồn (đọc từ bản đã nhập trong chính file 3D); lỗi ⇒ "" (không làm hỏng build).</summary>
        static string Fingerprint3D(Database db, ObjectId srcBtr)
        {
            if (srcBtr.IsNull) return "";
            try
            {
                using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                    return Services.Detail.Detail3DMatch.Fingerprint(Detail.SourceFingerprintReader.Records(tr, srcBtr));
            }
            catch (System.Exception) { return ""; }
        }

        /// <summary>Ghi PanelMeta (nguồn 2D, input, vị trí, danh sách solid) vào BTR 3D để mở lại file vẫn điền được các tab.
        /// Cần LockDocument; lỗi ghi KHÔNG làm hỏng build — chỉ thêm cảnh báo.</summary>
        internal static void WritePanelMeta(Database db, BuildResult build)
        {
            if (build.BtrId.IsNull) return;   // không dựng được block 3D ⇒ không có chỗ ghi
            try
            {
                var meta = new PanelMeta
                {
                    SourceRefHandle = build.SourceRefId.IsNull ? "" : build.SourceRefId.Handle.ToString(),
                    SourceBtrHandle = build.SourceBtrId.IsNull ? "" : build.SourceBtrId.Handle.ToString(),
                    SourcePath = build.SourcePath ?? "",
                    SourceFingerprint = Fingerprint3D(db, build.SourceBtrId),
                    Input = build.Input,
                    Position = (int)build.Position,
                };
                meta.Warnings.AddRange(build.Warnings);
                meta.NotBuilt.AddRange(build.Parts.Where(p => !p.Built)
                    .Select(p => new PanelMetaNotBuilt { Handle = p.Handle ?? "", Type = p.Type ?? "", Code = p.Code ?? "" }));
                foreach (BuiltSolid s in build.Solids)
                {
                    var ms = new PanelMetaSolid
                    {
                        Handle = s.SolidId.IsNull ? "" : s.SolidId.Handle.ToString(),
                        Type = s.Type ?? "",
                        PartKey = s.Part?.SourceKey,   // null với solid sinh thêm
                        FlipKey = s.FlipKey ?? "",
                        DeleteKey = s.DeleteKey ?? "",
                        Code = s.Code ?? "",
                        Deleted = s.Deleted,
                        SnipLo = s.Part?.SnipLo ?? false,
                        SnipHi = s.Part?.SnipHi ?? false,
                    };
                    ms.MergedCodes.AddRange(s.MergedCodes);
                    meta.Solids.Add(ms);
                }
                // FromJson từ chối handle rỗng ⇒ ghi ra cũng vô dụng; coi là lỗi thay vì ghi file hỏng.
                if (string.IsNullOrEmpty(meta.SourceRefHandle) || string.IsNullOrEmpty(meta.SourceBtrHandle)
                    || meta.Solids.Any(x => string.IsNullOrEmpty(x.Handle)))
                    throw new System.InvalidOperationException("missing source or solid handle");
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    CorrectionXRecord.Write(tr, build.BtrId, CorrectionXRecord.PanelKey, meta.ToJson());
                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                build.Report.Add($"[PanelMeta] không ghi được: {ex.Message}");
                build.Warnings.Add($"Panel data could not be saved: {ex.Message}. This panel will not fill the tabs when the file is reopened.");
            }
        }

        /// <summary>Block reference 2D nguồn còn sống (chưa bị xoá) và định nghĩa block chưa bị PURGE.</summary>
        private static bool SourceStillExists(Database db, ObjectId src)
        {
            if (src.IsNull || src.Database != db || src.IsErased) return false;
            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    var br = tr.GetObject(src, OpenMode.ForRead) as BlockReference;
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

            var pso = new PromptSelectionOptions { MessageForAdding = "\nSelect panel block(s):" };
            // Bỏ luôn block 3D đã dựng (MCG3D_*) — quét window qua vùng đã build không bị dựng lại.
            var filter = new SelectionFilter(new[]
            {
                new TypedValue((int)DxfCode.Start, "INSERT"),
                new TypedValue((int)DxfCode.BlockName, "~MCG3D_*"),
            });

            PromptSelectionResult psr = ed.GetSelection(pso, filter);
            if (psr.Status != PromptStatus.OK || psr.Value == null || psr.Value.Count == 0)
            {
                ed.WriteMessage("\nCancelled.\n");
                return false;
            }
            ids = psr.Value.GetObjectIds();
            ed.WriteMessage($"\n{LOG_PREFIX} {ids.Length} panel block(s) selected.\n");
            return true;
        }
    }

    /// <summary>Kết quả 1 lần bấm Build 3D, để tab Input báo trạng thái.</summary>
    public sealed class BuildRun
    {
        /// <summary>Lý do dừng trước khi tạo file; null = không dừng, hoặc người dùng huỷ.</summary>
        public string Stop;
        /// <summary>Số file 3D đã dựng và lưu.</summary>
        public int Files;
        public int Solids;
        /// <summary>Số panel không ra solid (file đã bỏ).</summary>
        public int Empty;
        /// <summary>Số panel hoặc file lỗi (đã báo trên dòng lệnh).</summary>
        public int Failed;
        /// <summary>Số file có sẵn đã mở theo tuỳ chọn Open.</summary>
        public int Opened;
        /// <summary>Lỗi phụ cần báo trên thanh trạng thái, vd không xoá được file.</summary>
        public List<string> Problems = new List<string>();
    }
}
