using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCG_3DPanel.Services;
using MCG_3DPanel.Services.PlanView;

namespace MCG_3DPanel.Commands
{
    /// <summary>Bổ sung block Plan View còn thiếu trong bản vẽ 2D (kỹ sư 2026-11-10, lựa chọn (a)):
    /// 1. chép từ template công ty CAS.dwt (kèm block lồng; layer/style mà nét trong block dùng được tạo theo template);
    /// 2. template cũng không có ⇒ tool tự vẽ: profile "HP{b}x{t}" / "BS {h}x{t}" (HpProfileService / chữ nhật) và 7 block ký hiệu
    ///    (<see cref="SymbolBlockShapes"/>, hình chép từ tờ 7D-17P; layer thiếu tạo theo CAS.dwt);
    /// 3. còn lại (block lạ) ⇒ báo thiếu, không vẽ gì. Block đã có trong bản vẽ KHÔNG bao giờ bị ghi đè.</summary>
    public static class PlanViewBlockSource
    {
        public const string TemplatePath = @"c:\RoRoWF\System\Programconfig\AutoCAD Mechanical\ARAS\Template\CAS.dwt";

        public sealed class Plan
        {
            public List<string> FromTemplate { get; } = new List<string>();
            public List<string> Drawn { get; } = new List<string>();
            public List<string> Unavailable { get; } = new List<string>();
            public bool TemplateFound { get; set; }
        }

        /// <summary>Chỉ ĐỌC: block nào thiếu, lấy từ đâu. Không ghi gì vào <paramref name="db"/>.</summary>
        public static Plan Prepare(Database db, IEnumerable<string> required)
        {
            var p = new Plan();
            List<string> missing;
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                missing = required.Where(b => !SymbolTables.HasLive(bt, b)).ToList();
                // Block lồng (CS_horl_up_right ⇒ CS_symbol) cũng phải có — chèn TRƯỚC block cha.
                foreach (var b in missing.ToList())
                    if (SymbolBlockShapes.Has(b))
                        foreach (var dep in SymbolBlockShapes.Dependencies(b))
                            if (!SymbolTables.HasLive(bt, dep) && !missing.Contains(dep)) missing.Insert(missing.IndexOf(b), dep);
                tr.Commit();
            }
            if (missing.Count == 0) return p;

            var inTemplate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(TemplatePath))
            {
                p.TemplateFound = true;
                using (var tdb = new Database(false, true))
                {
                    tdb.ReadDwgFile(TemplatePath, FileOpenMode.OpenForReadAndAllShare, true, "");
                    using (Transaction tr = tdb.TransactionManager.StartOpenCloseTransaction())
                    {
                        var bt = (BlockTable)tr.GetObject(tdb.BlockTableId, OpenMode.ForRead);
                        foreach (var b in missing) if (SymbolTables.HasLive(bt, b)) inTemplate.Add(b);
                        tr.Commit();
                    }
                }
            }
            foreach (var b in missing)
            {
                if (inTemplate.Contains(b)) p.FromTemplate.Add(b);
                else if (CanDraw(b)) p.Drawn.Add(b);
                else p.Unavailable.Add(b);
            }
            return p;
        }

        /// <summary>Thực hiện <paramref name="plan"/> trên <paramref name="db"/>. Trả tên layer / text style / linetype / dim style
        /// mới sinh ra do chép block (để báo trong thông báo trạng thái).</summary>
        public static List<string> Apply(Database db, Plan plan)
        {
            var before = TableNames(db);
            // Tên còn bị bản ghi ĐÃ XOÁ giữ (lần chạy trước bị huỷ) ⇒ giải phóng trước khi chép / vẽ.
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (var name in plan.FromTemplate.Concat(plan.Drawn)) SymbolTables.FreeErasedName(tr, bt, name);
                tr.Commit();
            }
            if (plan.FromTemplate.Count > 0)
            {
                using (var tdb = new Database(false, true))
                {
                    tdb.ReadDwgFile(TemplatePath, FileOpenMode.OpenForReadAndAllShare, true, "");
                    var ids = new ObjectIdCollection();
                    using (Transaction tr = tdb.TransactionManager.StartOpenCloseTransaction())
                    {
                        var bt = (BlockTable)tr.GetObject(tdb.BlockTableId, OpenMode.ForRead);
                        foreach (var b in plan.FromTemplate) ids.Add(SymbolTables.Require(bt, b, "Template block"));
                        tr.Commit();
                    }
                    // Ignore: block/layer trùng tên đã có trong bản vẽ giữ nguyên, không bị template ghi đè.
                    tdb.WblockCloneObjects(ids, db.BlockTableId, new IdMapping(), DuplicateRecordCloning.Ignore, false);
                }
            }
            if (plan.Drawn.Count > 0)
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                    foreach (var name in plan.Drawn) DrawBlock(tr, db, bt, name);
                    tr.Commit();
                }
            }
            var after = TableNames(db);
            return after.Where(n => !before.Contains(n)).OrderBy(n => n).ToList();
        }

        /// <summary>Block trong <paramref name="names"/> chưa có bản ghi còn sống trong bản vẽ (kèm "[erased name]" nếu tên đang bị
        /// bản ghi đã xoá giữ) — kiểm tra ngay sau <see cref="Apply"/>.</summary>
        public static List<string> NotLive(Database db, IEnumerable<string> names)
        {
            var r = new List<string>();
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (var n in names)
                    if (!SymbolTables.HasLive(bt, n)) r.Add(bt.Has(n) ? n + " [erased name]" : n);
                tr.Commit();
            }
            return r;
        }

        private static bool CanDraw(string name)
        {
            if (SymbolBlockShapes.Has(name)) return true;
            if (!ProfileBlocks.TryParse(name, out string kind, out double h, out double t)) return false;
            return kind == "BS" || new HpProfileService().TryGetSection(ProfileBlocks.HpSpec(h, t), out _, out _);
        }

        private static void DrawBlock(Transaction tr, Database db, BlockTable bt, string name)
        {
            if (SymbolBlockShapes.Has(name)) DrawSymbol(tr, db, bt, name);
            else DrawProfile(tr, bt, name);
        }

        /// <summary>Block ký hiệu theo danh mục (hình học chép từ tờ mẫu 7D-17P). Layer thiếu ⇒ tạo theo CAS.dwt (lựa chọn (a));
        /// linetype của layer chưa có trong bản vẽ ⇒ Continuous.</summary>
        private static void DrawSymbol(Transaction tr, Database db, BlockTable bt, string name)
        {
            var btr = new BlockTableRecord { Name = name, Origin = Point3d.Origin };
            bt.Add(btr); tr.AddNewlyCreatedDBObject(btr, true);
            foreach (ShapeItem s in SymbolBlockShapes.Get(name))
            {
                EnsureLayer(tr, db, s.Layer);
                Entity e;
                switch (s.Kind)
                {
                    case "Line":
                        e = new Line(new Point3d(s.Points[0].Item1, s.Points[0].Item2, 0), new Point3d(s.Points[1].Item1, s.Points[1].Item2, 0));
                        break;
                    case "Polyline":
                        var pl = new Polyline { Closed = true };
                        for (int i = 0; i < s.Points.Count; i++) pl.AddVertexAt(i, new Point2d(s.Points[i].Item1, s.Points[i].Item2), 0, 0, 0);
                        e = pl;
                        break;
                    case "Solid":
                        Point3d P(int i) => new Point3d(s.Points[i].Item1, s.Points[i].Item2, 0);
                        e = new Solid(P(0), P(1), P(2), P(3));
                        break;
                    case "MText":
                        var ts = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
                        e = new MText
                        {
                            Contents = s.Text, TextHeight = s.Height, Width = s.Width, Attachment = AttachmentPoint.TopLeft,
                            Location = new Point3d(s.Points[0].Item1, s.Points[0].Item2, 0),
                            TextStyleId = SymbolTables.HasLive(ts, "STANDARD") ? SymbolTables.Live(ts, "STANDARD") : db.Textstyle,
                        };
                        break;
                    case "Leader":
                        var ld = new Leader();
                        foreach (var q in s.Points) ld.AppendVertex(new Point3d(q.Item1, q.Item2, 0));
                        var ds = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
                        ObjectId style = SymbolTables.Live(ds, s.DimStyle ?? "");
                        if (style.IsNull) style = SymbolTables.Live(ds, "AM_ISO");
                        if (!style.IsNull) ld.DimensionStyle = style;
                        ld.HasArrowHead = true;
                        e = ld;
                        break;
                    default:   // BlockRef — block lồng đã được vẽ/chép trước (Prepare xếp thứ tự)
                        e = new BlockReference(Point3d.Origin, SymbolTables.Require(bt, s.Block, "Block")) { Rotation = s.Rotation };
                        break;
                }
                e.Layer = s.Layer;
                if (s.Color != 256) e.ColorIndex = s.Color;
                if (s.LtScale != 1) e.LinetypeScale = s.LtScale;
                btr.AppendEntity(e); tr.AddNewlyCreatedDBObject(e, true);
            }
        }

        internal static void EnsureLayer(Transaction tr, Database db, string name)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (SymbolTables.HasLive(lt, name)) return;
            var spec = SymbolBlockShapes.Layer(name);
            var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            var rec = new LayerTableRecord { Name = name };
            if (spec != null)
            {
                rec.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, spec.Color);
                rec.LineWeight = (LineWeight)spec.LineweightHundredths;
                if (SymbolTables.HasLive(ltt, spec.Linetype)) rec.LinetypeObjectId = SymbolTables.Live(ltt, spec.Linetype);
            }
            lt.UpgradeOpen(); lt.Add(rec); tr.AddNewlyCreatedDBObject(rec, true);
        }

        /// <summary>Profile cùng hệ trục với block template (gốc = lưng∩đầu, +X về bụng, −Y xuống). HP: layer 0; BS: Mechanical-AM_0
        /// — như "HP120x6" / "BS 80x6" của CAS.dwt.</summary>
        private static void DrawProfile(Transaction tr, BlockTable bt, string name)
        {
            ProfileBlocks.TryParse(name, out string kind, out double h, out double t);
            var pl = new Polyline { Closed = true };
            if (kind == "HP")
            {
                new HpProfileService().TryGetSection(ProfileBlocks.HpSpec(h, t), out List<VertexBulge> sec, out _);
                for (int i = 0; i < sec.Count; i++) pl.AddVertexAt(i, sec[i].Pt, sec[i].Bulge, 0, 0);
                pl.Layer = "0";
            }
            else
            {
                var v = ProfileBlocks.FlatBarOutline(h, t);
                for (int i = 0; i < v.Count; i++) pl.AddVertexAt(i, new Point2d(v[i].Item1, v[i].Item2), 0, 0, 0);
                pl.Layer = "Mechanical-AM_0";
            }
            var btr = new BlockTableRecord { Name = name, Origin = Point3d.Origin };
            bt.Add(btr); tr.AddNewlyCreatedDBObject(btr, true);
            btr.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
        }

        private static HashSet<string> TableNames(Database db)
        {
            var r = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                void Add(ObjectId tableId, string kind)
                {
                    foreach (ObjectId id in (SymbolTable)tr.GetObject(tableId, OpenMode.ForRead))
                        r.Add(kind + " " + ((SymbolTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name);
                }
                Add(db.LayerTableId, "layer");
                Add(db.TextStyleTableId, "text style");
                Add(db.LinetypeTableId, "linetype");
                Add(db.DimStyleTableId, "dim style");
                tr.Commit();
            }
            return r;
        }
    }
}
