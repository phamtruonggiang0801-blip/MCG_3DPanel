using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.Geometry;

namespace MCG_3DPanel.Services
{
    /// <summary>
    /// Sinh biên dạng (elevation) cho Bracket knee. 2 chế độ (kiểm chứng block explode OB-OB1.dump):
    ///  - FLANGED (có gấp lip/bend): toe = R30(cắt lõm vào thân) → đoạn 18mm(∥free-edge) → Rb → đoạn dày(⊥) → bend.
    ///    Dựng PARAMETRIC, **scale theo chiều dày**: Rb=2·thk (12@6 / 20@10), đoạn-dày = thk (6@6 / 10@10).
    ///  - KHÔNG flanged: chỉ R30 (2 cung R30 tiếp tuyến chung 1 free-edge thẳng).
    /// R30 = bán kính toe CỐ ĐỊNH (mọi dày). Xem skill parametric-profiles + bracket-identification.
    ///
    /// Hệ toạ độ local: gốc (0,0) = góc Web∩TopPlate; +X = dọc member (áp Top Plate);
    /// −Y = xuống theo Web (height). Outline duyệt CW. Bulge = cung (chuẩn LWPolyline).
    /// </summary>
    public interface IProfileService
    {
        /// <summary>
        /// Outline bracket knee (điểm 2D local + bulge), kín. S=leg-on-TopPlate; H=chiều cao (đáy y=−H);
        /// F=leg-on-Flange; thk=chiều dày (→ Rb=2·thk, đoạn-dày=thk); under50=overhang&lt;50 (H=Huse, F=15);
        /// dtop=chiều cao mặt đầu span (OB=HP−15 · IB=HP−30 · FB=FlatBar−15);
        /// hpLedge=đoạn ngang đầu span trước R30 (IB=15, OB/FB=0); flanged=có gấp lip → toe thêm 18mm+Rb+dày.
        /// squareCorner=Flange đỡ vươn xa hơn cả bracket (F bị kẹp trần S−R30): bỏ R30 dưới, hạ thẳng
        /// đứng góc vuông 90° thay vì "ôm" theo F — toe trên (Dtop/R30 gần HP) giữ nguyên không đổi.
        /// </summary>
        List<VertexBulge> BracketOutline(bool under50, double spanS, double heightH, double flangeF, double thk,
            double dtop, double hpLedge, bool flanged, bool squareCorner);
    }

    public struct VertexBulge
    {
        public Point2d Pt;
        public double Bulge;
        public VertexBulge(double x, double y, double bulge) { Pt = new Point2d(x, y); Bulge = bulge; }
    }

    public class ProfileService : IProfileService
    {
        private const double ToeR = 30.0;      // bán kính toe-end R30 (cố định mọi chiều dày)
        private const double FlangeSeg = 18.0; // đoạn thẳng R30→Rb (∥ free-edge)

        public List<VertexBulge> BracketOutline(bool under50, double S, double H, double F, double thk,
            double dtop, double hpLedge, bool flanged, bool squareCorner)
        {
            if (!flanged) return R30Only(S, H, F, dtop, hpLedge, squareCorner, under50);
            if (under50) return TemplateUnder50(S, H, F);        // stub nâng (hiếm) — template thk6, Dtop=105
            return FlangedToe(S, H, F, thk, dtop, hpLedge);      // over-50: parametric, scale dày + Dtop/ledge
        }

        // Đầu span: (S,0)→(S,−Dtop) [mặt đứng] → (nếu IB) ledge ngang (S,−Dtop)→(S−ledge,−Dtop).
        // R30 top: tâm (S−ledge, −Dtop−R); tiếp điểm-leg (top cung) = (S−ledge, −Dtop).
        private static void TopSpan(List<VertexBulge> o, double S, double dtop, double ledge)
        {
            o.Add(new VertexBulge(0, 0, 0));
            o.Add(new VertexBulge(S, 0, 0));
            if (ledge > 0.0) o.Add(new VertexBulge(S, -dtop, 0));   // IB: mặt đứng rồi mới ledge
        }

        // ─── FLANGED over-50: toe R30 → 18mm → Rb → dày → bend (parametric, scale thk + Dtop/ledge) ───
        // Khớp block OB_type1 khi thk=6/Dtop=105/ledge=0; IB thêm ledge; thk10 → Rb=20, dày=10.
        private static List<VertexBulge> FlangedToe(double S, double H, double F, double thk, double dtop, double ledge)
        {
            double R = ToeR, Rb = 2.0 * thk, seg = FlangeSeg;
            var cR30T = new Point2d(S - ledge, -dtop - R);   // tâm R30 top (trên đường leg đầu span/ledge)
            var cR30B = new Point2d(F + R, -H);              // tâm R30 bot (trên đường flange)

            Vector2d ctr = cR30B - cR30T;
            if (ctr.Length < 2.0 * R + 2.0 * Rb + seg + thk)   // quá ngắn cho chuỗi toe → R30 đơn
                return R30Only(S, H, F, dtop, ledge, false);

            Vector2d u = ctr.GetNormal();                       // dọc free-edge (top→bot)
            var n = new Vector2d(u.Y, -u.X);
            if ((Point2d.Origin - cR30T).DotProduct(n) < 0.0)   // n hướng VỀ thân (gốc)
                n = n.Negate();

            var legT = new Point2d(S - ledge, -dtop);
            var legB = new Point2d(F, -H);
            Point2d tR30T = cR30T + n * R,  tR30B = cR30B + n * R;   // tiếp điểm R30 trên đường segment
            Point2d p1T = tR30T + u * seg,  p1B = tR30B - u * seg;   // cuối đoạn 18mm
            Point2d cRbT = p1T - n * Rb,    cRbB = p1B - n * Rb;     // tâm Rb (phía ngoài thân)
            Point2d p2T = cRbT + u * Rb,    p2B = cRbB - u * Rb;     // cuối cung Rb
            Point2d p3T = p2T - n * thk,    p3B = p2B - n * thk;     // đầu/cuối free-edge (sau đoạn dày)

            double bR30T = ArcBulge(cR30T, legT, tR30T);
            double bRbT = ArcBulge(cRbT, p1T, p2T);
            double bRbB = ArcBulge(cRbB, p2B, p1B);
            double bR30B = ArcBulge(cR30B, tR30B, legB);

            var o = new List<VertexBulge>(14);
            TopSpan(o, S, dtop, ledge);
            o.Add(new VertexBulge(legT.X, legT.Y, bR30T));   // (S−ledge,−Dtop) → R30
            o.Add(new VertexBulge(tR30T.X, tR30T.Y, 0));     // → đoạn 18mm
            o.Add(new VertexBulge(p1T.X, p1T.Y, bRbT));      // → Rb
            o.Add(new VertexBulge(p2T.X, p2T.Y, 0));         // → đoạn dày (thk)
            o.Add(new VertexBulge(p3T.X, p3T.Y, 0));         // → free-edge (bend)
            o.Add(new VertexBulge(p3B.X, p3B.Y, 0));         // → đoạn dày
            o.Add(new VertexBulge(p2B.X, p2B.Y, bRbB));      // → Rb
            o.Add(new VertexBulge(p1B.X, p1B.Y, 0));         // → đoạn 18mm
            o.Add(new VertexBulge(tR30B.X, tR30B.Y, bR30B)); // → R30
            o.Add(new VertexBulge(legB.X, legB.Y, 0));       // (F,−H) → flange leg
            o.Add(new VertexBulge(0, -H, 0));                // → web (tự đóng)
            return o;
        }

        // ─── KHÔNG flanged: chỉ R30 (2 cung tiếp tuyến chung free-edge thẳng) ───
        private static List<VertexBulge> R30Only(double S, double H, double F, double dtop, double ledge, bool squareCorner, bool under50 = false)
        {
            double R = ToeR;
            var cTop = new Point2d(S - ledge, -dtop - R);
            var tLegTop = new Point2d(S - ledge, -dtop);

            if (squareCorner)
            {
                // Flange đỡ vươn xa hơn cả bracket (F bị kẹp sát S−R30, "Flange dài hơn"): bỏ hẳn R30
                // dưới — hạ THẲNG ĐỨNG từ tiếp tuyến R30 trên xuống đáy, gặp đáy tại góc vuông 90°. Top
                // (R30 gần HP/span) giữ nguyên không đổi. Tương tự squareCorner của GE (xem GeProfileService
                // + skill girder-end mục 6c) — tránh vòng tròn R30 dưới bị đẩy sát mép, lấn qua đáy.
                var tFreeSq = new Point2d(cTop.X - R, cTop.Y);   // tiếp tuyến khi free-edge thẳng đứng u=(0,−1)
                double bTopSq = ArcBulge(cTop, tLegTop, tFreeSq);
                var osq = new List<VertexBulge>(7);
                TopSpan(osq, S, dtop, ledge);
                osq.Add(new VertexBulge(tLegTop.X, tLegTop.Y, bTopSq));
                osq.Add(new VertexBulge(tFreeSq.X, tFreeSq.Y, 0));
                osq.Add(new VertexBulge(tFreeSq.X, -H, 0));
                osq.Add(new VertexBulge(0, -H, 0));
                return osq;
            }

            if (under50)
            {
                // Chân bracket "thụt 30mm trên Flange" (Huse=WebH−30, F=15 cố định — xem KneeParams):
                // bỏ hẳn cung R30 dưới, nhưng chân VẪN Ở ĐÚNG VỊ TRÍ CŨ (F,−H) — xác nhận kỹ sư
                // 2026-08-26 (khác squareCorner: squareCorner đổi cả vị trí X chân sang thẳng đứng dưới
                // R30 trên; ở đây chân giữ nguyên, chỉ đường free-edge nối THẲNG (không bo) từ tiếp
                // tuyến R30 trên xuống đúng góc (F,−H) — góc SẮC tại đó thay vì bo tròn).
                // Free-edge giờ là tiếp tuyến từ đường tròn R30 trên (tâm cTop) tới ĐIỂM (F,−H) — không
                // còn đường tròn dưới để "tiếp tuyến chung 2 đường tròn" như nhánh thường bên dưới nữa.
                var legBot = new Point2d(F, -H);

                // Hướng tham khảo (n) để chọn đúng 1 trong 2 nghiệm tiếp tuyến — dùng CHÍNH cách tính n
                // của nhánh R30-cả-2-đầu bên dưới (dựa trên đường tròn tưởng tượng tâm F+R,−H) để đảm
                // bảo chọn đúng phía NGOÀI thân, nhất quán với mọi trường hợp khác.
                Vector2d refCtr = new Point2d(F + R, -H) - cTop;
                Vector2d refN = refCtr.Length > 1e-9 ? new Vector2d(refCtr.Y, -refCtr.X).GetNormal() : new Vector2d(1, 0);
                if ((Point2d.Origin - cTop).DotProduct(refN) < 0.0) refN = refN.Negate();

                Point2d tFreeTop50 = TangentPointFromExternal(cTop, R, legBot, refN);
                double bTop50 = ArcBulge(cTop, tLegTop, tFreeTop50);

                var o50 = new List<VertexBulge>(7);
                TopSpan(o50, S, dtop, ledge);
                o50.Add(new VertexBulge(tLegTop.X, tLegTop.Y, bTop50));
                o50.Add(new VertexBulge(tFreeTop50.X, tFreeTop50.Y, 0));
                o50.Add(new VertexBulge(legBot.X, legBot.Y, 0));   // góc SẮC, đúng vị trí chân cũ (F,−H)
                o50.Add(new VertexBulge(0, -H, 0));
                return o50;
            }

            var cBot = new Point2d(F + R, -H);

            Vector2d ctr = cBot - cTop;
            if (ctr.Length < 2.0 * R + 1.0)
                return SharpOutline(S, H, F, dtop, ledge);

            Vector2d u = ctr.GetNormal();
            var n = new Vector2d(u.Y, -u.X);
            if ((Point2d.Origin - cTop).DotProduct(n) < 0.0)   // n hướng VỀ thân → cung LÕM cắt vào
                n = n.Negate();

            Point2d tFreeTop = cTop + n * R, tFreeBot = cBot + n * R;
            var tLegBot = new Point2d(F, -H);
            double bTop = ArcBulge(cTop, tLegTop, tFreeTop);
            double bBot = ArcBulge(cBot, tFreeBot, tLegBot);

            var o = new List<VertexBulge>(8);
            TopSpan(o, S, dtop, ledge);
            o.Add(new VertexBulge(tLegTop.X, tLegTop.Y, bTop));
            o.Add(new VertexBulge(tFreeTop.X, tFreeTop.Y, 0));
            o.Add(new VertexBulge(tFreeBot.X, tFreeBot.Y, bBot));
            o.Add(new VertexBulge(tLegBot.X, tLegBot.Y, 0));
            o.Add(new VertexBulge(0, -H, 0));
            return o;
        }

        // ─── UNDER-50 flanged (hiếm): template block OB_type2 (thk6) + giãn leg ───
        private static readonly double[][] UNDER50 =   // OB_type2 (mirror +x): RefS=547 RefH=344 RefF=15
        {
            new[] {  15.0,   -344.0,    0.0    }, new[] {  31.385, -336.548, -0.414  },
            new[] {  47.276, -342.503,  0.0    }, new[] {  49.76,  -347.965,  0.0    },
            new[] { 514.724, -136.497,  0.0    }, new[] { 512.24,  -131.035, -0.414  },
            new[] { 518.195, -115.144,  0.0    }, new[] { 534.58,  -107.692, -0.107  },
            new[] { 547.0,   -105.0,    0.0    }, new[] { 547.0,      0.0,    0.0    },
            new[] {   0.0,      0.0,    0.0    }, new[] {   0.0,   -344.0,    0.0    },
        };

        private static List<VertexBulge> TemplateUnder50(double S, double H, double F)
        {
            const double refS = 547.0, refH = 344.0, refF = 15.0;
            double dS = S - refS, dH = H - refH, dF = F - refF;
            var o = new List<VertexBulge>(UNDER50.Length);
            foreach (var v in UNDER50)
            {
                double x = v[0], y = v[1];
                if (y > -refH / 2.0) { if (x > refS / 2.0) x += dS; }
                else { y -= dH; if (x > 50.0) x += dF; }
                o.Add(new VertexBulge(x, y, v[2]));
            }
            return o;
        }

        /// <summary>Tiếp điểm trên đường tròn tâm <paramref name="c"/> bán kính <paramref name="r"/> của
        /// tiếp tuyến đi qua điểm NGOÀI <paramref name="p"/> — có 2 nghiệm, chọn nghiệm có
        /// <c>(T−c)</c> cùng hướng (tích vô hướng LỚN HƠN) với <paramref name="refN"/> (hướng tham khảo,
        /// đã chọn đúng phía ngoài thân từ nơi gọi). Dùng cho <see cref="R30Only"/> nhánh <c>under50</c>
        /// (bỏ vòng tròn R30 dưới, free-edge tiếp tuyến thẳng tới 1 ĐIỂM thay vì 1 đường tròn khác).</summary>
        private static Point2d TangentPointFromExternal(Point2d c, double r, Point2d p, Vector2d refN)
        {
            Vector2d e = p - c;
            double d2 = e.X * e.X + e.Y * e.Y;
            if (d2 <= r * r) return p;   // suy biến (điểm trong/trên đường tròn) — không nên xảy ra thực tế
            double rd2 = (r * r) / d2;
            double k = r * Math.Sqrt(d2 - r * r) / d2;
            var perp = new Vector2d(-e.Y, e.X);
            Point2d t1 = c + e * rd2 + perp * k;
            Point2d t2 = c + e * rd2 - perp * k;
            double dot1 = (t1 - c).DotProduct(refN), dot2 = (t2 - c).DotProduct(refN);
            return dot1 >= dot2 ? t1 : t2;
        }

        /// <summary>Bulge cung từ ps→pe quanh tâm c (lấy cung nhỏ, dấu theo chiều quét).</summary>
        private static double ArcBulge(Point2d c, Point2d ps, Point2d pe)
        {
            double a0 = Math.Atan2(ps.Y - c.Y, ps.X - c.X);
            double a1 = Math.Atan2(pe.Y - c.Y, pe.X - c.X);
            double sweep = a1 - a0;
            while (sweep <= -Math.PI) sweep += 2.0 * Math.PI;
            while (sweep > Math.PI) sweep -= 2.0 * Math.PI;
            return Math.Tan(sweep / 4.0);
        }

        /// <summary>Fallback hình học suy biến: knee sắc cạnh (không bo toe).</summary>
        private static List<VertexBulge> SharpOutline(double S, double H, double F, double dtop, double ledge)
        {
            return new List<VertexBulge>(6)
            {
                new VertexBulge(0, 0, 0),
                new VertexBulge(S, 0, 0),
                new VertexBulge(S, -dtop, 0),
                new VertexBulge(S - ledge, -dtop, 0),
                new VertexBulge(F, -H, 0),
                new VertexBulge(0, -H, 0),
            };
        }
    }
}
