using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using MCG_3DPanel.Services.Detail;

namespace MCG_3DPanel.Commands.Detail
{
    /// <summary>Ghi block Detail vào bản vẽ 2D: kiểm layer, định nghĩa (lại) block, kéo-thả chèn, tiêu đề, XData MCG3D_DETAIL.
    /// Mọi hàm chạy trong transaction DUY NHẤT của caller (compose + define + place + title + label); Place trả false ⇒ caller
    /// huỷ transaction (không để lại gì). Không tạo layer / text style — chỉ RegApp MCG3D_DETAIL.</summary>
    public static class DetailWriter
    {
        public const string App = "MCG3D_DETAIL";
        const string TextStyle = "STANDARD";
        const int Chunk = 255;

        /// <summary>Layer theo Global Constraints (AM_9 chỉ để bỏ qua, không cần).</summary>
        public static readonly string[] Layers =
        {
            DetailComposer.AM0, "Mechanical-AM_1", DetailComposer.AM2, DetailComposer.AM4, DetailComposer.AM5,
            DetailComposer.AM7, DetailComposer.AM8, DetailComposer.AM10,
        };

        /// <summary>Layer bản vẽ chưa có ("layer &lt;tên&gt;"). Rỗng ⇒ vẽ được.</summary>
        public static List<string> Missing(Database db)
        {
            var miss = new List<string>();
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                foreach (var l in Layers) if (!SymbolTables.HasLive(lt, l)) miss.Add("layer " + l);
                tr.Commit();
            }
            return miss;
        }

        /// <summary>Định nghĩa block <paramref name="name"/> từ <paramref name="ents"/> (gốc = 0,0) và gắn <paramref name="rec"/> vào
        /// block. Tên đã có và mang MCG3D_DETAIL ⇒ thay nội dung (reference giữ nguyên); tên do người vẽ tay tạo ⇒ thêm "_MCG"
        /// (rồi "_MCG2"…). Entity chưa vào được bản vẽ khi lỗi sẽ được giải phóng.</summary>
        public static ObjectId DefineBlock(Transaction tr, Database db, string name, IEnumerable<Entity> ents, DetailRecord rec)
        {
            var list = (ents ?? Enumerable.Empty<Entity>()).ToList();
            try
            {
                EnsureRegApp(tr, db);
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord btr = null;
                for (int k = 0; btr == null; k++)
                {
                    string n = k == 0 ? name : k == 1 ? name + "_MCG" : name + "_MCG" + k;
                    ObjectId id = SymbolTables.Live(bt, n);
                    if (id.IsNull)
                    {
                        SymbolTables.FreeErasedName(tr, bt, n);
                        btr = new BlockTableRecord { Name = n, Origin = Point3d.Origin };
                        if (!bt.IsWriteEnabled) bt.UpgradeOpen();
                        bt.Add(btr); tr.AddNewlyCreatedDBObject(btr, true);
                        break;
                    }
                    var old = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (Read(old) == null) continue;   // block vẽ tay trùng tên ⇒ thử tên kế
                    if (!old.IsWriteEnabled) old.UpgradeOpen();
                    foreach (ObjectId e in old) tr.GetObject(e, OpenMode.ForWrite, false, true).Erase();   // kể cả layer khoá
                    btr = old;
                }
                TagObject(tr, db, btr, rec);
                // Từng entity trong block: RegApp + dấu ngắn (JSON đầy đủ chỉ trên block / reference / tiêu đề)
                AppendMarked(tr, btr, list, rec);
                return btr.ObjectId;
            }
            catch
            {
                foreach (var e in list) if (!e.IsDisposed && e.ObjectId.IsNull) e.Dispose();
                throw;
            }
        }

        /// <summary>Thêm <paramref name="ents"/> vào block CÓ SẴN <paramref name="btrId"/> (vd ký hiệu mặt cắt trên DET), mỗi entity mang
        /// dấu ngắn của <paramref name="rec"/>; các reference của block được báo vẽ lại. Lỗi ⇒ giải phóng entity chưa vào bản vẽ.</summary>
        public static void AddToBlock(Transaction tr, ObjectId btrId, IEnumerable<Entity> ents, DetailRecord rec)
        {
            var list = (ents ?? Enumerable.Empty<Entity>()).ToList();
            try
            {
                EnsureRegApp(tr, btrId.Database);
                var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForWrite);
                AppendMarked(tr, btr, list, rec);
                foreach (ObjectId id in btr.GetBlockReferenceIds(true, false))
                    ((Entity)tr.GetObject(id, OpenMode.ForWrite, false, true)).RecordGraphicsModified(true);
            }
            catch
            {
                foreach (var e in list) if (!e.IsDisposed && e.ObjectId.IsNull) e.Dispose();
                throw;
            }
        }

        static void AppendMarked(Transaction tr, BlockTableRecord btr, List<Entity> list, DetailRecord rec)
        {
            var mark = new TypedValue[]
            {
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, App),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, rec.Marker()),
            };
            foreach (var e in list)
            {
                btr.AppendEntity(e);
                tr.AddNewlyCreatedDBObject(e, true);
                using (var rb = new ResultBuffer(mark)) e.XData = rb;
            }
        }

        /// <summary>Block đi theo con trỏ (EntityJig) tới khi click ⇒ chèn vào Model Space ở tỉ lệ <paramref name="scale"/>, true.
        /// Esc / chuột phải ⇒ false, không ghi gì.</summary>
        public static bool Place(Editor ed, Transaction tr, ObjectId btrId, double scale, string prompt, out BlockReference br)
        {
            br = null;
            var db = btrId.Database;
            var probe = new BlockReference(Point3d.Origin, btrId) { ScaleFactors = new Scale3d(scale) };
            probe.SetDatabaseDefaults(db);
            probe.Layer = DetailComposer.AM0;   // mẫu: reference DET / section trên AM_0
            var jig = new PlaceJig(probe, prompt);
            PromptResult res;
            try { res = ed.Drag(jig); }
            catch { probe.Dispose(); throw; }
            if (res.Status != PromptStatus.OK) { probe.Dispose(); return false; }
            try
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                ms.AppendEntity(probe);
                tr.AddNewlyCreatedDBObject(probe, true);
            }
            catch { if (probe.ObjectId.IsNull) probe.Dispose(); throw; }
            br = probe;
            return true;
        }

        sealed class PlaceJig : EntityJig
        {
            readonly string _prompt;
            Point3d _at = Point3d.Origin;
            public PlaceJig(BlockReference br, string prompt) : base(br) { _prompt = prompt; }

            protected override SamplerStatus Sampler(JigPrompts prompts)
            {
                var o = new JigPromptPointOptions("\n" + _prompt)
                {
                    UserInputControls = UserInputControls.Accept3dCoordinates | UserInputControls.NullResponseAccepted,
                };
                var r = prompts.AcquirePoint(o);
                if (r.Status != PromptStatus.OK) return SamplerStatus.Cancel;
                if (r.Value.DistanceTo(_at) < 1e-9) return SamplerStatus.NoChange;
                _at = r.Value;
                return SamplerStatus.OK;
            }

            protected override bool Update()
            {
                ((BlockReference)Entity).Position = _at;
                return true;
            }
        }

        /// <summary>Block tiêu đề như bản vẽ tay (dump 2026-11-18): reference "DETAIL TITLE" (TITLE + 4 dòng NOTE) layer 0, scale 10.</summary>
        public const string TitleBlock = "DETAIL TITLE";
        const double TitleScale = 10, TitleTop = 2.76;   // đỉnh chữ TITLE trên điểm chèn (đơn vị block): −0.74 + 3.5

        /// <summary>Tiêu đề = block <see cref="TitleBlock"/> căn giữa dưới khung bao của reference, đỉnh chữ cách
        /// <paramref name="below"/>; TITLE = <paramref name="text"/> (gạch chân "%%U"), NOTE để trống. Chép XData MCG3D_DETAIL của
        /// reference (Tag reference trước). Block phải có sẵn (caller bổ sung trước).</summary>
        public static void AddTitle(Transaction tr, BlockReference br, string text, double below = 150)
        {
            var db = br.Database;
            // Block rỗng (vd section không chạm vật liệu) không có khung bao ⇒ lấy điểm chèn
            Point3d lo, hi;
            try { var ext = br.GeometricExtents; lo = ext.MinPoint; hi = ext.MaxPoint; }
            catch (Autodesk.AutoCAD.Runtime.Exception) { lo = hi = br.Position; }
            string title = text ?? "";
            if (!title.StartsWith("%%U", StringComparison.OrdinalIgnoreCase)) title = "%%U" + title;

            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            ObjectId tid = SymbolTables.Require(bt, TitleBlock, "Block");
            var tref = new BlockReference(new Point3d((lo.X + hi.X) / 2, lo.Y - below - TitleTop * TitleScale, 0), tid)
            {
                ScaleFactors = new Scale3d(TitleScale),
            };
            tref.SetDatabaseDefaults(db);
            tref.Layer = "0";
            var owner = (BlockTableRecord)tr.GetObject(br.OwnerId, OpenMode.ForWrite);
            owner.AppendEntity(tref);
            tr.AddNewlyCreatedDBObject(tref, true);
            foreach (ObjectId id in (BlockTableRecord)tr.GetObject(tid, OpenMode.ForRead))
            {
                if (!(tr.GetObject(id, OpenMode.ForRead) is AttributeDefinition ad) || ad.Constant) continue;
                var ar = new AttributeReference();
                ar.SetAttributeFromBlock(ad, tref.BlockTransform);
                ar.TextString = string.Equals(ad.Tag, "TITLE", StringComparison.OrdinalIgnoreCase) ? title : ad.TextString;
                tref.AttributeCollection.AppendAttribute(ar);
                tr.AddNewlyCreatedDBObject(ar, true);
            }
            ResultBuffer xd = br.GetXDataForApplication(App);
            if (xd != null) using (xd) tref.XData = xd;
        }

        /// <summary>XData MCG3D_DETAIL = JSON của <paramref name="rec"/>, cắt chuỗi 255 ký tự. Entity phải nằm trong bản vẽ.</summary>
        public static void Tag(Entity e, DetailRecord rec) => Tag((DBObject)e, rec);

        public static void Tag(DBObject o, DetailRecord rec)
        {
            var db = o.Database ?? throw new InvalidOperationException("Tag needs an object in the drawing.");
            var tr = db.TransactionManager.TopTransaction ?? throw new InvalidOperationException("Tag needs an open transaction.");
            TagObject(tr, db, o, rec);
        }

        static void TagObject(Transaction tr, Database db, DBObject o, DetailRecord rec)
        {
            EnsureRegApp(tr, db);
            string json = rec.ToJson();
            var vals = new List<TypedValue> { new TypedValue((int)DxfCode.ExtendedDataRegAppName, App) };
            for (int i = 0; i < json.Length; i += Chunk)
                vals.Add(new TypedValue((int)DxfCode.ExtendedDataAsciiString, json.Substring(i, Math.Min(Chunk, json.Length - i))));
            if (!o.IsWriteEnabled) o.UpgradeOpen();
            using (var rb = new ResultBuffer(vals.ToArray())) o.XData = rb;
        }

        /// <summary>Đọc bản ghi MCG3D_DETAIL; không có / hỏng ⇒ null.</summary>
        public static DetailRecord Read(Entity e) => Read((DBObject)e);

        public static DetailRecord Read(DBObject o)
        {
            ResultBuffer rb = o?.GetXDataForApplication(App);
            if (rb == null) return null;
            using (rb)
            {
                var sb = new StringBuilder();
                foreach (TypedValue v in rb)
                    if (v.TypeCode == (int)DxfCode.ExtendedDataAsciiString) sb.Append((string)v.Value);
                return DetailRecord.FromJson(sb.ToString());
            }
        }

        static void EnsureRegApp(Transaction tr, Database db)
        {
            var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (SymbolTables.HasLive(rat, App)) return;
            if (!rat.IsWriteEnabled) rat.UpgradeOpen();
            var r = new RegAppTableRecord { Name = App };
            rat.Add(r); tr.AddNewlyCreatedDBObject(r, true);
        }
    }
}
