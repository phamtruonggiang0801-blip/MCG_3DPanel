using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCG_3DPanel.Services.PlanView;

namespace MCG_3DPanel.Commands
{
    /// <summary>Phía AutoCAD của Plan View trên bản vẽ 2D: kiểm tra tài nguyên có sẵn, tìm tờ, xoá phần tool đã vẽ, tìm phần vẽ tay,
    /// ghi mô hình. KHÔNG mở ghi/sửa/xoá gì trong cây block _Assy (handle = khoá correction của file 3D). Không tạo layer / dim style /
    /// text style / block ký hiệu — chỉ RegApp MCG3D_PLANVIEW và 2 block mặt cắt &lt;panel&gt;_X-X_MCG / _Y-Y_MCG.</summary>
    public static class PlanViewWriter
    {
        public const string App = "MCG3D_PLANVIEW";
        public const string KeepPrefix = "MCG3D_PLANVIEW_KEEP_";
        private static readonly string[] Layers =
            { "0", "Mechanical-AM_0", "Mechanical-AM_1", "Mechanical-AM_4", "Mechanical-AM_5", "Mechanical-AM_6" };
        private const string TextStyle = "STANDARD";

        // ─── Tờ ───

        /// <summary>Reference _Assy (handle trong file 3D = trong file 2D), khung A1 chứa nó ⇒ bối cảnh tờ. null ⇒ <paramref name="reason"/>.</summary>
        public static SheetContext Sheet(Database db, string assyRefHandle, PlanViewInput input, out ObjectId assyRef, out string reason)
        {
            reason = null; assyRef = ObjectId.Null;
            if (!db.TryGetObjectId(new Handle(Convert.ToInt64(assyRefHandle, 16)), out assyRef) || assyRef.IsErased)
            { reason = $"The 2D block of {input.Panel} is missing from the drawing."; return null; }
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var br = (BlockReference)tr.GetObject(assyRef, OpenMode.ForRead);
                Matrix3d m = br.BlockTransform;
                var frames = new List<(double, double, double, double, double)>();
                var origins = new List<Point3d>();   // gốc khung = điểm chèn A1 (bảng quy tắc đo từ đây)
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is BlockReference f) || BlockName(tr, f) != "A1") continue;
                    try { var e = f.GeometricExtents; frames.Add((e.MinPoint.X, e.MinPoint.Y, e.MaxPoint.X, e.MaxPoint.Y, f.ScaleFactors.X)); origins.Add(f.Position); }
                    catch { /* khung không có hình học */ }
                }
                int? k = SheetGeometry.PickFrame(frames, br.Position.X, br.Position.Y);
                tr.Commit();
                if (k == null) { reason = $"No sheet frame (A1) around {input.Panel}."; return null; }
                var fr = frames[k.Value];
                var t = new[] { m[0, 0], m[0, 1], m[1, 0], m[1, 1], m[0, 3], m[1, 3] };
                if (!SheetGeometry.IsUnrotated(t))
                { reason = $"The {input.Panel} block is rotated or mirrored; Plan View supports unrotated blocks only. Nothing was drawn."; return null; }
                var tp = input.TopPlate;
                var c = new[] { (tp.MinX, tp.MinY), (tp.MaxX, tp.MinY), (tp.MaxX, tp.MaxY), (tp.MinX, tp.MaxY) }
                    .Select(p => SheetGeometry.ToSheet(p.Item1, p.Item2, t)).ToList();
                return new SheetContext
                {
                    Scale = SheetGeometry.ScaleFromFrame(fr.Item5), FrameX = origins[k.Value].X, FrameY = origins[k.Value].Y,
                    PlanMinX = c.Min(p => p.X), PlanMinY = c.Min(p => p.Y), PlanMaxX = c.Max(p => p.X), PlanMaxY = c.Max(p => p.Y),
                    AssyTransform = t,
                };
            }
        }

        /// <summary>Layer / dim style / text style mô hình cần mà bản vẽ chưa có. Rỗng ⇒ ghi được. Block thiếu do
        /// <see cref="PlanViewBlockSource"/> bổ sung (template CAS.dwt hoặc tự vẽ profile).</summary>
        public static List<string> Missing(Database db, PlanViewModel model)
        {
            var miss = new List<string>();
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                foreach (var l in Layers) if (!SymbolTables.HasLive(lt, l)) miss.Add("layer " + l);
                var ds = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
                foreach (var s in AllDims(model).Select(d => d.DimStyle).Distinct()) if (!SymbolTables.HasLive(ds, s)) miss.Add("dim style " + s);
                var ts = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
                if (!SymbolTables.HasLive(ts, TextStyle)) miss.Add("text style " + TextStyle);
                tr.Commit();
            }
            return miss;
        }

        // ─── Phần tool đã vẽ / phần vẽ tay ───

        public static int EraseOwn(Database db, string btrHandle)
        {
            int n = 0;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    var ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                    if (!IsOwn(ent, btrHandle)) continue;
                    ent.UpgradeOpen(); ent.Erase(); n++;
                }
                tr.Commit();
            }
            return n;
        }

        /// <summary>Phần vẽ tay trong khung tờ mà Replace sẽ xoá (HandDrawnRules). Bỏ qua chính reference _Assy và mọi thứ của tool.</summary>
        public static List<ObjectId> FindHandDrawn(Database db, string panel, ObjectId assyRef, SheetContext s, PlanViewModel model, string btrHandle)
        {
            var known = KnownIntervals(model, s);
            var plate = (s.PlanMinX, s.PlanMinY, s.PlanMaxX, s.PlanMaxY);
            double fx0 = s.FrameX, fy0 = s.FrameY, fx1 = s.FrameX + 841.0 * s.Scale, fy1 = s.FrameY + 594.0 * s.Scale;
            var r = new List<ObjectId>();
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id == assyRef) continue;
                    var ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                    var e = Describe(tr, ent, btrHandle, out Point3d at);
                    if (e != null) e.HasDetailMark = HasDetailMark(ent);   // biên / nhãn / tiêu đề Detail: không đếm, không xoá
                    if (e == null || at.X < fx0 || at.X > fx1 || at.Y < fy0 || at.Y > fy1) continue;
                    if (HandDrawnRules.IsReplaceable(e, panel, known, plate)) r.Add(id);
                }
                tr.Commit();
            }
            return r;
        }

        public static void EraseAll(Database db, IEnumerable<ObjectId> ids)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in ids) if (!id.IsErased) tr.GetObject(id, OpenMode.ForWrite).Erase();
                tr.Commit();
            }
        }

        public static bool HasKeepMarker(Database db, string btrHandle) => KeepMarker(db, btrHandle, null);
        public static void SetKeepMarker(Database db, string btrHandle, bool keep) => KeepMarker(db, btrHandle, keep);

        private static bool KeepMarker(Database db, string btrHandle, bool? set)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                string key = KeepPrefix + btrHandle;
                bool has = nod.Contains(key);
                if (set == true && !has)
                {
                    nod.UpgradeOpen();
                    var x = new Xrecord { Data = new ResultBuffer(new TypedValue((int)DxfCode.Text, "Keep both")) };
                    nod.SetAt(key, x); tr.AddNewlyCreatedDBObject(x, true);
                }
                else if (set == false && has)
                {
                    nod.UpgradeOpen();
                    tr.GetObject(nod.GetAt(key), OpenMode.ForWrite).Erase();
                    nod.Remove(key);
                }
                tr.Commit();
                return has;
            }
        }

        // ─── Ghi ───

        /// <summary>Ghi toàn bộ mô hình trong 1 transaction; mọi entity mang XData MCG3D_PLANVIEW = <paramref name="btrHandle"/>.
        /// Trả (số nhãn, số kích thước).</summary>
        public static (int labels, int dims) Write(Database db, PlanViewModel m, SheetContext s, string btrHandle)
        {
            var oldWdb = HostApplicationServices.WorkingDatabase;
            HostApplicationServices.WorkingDatabase = db;   // DBText.AdjustAlignment cần database đang làm việc
            ObjectId oblique = ObliqueArrow(db);   // trước transaction ghi: có thể phải nạp block mũi tên
            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    EnsureRegApp(tr, db);
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ts = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
                    var ds = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
                    ObjectId style = SymbolTables.Require(ts, TextStyle, "Text style");
                    var xd = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, App),
                                              new TypedValue((int)DxfCode.ExtendedDataAsciiString, btrHandle));
                    var x = new Placer(s);

                    void Add(Entity e) { ms.AppendEntity(e); tr.AddNewlyCreatedDBObject(e, true); e.XData = xd; }

                    foreach (var l in m.Lines.Select(q => q.Layer).Concat(m.Polylines.Select(q => q.Layer)).Concat(m.Leaders.Select(q => q.Layer)).Distinct())
                        PlanViewBlockSource.EnsureLayer(tr, db, l);   // AM_7 / AM_8 / posb thiếu ⇒ tạo theo CAS.dwt (lựa chọn (a))
                    foreach (var t in m.Texts)
                    {
                        var e = MakeText(t, x, s.Scale, style, db);
                        Add(e);
                        if (t.Justify == "CenterMiddleLeft" && e is DBText dt) CentreMiddleLeft(dt, x.P(t.Space, t.X, t.Y), db);
                    }
                    void AddDim(PvDim d) { var dim = MakeDim(d, x, ds); Add(dim); Overrides(dim, d, s.Scale, oblique, d.TextX.HasValue && d.TextY.HasValue ? x.P(d.Space, d.TextX.Value, d.TextY.Value) : Point3d.Origin); }
                    foreach (var d in m.Dims) AddDim(d);
                    foreach (var l in m.Lines) Add(new Line(x.P(l.Space, l.X1, l.Y1), x.P(l.Space, l.X2, l.Y2)) { Layer = l.Layer });
                    foreach (var pl in m.Polylines)
                    {
                        var poly = new Polyline { Closed = true, Layer = pl.Layer };
                        for (int k = 0; k < pl.Points.Count; k++) { var q = x.P(pl.Space, pl.Points[k].X, pl.Points[k].Y); poly.AddVertexAt(k, new Point2d(q.X, q.Y), 0, 0, 0); }
                        Add(poly);
                    }
                    foreach (var ld in m.Leaders)
                    {
                        var leader = new Leader { Layer = ld.Layer };
                        foreach (var q in ld.Points) leader.AppendVertex(x.P(ld.Space, q.X, q.Y));
                        if (SymbolTables.HasLive(ds, "AM_ISO")) leader.DimensionStyle = SymbolTables.Live(ds, "AM_ISO");
                        leader.HasArrowHead = true;
                        Add(leader);
                        leader.Dimscale = s.Scale;   // override SAU khi vào bản vẽ (như kích thước)
                    }
                    foreach (var i in m.Inserts) InsertBlock(tr, bt, i, x, s.Scale, Add);
                    foreach (var sec in m.Sections)
                    {
                        ObjectId secBtr = DefineSection(tr, db, bt, sec);
                        Add(new BlockReference(new Point3d(sec.X, sec.Y, 0), secBtr) { Rotation = sec.Rotation, Layer = "Mechanical-AM_0" });
                        foreach (var k in sec.ThkSymbols) InsertBlock(tr, bt, k, x, s.Scale, Add);
                        foreach (var d in sec.Dims) AddDim(d);
                    }
                    tr.Commit();
                    return (m.Texts.Count(t => t.Category == "Name" || t.Category == "Symbol"), AllDims(m).Count());
                }
            }
            finally { HostApplicationServices.WorkingDatabase = oldWdb; }
        }

        /// <summary>Đổi điểm/góc từ hệ block 2D (Space.Block) sang Model Space theo transform reference _Assy.</summary>
        private sealed class Placer
        {
            private readonly double[] _t; public readonly double Angle;
            public Placer(SheetContext s) { _t = s.AssyTransform; Angle = Math.Atan2(_t[2], _t[0]); }
            public Point3d P(PvSpace sp, double x, double y)
            {
                if (sp == PvSpace.Sheet) return new Point3d(x, y, 0);
                var q = SheetGeometry.ToSheet(x, y, _t); return new Point3d(q.X, q.Y, 0);
            }
            public double R(PvSpace sp, double rot) => sp == PvSpace.Sheet ? rot : rot + Angle;
        }

        private static Entity MakeText(PvText t, Placer x, int scale, ObjectId style, Database db)
        {
            Point3d p = x.P(t.Space, t.X, t.Y);
            double h = SheetGeometry.ModelHeight(t.PaperHeight, scale);
            if (t.IsMText)
                return new MText
                {
                    Contents = t.Text, TextHeight = h, TextStyleId = style, Layer = t.Layer, Location = p,
                    Width = t.PaperWidth * scale, Attachment = Attach(t.Justify), Rotation = x.R(t.Space, t.Rotation),
                };
            var d = new DBText { TextString = t.Text, Height = h, TextStyleId = style, Layer = t.Layer, Rotation = x.R(t.Space, t.Rotation), Position = p };
            if (t.Justify != "BaseLeft") { d.Justify = Attach(t.Justify == "CenterMiddleLeft" ? "MiddleLeft" : t.Justify); d.AlignmentPoint = p; d.AdjustAlignment(db); }
            return d;
        }

        /// <summary>Tờ tay: nhãn MiddleLeft nhưng chữ nằm GIỮA đường tâm thanh ⇒ đo bề rộng chữ thật rồi lùi điểm căn nửa bề rộng.</summary>
        private static void CentreMiddleLeft(DBText t, Point3d centre, Database db)
        {
            try
            {
                var e = t.GeometricExtents;
                double w = Math.Abs(Math.Cos(t.Rotation)) > 0.5 ? e.MaxPoint.X - e.MinPoint.X : e.MaxPoint.Y - e.MinPoint.Y;
                var u = new Vector3d(Math.Cos(t.Rotation), Math.Sin(t.Rotation), 0);
                t.AlignmentPoint = centre - u * (w / 2);
                t.AdjustAlignment(db);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { /* không đo được ⇒ giữ MiddleLeft tại tâm */ }
        }

        /// <summary>Block mũi tên "_OBLIQUE" (nạp vào bản vẽ nếu chưa có — đặt DIMBLK tạm rồi trả lại). Null ⇒ không dùng mũi tên xiên.</summary>
        private static ObjectId ObliqueArrow(Database db)
        {
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                if (SymbolTables.HasLive(bt, "_Oblique")) return SymbolTables.Live(bt, "_Oblique");
            }
            try
            {
                string old = Autodesk.AutoCAD.ApplicationServices.Application.GetSystemVariable("DIMBLK") as string;
                Autodesk.AutoCAD.ApplicationServices.Application.SetSystemVariable("DIMBLK", "_OBLIQUE");
                Autodesk.AutoCAD.ApplicationServices.Application.SetSystemVariable("DIMBLK", string.IsNullOrEmpty(old) ? "." : old);
                using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    return SymbolTables.Live(bt, "_Oblique");
                }
            }
            catch { return ObjectId.Null; }
        }

        private static Dimension MakeDim(PvDim d, Placer x, DimStyleTable ds)
        {
            ObjectId style = SymbolTables.Require(ds, d.DimStyle, "Dim style");
            string text = d.TextOverride ?? "";
            Dimension dim;
            if (d.Kind == "Diametric")
            {
                Point3d a = x.P(d.Space, d.X1, d.Y1), b = x.P(d.Space, d.X2, d.Y2);
                dim = new DiametricDimension(a, b, 0, text, style);
            }
            else
                dim = new RotatedDimension(x.R(d.Space, d.Rotation), x.P(d.Space, d.X1, d.Y1), x.P(d.Space, d.X2, d.Y2),
                                           x.P(d.Space, d.LineX, d.LineY), text, style);
            dim.Layer = d.Layer;
            return dim;
        }

        /// <summary>Override kích thước — gọi SAU khi kích thước đã nằm trong bản vẽ: DIMBLK1/2 đổi ObjectId mũi tên sang tên block
        /// qua database của đối tượng (trước đó chưa có ⇒ eKeyNotFound, lỗi 2026-11-10).</summary>
        private static void Overrides(Dimension dim, PvDim d, int scale, ObjectId oblique, Point3d textAt)
        {
            dim.Dimscale = scale;   // override bắt buộc (spec): DIMSCALE = tỉ lệ tờ
            if (d.Tmove) dim.Dimtmove = 1;   // tờ tay: có ở chuỗi chép từ nháp, không có ở kích thước "<>"
            if (d.TextX.HasValue && d.TextY.HasValue && d.Kind != "Diametric")
            {
                // Chữ không lọt giữa 2 đường gióng ⇒ đặt trên đường kích thước (DimTextLayout), DIMTMOVE = 1 như tờ tay — thay vì để
                // AutoCAD đẩy chữ ra ngoài đè lên chữ của kích thước bên cạnh.
                dim.Dimtmove = 1;
                dim.TextPosition = textAt;
            }
            if (d.Oblique && !oblique.IsNull) { dim.Dimblk1 = oblique; dim.Dimblk2 = oblique; dim.Dimsah = true; }
        }

        private static void InsertBlock(Transaction tr, BlockTable bt, PvInsert i, Placer x, int scale, Action<Entity> add)
        {
            ObjectId btrId = SymbolTables.Require(bt, i.Block, "Block");
            double f = i.ScaleIsAbsolute ? i.Scale : i.Scale * scale / 50.0;
            var br = new BlockReference(x.P(i.Space, i.X, i.Y), btrId)
            {
                Rotation = x.R(i.Space, i.Rotation), Layer = i.Layer ?? "Mechanical-AM_0",
                ScaleFactors = new Scale3d(i.MirrorX ? -f : f, f, f),
            };
            add(br);
            var def = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
            if (!def.HasAttributeDefinitions) return;
            foreach (ObjectId aid in def)
            {
                if (!(tr.GetObject(aid, OpenMode.ForRead) is AttributeDefinition ad) || ad.Constant) continue;
                var ar = new AttributeReference();
                ar.SetAttributeFromBlock(ad, br.BlockTransform);
                br.AttributeCollection.AppendAttribute(ar);
                tr.AddNewlyCreatedDBObject(ar, true);
            }
        }

        /// <summary>(Định nghĩa lại) block mặt cắt của tool: tấm = polyline kín layer 0, profile = chèn block quy cách.</summary>
        private static ObjectId DefineSection(Transaction tr, Database db, BlockTable bt, PvSection sec)
        {
            BlockTableRecord btr;
            if (SymbolTables.HasLive(bt, sec.BlockName))
            {
                btr = (BlockTableRecord)tr.GetObject(SymbolTables.Live(bt, sec.BlockName), OpenMode.ForWrite);
                foreach (ObjectId id in btr) tr.GetObject(id, OpenMode.ForWrite).Erase();
            }
            else
            {
                SymbolTables.FreeErasedName(tr, bt, sec.BlockName);
                btr = new BlockTableRecord { Name = sec.BlockName, Origin = Point3d.Origin };
                bt.UpgradeOpen(); bt.Add(btr); tr.AddNewlyCreatedDBObject(btr, true);
            }
            foreach (var r in sec.Plates)
            {
                var pl = new Polyline { Closed = true, Layer = "0" };
                pl.AddVertexAt(0, new Point2d(r.X0, r.Y0), 0, 0, 0);
                pl.AddVertexAt(1, new Point2d(r.X1, r.Y0), 0, 0, 0);
                pl.AddVertexAt(2, new Point2d(r.X1, r.Y1), 0, 0, 0);
                pl.AddVertexAt(3, new Point2d(r.X0, r.Y1), 0, 0, 0);
                btr.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
            }
            foreach (var p in sec.Profiles)
            {
                // Lật profile theo hướng bụng HP (sx = −1) — trước đây bị bỏ qua nên HP trên mặt cắt không bao giờ lật (tờ 17C: 13 HP Y-Y lật).
                var br = new BlockReference(new Point3d(p.X, p.Y, 0), SymbolTables.Require(bt, p.Block, "Block"))
                {
                    Layer = "0", ScaleFactors = new Scale3d(p.MirrorX ? -p.Scale : p.Scale, p.Scale, p.Scale), Rotation = p.Rotation,
                };
                btr.AppendEntity(br); tr.AddNewlyCreatedDBObject(br, true);
            }
            return btr.ObjectId;
        }

        // ─── Tiện ích ───

        private static IEnumerable<PvDim> AllDims(PlanViewModel m) => m.Dims.Concat(m.Sections.SelectMany(s => s.Dims));

        /// <summary>Khoảng các kích thước tool sẽ vẽ, toạ độ tờ — để nhận kích thước vẽ tay trùng.</summary>
        private static List<(double, double, double)> KnownIntervals(PlanViewModel m, SheetContext s)
        {
            var x = new Placer(s);
            var r = new List<(double, double, double)>();
            foreach (var d in AllDims(m).Where(d => d.Kind == "Rotated"))
            {
                double rot = x.R(d.Space, d.Rotation), ux = Math.Cos(rot), uy = Math.Sin(rot);
                Point3d a = x.P(d.Space, d.X1, d.Y1), b = x.P(d.Space, d.X2, d.Y2);
                double pa = a.X * ux + a.Y * uy, pb = b.X * ux + b.Y * uy;
                r.Add((rot, Math.Min(pa, pb), Math.Max(pa, pb)));
            }
            return r;
        }

        private static PvExisting Describe(Transaction tr, Entity ent, string btrHandle, out Point3d at)
        {
            at = Point3d.Origin;
            bool own = IsOwn(ent, btrHandle);
            switch (ent)
            {
                case DBText t: at = t.Position; return new PvExisting { Type = "Text", Layer = t.Layer, Text = t.TextString, HasOwnMark = own, InModelSpace = true };
                case MText mt: at = mt.Location; return new PvExisting { Type = "MText", Layer = mt.Layer, Text = mt.Text, HasOwnMark = own, InModelSpace = true };
                case RotatedDimension rd:
                    at = rd.XLine1Point;
                    return new PvExisting { Type = "Dim", Layer = rd.Layer, Text = rd.DimensionText, X1 = rd.XLine1Point.X, Y1 = rd.XLine1Point.Y,
                                            X2 = rd.XLine2Point.X, Y2 = rd.XLine2Point.Y, Rotation = rd.Rotation, HasOwnMark = own, InModelSpace = true };
                case AlignedDimension ad:
                    at = ad.XLine1Point;
                    return new PvExisting { Type = "Dim", Layer = ad.Layer, Text = ad.DimensionText, X1 = ad.XLine1Point.X, Y1 = ad.XLine1Point.Y,
                                            X2 = ad.XLine2Point.X, Y2 = ad.XLine2Point.Y,
                                            Rotation = Math.Atan2(ad.XLine2Point.Y - ad.XLine1Point.Y, ad.XLine2Point.X - ad.XLine1Point.X),
                                            HasOwnMark = own, InModelSpace = true };
                case Line ln:
                    at = ln.StartPoint; return new PvExisting { Type = "Line", Layer = ln.Layer, HasOwnMark = own, InModelSpace = true };
                case Polyline pl:
                    at = pl.GetPoint3dAt(0); return new PvExisting { Type = "Polyline", Layer = pl.Layer, HasOwnMark = own, InModelSpace = true };
                case Leader ld:
                    at = ld.VertexAt(0); return new PvExisting { Type = "Leader", Layer = ld.Layer, HasOwnMark = own, InModelSpace = true };
                case BlockReference br:
                    at = br.Position; return new PvExisting { Type = "BlockRef", BlockName = BlockName(tr, br), HasOwnMark = own, InModelSpace = true };
            }
            return null;
        }

        /// <summary>Có XData MCG3D_DETAIL (Detail Drawing ghi) — Plan View không bao giờ coi là phần vẽ tay.</summary>
        private static bool HasDetailMark(Entity ent)
        {
            ResultBuffer rb = ent.GetXDataForApplication(MCG_3DPanel.Commands.Detail.DetailWriter.App);
            if (rb == null) return false;
            rb.Dispose(); return true;
        }

        /// <summary>Model Space của bản 2D có entity Plan View của panel <paramref name="btrHandle"/> (tool đã vẽ) — Update từ bản 2D
        /// chỉ vẽ lại khi đã có (spec 2026-11-20 3d-controls-in-2d).</summary>
        internal static bool HasPlanView(Database db, string btrHandle)
        {
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                    if (tr.GetObject(id, OpenMode.ForRead) is Entity e && IsOwn(e, btrHandle)) return true;
            }
            return false;
        }

        private static bool IsOwn(Entity ent, string btrHandle)
        {
            ResultBuffer rb = ent.GetXDataForApplication(App);
            if (rb == null) return false;
            using (rb) return rb.AsArray().Any(v => v.TypeCode == (int)DxfCode.ExtendedDataAsciiString && (string)v.Value == btrHandle);
        }

        private static string BlockName(Transaction tr, BlockReference br)
        {
            try
            {
                var id = br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord;
                return ((BlockTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name;
            }
            catch { return ""; }
        }

        private static void EnsureRegApp(Transaction tr, Database db)
        {
            var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (SymbolTables.HasLive(rat, App)) return;
            rat.UpgradeOpen();
            var r = new RegAppTableRecord { Name = App };
            rat.Add(r); tr.AddNewlyCreatedDBObject(r, true);
        }

        private static AttachmentPoint Attach(string j)
        {
            switch (j)
            {
                case "BottomCenter": return AttachmentPoint.BottomCenter;
                case "MiddleLeft": return AttachmentPoint.MiddleLeft;
                case "MiddleCenter": return AttachmentPoint.MiddleCenter;
                case "TopCenter": return AttachmentPoint.TopCenter;
                case "TopLeft": return AttachmentPoint.TopLeft;
                default: return AttachmentPoint.BaseLeft;
            }
        }
    }
}
