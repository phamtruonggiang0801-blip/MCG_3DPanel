using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCG_3DPanel.Models;
using MCG_3DPanel.Services.Corrections;
using MCG_3DPanel.UI;

namespace MCG_3DPanel.Services.Classify
{
    /// <summary>
    /// BƯỚC 3 — Gán MÃ bracket (OB/IB/FB + hậu tố dày) và đóng dấu hướng BỤNG lên từng HP.
    ///
    /// - Đầu tự do bracket chạm **Flat Bar** → `BF` (BracketFlatbar); chạm **HP** → `OB` (lưng) / `IB` (bụng).
    /// - Hướng bụng HP (<see cref="BellyDir"/>): HP **ngoài cùng** → bụng RA NGOÀI (override mọi panel type);
    ///   HP **bên trong** → theo **CHIỀU ĐỔ** (HP đứng: +X; HP ngang: Center về giữa / Port −Y / Starboard +Y).
    /// - Kiểm chứng panel thật: Center → 25 OB / 0 IB; Port → 24 OB / 1 IB.
    /// Xem skill bracket-identification (mục 4, 4b).
    /// </summary>
    internal static class BracketCoder
    {
        /// <summary>Bậc chiều dày → hậu tố mã (OB/OB1/OB2…).</summary>
        private static readonly Dictionary<int, string> ThicknessSuffix = new Dictionary<int, string>
        {
            { 6, "" }, { 10, "1" }, { 15, "2" }, { 20, "3" },
        };

        /// <param name="corrections">Correction tab Result theo SourceKey: HP FlipDir → đảo bụng (TRƯỚC khi tính
        /// OB/IB, nên bracket lân cận theo hướng mới); Bracket Kind OB/IB/BF → thay mã gốc (giữ hậu tố dày),
        /// OB↔IB tự đảo hướng gấp; Bracket FlipDir → đảo hướng gấp thêm 1 lần (kỹ sư chốt 2026-11-04).</param>
        public static void Apply(ClassificationResult result, List<PanelPart> hpParts,
            List<PanelPart> fbParts, PanelPosition position, Point3d center,
            IReadOnlyDictionary<string, PartCorrection> corrections = null)
        {
            PartCorrection Corr(PanelPart p) =>
                corrections != null && p.SourceKey != null && corrections.TryGetValue(p.SourceKey, out var c) ? c : null;

            // HP đủ dài mới xét; lấy biên NGOÀI CÙNG cho quy tắc "HP ngoài cùng đổ ra ngoài".
            var hpLong = hpParts.Where(h => h.MemberLength > ClassifyUtil.HP_MIN_LEN).ToList();
            var vert = hpLong.Where(h => !h.IsHorizontal).ToList();
            var horz = hpLong.Where(h => h.IsHorizontal).ToList();
            double vMinX = vert.Count > 0 ? vert.Min(h => h.Centroid.X) : double.NaN;
            double vMaxX = vert.Count > 0 ? vert.Max(h => h.Centroid.X) : double.NaN;
            double hMinY = horz.Count > 0 ? horz.Min(h => h.Centroid.Y) : double.NaN;
            double hMaxY = horz.Count > 0 ? horz.Max(h => h.Centroid.Y) : double.NaN;

            // Đóng dấu hướng BỤNG lên **MỌI** HP (HpBuilder dùng để xoay tiết diện bulb).
            // ⚠️ KHÔNG lọc theo HP_MIN_LEN ở đây: các đoạn HP viền/vát góc chỉ dài ~300 nhưng LÀ HP thật
            // (bbox trông "dày" 20+ vì nằm CHÉO). Lọc ở đây từng làm chúng thiếu belly → rơi về hộp chữ nhật.
            // HP_MIN_LEN chỉ dùng cho biên ngoài cùng (vMinX…) và ghép bracket.
            Point3d[] topVerts = result.TopPlate?.Vertices;
            foreach (var h in hpParts)
            {
                h.BellyDir = BellyDir(h, position, center, vMinX, vMaxX, hMinY, hMaxY, topVerts);
                if (Corr(h)?.FlipDir == true)
                {
                    h.BellyDir = -h.BellyDir;
                    result.Notes.Add($"[Correction] {h.SourceKey}: đảo bụng HP.");
                }
            }

            var webs = result.OfType(PartType.Web)
                             .Where(w => w.MemberLength > ClassifyUtil.WEB_MIN_LEN).ToList();

            foreach (var brk in result.OfType(PartType.Bracket))
            {
                // Xét ĐẦU TỰ DO (đầu xa Web) — đó mới là đầu ốp vào HP/Flat Bar.
                Point3d free = FreeEnd(brk, webs);
                var fb = NearestPart(fbParts, free, ClassifyUtil.ADJ_TOL);
                var hp = NearestPart(hpLong, free, ClassifyUtil.ADJ_TOL);

                string baseCode;
                if (fb != null && (hp == null ||
                        ClassifyUtil.DistToBox(free, fb.Bounds) <= ClassifyUtil.DistToBox(free, hp.Bounds)))
                    baseCode = "BF";                      // Bracket trên Flat Bar (trước đây ghi "FB")
                else if (hp != null)
                    baseCode = ObOrIb(brk, hp);
                else
                    baseCode = "OB";

                var corr = Corr(brk);
                string want = null;
                switch (ResultCatalog.ParseIntent(corr?.Kind))
                {
                    case CorrectionIntent.BracketOB: want = "OB"; break;
                    case CorrectionIntent.BracketIB: want = "IB"; break;
                    case CorrectionIntent.BracketBF: want = "BF"; break;
                }
                if (want != null && want != baseCode)
                {
                    bool swapObIb = (baseCode == "OB" && want == "IB") || (baseCode == "IB" && want == "OB");
                    if (swapObIb) brk.FlipFold = !brk.FlipFold;
                    result.Notes.Add($"[Correction] {brk.SourceKey}: bracket {baseCode} → {want}" + (swapObIb ? " (đảo hướng gấp)." : "."));
                    baseCode = want;
                }
                if (corr?.FlipDir == true)
                {
                    brk.FlipFold = !brk.FlipFold;
                    result.Notes.Add($"[Correction] {brk.SourceKey}: đảo hướng gấp bracket.");
                }

                brk.Code = baseCode + SuffixFor(brk.FootprintThickness);
            }
        }

        /// <summary>Đầu TỰ DO của bracket = đầu XA Web (đầu kia là anchor chạm Web).</summary>
        private static Point3d FreeEnd(PanelPart brk, List<PanelPart> webs)
        {
            Extents3d b = brk.Bounds;
            Point3d e1, e2;
            if (brk.IsHorizontal)
            {
                e1 = new Point3d(b.MinPoint.X, brk.Centroid.Y, 0);
                e2 = new Point3d(b.MaxPoint.X, brk.Centroid.Y, 0);
            }
            else
            {
                e1 = new Point3d(brk.Centroid.X, b.MinPoint.Y, 0);
                e2 = new Point3d(brk.Centroid.X, b.MaxPoint.Y, 0);
            }
            if (webs.Count == 0) return e2;
            double d1 = webs.Min(w => ClassifyUtil.DistToBox(e1, w.Bounds));
            double d2 = webs.Min(w => ClassifyUtil.DistToBox(e2, w.Bounds));
            return d1 <= d2 ? e2 : e1;
        }

        private static PanelPart NearestPart(List<PanelPart> parts, Point3d p, double tol)
        {
            PanelPart best = null; double bd = double.MaxValue;
            foreach (var m in parts)
            {
                double d = ClassifyUtil.DistToBox(p, m.Bounds);
                if (d < bd) { bd = d; best = m; }
            }
            return (best != null && bd <= tol) ? best : null;
        }

        /// <summary>
        /// OB nếu bracket ở phía LƯNG HP, IB nếu phía BỤNG (bulb).
        /// Dùng ĐỈNH bracket XA tâm HP nhất theo trục bụng (thành phần vuông góc rõ nhất) — bền hơn centroid
        /// với bracket NGẮN (centroid gần trục HP làm dấu bị nhiễu, từng khiến 2 OB bị chấm nhầm IB).
        /// </summary>
        private static string ObOrIb(PanelPart brk, PanelPart hp)
        {
            // Dùng lại BellyDir ĐÃ đóng dấu ở Apply (cùng 1 giá trị cho mọi nơi cần bụng HP này) —
            // tránh tính lại thiếu tham số (VD biên Top Plate thật) gây lệch so với hp.BellyDir gốc.
            Vector3d belly = hp.BellyDir;

            double signed;
            if (brk.Vertices != null && brk.Vertices.Length > 0)
            {
                signed = 0.0;
                foreach (var v in brk.Vertices)
                {
                    double s = (v.X - hp.Centroid.X) * belly.X + (v.Y - hp.Centroid.Y) * belly.Y;
                    if (Math.Abs(s) > Math.Abs(signed)) signed = s;   // đỉnh xa nhất theo trục bụng
                }
            }
            else
            {
                Vector3d d = brk.Centroid - hp.Centroid;
                signed = d.X * belly.X + d.Y * belly.Y;
            }
            return signed >= 0 ? "IB" : "OB";
        }

        /// <summary>
        /// Hướng BỤNG (bulb) của 1 HP — luôn **VUÔNG GÓC với trục thật** (`AxisDir`), nên đúng cả HP **CHÉO**.
        /// Chọn dấu của pháp tuyến:
        ///  - HP **NGOÀI CÙNG viền theo mép Top Plate** (kể cả CHÉO lẫn song song trục — xác nhận kỹ sư
        ///    2026-08-26) ⇒ bụng **RA NGOÀI**, override mọi panel type.
        ///  - HP **bên trong** ⇒ theo **CHIỀU ĐỔ** `(+X, dozY)` chiếu lên pháp tuyến;
        ///    dozY: Center → về giữa · Port → −Y · Starboard → +Y.
        ///
        /// ⚠️ "Ra ngoài" tính bằng PHÁP TUYẾN CỤC BỘ của biên thật Top Plate tại điểm gần nhất
        /// (<see cref="ClassifyUtil.LocalOutwardNormal"/>), KHÔNG dùng "xa tâm panel" (centroid−center):
        /// xấp xỉ tâm chỉ đúng khi biên LỒI đơn giản — tại góc LÕM/khấc (notch cắt vào tấm), "xa tâm panel"
        /// vẫn trỏ ra ngoài panel nói chung nhưng có thể trỏ VÀO đúng vật liệu thật tại chỗ khấc bẻ ngược
        /// biên cục bộ. `LocalOutwardNormal` không có giả định đó — luôn đúng bất kể biên lồi/lõm, áp dụng
        /// cho CẢ HP chéo lẫn song song trục (trước đây chỉ HP song song trục mới xét biên thật; HP chéo
        /// luôn coi "ngoài cùng" rồi lấy hướng theo tâm panel — vẫn sai tại khấc). "Ngoài cùng" xác định
        /// bằng khoảng cách tới biên thật (<see cref="ClassifyUtil.DistToPolygonBoundary"/> ≤ `EDGE_POLY_TOL`)
        /// khi có Top Plate; nếu không có, dùng fallback `vMinX/vMaxX/hMinY/hMaxY` (vị trí HP dài khác) như
        /// cũ — chỉ còn là fallback hiếm khi xảy ra (panel luôn có Top Plate).
        /// </summary>
        public static Vector3d BellyDir(PanelPart hp, PanelPosition position, Point3d center,
            double vMinX, double vMaxX, double hMinY, double hMaxY, Point3d[] topPlateVerts = null)
        {
            Vector3d axis = hp.AxisDir;
            if (axis.Length < 1e-9) axis = hp.IsHorizontal ? Vector3d.XAxis : Vector3d.YAxis;
            axis = new Vector3d(axis.X, axis.Y, 0).GetNormal();

            var perp = new Vector3d(-axis.Y, axis.X, 0);          // pháp tuyến (1 trong 2 chiều)
            if (perp.Length < 1e-9) return new Vector3d(1, 0, 0);
            perp = perp.GetNormal();

            bool axisAligned = Math.Abs(axis.X) < 1e-3 || Math.Abs(axis.Y) < 1e-3;
            bool haveTop = topPlateVerts != null && topPlateVerts.Length >= 3;

            // ⚠️ HP CHÉO = thanh viền/vát góc → LUÔN coi ngoài cùng (quy tắc gốc, KHÔNG gate bằng khoảng
            // cách biên thật) — bản sửa 2026-08-26 từng gate cả HP chéo qua EDGE_POLY_TOL, gây regression
            // thật: `393A0` (chéo, cách biên thật 172.9mm — VỪA quá EDGE_POLY_TOL=150) bị loại khỏi nhánh
            // "ngoài cùng", rơi về chiều-đổ (sai). Distance-to-boundary chỉ cần thiết để xác định "ngoài
            // cùng" cho HP SONG SONG TRỤC (không có quy ước "chéo=viền" sẵn); HP chéo giữ quy tắc gốc.
            bool edge;
            if (!axisAligned) edge = true;
            else if (haveTop)
                edge = ClassifyUtil.DistToPolygonBoundary(hp.Centroid, topPlateVerts) <= ClassifyUtil.EDGE_POLY_TOL;
            else if (!hp.IsHorizontal)
                edge = Math.Abs(hp.Centroid.X - vMinX) < ClassifyUtil.EDGE_TOL
                    || Math.Abs(hp.Centroid.X - vMaxX) < ClassifyUtil.EDGE_TOL;
            else
                edge = Math.Abs(hp.Centroid.Y - hMinY) < ClassifyUtil.EDGE_TOL
                    || Math.Abs(hp.Centroid.Y - hMaxY) < ClassifyUtil.EDGE_TOL;

            if (edge)
            {
                Vector3d outward = haveTop
                    ? ClassifyUtil.LocalOutwardNormal(hp.Centroid, topPlateVerts)
                    : hp.Centroid - center;                        // fallback cũ (xa tâm panel)
                if (outward.Length < 1e-9) outward = hp.Centroid - center;
                return (outward.X * perp.X + outward.Y * perp.Y) >= 0 ? perp : perp.Negate();
            }

            double dozY = position == PanelPosition.Port ? -1.0
                        : position == PanelPosition.Starboard ? 1.0
                        : (hp.Centroid.Y <= center.Y ? 1.0 : -1.0);
            var doz = new Vector3d(1.0, dozY, 0);                 // chiều đổ: X luôn +1
            return (doz.X * perp.X + doz.Y * perp.Y) >= 0 ? perp : perp.Negate();
        }

        private static string SuffixFor(double thickness)
        {
            int t = (int)Math.Round(thickness);
            return ThicknessSuffix.TryGetValue(t, out string s) ? s : "?";
        }
    }
}
