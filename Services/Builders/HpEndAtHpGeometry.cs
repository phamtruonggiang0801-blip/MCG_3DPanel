using System;
using System.Collections.Generic;

namespace MCG_3DPanel.Services.Builders
{
    /// <summary>
    /// Toán THUẦN (không AutoCAD — để unit test được) cho quy cách <b>"HP TO HP END"</b> (CAS.dwt →
    /// CAS.dump.json, xác nhận kỹ sư 2026-11-04): đầu 1 HP (<i>HP đầu</i>) kết thúc chạm THÂN 1 HP khác
    /// (<i>HP chạy</i>). HP đầu hàn vào mặt web HP chạy phần trên; phần dưới cắt lùi <c>p + R</c> với cung
    /// <c>R = 30</c> thoát bulb của HP chạy.
    ///
    /// Hệ x'–y = MẶT CẮT HP CHẠY: <c>x'</c> = pháp tuyến từ mặt tiếp xúc (mặt web HP chạy phía HP đầu)
    /// hướng VÀO HP đầu; <c>y</c> = Z thế giới (0 = mặt dưới Top Plate, −b = đáy HP).
    /// Xem skill hp-end-at-hp · spec docs/superpowers/specs/2026-11-04-hp-end-at-hp-design.md.
    /// </summary>
    public static class HpEndAtHpGeometry
    {
        /// <summary>Bán kính cung thoát bulb — CỐ ĐỊNH mọi cỡ HP (kỹ sư 2026-11-04).</summary>
        public const double R = 30.0;
        /// <summary>Margin dao cắt (tránh mặt trùng khít khi BoolSubtract).</summary>
        public const double MARGIN = 2.0;
        /// <summary>Margin thêm 2 đầu extrude dọc trục HP chạy.</summary>
        public const double SPAN_MARGIN = 10.0;

        private static readonly double Sqrt3 = Math.Sqrt(3.0);
        private static readonly double ArcBulge = -Math.Tan(Math.PI / 8.0);   // cung 90° chiều kim đồng hồ

        /// <summary>Chiều cao đoạn bulb <c>d = b − dx</c>, <c>dx = b − r − (3r − t + c)/√3</c> (skill hp-profile).</summary>
        public static double BulbHeight(double b, double t, double c, double r)
            => b - (b - r - (3.0 * r - t + c) / Sqrt3);

        /// <summary>Phần bulb nhô khỏi mặt web <c>p = c + r − t</c> (bulb rộng nhất c+r tính từ LƯNG).</summary>
        public static double BulbProtrusion(double c, double r, double t) => c + r - t;

        /// <summary>
        /// Biên dạng dao cắt kín (x', y, bulge) trong mặt cắt HP chạy — phủ (1) phần HP đầu vẽ lấn qua mặt
        /// tiếp xúc (x' &lt; 0, rộng <c>W = tRun + c + r + MARGIN</c>) và (2) vùng thoát bulb: từ
        /// <c>y = −(b−d−R)</c> ra <c>x' = p</c>, cung R tâm <c>(p, −(b−d))</c>, rồi đứng <c>x' = p+R</c> xuống đáy.
        /// <paramref name="inside"/> = bụng HP chạy quay về HP đầu (p = c+r−t); ngược lại p = 0.
        /// null nếu tham số vô lý hoặc không còn mặt hàn (<c>b − d − R ≤ 0</c>).
        /// </summary>
        public static List<(double X, double Y, double Bulge)> CutterOutline(double b, double tRun, double c, double r, bool inside)
        {
            if (b <= 0 || tRun <= 0 || c <= 0 || r <= 0 || c <= tRun) return null;
            double d = BulbHeight(b, tRun, c, r);
            if (d <= 0 || b - d - R <= 0) return null;

            double p = inside ? BulbProtrusion(c, r, tRun) : 0.0;
            if (p < 0) return null;
            double w = tRun + c + r + MARGIN;
            double yW = -(b - d - R), yC = -(b - d), yB = -b - MARGIN;

            var o = new List<(double X, double Y, double Bulge)>
            {
                (-w, MARGIN, 0), (0, MARGIN, 0),
            };
            if (p > 1e-9) { o.Add((0, yW, 0)); o.Add((p, yW, ArcBulge)); }
            else o.Add((0, yW, ArcBulge));                       // OUTSIDE: đoạn p dài 0 → bỏ đỉnh trùng
            o.Add((p + R, yC, 0));
            o.Add((p + R, yB, 0));
            o.Add((-w, yB, 0));
            return o;
        }

        /// <summary>
        /// Nửa chiều dài extrude dao cắt dọc trục HP chạy (tâm = chân vuông góc đầu HP đầu): phủ hết bề
        /// rộng HP đầu kể cả bulb (<c>(c+r+tEnd)/sinθ</c>) + độ trượt dọc trục của vùng cắt khi xiên
        /// (<c>(p+R+W)·|cotθ|</c>) + <see cref="SPAN_MARGIN"/>. θ = góc giữa 2 trục (cosθ = |axis_h·axis_m|).
        /// </summary>
        public static double HalfSpan(double tEnd, double tRun, double c, double r, bool inside, double cosTheta)
        {
            double cos = Math.Min(1.0, Math.Abs(cosTheta));
            double sin = Math.Sqrt(Math.Max(1e-12, 1.0 - cos * cos));
            double p = inside ? BulbProtrusion(c, r, tRun) : 0.0;
            double w = tRun + c + r + MARGIN;
            return (c + r + tEnd) / sin + (p + R + w) * cos / sin + SPAN_MARGIN;
        }

        /// <summary>Dung sai chạm thêm ngoài mặt web (+ p phía bụng — người vẽ có thể dừng HP đầu ở mép bulb).</summary>
        public const double TOL_TOUCH = 10.0;
        /// <summary>HP chạy phải THÒ qua MẶT WEB HP đầu (±t/2, không tính bulb) ≥ giá trị này ở CẢ 2 phía
        /// dọc trục — dưới mức này là góc chữ L / mối nối (kỹ sư 2026-11-04: thò 15mm vẫn là HP end tại HP).</summary>
        public const double OVERHANG_MIN = 5.0;
        /// <summary>Đầu trùng đầu mút HP khác ≤ 25mm = mối nối đầu-đầu (= HpEndBracketBuilder.HP_JOINT_TOL) —
        /// TRỪ KHI HP chạy vẫn thò qua mặt web HP đầu ≥ <see cref="OVERHANG_MIN"/> ở đầu đó (chữ T sát đầu).</summary>
        public const double JOINT_TOL = 25.0;
        /// <summary>Ngưỡng góc: |axis_h·axis_m| ≤ 0.70 (θ ≥ ~45°, cùng ngưỡng "End 30").</summary>
        public const double MAX_COS = 0.70;

        /// <summary>
        /// Xét đầu mút <paramref name="end"/> của HP đầu (đầu kia <paramref name="otherEnd"/>) có chạm THÂN HP
        /// chạy (đường tâm <paramref name="runA"/>→<paramref name="runB"/>, dày <paramref name="tRun"/>, hướng
        /// bụng <paramref name="runBelly"/>) không. Thứ tự kiểm (dừng ở lý do đầu tiên): degenerate → joint →
        /// noBelly → far → nearRunEnd → angle → Ok. Phía HP đầu lấy theo <paramref name="otherEnd"/> nên đầu mút
        /// vẽ lấn qua đường tâm vẫn đúng phía. <paramref name="tEnd"/> = dày web HP đầu (≤ 0 ⇒ = tRun).
        /// "Thò" (overhang) = phần HP chạy vượt quá MẶT WEB HP đầu (±tEnd/2/sinθ dọc trục HP chạy, KHÔNG tính
        /// bulb — kỹ sư 2026-11-04) tới mỗi đầu mút HP chạy.
        /// </summary>
        public static HpEndMatch Match((double X, double Y) end, (double X, double Y) otherEnd,
                                       (double X, double Y) runA, (double X, double Y) runB,
                                       double tRun, (double X, double Y) runBelly, double c, double r,
                                       double tEnd = 0)
        {
            var m = new HpEndMatch();
            double ax = runB.X - runA.X, ay = runB.Y - runA.Y, len = Math.Sqrt(ax * ax + ay * ay);
            double hx = otherEnd.X - end.X, hy = otherEnd.Y - end.Y, hLen = Math.Sqrt(hx * hx + hy * hy);
            if (len < 1e-6 || hLen < 1e-6 || tRun <= 0) return m.Fail("degenerate");
            ax /= len; ay /= len; hx /= hLen; hy /= hLen;
            m.RunAxis = (ax, ay);

            double s = (end.X - runA.X) * ax + (end.Y - runA.Y) * ay;          // dọc trục từ runA
            m.Foot = (runA.X + ax * s, runA.Y + ay * s);
            double sc = Math.Max(0, Math.Min(len, s));
            m.Dist = Dist(end, (runA.X + ax * sc, runA.Y + ay * sc));

            (double X, double Y) perp = (-ay, ax);
            double side = (otherEnd.X - m.Foot.X) * perp.X + (otherEnd.Y - m.Foot.Y) * perp.Y;
            m.Normal = side >= 0 ? perp : (-perp.X, -perp.Y);
            m.Gap = (end.X - m.Foot.X) * m.Normal.X + (end.Y - m.Foot.Y) * m.Normal.Y - tRun / 2.0;
            m.CosTheta = Math.Abs(hx * ax + hy * ay);

            // 2 mặt web HP đầu chiếu lên trục HP chạy (±t/2/sinθ) → phần HP chạy THÒ qua mỗi phía.
            double sinT = Math.Sqrt(Math.Max(1e-12, 1.0 - m.CosTheta * m.CosTheta));
            double halfW = (tEnd > 0 ? tEnd : tRun) / 2.0 / sinT;
            double ovA = s - halfW, ovB = len - (s + halfW);

            bool nearA = Dist(end, runA) <= JOINT_TOL, nearB = Dist(end, runB) <= JOINT_TOL;
            if ((nearA && (ovA < OVERHANG_MIN || m.CosTheta > MAX_COS)) ||
                (nearB && (ovB < OVERHANG_MIN || m.CosTheta > MAX_COS))) return m.Fail("joint");

            double bLen = Math.Sqrt(runBelly.X * runBelly.X + runBelly.Y * runBelly.Y);
            if (bLen < 1e-9) return m.Fail("noBelly");
            m.Inside = runBelly.X * m.Normal.X + runBelly.Y * m.Normal.Y > 0;

            double p = m.Inside ? BulbProtrusion(c, r, tRun) : 0.0;
            if (m.Dist > tRun / 2.0 + p + TOL_TOUCH) return m.Fail("far");
            // HP chạy phải thò qua CẢ 2 mặt web HP đầu ≥ OVERHANG_MIN — loại góc chữ L (chạy dừng ngang mặt web).
            if (ovA < OVERHANG_MIN || ovB < OVERHANG_MIN) return m.Fail("nearRunEnd");
            if (m.CosTheta > MAX_COS) return m.Fail("angle");
            m.Ok = true;
            return m;
        }

        /// <summary>
        /// Chiều dài NỐI DÀI HP đầu dọc trục chính nó (từ đầu mút về phía HP chạy) khi vẽ dừng hụt
        /// (<paramref name="gap"/> &gt; 0.5): đưa đường tâm tới mặt tiếp xúc (<c>gap/sinθ</c>) + góc xa của mặt đầu
        /// khi xiên (<c>(tEnd/2)·|cotθ|</c>) + <see cref="MARGIN"/> (phần thừa lấn qua mặt tiếp xúc do dao cắt xén).
        /// 0 nếu không hụt.
        /// </summary>
        public static double FillLength(double gap, double tEnd, double cosTheta)
        {
            if (gap <= 0.5) return 0.0;
            double cos = Math.Min(1.0, Math.Abs(cosTheta));
            double sin = Math.Sqrt(Math.Max(1e-12, 1.0 - cos * cos));
            return gap / sin + (tEnd / 2.0) * cos / sin + MARGIN;
        }

        private static double Dist((double X, double Y) a, (double X, double Y) b)
            => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    }

    /// <summary>Kết quả <see cref="HpEndAtHpGeometry.Match"/>.</summary>
    public sealed class HpEndMatch
    {
        public bool Ok { get; set; }
        /// <summary>null | "degenerate" | "joint" | "noBelly" | "far" | "nearRunEnd" | "angle".</summary>
        public string Reject { get; set; }
        /// <summary>Bụng (bulb) HP chạy quay về phía HP đầu.</summary>
        public bool Inside { get; set; }
        /// <summary>|axis_h · axis_m|.</summary>
        public double CosTheta { get; set; }
        /// <summary>Khoảng cách đầu mút tới đoạn tâm HP chạy.</summary>
        public double Dist { get; set; }
        /// <summary>Chân vuông góc của đầu mút trên ĐƯỜNG TÂM HP chạy.</summary>
        public (double X, double Y) Foot { get; set; }
        /// <summary>Pháp tuyến đơn vị ⟂ trục HP chạy, hướng về phía HP đầu.</summary>
        public (double X, double Y) Normal { get; set; }
        /// <summary>Trục đơn vị runA→runB.</summary>
        public (double X, double Y) RunAxis { get; set; }
        /// <summary>Khoảng hở (có dấu) từ mặt tiếp xúc tới đầu mút, dọc <see cref="Normal"/>: &gt; 0 = vẽ DỪNG HỤT
        /// (cần nối dài HP đầu tới mặt web), &lt; 0 = vẽ LẤN qua (dao cắt xén).</summary>
        public double Gap { get; set; }

        internal HpEndMatch Fail(string why) { Ok = false; Reject = why; return this; }
    }
}
