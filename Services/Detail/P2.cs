using System;

namespace MCG_3DPanel.Services.Detail
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.*.

    /// <summary>Điểm/vector 2D bất biến.</summary>
    public readonly struct P2
    {
        public readonly double X, Y;
        public P2(double x, double y) { X = x; Y = y; }
        public static P2 operator +(P2 a, P2 b) => new P2(a.X + b.X, a.Y + b.Y);
        public static P2 operator -(P2 a, P2 b) => new P2(a.X - b.X, a.Y - b.Y);
        public static P2 operator *(P2 a, double k) => new P2(a.X * k, a.Y * k);
        public double Dot(P2 o) => X * o.X + Y * o.Y;
        public double Length => Math.Sqrt(X * X + Y * Y);
    }
}
