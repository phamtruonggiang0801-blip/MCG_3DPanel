using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCG_3DPanel.Utilities;

namespace MCG_3DPanel.Services
{
    /// <summary>
    /// Duyệt đệ quy Block Panel (kể cả Nesting Block) và xuất toàn bộ dữ liệu entity ra JSON.
    /// Mục đích: lấy dữ liệu THẬT (layer, color, hình học, cây nesting) để đối chiếu quy tắc
    /// nhận diện trong skill panel-parts-reference trước khi dựng 3D.
    /// </summary>
    public class InspectorService : IInspectorService
    {
        private const string LOG_PREFIX = "[InspectorService]";

        // Histogram (layer|colorIndex) -> số lượng, để tổng hợp nhanh cuối file.
        private readonly Dictionary<string, int> _layerColorCount = new Dictionary<string, int>();
        private int _totalEntities;

        public string InspectToJson(ObjectId blockRefId, string outputPath)
        {
            Debug.WriteLine($"{LOG_PREFIX} Bắt đầu inspect {blockRefId}.");
            _layerColorCount.Clear();
            _totalEntities = 0;

            Database db = blockRefId.Database;
            var root = new JsonObject();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var topRef = (BlockReference)tr.GetObject(blockRefId, OpenMode.ForRead);
                string topName = GetEffectiveName(tr, topRef);

                root.Add("panelBlockName", topName);
                root.Add("handle", topRef.Handle.ToString());
                root.Add("layer", topRef.Layer);
                root.Add("position", Pt(topRef.Position));
                root.Add("rotationDeg", Round(topRef.Rotation * 180.0 / Math.PI));
                root.Add("scale", Pt(new Point3d(topRef.ScaleFactors.X, topRef.ScaleFactors.Y, topRef.ScaleFactors.Z)));

                // Duyệt nội dung block. Bắt đầu bằng transform của chính block ref => toạ độ WCS.
                var children = new List<object>();
                WalkBlock(tr, topRef, topRef.BlockTransform, topName, 1, children);
                root.Add("children", children);

                tr.Commit();
            }

            // Tổng hợp
            var summary = new JsonObject { { "totalEntities", _totalEntities } };
            var histo = new List<object>();
            foreach (var kv in _layerColorCount)
            {
                string[] parts = kv.Key.Split('|');
                histo.Add(new JsonObject
                {
                    { "layer", parts[0] },
                    { "colorIndex", int.Parse(parts[1]) },
                    { "count", kv.Value },
                });
            }
            summary.Add("byLayerColor", histo);

            var final = new JsonObject
            {
                { "summary", summary },
                { "panel", root },
            };

            string json = JsonWriter.Serialize(final);
            File.WriteAllText(outputPath, json, System.Text.Encoding.UTF8);
            Debug.WriteLine($"{LOG_PREFIX} Ghi {outputPath} ({_totalEntities} entities).");
            return outputPath;
        }

        /// <summary>Duyệt các entity trong định nghĩa của 1 block reference.</summary>
        private void WalkBlock(Transaction tr, BlockReference blockRef, Matrix3d accum, string path, int depth, List<object> outList)
        {
            var btr = (BlockTableRecord)tr.GetObject(blockRef.BlockTableRecord, OpenMode.ForRead);
            foreach (ObjectId id in btr)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;
                outList.Add(BuildEntityNode(tr, ent, accum, path, depth));
            }
        }

        private JsonObject BuildEntityNode(Transaction tr, Entity ent, Matrix3d accum, string path, int depth)
        {
            _totalEntities++;
            string colorKey = ent.Layer + "|" + ent.Color.ColorIndex;
            _layerColorCount.TryGetValue(colorKey, out int c);
            _layerColorCount[colorKey] = c + 1;

            var node = new JsonObject
            {
                { "type", ent.GetType().Name },
                { "dxfName", TryGet(() => ent.GetRXClass().DxfName) },
                { "handle", ent.Handle.ToString() },
                { "path", path },
                { "depth", depth },
                { "layer", ent.Layer },
                { "colorMethod", ent.Color.ColorMethod.ToString() },
                { "colorIndex", (int)ent.Color.ColorIndex },
            };

            // Hình học Polyline (LWPolyline) — thứ ta quan tâm nhất để nhận diện.
            if (ent is Polyline pl)
            {
                node.Add("isClosed", pl.Closed);
                node.Add("numVertices", pl.NumberOfVertices);
                node.Add("length", TryGetD(() => pl.Length));
                node.Add("area", TryGetD(() => pl.Area));
                node.Add("vertices", ReadVertices(pl, accum));
            }
            else if (ent is Curve crv)
            {
                node.Add("isClosed", crv.Closed);
                node.Add("area", crv.Closed ? TryGetD(() => crv.Area) : (object)null);
            }

            // Bounding box (WCS, đã nhân accum).
            try
            {
                Extents3d ext = ent.GeometricExtents;
                ext.TransformBy(accum);
                node.Add("bboxMin", Pt(ext.MinPoint));
                node.Add("bboxMax", Pt(ext.MaxPoint));
            }
            catch { node.Add("bbox", null); }

            // Nếu là Block Reference => đệ quy vào bên trong (Nesting Block).
            if (ent is BlockReference nestedRef)
            {
                node.Add("blockName", GetEffectiveName(tr, nestedRef));
                node.Add("blockPosition", Pt(nestedRef.Position.TransformBy(accum)));
                node.Add("isDynamic", nestedRef.IsDynamicBlock);
                var kids = new List<object>();
                Matrix3d childAccum = accum * nestedRef.BlockTransform;
                WalkBlock(tr, nestedRef, childAccum, path + " / " + GetEffectiveName(tr, nestedRef), depth + 1, kids);
                node.Add("children", kids);
            }

            return node;
        }

        /// <summary>Đọc toạ độ đỉnh polyline (đã transform về WCS) + bulge.</summary>
        private List<object> ReadVertices(Polyline pl, Matrix3d accum)
        {
            var list = new List<object>();
            for (int i = 0; i < pl.NumberOfVertices; i++)
            {
                Point3d p = pl.GetPoint3dAt(i).TransformBy(accum);
                double bulge = pl.GetBulgeAt(i);
                list.Add(new JsonObject
                {
                    { "p", Pt(p) },
                    { "bulge", Round(bulge) },
                });
            }
            return list;
        }

        /// <summary>Tên block hiển thị (xử lý dynamic/anonymous block).</summary>
        private string GetEffectiveName(Transaction tr, BlockReference br)
        {
            try
            {
                if (br.IsDynamicBlock)
                {
                    var dyn = (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead);
                    return dyn.Name;
                }
                var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                return btr.Name;
            }
            catch { return "<unknown>"; }
        }

        private static List<object> Pt(Point3d p) => new List<object> { Round(p.X), Round(p.Y), Round(p.Z) };
        private static double Round(double v) => Math.Round(v, 4);

        private static object TryGet(Func<string> f)
        {
            try { return f(); } catch { return null; }
        }
        private static object TryGetD(Func<double> f)
        {
            try { return Round(f()); } catch { return null; }
        }
    }
}
