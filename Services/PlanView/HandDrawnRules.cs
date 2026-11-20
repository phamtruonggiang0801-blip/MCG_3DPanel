using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MCG_3DPanel.Services.PlanView
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>1 đối tượng Model Space có sẵn trong khung tờ (writer đọc ra). Type "Text" | "MText" | "Dim" | "BlockRef" | "Line" | "Polyline" | "Leader".| "MText" | "Dim" | "BlockRef".
    /// Dim: (X1,Y1)-(X2,Y2) = 2 điểm đo, Rotation = hướng đo, Text = chuỗi ghi đè. InModelSpace = false với thứ nằm trong cây _Assy.</summary>
    public sealed class PvExisting
    {
        public string Type { get; set; }
        public string Layer { get; set; }
        public string Text { get; set; }
        public string BlockName { get; set; }
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
        public double Rotation { get; set; }
        public bool HasOwnMark { get; set; }
        /// <summary>Mang XData MCG3D_DETAIL (biên / nhãn DET.n / tiêu đề / block Detail) ⇒ không bao giờ là phần vẽ tay.</summary>
        public bool HasDetailMark { get; set; }
        public bool InModelSpace { get; set; }
    }

    /// <summary>Phần vẽ tay mà Replace được xoá = đúng thứ tool sẽ vẽ lại (spec quyết định 6, lựa chọn của tôi). Giữ: DETAIL n, 1A-1A,
    /// REF. DIM, kích thước tham khảo "(&lt;&gt;)", kích thước tổng, kích thước vẽ tay trên mặt cắt.</summary>
    public static class HandDrawnRules
    {
        private static readonly Regex Label = new Regex(@"^((T|L|HT|HL)\d+[a-z]?|BS|B|S|CG|GB|(OB|GE|GB)\w*)$", RegexOptions.CultureInvariant);
        private static readonly string[] SymbolBlocks =
            { "CS_horl_up_right", "CS_symbol", "thk_sym", "M.D1", "M.D2", "Rec. for tolerance", "Measurements_Tolerances vFuture" };
        private const double Tol = 1.0;

        public static bool IsOwn(PvExisting e) => e.HasOwnMark;

        /// <param name="known">Khoảng kích thước tool sẽ vẽ, toạ độ tờ: (hướng đo, đầu, cuối) chiếu lên hướng đo.</param>
        /// <param name="plate">Bao Top Plate của mặt bằng trên tờ (kích thước bao dài/rộng).</param>
        public static bool IsReplaceable(PvExisting e, string panel, IReadOnlyList<(double Rotation, double Lo, double Hi)> known,
            (double MinX, double MinY, double MaxX, double MaxY) plate)
        {
            if (e.HasOwnMark || e.HasDetailMark || !e.InModelSpace) return false;
            switch (e.Type)
            {
                case "Text":
                case "MText":
                    string t = (e.Text ?? "").Trim();
                    if (e.Layer == "Mechanical-AM_1")
                        return Label.IsMatch(t) || t.StartsWith("PLAN VIEW", StringComparison.Ordinal) || t.StartsWith("LASHING HOLES PLAN", StringComparison.Ordinal);
                    if (e.Layer == "Mechanical-AM_6")
                        return Label.IsMatch(t) || t.StartsWith("TOP PLATE THICKNESS", StringComparison.Ordinal);
                    return false;

                case "Dim":
                    if (e.Layer != "Mechanical-AM_4" && e.Layer != "Mechanical-AM_5") return false;
                    if ((e.Text ?? "").Contains("(")) return false;                         // kích thước tham khảo
                    double ux = Math.Cos(e.Rotation), uy = Math.Sin(e.Rotation);
                    double a = e.X1 * ux + e.Y1 * uy, b = e.X2 * ux + e.Y2 * uy;
                    double lo = Math.Min(a, b), hi = Math.Max(a, b);
                    foreach (var k in known)
                        if (SameAxis(k.Rotation, e.Rotation) && Math.Abs(k.Lo - lo) < Tol && Math.Abs(k.Hi - hi) < Tol) return true;
                    bool horiz = Math.Abs(uy) < 0.01, vert = Math.Abs(ux) < 0.01;
                    if (horiz && Math.Abs(hi - lo - (plate.MaxX - plate.MinX)) < Tol) return true;   // dài bao
                    if (vert && Math.Abs(hi - lo - (plate.MaxY - plate.MinY)) < Tol) return true;    // rộng bao
                    return false;

                case "Line":
                case "Polyline":
                    return e.Layer == "Mechanical-AM_7" || e.Layer == "Mechanical-AM_8";   // đường chéo đo kiểm, khung member list

                case "Leader":
                    return e.Layer == "posb";                                              // leader M.D1/M.D2 → đường chéo

                case "BlockRef":
                    string n = e.BlockName ?? "";
                    if (Array.IndexOf(SymbolBlocks, n) >= 0) return true;
                    return n == panel + "_X-X" || n == panel + "_Y-Y" || n == "T." + panel + "_X-X" || n == "T." + panel + "_Y-Y"
                           || n == panel + "_Top Plate" || n == panel + "_Lashing Hole";
            }
            return false;
        }

        private static bool SameAxis(double r1, double r2) => Math.Abs(Math.IEEERemainder(r1 - r2, Math.PI)) < 0.01;
    }
}
