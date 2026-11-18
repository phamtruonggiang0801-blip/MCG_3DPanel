using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using MCG_3DPanel.Services.Classify;
using MCG_3DPanel.Services.Detail;

[assembly: CommandClass(typeof(MCG_3DPanel.Commands.Detail.DetailCommands))]

namespace MCG_3DPanel.Commands.Detail
{
    /// <summary>Create Drawing ▸ Detail Plan / Detail View (spec 2026-11-16): chạy trong bản vẽ 2D. Chọn biên AM_7 → tìm _Assy
    /// → đọc file 3D → cắt solid → 1 transaction: định nghĩa &lt;panel&gt;_DET.n, kéo-thả chèn, tiêu đề, nhãn DET.n, XData biên.
    /// Esc ở bất kỳ bước nào / lỗi ⇒ không để lại gì.</summary>
    public class DetailCommands
    {
        public const string PlanCommand = "MCG_DetailPlan", ViewCommand = "MCG_DetailView", SectionCommand = "MCG_DetailSection",
                            UpdateCommand = "MCG_DetailUpdate";
        const string LabelLayer = "Mechanical-AM_1";
        const double LabelHeight = 150, LabelGap = 50;

        public static event Action<string, bool> Finished;

        /// <summary>Gọi từ palette: chạy lệnh trong ngữ cảnh lệnh của document đang active. Chỉ nhận đúng tên lệnh Detail.</summary>
        public static void Request(string command)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null || !DetailCommandRules.IsCommand(command)) return;
            doc.SendStringToExecute(command.Trim() + " ", true, false, false);
        }

        /// <summary>Kiểm rẻ cho menu palette (bản vẽ <paramref name="doc"/> đang active): có reference *_Assy trong Model Space mà thư mục
        /// của bản vẽ có file &lt;panel&gt;_3D*.dwg; <paramref name="hasDetail"/> = có block / reference mang bản ghi MCG3D_DETAIL.
        /// Khớp đầy đủ do lệnh làm. Chỉ đọc.</summary>
        public static bool HasPanelWith3DFile(Document doc, out bool hasDetail)
        {
            hasDetail = false;
            bool found = false;
            string dir = string.IsNullOrEmpty(doc.Name) ? null : Path.GetDirectoryName(doc.Name);
            var checkedPanels = new HashSet<string>(StringComparer.Ordinal);
            using (Transaction tr = doc.Database.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is BlockReference br)) continue;
                    if (!hasDetail)
                        hasDetail = DetailWriter.Read(br) != null
                            || DetailWriter.Read(tr.GetObject(br.BlockTableRecord, OpenMode.ForRead)) != null;
                    if (found) { if (hasDetail) break; continue; }
                    string n = PartCollector.GetName(tr, br);
                    if (n == null || !n.EndsWith("_Assy", StringComparison.Ordinal) || n.Length <= 5) continue;
                    string panel = n.Substring(0, n.Length - 5);
                    if (dir != null && checkedPanels.Add(panel) && Directory.Exists(dir)
                        && Directory.EnumerateFiles(dir, Detail3DMatch.CandidatePattern(n)).Any()) found = true; // tên file 3D = tên block _Assy + "_3D"
                }
                tr.Commit();
            }
            return found;
        }

        [CommandMethod(PlanCommand, CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void DetailPlan() => Run("Detail Plan", doc => Create(doc, false));

        [CommandMethod(ViewCommand, CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void DetailView() => Run("Detail View", doc => Create(doc, true));

        [CommandMethod(SectionCommand, CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void DetailSection() => Run("Section Detail", CreateSection);

        [CommandMethod(UpdateCommand, CommandFlags.Modal)]
        public void DetailUpdate() => Run("Update Details", UpdateDetails, "Nothing was changed.");

        static void Run(string what, Func<Document, string> create, string nothing = "Nothing was drawn.")
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            try { Finish(create(doc), false); }
            catch (StopException stop) { Finish(stop.Message, true); }
            catch (System.Exception ex)
            {
                // Lỗi lập trình: stack trace đầy đủ ra dòng lệnh; transaction chưa commit ⇒ không để lại gì
                try { doc.Editor.WriteMessage($"\n[MCG_3DPanel] {what} error detail:\n" + ex + "\n"); } catch { }
                Finish($"{what} failed: {ex.Message} (details on the command line). {nothing}", true);
            }
        }

        /// <summary>Trả status; lỗi / huỷ người dùng cần biết ⇒ <see cref="StopException"/>.</summary>
        static string Create(Document doc, bool view)
        {
            Editor ed = doc.Editor;
            Database db = doc.Database;
            string what = view ? "Detail View" : "Detail Plan";
            string cancelled = what + " cancelled. Nothing was drawn.";

            // 1. Biên AM_7 (chọn trước hoặc hỏi)
            ObjectId boundaryId = PickBoundary(ed, view, out DetailRegion region);
            if (boundaryId.IsNull) throw new StopException(cancelled);

            var missing = DetailWriter.Missing(db);
            if (missing.Count > 0)
                throw new StopException($"{what} stopped; the drawing is missing: {string.Join(", ", missing)}. Nothing was drawn.");

            // 2. _Assy chứa tâm vùng
            P2 centre = DetailCommandRules.Centre(region);
            ObjectId assyId = FindAssy(db, centre, out string assyName);
            if (assyId.IsNull) throw new StopException(view ? "The boundary is not on a panel." : "The rectangle is not on a panel.");
            string panel = assyName.Substring(0, assyName.Length - "_Assy".Length);

            // 3. File 3D + kiểm 3D cũ
            var notes = new List<string>();
            using (Detail3DSource src = Open3D(doc, assyId, panel, what, cancelled, notes, out string file3D))
            {
                // 4. Số DET
                int n = DetailNaming.NextDetailNumber(panel, BlockNames(db));

                // Detail View: tỉ lệ + tên (hỏi TRƯỚC khi cắt — chưa ghi gì)
                double scale = 1;
                string title = DetailNaming.DetailTitle(n);
                if (view)
                {
                    scale = AskScale(ed) ?? throw new StopException(cancelled);
                    string typed = AskName(ed, n) ?? throw new StopException(cancelled);
                    title = DetailCommandRules.ViewTitle(typed, n);
                }
                string shown = title.StartsWith("%%U", StringComparison.OrdinalIgnoreCase) ? title.Substring(3) : title;

                // 5. Cắt (transaction riêng, net-empty) TRƯỚC transaction ghi
                SectionResult result = null;
                try
                {
                    result = SectionEngine.Plan(db, src, region);
                    if (result.Cut.Count == 0 && result.Visible.Count == 0)
                        throw new StopException("No structure inside the boundary. Nothing was drawn.");
                    TopPlateResult top = SectionEngine.TopPlateOutline(db, src, region);

                    // 6. MỘT transaction: không commit (Esc / lỗi) ⇒ huỷ hết
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        var assyRef = (BlockReference)tr.GetObject(assyId, OpenMode.ForRead);
                        var boundary = (Entity)tr.GetObject(boundaryId, OpenMode.ForWrite, false, true);   // kể cả layer khoá
                        var rec = new DetailRecord
                        {
                            Kind = view ? DetailKind.View : DetailKind.Plan,
                            Panel = panel,
                            Number = n,
                            AssyRefHandle = assyId.Handle.ToString(),
                            BoundaryHandle = boundaryId.Handle.ToString(),
                            Region = DetailCommandRules.RegionData(region),
                            Circle = region.IsCircle,
                            Scale = scale,
                            Title = title,
                            BaseX = centre.X,
                            BaseY = centre.Y,
                        };

                        List<Entity> twoD = DetailComposer.TwoDItems(tr, assyRef, region, result.Warnings);
                        List<Entity> ents;
                        try { ents = DetailComposer.PlanEntities(result, top.Loops, twoD, centre, region); }
                        catch
                        {
                            // PlanEntities chỉ giải phóng phần đã nhận ⇒ phần còn lại do ta giải phóng
                            foreach (var e in twoD) if (!e.IsDisposed && e.ObjectId.IsNull) e.Dispose();
                            throw;
                        }

                        ObjectId btrId = DetailWriter.DefineBlock(tr, db, DetailNaming.DetailBlock(panel, n), ents, rec);
                        if (!DetailWriter.Place(ed, tr, btrId, scale, $"Insertion point of {shown}:", out BlockReference br))
                            throw new StopException(cancelled);   // tr chưa commit ⇒ huỷ block, RegApp…
                        DetailWriter.Tag(br, rec);
                        DetailWriter.AddTitle(tr, br, title);
                        AddLabel(tr, db, region, n, rec);
                        DetailWriter.Tag(boundary, rec);
                        tr.Commit();
                    }

                    notes.AddRange(result.Warnings);
                    notes.AddRange(top.Warnings);
                }
                finally { result?.DisposeCurves(); }

                string tail = DetailCommandRules.Notes(notes);
                return $"{shown} created from {file3D}." + (tail.Length > 0 ? " " + tail : "");
            }
        }

        /// <summary>Mở file 3D của panel + kiểm 3D cũ (hỏi Yes/No). Huỷ ⇒ <see cref="StopException"/>; ghi chú 3D cũ vào
        /// <paramref name="notes"/>.</summary>
        static Detail3DSource Open3D(Document doc, ObjectId assyId, string panel, string what, string cancelled,
            List<string> notes, out string file3D)
        {
            Editor ed = doc.Editor;
            Detail3DSource src = Detail3DSource.Open(doc.Database, doc.Name, assyId, paths => Choose(ed, paths), out string why);
            if (src == null) throw new StopException(why == "Cancelled." ? cancelled : why + " Nothing was drawn.");
            try
            {
                file3D = AskStale(src, panel, what, cancelled, notes);
                return src;
            }
            catch { src.Dispose(); throw; }
        }

        /// <summary>Kiểm 3D cũ của <paramref name="src"/>: hỏi Yes/No (No ⇒ <see cref="StopException"/>), ghi chú vào
        /// <paramref name="notes"/>. Trả tên file 3D.</summary>
        static string AskStale(Detail3DSource src, string panel, string what, string cancelled, List<string> notes)
        {
            string file3D = Path.GetFileName(src.FilePath);
            string question = DetailCommandRules.StaleQuestion(src.Stale, file3D, panel);
            if (question != null)
            {
                var answer = System.Windows.MessageBox.Show(question, what, System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxResult.No);
                if (answer != System.Windows.MessageBoxResult.Yes) throw new StopException(cancelled);
                notes.Add(DetailCommandRules.StaleNote(src.Stale, file3D));
            }
            return file3D;
        }

        // ─── Section Detail ────────────────────────────────────────────────────

        /// <summary>Section Detail: chọn DET (Detail Plan) → đầu/cuối đường cắt (bắt ngang/dọc) → bên nhìn → cắt → 1 transaction:
        /// ký hiệu vào block DET, định nghĩa &lt;panel&gt;_DET.n_nL-nL, kéo-thả chèn, tiêu đề. Esc / lỗi ⇒ không để lại gì.</summary>
        static string CreateSection(Document doc)
        {
            Editor ed = doc.Editor;
            Database db = doc.Database;
            const string what = "Section Detail";
            const string cancelled = what + " cancelled. Nothing was drawn.";

            // 1. Reference DET của Detail Plan
            ObjectId detRefId = PickDetailPlan(ed, out DetailRecord det, out DetailRegion region);
            if (detRefId.IsNull) throw new StopException(cancelled);

            var missing = DetailWriter.Missing(db);
            if (missing.Count > 0)
                throw new StopException($"{what} stopped; the drawing is missing: {string.Join(", ", missing)}. Nothing was drawn.");

            string panel = det.Panel;
            int n = det.Number;
            ObjectId assyId = AssyOf(db, det);
            if (assyId.IsNull) throw new StopException($"The {panel}_Assy block of DET.{n} is no longer in this drawing. Nothing was drawn.");

            var notes = new List<string>();
            using (Detail3DSource src = Open3D(doc, assyId, panel, what, cancelled, notes, out string file3D))
            {
                // 2–3. Đường cắt + bên nhìn (toạ độ plan)
                SectionFrame f = AskCut(ed, db, detRefId, det, region, out P2 cutA, out P2 cutB, out P2 side)
                    ?? throw new StopException(cancelled);

                // 4. Chữ section
                string letter = DetailNaming.NextSectionLetter(UsedSectionLetters(db, panel, n));
                string title = DetailNaming.SectionTitle(n, letter, f.Vertical);

                // 5. Cắt (transaction riêng, net-empty) TRƯỚC transaction ghi
                SectionResult result = null;
                try
                {
                    result = SectionEngine.Section(db, src, region, f);
                    if (result.Cut.Count == 0 && result.Visible.Count == 0)
                        throw new StopException("The cut line crosses no structure. Nothing was drawn.");

                    WithContinuousBlock(db, DetailComposer.ContinuousMembers(result).Count > 0, what, "Nothing was drawn.", notes, () =>
                    {
                        // 6. MỘT transaction: không commit (Esc / lỗi) ⇒ huỷ hết, kể cả ký hiệu đã thêm vào block DET
                        using (Transaction tr = db.TransactionManager.StartTransaction())
                        {
                            var detRef = (BlockReference)tr.GetObject(detRefId, OpenMode.ForRead);
                            var rec = new DetailRecord
                            {
                                Kind = DetailKind.Section,
                                Panel = panel,
                                Number = n,
                                Letter = letter,
                                AssyRefHandle = assyId.Handle.ToString(),
                                BoundaryHandle = det.BoundaryHandle,
                                Region = det.Region,
                                Circle = det.Circle,
                                CutA = new[] { cutA.X, cutA.Y },
                                CutB = new[] { cutB.X, cutB.Y },
                                Side = new[] { side.X, side.Y },
                                Scale = 1,
                                Title = title,
                            };

                            // Ký hiệu nA trong block DET (toạ độ cục bộ = plan − gốc DET)
                            var marks = DetailComposer.SectionMarks(f, n, letter, new P2(det.BaseX, det.BaseY));
                            DetailWriter.AddToBlock(tr, detRef.BlockTableRecord, marks, rec);

                            List<Entity> ents = DetailComposer.SectionEntities(result, f, out P2 basePt);
                            rec.BaseX = basePt.X;   // gốc block section, toạ độ section
                            rec.BaseY = basePt.Y;
                            ObjectId btrId = DetailWriter.DefineBlock(tr, db, DetailNaming.SectionBlock(panel, n, letter), ents, rec);
                            tr.TransactionManager.QueueForGraphicsFlush();
                            doc.TransactionManager.FlushGraphics();   // ký hiệu hiện trên DET trong lúc kéo-thả
                            if (!DetailWriter.Place(ed, tr, btrId, 1, $"Insertion point of {title}:", out BlockReference br))
                                throw new StopException(cancelled);   // tr chưa commit ⇒ huỷ ký hiệu + block section
                            DetailWriter.Tag(br, rec);
                            DetailWriter.AddTitle(tr, br, title);
                            tr.Commit();
                        }
                        // Sau khi ghi: SectionEntities cũng đẩy cảnh báo (thiếu amzigzag) vào result.Warnings
                        notes.AddRange(result.Warnings);
                    });
                }
                finally { result?.DisposeCurves(); }

                string tail = DetailCommandRules.Notes(notes);
                return $"{title} created from {file3D}." + (tail.Length > 0 ? " " + tail : "");
            }
        }

        /// <summary>Reference DET của Detail Plan (chọn trước, rồi hỏi lại tới khi đúng). Esc ⇒ Null.</summary>
        static ObjectId PickDetailPlan(Editor ed, out DetailRecord rec, out DetailRegion region)
        {
            rec = null; region = null;
            PromptSelectionResult pre = ed.SelectImplied();
            if (pre.Status == PromptStatus.OK && pre.Value != null)
            {
                ed.SetImpliedSelection(new ObjectId[0]);
                if (pre.Value.Count == 1)
                {
                    ObjectId id = pre.Value[0].ObjectId;
                    string why = CheckDetailPlan(id, out rec, out region);
                    if (why == null) return id;
                    ed.WriteMessage("\n" + why);
                }
                else ed.WriteMessage("\nSelect only one Detail Plan.");
            }

            var opt = new PromptEntityOptions("\nSelect the Detail Plan (DET block) to cut: ");
            opt.SetRejectMessage("\nSelect a Detail Plan block.");
            opt.AddAllowedClass(typeof(BlockReference), true);
            while (true)
            {
                PromptEntityResult r = ed.GetEntity(opt);
                if (r.Status != PromptStatus.OK) return ObjectId.Null;
                string why = CheckDetailPlan(r.ObjectId, out rec, out region);
                if (why == null) return r.ObjectId;
                ed.WriteMessage("\n" + why);
            }
        }

        /// <summary>Null ⇒ là reference DET của Detail Plan (bản ghi + vùng đã điền); ngược lại là lý do để hỏi lại.</summary>
        static string CheckDetailPlan(ObjectId id, out DetailRecord rec, out DetailRegion region)
        {
            rec = null; region = null;
            using (Transaction tr = id.Database.TransactionManager.StartOpenCloseTransaction())
            {
                const string other = "Select a Detail Plan block (DET.n) made by the tool.";
                if (!(tr.GetObject(id, OpenMode.ForRead) is BlockReference br)) return other;
                // Bản ghi đầy đủ trên reference; thiếu ⇒ bản ghi trên định nghĩa block
                DetailRecord r = DetailWriter.Read(br) ?? DetailWriter.Read(tr.GetObject(br.BlockTableRecord, OpenMode.ForRead));
                if (r == null) return other;
                if (r.Kind == DetailKind.View) return "Sections are only made on Detail Plans.";
                if (r.Kind != DetailKind.Plan) return other;
                region = DetailCommandRules.RegionFromData(r.Region, r.Circle);
                if (region == null) return $"The detail data of DET.{r.Number} is damaged (no valid boundary). Select another Detail Plan.";
                rec = r;
                return null;
            }
        }

        /// <summary>_Assy của DET: theo handle trong bản ghi (đúng tên &lt;panel&gt;_Assy), mất ⇒ tìm theo gốc DET trên plan.</summary>
        static ObjectId AssyOf(Database db, DetailRecord det)
        {
            string want = det.Panel + "_Assy";
            if (long.TryParse(det.AssyRefHandle, System.Globalization.NumberStyles.HexNumber, null, out long h)
                && db.TryGetObjectId(new Handle(h), out ObjectId id) && !id.IsErased)
            {
                using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    bool ok = tr.GetObject(id, OpenMode.ForRead) is BlockReference br && PartCollector.GetName(tr, br) == want;
                    tr.Commit();
                    if (ok) return id;
                }
            }
            ObjectId found = FindAssy(db, new P2(det.BaseX, det.BaseY), out string name);
            return name == want ? found : ObjectId.Null;
        }

        /// <summary>Hỏi đầu, cuối (rubber band từ đầu, bắt ngang/dọc), bên nhìn; toạ độ plan. Khung sai ⇒ nói lý do, hỏi lại
        /// điểm cuối hoặc bên nhìn. Esc ở bất kỳ câu nào ⇒ null.</summary>
        static SectionFrame AskCut(Editor ed, Database db, ObjectId detRefId, DetailRecord det, DetailRegion region,
            out P2 a, out P2 b, out P2 side)
        {
            a = b = side = default;
            Matrix3d toLocal;
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                toLocal = ((BlockReference)tr.GetObject(detRefId, OpenMode.ForRead)).BlockTransform.Inverse();
                tr.Commit();
            }
            // WCS → cục bộ block DET → plan (+ gốc DET)
            Matrix3d toPlan = Matrix3d.Displacement(new Vector3d(det.BaseX, det.BaseY, 0)) * toLocal;
            Matrix3d ucs = ed.CurrentUserCoordinateSystem;

            P2 Plan(Point3d wcs) { Point3d p = wcs.TransformBy(toPlan); return new P2(p.X, p.Y); }
            Point3d? Ask(string prompt, Point3d? fromWcs)
            {
                var opt = new PromptPointOptions("\n" + prompt) { AllowNone = false };
                if (fromWcs.HasValue)
                {
                    opt.UseBasePoint = true;
                    opt.UseDashedLine = true;
                    opt.BasePoint = fromWcs.Value.TransformBy(ucs.Inverse());
                }
                PromptPointResult r = ed.GetPoint(opt);
                return r.Status == PromptStatus.OK ? r.Value.TransformBy(ucs) : (Point3d?)null;
            }

            Point3d? startW = Ask("Start point of the cut line:", null);
            if (startW == null) return null;
            a = Plan(startW.Value);
            while (true)
            {
                Point3d? endW = Ask("End point of the cut line:", startW);
                if (endW == null) return null;
                b = SectionFrame.Snap(a, Plan(endW.Value));
                // Đường cắt sai (không dài / không qua vùng) ⇒ hỏi lại điểm cuối ngay, chưa hỏi bên nhìn
                string lineWhy = DetailCommandRules.CutLineProblem(a, b, region);
                if (lineWhy != null) { ed.WriteMessage("\n" + lineWhy); continue; }
                while (true)
                {
                    Point3d? sideW = Ask("Viewing side (pick a point on the side to look at):", null);
                    if (sideW == null) return null;
                    side = Plan(sideW.Value);
                    string why = DetailCommandRules.FrameProblem(a, b, side, region, out bool endAgain);
                    if (why == null) return SectionFrame.From(a, b, side, region);
                    ed.WriteMessage("\n" + why);
                    if (endAgain) break;
                }
            }
        }

        /// <summary>Chữ section đã dùng của DET n: bản ghi Section trên các định nghĩa block + tên block nL-nL.</summary>
        static IEnumerable<string> UsedSectionLetters(Database db, string panel, int n)
        {
            var recs = new List<DetailRecord>();
            var names = new List<string>();
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId id in bt)
                {
                    if (id.IsErased) continue;
                    var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    names.Add(btr.Name);
                    var r = DetailWriter.Read(btr);
                    if (r != null) recs.Add(r);
                }
                tr.Commit();
            }
            return DetailCommandRules.UsedLetters(panel, n, recs, names);
        }

        /// <summary>Block "Continious symbol" chỉ cần khi có tấm cắt liên tục (<paramref name="needed"/>). Thiếu ⇒ bổ sung (bản vẽ →
        /// CAS.dwt → tự vẽ) TRƯỚC, commit riêng như Plan View, rồi chạy <paramref name="write"/> (transaction ghi duy nhất, tự commit).
        /// <paramref name="write"/> ném lỗi / huỷ ⇒ xoá mọi bản ghi lần bổ sung đã tạo (handle ≥ Handseed trước khi bổ sung).
        /// Thành công ⇒ ghi chú nguồn block vào <paramref name="notes"/>.</summary>
        static void WithContinuousBlock(Database db, bool needed, string what, string nothing, List<string> notes, Action write)
        {
            var blocks = new PlanViewBlockSource.Plan();
            if (needed) blocks = PlanViewBlockSource.Prepare(db, new[] { DetailComposer.ContinuousBlock });
            if (blocks.Unavailable.Count > 0)
                throw new StopException($"{what} stopped; the drawing is missing: block {DetailComposer.ContinuousBlock}. {nothing}");
            bool added = blocks.FromTemplate.Count + blocks.Drawn.Count > 0;
            long seed = db.Handseed.Value;
            bool done = false;
            try
            {
                if (added)
                {
                    PlanViewBlockSource.Apply(db, blocks);
                    if (PlanViewBlockSource.NotLive(db, new[] { DetailComposer.ContinuousBlock }).Count > 0)
                        throw new StopException($"{what} stopped; block {DetailComposer.ContinuousBlock} could not be added. {nothing}");
                }
                write();
                done = true;
            }
            finally
            {
                if (added && !done) RemoveCreatedSince(db, seed);
            }
            if (blocks.FromTemplate.Count > 0) notes.Add($"block {DetailComposer.ContinuousBlock} added from CAS.dwt");
            if (blocks.Drawn.Count > 0) notes.Add($"block {DetailComposer.ContinuousBlock} drawn by the tool");
        }

        /// <summary>Huỷ phần bổ sung block khi lần chạy không commit: xoá mọi bản ghi bảng ký hiệu / mục dictionary có handle ≥
        /// <paramref name="seed"/> (chỉ của lần chạy này, không bao giờ bản ghi có sẵn). Block trước (kéo theo entity bên trong), rồi
        /// lặp Purge cho phần còn lại (dim style, text style, linetype, layer… chỉ xoá khi không còn ai tham chiếu).
        /// Còn sót ⇒ liệt kê trên dòng lệnh.</summary>
        static void RemoveCreatedSince(Database db, long seed)
        {
            var leftover = new List<string>();
            try
            {
                var blocks = new List<ObjectId>();
                var others = new List<ObjectId>();
                using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    ObjectId[] tables =
                    {
                        db.BlockTableId, db.DimStyleTableId, db.TextStyleTableId, db.LinetypeTableId, db.LayerTableId, db.RegAppTableId,
                    };
                    foreach (ObjectId t in tables)
                        foreach (ObjectId id in (SymbolTable)tr.GetObject(t, OpenMode.ForRead))
                            if (!id.IsErased && SectionCleanupRules.CreatedThisRun(seed, id.Handle.Value))
                                (t == db.BlockTableId ? blocks : others).Add(id);
                    CollectNew(tr, db.NamedObjectsDictionaryId, seed, others, 0);
                    tr.Commit();
                }
                if (blocks.Count + others.Count == 0) return;

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // 2 lượt: xoá entity của MỌI block mới trước (gỡ reference block lồng), rồi mới xoá định nghĩa —
                    // thứ tự bảng block theo tên nên block lồng có thể đứng trước block cha
                    foreach (ObjectId id in blocks)
                        foreach (ObjectId e in (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead))
                            tr.GetObject(e, OpenMode.ForWrite, false, true).Erase();
                    foreach (ObjectId id in blocks)
                    {
                        // openErased: định nghĩa đã bị xoá (vd theo block cha) không làm hỏng lượt dọn
                        var b = tr.GetObject(id, OpenMode.ForWrite, true);
                        if (!b.IsErased) b.Erase();
                    }
                    tr.Commit();
                }
                // Purge lặp: xoá cái không còn tham chiếu sẽ giải phóng cái nó tham chiếu (dim style → text style → …)
                for (int round = 0; round < 8 && others.Count > 0; round++)
                {
                    var free = new ObjectIdCollection(others.ToArray());
                    db.Purge(free);
                    if (free.Count == 0) break;
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        foreach (ObjectId id in free) tr.GetObject(id, OpenMode.ForWrite).Erase();
                        tr.Commit();
                    }
                    var gone = new HashSet<ObjectId>(free.Cast<ObjectId>());
                    others.RemoveAll(gone.Contains);
                }
                using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    foreach (ObjectId id in others)
                    {
                        var o = tr.GetObject(id, OpenMode.ForRead);
                        leftover.Add(o is SymbolTableRecord s ? s.Name : o.GetRXClass().DxfName + " " + id.Handle);
                    }
                    tr.Commit();
                }
            }
            catch (System.Exception ex) { leftover.Add("(" + ex.Message + ")"); }
            if (leftover.Count > 0)
                try { Application.DocumentManager.MdiActiveDocument?.Editor?.WriteMessage(
                    $"\n[MCG_3DPanel] Could not remove what was added with {DetailComposer.ContinuousBlock}: {string.Join(", ", leftover)}\n"); } catch { }
        }

        /// <summary>Mục mới (handle ≥ seed) trong dictionary và dictionary con (mục mới là dictionary ⇒ đi vào trong, rồi tự nó).</summary>
        static void CollectNew(Transaction tr, ObjectId dictId, long seed, List<ObjectId> into, int depth)
        {
            if (depth > 3 || !(tr.GetObject(dictId, OpenMode.ForRead) is DBDictionary dict)) return;
            foreach (DBDictionaryEntry e in dict)
            {
                if (e.Value.IsErased) continue;
                bool isNew = SectionCleanupRules.CreatedThisRun(seed, e.Value.Handle.Value);
                if (tr.GetObject(e.Value, OpenMode.ForRead) is DBDictionary) CollectNew(tr, e.Value, seed, into, depth + 1);
                if (isNew) into.Add(e.Value);
            }
        }

        // ─── Update Details ────────────────────────────────────────────────────

        /// <summary>1 block Detail cần dựng lại (mọi reference của cùng 1 định nghĩa).</summary>
        sealed class UpdateItem
        {
            public ObjectId BtrId;
            public readonly List<ObjectId> Refs = new List<ObjectId>();
            public DetailRecord Rec;            // bản ghi hiện có (định nghĩa block, thiếu ⇒ reference)
            public DetailRecord New;            // bản ghi sau Update
            public DetailRegion Region;         // Plan/View: vùng dùng; Section: vùng hiện tại của DET
            public DetailRegion OldRegion;      // vùng trong bản ghi (dời nhãn / gốc section)
            public SectionFrame Frame;          // Section; null ⇒ đường cắt không còn qua vùng
            public SectionResult Result;
            public TopPlateResult Top;
            public bool Empty;                  // Section: để block rỗng
            public ObjectId AssyId;
        }

        /// <summary>Chữ trong Model Space mang bản ghi (nhãn DET.n = DBText, tiêu đề = MText).</summary>
        sealed class TaggedText
        {
            public ObjectId Id;
            public DetailRecord Rec;
            public bool IsDBText;
            public string Text;
        }

        /// <summary>Update Details: dựng lại nội dung mọi block Detail (Plan / View / Section) do tool tạo từ file 3D hiện tại và
        /// biên AM_7 hiện tại; giữ reference, tiêu đề, kích thước người dùng; dời nhãn DET.n theo biên. Esc / No / lỗi ⇒ không đổi gì.</summary>
        static string UpdateDetails(Document doc)
        {
            Editor ed = doc.Editor;
            Database db = doc.Database;
            const string what = "Update Details";
            const string nothing = "Nothing was changed.";
            const string cancelled = what + " cancelled. " + nothing;

            // 1. Block Detail trong Model Space (theo định nghĩa) + chữ mang bản ghi + mọi bản ghi Section (ký hiệu trên DET)
            var items = new List<UpdateItem>();
            var texts = new List<TaggedText>();
            var sectionRecs = new List<DetailRecord>();
            Collect(db, items, texts, sectionRecs);
            if (items.Count == 0) throw new StopException("There are no details made by the tool in this drawing. " + nothing);

            var missing = DetailWriter.Missing(db);
            if (missing.Count > 0)
                throw new StopException($"{what} stopped; the drawing is missing: {string.Join(", ", missing)}. {nothing}");

            var notes = new List<string>();
            var sources = new Dictionary<string, Detail3DSource>(StringComparer.Ordinal);
            try
            {
                // 2. File 3D mỗi panel 1 lần (+ hỏi 3D cũ 1 lần). Không có _Assy / file 3D ⇒ bỏ panel đó, ghi chú
                foreach (var g in items.GroupBy(i => i.Rec.Panel, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
                {
                    string panel = g.Key;
                    DetailRecord any = g.FirstOrDefault(i => i.Rec.Kind != DetailKind.Section)?.Rec ?? g.First().Rec;
                    ObjectId assyId = AssyOf(db, any);
                    if (assyId.IsNull)
                    {
                        notes.Add($"the {panel}_Assy block is no longer in this drawing; its details were not updated");
                        continue;
                    }
                    Detail3DSource src = Detail3DSource.Open(db, doc.Name, assyId, paths => Choose(ed, paths), out string why);
                    if (src == null)
                    {
                        if (why == "Cancelled.") throw new StopException(cancelled);
                        notes.Add($"{panel}: {why?.TrimEnd('.')}; its details were not updated");
                        continue;
                    }
                    sources[panel] = src;
                    AskStale(src, panel, what, cancelled, notes);
                    foreach (var i in g) i.AssyId = assyId;
                }
                items.RemoveAll(i => !sources.ContainsKey(i.Rec.Panel));

                // Vùng: Plan/View theo biên (còn sống) hoặc bản ghi; Section theo vùng hiện tại của DET
                items = DetailUpdateRules.Order(items, i => i.Rec);
                var damaged = new List<UpdateItem>();
                foreach (var i in items.Where(i => i.Rec.Kind != DetailKind.Section))
                {
                    i.OldRegion = DetailCommandRules.RegionFromData(i.Rec.Region, i.Rec.Circle);
                    i.Region = CurrentRegion(db, i.Rec, i.OldRegion, notes);
                    if (i.Region == null) damaged.Add(i);
                }
                foreach (var i in items.Where(i => i.Rec.Kind == DetailKind.Section))
                {
                    i.OldRegion = DetailCommandRules.RegionFromData(i.Rec.Region, i.Rec.Circle);
                    UpdateItem det = items.FirstOrDefault(d => d.Rec.Kind == DetailKind.Plan && d.Region != null
                        && d.Rec.Number == i.Rec.Number && d.Rec.Panel == i.Rec.Panel);
                    i.Region = det != null ? det.Region : CurrentRegion(db, i.Rec, i.OldRegion, notes);
                    if (i.Region == null) { damaged.Add(i); continue; }
                    i.Frame = SectionFrame.From(P(i.Rec.CutA), P(i.Rec.CutB), P(i.Rec.Side), i.Region);
                }
                foreach (var i in damaged)
                {
                    items.Remove(i);
                    notes.Add($"the detail data of {DetailUpdateRules.DisplayName(i.Rec)} is damaged (no valid boundary); it was not updated");
                }
                if (items.Count == 0)
                    throw new StopException("No detail could be updated. " + DetailCommandRules.Notes(notes) + " " + nothing);

                // 3. Xác nhận
                var answer = System.Windows.MessageBox.Show(DetailUpdateRules.ConfirmQuestion(items.Count), what,
                    System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Question,
                    System.Windows.MessageBoxResult.Cancel);
                if (answer != System.Windows.MessageBoxResult.OK) throw new StopException(cancelled);

                var changed = new List<string>();
                try
                {
                    // 4a. Cắt mọi detail (transaction riêng, net-empty) TRƯỚC transaction ghi
                    foreach (var i in items)
                    {
                        Detail3DSource src = sources[i.Rec.Panel];
                        if (i.Rec.Kind == DetailKind.Section)
                        {
                            if (i.Frame != null) i.Result = SectionEngine.Section(db, src, i.Region, i.Frame);
                            i.Empty = i.Frame == null || i.Result.Cut.Count == 0 && i.Result.Visible.Count == 0;
                            if (i.Empty) notes.Add(DetailUpdateRules.EmptySectionNote(i.Rec));
                        }
                        else
                        {
                            i.Result = SectionEngine.Plan(db, src, i.Region);
                            i.Top = SectionEngine.TopPlateOutline(db, src, i.Region);
                            if (i.Result.Cut.Count == 0 && i.Result.Visible.Count == 0)
                                notes.Add($"{DetailUpdateRules.DisplayName(i.Rec)}: no structure inside the boundary");
                        }
                    }
                    // 4b. MỘT transaction cho tất cả: không commit (lỗi) ⇒ không đổi gì; block ký hiệu vừa bổ sung bị xoá
                    bool needSymbol = items.Any(i => !i.Empty && i.Rec.Kind == DetailKind.Section
                        && DetailComposer.ContinuousMembers(i.Result).Count > 0);
                    WithContinuousBlock(db, needSymbol, what, nothing, notes, () =>
                    {
                        using (Transaction tr = db.TransactionManager.StartTransaction())
                        {
                            foreach (var i in items)
                            {
                                bool diff = i.Rec.Kind == DetailKind.Section
                                    ? RebuildSection(tr, db, i)
                                    : RebuildPlan(tr, db, i, items, sectionRecs);
                                if (diff) changed.Add(DetailUpdateRules.DisplayName(i.Rec));
                                RetagTexts(tr, i, texts);
                            }
                            tr.Commit();
                        }
                        // Sau khi ghi: PlanEntities / SectionEntities cũng đẩy cảnh báo (thiếu amzigzag) vào Warnings
                        foreach (var i in items)
                        {
                            if (i.Result != null) notes.AddRange(i.Result.Warnings);
                            if (i.Top != null) notes.AddRange(i.Top.Warnings);
                        }
                    });
                }
                finally
                {
                    foreach (var i in items) i.Result?.DisposeCurves();
                }

                // 5. Status
                return DetailUpdateRules.Status(items.Count, changed, notes);
            }
            finally
            {
                foreach (var s in sources.Values) s.Dispose();
            }
        }

        static P2 P(double[] a) => new P2(a[0], a[1]);

        /// <summary>Reference Model Space mang bản ghi (gom theo định nghĩa block), chữ Model Space mang bản ghi, bản ghi Section trên
        /// mọi định nghĩa block.</summary>
        static void Collect(Database db, List<UpdateItem> items, List<TaggedText> texts, List<DetailRecord> sectionRecs)
        {
            var byBtr = new Dictionary<ObjectId, UpdateItem>();
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    DBObject o = tr.GetObject(id, OpenMode.ForRead);
                    if (o is BlockReference br)
                    {
                        if (byBtr.TryGetValue(br.BlockTableRecord, out UpdateItem known)) { known.Refs.Add(id); continue; }
                        DetailRecord rec = DetailWriter.Read(tr.GetObject(br.BlockTableRecord, OpenMode.ForRead)) ?? DetailWriter.Read(br);
                        if (rec == null) continue;
                        var item = new UpdateItem { BtrId = br.BlockTableRecord, Rec = rec };
                        item.Refs.Add(id);
                        byBtr[br.BlockTableRecord] = item;
                        items.Add(item);
                    }
                    else if (o is DBText t)
                    {
                        DetailRecord rec = DetailWriter.Read(t);
                        if (rec != null) texts.Add(new TaggedText { Id = id, Rec = rec, IsDBText = true, Text = t.TextString });
                    }
                    else if (o is MText m)
                    {
                        DetailRecord rec = DetailWriter.Read(m);
                        if (rec != null) texts.Add(new TaggedText { Id = id, Rec = rec, IsDBText = false, Text = m.Contents });
                    }
                }
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId id in bt)
                {
                    if (id.IsErased) continue;
                    DetailRecord r = DetailWriter.Read(tr.GetObject(id, OpenMode.ForRead));
                    if (r != null && r.Kind == DetailKind.Section) sectionRecs.Add(r);
                }
                tr.Commit();
            }
            // Section có reference mà định nghĩa thiếu bản ghi: vẫn cần ký hiệu trên DET
            foreach (var i in items)
                if (i.Rec.Kind == DetailKind.Section && !sectionRecs.Any(r => DetailUpdateRules.SameDetail(r, i.Rec))) sectionRecs.Add(i.Rec);
        }

        /// <summary>Vùng hiện tại theo biên của bản ghi: biên còn sống + hợp lệ ⇒ vùng của biên; bị xoá / không dùng được ⇒
        /// <paramref name="stored"/> + ghi chú. Bản ghi hỏng ⇒ null.</summary>
        static DetailRegion CurrentRegion(Database db, DetailRecord rec, DetailRegion stored, List<string> notes)
        {
            ObjectId bid = ObjectId.Null;
            if (long.TryParse(rec.BoundaryHandle, System.Globalization.NumberStyles.HexNumber, null, out long h)
                && db.TryGetObjectId(new Handle(h), out ObjectId id) && !id.IsErased) bid = id;
            if (bid.IsNull)
            {
                if (stored != null) notes.Add(DetailUpdateRules.ErasedNote(rec.Number));
                return stored;
            }
            string why = Check(bid, rec.Kind == DetailKind.View, out DetailRegion region);
            if (why == null) return region;
            if (stored != null) notes.Add(DetailUpdateRules.UnusableNote(rec.Number, why.TrimEnd('.')));
            return stored;
        }

        /// <summary>Dựng lại block DET (Plan/View) theo vùng hiện tại, giữ gốc block (reference không xê dịch), thêm lại ký hiệu của
        /// mọi section của DET này. Trả true nếu nội dung đổi.</summary>
        static bool RebuildPlan(Transaction tr, Database db, UpdateItem i, List<UpdateItem> items, List<DetailRecord> sectionRecs)
        {
            var btr = (BlockTableRecord)tr.GetObject(i.BtrId, OpenMode.ForRead);
            string before = Signature(tr, btr);
            P2 basePt = new P2(i.Rec.BaseX, i.Rec.BaseY);

            i.New = DetailRecord.FromJson(i.Rec.ToJson());
            i.New.Region = DetailCommandRules.RegionData(i.Region);
            i.New.Circle = i.Region.IsCircle;
            i.New.AssyRefHandle = i.AssyId.Handle.ToString();

            var assyRef = (BlockReference)tr.GetObject(i.AssyId, OpenMode.ForRead);
            List<Entity> twoD = DetailComposer.TwoDItems(tr, assyRef, i.Region, i.Result.Warnings);
            List<Entity> ents;
            try { ents = DetailComposer.PlanEntities(i.Result, i.Top?.Loops, twoD, basePt, i.Region); }
            catch
            {
                foreach (var e in twoD) if (!e.IsDisposed && e.ObjectId.IsNull) e.Dispose();
                throw;
            }
            // Định nghĩa thiếu bản ghi (chỉ reference có) ⇒ gắn trước để DefineBlock thay nội dung, không tạo tên _MCG
            if (DetailWriter.Read(btr) == null) DetailWriter.Tag(btr, i.New);
            DetailWriter.DefineBlock(tr, db, btr.Name, ents, i.New);

            // Ký hiệu section trên DET: theo đường cắt đã lưu + vùng mới (gốc DET giữ nguyên)
            foreach (DetailRecord s in DetailUpdateRules.SectionsOf(sectionRecs, i.Rec))
            {
                SectionFrame f = SectionFrame.From(P(s.CutA), P(s.CutB), P(s.Side), i.Region);
                if (f == null) continue;
                DetailWriter.AddToBlock(tr, i.BtrId, DetailComposer.SectionMarks(f, i.Rec.Number, s.Letter, basePt), s);
            }
            Redraw(tr, btr);
            foreach (ObjectId r in i.Refs) DetailWriter.Tag((DBObject)tr.GetObject(r, OpenMode.ForWrite, false, true), i.New);
            return DetailUpdateRules.Changed(before, Signature(tr, btr));
        }

        /// <summary>Dựng lại block section từ đường cắt đã lưu + vùng hiện tại của DET; gốc block chọn để ánh xạ plan → block giữ
        /// nguyên. Đường cắt không còn qua vật liệu ⇒ block rỗng. Trả true nếu nội dung đổi.</summary>
        static bool RebuildSection(Transaction tr, Database db, UpdateItem i)
        {
            var btr = (BlockTableRecord)tr.GetObject(i.BtrId, OpenMode.ForRead);
            string before = Signature(tr, btr);

            i.New = DetailRecord.FromJson(i.Rec.ToJson());
            i.New.Region = DetailCommandRules.RegionData(i.Region);
            i.New.Circle = i.Region.IsCircle;
            i.New.AssyRefHandle = i.AssyId.Handle.ToString();

            var ents = new List<Entity>();
            SectionFrame oldFrame = i.OldRegion == null ? null
                : SectionFrame.From(P(i.Rec.CutA), P(i.Rec.CutB), P(i.Rec.Side), i.OldRegion);
            P2? keep = DetailUpdateRules.SectionBase(new P2(i.Rec.BaseX, i.Rec.BaseY), oldFrame, i.Frame);
            if (keep.HasValue)
            {
                // Có đủ 2 khung ⇒ luôn dời gốc theo vùng, kể cả section rỗng (lần Update sau không lệch)
                i.New.BaseX = keep.Value.X;
                i.New.BaseY = keep.Value.Y;
            }
            if (!i.Empty)
            {
                ents = DetailComposer.SectionEntities(i.Result, i.Frame, out P2 basePt, keep);
                i.New.BaseX = basePt.X;
                i.New.BaseY = basePt.Y;
            }
            if (DetailWriter.Read(btr) == null) DetailWriter.Tag(btr, i.New);
            DetailWriter.DefineBlock(tr, db, btr.Name, ents, i.New);
            Redraw(tr, btr);
            foreach (ObjectId r in i.Refs) DetailWriter.Tag((DBObject)tr.GetObject(r, OpenMode.ForWrite, false, true), i.New);
            return DetailUpdateRules.Changed(before, Signature(tr, btr));
        }

        /// <summary>Tiêu đề (MText cùng detail): chỉ cập nhật bản ghi. Nhãn DET.n (DBText): dời theo góc biên + cập nhật bản ghi.</summary>
        static void RetagTexts(Transaction tr, UpdateItem i, List<TaggedText> texts)
        {
            foreach (var t in texts)
            {
                if (t.IsDBText && DetailUpdateRules.IsLabelOf(t.Rec, t.Text, i.Rec))
                {
                    var label = (DBText)tr.GetObject(t.Id, OpenMode.ForWrite, false, true);
                    if (i.OldRegion != null)
                    {
                        P2 d = DetailUpdateRules.LabelShift(i.OldRegion, i.Region, LabelGap);
                        if (d.Length > 1e-9) label.TransformBy(Matrix3d.Displacement(new Vector3d(d.X, d.Y, 0)));
                    }
                    DetailWriter.Tag(label, i.New);
                }
                else if (!t.IsDBText && DetailUpdateRules.SameDetail(t.Rec, i.Rec))
                    DetailWriter.Tag((DBObject)tr.GetObject(t.Id, OpenMode.ForWrite, false, true), i.New);
            }
        }

        /// <summary>Chữ ký nội dung block: loại + layer + khung bao từng entity.</summary>
        static string Signature(Transaction tr, BlockTableRecord btr)
        {
            var list = new List<(string, string, double[])>();
            foreach (ObjectId id in btr)
            {
                if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead, false, true) is Entity e)) continue;
                double[] ext = null;
                try { var x = e.GeometricExtents; ext = new[] { x.MinPoint.X, x.MinPoint.Y, x.MaxPoint.X, x.MaxPoint.Y }; }
                catch (Autodesk.AutoCAD.Runtime.Exception) { }
                list.Add((e.GetRXClass().DxfName, e.Layer, ext));
            }
            return DetailUpdateRules.Signature(list);
        }

        /// <summary>Báo vẽ lại mọi reference của block (nội dung vừa thay).</summary>
        static void Redraw(Transaction tr, BlockTableRecord btr)
        {
            foreach (ObjectId id in btr.GetBlockReferenceIds(true, false))
                ((Entity)tr.GetObject(id, OpenMode.ForWrite, false, true)).RecordGraphicsModified(true);
        }

        // ─── Chọn biên ─────────────────────────────────────────────────────────

        /// <summary>Biên hợp lệ (chọn trước, rồi hỏi lại tới khi đúng). Esc ⇒ Null.</summary>
        static ObjectId PickBoundary(Editor ed, bool view, out DetailRegion region)
        {
            region = null;
            PromptSelectionResult pre = ed.SelectImplied();
            if (pre.Status == PromptStatus.OK && pre.Value != null)
            {
                ed.SetImpliedSelection(new ObjectId[0]);
                if (pre.Value.Count == 1)
                {
                    ObjectId id = pre.Value[0].ObjectId;
                    string why = Check(id, view, out region);
                    if (why == null) return id;
                    ed.WriteMessage("\n" + why);
                }
                else ed.WriteMessage("\nSelect only one boundary.");
            }

            string prompt = view ? "\nSelect the AM_7 rectangle or circle of the detail: " : "\nSelect the AM_7 rectangle of the detail: ";
            var opt = new PromptEntityOptions(prompt);
            opt.SetRejectMessage(view ? "\nSelect a closed polyline or a circle." : "\nSelect a closed polyline.");
            opt.AddAllowedClass(typeof(Polyline), true);
            if (view) opt.AddAllowedClass(typeof(Circle), true);
            while (true)
            {
                PromptEntityResult r = ed.GetEntity(opt);
                if (r.Status != PromptStatus.OK) return ObjectId.Null;
                string why = Check(r.ObjectId, view, out region);
                if (why == null) return r.ObjectId;
                ed.WriteMessage("\n" + why);
            }
        }

        /// <summary>Null ⇒ hợp lệ (<paramref name="region"/> đã điền); ngược lại là lý do để hỏi lại.</summary>
        static string Check(ObjectId id, bool view, out DetailRegion region)
        {
            region = null;
            using (Transaction tr = id.Database.TransactionManager.StartOpenCloseTransaction())
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                string kinds = view ? "a rectangle or a circle" : "a rectangle";
                if (ent == null) return $"Select {kinds}.";
                if (!string.Equals(ent.Layer, DetailComposer.AM7, StringComparison.OrdinalIgnoreCase))
                    return $"The boundary must be on layer {DetailComposer.AM7} (it is on {ent.Layer}).";
                if (ent is Polyline pl)
                {
                    if (!pl.Normal.IsEqualTo(Vector3d.ZAxis)) return "The polyline is not drawn in the plan (XY) plane.";
                    var v = new List<(double, double, double)>();
                    for (int i = 0; i < pl.NumberOfVertices; i++)
                    {
                        Point2d p = pl.GetPoint2dAt(i);
                        v.Add((p.X, p.Y, pl.GetBulgeAt(i)));
                    }
                    region = DetailRegion.FromPolyline(v, pl.Closed, out string why);
                    if (region == null) return "The polyline " + (why == "has arcs" ? why : "is " + why) + ".";
                    return null;
                }
                if (view && ent is Circle c)
                {
                    if (!c.Normal.IsParallelTo(Vector3d.ZAxis)) return "The circle is not drawn in the plan (XY) plane.";
                    if (c.Radius <= 0) return "The circle has no size.";
                    region = DetailRegion.FromCircle(new P2(c.Center.X, c.Center.Y), c.Radius);
                    return null;
                }
                return $"Select {kinds} (a closed polyline{(view ? " or a circle" : "")}).";
            }
        }

        // ─── Tìm panel / file / tên ────────────────────────────────────────────

        /// <summary>Reference *_Assy trong Model Space có khung bao chứa <paramref name="p"/>; nhiều ⇒ khung nhỏ nhất.</summary>
        static ObjectId FindAssy(Database db, P2 p, out string name)
        {
            name = null;
            ObjectId best = ObjectId.Null;
            double bestArea = double.MaxValue;
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is BlockReference br)) continue;
                    string n = PartCollector.GetName(tr, br);
                    if (n == null || !n.EndsWith("_Assy", StringComparison.Ordinal) || n.Length <= 5) continue;
                    Extents3d ext;
                    try { ext = br.GeometricExtents; }
                    catch (Autodesk.AutoCAD.Runtime.Exception) { continue; }
                    if (p.X < ext.MinPoint.X || p.X > ext.MaxPoint.X || p.Y < ext.MinPoint.Y || p.Y > ext.MaxPoint.Y) continue;
                    double area = (ext.MaxPoint.X - ext.MinPoint.X) * (ext.MaxPoint.Y - ext.MinPoint.Y);
                    if (area < bestArea) { bestArea = area; best = id; name = n; }
                }
                tr.Commit();
            }
            return best;
        }

        static List<string> BlockNames(Database db)
        {
            var names = new List<string>();
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId id in bt)
                    if (!id.IsErased) names.Add(((BlockTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name);
                tr.Commit();
            }
            return names;
        }

        /// <summary>Nhiều file 3D khớp ⇒ liệt kê trên dòng lệnh, gõ số. Esc ⇒ null.</summary>
        static string Choose(Editor ed, IList<string> paths)
        {
            ed.WriteMessage("\nSeveral 3D files match this panel:");
            for (int i = 0; i < paths.Count; i++) ed.WriteMessage($"\n  {i + 1}. {Path.GetFileName(paths[i])}");
            var opt = new PromptIntegerOptions($"\nWhich 3D file [1-{paths.Count}] <1>: ")
            {
                AllowNone = true, AllowZero = false, AllowNegative = false, LowerLimit = 1, UpperLimit = paths.Count,
            };
            PromptIntegerResult r = ed.GetInteger(opt);
            if (r.Status == PromptStatus.None) return paths[0];
            return r.Status == PromptStatus.OK ? paths[r.Value - 1] : null;
        }

        /// <summary>"Scale &lt;1&gt;:" — số dương, Enter ⇒ 1, Esc ⇒ null.</summary>
        static double? AskScale(Editor ed)
        {
            var opt = new PromptDoubleOptions("\nScale <1>: ") { AllowNone = true, AllowZero = false, AllowNegative = false };
            PromptDoubleResult r = ed.GetDouble(opt);
            if (r.Status == PromptStatus.None) return 1;
            return r.Status == PromptStatus.OK ? r.Value : (double?)null;
        }

        /// <summary>"Name &lt;DETAIL n&gt;:" — Enter ⇒ "" (dùng mặc định), Esc ⇒ null.</summary>
        static string AskName(Editor ed, int n)
        {
            var opt = new PromptStringOptions($"\nName <DETAIL {n}>: ") { AllowSpaces = true };
            PromptResult r = ed.GetString(opt);
            if (r.Status == PromptStatus.None) return "";
            return r.Status == PromptStatus.OK ? r.StringResult ?? "" : null;
        }

        // ─── Ghi ───────────────────────────────────────────────────────────────

        /// <summary>Nhãn "DET.n" (DBText AM_1 h150) cạnh góc trên-phải của biên, mang bản ghi MCG3D_DETAIL.</summary>
        static void AddLabel(Transaction tr, Database db, DetailRegion region, int n, DetailRecord rec)
        {
            P2 at = DetailCommandRules.LabelAt(region, LabelGap);
            var t = new DBText();
            t.SetDatabaseDefaults(db);
            t.TextString = DetailNaming.PlanLabel(n);
            t.Height = LabelHeight;
            t.Position = new Point3d(at.X, at.Y, 0);
            t.Layer = LabelLayer;
            var ts = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            ObjectId style = SymbolTables.Live(ts, "STANDARD");
            if (!style.IsNull) t.TextStyleId = style;
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            ms.AppendEntity(t);
            tr.AddNewlyCreatedDBObject(t, true);
            DetailWriter.Tag(t, rec);
        }

        static void Finish(string msg, bool error)
        {
            if (msg == null) return;
            Finished?.Invoke(msg, error);
            try { Application.DocumentManager.MdiActiveDocument?.Editor?.WriteMessage("\n[MCG_3DPanel] " + msg + "\n"); } catch { }
        }

        /// <summary>Dừng có lý do người dùng cần đọc (không phải lỗi lập trình).</summary>
        sealed class StopException : System.Exception
        {
            public StopException(string message) : base(message) { }
        }
    }
}
