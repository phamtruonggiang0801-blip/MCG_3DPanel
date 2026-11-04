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
    }
}
