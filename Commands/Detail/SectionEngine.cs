using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCG_3DPanel.Models;
using MCG_3DPanel.Services.Detail;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcSection = Autodesk.AutoCAD.DatabaseServices.Section;

namespace MCG_3DPanel.Commands.Detail
{
    /// <summary>Vết cắt của 1 tấm (1 vòng kín), toạ độ 2D của mặt cắt.</summary>
    public sealed class CutFace
    {
        public string Type;
        public string Handle;
        public List<P2> Outline;            // kín, không lặp điểm đầu
        public double MinSigned, MaxSigned;  // khoảng solid CHƯA cắt hộp theo hướng nhìn, so với mặt cắt
    }

    /// <summary>Đường thấy (không nét khuất), 2D Z=0, không nằm trong database.</summary>
    public sealed class EdgeCurve
    {
        public Curve Curve;
        public bool OnBoundary;   // nằm trên mặt bên hộp cắt ⇒ break line
        public string Type;       // "" khi không gán được tấm
        public string Handle;
    }

    public sealed class SectionResult
    {
        public List<CutFace> Cut = new List<CutFace>();
        public List<EdgeCurve> Visible = new List<EdgeCurve>();
        public List<ParallelPlate> Parallel = new List<ParallelPlate>();   // chỉ Section
        public List<string> Warnings = new List<string>();

        /// <summary>Giải phóng các đường chưa đưa vào bản vẽ.</summary>
        public void DisposeCurves()
        {
            foreach (var v in Visible)
                if (v.Curve != null && !v.Curve.IsDisposed && v.Curve.ObjectId.IsNull) v.Curve.Dispose();
        }
    }

    /// <summary>Cắt solid 3D (file &lt;panel&gt;_3D) ra hình Detail Plan / Section KHÔNG nét khuất — cách D của spike (Task 1):
    /// WblockClone vào bản vẽ 2D → cắt hộp → Section từng tấm (vết cắt) → gộp + in cạnh từng tấm → Section khối gộp (đường thấy).
    /// Mọi đối tượng tạm bị xoá; lỗi ⇒ không để lại gì rồi ném tiếp. Caller đã lock document của <c>target</c>.</summary>
    public static class SectionEngine
    {
        const double Tol = 0.5;          // mm: phủ cạnh / mặt biên
        const double ChordErr = 0.1;     // mm: chia cong
        const double JoinTol = 0.05;     // mm: nối vết cắt
        const double CoverStep = 2.0;    // mm: mật độ điểm khi kiểm phủ
        const double ZMargin = 10.0;     // mm: hộp vượt solid theo Z để đáy/đỉnh hộp không trùng mặt solid
        const double PlanCutZ = -1.0;    // mặt cắt Detail Plan
        const double ParallelDeg = 1.0;

        sealed class Job
        {
            public bool IsSection;
            public ClipBox Box;
            public DetailRegion Region3;
            public P2[] Slab;                 // Section: slab (toạ độ 3D)
            public double ZMin, ZMax;
            public Point3d SecP1, SecP2;
            public Vector3d SecVertical, View;
            public Point3d PlaneOrigin;       // điểm trên mặt cắt (tính khoảng có dấu)
            public Vector3d Look3, Right3;
            public Matrix3d ToOut;            // WCS 3D → toạ độ ra (mặt cắt ở Z=0)
        }

        sealed class Member
        {
            public SolidItem Item;
            public ObjectId Id;               // bản chép trong target
            public double MinSigned, MaxSigned;
            public P2? FootAxis;              // Section: trục dài footprint (XY 3D)
            public bool Cut;
            public readonly List<IList<P2>> Edges2D = new List<IList<P2>>();   // cạnh chiếu riêng (gán đường thấy)
            public readonly List<Curve> Seams = new List<Curve>();             // cạnh Brep 3D (in lên khối gộp)
        }

        // ─── API ───────────────────────────────────────────────────────────────

        /// <summary>Detail Plan: mặt cắt Z=−1 nhìn −Z, hộp = lăng trụ vùng Z∈[−1, đáy kết cấu]; bỏ Top Plate. Toạ độ ra = toạ độ plan.</summary>
        public static SectionResult Plan(Database target, Detail3DSource src, DetailRegion regionPlan)
        {
            var region3 = To3D(src, regionPlan);
            double minZ = src.Solids.Count == 0 ? PlanCutZ : src.Solids.Min(s => s.Extents.MinPoint.Z);
            double zMin = Math.Min(minZ, PlanCutZ) - ZMargin;
            var box = ClipBox.Plan(region3, zMin, PlanCutZ);
            var b = box.Bounds();
            var c = new Point3d((b.minX + b.maxX) / 2, (b.minY + b.maxY) / 2, PlanCutZ);
            var job = new Job
            {
                Box = box, Region3 = region3, ZMin = zMin, ZMax = PlanCutZ,
                SecP1 = c, SecP2 = c + Vector3d.XAxis * 100, SecVertical = Vector3d.YAxis, View = -Vector3d.ZAxis,
                PlaneOrigin = c, Look3 = -Vector3d.ZAxis, Right3 = Vector3d.XAxis,
                ToOut = PlanOut(src, c),
            };
            var items = src.Solids.Where(s => s.Type != nameof(PartType.TopPlate) && Meets(box, s)).ToList();
            return Run(target, src, items, job);
        }

        /// <summary>Section Detail: mặt cắt đứng qua đường cắt nhìn theo Look, hộp = lăng trụ vùng ∩ slab (0..Depth), Z toàn bộ.
        /// Toạ độ ra = <see cref="SectionFrame.ToSection"/> (x dọc Right từ Start, y = Z của 3D).</summary>
        public static SectionResult Section(Database target, Detail3DSource src, DetailRegion regionPlan, SectionFrame f)
        {
            Matrix3d m = src.PlanTo3D;
            var region3 = To3D(src, regionPlan);
            Point3d s3 = new Point3d(f.Start.X, f.Start.Y, 0).TransformBy(m);
            Point3d e3 = new Point3d(f.End.X, f.End.Y, 0).TransformBy(m);
            Vector3d lookRaw = new Vector3d(f.Look.X, f.Look.Y, 0).TransformBy(m);
            Vector3d look3 = new Vector3d(lookRaw.X, lookRaw.Y, 0).GetNormal();
            Vector3d rightRaw = new Vector3d(f.Right.X, f.Right.Y, 0).TransformBy(m);
            Vector3d right3 = new Vector3d(rightRaw.X, rightRaw.Y, 0).GetNormal();
            double depth = f.Depth * lookRaw.Length;   // PlanTo3D là phép cứng ⇒ hệ số 1

            double zMin = src.Solids.Count == 0 ? -ZMargin : src.Solids.Min(s => s.Extents.MinPoint.Z) - ZMargin;
            double zMax = src.Solids.Count == 0 ? ZMargin : src.Solids.Max(s => s.Extents.MaxPoint.Z) + ZMargin;
            P2 start = new P2(s3.X, s3.Y), end = new P2(e3.X, e3.Y), look = new P2(look3.X, look3.Y);
            var box = ClipBox.Section(region3, start, end, look, depth, zMin, zMax);

            var origin = new Point3d(s3.X, s3.Y, 0);
            var job = new Job
            {
                IsSection = true, Box = box, Region3 = region3, Slab = ClipBox.Slab(start, end, look, depth),
                ZMin = zMin, ZMax = zMax,
                SecP1 = origin, SecP2 = new Point3d(e3.X, e3.Y, 0), SecVertical = Vector3d.ZAxis, View = look3,
                PlaneOrigin = origin, Look3 = look3, Right3 = right3,
                // (O, Right, Z, −Look) → (gốc, X, Y, Z): x = dọc Right, y = Z, z = −khoảng theo Look (mặt cắt ⇒ 0)
                ToOut = Matrix3d.AlignCoordinateSystem(origin, right3, Vector3d.ZAxis, -look3,
                    Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis),
            };
            var items = src.Solids.Where(s => Meets(box, s)).ToList();
            return Run(target, src, items, job);
        }

        /// <summary>Viền Top Plate trong vùng (cắt ngang Z = +t/2, đã cắt theo vùng), toạ độ plan, CCW; vòng ngoài lớn nhất. Rỗng ⇒ không có.</summary>
        public static List<P2> TopPlateOutline(Database target, Detail3DSource src, DetailRegion regionPlan)
        {
            var plates = src.Solids.Where(s => s.Type == nameof(PartType.TopPlate)).ToList();
            if (plates.Count == 0) return new List<P2>();
            double t = src.TopPlateThickness > 0 ? src.TopPlateThickness : plates.Max(s => s.Extents.MaxPoint.Z);
            double zCut = t / 2;
            var region3 = To3D(src, regionPlan);
            double zMin = plates.Min(s => s.Extents.MinPoint.Z) - ZMargin, zMax = plates.Max(s => s.Extents.MaxPoint.Z) + ZMargin;
            var box = ClipBox.Plan(region3, zMin, zMax);
            var items = plates.Where(s => Meets(box, s)).ToList();
            if (items.Count == 0) return new List<P2>();
            var b = box.Bounds();
            Matrix3d toOut = PlanOut(src, new Point3d((b.minX + b.maxX) / 2, (b.minY + b.maxY) / 2, zCut));

            var clones = new Clones();
            Solid3d template = null;
            bool done = false;
            try
            {
                template = Prism(region3, zMin, zMax);
                clones.Clone(target, src, items);
                var loops = new List<List<P2>>();
                using (Transaction tr = target.TransactionManager.StartTransaction())
                {
                    Solid3d union = null;
                    foreach (var pair in clones.Primary)
                    {
                        var s = (Solid3d)tr.GetObject(pair.Value, OpenMode.ForWrite);
                        s.TransformBy(src.BlockToWcs);
                        using (var bx = (Solid3d)template.Clone()) s.BooleanOperation(BooleanOperationType.BoolIntersect, bx);
                        if (s.IsNull) continue;
                        if (union == null) union = s;
                        else union.BooleanOperation(BooleanOperationType.BoolUnite, s);
                    }
                    if (union != null)
                    {
                        var pieces = new List<List<P2>>();
                        using (Region r = union.GetSection(new Plane(new Point3d(0, 0, zCut), Vector3d.ZAxis)))
                            if (r != null) Consume(ExplodeRegion(r), cv => { cv.TransformBy(toOut); pieces.Add(Sample2(cv)); });
                        loops = SectionCurveRules.ChainLoops(pieces, JoinTol, out _);
                    }
                    clones.EraseAll(tr);
                    tr.Commit();
                }
                done = true;
                var best = loops.Where(l => l.Count >= 3).OrderByDescending(l => Math.Abs(SectionCurveRules.SignedArea(l))).FirstOrDefault();
                if (best == null) return new List<P2>();
                if (SectionCurveRules.SignedArea(best) < 0) best.Reverse();
                return best;
            }
            finally
            {
                template?.Dispose();
                if (!done) clones.EraseLeftovers(target);
            }
        }

        // ─── Pipeline ──────────────────────────────────────────────────────────

        static SectionResult Run(Database target, Detail3DSource src, List<SolidItem> items, Job job)
        {
            var result = new SectionResult();
            if (items.Count == 0) return result;

            var clones = new Clones();
            var members = new List<Member>();
            Solid3d template = null;
            bool done = false;
            try
            {
                template = MakeBox(job);
                // 1. Chép solid nguồn vào bản vẽ 2D (WblockCloneObjects; Clone() chéo database ⇒ eWrongDatabase)
                clones.Clone(target, src, items);
                int missing = items.Count - clones.Primary.Count;
                if (missing > 0) result.Warnings.Add($"{missing} solid(s) could not be copied from the 3D file");

                using (Transaction tr = target.TransactionManager.StartTransaction())
                {
                    var bySource = items.ToDictionary(i => i.Id);
                    // 2. Đặt về WCS, đo khoảng chưa cắt, cắt hộp
                    int clipFail = 0, footFail = 0;
                    foreach (var pair in clones.Primary)
                    {
                        var s = (Solid3d)tr.GetObject(pair.Value, OpenMode.ForWrite);
                        s.TransformBy(src.BlockToWcs);
                        var item = bySource[pair.Key];
                        var m = new Member { Item = item, Id = pair.Value };
                        (m.MinSigned, m.MaxSigned) = SignedRange(s, job.PlaneOrigin, job.Look3);
                        if (job.IsSection && item.Type != nameof(PartType.HP))
                        {
                            try { m.FootAxis = FootAxis(s, (item.Extents.MinPoint.Z + item.Extents.MaxPoint.Z) / 2); }
                            catch (System.Exception) { footFail++; }
                        }
                        try
                        {
                            using (var bx = (Solid3d)template.Clone()) s.BooleanOperation(BooleanOperationType.BoolIntersect, bx);
                        }
                        catch (System.Exception) { clipFail++; continue; }   // bản chép vẫn bị xoá ở bước 7
                        if (s.IsNull) continue;
                        members.Add(m);
                    }
                    if (clipFail > 0) result.Warnings.Add($"{clipFail} solid(s) could not be clipped to the detail region and were skipped");
                    if (footFail > 0) result.Warnings.Add($"{footFail} solid footprint(s) could not be read (weld-trace check skipped)");

                    if (members.Count > 0) Generate(target, tr, job, members, result);

                    // 7. Xoá mọi đối tượng tạm
                    int eraseFail = clones.EraseAll(tr);
                    if (eraseFail > 0) result.Warnings.Add($"{eraseFail} temporary object(s) could not be erased");
                    tr.Commit();
                }
                done = true;
                return result;
            }
            finally
            {
                template?.Dispose();
                foreach (var m in members) foreach (var c in m.Seams) if (!c.IsDisposed) c.Dispose();
                if (!done)
                {
                    result.DisposeCurves();
                    clones.EraseLeftovers(target);
                }
            }
        }

        /// <summary>Bước 3–6 trên các tấm đã cắt hộp (bản chép resident trong target).</summary>
        static void Generate(Database target, Transaction tr, Job job, List<Member> members, SectionResult result)
        {
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(target), OpenMode.ForWrite);
            var sec = new AcSection(new Point3dCollection { job.SecP1, job.SecP2 }, job.SecVertical);
            ms.AppendEntity(sec);
            tr.AddNewlyCreatedDBObject(sec, true);
            // Lỗi giữa chừng ⇒ transaction bị huỷ ⇒ section + settings (tạo trong transaction) tự mất.
            sec.State = SectionState.Plane;
            if (!sec.ViewingDirection.IsCodirectionalTo(job.View)) sec.ViewingDirection = job.View;
            ObjectId settingsId = sec.Settings;
            if (settingsId.IsNull) throw new InvalidOperationException("the section has no settings object (hidden-line removal unavailable)");
            var st = (SectionSettings)tr.GetObject(settingsId, OpenMode.ForWrite);
            st.CurrentSectionType = SectionType.Section2d;
            st.SetVisibility(SectionType.Section2d, SectionGeometry.BackgroundGeometry, true);
            st.SetHiddenLine(SectionType.Section2d, SectionGeometry.BackgroundGeometry, false);   // false = bỏ nét khuất
            st.SetVisibility(SectionType.Section2d, SectionGeometry.CurveTangencyLines, true);

            GenerateOn(tr, job, sec, members, result);

            // 7 (phần section): xoá section rồi settings của nó
            sec.Erase();
            var so = tr.GetObject(settingsId, OpenMode.ForWrite);
            if (!so.IsErased) so.Erase();
        }

        static void GenerateOn(Transaction tr, Job job, AcSection sec, List<Member> members, SectionResult result)
        {
            // 3. Section từng tấm: vết cắt + cạnh chiếu riêng (chỉ để gán)
            int secFail = 0, open = 0;
            var live = new List<Member>();
            foreach (var m in members)
            {
                var solid = (Solid3d)tr.GetObject(m.Id, OpenMode.ForRead);
                Array bnd, fill, bg, fg, tan;
                try { sec.GenerateSectionGeometry(solid, out bnd, out fill, out bg, out fg, out tan); }
                catch (System.Exception) { secFail++; continue; }
                live.Add(m);
                DisposeAll(fill); DisposeAll(fg);

                var pieces = new List<List<P2>>();
                Consume(Curves(bnd), c => { c.TransformBy(job.ToOut); pieces.Add(Sample2(c)); });
                var loops = SectionCurveRules.ChainLoops(pieces, JoinTol, out int o);
                open += o;
                foreach (var loop in loops.Where(l => l.Count >= 3))
                {
                    m.Cut = true;
                    m.Edges2D.Add(Closed(loop));
                    result.Cut.Add(new CutFace
                    {
                        Type = m.Item.Type, Handle = m.Item.Handle, Outline = loop,
                        MinSigned = m.MinSigned, MaxSigned = m.MaxSigned,
                    });
                }
                Consume(Curves(bg).Concat(Curves(tan)), c => { c.TransformBy(job.ToOut); m.Edges2D.Add(Sample2(c)); });
            }
            if (secFail > 0) result.Warnings.Add($"{secFail} solid(s) could not be sectioned and were skipped");
            if (open > 0) result.Warnings.Add($"{open} cut outline(s) did not close");
            if (live.Count == 0) return;

            // 4. Cạnh Brep từng tấm (để in lại mối nối) + cạnh nằm trên mặt bên hộp ⇒ biên (chiếu ra 2D)
            var boundary = new List<IList<P2>>();
            int edgeFail = 0;
            foreach (var m in live)
            {
                var solid = (Solid3d)tr.GetObject(m.Id, OpenMode.ForRead);
                try
                {
                    using (var brep = new Autodesk.AutoCAD.BoundaryRepresentation.Brep(solid))
                        foreach (var e in brep.Edges)
                        {
                            Curve ent;
                            try { using (var gc = e.Curve) ent = ToDbCurve(gc); }
                            catch (System.Exception) { edgeFail++; continue; }
                            m.Seams.Add(ent);
                            try
                            {
                                var pts = Sample3(ent);
                                if (job.Box.OnSideFace(pts.Select(p => (new P2(p.X, p.Y), p.Z)).ToList(), Tol))
                                    boundary.Add(pts.Select(p => XY(p.TransformBy(job.ToOut))).ToList());
                            }
                            catch (System.Exception) { edgeFail++; }
                        }
                }
                catch (System.Exception) { edgeFail++; }
            }
            if (edgeFail > 0) result.Warnings.Add($"{edgeFail} solid edge(s) could not be read (some joint lines may be missing)");

            // 5. Gộp mọi tấm rồi in cạnh từng tấm lên khối gộp
            var union = (Solid3d)tr.GetObject(live[0].Id, OpenMode.ForWrite);
            var united = new List<Member> { live[0] };
            var apart = new List<Member>();
            for (int i = 1; i < live.Count; i++)
            {
                try
                {
                    union.BooleanOperation(BooleanOperationType.BoolUnite, (Solid3d)tr.GetObject(live[i].Id, OpenMode.ForWrite));
                    united.Add(live[i]);
                }
                catch (System.Exception) { apart.Add(live[i]); }
            }
            if (apart.Count > 0)
                result.Warnings.Add($"{apart.Count} solid(s) could not be merged; lines they hide may still show");
            int imp = 0, impFail = 0;
            foreach (var m in united)
                foreach (var c in m.Seams)
                {
                    try { union.ImprintEntity(c); imp++; }
                    catch (System.Exception) { impFail++; }   // spike: vài cạnh eGeneralModelingFailure, vô hại
                }
            if (impFail > 0) result.Warnings.Add($"{impFail} of {imp + impFail} edge(s) could not be imprinted (some joint lines may be missing)");

            // 6. Section khối gộp: đường thấy = background + tangency (bỏ boundary của khối gộp)
            var raw = new List<Curve>();
            foreach (var solid in new[] { union }.Concat(apart.Select(m => (Solid3d)tr.GetObject(m.Id, OpenMode.ForRead))))
            {
                Array bnd, fill, bg, fg, tan;
                try { sec.GenerateSectionGeometry(solid, out bnd, out fill, out bg, out fg, out tan); }
                catch (System.Exception) { result.Warnings.Add("the merged solid could not be sectioned; visible lines are missing"); continue; }
                DisposeAll(bnd); DisposeAll(fill); DisposeAll(fg);
                raw.AddRange(Curves(bg)); raw.AddRange(Curves(tan));
            }

            var memberEdges = live.Select(m => (IList<IList<P2>>)m.Edges2D).ToList();
            int next = 0;
            try
            {
                for (; next < raw.Count; next++)
                {
                    Curve c = raw[next];
                    c.TransformBy(job.ToOut);
                    Flatten(c);
                    var pts = Sample2(c);
                    if (c is Spline)
                    {
                        var pl = ToPolyline(pts);
                        c.Dispose();
                        raw[next] = c = pl;
                    }
                    c.SetDatabaseDefaults(sec.Database);   // không trỏ tới layer/linetype chép tạm (bị xoá ở bước 7)
                    var dense = SectionCurveRules.Densify(pts, CoverStep);
                    int who = SectionCurveRules.Attribute(dense, memberEdges, Tol);
                    result.Visible.Add(new EdgeCurve
                    {
                        Curve = c,
                        OnBoundary = SectionCurveRules.Covers(dense, boundary, Tol),
                        Type = who < 0 ? "" : live[who].Item.Type,
                        Handle = who < 0 ? "" : live[who].Item.Handle,
                    });
                }
            }
            catch
            {
                // đường chưa vào result: giải phóng ngay (result do Run giải phóng)
                for (int i = next; i < raw.Count; i++) if (!raw[i].IsDisposed) raw[i].Dispose();
                throw;
            }

            if (job.IsSection) FindParallel(job, live, result);
        }

        /// <summary>Tấm không phải HP, mặt cắt không cắt qua, nằm trong hộp, footprint song song đường cắt trong 1°.</summary>
        static void FindParallel(Job job, List<Member> live, SectionResult result)
        {
            var right = new P2(job.Right3.X, job.Right3.Y);
            foreach (var m in live)
            {
                if (m.Cut || m.Item.Type == nameof(PartType.HP) || m.FootAxis == null) continue;
                if (!SectionCurveRules.IsParallel(m.FootAxis.Value, right, ParallelDeg)) continue;
                var mine = result.Visible.Where(v => v.Handle == m.Item.Handle && !v.OnBoundary).ToList();
                var p = new ParallelPlate { Type = m.Item.Type, Visible = mine.Count > 0 };
                if (p.Visible)
                {
                    var longest = mine.OrderByDescending(v => Length(v.Curve)).First().Curve;
                    p.OutlineA = XY(longest.StartPoint);
                    p.OutlineB = XY(longest.EndPoint);
                }
                result.Parallel.Add(p);
            }
        }

        // ─── Chép / xoá tạm ────────────────────────────────────────────────────

        /// <summary>Bản chép WblockClone trong target: solid (khoá = id nguồn) + đối tượng phụ mới tạo (layer, linetype, material…).</summary>
        sealed class Clones
        {
            public readonly Dictionary<ObjectId, ObjectId> Primary = new Dictionary<ObjectId, ObjectId>();
            readonly List<ObjectId> _dependents = new List<ObjectId>();

            public void Clone(Database target, Detail3DSource src, List<SolidItem> items)
            {
                var ids = new ObjectIdCollection(items.Select(i => i.Id).ToArray());
                using (var map = new IdMapping())
                try
                {
                    using (LockSource(src.SourceDb))
                        src.SourceDb.WblockCloneObjects(ids, SymbolUtilityServices.GetBlockModelSpaceId(target), map,
                            DuplicateRecordCloning.Ignore, false);
                }
                finally
                {
                    // kể cả khi chép dở: ghi lại mọi thứ đã tạo để xoá
                    foreach (IdPair p in map)
                    {
                        if (!p.IsCloned || p.Value.IsNull) continue;
                        if (p.IsPrimary) Primary[p.Key] = p.Value;
                        else _dependents.Add(p.Value);
                    }
                }
            }

            /// <summary>Xoá trong transaction đang mở (đường thành công). Trả số đối tượng không xoá được.</summary>
            public int EraseAll(Transaction tr)
            {
                int fail = 0;
                foreach (var id in Primary.Values.Concat(_dependents))
                {
                    if (!id.IsValid || id.IsErased) continue;
                    try { tr.GetObject(id, OpenMode.ForWrite).Erase(); }
                    catch (System.Exception) { fail++; }
                }
                return fail;
            }

            /// <summary>Đường lỗi: transaction chính đã huỷ; xoá phần WblockClone còn sót trong transaction riêng.</summary>
            public void EraseLeftovers(Database target)
            {
                if (Primary.Count == 0 && _dependents.Count == 0) return;
                try
                {
                    using (Transaction tr = target.TransactionManager.StartTransaction())
                    {
                        int fail = EraseAll(tr);
                        tr.Commit();
                        if (fail > 0) System.Diagnostics.Debug.WriteLine($"[SectionEngine] cleanup: {fail} object(s) not erased");
                    }
                }
                catch (System.Exception ex)
                {
                    // đang trên đường lỗi: không che lỗi gốc, chỉ ghi debug
                    System.Diagnostics.Debug.WriteLine($"[SectionEngine] cleanup failed: {ex}");
                }
            }
        }

        /// <summary>File 3D đang mở trong AutoCAD ⇒ lock document đó khi đọc. Database phụ (không document) ⇒ null.</summary>
        static IDisposable LockSource(Database db)
        {
            Document d = null;
            foreach (Document doc in AcApp.DocumentManager)
                if (doc.Database == db) { d = doc; break; }
            if (d == null || d == AcApp.DocumentManager.MdiActiveDocument) return null;
            return d.LockDocument();
        }

        // ─── Hình học AutoCAD ──────────────────────────────────────────────────

        static DetailRegion To3D(Detail3DSource src, DetailRegion r)
        {
            Matrix3d m = src.PlanTo3D;
            if (r.IsCircle)
            {
                Point3d c = new Point3d(r.Center.X, r.Center.Y, 0).TransformBy(m);
                double rad = new Vector3d(r.Radius, 0, 0).TransformBy(m).Length;
                return DetailRegion.FromCircle(new P2(c.X, c.Y), rad);
            }
            return new DetailRegion { Corners = r.Corners.Select(p => XY(new Point3d(p.X, p.Y, 0).TransformBy(m))).ToArray() };
        }

        /// <summary>WCS 3D → plan, rồi dời Z để mặt ngang qua <paramref name="onPlane"/> về Z=0.</summary>
        static Matrix3d PlanOut(Detail3DSource src, Point3d onPlane)
        {
            Matrix3d inv = src.PlanTo3D.Inverse();
            double zp = onPlane.TransformBy(inv).Z;
            return Matrix3d.Displacement(new Vector3d(0, 0, -zp)) * inv;
        }

        static bool Meets(ClipBox box, SolidItem s) =>
            box.Meets(s.Extents.MinPoint.X, s.Extents.MinPoint.Y, s.Extents.MinPoint.Z,
                      s.Extents.MaxPoint.X, s.Extents.MaxPoint.Y, s.Extents.MaxPoint.Z);

        static Solid3d MakeBox(Job j)
        {
            var box = Prism(j.Region3, j.ZMin, j.ZMax);
            if (j.Slab == null) return box;
            try
            {
                using (var slab = RectPrism(j.Slab, j.ZMin, j.ZMax))
                    box.BooleanOperation(BooleanOperationType.BoolIntersect, slab);
                if (box.IsNull) throw new InvalidOperationException("the section line and the detail region do not overlap");
                return box;
            }
            catch
            {
                box.Dispose();
                throw;
            }
        }

        static Solid3d Prism(DetailRegion r3, double zMin, double zMax)
        {
            if (!r3.IsCircle) return RectPrism(r3.Corners, zMin, zMax);
            var s = new Solid3d();
            s.CreateFrustum(zMax - zMin, r3.Radius, r3.Radius, r3.Radius);   // trụ quanh gốc, trục Z
            s.TransformBy(Matrix3d.Displacement(new Vector3d(r3.Center.X, r3.Center.Y, (zMin + zMax) / 2)));
            return s;
        }

        static Solid3d RectPrism(IList<P2> corners, double zMin, double zMax)
        {
            var f = ClipBox.RectFrame(corners);
            var s = new Solid3d();
            s.CreateBox(f.lenU, f.lenV, zMax - zMin);   // hộp quanh gốc
            var u = new Vector3d(f.u.X, f.u.Y, 0);
            var v = Vector3d.ZAxis.CrossProduct(u);
            s.TransformBy(Matrix3d.AlignCoordinateSystem(
                Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                new Point3d(f.center.X, f.center.Y, (zMin + zMax) / 2), u, v, Vector3d.ZAxis));
            return s;
        }

        /// <summary>Khoảng có dấu của solid theo look (so với mặt cắt): đưa look về trục X, đọc extents, đưa lại.</summary>
        static (double min, double max) SignedRange(Solid3d s, Point3d origin, Vector3d look)
        {
            Vector3d y = look.GetPerpendicularVector();
            Vector3d z = look.CrossProduct(y);
            Matrix3d toLocal = Matrix3d.AlignCoordinateSystem(origin, look, y, z,
                Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis);
            s.TransformBy(toLocal);
            try
            {
                Extents3d e = s.GeometricExtents;
                return (e.MinPoint.X, e.MaxPoint.X);
            }
            finally { s.TransformBy(toLocal.Inverse()); }
        }

        /// <summary>Trục dài footprint: cắt ngang solid (chưa cắt hộp) ở Z giữa → MinAreaRect.</summary>
        static P2? FootAxis(Solid3d s, double z)
        {
            var pts = new List<P2>();
            using (Region r = s.GetSection(new Plane(new Point3d(0, 0, z), Vector3d.ZAxis)))
            {
                if (r == null) return null;
                Consume(ExplodeRegion(r), c => pts.AddRange(Sample3(c).Select(XY)));
            }
            if (pts.Count < 3) return null;
            return SectionMarkRules.MinAreaRect(pts).axis;
        }

        static List<Curve> ExplodeRegion(Region r)
        {
            var res = new List<Curve>();
            var coll = new DBObjectCollection();
            r.Explode(coll);
            foreach (DBObject o in coll)
            {
                if (o is Curve c) res.Add(c);
                else if (o is Region sub) { using (sub) res.AddRange(ExplodeRegion(sub)); }
                else o.Dispose();
            }
            return res;
        }

        /// <summary>Đường cong từ mảng kết quả Section; Region ⇒ tách ra đường; còn lại (Hatch…) giải phóng.</summary>
        static IEnumerable<Curve> Curves(Array arr)
        {
            var res = new List<Curve>();
            if (arr == null) return res;
            foreach (object x in arr)
            {
                if (x is Curve c) res.Add(c);
                else if (x is Region r) { using (r) res.AddRange(ExplodeRegion(r)); }
                else if (x is DBObject o) o.Dispose();
            }
            return res;
        }

        /// <summary>Chạy act trên từng đường rồi giải phóng TẤT CẢ (kể cả khi act ném) — đối tượng ngoài database không để GC dọn.</summary>
        static void Consume(IEnumerable<Curve> curves, Action<Curve> act)
        {
            var list = curves.ToList();
            try { foreach (var c in list) act(c); }
            finally { foreach (var c in list) if (!c.IsDisposed) c.Dispose(); }
        }

        static void DisposeAll(Array arr)
        {
            if (arr == null) return;
            foreach (object x in arr) if (x is DBObject o && o.ObjectId.IsNull) o.Dispose();
        }

        /// <summary>Cạnh Brep → Curve database (Line dự phòng giữa 2 đầu mút), như spike.</summary>
        static Curve ToDbCurve(Curve3d gc)
        {
            Curve ent = null;
            try { ent = Curve.CreateFromGeCurve(gc); } catch (System.Exception) { ent = null; }
            if (ent == null && gc is ExternalCurve3d ext && ext.IsNativeCurve)
            {
                Curve3d nc = ext.NativeCurve;
                if (nc is LinearEntity3d) ent = new Line(gc.StartPoint, gc.EndPoint);
                else { try { ent = Curve.CreateFromGeCurve(nc); } catch (System.Exception) { ent = null; } }
            }
            return ent ?? new Line(gc.StartPoint, gc.EndPoint);
        }

        /// <summary>Điểm trên đường cong theo sai số dây cung 0.1 (Line: 2 đầu; Polyline: theo từng đoạn).</summary>
        static List<Point3d> Sample3(Curve c)
        {
            if (c is Line l) return new List<Point3d> { l.StartPoint, l.EndPoint };
            double t0 = c.StartParam, t1 = c.EndParam;
            var cuts = new List<double> { t0 };
            if (c is Polyline || c is Polyline2d || c is Polyline3d)
                for (double k = Math.Floor(t0) + 1; k < t1 - 1e-9; k++) cuts.Add(k);
            cuts.Add(t1);
            var res = new List<Point3d>();
            for (int i = 1; i < cuts.Count; i++)
            {
                var ts = SectionCurveRules.AdaptiveParams(cuts[i - 1], cuts[i], (a, m, b) =>
                    Dev(c.GetPointAtParameter(m), c.GetPointAtParameter(a), c.GetPointAtParameter(b)), ChordErr);
                for (int k = i == 1 ? 0 : 1; k < ts.Count; k++) res.Add(c.GetPointAtParameter(ts[k]));
            }
            return res;
        }

        static List<P2> Sample2(Curve c) => Sample3(c).Select(XY).ToList();

        static double Dev(Point3d p, Point3d a, Point3d b)
        {
            Vector3d ab = b - a;
            double l2 = ab.DotProduct(ab);
            if (l2 < 1e-24) return p.DistanceTo(a);
            double t = Math.Max(0, Math.Min(1, (p - a).DotProduct(ab) / l2));
            return p.DistanceTo(a + ab * t);
        }

        /// <summary>Dời đường cong phẳng song song XY về Z=0 (sai số số học sau phép chiếu).</summary>
        static void Flatten(Curve c)
        {
            double z0 = c.StartPoint.Z, z1 = c.EndPoint.Z;
            if (Math.Abs(z0) > 1e-9 && Math.Abs(z1 - z0) < 1e-6)
                c.TransformBy(Matrix3d.Displacement(new Vector3d(0, 0, -z0)));
        }

        static Polyline ToPolyline(List<P2> pts)
        {
            var pl = new Polyline();
            for (int i = 0; i < pts.Count; i++) pl.AddVertexAt(i, new Point2d(pts[i].X, pts[i].Y), 0, 0, 0);
            return pl;
        }

        static List<P2> Closed(List<P2> loop)
        {
            var l = new List<P2>(loop) { loop[0] };
            return l;
        }

        static double Length(Curve c)
        {
            try { return c.GetDistanceAtParameter(c.EndParam) - c.GetDistanceAtParameter(c.StartParam); }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return c.StartPoint.DistanceTo(c.EndPoint); }
        }

        static P2 XY(Point3d p) => new P2(p.X, p.Y);
    }
}
