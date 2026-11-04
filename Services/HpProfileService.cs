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

        /// <summary>
        /// Tiết diện tấm <b>Collar Plate / LUG B1R</b> (Hatch <c>1F3F2</c>, layer <c>DM4_STEEL_CUTOUT_RASTER</c>,
        /// <c>*Model_Space</c> của CAS.dwt — HP120). Hệ trục KHỚP <see cref="HpCutoutSection"/> (gốc = LƯNG∩ĐẦU,
        /// +X → BỤNG, −Y xuống). Tấm phẳng nằm trong MẶT PHẲNG WEB, PHỦ phần BỤNG của lỗ CO + lấn lên web:
        /// mép GÓT tại <c>X = t</c> (mặt bụng thân HP); đáy tại <c>Y = −B1</c> (<see cref="LookupB1"/> — TABLE 1
        /// cột B1 tra theo <b>b</b>, HP120 ⇒ 90); mép BỤNG tại <c>X = W + coR</c> (<c>W = (c+r)+coR</c>);
        /// vát góc gót 10×10; đỉnh phía bụng vát 45° + bo <c>coR</c>; góc bụng-đáy bo <c>coR</c>. KHÔNG có
        /// "váy" dưới B1 (xác nhận qua Hatch 2026-08-28) — tấm KHÔNG chồng thân/bầu HP nên KHÔNG trừ lỗ.
        /// Xem skill web-hp-cutout §7.
        /// </summary>
        List<VertexBulge> CollarPlateSection(double b, double t, double c, double r, double coR);

        /// <summary>
        /// Dao cắt <b>"HP TO HP END"</b> (CAS.dump.json) trong MẶT CẮT HP CHẠY — gốc = mặt tiếp xúc (mặt web
        /// HP chạy phía HP đầu), +X → VÀO HP đầu, −Y xuống. Bọc <see cref="Builders.HpEndAtHpGeometry.CutterOutline"/>.
        /// null nếu tham số vô lý / không còn mặt hàn. Xem skill hp-end-at-hp.
        /// </summary>
        List<VertexBulge> HpEndAtHpCutter(double b, double tRun, double c, double r, bool inside);

        /// <summary>Dao cắt <b>"HP TO FB END"</b> (mặt hàn h_fb−15 + vát 30°; FB cao ⇒ R30) trong MẶT CẮT FB CHẠY.
        /// Bọc <see cref="Builders.HpEndAtHpGeometry.FbCutterOutline"/>. null nếu vô lý. Xem skill hp-end-at-hp §7.</summary>
        List<VertexBulge> HpEndAtFbCutter(double b, double hFb, double tFb, double c, double r);

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
        /// Bảng chiều cao tấm <b>Collar Plate / LUG B1R</b> — TABLE 1 (CAS.dwt), cột <b>B1</b>. Key = <b>H
        /// = chiều cao HP</b> (bulb <c>b</c>) — GIỐNG <see cref="CoRadiusTable"/>, KHÔNG phải chiều cao web
        /// (xác nhận kỹ sư 2026-08-28: "HP120 → B1 = 90"; Hatch `1F3F2` đáy collar tại Y = −90 = −B1).
        /// <c>B1 ≈ round5(b − min(b/4, 100)) ≈ 0.75·b</c>. Cổng "web &lt; 300" là điều kiện RIÊNG.
        /// Xem skill web-hp-cutout §7.
        /// </summary>
        private static readonly Dictionary<int, double> B1Table = new Dictionary<int, double>
        {
            {  80,  60.0 }, { 100,  75.0 }, { 120,  90.0 }, { 140, 105.0 }, { 160, 120.0 }, { 180, 135.0 },
            { 200, 150.0 }, { 220, 165.0 }, { 240, 180.0 }, { 260, 195.0 }, { 280, 210.0 }, { 300, 225.0 },
            { 320, 240.0 }, { 340, 255.0 }, { 370, 275.0 }, { 400, 300.0 }, { 430, 320.0 },
        };

        /// <summary>Tra chiều cao B1 tấm Collar Plate theo chiều cao HP <paramref name="b"/> (mm) — bulb.
        /// b ngoài bảng → mục gần nhất (bảng bậc thang, KHÔNG nội suy — như <see cref="LookupCoRadius"/>).</summary>
        public static void LookupB1(double b, out double b1)
        {
            int key = (int)Math.Round(b);
            if (B1Table.TryGetValue(key, out b1)) return;
            int best = 0; double bestD = double.MaxValue;
            foreach (int k in B1Table.Keys)
            {
                double d = Math.Abs(k - key);
                if (d < bestD) { bestD = d; best = k; }
            }
            b1 = B1Table[best];
        }

        /// <summary>Dao cắt "HP TO HP END" — xem <see cref="IHpProfileService.HpEndAtHpCutter"/>.</summary>
        public List<VertexBulge> HpEndAtHpCutter(double b, double tRun, double c, double r, bool inside)
            => ToVb(Builders.HpEndAtHpGeometry.CutterOutline(b, tRun, c, r, inside));

        /// <summary>Dao cắt "HP TO FB END" — xem <see cref="IHpProfileService.HpEndAtFbCutter"/>.</summary>
        public List<VertexBulge> HpEndAtFbCutter(double b, double hFb, double tFb, double c, double r)
            => ToVb(Builders.HpEndAtHpGeometry.FbCutterOutline(b, hFb, tFb, c, r));

        private static List<VertexBulge> ToVb(List<(double X, double Y, double Bulge)> o)
        {
            if (o == null) return null;
            var list = new List<VertexBulge>(o.Count);
            foreach (var v in o) list.Add(new VertexBulge(v.X, v.Y, v.Bulge));
            return list;
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

        // Vát góc gót collar 10×10 (hình "10×10"); cũng dùng làm mức lùi mép TOP-FLAT phía bụng (W − HC).
        private const double CollarHeelChamfer = 10.0;

        /// <summary>
        /// Tiết diện Collar Plate / LUG B1R — polygon 8 đỉnh KHỚP 1:1 Hatch <c>1F3F2</c> (HP120: b=120,
        /// c≈17, r≈5, coR=30 ⇒ W=52, mép bụng X=82≈83, đáy Y=−B1=−90). Hệ trục = <see cref="HpCutoutSection"/>.
        /// Duyệt CCW (bulge DƯƠNG, khớp thứ tự đỉnh trong Hatch). Đáy = −B1 (<see cref="LookupB1"/>), KHÔNG váy.
        /// </summary>
        public List<VertexBulge> CollarPlateSection(double b, double t, double c, double r, double coR)
        {
            double R = coR;
            double bw = c + r;
            double W = bw + R;                                 // mép BỤNG lỗ CO
            LookupB1(b, out double b1);                        // chiều cao collar = TABLE 1 cột B1 (theo b — HP120 ⇒ 90)
            double bot = -b1;                                  // Y đáy collar
            double HC = CollarHeelChamfer;
            double hIn = t;                                    // mép GÓT collar = mặt BỤNG thân HP

            // Tham số vô lý → chữ nhật đơn giản hIn..W+R × 0..bot (an toàn, không vỡ build).
            if (b <= 0 || R <= 0 || c <= 0 || r <= 0 || b1 <= R + HC || (W - HC) <= (hIn + HC))
            {
                double xr = Math.Max(W + R, hIn + 1);
                return new List<VertexBulge>(4)
                {
                    new VertexBulge(hIn, 0,   0),
                    new VertexBulge(xr,  0,   0),
                    new VertexBulge(xr,  bot, 0),
                    new VertexBulge(hIn, bot, 0),
                };
            }

            double sq = R * Math.Sqrt(2.0);
            double s = R / Math.Sqrt(2.0);
            const double B45 = 0.19891237;                     // tan(45°/4) — cung ¼ R nối 45° → mép bụng đứng
            const double B90 = 0.41421356;                     // tan(90°/4) — cung ¼ R góc bụng-đáy

            return new List<VertexBulge>(8)
            {
                new VertexBulge(W + R,     bot + R,        0),      // (83,−60) đầu cung R góc bụng-đáy
                new VertexBulge(W + R,    -(HC + sq),       B45),   // (83,−52.4) mép BỤNG đứng ↑ → cung ¼ R
                new VertexBulge(W + s,    -(HC + sq) + s,   0),     // (74.2,−31.2) tiếp điểm cung ↔ đường vát 45°
                new VertexBulge(W - HC,    0,               0),     // (43,0) đỉnh đường vát 45° trên mặt dưới Top Plate
                new VertexBulge(hIn + HC,  0,               0),     // (16,0) hết top-flat → vát góc gót
                new VertexBulge(hIn,      -HC,              0),     // (6,−10) vát góc gót 10×10
                new VertexBulge(hIn,       bot,             0),     // (6,−90) mép GÓT (mặt bụng thân HP) xuống đáy
                new VertexBulge(W,         bot,             B90),   // (53,−90) đáy → cung ¼ R lên (83,−60)
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
