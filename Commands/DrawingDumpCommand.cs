using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using MCG_3DPanel.Utilities;

[assembly: CommandClass(typeof(MCG_3DPanel.Commands.DrawingDumpCommand))]

namespace MCG_3DPanel.Commands
{
    /// <summary>
    /// Lệnh <c>MCG_DumpDrawing</c> — dump TOÀN BỘ entity (Model Space + mọi block definition) ra file
    /// <c>.dump.json</c> cạnh file DWG. JSON (thay txt cũ) để đọc bằng code đáng tin: vertices/bulge là SỐ,
    /// block lồng nhau rõ ràng, dễ diff/lọc. Dùng phân tích bản vẽ tham số (GE/OB/IB…).
    /// Tách khỏi <see cref="PanelCommands"/> vì là công cụ CHẨN ĐOÁN.
    ///
    /// 2026-11-10: thêm phần <c>tables</c> (layer, linetype, text style, dim style, biến hệ thống) và
    /// thuộc tính style trên từng entity (linetype, text style, dim style + điểm đặt + biến bị override),
    /// để làm chuẩn cho Create Drawing ▸ Plan View: bản vẽ sinh ra phải dùng đúng style có sẵn của template.
    /// </summary>
    public class DrawingDumpCommand
    {
        private const string LOG_PREFIX = "[MCG_3DPanel]";

        [CommandMethod("MCG_DumpDrawing")]
        public void DumpDrawing()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            var blocks = new List<object>();
            JsonObject tables;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                tables = DescribeTables(db, tr);

                // So sánh theo ObjectId: tên Model Space có thể là "*Model_Space" hoặc "*MODEL_SPACE"
                // tuỳ bản vẽ (so chuỗi đã từng gắn nhầm Model Space thành "paper").
                ObjectId modelId = SymbolUtilityServices.GetBlockModelSpaceId(db);
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId btrId in bt)
                {
                    var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                    if (btr.IsAnonymous) continue;   // bỏ block ẩn (dim/hatch def); GIỮ Model + Layout(paper) + block def

                    bool isModel = btrId == modelId;
                    string space = isModel ? "model" : btr.IsLayout ? "paper" : "block";

                    var ents = new List<object>();
                    foreach (ObjectId id in btr)
                    {
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;
                        try { ents.Add(Describe(ent, tr)); }
                        catch (System.Exception ex)
                        {
                            ents.Add(new JsonObject
                            {
                                { "type", ent.GetType().Name }, { "handle", ent.Handle.ToString() }, { "error", ex.Message },
                            });
                        }
                    }
                    var bo = new JsonObject
                    {
                        { "block", isModel ? "*MODEL_SPACE" : btr.Name },
                        { "space", space },              // model | paper (layout) | block (definition)
                        { "isModelSpace", isModel },
                    };
                    if (btr.Origin != Point3d.Origin) bo.Add("origin", P(btr.Origin));
                    bo.Add("count", ents.Count);
                    bo.Add("entities", ents);
                    blocks.Add(bo);
                }
                tr.Commit();
            }

            var root = new JsonObject
            {
                { "drawing", string.IsNullOrEmpty(db.Filename) ? "" : Path.GetFileName(db.Filename) },
                { "dumpVersion", 2 },
                { "tables", tables },
                { "blockCount", blocks.Count },
                { "blocks", blocks },
            };

            string outPath = string.IsNullOrEmpty(db.Filename)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "drawing.dump.json")
                : Path.ChangeExtension(db.Filename, ".dump.json");
            File.WriteAllText(outPath, JsonWriter.Serialize(root), Encoding.UTF8);
            ed.WriteMessage($"\n{LOG_PREFIX} Dump JSON: {outPath}\n");
            string openErr = ReportOutput.OpenFile(outPath);
            if (openErr != null) ed.WriteMessage($"{LOG_PREFIX} Could not open the file: {openErr}\n");
        }

        // ───────────────────────────── Bảng ký hiệu (symbol tables) ─────────────────────────────

        private static readonly string[] SYSVARS =
        {
            "INSUNITS", "MEASUREMENT", "LUNITS", "LUPREC", "LTSCALE", "PSLTSCALE", "MSLTSCALE", "CELTSCALE",
            "CLAYER", "TEXTSTYLE", "DIMSTYLE", "DIMSCALE", "CANNOSCALE", "TILEMODE",
        };

        private static JsonObject DescribeTables(Database db, Transaction tr)
        {
            var sys = new JsonObject();
            foreach (string v in SYSVARS)
            {
                object val;
                try { val = Application.GetSystemVariable(v); } catch { val = null; }
                sys.Add(v, val is short s ? (int)s : val);
            }

            var layers = new List<object>();
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            foreach (ObjectId id in lt)
            {
                var r = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                layers.Add(new JsonObject
                {
                    { "name", r.Name }, { "color", ColorText(r.Color) },
                    { "linetype", NameOf(r.LinetypeObjectId, tr) }, { "lineweight", r.LineWeight.ToString() },
                    { "off", r.IsOff }, { "frozen", r.IsFrozen }, { "locked", r.IsLocked }, { "plot", r.IsPlottable },
                    { "description", r.Description },
                });
            }

            var linetypes = new List<object>();
            var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            foreach (ObjectId id in ltt)
            {
                var r = (LinetypeTableRecord)tr.GetObject(id, OpenMode.ForRead);
                linetypes.Add(new JsonObject
                {
                    { "name", r.Name }, { "description", r.AsciiDescription }, { "patternLength", r.PatternLength },
                });
            }

            var textStyles = new List<object>();
            var tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            foreach (ObjectId id in tst)
            {
                var r = (TextStyleTableRecord)tr.GetObject(id, OpenMode.ForRead);
                if (r.IsShapeFile) continue;   // bản ghi ẩn của file .shx dùng cho linetype
                string face;
                try { face = r.Font.TypeFace; } catch { face = ""; }
                textStyles.Add(new JsonObject
                {
                    { "name", r.Name }, { "font", r.FileName }, { "bigFont", r.BigFontFileName }, { "typeFace", face },
                    { "height", r.TextSize }, { "widthFactor", r.XScale }, { "oblique", r.ObliquingAngle },
                    { "annotative", r.Annotative.ToString() },
                });
            }

            var dimStyles = new List<object>();
            var dst = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
            foreach (ObjectId id in dst)
            {
                var r = (DimStyleTableRecord)tr.GetObject(id, OpenMode.ForRead);
                var o = new JsonObject { { "name", r.Name }, { "current", id == db.Dimstyle } };
                foreach (var v in DIMVARS) o.Add(v.Name, Safe(() => v.FromStyle(r, tr)));
                dimStyles.Add(o);
            }

            var mleaderStyles = new List<object>();
            try
            {
                var mld = (DBDictionary)tr.GetObject(db.MLeaderStyleDictionaryId, OpenMode.ForRead);
                foreach (DBDictionaryEntry e in mld) mleaderStyles.Add(e.Key);
            }
            catch { /* bản vẽ không có dictionary MLeader style */ }

            return new JsonObject
            {
                { "sysvars", sys },
                { "layers", layers },
                { "linetypes", linetypes },
                { "textStyles", textStyles },
                { "dimStyles", dimStyles },
                { "mleaderStyles", mleaderStyles },
            };
        }

        /// <summary>Một biến kích thước: đọc được cả từ Dim Style lẫn từ chính kích thước (giá trị hiệu lực).
        /// Khác nhau ⇒ kích thước đang override biến đó.</summary>
        private sealed class DimVar
        {
            public string Name;
            public Func<DimStyleTableRecord, Transaction, object> FromStyle;
            public Func<Dimension, Transaction, object> FromDim;
        }

        private static readonly DimVar[] DIMVARS =
        {
            V("DIMSCALE", (s, t) => s.Dimscale, (d, t) => d.Dimscale),
            V("DIMLFAC",  (s, t) => s.Dimlfac,  (d, t) => d.Dimlfac),
            V("DIMTXT",   (s, t) => s.Dimtxt,   (d, t) => d.Dimtxt),
            V("DIMTXSTY", (s, t) => NameOf(s.Dimtxsty, t), (d, t) => NameOf(d.TextStyleId, t)),
            V("DIMASZ",   (s, t) => s.Dimasz,   (d, t) => d.Dimasz),
            V("DIMBLK",   (s, t) => ArrowName(s.Dimblk, t),  (d, t) => ArrowName(d.Dimblk, t)),
            V("DIMBLK1",  (s, t) => ArrowName(s.Dimblk1, t), (d, t) => ArrowName(d.Dimblk1, t)),
            V("DIMBLK2",  (s, t) => ArrowName(s.Dimblk2, t), (d, t) => ArrowName(d.Dimblk2, t)),
            V("DIMSAH",   (s, t) => s.Dimsah,   (d, t) => d.Dimsah),
            V("DIMTSZ",   (s, t) => s.Dimtsz,   (d, t) => d.Dimtsz),
            V("DIMEXO",   (s, t) => s.Dimexo,   (d, t) => d.Dimexo),
            V("DIMEXE",   (s, t) => s.Dimexe,   (d, t) => d.Dimexe),
            V("DIMDLI",   (s, t) => s.Dimdli,   (d, t) => d.Dimdli),
            V("DIMDLE",   (s, t) => s.Dimdle,   (d, t) => d.Dimdle),
            V("DIMGAP",   (s, t) => s.Dimgap,   (d, t) => d.Dimgap),
            V("DIMTAD",   (s, t) => s.Dimtad,   (d, t) => d.Dimtad),
            V("DIMJUST",  (s, t) => s.Dimjust,  (d, t) => d.Dimjust),
            V("DIMTIH",   (s, t) => s.Dimtih,   (d, t) => d.Dimtih),
            V("DIMTOH",   (s, t) => s.Dimtoh,   (d, t) => d.Dimtoh),
            V("DIMTIX",   (s, t) => s.Dimtix,   (d, t) => d.Dimtix),
            V("DIMTOFL",  (s, t) => s.Dimtofl,  (d, t) => d.Dimtofl),
            V("DIMATFIT", (s, t) => s.Dimatfit, (d, t) => d.Dimatfit),
            V("DIMTMOVE", (s, t) => s.Dimtmove, (d, t) => d.Dimtmove),
            V("DIMSE1",   (s, t) => s.Dimse1,   (d, t) => d.Dimse1),
            V("DIMSE2",   (s, t) => s.Dimse2,   (d, t) => d.Dimse2),
            V("DIMSD1",   (s, t) => s.Dimsd1,   (d, t) => d.Dimsd1),
            V("DIMSD2",   (s, t) => s.Dimsd2,   (d, t) => d.Dimsd2),
            V("DIMFXLON", (s, t) => s.DimfxlenOn, (d, t) => d.DimfxlenOn),
            V("DIMFXL",   (s, t) => s.Dimfxlen, (d, t) => d.Dimfxlen),
            V("DIMLUNIT", (s, t) => s.Dimlunit, (d, t) => d.Dimlunit),
            V("DIMDEC",   (s, t) => s.Dimdec,   (d, t) => d.Dimdec),
            V("DIMRND",   (s, t) => s.Dimrnd,   (d, t) => d.Dimrnd),
            V("DIMZIN",   (s, t) => s.Dimzin,   (d, t) => d.Dimzin),
            V("DIMDSEP",  (s, t) => s.Dimdsep.ToString(), (d, t) => d.Dimdsep.ToString()),
            V("DIMPOST",  (s, t) => s.Dimpost,  (d, t) => d.Dimpost),
            V("DIMCEN",   (s, t) => s.Dimcen,   (d, t) => d.Dimcen),
            V("DIMCLRD",  (s, t) => ColorText(s.Dimclrd), (d, t) => ColorText(d.Dimclrd)),
            V("DIMCLRE",  (s, t) => ColorText(s.Dimclre), (d, t) => ColorText(d.Dimclre)),
            V("DIMCLRT",  (s, t) => ColorText(s.Dimclrt), (d, t) => ColorText(d.Dimclrt)),
            V("DIMLWD",   (s, t) => s.Dimlwd.ToString(), (d, t) => d.Dimlwd.ToString()),
            V("DIMLWE",   (s, t) => s.Dimlwe.ToString(), (d, t) => d.Dimlwe.ToString()),
            V("DIMLTYPE", (s, t) => NameOf(s.Dimltype, t), (d, t) => NameOf(d.Dimltype, t)),
            V("DIMTFILL", (s, t) => s.Dimtfill, (d, t) => d.Dimtfill),
        };

        private static DimVar V(string name, Func<DimStyleTableRecord, Transaction, object> fromStyle,
                                Func<Dimension, Transaction, object> fromDim)
            => new DimVar { Name = name, FromStyle = fromStyle, FromDim = fromDim };

        // ───────────────────────────── Entity ─────────────────────────────

        /// <summary>Mô tả 1 entity thành JsonObject — đủ để dựng lại hình học và style.</summary>
        private static object Describe(Entity ent, Transaction tr)
        {
            JsonObject o = DescribeShape(ent, tr);
            AddCommonStyle(o, ent);
            return o;
        }

        /// <summary>Thuộc tính chung: handle luôn có; linetype / lineweight / ltscale chỉ ghi khi khác mặc định
        /// (ByLayer, 1.0) để file không phình.</summary>
        private static void AddCommonStyle(JsonObject o, Entity ent)
        {
            bool hasHandle = false;
            foreach (var kv in o) if (kv.Key == "handle") { hasHandle = true; break; }
            if (!hasHandle) o.Insert(1, new KeyValuePair<string, object>("handle", ent.Handle.ToString()));

            if (!string.Equals(ent.Linetype, "ByLayer", StringComparison.OrdinalIgnoreCase)) o.Add("linetype", ent.Linetype);
            if (Math.Abs(ent.LinetypeScale - 1.0) > 1e-9) o.Add("ltScale", ent.LinetypeScale);
            if (ent.LineWeight != LineWeight.ByLayer) o.Add("lineweight", ent.LineWeight.ToString());
            if (ent.Color.ColorMethod == ColorMethod.ByColor) o.Add("trueColor", ColorText(ent.Color));
        }

        private static JsonObject DescribeShape(Entity ent, Transaction tr)
        {
            string L = ent.Layer;
            int c = ent.Color.ColorIndex;

            if (ent is BlockReference br)
            {
                string bn;
                try
                {
                    var id = br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord;
                    bn = ((BlockTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name;
                }
                catch { bn = "?"; }

                // Đọc Block Attribute (note quy cách thường để dạng này): tag + giá trị text + style.
                var attrs = new List<object>();
                try
                {
                    foreach (ObjectId aid in br.AttributeCollection)
                    {
                        if (tr.GetObject(aid, OpenMode.ForRead) is AttributeReference ar)
                            attrs.Add(new JsonObject
                            {
                                { "tag", ar.Tag }, { "text", ar.TextString }, { "style", ar.TextStyleName },
                                { "h", ar.Height }, { "x", ar.Position.X }, { "y", ar.Position.Y },
                                { "invisible", ar.Invisible },
                            });
                    }
                }
                catch { /* block không có attribute */ }

                var obj = new JsonObject
                {
                    { "type", "BlockRef" }, { "layer", L }, { "color", c }, { "name", bn },
                    { "x", br.Position.X }, { "y", br.Position.Y },
                    { "rot", br.Rotation }, { "sx", br.ScaleFactors.X }, { "sy", br.ScaleFactors.Y },
                };
                if (attrs.Count > 0) obj.Add("attributes", attrs);
                return obj;
            }
            if (ent is Polyline pl)
            {
                var verts = new List<object>(pl.NumberOfVertices);
                for (int i = 0; i < pl.NumberOfVertices; i++)
                {
                    var p = pl.GetPoint2dAt(i);
                    verts.Add(new JsonObject
                    {
                        { "x", p.X }, { "y", p.Y }, { "bulge", pl.GetBulgeAt(i) },
                    });
                }
                var po = new JsonObject
                {
                    { "type", "Polyline" }, { "handle", ent.Handle.ToString() }, { "layer", L }, { "color", c },
                    { "closed", pl.Closed }, { "nv", pl.NumberOfVertices },
                };
                if (pl.Plinegen) po.Add("plinegen", true);
                try { if (pl.ConstantWidth > 0) po.Add("width", pl.ConstantWidth); } catch { /* bề rộng không đều */ }
                po.Add("vertices", verts);
                return po;
            }
            if (ent is Line ln)
                return new JsonObject
                {
                    { "type", "Line" }, { "layer", L }, { "color", c },
                    { "x0", ln.StartPoint.X }, { "y0", ln.StartPoint.Y },
                    { "x1", ln.EndPoint.X }, { "y1", ln.EndPoint.Y }, { "len", ln.Length },
                };
            if (ent is Arc arc)
                return new JsonObject
                {
                    { "type", "Arc" }, { "layer", L }, { "color", c },
                    { "cx", arc.Center.X }, { "cy", arc.Center.Y }, { "r", arc.Radius },
                    { "a0", arc.StartAngle }, { "a1", arc.EndAngle },
                };
            if (ent is Circle cir)
                return new JsonObject
                {
                    { "type", "Circle" }, { "layer", L }, { "color", c },
                    { "cx", cir.Center.X }, { "cy", cir.Center.Y }, { "r", cir.Radius },
                };
            if (ent is DBText t)
            {
                var to = new JsonObject
                {
                    { "type", "Text" }, { "layer", L }, { "color", c },
                    { "x", t.Position.X }, { "y", t.Position.Y }, { "h", t.Height }, { "text", t.TextString },
                    { "style", t.TextStyleName }, { "rot", t.Rotation },
                };
                if (Math.Abs(t.WidthFactor - 1.0) > 1e-9) to.Add("widthFactor", t.WidthFactor);
                if (Math.Abs(t.Oblique) > 1e-9) to.Add("oblique", t.Oblique);
                if (t.Justify != AttachmentPoint.BaseLeft)
                {
                    to.Add("justify", t.Justify.ToString());
                    to.Add("alignX", t.AlignmentPoint.X);
                    to.Add("alignY", t.AlignmentPoint.Y);
                }
                return to;
            }
            if (ent is MText mt)
                return new JsonObject
                {
                    { "type", "MText" }, { "layer", L }, { "color", c },
                    { "x", mt.Location.X }, { "y", mt.Location.Y }, { "text", mt.Text },
                    { "contents", mt.Contents },          // chuỗi gốc, giữ mã định dạng (\H, \C, \P…)
                    { "style", mt.TextStyleName }, { "h", mt.TextHeight }, { "width", mt.Width },
                    { "attach", mt.Attachment.ToString() }, { "rot", mt.Rotation },
                };
            if (ent is Dimension dim) return DescribeDimension(dim, tr);
            if (ent is Leader ld)
            {
                var vs = new List<object>();
                for (int i = 0; i < ld.NumVertices; i++) vs.Add(P(ld.VertexAt(i)));
                return new JsonObject
                {
                    { "type", "Leader" }, { "layer", L }, { "color", c },
                    { "dimStyle", ld.DimensionStyleName }, { "arrow", ld.HasArrowHead },
                    { "spline", ld.IsSplined }, { "annoType", ld.AnnoType.ToString() }, { "vertices", vs },
                };
            }
            if (ent is MLeader ml)
            {
                string txt = "";
                try { if (ml.ContentType == ContentType.MTextContent && ml.MText != null) txt = ml.MText.Text; } catch { }
                return new JsonObject
                {
                    { "type", "MLeader" }, { "layer", L }, { "color", c },
                    { "style", NameOf(ml.MLeaderStyle, tr) }, { "content", ml.ContentType.ToString() }, { "text", txt },
                    { "extents", Extents(ent) },
                };
            }
            if (ent is DBPoint pt)
                return new JsonObject { { "type", "DBPoint" }, { "layer", L }, { "color", c }, { "pos", P(pt.Position) } };
            if (ent is Solid sd)
                return new JsonObject
                {
                    { "type", "Solid" }, { "layer", L }, { "color", c },
                    { "points", new List<object> { P(sd.GetPointAt(0)), P(sd.GetPointAt(1)), P(sd.GetPointAt(2)), P(sd.GetPointAt(3)) } },
                };
            if (ent is Hatch hat)
            {
                // Biên Hatch = outline THẬT của chi tiết tô đặc (vd tấm Collar Plate "LUG B1R" trong
                // CAS.dwt). Trước đây rơi vào fallback cuối → chỉ có type/layer/color, MẤT hình học.
                string pat;
                try { pat = hat.PatternName; } catch { pat = "?"; }
                var loops = new List<object>();
                try
                {
                    for (int i = 0; i < hat.NumberOfLoops; i++)
                    {
                        HatchLoop loop = hat.GetLoopAt(i);
                        var lo = new JsonObject { { "loopType", loop.LoopType.ToString() } };
                        if (loop.IsPolyline)
                        {
                            var vs = new List<object>();
                            foreach (BulgeVertex bv in loop.Polyline)
                                vs.Add(new JsonObject { { "x", bv.Vertex.X }, { "y", bv.Vertex.Y }, { "bulge", bv.Bulge } });
                            lo.Add("polyline", true);
                            lo.Add("vertices", vs);
                        }
                        else
                        {
                            var cs = new List<object>();
                            foreach (Curve2d cv in loop.Curves)
                            {
                                var co = new JsonObject
                                {
                                    { "curve", cv.GetType().Name },
                                    { "x0", cv.StartPoint.X }, { "y0", cv.StartPoint.Y },
                                    { "x1", cv.EndPoint.X }, { "y1", cv.EndPoint.Y },
                                };
                                if (cv is CircularArc2d ca)
                                {
                                    co.Add("cx", ca.Center.X); co.Add("cy", ca.Center.Y); co.Add("r", ca.Radius);
                                    co.Add("a0", ca.StartAngle); co.Add("a1", ca.EndAngle);
                                }
                                cs.Add(co);
                            }
                            lo.Add("polyline", false);
                            lo.Add("curves", cs);
                        }
                        loops.Add(lo);
                    }
                }
                catch (System.Exception ex) { loops.Add(new JsonObject { { "error", ex.Message } }); }

                var ho = new JsonObject
                {
                    { "type", "Hatch" }, { "handle", ent.Handle.ToString() }, { "layer", L }, { "color", c },
                    { "pattern", pat },
                };
                try
                {
                    ho.Add("patternType", hat.PatternType.ToString());
                    ho.Add("patternScale", hat.PatternScale);
                    ho.Add("patternAngle", hat.PatternAngle);
                }
                catch { /* hatch solid/gradient */ }
                ho.Add("numLoops", hat.NumberOfLoops);
                ho.Add("loops", loops);
                return ho;
            }
            if (ent is Solid3d s3d)
            {
                try
                {
                    var e = s3d.GeometricExtents;
                    return new JsonObject
                    {
                        { "type", "Solid3d" }, { "handle", ent.Handle.ToString() }, { "layer", L }, { "color", c },
                        { "min", new List<object> { e.MinPoint.X, e.MinPoint.Y, e.MinPoint.Z } },
                        { "max", new List<object> { e.MaxPoint.X, e.MaxPoint.Y, e.MaxPoint.Z } },
                    };
                }
                catch { return new JsonObject { { "type", "Solid3d" }, { "handle", ent.Handle.ToString() }, { "layer", L }, { "color", c } }; }
            }

            // Còn lại (Spline, Ellipse, proxy của AutoCAD Mechanical…): tên lớp DXF + khung bao.
            string dxf;
            try { dxf = ent.GetRXClass().DxfName; } catch { dxf = ""; }
            return new JsonObject
            {
                { "type", ent.GetType().Name }, { "dxf", dxf }, { "layer", L }, { "color", c }, { "extents", Extents(ent) },
            };
        }

        /// <summary>Kích thước: dim style, chữ, các điểm định nghĩa theo từng loại, và những biến DIM*
        /// mà kích thước này override so với dim style của nó.</summary>
        private static JsonObject DescribeDimension(Dimension dim, Transaction tr)
        {
            var o = new JsonObject
            {
                { "type", "Dim" }, { "layer", dim.Layer }, { "color", dim.Color.ColorIndex },
                { "dimType", dim.GetType().Name }, { "meas", dim.Measurement }, { "text", dim.DimensionText },
                { "dimStyle", dim.DimensionStyleName },
                { "textPos", P(dim.TextPosition) }, { "textRot", dim.TextRotation },
                { "defaultTextPos", dim.UsingDefaultTextPosition },
            };

            switch (dim)
            {
                case RotatedDimension rd:
                    o.Add("p1", P(rd.XLine1Point)); o.Add("p2", P(rd.XLine2Point));
                    o.Add("dimLine", P(rd.DimLinePoint)); o.Add("rot", rd.Rotation);
                    if (Math.Abs(rd.Oblique) > 1e-9) o.Add("oblique", rd.Oblique);
                    break;
                case AlignedDimension ad:
                    o.Add("p1", P(ad.XLine1Point)); o.Add("p2", P(ad.XLine2Point));
                    o.Add("dimLine", P(ad.DimLinePoint));
                    break;
                case DiametricDimension dd:
                    o.Add("chord", P(dd.ChordPoint)); o.Add("farChord", P(dd.FarChordPoint));
                    o.Add("leaderLen", dd.LeaderLength);
                    break;
                case RadialDimension rad:
                    o.Add("center", P(rad.Center)); o.Add("chord", P(rad.ChordPoint));
                    o.Add("leaderLen", rad.LeaderLength);
                    break;
                case OrdinateDimension od:
                    o.Add("origin", P(od.Origin)); o.Add("defPoint", P(od.DefiningPoint));
                    o.Add("leaderEnd", P(od.LeaderEndPoint)); o.Add("xAxis", od.UsingXAxis);
                    break;
                case LineAngularDimension2 la:
                    o.Add("l1a", P(la.XLine1Start)); o.Add("l1b", P(la.XLine1End));
                    o.Add("l2a", P(la.XLine2Start)); o.Add("l2b", P(la.XLine2End));
                    o.Add("arcPoint", P(la.ArcPoint));
                    break;
            }

            // Override: biến có giá trị hiệu lực trên kích thước khác giá trị trong dim style.
            var overrides = new JsonObject();
            try
            {
                var style = (DimStyleTableRecord)tr.GetObject(dim.DimensionStyle, OpenMode.ForRead);
                foreach (var v in DIMVARS)
                {
                    object a = Safe(() => v.FromStyle(style, tr));
                    object b = Safe(() => v.FromDim(dim, tr));
                    if (!SameValue(a, b)) overrides.Add(v.Name, b);
                }
            }
            catch (System.Exception ex) { overrides.Add("error", ex.Message); }
            if (overrides.Count > 0) o.Add("overrides", overrides);
            return o;
        }

        // ───────────────────────────── Tiện ích ─────────────────────────────

        private static List<object> P(Point3d p) => new List<object> { p.X, p.Y };

        private static object Extents(Entity ent)
        {
            try
            {
                var e = ent.GeometricExtents;
                return new List<object> { e.MinPoint.X, e.MinPoint.Y, e.MaxPoint.X, e.MaxPoint.Y };
            }
            catch { return null; }
        }

        private static string NameOf(ObjectId id, Transaction tr)
        {
            if (id.IsNull) return "";
            try
            {
                var rec = tr.GetObject(id, OpenMode.ForRead);
                if (rec is SymbolTableRecord str) return str.Name;
                if (rec is MLeaderStyle mls) return mls.Name;
                return rec.GetType().Name;
            }
            catch { return "?"; }
        }

        /// <summary>Mũi tên: ObjectId rỗng = mặc định "Closed filled"; còn lại là tên block (vd _ACMFILLED15).</summary>
        private static string ArrowName(ObjectId id, Transaction tr) => id.IsNull ? "(closed filled)" : NameOf(id, tr);

        private static string ColorText(Color col)
        {
            if (col == null) return "";
            switch (col.ColorMethod)
            {
                case ColorMethod.ByLayer: return "ByLayer";
                case ColorMethod.ByBlock: return "ByBlock";
                case ColorMethod.ByAci: return col.ColorIndex.ToString();
                default: return $"{col.Red},{col.Green},{col.Blue}";
            }
        }

        private static object Safe(Func<object> f)
        {
            try
            {
                object v = f();
                if (v is short s) return (int)s;
                if (v is byte b) return (int)b;
                if (v is Enum e) return e.ToString();
                return v;
            }
            catch (System.Exception ex) { return "!" + ex.Message; }
        }

        private static bool SameValue(object a, object b)
        {
            if (a is double da && b is double db) return Math.Abs(da - db) < 1e-9;
            return Equals(a, b);
        }
    }
}
