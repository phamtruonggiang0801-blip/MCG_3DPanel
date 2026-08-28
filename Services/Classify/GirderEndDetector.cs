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
        /// "1 khoảng HP" — quy tắc kỹ sư (2026-08-27):
        ///  1. = khoảng cách LỚN NHẤT giữa 2 đường HP **liền kề**.
        ///  2. Nếu GIỮA 2 đường HP đó có 1 **Web** (girder cùng phương, chạy ngang qua) → 2 HP đó KHÔNG
        ///     tính là "liền kề", BỎ khoảng đó.
        ///  3. Panel có cả HP dọc lẫn ngang → lấy khoảng của phương có **NHIỀU đường HP hơn**.
        ///
        /// HP CHÉO (<see cref="ClassifyUtil.IsDiagonalBloatedBbox"/>) bị loại — thanh viền/vát góc, không
        /// thuộc field. 7D-17C: phương ngang 33 đường ≫ đứng 3 → dùng ngang; các gap lớn 826/867/970
        /// đều có 1 girder Web ngang ở giữa (Y≈16206/27243/28181) → bỏ; còn lại đều ~775 → **hpSpan = 775**.
        /// (Trước đây trả 5372 = khoảng cách giữa 2 HP bulb CHÉO ở khúc cong panel, bị `IsHorizontal`
        /// xếp nhầm thành "2 đường HP đứng liền kề".)
        ///
        /// Dùng cho Sniped End (chặn vát mẩu FB ngắn) + GB (giai đoạn sau). 0 nếu không đủ dữ liệu.
        /// </summary>
        public static double HpMaxSpan(ClassificationResult cls)
        {
            var hp = cls.OfType(PartType.HP)
                        .Where(h => h.MemberLength > ClassifyUtil.HP_MIN_LEN
                                 && !ClassifyUtil.IsDiagonalBloatedBbox(h)).ToList();
            var web = cls.OfType(PartType.Web)
                         .Where(w => w.MemberLength > ClassifyUtil.WEB_MIN_LEN).ToList();

            List<double> Lines(IEnumerable<double> cs)
            {
                var r = new List<double>();
                foreach (var c in cs.OrderBy(x => x))
                    if (r.Count == 0 || Math.Abs(c - r[r.Count - 1]) > 50) r.Add(c);
                return r;
            }

            var hpH = Lines(hp.Where(x => x.IsHorizontal).Select(x => x.Centroid.Y));
            var hpV = Lines(hp.Where(x => !x.IsHorizontal).Select(x => x.Centroid.X));

            // Phương có NHIỀU đường HP hơn thắng.
            List<double> hpLines, webLines;
            if (hpH.Count >= hpV.Count)
            {
                hpLines = hpH;
                webLines = Lines(web.Where(w => w.IsHorizontal).Select(w => w.Centroid.Y));
            }
            else
            {
                hpLines = hpV;
                webLines = Lines(web.Where(w => !w.IsHorizontal).Select(w => w.Centroid.X));
            }
            if (hpLines.Count < 2) return 0;

            const double EPS = 30.0;
            double best = 0;
            for (int i = 1; i < hpLines.Count; i++)
            {
                double lo = hpLines[i - 1], hi = hpLines[i];
                bool webBetween = webLines.Any(w => w > lo + EPS && w < hi - EPS);
                if (!webBetween && hi - lo > best) best = hi - lo;
            }
            return best;
        }
    }
}
