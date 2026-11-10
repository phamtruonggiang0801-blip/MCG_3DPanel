using System;
using System.Collections.Generic;
using System.Linq;

namespace MCG_3DPanel.Services.PlanView
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>Kích thước theo tờ mẫu 7D-17P (so sánh 2026-11-10):
    /// • mặt bằng chính CHỈ có 2 kích thước bao (dài dưới, rộng trái, chữ ±4 TOP PLATE);
    /// • nháp Lashing Hole chỉ ghi trên mặt bằng lỗ lashing: GIỮ điểm đo của nháp, đường kích thước ra chuỗi trái / trên tấm;
    ///   nháp có 1 đầu nằm trên đường bao tấm ⇒ AM_5, còn lại AM_4; + Ø lỗ lashing;
    /// • nháp Structure ghi trên mặt cắt — xem <see cref="SectionRules"/>.</summary>
    public static class DraftDimRules
    {
        public const string LayerLashing = "Mechanical-AM_4";
        public const string LayerStructure = "Mechanical-AM_5";
        public const string OverallText = "<>{}{\\H0.71x;\\C3;%%p4}{{}}TOP PLATE";
        public const string LashingText = "<>-LASHING HOLES";

        /// <summary>Kích thước bao trên mặt bằng chính (hệ block). Đường bao rỗng ⇒ góc khung bao.</summary>
        public static List<PvDim> Overall(PlanViewInput input, int scale)
        {
            var tp = input.TopPlate;
            var o = Outline(input);
            var (l1, l2) = PlateGeometry.OverallLength(o);
            var (w1, w2) = PlateGeometry.OverallWidth(o);
            return new List<PvDim>
            {
                new PvDim
                {
                    Space = PvSpace.Block, X1 = l1.Item1, Y1 = l1.Item2, X2 = l2.Item1, Y2 = l2.Item2,
                    LineX = l2.Item1, LineY = tp.MinY - PlanViewLayout.OverallBelow * scale,
                    Rotation = 0, Layer = LayerStructure, TextOverride = OverallText, Tmove = false,
                },
                new PvDim
                {
                    Space = PvSpace.Block, X1 = w1.Item1, Y1 = w1.Item2, X2 = w2.Item1, Y2 = w2.Item2,
                    LineX = tp.MinX - PlanViewLayout.OverallLeft * scale, LineY = w2.Item2,
                    Rotation = Math.PI / 2, Layer = LayerStructure, TextOverride = OverallText, Tmove = false,
                },
            };
        }

        /// <summary>Kích thước mặt bằng lỗ lashing (hệ block, dời <paramref name="shift"/> = độ lệch giữa lần chèn thứ 2 và _Assy).</summary>
        public static List<PvDim> Lashing(PlanViewInput input, int scale, (double dx, double dy) shift)
        {
            var tp = input.TopPlate;
            var o = Outline(input);
            var r = new List<PvDim>();
            foreach (var d in input.Drafts.Where(x => x.Source == "LashingHole"))
            {
                bool horiz = Math.Abs(Math.Sin(d.Rotation)) < 0.01, vert = Math.Abs(Math.Cos(d.Rotation)) < 0.01;
                if (!horiz && !vert) continue;
                bool edge = PlateGeometry.OnOutline(o, d.X1, d.Y1) || PlateGeometry.OnOutline(o, d.X2, d.Y2);
                r.Add(new PvDim
                {
                    Space = PvSpace.Block,
                    X1 = d.X1 + shift.dx, Y1 = d.Y1 + shift.dy, X2 = d.X2 + shift.dx, Y2 = d.Y2 + shift.dy,
                    LineX = (horiz ? d.X2 : tp.MinX - PlanViewLayout.LashChainLeft * scale) + shift.dx,
                    LineY = (horiz ? tp.MaxY + PlanViewLayout.LashChainTop * scale : d.Y2) + shift.dy,
                    Rotation = horiz ? 0 : Math.PI / 2,
                    Layer = edge ? LayerStructure : LayerLashing,
                    TextOverride = string.IsNullOrEmpty(d.Text) ? null : d.Text, Tmove = d.Tmove, Oblique = d.Oblique,
                });
            }
            if (input.Lashing != null)
            {
                var h = input.Lashing; double rad = h.Diameter / 2;
                r.Add(new PvDim
                {
                    Space = PvSpace.Block, Kind = "Diametric",
                    X1 = h.X + rad + shift.dx, Y1 = h.Y + shift.dy, X2 = h.X - rad + shift.dx, Y2 = h.Y + shift.dy,
                    Layer = LayerStructure, DimStyle = "AM_ISO$3", TextOverride = LashingText, Tmove = false,
                });
            }
            return r;
        }

        internal static IReadOnlyList<(double X, double Y)> Outline(PlanViewInput input)
        {
            if (input.PlateOutline.Count >= 3) return input.PlateOutline;
            var tp = input.TopPlate;
            return new List<(double X, double Y)> { (tp.MinX, tp.MinY), (tp.MaxX, tp.MinY), (tp.MaxX, tp.MaxY), (tp.MinX, tp.MaxY) };
        }
    }
}
