using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCG_3DPanel.Services.Detail;

namespace MCG_3DPanel.Commands.Detail
{
    /// <summary>Biến kết quả <see cref="SectionEngine"/> + đồ 2D thành danh sách entity CHƯA vào bản vẽ (toạ độ cục bộ block).
    /// Không lấy quyền sở hữu đường của <see cref="SectionResult.Visible"/> (luôn chép) ⇒ caller vẫn gọi
    /// <see cref="SectionResult.DisposeCurves"/>. Linetype / block / text style tra trong database đang làm việc (= bản vẽ 2D,
    /// caller đã lock). Ghi chú (thiếu linetype amzigzag) đẩy vào <see cref="SectionResult.Warnings"/>.</summary>
    public static class DetailComposer
    {
        public const string AM0 = "Mechanical-AM_0", AM2 = "Mechanical-AM_2", AM4 = "Mechanical-AM_4", AM5 = "Mechanical-AM_5",
                            AM7 = "Mechanical-AM_7", AM8 = "Mechanical-AM_8", AM9 = "Mechanical-AM_9", AM10 = "Mechanical-AM_10";
        public const string Zigzag = "amzigzag";
        public const string ContinuousBlock = "Continious symbol";
        const string TextStyle = "STANDARD";
        const double DedupeTol = 0.05;
        const double BoundaryTol = 0.5;   // = SectionEngine.Tol: cạnh nằm trên biên vùng / hộp cắt
        const short TextColor = 2;   // chữ ký hiệu trên mẫu: màu 2

        static Database Db => HostApplicationServices.WorkingDatabase;

        // ─── Detail Plan ───────────────────────────────────────────────────────

        /// <summary>Block DET: vết cắt + AM_8 + đường thấy + viền Top Plate (AM_7) + đồ 2D; toạ độ = plan − <paramref name="basePt"/>.
        /// <paramref name="twoDItems"/> (từ <see cref="TwoDItems"/>) được dời rồi đưa luôn vào danh sách (không chép).
        /// <paramref name="region"/> (toạ độ plan, BẮT BUỘC): cạnh vết cắt nằm trên biên vùng ⇒ break AM_4.</summary>
        public static List<Entity> PlanEntities(SectionResult r, List<P2> topPlate, IEnumerable<Entity> twoDItems, P2 basePt,
            DetailRegion region)
        {
            // Thiếu vùng ⇒ cạnh tại biên lại thành nét liền AM_0 — không cho phép im lặng
            if (region == null) throw new ArgumentNullException(nameof(region));
            var ents = new List<Entity>();
            try
            {
                var shift = new P2(-basePt.X, -basePt.Y);
                AddStructure(r, shift, ents, p => region.DistanceToBoundary(p - shift));
                if (topPlate != null && topPlate.Count >= 3)
                    ents.Add(Poly(topPlate.Select(p => p + shift), true, AM7));
                var m = Matrix3d.Displacement(new Vector3d(shift.X, shift.Y, 0));
                foreach (var e in twoDItems ?? Enumerable.Empty<Entity>())
                {
                    e.TransformBy(m);
                    ents.Add(e);
                }
                return ents;
            }
            catch
            {
                DisposeAll(ents);
                throw;
            }
        }

        // ─── Section ───────────────────────────────────────────────────────────

        /// <summary>Block section: vết cắt + AM_8 + đường thấy + Continuous symbol/(t) + "$". Gốc block = tâm khung bao hình
        /// (<paramref name="basePt"/>, toạ độ section); <paramref name="keepBase"/> (Update Details) ⇒ dùng gốc đó để hình không xê dịch.</summary>
        public static List<Entity> SectionEntities(SectionResult r, SectionFrame f, out P2 basePt, P2? keepBase = null)
        {
            basePt = keepBase ?? Centre(r);
            var shift = new P2(-basePt.X, -basePt.Y);
            var ents = new List<Entity>();
            try
            {
                // Hộp cắt section chặn 2 đầu đường cắt: x = 0 và x = Length (toạ độ section, trước khi dời)
                AddStructure(r, shift, ents, f == null ? (Func<P2, double>)null
                    : p => Math.Min(Math.Abs(p.X - shift.X), Math.Abs(p.X - shift.X - f.Length)));

                // Continuous: 1 cho mỗi tấm (vết cắt lớn nhất của tấm) cắt liên tục, không phải HP
                var members = ContinuousMembers(r);
                if (members.Count > 0)
                {
                    ObjectId sym = BlockId(ContinuousBlock);
                    foreach (var m in members)
                    {
                        var (at, rot) = SectionSymbolRules.ContinuousPlacement(m);
                        var p = at + shift;
                        var br = new BlockReference(new Point3d(p.X, p.Y, 0), sym) { Rotation = rot };
                        br.SetDatabaseDefaults(Db);
                        br.Layer = AM5;
                        ents.Add(br);
                        ents.Add(Text("{\\L" + SectionSymbolRules.ThicknessText(m.Thickness) + "}",
                            DetailComposeRules.ThicknessTextAt(p, rot), 30, AM5));
                    }
                }

                // "$": 1 cho mỗi tấm song song mặt cắt nhìn thấy
                foreach (var pp in r.Parallel)
                {
                    var at = SectionSymbolRules.WeldTracePlacement(pp);
                    if (at == null) continue;
                    ents.Add(Text("$", at.Value + shift, 35, AM10));
                }
                return ents;
            }
            catch
            {
                DisposeAll(ents);
                throw;
            }
        }

        /// <summary>Tấm nhận ký hiệu Continuous trong <see cref="SectionEntities"/>; rỗng ⇒ section không cần block "Continious symbol".</summary>
        public static List<CutMember> ContinuousMembers(SectionResult r) =>
            DetailComposeRules.ContinuousMembers(r.Cut.Select(c => (c.Handle, c.Type, (IList<P2>)c.Outline, c.MinSigned, c.MaxSigned)));

        /// <summary>Ký hiệu mặt cắt trên block DET: 2 mũi tên (theo block mẫu A$C1A6307D4, AM_10) ở 2 đầu đường cắt chĩa theo Look
        /// + chữ "nA" h30. Toạ độ cục bộ block DET (plan − <paramref name="basePt"/>).</summary>
        public static List<Entity> SectionMarks(SectionFrame f, int n, string letter, P2 basePt)
        {
            var ents = new List<Entity>();
            try
            {
                var shift = new P2(-basePt.X, -basePt.Y);
                string label = DetailNaming.SectionMark(n, letter);
                foreach (var a in DetailComposeRules.SectionArrows(f.Start + shift, f.End + shift, f.Look))
                {
                    var bar = Poly(new[] { a.BarA, a.BarB }, false, AM10);
                    bar.ConstantWidth = DetailComposeRules.BarWidth;
                    ents.Add(bar);
                    var arrow = Poly(new[] { a.Base, a.Neck, a.Tip }, false, AM10);
                    arrow.SetStartWidthAt(1, DetailComposeRules.HeadWidth);
                    arrow.SetEndWidthAt(1, 0);
                    ents.Add(arrow);
                    ents.Add(Text(label, a.Text, 30, AM10));
                }
                return ents;
            }
            catch
            {
                DisposeAll(ents);
                throw;
            }
        }

        // ─── Đồ 2D (chỉ Detail Plan / View) ────────────────────────────────────

        /// <summary>Đồ chỉ có trên bản vẽ 2D, toạ độ plan (WCS): đi cây block _Assy (bỏ Top Plate / Structure, đi xuống
        /// mọi block &lt;panel&gt;_*). Block khác = phụ kiện, giữ nguyên nếu điểm chèn trong vùng (AM_0 nếu thuộc Hole cut out, còn lại
        /// AM_2). Đường cong trong các block chứa: cắt theo vùng, bỏ AM_9; polyline của Wire guide support ⇒ AM_7 rộng 3.
        /// Chỉ ĐỌC cây _Assy — entity trả về là bản chép.</summary>
        public static List<Entity> TwoDItems(Transaction tr, BlockReference assyRef, DetailRegion region, List<string> warnings = null)
        {
            var res = new List<Entity>();
            string assyName = Name(tr, assyRef);
            string panel = assyName.EndsWith("_Assy", StringComparison.Ordinal) ? assyName.Substring(0, assyName.Length - 5) : assyName;
            // Lashing Hole KHÔNG bỏ: lỗ lashing chỉ có trên 2D (spec)
            var skip = new HashSet<string>(StringComparer.Ordinal) { panel + "_Top Plate", panel + "_Structure" };
            var withAttributes = new List<string>();   // phụ kiện có thuộc tính: bản chép mất giá trị ⇒ báo
            Entity boundary = Boundary(region);
            try
            {
                Walk((BlockTableRecord)tr.GetObject(assyRef.BlockTableRecord, OpenMode.ForRead), assyRef.BlockTransform, assyName, 0);
                string note = DetailComposeRules.AttributesNote(withAttributes);
                if (note != null) warnings?.Add(note);
                return res;
            }
            catch
            {
                DisposeAll(res);
                throw;
            }
            finally { boundary.Dispose(); }

            void Walk(BlockTableRecord btr, Matrix3d xf, string container, int depth)
            {
                if (depth > 16) throw new InvalidOperationException($"Block nesting under {assyName} is too deep (over 16 levels).");
                foreach (ObjectId id in btr)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;
                    if (ent is BlockReference br)
                    {
                        string n = Name(tr, br);
                        if (skip.Contains(n)) continue;
                        if (n.StartsWith(panel + "_", StringComparison.Ordinal))
                        {
                            var child = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                            Walk(child, xf * br.BlockTransform, n, depth + 1);
                            continue;
                        }
                        var at = br.Position.TransformBy(xf);
                        if (!region.Contains(new P2(at.X, at.Y))) continue;
                        if (br.AttributeCollection.Count > 0) withAttributes.Add(n);
                        var leaf = (BlockReference)br.Clone();
                        res.Add(leaf);
                        leaf.TransformBy(xf);
                        leaf.Layer = container == panel + "_Hole cut out" ? AM0 : AM2;
                        continue;
                    }
                    if (!(ent is Curve c) || string.Equals(c.Layer, AM9, StringComparison.OrdinalIgnoreCase)) continue;
                    bool wire = container == panel + "_Wire guide support" && c is Polyline;
                    foreach (var piece in Clip(c, xf, region, boundary))
                    {
                        res.Add(piece);
                        if (wire && piece is Polyline pl) { pl.Layer = AM7; pl.ConstantWidth = 3; }
                    }
                }
            }
        }

        /// <summary>Bản chép đường cong (đổi sang plan) cắt theo vùng: phần có trung điểm trong vùng.</summary>
        static List<Curve> Clip(Curve src, Matrix3d xf, DetailRegion region, Entity boundary)
        {
            var res = new List<Curve>();
            var c = (Curve)src.Clone();
            try
            {
                c.TransformBy(xf);
                if (!Overlaps(c, region)) return res;
                var pts = new Point3dCollection();
                c.IntersectWith(boundary, Intersect.OnBothOperands, pts, IntPtr.Zero, IntPtr.Zero);
                var ps = new List<double>();
                foreach (Point3d p in pts)
                {
                    double t = c.GetParameterAtPoint(c.GetClosestPointTo(p, false));
                    if (t > c.StartParam + 1e-9 && t < c.EndParam - 1e-9) ps.Add(t);
                }
                ps.Sort();
                for (int i = ps.Count - 1; i > 0; i--) if (ps[i] - ps[i - 1] < 1e-9) ps.RemoveAt(i);
                if (c.Closed && ps.Count < 2) ps.Clear();   // đường kín chỉ chạm biên 1 điểm ⇒ không tách
                if (ps.Count == 0)
                {
                    if (Inside(c, c.StartParam, c.EndParam, region)) { res.Add(c); c = null; }
                    return res;
                }
                var dc = new DoubleCollection(ps.ToArray());
                DBObjectCollection parts = c.GetSplitCurves(dc);   // không Dispose collection: phần giữ lại phải sống
                var all = parts.Cast<DBObject>().ToList();
                try
                {
                    for (int i = 0; i < all.Count; i++)
                        if (all[i] is Curve part && Inside(part, part.StartParam, part.EndParam, region)) { res.Add(part); all[i] = null; }
                }
                finally { foreach (var o in all) o?.Dispose(); }
                return res;
            }
            catch
            {
                foreach (var x in res) x.Dispose();
                throw;
            }
            finally { c?.Dispose(); }
        }

        static bool Inside(Curve c, double t0, double t1, DetailRegion region)
        {
            var p = c.GetPointAtParameter((t0 + t1) / 2);
            return region.Contains(new P2(p.X, p.Y));
        }

        static bool Overlaps(Curve c, DetailRegion region)
        {
            Extents3d ext;
            try { ext = c.GeometricExtents; } catch (Autodesk.AutoCAD.Runtime.Exception) { return true; }   // không có khung bao ⇒ thử cắt
            var o = region.Outline(72);
            return ext.MaxPoint.X >= o.Min(q => q.X) && ext.MinPoint.X <= o.Max(q => q.X)
                && ext.MaxPoint.Y >= o.Min(q => q.Y) && ext.MinPoint.Y <= o.Max(q => q.Y);
        }

        static Entity Boundary(DetailRegion r)
        {
            if (r.IsCircle) return new Circle(new Point3d(r.Center.X, r.Center.Y, 0), Vector3d.ZAxis, r.Radius);
            return Poly(r.Corners, true, null);
        }

        // ─── Chung ─────────────────────────────────────────────────────────────

        /// <summary>Vết cắt + AM_8 + đường thấy, đã gộp đoạn trùng. Cạnh vết cắt nằm trên biên vùng / hộp cắt
        /// (<paramref name="boundaryDist"/>, toạ độ đã dời) hoặc trùng break ⇒ AM_4 amzigzag, còn lại AM_0; đường thấy trên biên
        /// ⇒ AM_4 amzigzag, bỏ phần đã có cạnh vết cắt break. Thiếu linetype ⇒ Continuous + ghi chú.</summary>
        static void AddStructure(SectionResult r, P2 shift, List<Entity> ents, Func<P2, double> boundaryDist)
        {
            ObjectId zig = LinetypeId(Zigzag);
            bool anyBreak = false;
            void Zig(Entity e) { anyBreak = true; if (!zig.IsNull) e.LinetypeId = zig; }

            // 1. Đường thấy: tách đoạn thẳng (gộp được) và đường cong (chép ngay vào danh sách)
            var m = Matrix3d.Displacement(new Vector3d(shift.X, shift.Y, 0));
            var straight0 = new List<(P2, P2)>();
            var straight4 = new List<(P2, P2)>();
            var curved = new List<(Curve c, bool boundary)>();
            foreach (var v in r.Visible)
            {
                if (v.Curve == null || v.Curve.IsDisposed) continue;
                if (TryStraight(v.Curve, out P2 a, out P2 b))
                    (v.OnBoundary ? straight4 : straight0).Add((a + shift, b + shift));
                else
                {
                    var c = (Curve)v.Curve.Clone();
                    ents.Add(c);   // vào danh sách ngay: lỗi sau đó ⇒ caller giải phóng
                    c.TransformBy(m);
                    curved.Add((c, v.OnBoundary));
                }
            }
            var breaksRaw = DetailComposeRules.MergeCollinear(straight4, null, DedupeTol);

            // 2. Vết cắt: tách cạnh break / cạnh thường
            var cutSolid = new List<(P2, P2)>();
            var cutBreak = new List<(P2, P2)>();
            foreach (var cf in r.Cut)
            {
                if (cf.Outline == null || cf.Outline.Count < 3) continue;
                var o = cf.Outline.Select(p => p + shift).ToList();
                var flags = DetailComposeRules.BreakEdges(o, boundaryDist, breaksRaw, BoundaryTol);
                foreach (var run in DetailComposeRules.Runs(o, flags))
                {
                    var pl = Poly(run.Pts, run.Closed, run.Break ? AM4 : AM0);
                    ents.Add(pl);
                    if (run.Break) Zig(pl);
                    var bucket = run.Break ? cutBreak : cutSolid;
                    for (int i = 1; i < run.Pts.Count; i++) bucket.Add((run.Pts[i - 1], run.Pts[i]));
                    if (run.Closed) bucket.Add((run.Pts[run.Pts.Count - 1], run.Pts[0]));
                }
                foreach (var s in DetailComposeRules.MarkLines(o)) ents.Add(Line(s, AM8));
            }

            // 3. Break thẳng còn lại (bỏ phần trùng cạnh vết cắt break) + nét thấy AM_0 (bỏ phần trùng mọi thứ đã vẽ)
            var breaks = DetailComposeRules.MergeCollinear(breaksRaw, cutBreak, BoundaryTol);
            foreach (var s in breaks)
            {
                var pl = Poly(new[] { s.Item1, s.Item2 }, false, AM4);
                ents.Add(pl);
                Zig(pl);
            }
            var lines = DetailComposeRules.MergeCollinear(straight0, cutSolid.Concat(cutBreak).Concat(breaks), DedupeTol);
            foreach (var s in lines) ents.Add(Line(s, AM0));

            // 4. Đường cong: trên biên ⇒ AM_4 (bỏ nếu nằm trọn trên cạnh vết cắt break), còn lại AM_0
            foreach (var (c, boundary) in curved)
            {
                if (boundary && cutBreak.Count > 0 && OnSegments(c, cutBreak))
                {
                    ents.Remove(c);
                    c.Dispose();
                    continue;
                }
                c.SetDatabaseDefaults(Db);
                c.Layer = boundary ? AM4 : AM0;
                if (boundary) Zig(c);
            }
            if (anyBreak && zig.IsNull) r.Warnings.Add($"Linetype {Zigzag} is missing from the drawing; break lines are drawn Continuous.");
        }

        /// <summary>Đầu, giữa, cuối đường cong đều cách các đoạn ≤ BoundaryTol.</summary>
        static bool OnSegments(Curve c, List<(P2, P2)> segs)
        {
            foreach (double k in new[] { 0, 0.25, 0.5, 0.75, 1 })
            {
                var p = c.GetPointAtParameter(c.StartParam + (c.EndParam - c.StartParam) * k);
                var q = new P2(p.X, p.Y);
                if (!segs.Any(s => SectionCurveRules.DistanceToSegment(q, s.Item1, s.Item2) <= BoundaryTol)) return false;
            }
            return true;
        }

        /// <summary>Line, hoặc polyline 2 đỉnh không cung ⇒ đoạn thẳng.</summary>
        static bool TryStraight(Curve c, out P2 a, out P2 b)
        {
            a = b = default(P2);
            if (c is Line l) { a = XY(l.StartPoint); b = XY(l.EndPoint); return true; }
            if (c is Polyline pl && pl.NumberOfVertices == 2 && !pl.Closed && Math.Abs(pl.GetBulgeAt(0)) < 1e-12)
            { a = XY(pl.GetPoint3dAt(0)); b = XY(pl.GetPoint3dAt(1)); return true; }
            return false;
        }

        /// <summary>Tâm khung bao vết cắt + đường thấy; rỗng ⇒ (0,0).</summary>
        static P2 Centre(SectionResult r)
        {
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            void Add(double x, double y) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
            foreach (var cf in r.Cut) if (cf.Outline != null) foreach (var p in cf.Outline) Add(p.X, p.Y);
            foreach (var v in r.Visible)
            {
                if (v.Curve == null || v.Curve.IsDisposed) continue;
                try { var e = v.Curve.GeometricExtents; Add(e.MinPoint.X, e.MinPoint.Y); Add(e.MaxPoint.X, e.MaxPoint.Y); }
                catch (Autodesk.AutoCAD.Runtime.Exception) { Add(v.Curve.StartPoint.X, v.Curve.StartPoint.Y); Add(v.Curve.EndPoint.X, v.Curve.EndPoint.Y); }
            }
            return x0 > x1 ? new P2(0, 0) : new P2((x0 + x1) / 2, (y0 + y1) / 2);
        }

        static Polyline Poly(IEnumerable<P2> pts, bool closed, string layer)
        {
            var pl = new Polyline();
            if (layer != null) pl.SetDatabaseDefaults(Db);
            int i = 0;
            foreach (var p in pts) pl.AddVertexAt(i++, new Point2d(p.X, p.Y), 0, 0, 0);
            pl.Closed = closed;
            if (layer != null) pl.Layer = layer;
            return pl;
        }

        static Line Line((P2 A, P2 B) s, string layer)
        {
            var l = new Line(new Point3d(s.A.X, s.A.Y, 0), new Point3d(s.B.X, s.B.Y, 0));
            l.SetDatabaseDefaults(Db);
            l.Layer = layer;
            return l;
        }

        static MText Text(string contents, P2 at, double h, string layer)
        {
            var t = new MText();
            t.SetDatabaseDefaults(Db);
            t.Contents = contents;
            t.TextHeight = h;
            t.Attachment = AttachmentPoint.MiddleCenter;
            t.Location = new Point3d(at.X, at.Y, 0);
            t.Layer = layer;
            t.ColorIndex = TextColor;
            ObjectId style = TextStyleId();
            if (!style.IsNull) t.TextStyleId = style;
            return t;
        }

        /// <summary>Tra bảng trong transaction đang mở của caller (bảng có thể đã mở ghi); không có ⇒ OpenClose riêng.</summary>
        static T Look<T>(Func<Transaction, T> f)
        {
            Transaction top = Db.TransactionManager.TopTransaction;
            if (top != null) return f(top);
            using (Transaction tr = Db.TransactionManager.StartOpenCloseTransaction()) return f(tr);
        }

        static ObjectId TextStyleId() => Look(tr =>
            SymbolTables.Live((TextStyleTable)tr.GetObject(Db.TextStyleTableId, OpenMode.ForRead), TextStyle));

        static ObjectId LinetypeId(string name) => Look(tr =>
        {
            var lt = (LinetypeTable)tr.GetObject(Db.LinetypeTableId, OpenMode.ForRead);
            foreach (ObjectId id in lt)
            {
                var rec = (LinetypeTableRecord)tr.GetObject(id, OpenMode.ForRead);
                if (!rec.IsErased && string.Equals(rec.Name, name, StringComparison.OrdinalIgnoreCase)) return id;
            }
            return ObjectId.Null;
        });

        /// <summary>Block phải có sẵn (caller đã bổ sung qua PlanViewBlockSource).</summary>
        static ObjectId BlockId(string name) => Look(tr =>
            SymbolTables.Require((BlockTable)tr.GetObject(Db.BlockTableId, OpenMode.ForRead), name, "Block"));

        static string Name(Transaction tr, BlockReference br)
        {
            var id = br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord;
            return ((BlockTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name;
        }

        static void DisposeAll(IEnumerable<Entity> ents)
        {
            foreach (var e in ents) if (e != null && !e.IsDisposed && e.ObjectId.IsNull) e.Dispose();
        }

        static P2 XY(Point3d p) => new P2(p.X, p.Y);
    }
}
