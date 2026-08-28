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

                // 1b. Lọc về ĐÚNG panel: chỉ giữ part thuộc 2 block con (chứa Top Plate + chứa Web chính).
                //     Part ở block con KHÁC (dù đúng Layer/Color) là của đối tượng khác → bỏ.
                FilterToPanelBlocks(all);
                Debug.WriteLine($"{LOG_PREFIX} Sau lọc block panel: {all.Count} part.");

                // Định tuyến theo LAYER (+ màu cho AM_3). AM_0 HOÃN lại để tách tấm/cutout trước web.
                var topCands = new List<PanelPart>();
                var am0 = new List<PanelPart>();
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
                            am0.Add(p);
                            break;
                        case ClassifyUtil.LAYER_FBHP:
                            if (p.ColorIndex == ClassifyUtil.COLOR_HP) { p.Type = PartType.HP; hpParts.Add(p); result.Parts.Add(p); }
                            else { p.Type = PartType.FlatBar; fbParts.Add(p); }   // FB: KHÔNG add ngay — chờ tách tại chỗ giao HP (bên dưới).
                            break;
                    }
                }

                // Top Plate = AREA lớn nhất trên layer 0.
                PanelPart top = null;
                if (topCands.Count > 0)
                {
                    top = topCands.OrderByDescending(p => p.Area).First();
                    top.Type = PartType.TopPlate;
                    result.TopPlate = top;
                    result.Parts.Add(top);
                }
                ObjectId topKey = top != null ? OwnerBlock(top) : ObjectId.Null;
                var faceplates = result.OfType(PartType.FacePlate).ToList();
                var longWebs = am0.Where(p => p.MemberLength > ClassifyUtil.WEB_MIN_LEN).ToList();

                // CutOut layer-0: polyline layer 0 khác Top Plate, cùng block Top Plate, nằm HẲN trong biên.
                if (top != null)
                    foreach (var p in topCands)
                    {
                        if (ReferenceEquals(p, top)) continue;
                        if (OwnerBlock(p) == topKey && ClassifyUtil.StrictlyInside(p, top, ClassifyUtil.EDGE_TOL))
                        { p.Type = PartType.CutOut; result.Parts.Add(p); }
                    }

                // FlatBar: người vẽ 2D thường kéo 1 polyline liên tục xuyên qua HP/Web cho nhanh, nhưng
                // THỰC TẾ FB không liên tục — nó đứt tại đó, chừa khe = bề dày member cắt ngang (xác nhận
                // kỹ sư 2026-08-27, xem skill sniped-end). Tách TRƯỚC khi add vào result.Parts / dùng ở
                // vòng am0 bên dưới, để mỗi đoạn có 2 đầu mút THẬT (cần cho FlatBarBuilder xác định đầu tự
                // do). Xét cắt bởi HP + MỌI member AM_0 TRỪ lỗ khoét (Web/tier/Bracket/ClosedPlate — còn
                // Unknown ở bước này nên chỉ dùng hình học; loại lỗ khoét = cùng block Top Plate + nằm hẳn
                // trong biên, đúng điều kiện CutOut ở vòng am0). Không member nào cắt ngang → giữ nguyên.
                var fbCrossers = am0.Where(p => !(top != null && OwnerBlock(p) == topKey
                        && ClassifyUtil.StrictlyInside(p, top, ClassifyUtil.EDGE_TOL))).ToList();
                fbParts = FlatBarSplitter.Split(fbParts, hpParts, fbCrossers);
                result.Parts.AddRange(fbParts);

                // AM_0: tách CutOut / BackingPlate / ClosedPlate; phần còn lại mới đưa vào Web/Bracket.
                // ⚠️ CutOut xét ĐẦU TIÊN khi "cùng owner block với biên Top Plate" (xác nhận kỹ sư
                // 2026-08-25: "Lỗ khoét của Top Plate sẽ được để cùng Polyline của Block Top Plate") —
                // đây là tín hiệu TỔ CHỨC BẢN VẼ (part nằm trong block CHUYÊN DÙNG cho Top Plate, không
                // phải block Structure chứa Web/Bracket thật), đáng tin hơn hẳn heuristic hình học
                // (IsClosedPlate/IsBackingPlate chỉ xét overlap/kề — 1 lỗ khoét tình cờ nằm ĐÈ lên 1 Web/
                // FacePlate bên dưới trong plan view vẫn có thể khớp nhầm). Bug thật đã xảy ra: panel
                // 7D-17C có 6 lỗ khoét khác (`38DDD`,`38DE0`..`38DE4`, cùng block "7D-17C_Top Plate" với
                // biên `38DDC`) bị rơi vào ClosedPlate/Web (đứng thẳng, extrude −WebHeight xuống đáy)
                // thay vì bị trừ (subtract) như lỗ, vì check block-owner trước đó đặt SAU CÙNG.
                // (Trước đây, lúc `top` còn bị nhận nhầm — xem Bug #8 — đặt check này lên đầu từng "cướp
                // nhầm" 1 ClosedPlate/Bracket thật, vì `topKey` khi đó trỏ SAI sang block Structure. Giờ
                // `top` đã đúng block Top Plate riêng biệt (chỉ chứa biên+lỗ+mốc COG, không có Web/Bracket
                // thật) nên rủi ro đó không còn.)
                foreach (var p in am0)
                {
                    if (top != null && OwnerBlock(p) == topKey && ClassifyUtil.StrictlyInside(p, top, ClassifyUtil.EDGE_TOL))
                    { p.Type = PartType.CutOut; result.Parts.Add(p); continue; }

                    if (PlateDetector.IsBackingPlate(p, faceplates, longWebs))
                    { p.Type = PartType.BackingPlate; result.Parts.Add(p); continue; }

                    bool touches = hpParts.Any(h => ClassifyUtil.Adjacent(p, h, ClassifyUtil.ADJ_TOL))
                                || fbParts.Any(f => ClassifyUtil.Adjacent(p, f, ClassifyUtil.ADJ_TOL));
                    if (!touches && PlateDetector.IsClosedPlate(p, faceplates, longWebs, am0, hpParts, fbParts))
                    { p.Type = PartType.ClosedPlate; result.Parts.Add(p); continue; }

                    // "Chạm" nhưng KHÔNG đáng tin (bbox chéo của HP bị phồng, hoặc member khớp lại
                    // NGẮN HƠN p — phi lý làm chỗ hàn) ⇒ vẫn xét ClosedPlate, đòi hỏi bằng chứng MẠNH
                    // hơn hẳn (đa giác FacePlate thật ≥90%, không phải bbox 25%). Xem ClassifyUtil.HasOnlyWeakTouch.
                    if (touches && ClassifyUtil.HasOnlyWeakTouch(p, hpParts, fbParts, ClassifyUtil.ADJ_TOL)
                        && PlateDetector.IsClosedPlateStrict(p, faceplates, am0, hpParts, fbParts))
                    { p.Type = PartType.ClosedPlate; result.Parts.Add(p); continue; }

                    webCands.Add(p);
                }

                // 2. Web vs Bracket
                WebBracketSplitter.Split(webCands, am0, hpParts, fbParts, result);

                // 2b. Tách Girder End (GE) khỏi Bracket (collinear + liên tục với Web chính)
                GirderEndDetector.Apply(result, hpParts, fbParts);

                // 3. Mã bracket + hướng bụng HP
                Point3d center = result.TopPlate?.Centroid ?? Point3d.Origin;
                BracketCoder.Apply(result, hpParts, fbParts, position, center);

                tr.Commit();
            }

            Debug.WriteLine($"{LOG_PREFIX} {result.Summary()}");
            return result;
        }

        /// <summary>
        /// Giữ lại đúng part của panel: gom theo BLOCK CON cấp 1 (<c>IdPath[1]</c>), chỉ giữ 2 block —
        /// block chứa TOP PLATE (polyline layer 0 diện tích lớn nhất) và block chứa WEB CHÍNH
        /// (tổng chiều dài member AM_0 lớn nhất). Part ở block con khác bị loại (là đối tượng khác).
        /// Không xác định được Top Plate ⇒ giữ nguyên (an toàn).
        /// </summary>
        private static void FilterToPanelBlocks(List<PanelPart> all)
        {
            if (all.Count == 0) return;

            var top = all.Where(p => p.Layer == ClassifyUtil.LAYER_TOPPLATE)
                         .OrderByDescending(p => p.Area).FirstOrDefault();
            if (top == null) return;

            ObjectId topKey = OwnerBlock(top);
            ObjectId webKey = all.Where(p => p.Layer == ClassifyUtil.LAYER_WEB)
                                 .GroupBy(OwnerBlock)
                                 .OrderByDescending(g => g.Sum(p => p.MemberLength))
                                 .Select(g => g.Key)
                                 .DefaultIfEmpty(topKey)
                                 .First();

            var keep = new HashSet<ObjectId> { topKey, webKey };
            all.RemoveAll(p => !keep.Contains(OwnerBlock(p)));
        }

        /// <summary>Block con cấp 1 chứa part = <c>IdPath[1]</c> (phần tử 0 là block ngoài cùng được pick).
        /// Part nằm trực tiếp trong block ngoài cùng (không qua block con) ⇒ ObjectId.Null.</summary>
        private static ObjectId OwnerBlock(PanelPart p)
            => (p.IdPath != null && p.IdPath.Length >= 3) ? p.IdPath[1] : ObjectId.Null;
    }
}
