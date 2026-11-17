using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(MCG_3DPanel.Commands.Detail.DetailSpike))]

namespace MCG_3DPanel.Commands.Detail
{
    /// <summary>SPIKE (bỏ đi trước Task 9): thử Section.GenerateSectionGeometry trên solid đọc nền từ file 3D.
    /// Ghi mọi kết quả vào %TEMP%\MCG_DetailSpike.log. Chỉ đọc file 3D, không ghi đĩa.</summary>
    public class DetailSpike
    {
        static readonly string LogPath = Path.Combine(Path.GetTempPath(), "MCG_DetailSpike.log");
        static Editor _ed;
        static Database _host; // database file 3D đã đọc (nguồn WblockCloneObjects cho B)
        static Database _target; // bản vẽ 2D đang mở
        static SecOut _lastC = new SecOut(); // kết quả cách C của lần RunStage gần nhất
        static SecOut _lastD = new SecOut(); // cách D: C + in lại cạnh từng tấm
        static ObjectIdCollection _srcIds = new ObjectIdCollection();
        static Matrix3d _srcXf = Matrix3d.Identity;

        static void Log(string s)
        {
            try { File.AppendAllText(LogPath, s + Environment.NewLine); } catch { }
            try { _ed?.WriteMessage("\n" + s); } catch { }
        }

        // Kết quả 1 lần cắt: 5 mảng gom lại
        sealed class SecOut
        {
            public List<Entity> Boundary = new List<Entity>(), Fill = new List<Entity>(), Background = new List<Entity>(),
                Foreground = new List<Entity>(), Tangency = new List<Entity>();
            public int Ok, Failed;
            public string FirstError;

            public void DisposeAll()
            {
                foreach (var l in new[] { Boundary, Fill, Background, Foreground, Tangency })
                    foreach (var e in l) { try { if (!e.IsDisposed && e.ObjectId.IsNull) e.Dispose(); } catch { } }
            }
        }

        [CommandMethod("MCG_DetailSpike")]
        public void Run()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            _ed = doc.Editor;
            Log("");
            Log("==================== MCG_DetailSpike " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ====================");
            Log("2D drawing: " + doc.Name);
            Log("AutoCAD: " + AcApp.Version);

            // 1. File 3D
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "MCG_DetailSpike - pick the panel 3D file",
                Filter = "Panel 3D (*_3D*.dwg)|*_3D*.dwg|DWG (*.dwg)|*.dwg",
            };
            try
            {
                string dir = Path.GetDirectoryName(doc.Name);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) dlg.InitialDirectory = dir;
            }
            catch { }
            _srcIds = new ObjectIdCollection(); _srcXf = Matrix3d.Identity; _target = doc.Database;
            if (dlg.ShowDialog() != true) { Log("Cancelled (no file)."); return; }
            string path = dlg.FileName;
            Log("3D file: " + path);

            // 2. Vùng (WCS bản vẽ hiện tại = WCS file 3D)
            var pr1 = _ed.GetPoint(new PromptPointOptions("\nFirst corner of the detail region: "));
            if (pr1.Status != PromptStatus.OK) { Log("Cancelled (point 1)."); return; }
            var pr2 = _ed.GetCorner(new PromptCornerOptions("\nOpposite corner: ", pr1.Value));
            if (pr2.Status != PromptStatus.OK) { Log("Cancelled (point 2)."); return; }
            Matrix3d ucs = _ed.CurrentUserCoordinateSystem;
            Point3d a = pr1.Value.TransformBy(ucs), b = pr2.Value.TransformBy(ucs);
            double x0 = Math.Min(a.X, b.X), x1 = Math.Max(a.X, b.X), y0 = Math.Min(a.Y, b.Y), y1 = Math.Max(a.Y, b.Y);
            Log($"Region WCS: X {x0:0.##}..{x1:0.##}  Y {y0:0.##}..{y1:0.##}  ({x1 - x0:0.##} x {y1 - y0:0.##})");
            if (x1 - x0 < 1 || y1 - y0 < 1) { Log("Region too small."); return; }

            var sources = new List<Solid3d>();
            Database db3 = null;
            var swAll = Stopwatch.StartNew();
            try
            {
                // 3. Đọc file 3D (chỉ đọc)
                var sw = Stopwatch.StartNew();
                db3 = new Database(false, true);
                db3.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, null);
                db3.CloseInput(true);
                _host = db3;
                Log($"ReadDwgFile: {sw.ElapsedMilliseconds} ms");

                sw.Restart();
                ReadSolids(db3, sources);
                Log($"Clone+transform solids: {sources.Count} solids, {sw.ElapsedMilliseconds} ms");
                if (sources.Count == 0) { Log("No Solid3d found in MCG3D_* blocks referenced from Model Space."); return; }

                var drawList = new List<(Entity ent, int color)>();

                // 4. Mặt bằng: hộp Z −5000..−1, mặt cắt ngang Z = −1, nhìn xuống
                Log("---- PLAN (horizontal plane Z=-1, box Z -5000..-1) ----");
                var planClip = Clip(sources, x0, x1, y0, y1, -5000, -1);
                Point3dCollection planPts = new Point3dCollection { new Point3d(x0, y0, -1), new Point3d(x1, y0, -1) };
                var plan = RunStage("PLAN", planClip, planPts, Vector3d.YAxis, -Vector3d.ZAxis, (x0, x1, y0, y1, -5000, -1));
                // A: +20000 X; B: thêm +10000 Y để so cạnh nhau
                if (plan.A != null) Collect(plan.A, Matrix3d.Displacement(new Vector3d(20000, 0, 0)), drawList);
                if (plan.B != null) Collect(plan.B, Matrix3d.Displacement(new Vector3d(20000, 10000, 0)), drawList);
                Collect(_lastC, Matrix3d.Displacement(new Vector3d(20000, 20000, 0)), drawList); // C: +20000 Y
                Collect(_lastD, Matrix3d.Displacement(new Vector3d(20000, 30000, 0)), drawList); // D: +30000 Y

                // Thử thêm mặt cắt Z = −2 (chỉ log, không vẽ) — xem mặt cắt trùng mặt hộp có làm hỏng boundary không
                Log("---- PLAN variant (plane Z=-2, same clipped solids, log only) ----");
                Point3dCollection planPts2 = new Point3dCollection { new Point3d(x0, y0, -2), new Point3d(x1, y0, -2) };
                var plan2 = RunStage("PLAN@-2", planClip, planPts2, Vector3d.YAxis, -Vector3d.ZAxis, (x0, x1, y0, y1, -5000, -1));
                plan2.A?.DisposeAll();
                plan2.B?.DisposeAll();
                _lastC.DisposeAll(); _lastD.DisposeAll();
                foreach (var s in planClip) s.Dispose();

                // 5. Mặt cắt đứng: mặt phẳng qua đường giữa (trái→phải), nhìn +Y, hộp Y mid..top, Z −5000..+100
                double ym = (y0 + y1) / 2;
                Log($"---- VERTICAL (plane Y={ym:0.##}, view +Y, box Y {ym:0.##}..{y1:0.##} Z -5000..+100) ----");
                var vClip = Clip(sources, x0, x1, ym, y1, -5000, 100);
                Point3dCollection vPts = new Point3dCollection { new Point3d(x0, ym, 0), new Point3d(x1, ym, 0) };
                var vert = RunStage("VERTICAL", vClip, vPts, Vector3d.ZAxis, Vector3d.YAxis, (x0, x1, ym, y1, -5000, 100));
                // (x, ym, z) → (x + 20000, ym + dy + z, 0). A: dy = −10000.
                // B: dy = −20000 (dưới A 10000) — +10000 sẽ chồng lên mặt bằng A (z ∈ −5000..+100).
                Matrix3d VertMap(double dy) => Matrix3d.AlignCoordinateSystem(
                    new Point3d(0, ym, 0), Vector3d.XAxis, Vector3d.ZAxis, -Vector3d.YAxis,
                    new Point3d(20000, ym + dy, 0), Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis);
                if (vert.A != null) Collect(vert.A, VertMap(-10000), drawList);
                if (vert.B != null) Collect(vert.B, VertMap(-20000), drawList);
                Collect(_lastC, VertMap(-30000), drawList); // C: -30000 Y
                Collect(_lastD, VertMap(-40000), drawList); // D: -40000 Y
                foreach (var s in vClip) s.Dispose();

                // 6. Vẽ vào bản vẽ hiện tại (kỹ sư đóng không lưu)
                sw.Restart();
                int drawn = 0, drawFail = 0;
                using (doc.LockDocument())
                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForWrite);
                    foreach (var (ent, color) in drawList)
                    {
                        try
                        {
                            ent.SetDatabaseDefaults(doc.Database);
                            ent.Layer = "0";
                            ent.ColorIndex = color;
                            ms.AppendEntity(ent);
                            tr.AddNewlyCreatedDBObject(ent, true);
                            drawn++;
                        }
                        catch (System.Exception ex)
                        {
                            drawFail++;
                            if (drawFail <= 3) Log("Draw failed (" + ent.GetType().Name + "): " + ex);
                            try { ent.Dispose(); } catch { }
                        }
                    }
                    tr.Commit();
                }
                Log($"Drawn {drawn} entities (failed {drawFail}), {sw.ElapsedMilliseconds} ms. Plan A at +20000 X, plan B +10000 Y, plan C +20000 Y, plan D +30000 Y; vertical A -10000 Y, B -20000 Y, C -30000 Y, D -40000 Y (all +20000 X).");
                foreach (var o in new[] { plan.A, plan.B, vert.A, vert.B })
                    o?.Fill.ForEach(e => { try { e.Dispose(); } catch { } });
            }
            catch (System.Exception ex)
            {
                Log("FATAL: " + ex);
            }
            finally
            {
                foreach (var s in sources) { try { s.Dispose(); } catch { } }
                try { db3?.Dispose(); } catch { }
                Log($"Total: {swAll.ElapsedMilliseconds} ms. Log file: {LogPath}");
                _ed.WriteMessage("\nMCG_DetailSpike done. Log: " + LogPath + "\n");
            }
        }

        /// <summary>Clone mọi Solid3d trong block MCG3D_* có BlockReference ở Model Space, transform theo block ref.</summary>
        static void ReadSolids(Database db, List<Solid3d> outList)
        {
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is BlockReference br)) continue;
                    var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                    if (!btr.Name.StartsWith("MCG3D_", StringComparison.OrdinalIgnoreCase)) continue;
                    int n = 0;
                    foreach (ObjectId sid in btr)
                    {
                        if (!(tr.GetObject(sid, OpenMode.ForRead) is Solid3d s)) continue;
                        _srcIds.Add(sid); _srcXf = br.BlockTransform;
                        var c = (Solid3d)s.Clone();
                        c.TransformBy(br.BlockTransform);
                        outList.Add(c);
                        n++;
                    }
                    Log($"  block {btr.Name}: {n} solids, insert {br.Position}, transform identity={br.BlockTransform.IsEqualTo(Matrix3d.Identity)}");
                }
            }
        }

        /// <summary>Clone lại nguồn rồi giao với hộp; bỏ solid không chạm / rỗng.</summary>
        static List<Solid3d> Clip(List<Solid3d> sources, double x0, double x1, double y0, double y1, double z0, double z1)
        {
            var sw = Stopwatch.StartNew();
            var res = new List<Solid3d>();
            int considered = 0, empty = 0, failed = 0;
            foreach (var src in sources)
            {
                Extents3d ext;
                try { ext = src.GeometricExtents; } catch { continue; }
                if (ext.MaxPoint.X < x0 || ext.MinPoint.X > x1 || ext.MaxPoint.Y < y0 || ext.MinPoint.Y > y1
                    || ext.MaxPoint.Z < z0 || ext.MinPoint.Z > z1) continue;
                considered++;
                Solid3d clone = null, box = null;
                try
                {
                    clone = (Solid3d)src.Clone();
                    box = new Solid3d();
                    box.CreateBox(x1 - x0, y1 - y0, z1 - z0);
                    box.TransformBy(Matrix3d.Displacement(new Vector3d((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2)));
                    clone.BooleanOperation(BooleanOperationType.BoolIntersect, box);
                    if (clone.IsNull) { empty++; clone.Dispose(); }
                    else res.Add(clone);
                }
                catch (System.Exception ex)
                {
                    failed++;
                    if (failed <= 3) Log("  intersect failed: " + ex);
                    try { clone?.Dispose(); } catch { }
                }
                finally { try { box?.Dispose(); } catch { } }
            }
            Log($"  Clip: total {sources.Count}, extents overlap {considered}, intersected (non-empty) {res.Count}, empty {empty}, failed {failed}, {sw.ElapsedMilliseconds} ms");
            return res;
        }

        /// <summary>Luôn chạy cả 2: A Section ngoài database; B database tạm chứa solid + Section (settings 2D, bỏ nét khuất).</summary>
        static (SecOut A, SecOut B) RunStage(string tag, List<Solid3d> solids, Point3dCollection pts, Vector3d verticalDir, Vector3d wantView,
            (double x0, double x1, double y0, double y1, double z0, double z1) box)
        {
            if (solids.Count == 0) { Log($"  [{tag}] no clipped solids - skipped."); return (null, null); }

            // --- Approach A ---
            SecOut outA = new SecOut();
            var sw = Stopwatch.StartNew();
            Section sec = null;
            try
            {
                sec = new Section(pts, verticalDir);
                ConfigureSection(tag + "/A", sec, wantView);
                foreach (var s in solids) Generate(sec, s, outA);
            }
            catch (System.Exception ex)
            {
                outA.Failed++;
                if (outA.FirstError == null) outA.FirstError = ex.ToString();
            }
            finally { try { sec?.Dispose(); } catch { } }
            Log($"  [{tag}] Approach A (non-resident Section): ok {outA.Ok}, failed {outA.Failed}, {sw.ElapsedMilliseconds} ms");
            LogCounts(tag + "/A", outA);
            if (outA.FirstError != null) Log($"  [{tag}] A first exception: {outA.FirstError}");

            // --- Approach B (luôn chạy) ---
            // B: WblockCloneObjects solid gốc (file 3D) -> CHÍNH bản vẽ 2D; cắt hộp + Section ở đó; output thuộc bản vẽ 2D;
            // xoá solid tạm + Section rồi commit. (Database tạm riêng => output dính database bị huỷ => crash 0xC0000005.)
            SecOut outB = new SecOut(), outB2 = new SecOut();
            _lastC = new SecOut(); _lastD = new SecOut();
            sw.Restart();
            try
            {
                Database target = _target;
                ObjectId targetMs = SymbolUtilityServices.GetBlockModelSpaceId(target);
                var map = new IdMapping();
                _host.WblockCloneObjects(_srcIds, targetMs, map, DuplicateRecordCloning.Ignore, false);
                using (Transaction tr = target.TransactionManager.StartTransaction())
                {
                    var ms = (BlockTableRecord)tr.GetObject(targetMs, OpenMode.ForWrite);
                    var ids = new List<ObjectId>();
                    int cloned = 0, kept = 0;
                    foreach (IdPair p in map)
                    {
                        if (!p.IsCloned || !p.IsPrimary) continue;
                        if (!(tr.GetObject(p.Value, OpenMode.ForWrite) is Solid3d s)) continue;
                        cloned++;
                        s.TransformBy(_srcXf);
                        Extents3d ext;
                        try { ext = s.GeometricExtents; } catch { s.Erase(); continue; }
                        if (ext.MaxPoint.X < box.x0 || ext.MinPoint.X > box.x1 || ext.MaxPoint.Y < box.y0 || ext.MinPoint.Y > box.y1
                            || ext.MaxPoint.Z < box.z0 || ext.MinPoint.Z > box.z1) { s.Erase(); continue; }
                        var bx = new Solid3d();
                        bx.CreateBox(box.x1 - box.x0, box.y1 - box.y0, box.z1 - box.z0);
                        bx.TransformBy(Matrix3d.Displacement(new Vector3d((box.x0 + box.x1) / 2, (box.y0 + box.y1) / 2, (box.z0 + box.z1) / 2)));
                        s.BooleanOperation(BooleanOperationType.BoolIntersect, bx);
                        bx.Dispose();
                        if (s.IsNull) { s.Erase(); continue; }
                        ids.Add(s.ObjectId);
                        kept++;
                    }
                    Log($"  [{tag}/B] wblock-cloned {cloned}, kept after clip {kept}");
                    var secB = new Section(pts, verticalDir);
                    ms.AppendEntity(secB);
                    tr.AddNewlyCreatedDBObject(secB, true);
                    ConfigureSection(tag + "/B", secB, wantView);
                    ConfigureSettings(tag + "/B", tr, secB, hidden: false);
                    foreach (var id in ids) Generate(secB, (Entity)tr.GetObject(id, OpenMode.ForRead), outB);
                    // Đối chứng: cùng Section, HiddenLine = true (chỉ log)
                    ConfigureSettings(tag + "/B2", tr, secB, hidden: true);
                    foreach (var id in ids) Generate(secB, (Entity)tr.GetObject(id, OpenMode.ForRead), outB2);
                    // C: gộp mọi solid đã cắt hộp thành 1 khối (BoolUnite) rồi cắt với HiddenLine = false
                    //    => AutoCAD bỏ cả nét bị solid KHÁC che. Đổi lại: mặt phẳng trùng nhau giữa 2 tấm bị hợp nhất (mất nét mối nối phẳng).
                    // D: lấy cạnh của từng solid TRƯỚC khi gộp (để in lại đường nối sau khi gộp)
                    var swD = Stopwatch.StartNew();
                    var seamCurves = new List<Curve>();
                    int edgeFail = 0;
                    foreach (var id in ids)
                    {
                        try
                        {
                            using (var brep = new Autodesk.AutoCAD.BoundaryRepresentation.Brep((Solid3d)tr.GetObject(id, OpenMode.ForRead)))
                                foreach (var e in brep.Edges)
                                {
                                    try
                                    {
                                        using (var gc = e.Curve)
                                        {
                                            Curve ent = null;
                                            try { ent = Curve.CreateFromGeCurve(gc); } catch { }
                                            if (ent == null && gc is ExternalCurve3d ext && ext.IsNativeCurve)
                                            {
                                                var nc = ext.NativeCurve;
                                                if (nc is LinearEntity3d) ent = new Line(gc.StartPoint, gc.EndPoint);
                                                else { try { ent = Curve.CreateFromGeCurve(nc); } catch { } }
                                            }
                                            if (ent == null) ent = new Line(gc.StartPoint, gc.EndPoint);
                                            seamCurves.Add(ent);
                                        }
                                    }
                                    catch { edgeFail++; }
                                }
                        }
                        catch { edgeFail++; }
                    }
                    Log($"  [{tag}/D] edges collected {seamCurves.Count}, failed {edgeFail}, {swD.ElapsedMilliseconds} ms");
                    ConfigureSettings(tag + "/C", tr, secB, hidden: false);
                    var swC = Stopwatch.StartNew();
                    int uniteFail = 0;
                    if (ids.Count > 0)
                    {
                        var u = (Solid3d)tr.GetObject(ids[0], OpenMode.ForWrite);
                        for (int i = 1; i < ids.Count; i++)
                        {
                            try { u.BooleanOperation(BooleanOperationType.BoolUnite, (Solid3d)tr.GetObject(ids[i], OpenMode.ForWrite)); }
                            catch (System.Exception ex) { uniteFail++; if (uniteFail <= 3) Log($"  [{tag}/C] unite failed: {ex.Message}"); }
                        }
                        Log($"  [{tag}/C] unite {ids.Count} solids, failed {uniteFail}, {swC.ElapsedMilliseconds} ms");
                        swC.Restart();
                        Generate(secB, u, _lastC);
                        Log($"  [{tag}/C] generate on union: {swC.ElapsedMilliseconds} ms");
                        // D: in (imprint) cạnh từng tấm lên khối gộp -> đường nối thành cạnh thật, vẫn bị bỏ khi bị che
                        swD.Restart();
                        int imp = 0, impFail = 0; string impErr = null;
                        foreach (var c in seamCurves)
                        {
                            try { u.ImprintEntity(c); imp++; }
                            catch (System.Exception ex) { impFail++; if (impErr == null) impErr = ex.Message; }
                            finally { c.Dispose(); }
                        }
                        Log($"  [{tag}/D] imprint ok {imp}, failed {impFail} (first: {impErr}), {swD.ElapsedMilliseconds} ms");
                        swD.Restart();
                        Generate(secB, u, _lastD);
                        Log($"  [{tag}/D] generate on imprinted union: {swD.ElapsedMilliseconds} ms");
                    }
                    foreach (var id in ids) tr.GetObject(id, OpenMode.ForWrite).Erase();
                    secB.Erase();
                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                outB.Failed++;
                if (outB.FirstError == null) outB.FirstError = ex.ToString();
            }
            Log($"  [{tag}] Approach B (section in the 2D drawing): ok {outB.Ok}, failed {outB.Failed}, {sw.ElapsedMilliseconds} ms");
            LogCounts(tag + "/B", outB);
            Log($"  [{tag}] B2 (same section, HiddenLine=true) for comparison:");
            LogCounts(tag + "/B2", outB2);
            outB2.DisposeAll();
            Log($"  [{tag}] C (union of all clipped solids, HiddenLine=false):");
            LogCounts(tag + "/C", _lastC);
            Log($"  [{tag}] D (union + imprinted edges of every solid, HiddenLine=false):");
            LogCounts(tag + "/D", _lastD);
            if (outB.FirstError != null) Log($"  [{tag}] B first exception: {outB.FirstError}");
            return (outA, outB);
        }

        static void ConfigureSection(string tag, Section sec, Vector3d wantView)
        {
            try { sec.State = SectionState.Plane; } catch (System.Exception ex) { Log($"  [{tag}] set State failed: {ex.Message}"); }
            Log($"  [{tag}] Section normal {sec.Normal}, viewing {sec.ViewingDirection}, vertical {sec.VerticalDirection}, state {sec.State}");
            try
            {
                if (!sec.ViewingDirection.IsCodirectionalTo(wantView))
                {
                    sec.ViewingDirection = wantView;
                    Log($"  [{tag}] viewing set to {wantView} -> normal {sec.Normal}, viewing {sec.ViewingDirection}");
                }
            }
            catch (System.Exception ex) { Log($"  [{tag}] set ViewingDirection failed: {ex.Message}"); }
        }

        /// <summary>Chỉ có khi Section nằm trong database: log + bật background cho kiểu 2D.</summary>
        static void ConfigureSettings(string tag, Transaction tr, Section sec, bool hidden)
        {
            try
            {
                ObjectId sid = sec.Settings;
                Log($"  [{tag}] Settings null={sid.IsNull}");
                if (sid.IsNull) return;
                var st = (SectionSettings)tr.GetObject(sid, OpenMode.ForWrite);
                Log($"  [{tag}] settings CurrentSectionType={st.CurrentSectionType}");
                st.CurrentSectionType = SectionType.Section2d;
                foreach (SectionGeometry g in new[] { SectionGeometry.IntersectionBoundary, SectionGeometry.IntersectionFill,
                    SectionGeometry.BackgroundGeometry, SectionGeometry.ForegroundGeometry, SectionGeometry.CurveTangencyLines })
                    Log($"  [{tag}]   {g}: visible={st.Visibility(SectionType.Section2d, g)} hidden={st.HiddenLine(SectionType.Section2d, g)}");
                st.SetVisibility(SectionType.Section2d, SectionGeometry.BackgroundGeometry, true);
                st.SetHiddenLine(SectionType.Section2d, SectionGeometry.BackgroundGeometry, hidden);
                Log($"  [{tag}] -> CurrentSectionType={st.CurrentSectionType}, background visible={st.Visibility(SectionType.Section2d, SectionGeometry.BackgroundGeometry)}, " +
                    $"hidden={st.HiddenLine(SectionType.Section2d, SectionGeometry.BackgroundGeometry)} (false = hidden lines removed)");
            }
            catch (System.Exception ex) { Log($"  [{tag}] settings failed: {ex}"); }
        }

        static void Generate(Section sec, Entity solid, SecOut o)
        {
            try
            {
                sec.GenerateSectionGeometry(solid, out Array bnd, out Array fill, out Array bg, out Array fg, out Array tan);
                Add(o.Boundary, bnd); Add(o.Fill, fill); Add(o.Background, bg); Add(o.Foreground, fg); Add(o.Tangency, tan);
                o.Ok++;
            }
            catch (System.Exception ex)
            {
                o.Failed++;
                if (o.FirstError == null) o.FirstError = ex.ToString();
            }
        }

        static void Add(List<Entity> l, Array arr)
        {
            if (arr == null) return;
            foreach (object x in arr) if (x is Entity e) l.Add(e);
        }

        static void LogCounts(string tag, SecOut o)
        {
            Log($"  [{tag}] intersectionBoundary: {Counts(o.Boundary)}");
            Log($"  [{tag}] intersectionFill:     {Counts(o.Fill)}");
            Log($"  [{tag}] backgroundCurves:     {Counts(o.Background)}");
            Log($"  [{tag}] foregroundCurves:     {Counts(o.Foreground)}");
            Log($"  [{tag}] curveTangencyCurves:  {Counts(o.Tangency)}");
            // Z của background: phẳng (đã chiếu) hay 3D?
            try
            {
                if (o.Background.Count > 0)
                {
                    double zmin = double.MaxValue, zmax = double.MinValue;
                    foreach (var e in o.Background)
                    {
                        try { var ex = e.GeometricExtents; zmin = Math.Min(zmin, ex.MinPoint.Z); zmax = Math.Max(zmax, ex.MaxPoint.Z); } catch { }
                    }
                    Log($"  [{tag}] background Z range {zmin:0.###}..{zmax:0.###}; Y range check below for vertical");
                    double ymin = double.MaxValue, ymax = double.MinValue;
                    foreach (var e in o.Background)
                    {
                        try { var ex = e.GeometricExtents; ymin = Math.Min(ymin, ex.MinPoint.Y); ymax = Math.Max(ymax, ex.MaxPoint.Y); } catch { }
                    }
                    Log($"  [{tag}] background Y range {ymin:0.###}..{ymax:0.###}");
                }
            }
            catch { }
        }

        static string Counts(List<Entity> l)
        {
            if (l.Count == 0) return "0";
            var sb = new StringBuilder(l.Count.ToString()).Append(" (");
            sb.Append(string.Join(", ", l.GroupBy(e => e.GetType().Name).OrderBy(g => g.Key).Select(g => g.Key + " " + g.Count())));
            return sb.Append(')').ToString();
        }

        static void Collect(SecOut o, Matrix3d m, List<(Entity, int)> draw)
        {
            void Put(List<Entity> l, int color)
            {
                foreach (var e in l)
                {
                    try { e.TransformBy(m); draw.Add((e, color)); }
                    catch (System.Exception ex) { Log("  transform failed (" + e.GetType().Name + "): " + ex.Message); }
                }
            }
            Put(o.Boundary, 1);
            Put(o.Background, 3);
            Put(o.Foreground, 5);
            Put(o.Tangency, 4);
        }
    }
}
