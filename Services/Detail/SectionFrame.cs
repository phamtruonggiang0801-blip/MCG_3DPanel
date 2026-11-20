using System;

namespace MCG_3DPanel.Services.Detail
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.*.

    /// <summary>Khung mặt cắt: đường cắt đã cắt theo vùng + hướng nhìn.</summary>
    public sealed class SectionFrame
    {
        public P2 Start, End;     // đầu mút đường cắt (toạ độ plan) sau khi cắt theo vùng
        public P2 Right;          // đơn vị, bên phải người nhìn = Look × Z
        public P2 Look;           // đơn vị, từ đường cắt về phía điểm bên
        public double Length, Depth;
        public bool Vertical;     // đường cắt dọc trên plan ⇒ "90°ROTATED"

        const double SideTol = 1.0;   // mm
        const double Eps = 1e-6;

        /// <summary>Bắt ortho: giữ trục có |delta| lớn hơn, trục kia về 0 (bằng nhau ⇒ ngang).</summary>
        public static P2 Snap(P2 start, P2 raw)
        {
            double dx = raw.X - start.X, dy = raw.Y - start.Y;
            return Math.Abs(dy) > Math.Abs(dx) ? new P2(start.X, raw.Y) : new P2(raw.X, start.Y);
        }

        /// <summary><paramref name="maxDepth"/> &gt; 0 = chiều sâu người dùng vẽ (khung chữ nhật Section, kỹ sư 2026-11-18), vẫn chặn ở
        /// biên vùng; 0 = tới biên vùng (bản ghi cũ).</summary>
        public static SectionFrame From(P2 a, P2 b, P2 side, DetailRegion region, double maxDepth = 0)
        {
            var d = b - a;
            double len = d.Length;
            if (len < Eps) return null;
            var dir = d * (1.0 / len);
            var n = new P2(-dir.Y, dir.X);
            double off = (side - a).Dot(n);
            if (Math.Abs(off) <= SideTol) return null;
            var look = off > 0 ? n : n * -1.0;
            var right = new P2(look.Y, -look.X);

            if (!Clip(a, d, region, out double t0, out double t1)) return null;
            var p0 = a + d * t0; var p1 = a + d * t1;
            if ((p1 - p0).Length < Eps) return null;
            bool swap = p1.Dot(right) < p0.Dot(right);
            var start = swap ? p1 : p0; var end = swap ? p0 : p1;

            double depth = 0;
            foreach (var p in region.Outline(72))
                depth = Math.Max(depth, (p - start).Dot(look));
            if (maxDepth > 0) depth = Math.Min(depth, maxDepth);

            return new SectionFrame
            {
                Start = start, End = end, Right = right, Look = look,
                Length = (end - start).Length, Depth = depth,
                Vertical = Math.Abs(d.Y) > Math.Abs(d.X)
            };
        }

        /// <summary>x = dọc Right từ đầu trái, y = z.</summary>
        public P2 ToSection(P2 planPt, double z) => new P2((planPt - Start).Dot(Right), z);

        // Cắt đoạn a + t·d (t∈[0,1]) theo vùng; false nếu rỗng.
        static bool Clip(P2 a, P2 d, DetailRegion r, out double t0, out double t1)
        {
            t0 = 0; t1 = 1;
            if (r.IsCircle)
            {
                var f = a - r.Center;
                double A = d.Dot(d), B = 2 * f.Dot(d), C = f.Dot(f) - r.Radius * r.Radius;
                double disc = B * B - 4 * A * C;
                if (disc < 0) return false;
                double s = Math.Sqrt(disc);
                double u0 = (-B - s) / (2 * A), u1 = (-B + s) / (2 * A);
                t0 = Math.Max(t0, u0); t1 = Math.Min(t1, u1);
                return t1 - t0 > 0;
            }
            // Cyrus–Beck trên đa giác lồi CCW
            for (int i = 0; i < 4; i++)
            {
                var ea = r.Corners[i]; var e = r.Corners[(i + 1) % 4] - ea;
                var inward = new P2(-e.Y, e.X);
                double num = (a - ea).Dot(inward), den = d.Dot(inward);
                if (Math.Abs(den) < 1e-12) { if (num < 0) return false; continue; }
                double t = -num / den;
                if (den > 0) t0 = Math.Max(t0, t); else t1 = Math.Min(t1, t);
                if (t1 < t0) return false;
            }
            return t1 - t0 > 0;
        }
    }
}
