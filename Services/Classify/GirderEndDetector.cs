using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using MCG_3DPanel.Models;

namespace MCG_3DPanel.Services.Classify
{
    /// <summary>
    /// BƯỚC 2b — Tách GIRDER END (GE) khỏi nhóm "Bracket-candidate".
    /// GE = mảnh AM_0 **DÀI (≥ GE_MIN_LEN) + DÀY (≥ GE_MIN_THK)**, chạm HP/FB.
    ///
    /// Không cần loại-trừ tier ở đây nữa: <see cref="WebBracketSplitter"/> (chạy TRƯỚC, dùng
    /// <see cref="ClassifyUtil.IsBracketPattern"/>) đã đảm bảo MỌI mảnh còn là <see cref="PartType.Bracket"/>
    /// khi tới đây đều có đúng 1 đầu mút chạm HP/FB thật (đoạn "cả 2 đầu đều là Web" — tức tier nối
    /// tiếp girder — đã bị loại thành Web ngay từ bước đó, không lọt tới đây). Kiểm chứng 7D-17C: khớp
    /// tuyệt đối 108/108 part AM_0. Xem skill panel-parts-reference mục 4d/6.
    ///
    /// Chạy SAU <see cref="WebBracketSplitter"/> và TRƯỚC <see cref="BracketCoder"/>.
    /// Mảnh nào là GE → đổi Type=GirderEnd + Code "GE-HP"/"GE-FB" (builder tinh chỉnh GE1/2/5/6 theo D).
    /// </summary>
    internal static class GirderEndDetector
    {
        public static void Apply(ClassificationResult result, List<PanelPart> hpParts, List<PanelPart> fbParts)
        {
            foreach (var p in result.OfType(PartType.Bracket).ToList())
            {
                if (p.MemberLength < ClassifyUtil.GE_MIN_LEN || p.FootprintThickness < ClassifyUtil.GE_MIN_THK) continue;

                bool hp = hpParts.Any(h => ClassifyUtil.Adjacent(p, h, ClassifyUtil.ADJ_TOL));
                bool fb = fbParts.Any(f => ClassifyUtil.Adjacent(p, f, ClassifyUtil.ADJ_TOL));
                if (!hp && !fb) continue;

                p.Type = PartType.GirderEnd;
                p.Code = hp ? "GE-HP" : "GE-FB";
            }
        }

        /// <summary>
        /// HPmaxspan tự tính (KHÔNG cần user nhập): khoảng cách LỚN NHẤT giữa các đường HP song song liền kề,
        /// chỉ xét nhóm phương có ≥3 đường (bỏ cặp biên 2-HP). Dùng cho GB (giai đoạn sau). 0 nếu không đủ dữ liệu.
        /// </summary>
        public static double HpMaxSpan(List<PanelPart> hpParts)
        {
            double Max(IEnumerable<double> centers)
            {
                var lines = new List<double>();
                foreach (var c in centers.OrderBy(x => x))
                    if (lines.Count == 0 || Math.Abs(c - lines[lines.Count - 1]) > 50) lines.Add(c);
                if (lines.Count < 3) return 0;              // <3 đường → không đủ để có "span" (bỏ biên)
                double m = 0;
                for (int i = 1; i < lines.Count; i++) m = Math.Max(m, lines[i] - lines[i - 1]);
                return m;
            }
            var longHp = hpParts.Where(h => h.MemberLength > ClassifyUtil.HP_MIN_LEN).ToList();
            double h = Max(longHp.Where(x => x.IsHorizontal).Select(x => x.Centroid.Y));
            double v = Max(longHp.Where(x => !x.IsHorizontal).Select(x => x.Centroid.X));
            return Math.Max(h, v);
        }
    }
}
