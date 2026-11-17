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
        public const string PlanCommand = "MCG_DetailPlan", ViewCommand = "MCG_DetailView";
        const string LabelLayer = "Mechanical-AM_1";
        const double LabelHeight = 150, LabelGap = 50;

        public static event Action<string, bool> Finished;

        /// <summary>Gọi từ palette: chạy lệnh trong ngữ cảnh lệnh của document đang active.</summary>
        public static void Request(string command)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null || string.IsNullOrWhiteSpace(command)) return;
            doc.SendStringToExecute(command.Trim() + " ", true, false, false);
        }

        [CommandMethod(PlanCommand, CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void DetailPlan() => Run(false);

        [CommandMethod(ViewCommand, CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void DetailView() => Run(true);

        static void Run(bool view)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            string what = view ? "Detail View" : "Detail Plan";
            try { Finish(Create(doc, view), false); }
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
            using (Detail3DSource src = Detail3DSource.Open(db, doc.Name, assyId, paths => Choose(ed, paths), out string why))
            {
                if (src == null) throw new StopException(why == "Cancelled." ? cancelled : why + " Nothing was drawn.");
                string file3D = Path.GetFileName(src.FilePath);
                var notes = new List<string>();
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
