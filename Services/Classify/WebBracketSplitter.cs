using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using MCG_3DPanel.Models;

namespace MCG_3DPanel.Services.Classify
{
    /// <summary>
    /// BƯỚC 2 — Tách layer Web (AM_0) thành **Web chính** vs **Bracket** vs **web-tier**, theo ĐÚNG
    /// định nghĩa vật lý của mã bracket (OB/IB/OB1.../FB): **1 đầu mút gắn vào Web (hay member AM_0
    /// khác — tier/Plate/Bracket…), đầu KIA gắn vào HP hoặc Flat Bar**. Nếu CẢ HAI đầu đều gắn vào
    /// AM_0 khác (không đầu nào chạm HP/FB) ⇒ chỉ là đoạn NỐI TIẾP của Web, không phải Bracket —
    /// bất kể dài/ngắn, collinear hay chênh dày ra sao (xem <see cref="ClassifyUtil.IsBracketPattern"/>).
    ///
    /// ⚠️ Dùng ĐOẠN TÂM thật (Vertices) cho từng đầu mút — không phải bbox — nên xử lý đúng cả member
    /// CHÉO (VD đoạn vát ở khúc cua panel) mà cách cũ (collinear theo trục X/Y) không nhận ra được.
    /// Kiểm chứng 7D-17C: khớp tuyệt đối 108/108 part AM_0. Xem skill panel-parts-reference mục 4d/6.
    /// </summary>
    internal static class WebBracketSplitter
    {
        /// <param name="webCands">Phần AM_0 CHƯA quyết định — sẽ tách thành Web/Bracket ở đây.</param>
        /// <param name="allAm0">TOÀN BỘ AM_0 của panel (kể cả phần đã quyết định BackingPlate/ClosedPlate/
        /// CutOut trước đó) — làm "mốc" cho <see cref="ClassifyUtil.IsBracketPattern"/> khi xét đầu mút p
        /// có chạm member AM_0 khác không. ⚠️ PHẢI dùng allAm0 (không phải webCands) cho việc này: 1 đầu
        /// bracket có thể chạm đúng 1 ClosedPlate/BackingPlate (đã bị loại khỏi webCands ở bước trước) —
        /// dùng webCands sẽ "mất dấu" match đó và gán nhầm Bracket thành Web (đã xảy ra thật: 38EF9 chạm
        /// ClosedPlate 38E4B ở 1 đầu, bị gán nhầm Web khi otherAm0 chỉ truyền webCands).</param>
        public static void Split(List<PanelPart> webCands, List<PanelPart> allAm0, List<PanelPart> hpParts,
            List<PanelPart> fbParts, ClassificationResult result)
        {
            var webPieces = new List<PanelPart>();
            var brkPieces = new List<PanelPart>();
            foreach (var p in webCands)
            {
                // Polygon AM_0 quá DÀY để là tôn web/plate thật (tôn kết cấu ≤ ~32mm) — gần như luôn là
                // đường bao panel / nét dựng phụ lọt layer AM_0 (vd 701S handle 6875E: 15931×9039mm,
                // ~100m²). Nhận là Web thì: (1) dựng thành khối đặc khổng lồ; (2) FootprintThickness sai
                // lan sang WebHpCutoutBuilder + WebCollarPlateBuilder → collar dài hàng mét, lệch ra ngoài
                // panel (bug 2026-09-04). Để Unknown → Build3DService bỏ qua, vẫn hiện trong report
                // (built=false) + Notes. CutOut/BackingPlate/ClosedPlate đã tách TRƯỚC bước này nên
                // ngưỡng dày không đụng tới chúng.
                if (p.FootprintThickness > ClassifyUtil.WEB_MAX_THK)
                {
                    p.Type = PartType.Unknown;
                    result.Parts.Add(p);
                    double bw = p.Bounds.MaxPoint.X - p.Bounds.MinPoint.X;
                    double bh = p.Bounds.MaxPoint.Y - p.Bounds.MinPoint.Y;
                    result.Notes.Add($"[WebBracketSplitter] BỎ QUA polygon AM_0 {p.Id.Handle}: bề dày " +
                        $"{p.FootprintThickness:0} > {ClassifyUtil.WEB_MAX_THK:0}mm (bbox {bw:0}×{bh:0}) — " +
                        "không phải Web thật (nghi đường bao / nét dựng), không dựng solid.");
                    continue;
                }

                bool isShort = p.MemberLength < ClassifyUtil.WEB_MIN_LEN;
                bool isBracket = isShort
                    && ClassifyUtil.IsBracketPattern(p, allAm0, hpParts, fbParts, ClassifyUtil.ADJ_TOL);

                if (isBracket) { p.Type = PartType.Bracket; brkPieces.Add(p); }
                else { p.Type = PartType.Web; webPieces.Add(p); }
            }
            GroupWebRuns(webPieces);
            result.Parts.AddRange(webPieces);
            result.Parts.AddRange(brkPieces);
        }

        /// <summary>Gom Web piece collinear thành run (GroupId) — chỉ để hiển thị web nhiều tầng dày.</summary>
        private static void GroupWebRuns(List<PanelPart> webPieces)
        {
            int n = webPieces.Count;
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            void Union(int a, int b) { parent[Find(a)] = Find(b); }

            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    if (Collinear(webPieces[i], webPieces[j])) Union(i, j);

            var gidMap = new Dictionary<int, int>();
            int gid = 0;
            for (int i = 0; i < n; i++)
            {
                int r = Find(i);
                if (!gidMap.TryGetValue(r, out int g)) { g = gid++; gidMap[r] = g; }
                webPieces[i].GroupId = g;
            }
        }

        /// <summary>Cùng phương, thẳng hàng (lệch ngang nhỏ), chồng/nối theo trục dài.</summary>
        private static bool Collinear(PanelPart a, PanelPart b)
        {
            if (a.IsHorizontal != b.IsHorizontal) return false;
            double perp = Math.Abs(a.PerpCenter - b.PerpCenter);
            if (perp > (a.FootprintThickness + b.FootprintThickness) / 2 + ClassifyUtil.TOL_PERP) return false;

            Extents3d ba = a.Bounds, bb = b.Bounds;
            if (a.IsHorizontal)
                return ba.MinPoint.X - ClassifyUtil.TOL_GAP <= bb.MaxPoint.X && ba.MaxPoint.X + ClassifyUtil.TOL_GAP >= bb.MinPoint.X;
            return ba.MinPoint.Y - ClassifyUtil.TOL_GAP <= bb.MaxPoint.Y && ba.MaxPoint.Y + ClassifyUtil.TOL_GAP >= bb.MinPoint.Y;
        }
    }
}
