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

        /// <summary>
        /// Quét chọn 1 hoặc NHIỀU Block Panel → mỗi panel 1 file 3D riêng (<see cref="Panel3DFile"/>), mở và dựng trong file đó
        /// (panel lỗi không chặn panel khác). File đích đã có ⇒ hỏi 1 LẦN cho cả lô. Bản vẽ nguồn KHÔNG bị ghi.
        /// Trả build của file mở cuối (rỗng nếu không dựng được gì), null nếu dừng trước khi tạo file
        /// (<see cref="BuildRun.Stop"/> nói lý do; null = người dùng huỷ).
        /// Từ lúc đổi document: KHÔNG dùng lại Editor của document cũ (AutoCAD crash) — mọi thông báo qua <see cref="Say"/>.
        /// </summary>
        public static List<BuildResult> RunBuild(PanelInput input, PanelPosition position, out BuildRun run)
        {
            run = new BuildRun();
            Document active = Application.DocumentManager.MdiActiveDocument;
            if (active == null) return null;
            if (!IsSavedOnDisk(active)) { run.Stop = "Save this drawing first, then Build 3D."; return null; }
            if (!TryPickPanels(out Document src, out ObjectId[] ids)) return null;

            Database source = src.Database;
            Document lastGood = null;   // file tốt cuối cùng (mở hoặc dựng) — cuối lần chạy phải là file active
            List<PanelJob> jobs = PlanJobs(source, ids);

            var existing = new HashSet<string>(jobs.Select(j => j.Target).Where(IsTaken), System.StringComparer.OrdinalIgnoreCase);
            if (existing.Count > 0)
            {
                ExistingChoice choice = ExistingFileDialog.Ask(existing.Select(Path.GetFileName));
                if (choice == ExistingChoice.Cancel) return null;
                if (choice == ExistingChoice.Open)
                {
                    // Mở trước, dựng sau: file active cuối cùng là file vừa dựng, khớp dòng trên các tab.
                    foreach (string path in existing) lastGood = OpenExisting(path, run) ?? lastGood;
                    jobs.RemoveAll(j => existing.Contains(j.Target));
                }
            }

            // NextFree giữ chỗ trong cùng lô: New copy, và 2 bản chèn của cùng 1 block ra 2 file khác nhau.
            var reserved = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (PanelJob job in jobs)
            {
                job.Target = Panel3DPath.NextFree(job.Target, p => IsTaken(p) || reserved.Contains(p));
                reserved.Add(job.Target);
            }

            var last = new List<BuildResult>();
            for (int i = 0; i < jobs.Count; i++)
            {
                BuildResult build = BuildInNewFile(source, jobs[i], $"[{i + 1}/{jobs.Count}]", input, position, run, out Document fileDoc);
                if (build == null) continue;

                lastGood = fileDoc;
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
        private static BuildResult BuildInNewFile(Database source, PanelJob job, string tag, PanelInput input, PanelPosition position,
            BuildRun run, out Document fileDoc)
        {
            fileDoc = null;
            string file = Path.GetFileName(job.Target);
            Document doc = null;
            bool created = false;   // CHỈ bỏ file do chính lần này tạo — không bao giờ xoá file có sẵn của người dùng
            bool saving = false;    // đã dời .bak cũ ⇒ .bak đứng ở tên đó từ đây là do lần lưu này sinh ra
            try
            {
                Panel3DFile.Create(source, job.RefId, job.Target);
                created = true;
                doc = Application.DocumentManager.Open(job.Target, false);
                Activate(doc);

                Database db = doc.Database;
                if (!db.TryGetObjectId(job.RefId.Handle, out ObjectId src2D))
                    throw new System.InvalidOperationException("the panel is missing from the new file");

                BuildResult build;
                using (doc.LockDocument())
                {
                    build = BuildOne(db, src2D, input, position, out ClassificationResult cls);
                    Say($"\n{LOG_PREFIX} {tag} {cls.PanelBlockName}: {build.SolidCount} solid ({cls.Summary()}). Block: {build.BlockName}\n");
                    // "Không dựng được" = không có solid nào ĐƯỢC DỰNG (kể cả solid đã Delete theo correction —
                    // panel xoá hết solid vẫn là kết quả hợp lệ, còn Restore được).
                    if (build.Solids.Count > 0)
                    {
                        build.Kept = true;
                        RegenActive();
                        string keptBak = Panel3DFile.MoveOldBakAside(job.Target);
                        saving = true;
                        if (keptBak != null) Say($"\n{LOG_PREFIX} The existing .bak of {file} was kept as {keptBak}.\n");
                        if (!Panel3DFile.Save(doc)) Say($"\n{LOG_PREFIX} {file} is saved, but AutoCAD may still ask to save it on close.\n");
                    }
                }
                if (build.Solids.Count > 0)
                {
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
        private static void Activate(Document doc)
        {
            Application.DocumentManager.MdiActiveDocument = doc;
            if (Application.DocumentManager.MdiActiveDocument != doc)
                throw new System.InvalidOperationException($"{Path.GetFileName(doc.Name)} did not become the active drawing");
            HostApplicationServices.WorkingDatabase = doc.Database;   // entity mới lấy mặc định từ đây
        }

        /// <summary>File cuối bị bỏ làm AutoCAD chuyển sang drawing khác ⇒ đưa file tốt cuối cùng trở lại active (spec: file cuối là file active).</summary>
        private static void Reactivate(Document doc, BuildRun run)
        {
            if (doc == null || Application.DocumentManager.MdiActiveDocument == doc) return;
            try { Activate(doc); }
            catch (System.Exception ex)
            {
                string error = $"Could not switch back to {Path.GetFileName(doc.Name)}: {ex.Message}";
                run.Problems.Add(error);
                Say($"\n{LOG_PREFIX} {error}\n");
            }
        }

        /// <summary>Bản trên đĩa khớp bản đang mở: đã đặt tên (bản vẽ mới chưa lưu có Filename = file .dwt), file còn đó, không có thay đổi chưa lưu.</summary>
        private static bool IsSavedOnDisk(Document doc)
        {
            string file = doc.Database.Filename;
            if (!doc.IsNamedDrawing || string.IsNullOrEmpty(file) || !File.Exists(file)) return false;
            // Bit 8 (window) và 16 (view): file 3D đọc từ đĩa nên đổi khung nhìn (zoom, pan) không ảnh hưởng nội dung.
            try { return (System.Convert.ToInt32(Application.GetSystemVariable("DBMOD")) & ~DBMOD_VIEW_ONLY) == 0; }
            catch (System.Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} DBMOD: {ex.Message}");
                return false;   // không đọc được ⇒ coi như chưa lưu, người dùng vẫn thấy thông báo
            }
        }

        /// <summary>Đọc tên block của từng panel từ bản vẽ nguồn (chỉ đọc) và tính file đích, TRƯỚC khi đổi document.</summary>
        private static List<PanelJob> PlanJobs(Database source, ObjectId[] ids)
        {
            string folder = Path.GetDirectoryName(source.Filename);
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

        private static Document FindOpen(string path)
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
        internal static BuildResult BuildOne(Database db, ObjectId src2D, PanelInput input, PanelPosition position,
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
            if (_sendScheduled) return;
            _sendScheduled = true;
            Application.Idle += SendWhenIdle;
        }

        /// <summary>Bản vẽ sắp đóng: bỏ yêu cầu rebuild còn treo của nó (ObjectId sắp không còn hợp lệ).</summary>
        internal static void ForgetDatabase(Database db) => _pending.RemoveAll(b => b.SourceRefId.Database == db);

        private static void SendWhenIdle(object sender, System.EventArgs e)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
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
        }

        private static void RebuildOne(Document doc, BuildResult old)
        {
            Database db = doc.Database;
            // Block reference 2D còn (kể cả đã xoá khi Keep, trong phiên) ⇒ dùng luôn (mở bằng openErased).
            // Không còn (bản vẽ đã đóng/mở lại, hoặc định nghĩa bị purge) ⇒ báo lỗi, không rebuild.
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
                neu = BuildOne(db, old.SourceRefId, old.Input, old.Position, out _);
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
                    Input = build.Input,
                    Position = (int)build.Position,
                };
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
