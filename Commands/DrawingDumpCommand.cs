using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
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
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId btrId in bt)
                {
                    var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                    if (btr.IsAnonymous) continue;   // bỏ block ẩn (dim/hatch def); GIỮ Model + Layout(paper) + block def

                    bool isModel = btr.Name == BlockTableRecord.ModelSpace;
                    string space = isModel ? "model" : btr.IsLayout ? "paper" : "block";

                    var ents = new List<object>();
                    foreach (ObjectId id in btr)
                    {
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent != null) ents.Add(Describe(ent, tr));
                    }
                    blocks.Add(new JsonObject
                    {
                        { "block", isModel ? "*MODEL_SPACE" : btr.Name },
                        { "space", space },              // model | paper (layout) | block (definition)
                        { "isModelSpace", isModel },
                        { "count", ents.Count },
                        { "entities", ents },
                    });
                }
                tr.Commit();
            }

            var root = new JsonObject
            {
                { "drawing", string.IsNullOrEmpty(db.Filename) ? "" : Path.GetFileName(db.Filename) },
                { "blockCount", blocks.Count },
                { "blocks", blocks },
            };

            string outPath = string.IsNullOrEmpty(db.Filename)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "drawing.dump.json")
                : Path.ChangeExtension(db.Filename, ".dump.json");
            File.WriteAllText(outPath, JsonWriter.Serialize(root), Encoding.UTF8);
            ed.WriteMessage($"\n{LOG_PREFIX} Dump JSON: {outPath}\n");
            ReportOutput.OpenFile(outPath);
        }

        /// <summary>Mô tả 1 entity thành JsonObject — đủ để dựng lại hình học.</summary>
        private static object Describe(Entity ent, Transaction tr)
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

                // Đọc Block Attribute (note quy cách thường để dạng này): tag + giá trị text.
                var attrs = new List<object>();
                try
                {
                    foreach (ObjectId aid in br.AttributeCollection)
                    {
                        if (tr.GetObject(aid, OpenMode.ForRead) is AttributeReference ar)
                            attrs.Add(new JsonObject { { "tag", ar.Tag }, { "text", ar.TextString } });
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
                return new JsonObject
                {
                    { "type", "Polyline" }, { "handle", ent.Handle.ToString() }, { "layer", L }, { "color", c },
                    { "closed", pl.Closed }, { "nv", pl.NumberOfVertices }, { "vertices", verts },
                };
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
                return new JsonObject
                {
                    { "type", "Text" }, { "layer", L }, { "color", c },
                    { "x", t.Position.X }, { "y", t.Position.Y }, { "h", t.Height }, { "text", t.TextString },
                };
            if (ent is MText mt)
                return new JsonObject
                {
                    { "type", "MText" }, { "layer", L }, { "color", c },
                    { "x", mt.Location.X }, { "y", mt.Location.Y }, { "text", mt.Text },
                };
            if (ent is Dimension dim)
                return new JsonObject
                {
                    { "type", "Dim" }, { "layer", L }, { "color", c },
                    { "dimType", ent.GetType().Name }, { "meas", dim.Measurement }, { "text", dim.DimensionText },
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

                return new JsonObject
                {
                    { "type", "Hatch" }, { "handle", ent.Handle.ToString() }, { "layer", L }, { "color", c },
                    { "pattern", pat }, { "numLoops", hat.NumberOfLoops }, { "loops", loops },
                };
            }
            if (ent is Solid3d s3d)
            {
                try
                {
                    var e = s3d.GeometricExtents;
                    return new JsonObject
                    {
                        { "type", "Solid3d" }, { "layer", L }, { "color", c },
                        { "min", new List<object> { e.MinPoint.X, e.MinPoint.Y, e.MinPoint.Z } },
                        { "max", new List<object> { e.MaxPoint.X, e.MaxPoint.Y, e.MaxPoint.Z } },
                    };
                }
                catch { return new JsonObject { { "type", "Solid3d" }, { "layer", L }, { "color", c } }; }
            }
            return new JsonObject { { "type", ent.GetType().Name }, { "layer", L }, { "color", c } };
        }
    }
}
