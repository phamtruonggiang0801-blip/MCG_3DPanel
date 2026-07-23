using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using MCG_3DPanel.Models;

namespace MCG_3DPanel.Services.Classify
{
    /// <summary>
    /// BƯỚC 2 — Tách layer Web (AM_0) thành **Web chính** vs **Bracket** vs **web-tier**:
    ///  - dài (&gt; WEB_MIN_LEN) → Web chính;
    ///  - ngắn **và CHẠM HP/Flat Bar** → Bracket (mọi bậc dày: OB/OB1/FB…);
    ///  - ngắn **không chạm** (đoạn web inline đổi dày) → web-tier, vẫn là Web.
    ///
    /// ⚠️ KHÔNG dùng chiều dày để phân loại (OB1 dày 10 vẫn là bracket) và KHÔNG dùng collinear
    /// (nhiều OB collinear với web). Xem skill bracket-identification mục 1.
    /// </summary>
    internal static class WebBracketSplitter
    {
        public static void Split(List<PanelPart> webCands, List<PanelPart> hpParts,
            List<PanelPart> fbParts, ClassificationResult result)
        {
            var webPieces = new List<PanelPart>();
            var brkPieces = new List<PanelPart>();
            foreach (var p in webCands)
            {
                bool isShort = p.MemberLength < ClassifyUtil.WEB_MIN_LEN;
                bool touches = hpParts.Any(h => ClassifyUtil.Adjacent(p, h, ClassifyUtil.ADJ_TOL))
                            || fbParts.Any(f => ClassifyUtil.Adjacent(p, f, ClassifyUtil.ADJ_TOL));
                if (isShort && touches) { p.Type = PartType.Bracket; brkPieces.Add(p); }
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
