using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCG_3DPanel.Models;
using MCG_3DPanel.Services.Classify;
using MCG_3DPanel.Services.Corrections;
using MCG_3DPanel.UI;

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

        public ClassificationResult Classify(ObjectId blockRefId, PanelPosition position,
            IReadOnlyDictionary<string, PartCorrection> corrections = null)
        {
            var result = new ClassificationResult();
            Database db = blockRefId.Database;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // openErased: Keep xoá block reference 2D — rebuild (tab Result) vẫn phải đọc lại được nó.
                var topRef = (BlockReference)tr.GetObject(blockRefId, OpenMode.ForRead, true);
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

                // Bản vẽ 701-series: tấm boong (Top Plate) vẽ trên layer Mechanical-AM_0, KHÔNG phải "0"
                // (7D-17C thì trên "0"). Layer "0" chỉ còn mốc Ø80 → `top` ra bé tí, boong 3D biến mất
                // (xác nhận kỹ sư 2026-09-04). Nhận diện: polyline AM_0 phủ ~toàn panel + đặc, mà Top Plate
                // layer-0 (nếu có) nhỏ hơn hẳn. Mốc Ø80 layer-0 nằm trong boong = lỗ khoét boong → CutOut.
                PanelPart deckOnAm0 = FindFullSpanPlate(am0, all);
                bool deckPromoted = deckOnAm0 != null && (top == null || top.Area < deckOnAm0.Area * 0.25);
                if (deckPromoted)
                {
                    foreach (var p in topCands)   // mốc/loop layer-0 trong boong = lỗ khoét
                        if (ClassifyUtil.StrictlyInside(p, deckOnAm0, ClassifyUtil.EDGE_TOL))
                        {
                            bool isOldTop = ReferenceEquals(p, top);
                            p.Type = PartType.CutOut;
                            if (!isOldTop) result.Parts.Add(p);
                        }
                    if (top != null && top.Type == PartType.TopPlate)
                        top.Type = PartType.Unknown;   // mốc layer-0 cũ KHÔNG nằm trong boong → không dựng

                    deckOnAm0.Type = PartType.TopPlate;
                    result.TopPlate = deckOnAm0;
                    result.Parts.Add(deckOnAm0);
                    am0.Remove(deckOnAm0);
                    result.Notes.Add($"[ClassificationService] Top Plate = polyline AM_0 {deckOnAm0.Id.Handle} " +
                        $"(phủ toàn panel, area {deckOnAm0.Area:0}) — layer \"0\" chỉ có mốc nhỏ" +
                        (top != null ? $" ({top.Id.Handle})" : "") + " → xử lý là CutOut. (701-series: boong vẽ ở AM_0)");
                    top = deckOnAm0;
                }

                ObjectId topKey = top != null ? OwnerBlock(top) : ObjectId.Null;
                var faceplates = result.OfType(PartType.FacePlate).ToList();
                var longWebs = am0.Where(p => p.MemberLength > ClassifyUtil.WEB_MIN_LEN).ToList();

                // CutOut layer-0: polyline layer 0 khác Top Plate, cùng block Top Plate, nằm HẲN trong biên.
                if (top != null && !deckPromoted)
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

                // 2-key. Khoá ổn định cho correction tab Result + đổi loại trong cùng họ (TRƯỚC khi tách GE/GB
                //        và gán mã bracket, để các bước sau xử lý theo loại mới).
                AssignSourceKeys(result, hpParts, fbParts);
                ApplyTypeCorrections(result, corrections);

                // 2a. Tầng Web DÀI (≥1000) có đầu chạm HP/FB → cắt GE (L<775) / GB
                GirderEndSplitter.Apply(result, hpParts, fbParts, corrections);

                // 2b. Tách Girder End (GE) khỏi Bracket (collinear + liên tục với Web chính)
                GirderEndDetector.Apply(result, hpParts, fbParts);

                // 3. Mã bracket + hướng bụng HP
                Point3d center = result.TopPlate?.Centroid ?? Point3d.Origin;
                BracketCoder.Apply(result, hpParts, fbParts, position, center, corrections);

                tr.Commit();
            }

            Debug.WriteLine($"{LOG_PREFIX} {result.Summary()}");
            return result;
        }

        /// <summary>
        /// SourceKey = "{handle}#{i}", i = thứ tự xuất hiện trong nhóm part cùng Id (1 polyline tách nhiều đoạn).
        /// Thứ tự duyệt TẤT ĐỊNH (List, không Hash) ⇒ cùng bản 2D cho cùng key giữa các lần phân loại.
        /// </summary>
        private static void AssignSourceKeys(ClassificationResult result, List<PanelPart> hpParts, List<PanelPart> fbParts)
        {
            var seen = new HashSet<PanelPart>();
            var ordered = new List<PanelPart>();
            foreach (var p in result.Parts.Concat(hpParts).Concat(fbParts))
                if (seen.Add(p)) ordered.Add(p);
            var counter = new Dictionary<ObjectId, int>();
            foreach (var p in ordered)
            {
                counter.TryGetValue(p.Id, out int i);
                p.SourceKey = $"{p.Id.Handle}#{i}";
                counter[p.Id] = i + 1;
            }
        }

        /// <summary>Đổi PartType trong cùng họ theo correction: Web↔Closed Plate, Flange↔Faceplate, Bracket↔Backing Plate.</summary>
        private static void ApplyTypeCorrections(ClassificationResult result, IReadOnlyDictionary<string, PartCorrection> corrections)
        {
            if (corrections == null || corrections.Count == 0) return;
            foreach (var p in result.Parts)
            {
                if (p.SourceKey == null || !corrections.TryGetValue(p.SourceKey, out var c)) continue;
                PartType to = p.Type;
                switch (ResultCatalog.ParseIntent(c.Kind))
                {
                    case CorrectionIntent.ClosedPlate when p.Type == PartType.Web: to = PartType.ClosedPlate; break;
                    case CorrectionIntent.ForceWeb when p.Type == PartType.ClosedPlate: to = PartType.Web; break;
                    case CorrectionIntent.Flange when p.Type == PartType.FacePlate: to = PartType.Flange; break;
                    case CorrectionIntent.FacePlate when p.Type == PartType.Flange: to = PartType.FacePlate; break;
                    case CorrectionIntent.BackingPlate when p.Type == PartType.Bracket: to = PartType.BackingPlate; break;
                    case CorrectionIntent.BracketOB when p.Type == PartType.BackingPlate:
                    case CorrectionIntent.BracketIB when p.Type == PartType.BackingPlate:
                    case CorrectionIntent.BracketBF when p.Type == PartType.BackingPlate: to = PartType.Bracket; break;
                }
                if (to == p.Type) continue;
                result.Notes.Add($"[Correction] {p.SourceKey}: {p.Type} → {to}.");
                p.Type = to;
                if (to != PartType.Bracket && to != PartType.GirderEnd) p.Code = null;
            }
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

        /// <summary>Polyline AM_0 phủ ~toàn bộ panel (bbox ≥ 75% mỗi chiều so union bbox mọi part) và
        /// ĐẶC (area ≥ 40% bbox của chính nó — loại khung/đường bao rỗng). = tấm boong 701-series vẽ nhầm
        /// sang layer AM_0. null nếu không có. Xem chỗ gọi (promote Top Plate).</summary>
        private static PanelPart FindFullSpanPlate(List<PanelPart> am0, List<PanelPart> allParts)
        {
            if (am0.Count == 0 || allParts.Count == 0) return null;

            var u = allParts[0].Bounds;
            foreach (var p in allParts) u.AddExtents(p.Bounds);
            double uw = u.MaxPoint.X - u.MinPoint.X, uh = u.MaxPoint.Y - u.MinPoint.Y;
            if (uw < 1.0 || uh < 1.0) return null;

            PanelPart best = null;
            foreach (var p in am0)
            {
                double pw = p.Bounds.MaxPoint.X - p.Bounds.MinPoint.X;
                double ph = p.Bounds.MaxPoint.Y - p.Bounds.MinPoint.Y;
                if (pw < uw * 0.75 || ph < uh * 0.75) continue;
                if (p.Area < pw * ph * 0.4) continue;
                if (best == null || p.Area > best.Area) best = p;
            }
            return best;
        }
    }
}
