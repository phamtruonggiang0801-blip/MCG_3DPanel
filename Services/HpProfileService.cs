using System;
using System.Collections.Generic;
using MCG_3DPanel.Models;

namespace MCG_3DPanel.Services
{
    /// <summary>
    /// Sinh TIẾT DIỆN HP (bulb flat / Holland Profile) theo tham số bản vẽ tiêu chuẩn.
    /// Đã kiểm chứng 1:1 với block `HP_120x6_sv` (IB-IB1.dump) — sai lệch &lt; 0.01mm.
    ///
    /// Tham số (theo hình parameter của HP):
    ///   b  = chiều cao toàn phần (vd 120)          t  = chiều dày web (vd 6)
    ///   c  = bề rộng ĐÁY BULB tính từ LƯNG (x=0)   r  = bán kính bulb + bán kính chuyển tiếp
    ///   r1 = bo góc mép (tuỳ chọn, 0 = sắc cạnh)   30° = góc vát (taper) — HẰNG SỐ
    /// Suy ra:  tâm cung bulb xB = c   ·   bulb rộng nhất = c + r   ·   dx = b − r − (3r − t + c)/√3
    ///
    /// Hệ toạ độ local: gốc (0,0) = góc LƯNG–ĐẦU (mặt phẳng ở x=0, đầu hàn vào tôn ở y=0);
    /// +X = về phía BỤNG (bulb); −Y = xuống theo chiều cao b. Outline kín, duyệt CW.
    /// Xem skill hp-profile.
    /// </summary>
    public interface IHpProfileService
    {
        /// <summary>Tiết diện HP kín (2D local + bulge). r1=0 → góc sắc.</summary>
        List<VertexBulge> HpSection(double b, double t, double c, double r, double r1 = 0.0);

        /// <summary>
        /// Tiết diện LỖ KHOÉT trên web cho HP xuyên qua ("CO B — Cut Out Bulb-profile", block
        /// <c>HP120x6-scaloop</c> trong CAS.dwt). Hệ trục KHỚP <see cref="HpSection"/>.
        /// Mép LƯNG (x=0) trùng mặt LƯNG HP (gap = 0); phía BỤNG mở tới <c>W = (c + r) + coR</c>; phần
        /// trên sâu tới Y = −(b−coR); chân "foot" = offset đoạn đáy ra bán kính <paramref name="coR"/>
        /// (TABLE 1, cột R — tra <see cref="LookupCoRadius"/>). Cột h1/h2/B1–B4 của TABLE 1 = tham số LUG
        /// (chi tiết riêng) → KHÔNG dùng. Xem skill web-hp-cutout.
        /// </summary>
        List<VertexBulge> HpCutoutSection(double b, double t, double c, double r, double coR);

        /// <summary>Tra tham số theo quy cách "cao*dày" (vd "120*6"). false nếu spec sai.</summary>
        bool TryGetSection(string spec, out List<VertexBulge> section, out bool estimated);
    }

    public class HpProfileService : IHpProfileService
    {
        private const double Taper = 30.0;                  // góc vát, hằng số theo bản vẽ
        private static readonly double Sqrt3 = Math.Sqrt(3.0);

        /// <summary>
        /// Bảng HP bulb-flat tiêu chuẩn (nguồn HP.csv). **c và r CHỈ phụ thuộc b**, không phụ thuộc t
        /// → key = b. Mỗi mục: { c, r, tMin, tMax }. Xem skill hp-profile (đánh giá dữ liệu + bảng đầy đủ).
        /// </summary>
        private static readonly Dictionary<int, double[]> Table = new Dictionary<int, double[]>
        {
            {  60, new[] { 13.0,  3.5,  4.0,  6.0 } },
            {  80, new[] { 14.0,  4.0,  5.0,  8.0 } },
            { 100, new[] { 15.5,  4.5,  6.0,  8.0 } },
            { 120, new[] { 17.0,  5.0,  6.0,  8.0 } },
            { 140, new[] { 19.0,  5.5,  7.0,  9.0 } },
            { 160, new[] { 22.0,  6.0,  7.0,  9.0 } },
            { 180, new[] { 25.0,  7.0,  8.0, 11.0 } },
            { 200, new[] { 28.0,  8.0,  9.0, 12.0 } },
            { 220, new[] { 31.0,  9.0, 10.0, 12.0 } },
            { 240, new[] { 34.0, 10.0, 10.0, 12.0 } },
            { 260, new[] { 37.0, 11.0, 10.0, 12.0 } },
            { 280, new[] { 40.0, 12.0, 11.0, 13.0 } },
            { 300, new[] { 43.0, 13.0, 11.0, 13.0 } },
            { 320, new[] { 46.0, 14.0, 12.0, 14.0 } },
            { 340, new[] { 49.0, 15.0, 12.0, 14.0 } },
            { 370, new[] { 53.5, 16.5, 13.0, 15.0 } },
            { 400, new[] { 58.0, 18.0, 14.0, 16.0 } },
            { 430, new[] { 62.5, 19.5, 14.0, 20.0 } },
        };

        /// <summary>Danh sách quy cách HP tiêu chuẩn ("cao*dày") — 18 cỡ b trong <see cref="Table"/>,
        /// dùng <c>tMin</c> làm chiều dày danh nghĩa. Đổ vào ComboBox chọn HP trên Palette. ⚠️ Khi dựng
        /// chỉ <b>b</b> được dùng (chiều dày thật lấy theo hình học Plan) — <c>t</c> ở đây chỉ để hiển thị.</summary>
        public static IEnumerable<string> StandardSpecs()
        {
            var keys = new List<int>(Table.Keys);
            keys.Sort();
            foreach (int b in keys)
                yield return b.ToString() + "*" + Table[b][2].ToString("0.#");
        }

        /// <summary>Tra (c, r) theo chiều cao b. estimated=true nếu b không có trong bảng (nội/ngoại suy).</summary>
        public static void LookupCR(double b, out double c, out double r, out bool estimated)
        {
            int key = (int)Math.Round(b);
            if (Table.TryGetValue(key, out double[] v)) { c = v[0]; r = v[1]; estimated = false; return; }
            // Ngoài bảng → nội suy tuyến tính theo b giữa 2 mục gần nhất (c,r tăng đều theo b).
            estimated = true;
            var keys = new List<int>(Table.Keys); keys.Sort();
            if (key <= keys[0]) { c = Table[keys[0]][0]; r = Table[keys[0]][1]; return; }
            if (key >= keys[keys.Count - 1]) { c = Table[keys[keys.Count - 1]][0]; r = Table[keys[keys.Count - 1]][1]; return; }
            int lo = keys[0], hi = keys[keys.Count - 1];
            foreach (int k in keys) { if (k <= key) lo = k; else { hi = k; break; } }
            double f = (b - lo) / (double)(hi - lo);
            c = Table[lo][0] + f * (Table[hi][0] - Table[lo][0]);
            r = Table[lo][1] + f * (Table[hi][1] - Table[lo][1]);
        }

        /// <summary>
        /// Bảng bán kính <b>R</b> của lỗ khoét CO ("CO B" / TABLE 1, cột R) — key = H (chiều cao HP
        /// danh nghĩa). R = 15 / 20 / 30 / 30 rồi **35 CỐ ĐỊNH** từ H≥160. Các cột h1/h2/B1–B4 của
        /// TABLE 1 là tham số LUG (chi tiết riêng) → KHÔNG dùng. Xem skill web-hp-cutout.
        /// </summary>
        private static readonly Dictionary<int, double> CoRadiusTable = new Dictionary<int, double>
        {
            {  80, 15.0 }, { 100, 20.0 }, { 120, 30.0 }, { 140, 30.0 }, { 160, 35.0 }, { 180, 35.0 },
            { 200, 35.0 }, { 220, 35.0 }, { 240, 35.0 }, { 260, 35.0 }, { 280, 35.0 }, { 300, 35.0 },
            { 320, 35.0 }, { 340, 35.0 }, { 370, 35.0 }, { 400, 35.0 }, { 430, 35.0 },
        };

        /// <summary>Tra bán kính R của lỗ khoét CO theo chiều cao b. b ngoài bảng (kể cả HP60 —
        /// TABLE 1 không có hàng) → lấy mục H gần nhất (bảng bậc thang, KHÔNG nội suy).</summary>
        public static void LookupCoRadius(double b, out double coR)
        {
            int key = (int)Math.Round(b);
            if (CoRadiusTable.TryGetValue(key, out coR)) return;
            int best = 0; double bestD = double.MaxValue;
            foreach (int k in CoRadiusTable.Keys)
            {
                double d = Math.Abs(k - key);
                if (d < bestD) { bestD = d; best = k; }
            }
            coR = CoRadiusTable[best];
        }

        /// <summary>
        /// Tiết diện LỖ KHOÉT trên web cho HP xuyên qua ("CO B", block <c>HP120x6-scaloop</c> trong
        /// CAS.dwt). Hệ trục KHỚP <see cref="HpSection"/>: gốc (0,0) = góc LƯNG–ĐẦU; +X → BỤNG; −Y xuống.
        ///
        /// Hình (xác nhận kỹ sư 2026-08-28): mép LƯNG (x=0) trùng mặt LƯNG HP (gap = 0 phía lưng); phía
        /// BỤNG mở rộng tới <c>W = (c + r) + R</c>. Phần trên = chữ nhật sâu tới Y = −(b−R). Chân "foot"
        /// = offset đoạn [(0,−b) → (c+r,−b)] ra ngoài bán kính R: nửa cung R sau LƯNG (tâm (0,−b), qua
        /// (−R,−b)), đáy phẳng Y = −(b+R), cung ¼ R lên mép BỤNG (tâm (c+r,−b)).
        /// Snipe 10×10 (vát mép hàn, DET.A) KHÔNG dựng vào solid.
        /// <paramref name="t"/> (dày web HP) hiện không dùng — mép slot lưng = mặt LƯNG HP (gap 0).
        /// </summary>
        public List<VertexBulge> HpCutoutSection(double b, double t, double c, double r, double coR)
        {
            double R = coR;
            if (b <= 0 || R <= 0 || c <= 0 || r <= 0)
                return HpSection(b, t, c, r);                  // tham số vô lý → tiết diện HP thô (an toàn)

            double bw = c + r;                                 // bề rộng bulb tính từ LƯNG
            double W = bw + R;                                 // mép BỤNG lỗ (xác nhận kỹ sư: W = (c+r) + R)
            double dUp = b - R;                                // đáy phần trên (= mức "90" HP120)
            if (dUp <= 1e-6) dUp = b * 0.5;                    // HP quá thấp so với R → kẹp an toàn

            const double B90 = 0.41421356;                     // |bulge| cung 90°
            const double B180 = 1.0;                           // |bulge| cung 180°
            return new List<VertexBulge>(6)
            {
                new VertexBulge(0,   0,          0),           // LƯNG ∩ ĐẦU
                new VertexBulge(W,   0,          0),           // mặt dưới Top Plate → mép BỤNG
                new VertexBulge(W,  -b,         -B90),         // mép BỤNG xuống ↓ → cung ¼ R (tâm (bw,−b))
                new VertexBulge(bw, -(b + R),    0),           // đáy phẳng foot ←
                new VertexBulge(0,  -(b + R),   -B180),        // → nửa cung R sau LƯNG (tâm (0,−b), qua (−R,−b))
                new VertexBulge(0,  -dUp,        0),           // mặt LƯNG ↑ về (0,0)
            };
        }

        public bool TryGetSection(string spec, out List<VertexBulge> section, out bool estimated)
        {
            section = null; estimated = false;
            if (!PanelInput.TryParseSpec(spec, out double b, out double t)) return false;
            LookupCR(b, out double c, out double r, out estimated);
            section = HpSection(b, t, c, r);
            return true;
        }

        public List<VertexBulge> HpSection(double b, double t, double c, double r, double r1 = 0.0)
        {
            double xB = c;                                       // tâm cung bulb (X) = mép đáy bulb
            double dx = b - r - (3.0 * r - t + c) / Sqrt3;       // đoạn web THẲNG (điều kiện vát 30° tiếp tuyến chung TRONG)
            if (b <= 0 || t <= 0 || c <= t || r <= 0 || dx <= r1 + r)
                return Rect(b, t);                               // tham số vô lý → tiết diện chữ nhật

            double ta = Taper * Math.PI / 180.0;
            // Cung 1 (LÕM, nối mặt web → vát): tâm A=(t+r,−dx), quét 180°→240°.
            double ax = t + r, ay = -dx;
            double p3x = ax + r * Math.Cos(Math.PI + ta * 2), p3y = ay + r * Math.Sin(Math.PI + ta * 2);
            // Cung 2 (LỒI, bulb): tâm B=(xB,−(b−r)), quét 60°→270° (CW 150°).
            double bx = xB, by = -(b - r);
            double p4x = bx + r * Math.Cos(ta * 2), p4y = by + r * Math.Sin(ta * 2);

            const double B90 = 0.41421356;                       // bulge góc vuông 90°
            double bArc1 = Math.Tan((90.0 - Taper) / 4.0 * Math.PI / 180.0);   // +tan(15°) cung lõm 60°
            double bArc2 = -Math.Tan((180.0 - Taper) / 4.0 * Math.PI / 180.0); // −tan(37.5°) cung bulb 150°

            var o = new List<VertexBulge>(10);
            if (r1 > 0.0)
            {
                o.Add(new VertexBulge(r1, 0, 0));                 // sau bo góc lưng-đầu
                o.Add(new VertexBulge(t - r1, 0, -B90));          // mép đầu → bo góc web-đầu
                o.Add(new VertexBulge(t, -r1, 0));
            }
            else
            {
                o.Add(new VertexBulge(0, 0, 0));
                o.Add(new VertexBulge(t, 0, 0));
            }
            o.Add(new VertexBulge(t, -dx, bArc1));                // mặt web → cung lõm
            o.Add(new VertexBulge(p3x, p3y, 0));                  // → vát 30°
            o.Add(new VertexBulge(p4x, p4y, bArc2));              // → cung bulb
            o.Add(new VertexBulge(xB, -b, 0));                    // → mép đáy (đầu bulb)
            if (r1 > 0.0)
            {
                o.Add(new VertexBulge(r1, -b, -B90));             // bo góc đáy-lưng
                o.Add(new VertexBulge(0, -b + r1, 0));
                o.Add(new VertexBulge(0, -r1, -B90));             // bo góc lưng-đầu (đóng)
            }
            else
            {
                o.Add(new VertexBulge(0, -b, 0));
            }
            return o;
        }

        /// <summary>Fallback: tiết diện chữ nhật b×t (khi tham số không hợp lệ).</summary>
        private static List<VertexBulge> Rect(double b, double t)
        {
            return new List<VertexBulge>(4)
            {
                new VertexBulge(0, 0, 0),
                new VertexBulge(t, 0, 0),
                new VertexBulge(t, -b, 0),
                new VertexBulge(0, -b, 0),
            };
        }
    }
}
