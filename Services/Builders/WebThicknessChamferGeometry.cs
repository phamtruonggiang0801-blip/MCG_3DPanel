using System;
using System.Collections.Generic;

namespace MCG_3DPanel.Services.Builders
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests để unit test).

    /// <summary>1 web trong mặt bằng: đường tâm A→B + chiều dày.</summary>
    public struct WebSeg
    {
        public double Ax, Ay, Bx, By, T;
    }

    /// <summary>Mối nối 2 web thẳng hàng khác chiều dày. <c>U</c> = đơn vị từ mối nối VÀO tấm dày;
    /// <c>N</c> = (−Uy, Ux). <c>StepPlus</c>/<c>StepMinus</c> = bậc lệch ở mặt +N / −N của tấm dày (0 = flush).</summary>
    public sealed class ChamferJoint
    {
        public int Thick, Thin;          // chỉ số trong cặp (0 = a, 1 = b)
        public double Jx, Jy;            // điểm mối nối trên đường tâm tấm dày
        public double Ux, Uy, Nx, Ny;
        public double StepPlus, StepMinus, Dt, L;
        public double HalfThick;         // nửa chiều dày tấm dày
    }

    /// <summary>
    /// Vát <b>1:4</b> mối nối web khác chiều dày (CAS.dwt / CAS.dump.json, kỹ sư 2026-11-05): tấm DÀY vát trên mặt bị
    /// lệch bậc, dài L = 4 × Δt từ mối nối, từ đúng mặt tấm mỏng về mặt tấm dày; mặt flush không cắt; 2 tấm thẳng tâm
    /// ⇒ vát cả 2 mặt (mỗi mặt Δt/2). Chỉ khi Δt ≥ <see cref="MIN_DT"/>. Xem skill web-thickness-chamfer.
    /// </summary>
    public static class WebThicknessChamferGeometry
    {
        public const double MIN_DT = 4.0;      // chênh dày tối thiểu (mm) — dưới đó không vát
        public const double RATIO = 4.0;       // L = RATIO × Δt (vát 1:4)
        public const double END_TOL = 5.0;     // khe / chồng tối đa giữa 2 đầu web (mm)
        public const double FLUSH_TOL = 0.5;   // bậc ≤ giá trị này ⇒ mặt flush
        public const double PAR_COS = 0.999;   // thẳng hàng

        /// <summary>Mối nối giữa <paramref name="a"/> và <paramref name="b"/>, hoặc null nếu không phải (không thẳng
        /// hàng, không nối đầu–đầu, lệch ngang quá, Δt &lt; <see cref="MIN_DT"/>).</summary>
        public static ChamferJoint Match(WebSeg a, WebSeg b)
        {
            if (!Dir(a, out double ax, out double ay) || !Dir(b, out double bx, out double by)) return null;
            if (Math.Abs(ax * bx + ay * by) < PAR_COS) return null;
            double dt = Math.Abs(a.T - b.T);
            if (dt < MIN_DT - 1e-9) return null;

            // Cặp đầu mút: đầu của a (ea, "ngoài" = ra khỏi a) gặp đầu của b, và b nằm HOÀN TOÀN phía ngoài đó.
            ChamferJoint best = null;
            double bestGap = double.MaxValue;
            foreach (bool aAtB in new[] { false, true })
                foreach (bool bAtB in new[] { false, true })
                {
                    double eax = aAtB ? a.Bx : a.Ax, eay = aAtB ? a.By : a.Ay;      // đầu a tại mối nối
                    double oax = aAtB ? a.Ax : a.Bx, oay = aAtB ? a.Ay : a.By;      // đầu kia của a
                    double ebx = bAtB ? b.Bx : b.Ax, eby = bAtB ? b.By : b.Ay;      // đầu b tại mối nối
                    double obx = bAtB ? b.Ax : b.Bx, oby = bAtB ? b.Ay : b.By;      // đầu kia của b
                    double la = Math.Sqrt((eax - oax) * (eax - oax) + (eay - oay) * (eay - oay));
                    double outX = (eax - oax) / la, outY = (eay - oay) / la;        // hướng RA KHỎI a
                    double gap = (ebx - eax) * outX + (eby - eay) * outY;           // + = khe, − = chồng
                    double far = (obx - eax) * outX + (oby - eay) * outY;           // đầu kia của b phải ở ngoài
                    double lat = Math.Abs(-(ebx - eax) * outY + (eby - eay) * outX);
                    if (Math.Abs(gap) > END_TOL || far <= END_TOL) continue;
                    if (lat > (a.T + b.T) / 2.0) continue;
                    if (Math.Abs(gap) >= bestGap) continue;

                    bool aThick = a.T > b.T;
                    var j = new ChamferJoint { Thick = aThick ? 0 : 1, Thin = aThick ? 1 : 0, Dt = dt, L = RATIO * dt };
                    // Điểm mối nối + U trên tấm DÀY.
                    if (aThick) { j.Jx = eax; j.Jy = eay; j.Ux = -outX; j.Uy = -outY; }
                    else
                    {
                        double lb = Math.Sqrt((ebx - obx) * (ebx - obx) + (eby - oby) * (eby - oby));
                        j.Jx = ebx; j.Jy = eby; j.Ux = (obx - ebx) / lb; j.Uy = (oby - eby) / lb;
                    }
                    j.Nx = -j.Uy; j.Ny = j.Ux;
                    WebSeg thick = aThick ? a : b, thin = aThick ? b : a;
                    // Lệch tâm tấm mỏng so với đường tâm tấm dày, đo theo N TẠI ĐẦU MỐI NỐI của tấm mỏng (trung điểm sai
                    // vài mm khi tấm mỏng dài vẽ lệch trục chút ít — review 2026-11-05).
                    double tx = aThick ? ebx : eax, ty = aThick ? eby : eay;
                    double mx = tx - j.Jx, my = ty - j.Jy;
                    double d = mx * j.Nx + my * j.Ny;
                    double h = thick.T / 2.0, hn = thin.T / 2.0;
                    j.HalfThick = h;
                    double sp = h - (d + hn), sm = (d - hn) + h;
                    // Mặt tấm mỏng phải nằm TRONG dải chiều dày tấm dày (± FLUSH_TOL); lệch ra ngoài = 2 tấm chỉ chạm mặt,
                    // không phải mối nối đối đầu (trước đó vát tấm dày tới mép dao — review 2026-11-05).
                    if (sp < -FLUSH_TOL || sm < -FLUSH_TOL) continue;
                    j.StepPlus = Clean(sp);
                    j.StepMinus = Clean(sm);
                    bestGap = Math.Abs(gap);   // chỉ khi ứng viên ĐƯỢC NHẬN
                    best = j;
                }
            return best;
        }

        /// <summary>Đa giác mặt bằng (4 đỉnh, WCS) của khối nêm cắt trên mặt <paramref name="side"/> (+1 = +N, −1 = −N):
        /// trong hệ (U, side·N): (−m, h−s−k·m) → (L+m, h+k·m) → (L+m, h+m+k·m) → (−m, h+m+k·m); h = nửa dày tấm dày, s = bậc mặt đó,
        /// k = s/L. Cạnh vát đi qua (0, h−s) = mặt tấm mỏng tại mối nối và (L, h) = mặt tấm dày; m = đệm ra NGOÀI để
        /// không trùng mặt (boolean ổn định).</summary>
        public static List<(double X, double Y)> Wedge(ChamferJoint j, int side, double margin)
        {
            var res = new List<(double, double)>();
            if (j == null || j.L <= 0) return res;
            double s = side > 0 ? j.StepPlus : j.StepMinus;
            if (s <= FLUSH_TOL) return res;
            double h = j.HalfThick;
            double k = s / j.L, m = margin;
            // Cạnh vát kéo qua L thêm m (đỉnh không nằm ĐÚNG trên mặt tấm dày ⇒ boolean ổn định hơn — review).
            var local = new[] { (-m, h - s - k * m), (j.L + m, h + k * m), (j.L + m, h + m + k * m), (-m, h + m + k * m) };
            foreach (var (x, y) in local)
                res.Add((j.Jx + j.Ux * x + j.Nx * side * y, j.Jy + j.Uy * x + j.Ny * side * y));
            return res;
        }

        private static bool Dir(WebSeg s, out double x, out double y)
        {
            double dx = s.Bx - s.Ax, dy = s.By - s.Ay, l = Math.Sqrt(dx * dx + dy * dy);
            x = l > 1e-9 ? dx / l : 0; y = l > 1e-9 ? dy / l : 0;
            return l > 1e-9 && s.T > 0;
        }

        private static double Clean(double v) => Math.Abs(v) < 1e-9 ? 0 : Math.Max(0, v);
    }
}
