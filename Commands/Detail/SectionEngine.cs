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
        public List<ThicknessLabel> Labels = new List<ThicknessLabel>();   // chỉ Section: "(t)" cho tấm song song nhìn thấy
        public List<string> Warnings = new List<string>();

        /// <summary>Giải phóng các đường chưa đưa vào bản vẽ.</summary>
        public void DisposeCurves()
        {
            foreach (var v in Visible)
                if (v.Curve != null && !v.Curve.IsDisposed && v.Curve.ObjectId.IsNull) v.Curve.Dispose();
        }
    }

    /// <summary>Các vòng viền Top Plate trong vùng (mép ngoài + lỗ khoét đã dựng 3D), toạ độ plan; rỗng ⇒ không có.</summary>
    public sealed class TopPlateResult
    {
        public List<List<P2>> Loops = new List<List<P2>>();
        public List<string> Warnings = new List<string>();
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
        const double PlanCutFallback = -1.0;   // mặt cắt Detail Plan khi không có Top Plate
        const double ParallelDeg = 1.0;
        // Lọc nét khuất bỏ sót (OcclusionRules): bước thử, bỏ 2 đầu, lệch ngang tia, tia bắt đầu trước mặt, vượt mặt cắt
        const double OccStep = 20.0, OccInset = 0.5, OccSide = 0.3, OccEps = 0.5, OccFar = 10.0;
        const double OccSeamDeg = 10.0, OccMinLen = 1.0, OccDeep = 100000.0;
        const int OccMaxHits = 64;
        const int OccRefine = 7;
        // "(t)": số ứng viên thử tối đa / giữ lại, lệch độ sâu cho phép so mặt trước tấm, lùi góc khung chữ vào trong
        const int LabelTry = 200, LabelKeep = 12;
        const double LabelDepthTol = 1.0, LabelInset = 2.0;

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
            public double FootWidth;          // Section: bề rộng footprint = chiều dày tấm đứng
            public bool Cut;
            public readonly List<IList<P2>> Edges2D = new List<IList<P2>>();   // cạnh chiếu riêng (gán đường thấy)
            public readonly List<Curve> Seams = new List<Curve>();             // cạnh Brep 3D (in lên khối gộp)
        }

        // ─── API ───────────────────────────────────────────────────────────────

        /// <summary>Detail Plan: mặt cắt Z = đáy Top Plate − 1 (không có Top Plate ⇒ −1 + cảnh báo) nhìn −Z, hộp = lăng trụ vùng
        /// Z∈[đáy kết cấu, mặt cắt]; bỏ Top Plate. Toạ độ ra = toạ độ plan.</summary>
        public static SectionResult Plan(Database target, Detail3DSource src, DetailRegion regionPlan)
        {
            var region3 = To3D(src, regionPlan);
            double? topCut = ClipBox.PlanCutZ(src.Solids.Where(s => s.Type == nameof(PartType.TopPlate)).Select(s => s.Extents.MinPoint.Z));
            double cutZ = topCut ?? PlanCutFallback;   // WCS 3D
            double minZ = src.Solids.Count == 0 ? cutZ : src.Solids.Min(s => s.Extents.MinPoint.Z);
            double zMin = Math.Min(minZ, cutZ) - ZMargin;
            var box = ClipBox.Plan(region3, zMin, cutZ);
            var b = box.Bounds();
            var c = new Point3d((b.minX + b.maxX) / 2, (b.minY + b.maxY) / 2, cutZ);
            var job = new Job
            {
                Box = box, Region3 = region3, ZMin = zMin, ZMax = cutZ,
                SecP1 = c, SecP2 = c + Vector3d.XAxis * 100, SecVertical = Vector3d.YAxis, View = -Vector3d.ZAxis,
                PlaneOrigin = c, Look3 = -Vector3d.ZAxis, Right3 = Vector3d.XAxis,
                ToOut = PlanOut(src, c),
            };
            var items = src.Solids.Where(s => s.Type != nameof(PartType.TopPlate) && Meets(box, s)).ToList();
            SectionResult result = Run(target, src, items, job);
            if (topCut == null) result.Warnings.Add("no top plate in the 3D file; the plan cut is at Z = -1");
            return result;
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

        /// <summary>Viền Top Plate trong vùng (cắt ngang giữa chiều dày, đáy + t/2, đã cắt theo vùng), toạ độ plan; mọi vòng (kể cả lỗ khoét).
        /// Outline rỗng ⇒ không có. Lỗi từng phần ⇒ Warnings.</summary>
        public static TopPlateResult TopPlateOutline(Database target, Detail3DSource src, DetailRegion regionPlan)
        {
            var res = new TopPlateResult();
            var plates = src.Solids.Where(s => s.Type == nameof(PartType.TopPlate)).ToList();
            if (plates.Count == 0) return res;
            double zCut = ClipBox.TopPlateCutZ(plates.Min(s => s.Extents.MinPoint.Z), plates.Max(s => s.Extents.MaxPoint.Z), src.TopPlateThickness);
            var region3 = To3D(src, regionPlan);
            double zMin = plates.Min(s => s.Extents.MinPoint.Z) - ZMargin, zMax = plates.Max(s => s.Extents.MaxPoint.Z) + ZMargin;
            var box = ClipBox.Plan(region3, zMin, zMax);
            var items = plates.Where(s => Meets(box, s)).ToList();
            if (items.Count == 0) return res;
            var b = box.Bounds();
            Matrix3d toOut = PlanOut(src, new Point3d((b.minX + b.maxX) / 2, (b.minY + b.maxY) / 2, zCut));

            var clones = new Clones(target);
            Solid3d template = null;
            try
            {
                template = Prism(region3, zMin, zMax);
                var nodKeys = NodKeys(target);   // khoá NOD có sẵn TRƯỚC khi chép
                clones.Clone(target, src, items);
                Warn(res.Warnings, items.Count - clones.Primary.Count, "top plate solid(s) could not be copied from the 3D file");
                var loops = new List<List<P2>>();
                int open = 0;
                using (Transaction tr = target.TransactionManager.StartTransaction())
                {
                    Solid3d union = null;
                    int clipFail = 0, uniteFail = 0;
                    foreach (var pair in clones.Primary)
                    {
                        var s = (Solid3d)tr.GetObject(pair.Value, OpenMode.ForWrite);
                        s.TransformBy(src.BlockToWcs);
                        try { using (var bx = (Solid3d)template.Clone()) s.BooleanOperation(BooleanOperationType.BoolIntersect, bx); }
                        catch (System.Exception) { clipFail++; continue; }   // bản chép vẫn bị xoá cuối
                        if (s.IsNull) continue;
                        if (union == null) { union = s; continue; }
                        try { union.BooleanOperation(BooleanOperationType.BoolUnite, s); }
                        catch (System.Exception) { uniteFail++; }
                    }
                    Warn(res.Warnings, clipFail, "top plate solid(s) could not be clipped to the detail region and were skipped");
                    Warn(res.Warnings, uniteFail, "top plate solid(s) could not be merged and were skipped");
                    if (union != null)
                    {
                        var pieces = new List<List<P2>>();
                        try
                        {
                            using (Region r = union.GetSection(new Plane(new Point3d(0, 0, zCut), Vector3d.ZAxis)))
                                if (r != null) Consume(ExplodeRegion(r), cv => { cv.TransformBy(toOut); pieces.Add(Sample2(cv)); });
                        }
                        catch (System.Exception ex) { res.Warnings.Add("the top plate could not be sectioned: " + ex.Message); }
                        loops = SectionCurveRules.ChainLoops(pieces, JoinTol, out open);
                    }
                    Warn(res.Warnings, open, "top plate outline(s) did not close");
                    Warn(res.Warnings, clones.EraseAll(tr), "temporary object(s) could not be erased");
                    RemoveNewNodEntries(tr, target, nodKeys, clones, res.Warnings);
                    tr.Commit();
                }
                // Mọi vòng: mép ngoài + lỗ khoét (CutOut đã trừ vào solid Top Plate khi Build 3D)
                res.Loops = loops.Where(l => l.Count >= 3).ToList();
                return res;
            }
            catch (System.Exception ex)
            {
                var wrapped = CleanupFailure(ex, clones.EraseLeftovers(target));
                if (wrapped != null) throw wrapped;
                throw;
            }
            finally
            {
                template?.Dispose();
            }
        }

        static void Warn(List<string> warnings, int n, string text)
        {
            string w = SectionCleanupRules.Count(n, text);
            if (w != null) warnings.Add(w);
        }

        // ─── Pipeline ──────────────────────────────────────────────────────────

        static SectionResult Run(Database target, Detail3DSource src, List<SolidItem> items, Job job)
        {
            var result = new SectionResult();
            if (items.Count == 0) return result;

            var clones = new Clones(target);
            var members = new List<Member>();
            Solid3d template = null;
            try
            {
                template = MakeBox(job);
                // 1. Chép solid nguồn vào bản vẽ 2D (WblockCloneObjects; Clone() chéo database ⇒ eWrongDatabase)
                var nodKeys = NodKeys(target);   // khoá NOD có sẵn TRƯỚC khi chép / thêm Section (Section manager tự tạo khi chưa có)
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
                            try { (m.FootAxis, m.FootWidth) = FootAxis(s, (item.Extents.MinPoint.Z + item.Extents.MaxPoint.Z) / 2); }
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
                    Warn(result.Warnings, clones.EraseAll(tr), "temporary object(s) could not be erased");
                    // SAU EraseAll: dictionary mới do chép / Section tạo, nay rỗng ⇒ bỏ luôn (chỉ mục mới + rỗng)
                    RemoveNewNodEntries(tr, target, nodKeys, clones, result.Warnings);
                    tr.Commit();
                }
                return result;
            }
            catch (System.Exception ex)
            {
                result.DisposeCurves();
                var wrapped = CleanupFailure(ex, clones.EraseLeftovers(target));
                if (wrapped != null) throw wrapped;
                throw;
            }
            finally
            {
                template?.Dispose();
                foreach (var m in members) foreach (var c in m.Seams) if (!c.IsDisposed) c.Dispose();
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

            // 7 (phần section): settings TRƯỚC (có thể nằm trong extension dictionary của section ⇒ xoá section là xoá luôn), rồi section
            var so = tr.GetObject(settingsId, OpenMode.ForWrite, true);
            if (!so.IsErased) so.Erase();
            if (!sec.IsErased) sec.Erase();
        }

        static HashSet<string> NodKeys(Database target)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (Transaction tr = target.TransactionManager.StartOpenCloseTransaction())
            {
                foreach (DBDictionaryEntry e in (DBDictionary)tr.GetObject(target.NamedObjectsDictionaryId, OpenMode.ForRead)) keys.Add(e.Key);
                tr.Commit();
            }
            return keys;
        }

        /// <summary>Khoá NOD do lần chạy này tạo (Section manager…) và nay rỗng ⇒ bỏ; khoá có sẵn không bao giờ đụng.</summary>
        static void RemoveNewNodEntries(Transaction tr, Database target, HashSet<string> before, Clones clones, List<string> warnings)
        {
            var nod = (DBDictionary)tr.GetObject(target.NamedObjectsDictionaryId, OpenMode.ForRead);
            var added = new List<DBDictionaryEntry>();
            foreach (DBDictionaryEntry e in nod) if (!before.Contains(e.Key)) added.Add(e);
            int fail = 0;
            foreach (var e in added)
            {
                try
                {
                    if (!clones.IsNew(e.Value)) continue;
                    var obj = tr.GetObject(e.Value, OpenMode.ForWrite, true);
                    if (obj.IsErased) continue;
                    if (LiveItems(obj) > 0) continue;   // còn nội dung ⇒ giữ
                    nod.UpgradeOpen();
                    nod.Remove(e.Key);
                    obj.Erase();
                }
                catch (System.Exception) { fail++; }
            }
            Warn(warnings, fail, "temporary drawing dictionary entr(ies) could not be removed");
        }

        /// <summary>Số phần tử còn sống trong một đối tượng chứa (DBDictionary, Section manager); không phải đối tượng chứa ⇒ 0.</summary>
        static int LiveItems(DBObject obj)
        {
            if (!(obj is System.Collections.IEnumerable en)) return 0;
            int n = 0;
            foreach (object x in en)
            {
                ObjectId id = x is ObjectId oid ? oid : x is DBDictionaryEntry de ? de.Value : ObjectId.Null;
                if (id.IsNull || (!id.IsErased && id.IsValid)) n++;
            }
            return n;
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
                                if (job.Box.OnSideFace(pts.Select(p => (new P2(p.X, p.Y), p.Z)).ToList(), Tol, includeBack: false))
                                    boundary.Add(pts.Select(p => XY(p.TransformBy(job.ToOut))).ToList());
                            }
                            catch (System.Exception) { edgeFail++; }
                        }
                }
                catch (System.Exception) { edgeFail++; }
            }
            if (edgeFail > 0) result.Warnings.Add($"{edgeFail} solid edge(s) could not be read (some joint lines may be missing)");

            // 5. Gộp mọi tấm rồi in cạnh từng tấm lên khối gộp. Gộp trên BẢN SAO: lần gộp lỗi hoặc thể tích vô lý ⇒ bỏ bản sao,
            //    giữ khối trước, tấm đó để riêng (DET.19 2026-11-19: một lần gộp lỗi làm khối gộp mất cả flange + bầu HP ⇒
            //    thiếu nét flange, cạnh bracket dưới HP lại hiện). Tấm gốc không bị gộp tiêu ⇒ còn nguyên để cắt riêng ở 6.
            var united = new List<Member> { live[0] };
            var apart = new List<Member>();
            Solid3d union = (Solid3d)((Solid3d)tr.GetObject(live[0].Id, OpenMode.ForRead)).Clone();
            try
            {
                double? unionVol = Volume(union);
                for (int i = 1; i < live.Count; i++)
                {
                    var part = (Solid3d)tr.GetObject(live[i].Id, OpenMode.ForRead);
                    double? partVol = Volume(part);
                    Solid3d merged = null;
                    bool ok = false;
                    try
                    {
                        merged = (Solid3d)union.Clone();
                        using (var operand = (Solid3d)part.Clone()) merged.BooleanOperation(BooleanOperationType.BoolUnite, operand);
                        double? v = Volume(merged);
                        ok = SectionCleanupRules.UnionPlausible(unionVol, partVol, v);
                        if (ok) unionVol = v;
                    }
                    catch (System.Exception) { }
                    if (ok) { union.Dispose(); union = merged; united.Add(live[i]); }
                    else { merged?.Dispose(); apart.Add(live[i]); }
                }
                var owner = (BlockTableRecord)tr.GetObject(sec.OwnerId, OpenMode.ForWrite);
                owner.AppendEntity(union);   // resident như tấm chép; xoá cuối bước 6 (lỗi ⇒ transaction huỷ ⇒ tự mất)
                tr.AddNewlyCreatedDBObject(union, true);
            }
            catch
            {
                if (union.ObjectId.IsNull) union.Dispose();
                throw;
            }
            if (apart.Count > 0)
                result.Warnings.Add($"{apart.Count} solid(s) could not be merged ({string.Join(", ", apart.Select(m => m.Item.Type + " " + m.Item.Handle))}); lines they hide may still show");
            int imp = 0, impFail = 0;
            foreach (var m in united)
                foreach (var c in m.Seams)
                {
                    try { union.ImprintEntity(c); imp++; }
                    catch (System.Exception) { impFail++; }   // spike: vài cạnh eGeneralModelingFailure, vô hại
                }
            if (impFail > 0) result.Warnings.Add($"{impFail} of {imp + impFail} edge(s) could not be imprinted (some joint lines may be missing)");

            // 6. Section khối gộp: đường thấy = background + tangency (bỏ boundary của khối gộp)
            //    Khối gộp không cắt được ⇒ cắt từng tấm đã gộp (tấm gốc còn nguyên), nét khuất giữa các tấm do bước 6b bắn tia lọc.
            Solid3d Of(Member m) => (Solid3d)tr.GetObject(m.Id, OpenMode.ForRead);
            var raw = new List<Curve>();
            bool Take(Solid3d solid)
            {
                Array bnd, fill, bg, fg, tan;
                try { sec.GenerateSectionGeometry(solid, out bnd, out fill, out bg, out fg, out tan); }
                catch (System.Exception) { return false; }
                DisposeAll(bnd); DisposeAll(fill); DisposeAll(fg);
                raw.AddRange(Curves(bg)); raw.AddRange(Curves(tan));
                return true;
            }
            var occluders = new List<Solid3d> { union };
            if (!Take(union))
            {
                occluders = united.Select(Of).ToList();
                int pieceFail = occluders.Count(s => !Take(s));
                result.Warnings.Add(pieceFail == 0
                    ? "the merged solid could not be sectioned; solids were sectioned one by one"
                    : $"the merged solid could not be sectioned; {pieceFail} solid(s) are missing their visible lines");
            }
            int apartFail = apart.Count(m => !Take(Of(m)));
            Warn(result.Warnings, apartFail, "unmerged solid(s) could not be sectioned; their visible lines are missing");
            occluders.AddRange(apart.Select(Of));

            var memberEdges = live.Select(m => (IList<IList<P2>>)m.Edges2D).ToList();
            void Emit(Curve c, List<P2> pts)
            {
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

            // 6b. Nét tiếp tuyến (vd bầu HP) không qua bước bỏ nét khuất của AutoCAD ⇒ tự kiểm bằng tia (kỹ sư 2026-11-18, 6A-6A)
            int next = 0;
            using (var occ = new Occluder(job, occluders, live.SelectMany(m => m.Seams)))
            {
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
                        var parts = occ.Clip(c, pts);
                        if (parts == null) { Emit(c, pts); continue; }
                        c.Dispose();
                        foreach (var p in parts) Emit(p, Sample2(p));
                    }
                }
                catch
                {
                    // đường chưa vào result: giải phóng ngay (result do Run giải phóng)
                    for (int i = next; i < raw.Count; i++) if (!raw[i].IsDisposed) raw[i].Dispose();
                    throw;
                }
                if (job.IsSection) FindParallel(job, live, result, occ);   // trước Warn: "(t)" cũng bắn tia
                Warn(result.Warnings, occ.Failures, "hidden-line check(s) failed; some hidden lines may still show");
            }
            if (!union.IsErased) union.Erase();   // khối gộp tạm (bước 5)
        }

        /// <summary>Thể tích solid; không đo được (khối hỏng) ⇒ null.</summary>
        static double? Volume(Solid3d s)
        {
            try { return s.IsNull ? (double?)null : s.MassProperties.Volume; }
            catch (System.Exception) { return null; }
        }

        /// <summary>Tấm không phải HP, mặt cắt không cắt qua, nằm trong hộp, footprint song song đường cắt trong 1°.</summary>
        static void FindParallel(Job job, List<Member> live, SectionResult result, Occluder occ)
        {
            // "$" (kỹ sư 2026-11-18): nét đứng của tấm song song — nét nối giữa hai tấm song song và nét đầu tấm; không cho
            // face plate / tấm boong / HP / FB, không cho đầu tấm nối vào HP/FB. Mỗi nét một "$".
            var right = new P2(job.Right3.X, job.Right3.Y);
            var parallel = live.Where(m => !m.Cut && m.FootAxis != null && SectionSymbolRules.TakesWeldTrace(m.Item.Type)
                                           && SectionCurveRules.IsParallel(m.FootAxis.Value, right, ParallelDeg)).ToList();
            var candidates = new List<(IList<P2>, bool)>();
            foreach (var m in parallel)
            {
                // nét NỐI = cũng là cạnh của một tấm song song khác (khác với nét bắt đầu vát của chính tấm)
                var otherEdges = parallel.Where(o => !ReferenceEquals(o, m)).SelectMany(o => o.Edges2D).Select(e => (IList<P2>)e).ToList();
                foreach (var v in result.Visible.Where(v => v.Handle == m.Item.Handle && !v.OnBoundary))
                {
                    var pts = Sample2(v.Curve);
                    bool joint = otherEdges.Count > 0 && SectionCurveRules.Covers(SectionCurveRules.Densify(pts, CoverStep), otherEdges, Tol);
                    candidates.Add((pts, joint));
                }
            }
            // "(t)" (kỹ sư 2026-11-18): chỉ cho tấm KHÔNG thể hiện chiều dày trên mặt cắt = tấm song song nhìn thấy (không cho
            // tấm cắt, tấm xiên, HP/FB, tấm nằm ngang)
            foreach (var m in parallel) AddLabel(m, occ, result);
            if (candidates.Count == 0) return;
            var blockers = live.Where(o => o.Item.Type == nameof(PartType.HP) || o.Item.Type == nameof(PartType.FlatBar))
                .SelectMany(o => o.Edges2D).Select(e => (IList<P2>)e).ToList();
            // đầu tấm tì vào tấm bị cắt (nét trùng / chạm viền vết cắt) cũng không "$" — kỹ sư 2026-11-18, 9B-9B x = -82.5
            var cutOutlines = result.Cut.Where(c => c.Outline != null && c.Outline.Count >= 3).Select(c => (IList<P2>)Closed(c.Outline)).ToList();
            blockers.AddRange(cutOutlines);
            // "$" chỉ cho nét chạy suốt giữa 2 tấm đỡ (mẫu tay 2026-11-19): vết cắt + cạnh tấm nằm ngang (Top Plate / Flange /
            // Face Plate) song song mặt cắt
            var supports = new List<IList<P2>>(cutOutlines);
            supports.AddRange(live.Where(o => o.Item.Type == nameof(PartType.TopPlate) || o.Item.Type == nameof(PartType.Flange)
                                              || o.Item.Type == nameof(PartType.FacePlate))
                .SelectMany(o => o.Edges2D).Select(e => (IList<P2>)e));
            foreach (var (a, b) in SectionSymbolRules.WeldTraceLines(candidates, blockers, Tol, CoverStep, supports: supports))
                result.Parallel.Add(new ParallelPlate { Visible = true, OutlineA = a, OutlineB = b });
        }

        /// <summary>Chữ "(t)" của tấm song song <paramref name="m"/>: chiều dày = bề rộng footprint (thật, không phóng đại như vết
        /// cắt xiên); chỗ đặt = ứng viên trong khung hình chiếu của tấm mà tia tại tâm và 4 góc khung chữ đều trúng mặt trước của
        /// chính tấm (không bị tấm khác che, không rơi vào lỗ). Không còn chỗ nào thấy ⇒ không ghi.</summary>
        static void AddLabel(Member m, Occluder occ, SectionResult result)
        {
            if (m.FootWidth <= 0 || m.Edges2D.Count == 0) return;
            var pts = m.Edges2D.SelectMany(e => e).ToList();
            if (pts.Count == 0) return;
            var min = new P2(pts.Min(p => p.X), pts.Min(p => p.Y));
            var max = new P2(pts.Max(p => p.X), pts.Max(p => p.Y));
            string text = SectionSymbolRules.ThicknessText(m.FootWidth);
            double h = DetailComposeRules.ThicknessTextH, w = DetailComposeRules.TextWidth(text, h);
            double? front = occ.Front(m.Seams);
            var label = new ThicknessLabel { Text = text };
            int tried = 0;
            foreach (var c in DetailComposeRules.LabelSpots(min, max, w, h))
            {
                if (tried++ >= LabelTry || label.Spots.Count >= LabelKeep) break;
                double dx = w / 2 - LabelInset, dy = h / 2 - LabelInset;
                var probe = new[] { c, c + new P2(-dx, -dy), c + new P2(dx, -dy), c + new P2(dx, dy), c + new P2(-dx, dy) };
                if (probe.All(p => occ.OnFace(p, front))) label.Spots.Add(c);
            }
            if (label.Spots.Count > 0) result.Labels.Add(label);
        }

        // ─── Chép / xoá tạm ────────────────────────────────────────────────────

        /// <summary>Bản chép WblockClone trong target: solid (khoá = id nguồn) + đối tượng phụ mới tạo (layer, linetype, material…).</summary>
        sealed class Clones
        {
            public readonly Dictionary<ObjectId, ObjectId> Primary = new Dictionary<ObjectId, ObjectId>();
            readonly List<ObjectId> _dependents = new List<ObjectId>();

            /// <summary>Handseed của target trước lần chạy: handle nhỏ hơn = bản ghi có sẵn, không bao giờ xoá.</summary>
            public long Seed { get; }

            public Clones(Database target) { Seed = target.Handseed.Value; }

            public bool IsNew(ObjectId id) => SectionCleanupRules.CreatedThisRun(Seed, id.Handle.Value);

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
                    if (!IsNew(id)) continue;   // chặn độc lập với IsCloned: bản ghi có sẵn (layer, style…) không bao giờ bị xoá
                    try { tr.GetObject(id, OpenMode.ForWrite).Erase(); }
                    catch (System.Exception) { fail++; }
                }
                return fail;
            }

            /// <summary>Đường lỗi: transaction chính đã huỷ; xoá phần WblockClone còn sót trong transaction riêng.
            /// Trả mô tả lỗi dọn dẹp (null = sạch) để gắn vào lỗi gốc.</summary>
            public string EraseLeftovers(Database target)
            {
                if (Primary.Count == 0 && _dependents.Count == 0) return null;
                try
                {
                    using (Transaction tr = target.TransactionManager.StartTransaction())
                    {
                        int fail = EraseAll(tr);
                        tr.Commit();
                        return fail > 0 ? $"{fail} temporary object(s) could not be erased" : null;
                    }
                }
                catch (System.Exception ex)
                {
                    return "temporary objects could not be erased: " + ex.Message;
                }
            }
        }

        /// <summary>Đường lỗi: dọn dẹp; nếu dọn dẹp cũng lỗi thì bọc cả hai vào thông báo (lỗi gốc là InnerException), không thì ném lại nguyên lỗi gốc.</summary>
        static System.Exception CleanupFailure(System.Exception original, string cleanup) =>
            cleanup == null ? null : new InvalidOperationException(SectionCleanupRules.FailureMessage(original.Message, cleanup), original);

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
        static (P2? axis, double width) FootAxis(Solid3d s, double z)
        {
            var pts = new List<P2>();
            using (Region r = s.GetSection(new Plane(new Point3d(0, 0, z), Vector3d.ZAxis)))
            {
                if (r == null) return (null, 0);
                Consume(ExplodeRegion(r), c => pts.AddRange(Sample3(c).Select(XY)));
            }
            if (pts.Count < 3) return (null, 0);
            var rect = SectionMarkRules.MinAreaRect(pts);
            return (rect.axis, rect.width);
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

        /// <summary>Bỏ phần nét bị solid che (toạ độ ra). Độ sâu nét = cạnh Brep 3D chạy dọc nó (gần nhất); tia từ ngay trước
        /// điểm đó bắn về phía người nhìn tới quá mặt cắt; trúng khối gộp/khối rời ⇒ khuất. Thử thêm 2 tia lệch ngang ±OccSide
        /// (nét nằm sát mép tấm khác: tia giữa lướt mặt tấm đó). Không tìm được độ sâu / lỗi ⇒ giữ nét.</summary>
        sealed class Occluder : IDisposable
        {
            readonly List<Autodesk.AutoCAD.BoundaryRepresentation.Brep> breps = new List<Autodesk.AutoCAD.BoundaryRepresentation.Brep>();
            readonly List<IList<P3>> seams = new List<IList<P3>>();
            readonly Matrix3d inv, toOut;
            readonly double sgn;
            readonly bool enabled;
            public int Failures;

            public Occluder(Job job, IEnumerable<Solid3d> solids, IEnumerable<Curve> seamCurves)
            {
                Vector3d v = job.View.TransformBy(job.ToOut).GetNormal();
                if (Math.Abs(v.Z) < 0.999) return;   // hướng nhìn không vuông góc mặt bản vẽ ⇒ không lọc
                sgn = v.Z < 0 ? 1 : -1;               // độ gần = sgn · Zra (mặt cắt ở Z = 0)
                inv = job.ToOut.Inverse();
                toOut = job.ToOut;
                foreach (var c in seamCurves)
                {
                    try
                    {
                        seams.Add(Sample3(c).Select(p => { var o = p.TransformBy(job.ToOut); return new P3(o.X, o.Y, sgn * o.Z); }).ToList());
                    }
                    catch (System.Exception) { Failures++; }
                }
                foreach (var s in solids)
                {
                    try { breps.Add(new Autodesk.AutoCAD.BoundaryRepresentation.Brep(s)); }
                    catch (System.Exception) { Failures++; }
                }
                enabled = breps.Count > 0 && seams.Count > 0;
            }

            /// <summary>null = giữ nguyên <paramref name="c"/>; danh sách (có thể rỗng) = các khúc thấy thay cho c.</summary>
            public List<Curve> Clip(Curve c, List<P2> pts)
            {
                if (!enabled || pts.Count < 2) return null;
                double len = OcclusionRules.Length(pts);
                if (len < 1e-6) return null;
                var spans = OcclusionRules.VisibleSpans(len, s =>
                {
                    var (p, d) = OcclusionRules.At(pts, s);
                    return VisibleAt(p, d);
                }, OccStep, OccInset, OccRefine, OccMinLen);
                if (spans.Count == 1 && spans[0].S0 <= 1e-9 && spans[0].S1 >= len - 1e-9) return null;
                if (spans.Count == 0) return new List<Curve>();
                if (c.Closed) return null;   // nét kín khuất một phần: giữ nguyên (hiếm; tách kín dễ sai)
                try { return Split(c, len, spans); }
                catch (System.Exception) { Failures++; return null; }
            }

            static List<Curve> Split(Curve c, double len, List<(double S0, double S1)> spans)
            {
                double total = Length(c), k = total / len;
                var ps = new DoubleCollection();
                foreach (var (a, b) in spans)
                {
                    if (a > 1e-6) ps.Add(c.GetParameterAtDistance(Math.Min(a * k, total)));
                    if (b < len - 1e-6) ps.Add(c.GetParameterAtDistance(Math.Min(b * k, total)));
                }
                var keep = new List<Curve>();
                if (ps.Count == 0) return null;
                foreach (DBObject o in c.GetSplitCurves(ps))
                {
                    var piece = (Curve)o;
                    double pl = Length(piece);
                    Point3d mid = piece.GetPointAtDist(pl / 2);
                    double s = c.GetDistAtPoint(c.GetClosestPointTo(mid, false)) / k;
                    if (pl >= OccMinLen && spans.Any(sp => s >= sp.S0 && s <= sp.S1)) keep.Add(piece);
                    else piece.Dispose();
                }
                return keep;
            }

            bool VisibleAt(P2 q, P2 dir)
            {
                double? w = OcclusionRules.SeamNearness(q, dir, seams, Tol, OccSeamDeg);
                if (w == null) return true;
                if (!Blocked(q, w.Value)) return true;
                var n = new P2(-dir.Y, dir.X);
                double nl = n.Length;
                if (nl < 1e-12) return false;
                n = n * (OccSide / nl);
                return !Blocked(q + n, w.Value) || !Blocked(q - n, w.Value);
            }


            /// <summary>Độ gần của mặt trước tấm (điểm gần người nhìn nhất trên các cạnh 3D của nó); không lọc được ⇒ null.</summary>
            public double? Front(IEnumerable<Curve> memberSeams)
            {
                if (!enabled) return null;
                double? best = null;
                foreach (var c in memberSeams)
                {
                    try
                    {
                        foreach (var p in Sample3(c))
                        {
                            double w = sgn * p.TransformBy(toOut).Z;
                            if (best == null || w > best) best = w;
                        }
                    }
                    catch (System.Exception) { Failures++; }
                }
                return best;
            }

            /// <summary>Tia tại <paramref name="p"/> trúng solid đầu tiên ở đúng độ gần <paramref name="front"/> (±LabelDepthTol).
            /// Không lọc được (hướng nhìn xiên, không có mặt trước) ⇒ true.</summary>
            public bool OnFace(P2 p, double? front)
            {
                if (!enabled || front == null) return true;
                double? hit = NearestHit(p);
                return hit != null && Math.Abs(hit.Value - front.Value) <= LabelDepthTol;
            }

            double? NearestHit(P2 p)
            {
                Point3d a = new Point3d(p.X, p.Y, sgn * OccFar).TransformBy(inv);
                Point3d b = new Point3d(p.X, p.Y, -sgn * OccDeep).TransformBy(inv);
                double? best = null;
                using (var seg = new LineSegment3d(a, b))
                    foreach (var br in breps)
                    {
                        Autodesk.AutoCAD.BoundaryRepresentation.Hit[] hits;
                        try { hits = br.GetLineContainment(seg, OccMaxHits); }
                        catch (System.Exception) { Failures++; continue; }
                        if (hits == null) continue;
                        foreach (var h in hits)
                        {
                            if (!h.IsNull)
                            {
                                double w = sgn * h.Point.TransformBy(toOut).Z;
                                if (best == null || w > best) best = w;
                            }
                            h.Dispose();
                        }
                    }
                return best;
            }
            bool Blocked(P2 p, double near)
            {
                double w0 = near + OccEps, w1 = Math.Max(w0, 0) + OccFar;
                Point3d a = new Point3d(p.X, p.Y, sgn * w0).TransformBy(inv);
                Point3d b = new Point3d(p.X, p.Y, sgn * w1).TransformBy(inv);
                using (var seg = new LineSegment3d(a, b))
                    foreach (var br in breps)
                    {
                        Autodesk.AutoCAD.BoundaryRepresentation.Hit[] hits;
                        try { hits = br.GetLineContainment(seg, 1); }
                        catch (System.Exception) { Failures++; continue; }
                        if (hits == null) continue;
                        bool any = false;
                        foreach (var h in hits) { if (!h.IsNull) any = true; h.Dispose(); }
                        if (any) return true;
                    }
                return false;
            }

            public void Dispose()
            {
                foreach (var b in breps) b.Dispose();
                breps.Clear();
            }
        }
    }
}
