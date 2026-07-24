using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.Geometry;

namespace MCG_3DPanel.Services
{
    /// <summary>
    /// Sinh biên dạng (elevation) WEB của GIRDER END (GE) — PARAMETRIC (kiểm chứng khớp block GE.dwg
    /// GE1/GE2/GE5/GE6 từng đỉnh). Hệ local: gốc (0,0)=Web∩TopPlate, +X ra HP/FB, −Y xuống.
    ///
    /// Chuỗi (từ span-face xuống flange):
    ///   (0,0)→(L,0) top edge → (L,−Dend) span-face → ledge → **R50** → free-edge → toe đáy → (0,−H) web.
    ///   Góc P = (L − 50, −Dend) [giao free-edge ∩ ledge] — "đoạn 50" = span-face cách P đúng 50.
    ///   Toe đáy: KHÔNG flange → **R30** (như Bracket); CÓ flange → SẮC + flange 50×10 dựng riêng (builder).
    ///   Dend = member − 15 (lưng) / − 30 (bụng). R30/R50/50 hằng số. Xem skill girder-end.
    /// </summary>
    public interface IGeProfileService
    {
        /// <summary>Outline web GE (điểm 2D local + bulge), kín. flanged → toe đáy sắc (không R30).</summary>
        List<VertexBulge> GeOutline(bool flanged, double L, double H, double Lb, double dend);

        /// <summary>Đoạn free-edge (mép dưới, để dựng flange 50×10): tiếp điểm trên (gần HP) → toe đáy.</summary>
        void FreeEdge(bool flanged, double L, double H, double Lb, double dend, out Point2d top, out Point2d bot);
    }

    public class GeProfileService : IGeProfileService
    {
        private const double R30 = 30.0, R50 = 50.0, Doan = 50.0;

        /// <summary>Các điểm chốt của biên dạng GE (dùng chung cho outline + flange).</summary>
        private struct Geo
        {
            public Point2d P;           // góc free-edge ∩ ledge = (L−50, −Dend)
            public Point2d TanLedge;    // R50 tiếp tuyến ledge
            public Point2d TanFree;     // R50 tiếp tuyến free-edge
            public Point2d R50c;        // tâm R50
            public Point2d BotTangent;  // free-edge kết ở đây (T nếu R30, hoặc toe sắc)
            public Point2d FlangeToe;   // (Lb, −H)
            public Point2d Cb;          // tâm R30 (chỉ non-flanged)
            public double BR50, BR30;
            public bool Flanged;
        }

        private static Geo Solve(bool flanged, double L, double H, double Lb, double dend)
        {
            var g = new Geo { Flanged = flanged };
            g.P = new Point2d(L - Doan, -dend);
            g.FlangeToe = new Point2d(Lb, -H);

            // Hướng free-edge từ P xuống dưới (u) + điểm kết dưới.
            Vector2d u;
            if (flanged)
            {
                u = (g.FlangeToe - g.P).GetNormal();     // free-edge tới toe sắc
                g.BotTangent = g.FlangeToe;
            }
            else
            {
                g.Cb = new Point2d(Lb + R30, -H);        // tâm R30 (như Bracket, gặp flange-leg ⊥)
                u = TangentDir(g.P, g.Cb, R30);          // free-edge tiếp tuyến R30 (nhánh xuống dưới)
                double dcb = (g.Cb - g.P).Length;
                double ltan = Math.Sqrt(Math.Max(0, dcb * dcb - R30 * R30));
                g.BotTangent = g.P + u * ltan;           // tiếp điểm R30 trên free-edge
                g.BR30 = ArcBulge(g.Cb, g.BotTangent, g.FlangeToe);
            }

            // R50 fillet tại P giữa ledge(+X) và free-edge(u).
            var d1 = new Vector2d(1, 0);
            double cosT = d1.DotProduct(u);
            double half = Math.Acos(Math.Max(-1, Math.Min(1, cosT))) / 2.0;
            double off = R50 / Math.Tan(half);
            g.TanLedge = g.P + d1 * off;
            g.TanFree = g.P + u * off;
            Vector2d bis = (d1 + u).GetNormal();
            g.R50c = g.P + bis * (R50 / Math.Sin(half));
            g.BR50 = ArcBulge(g.R50c, g.TanLedge, g.TanFree);
            return g;
        }

        public List<VertexBulge> GeOutline(bool flanged, double L, double H, double Lb, double dend)
        {
            if (L <= Doan + 1 || H <= dend + 1 || Lb < 0)
                return Rect(L, H);
            Geo g = Solve(flanged, L, H, Lb, dend);

            var o = new List<VertexBulge>(9)
            {
                new VertexBulge(0, 0, 0),
                new VertexBulge(L, 0, 0),                                   // top edge → span-face
                new VertexBulge(L, -dend, 0),                              // span-face → ledge
                new VertexBulge(g.TanLedge.X, g.TanLedge.Y, g.BR50),       // ledge → R50
                new VertexBulge(g.TanFree.X, g.TanFree.Y, 0),             // R50 → free-edge
            };
            if (flanged)
                o.Add(new VertexBulge(g.FlangeToe.X, g.FlangeToe.Y, 0));   // free-edge → toe SẮC
            else
            {
                o.Add(new VertexBulge(g.BotTangent.X, g.BotTangent.Y, g.BR30));  // free-edge → R30
                o.Add(new VertexBulge(g.FlangeToe.X, g.FlangeToe.Y, 0));         // R30 → flange toe
            }
            o.Add(new VertexBulge(0, -H, 0));                              // flange leg → web (tự đóng)
            return o;
        }

        public void FreeEdge(bool flanged, double L, double H, double Lb, double dend, out Point2d top, out Point2d bot)
        {
            Geo g = Solve(flanged, L, H, Lb, dend);
            top = g.TanFree; bot = g.BotTangent;
        }

        /// <summary>Hướng tiếp tuyến (đơn vị) từ điểm NGOÀI P tới đường tròn (C,r); chọn nhánh đi XUỐNG (về flange).</summary>
        private static Vector2d TangentDir(Point2d p, Point2d c, double r)
        {
            Vector2d w = c - p; double dd = w.Length;
            if (dd <= r) return (c - p).GetNormal();
            double cb = Math.Sqrt(Math.Max(0, dd * dd - r * r)) / dd, sb = r / dd;
            Vector2d wn = w / dd;
            // xoay wn góc −beta (nhánh xuống dưới, khớp block)
            return new Vector2d(wn.X * cb + wn.Y * sb, -wn.X * sb + wn.Y * cb);
        }

        private static double ArcBulge(Point2d c, Point2d ps, Point2d pe)
        {
            double a0 = Math.Atan2(ps.Y - c.Y, ps.X - c.X);
            double a1 = Math.Atan2(pe.Y - c.Y, pe.X - c.X);
            double s = a1 - a0;
            while (s <= -Math.PI) s += 2 * Math.PI;
            while (s > Math.PI) s -= 2 * Math.PI;
            return Math.Tan(s / 4.0);
        }

        private static List<VertexBulge> Rect(double L, double H)
        {
            return new List<VertexBulge>(4)
            {
                new VertexBulge(0, 0, 0), new VertexBulge(L, 0, 0),
                new VertexBulge(L, -H, 0), new VertexBulge(0, -H, 0),
            };
        }
    }
}
