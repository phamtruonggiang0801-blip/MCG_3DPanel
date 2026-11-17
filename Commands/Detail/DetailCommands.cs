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
        public const string PlanCommand = "MCG_DetailPlan", ViewCommand = "MCG_DetailView", SectionCommand = "MCG_DetailSection";
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

        [CommandMethod(PlanCommand, CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void DetailPlan() => Run("Detail Plan", doc => Create(doc, false));

        [CommandMethod(ViewCommand, CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void DetailView() => Run("Detail View", doc => Create(doc, true));

        [CommandMethod(SectionCommand, CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void DetailSection() => Run("Section Detail", CreateSection);

        static void Run(string what, Func<Document, string> create)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            try { Finish(create(doc), false); }
            catch (StopException stop) { Finish(stop.Message, true); }
            catch (System.Exception ex)
            {
                // Lỗi lập trình: stack trace đầy đủ ra dòng lệnh; transaction chưa commit ⇒ không để lại gì
                try { doc.Editor.WriteMessage($"\n[MCG_3DPanel] {what} error detail:\n" + ex + "\n"); } catch { }
                Finish($"{what} failed: {ex.Message} (details on the command line). Nothing was drawn.", true);
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

                        List<Entity> twoD = DetailComposer.TwoDItems(tr, assyRef, region);
                        List<Entity> ents;
                        try { ents = DetailComposer.PlanEntities(result, top.Outline, twoD, centre, region); }
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
                file3D = Path.GetFileName(src.FilePath);
                string question = DetailCommandRules.StaleQuestion(src.Stale, file3D, panel);
                if (question != null)
                {
                    var answer = System.Windows.MessageBox.Show(question, what, System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxResult.No);
                    if (answer != System.Windows.MessageBoxResult.Yes) throw new StopException(cancelled);
                    notes.Add(src.Stale == StaleState.Unsaved3D
                        ? $"cut from the saved {file3D} (it had unsaved changes)"
                        : $"{file3D} may be out of date");
                }
                return src;
            }
            catch { src.Dispose(); throw; }
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

                    // Block "Continious symbol" chỉ cần khi section có tấm cắt liên tục. Thiếu ⇒ bổ sung (bản vẽ → CAS.dwt → tự vẽ)
                    // TRƯỚC, commit riêng như Plan View; transaction ghi không commit ⇒ xoá mọi bản ghi lần bổ sung đã tạo
                    // (handle ≥ Handseed trước khi bổ sung: block, block lồng, dim style, layer…).
                    var blocks = new PlanViewBlockSource.Plan();
                    if (DetailComposer.ContinuousMembers(result).Count > 0)
                        blocks = PlanViewBlockSource.Prepare(db, new[] { DetailComposer.ContinuousBlock });
                    if (blocks.Unavailable.Count > 0)
                        throw new StopException($"{what} stopped; the drawing is missing: block {DetailComposer.ContinuousBlock}. Nothing was drawn.");
                    bool added = blocks.FromTemplate.Count + blocks.Drawn.Count > 0;
                    long seed = db.Handseed.Value;
                    bool committed = false;
                    try
                    {
                        if (added)
                        {
                            PlanViewBlockSource.Apply(db, blocks);
                            if (PlanViewBlockSource.NotLive(db, new[] { DetailComposer.ContinuousBlock }).Count > 0)
                                throw new StopException($"{what} stopped; block {DetailComposer.ContinuousBlock} could not be added. Nothing was drawn.");
                        }

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
                            committed = true;
                        }
                    }
                    finally
                    {
                        if (added && !committed) RemoveCreatedSince(db, seed);
                    }

                    notes.AddRange(result.Warnings);
                    if (blocks.FromTemplate.Count > 0) notes.Add($"block {DetailComposer.ContinuousBlock} added from CAS.dwt");
                    if (blocks.Drawn.Count > 0) notes.Add($"block {DetailComposer.ContinuousBlock} drawn by the tool");
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
                    foreach (ObjectId id in blocks) tr.GetObject(id, OpenMode.ForWrite).Erase();
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
