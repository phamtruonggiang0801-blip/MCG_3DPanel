using System;
using System.Globalization;
using System.IO;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

[assembly: CommandClass(typeof(MCG_3DPanel.Commands.DrawingDumpCommand))]

namespace MCG_3DPanel.Commands
{
    /// <summary>
    /// Lệnh <c>MCG_DumpDrawing</c> — dump TOÀN BỘ entity (Model Space + mọi block definition)
    /// ra file <c>.dump.txt</c> cạnh file DWG. Dùng phân tích bản vẽ tham số (OB-OB1.dwg, IB-IB1.dwg…).
    /// Tách riêng khỏi <see cref="PanelCommands"/> vì đây là công cụ CHẨN ĐOÁN, không thuộc luồng dựng 3D.
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

            var sb = new StringBuilder();
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId btrId in bt)
                {
                    var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                    if (btr.IsAnonymous || btr.IsLayout && btr.Name != BlockTableRecord.ModelSpace) continue;
                    string tag = btr.Name == BlockTableRecord.ModelSpace ? "MODEL SPACE" : "BLOCK \"" + btr.Name + "\"";
                    sb.Append($"===== {tag} =====\n");
                    foreach (ObjectId id in btr)
                    {
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent != null) sb.Append(Describe(ent));
                    }
                }
                tr.Commit();
            }

            string outPath = string.IsNullOrEmpty(db.Filename)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "drawing.dump.txt")
                : Path.ChangeExtension(db.Filename, ".dump.txt");
            File.WriteAllText(outPath, sb.ToString(), Encoding.UTF8);
            ed.WriteMessage($"\n{LOG_PREFIX} Dump: {outPath}\n");
            ReportOutput.OpenFile(outPath);
        }

        private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>Một dòng (hoặc nhiều dòng với Polyline) mô tả entity, đủ để dựng lại hình học.</summary>
        private static string Describe(Entity ent)
        {
            string L = ent.Layer;
            int c = ent.Color.ColorIndex;

            if (ent is Polyline pl)
            {
                var sb = new StringBuilder();
                sb.Append($"POLYLINE layer={L} color={c} closed={pl.Closed} nv={pl.NumberOfVertices}\n");
                for (int i = 0; i < pl.NumberOfVertices; i++)
                {
                    var p = pl.GetPoint2dAt(i);
                    sb.Append($"   v{i}: ({F(p.X)},{F(p.Y)}) bulge={F(pl.GetBulgeAt(i))}\n");
                }
                return sb.ToString();
            }
            if (ent is Line ln)
                return $"LINE   layer={L} color={c} ({F(ln.StartPoint.X)},{F(ln.StartPoint.Y)})->({F(ln.EndPoint.X)},{F(ln.EndPoint.Y)}) len={F(ln.Length)}\n";
            if (ent is Arc arc)
                return $"ARC    layer={L} color={c} c=({F(arc.Center.X)},{F(arc.Center.Y)}) r={F(arc.Radius)} ang {F(arc.StartAngle)}->{F(arc.EndAngle)}\n";
            if (ent is Circle cir)
                return $"CIRCLE layer={L} color={c} c=({F(cir.Center.X)},{F(cir.Center.Y)}) r={F(cir.Radius)}\n";
            if (ent is DBText t)
                return $"TEXT   layer={L} color={c} pos=({F(t.Position.X)},{F(t.Position.Y)}) h={F(t.Height)} \"{t.TextString}\"\n";
            if (ent is MText mt)
                return $"MTEXT  layer={L} color={c} pos=({F(mt.Location.X)},{F(mt.Location.Y)}) \"{mt.Text}\"\n";
            if (ent is Dimension dim)
                return $"DIM    layer={L} color={c} type={ent.GetType().Name} meas={F(dim.Measurement)} text=\"{dim.DimensionText}\"\n";
            if (ent is Solid3d s3d)
            {
                try
                {
                    var ext = s3d.GeometricExtents;
                    return $"SOLID3D layer={L} color={c} bbox=({F(ext.MinPoint.X)},{F(ext.MinPoint.Y)},{F(ext.MinPoint.Z)})->({F(ext.MaxPoint.X)},{F(ext.MaxPoint.Y)},{F(ext.MaxPoint.Z)})\n";
                }
                catch { return $"SOLID3D layer={L} color={c} (no ext)\n"; }
            }
            return $"OTHER  {ent.GetType().Name} layer={L} color={c}\n";
        }
    }
}
