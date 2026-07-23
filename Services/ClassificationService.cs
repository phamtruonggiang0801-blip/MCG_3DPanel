using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCG_3DPanel.Models;
using MCG_3DPanel.Services.Classify;

namespace MCG_3DPanel.Services
{
    /// <summary>
    /// ĐIỀU PHỐI phân loại — KHÔNG chứa hình học chi tiết. Pipeline 3 bước, mỗi bước 1 file
    /// trong <c>Services/Classify/</c> (xem skill module-per-object):
    ///   1. <see cref="PartCollector"/>      — đọc DWG → PanelPart (hình học WCS).
    ///   2. <see cref="WebBracketSplitter"/> — tách AM_0 thành Web chính / Bracket / web-tier.
    ///   3. <see cref="BracketCoder"/>       — gán mã OB/IB/FB + đóng dấu hướng bụng HP.
    /// Bản thân service chỉ định tuyến theo LAYER và chọn Top Plate.
    /// Xem skill panel-parts-reference + bracket-identification.
    /// </summary>
    public class ClassificationService : IClassificationService
    {
        private const string LOG_PREFIX = "[ClassificationService]";

        public ClassificationResult Classify(ObjectId blockRefId, PanelPosition position)
        {
            var result = new ClassificationResult();
            Database db = blockRefId.Database;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var topRef = (BlockReference)tr.GetObject(blockRefId, OpenMode.ForRead);
                result.PanelBlockName = PartCollector.GetName(tr, topRef);

                // 1. Thu thập
                var all = new List<PanelPart>();
                PartCollector.Collect(tr, topRef, topRef.BlockTransform, all);
                Debug.WriteLine($"{LOG_PREFIX} Thu {all.Count} closed polyline.");

                // Định tuyến theo LAYER (+ màu cho AM_3).
                var topCands = new List<PanelPart>();
                var webCands = new List<PanelPart>();
                var hpParts = new List<PanelPart>();
                var fbParts = new List<PanelPart>();

                foreach (var p in all)
                {
                    switch (p.Layer)
                    {
                        case ClassifyUtil.LAYER_TOPPLATE:
                            topCands.Add(p);
                            break;
                        case ClassifyUtil.LAYER_FLANGE:
                            p.Type = ClassifyUtil.IsRectangle(p) ? PartType.Flange : PartType.FacePlate;
                            result.Parts.Add(p);
                            break;
                        case ClassifyUtil.LAYER_WEB:
                            webCands.Add(p);
                            break;
                        case ClassifyUtil.LAYER_FBHP:
                            if (p.ColorIndex == ClassifyUtil.COLOR_HP) { p.Type = PartType.HP; hpParts.Add(p); }
                            else { p.Type = PartType.FlatBar; fbParts.Add(p); }
                            result.Parts.Add(p);
                            break;
                    }
                }

                // Top Plate = AREA lớn nhất trên layer 0.
                if (topCands.Count > 0)
                {
                    var top = topCands.OrderByDescending(p => p.Area).First();
                    top.Type = PartType.TopPlate;
                    result.TopPlate = top;
                    result.Parts.Add(top);
                }

                // 2. Web vs Bracket
                WebBracketSplitter.Split(webCands, hpParts, fbParts, result);

                // 3. Mã bracket + hướng bụng HP
                Point3d center = result.TopPlate?.Centroid ?? Point3d.Origin;
                BracketCoder.Apply(result, hpParts, fbParts, position, center);

                tr.Commit();
            }

            Debug.WriteLine($"{LOG_PREFIX} {result.Summary()}");
            return result;
        }
    }
}
