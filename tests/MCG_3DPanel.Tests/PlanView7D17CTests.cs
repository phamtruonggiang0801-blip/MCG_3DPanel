using System;
using System.Collections.Generic;
using System.Linq;
using MCG_3DPanel.Services.PlanView;
using Xunit;

namespace MCG_3DPanel.Tests
{
    /// <summary>Tờ tay 7D-17C (CAS-0068762, 1:75) = chuẩn công ty (kỹ sư 2026-11-10). Tấm KHÔNG chữ nhật: góc dưới-trái cắt xiên,
    /// tai trái 15.35 → −444.93, vấu đáy-phải tới −22871.6, đáy 2 bậc −22460.5 / −22465.5. Hệ block (gốc _Assy 12330.9, 36104.7).</summary>
    public class PlanView7D17CTests
    {
        private const int Scale = 75;

        internal static readonly List<(double, double)> Outline = new List<(double, double)>
        {
            (15.35, -197.45), (15.35, 0), (15368.35, 0), (15368.8, -110.15), (15398.8, -140.15), (15588.8, -140.15), (15618.8, -110.15), (15618.35, 0),
            (15940.35, 0), (15940.35, -20487.46), (15810.16, -20487.46), (15780.16, -20517.46), (15780.16, -22407.03), (15675.16, -22407.03),
            (15645.16, -22437.03), (15645.35, -22871.61), (15155.35, -22871.61), (15155.16, -22490.46), (15125.16, -22460.46), (12951.19, -22460.46),
            (12911.21, -22450.46), (12611.21, -22450.46), (12551.09, -22465.45), (10805.21, -22465.45), (10418.51, -22250.22), (10393.01, -22236.03),
            (9991.42, -22235.41), (9991.42, -21852.39), (10320.19, -21853.01), (10350.19, -21823.01), (10350.16, -21253.26), (10323.45, -21142.05),
            (9837.26, -20187.92), (9796.92, -20174.81), (9530.99, -20310.29), (9490.64, -20297.17), (9272.79, -19869.53), (9285.9, -19829.18),
            (9551.77, -19693.74), (9564.88, -19653.38), (9492.37, -19511.08), (9675.53, -19417.74), (9688.64, -19377.39), (9597.82, -19199.21),
            (9557.47, -19186.11), (9374.33, -19279.43), (5038.72, -10770.95), (5213.62, -10681.76), (5226.72, -10641.4), (5203.97, -10596.79),
            (5163.61, -10583.69), (4988.74, -10672.87), (4723.01, -10151.36), (4897.72, -10062.27), (4910.81, -10021.92), (4888.12, -9977.41),
            (4847.76, -9964.31), (4673.08, -10053.39), (1207.65, -3252.59), (1390.72, -3159.29), (1403.83, -3118.95), (1313.06, -2940.73),
            (1272.71, -2927.61), (1089.56, -3020.86), (59.22, -998.84), (32.49, -982.46), (-414.93, -982.46), (-444.93, -952.46), (-444.93, -643.45),
            (-14.65, -643.45), (15.35, -613.45), (15.35, -369.45), (162.43, -369.45), (192.43, -339.45), (192.43, -227.45), (162.43, -197.45),
        };

        internal static PlanViewInput Input()
        {
            var i = new PlanViewInput { Panel = "7D-17C", TopPlate = (-444.93, -22871.61, 15940.35, 0), PlateThickness = 6, HasLashingHoleBlock = true,
                                        LashingBlockOffset = (3170.3582, -10522.7332) };
            i.PlateOutline.AddRange(Outline);
            return i;
        }

        internal static SheetContext Sheet() => new SheetContext
        {
            Scale = Scale, FrameX = 0, FrameY = 0,
            PlanMinX = 12330.8952 - 444.93, PlanMinY = 36104.6524 - 22871.61, PlanMaxX = 12330.8952 + 15940.35, PlanMaxY = 36104.6524,
            AssyTransform = new[] { 1.0, 0, 0, 1, 12330.8952, 36104.6524 },
        };

        [Fact] public void MainEdges_IgnoreEarsAndLugs()
        {
            var e = PlateGeometry.MainEdges(Outline);
            Assert.Equal(15.35, e.Left.At, 2);          // tai trái −444.93 bị bỏ
            Assert.Equal(15940.35, e.Right.At, 2);
            Assert.Equal(-22465.45, e.Bottom.At, 2);    // 2 bậc đáy gộp, lấy bậc ngoài; vấu −22871.6 bị bỏ
            Assert.Equal(0, e.Top.At, 2);
            Assert.True(e.LengthOnTop);                 // cạnh dưới gãy ⇒ kích thước dài phía TRÊN (tờ tay)
            Assert.True(e.WidthOnRight);                // cạnh trái gãy ⇒ kích thước rộng bên PHẢI (tờ tay)
        }

        [Fact] public void Overall_TopAndRight_AsManual()
        {
            var o = DraftDimRules.Overall(Input(), Scale);
            var len = o.Single(d => d.Rotation == 0);
            Assert.Equal(16385.3, Math.Abs(len.X2 - len.X1), 1);                       // tờ tay 16385.3
            Assert.Equal(PlanViewLayout.OverallGapH * Scale, len.LineY, 3);           // trên mép trên (37.4 mm)
            Assert.Equal(-643.45, len.Y1, 2);                                          // đỉnh tai trái gần phía trên nhất
            var wid = o.Single(d => d.Rotation != 0);
            Assert.Equal(22465.45, Math.Abs(wid.Y2 - wid.Y1), 2);                     // tờ tay 22465.5 (không phải khung bao 22871.6)
            Assert.Equal(15940.35 + PlanViewLayout.OverallGapV * Scale, wid.LineX, 3); // bên phải (49.4 mm)
        }

        [Fact] public void Sections_SpanMainEdges()
        {
            var x = SectionRules.Build(Input(), 'X', Sheet());
            Assert.Equal(15925.0, x.Plates[0].X1 - x.Plates[0].X0, 2);               // tờ tay X-X: 15925
            Assert.Equal(12330.8952 + 15.35, x.X, 2);                                 // tờ tay chèn ở 164.6 mm
            Assert.Equal(36104.6524 + 56.0 * Scale, x.Y, 2);                          // tờ tay 537.4 mm
            var y = SectionRules.Build(Input(), 'Y', Sheet());
            Assert.Equal(22465.45, y.Plates[0].X1 - y.Plates[0].X0, 2);              // tờ tay Y-Y: 22465.5
            Assert.Equal(68.8, SectionRules.YLeft(Input()), 6);                       // kích thước rộng bên phải ⇒ không dời
        }

        [Fact] public void LashingPlan_InsertionAndNestedOffset()
        {
            var ins = SheetItemRules.LashingPlan(Input(), Sheet());
            var tp = ins.Single(x => x.Block == "7D-17C_Top Plate");
            var lh = ins.Single(x => x.Block == "7D-17C_Lashing Hole");
            Assert.InRange(tp.X / Scale, 468.8 - 1, 468.8 + 1);                       // tờ tay: chèn Top Plate tại 468.8 mm
            Assert.Equal(tp.X + 3170.3582, lh.X, 3);                                  // tờ tay: 38328.4 − 35158.0
            Assert.Equal(tp.Y - 10522.7332, lh.Y, 3);
        }

        [Fact] public void Diagonals_OnMainEdgeRectangle()
        {
            var (lines, _) = SheetItemRules.Diagonals(Input(), Sheet());
            var (ix, iy) = SheetItemRules.LashingInsertion(Input(), Sheet());
            var xs = lines.SelectMany(l => new[] { l.X1 - ix, l.X2 - ix }).ToList();
            var ys = lines.SelectMany(l => new[] { l.Y1 - iy, l.Y2 - iy }).ToList();
            Assert.Equal(15.35, xs.Min(), 2); Assert.Equal(15940.35, xs.Max(), 2);    // tờ tay 469.0 / 681.3 mm
            Assert.Equal(-22465.45, ys.Min(), 2); Assert.Equal(0, ys.Max(), 2);
        }

        [Fact] public void Titles_AsManual()
        {
            var t = SheetItemRules.PlanTitle(Input(), Sheet());
            Assert.Equal(140.3, t.Y / Scale, 1);                                      // tờ tay 140.3 mm (36.6 dưới tấm)
            var l = SheetItemRules.LashingTitle(Input(), Sheet());
            Assert.Equal(123.9, l.Y / Scale, 0);                                      // tờ tay 123.9 mm
        }

        /// <summary>Chuỗi dọc lỗ lashing bên PHẢI (cạnh trái gãy), lấy lỗ ngoài cùng phải; layer AM_5; đầu chưa tới cạnh chính ⇒ "(&lt;&gt;)".</summary>
        [Fact] public void Lashing_RightChain_RightmostHole_AM5()
        {
            var i = Input();
            i.Lashing = new PvLashing { Diameter = 55, X = 15000, Y = -500 };
            i.Lashing.Holes.Add((14000, -700)); i.Lashing.Holes.Add((15000, -700)); i.Lashing.Holes.Add((15000, -1400));
            i.Drafts.Add(new PvDraft { Source = "LashingHole", X1 = 15000, Y1 = -1400, X2 = 15000, Y2 = -700, Rotation = Math.PI / 2 });
            var dims = DraftDimRules.Lashing(i, Scale, (0, 0));
            var v = dims.Single(d => d.Kind != "Diametric" && Math.Abs(Math.Min(d.Y1, d.Y2) + 1400) < 1e-6 && Math.Abs(Math.Max(d.Y1, d.Y2) + 700) < 1e-6);
            Assert.Equal(15940.35 + PlanViewLayout.LashChainSide * Scale, v.LineX, 3);
            Assert.Equal(15000, v.X1, 3);
            Assert.All(dims, d => Assert.Equal("Mechanical-AM_5", d.Layer));
            Assert.Contains(dims, d => d.TextOverride == "(<>)" && Math.Abs(Math.Min(d.Y1, d.Y2) + 22465.45) < 1e-6);   // tới cạnh chính dưới
            Assert.Contains(dims, d => d.TextOverride == "(<>)" && Math.Abs(Math.Max(d.Y1, d.Y2)) < 1e-6);              // tới cạnh trên
        }
    }

    /// <summary>CS_symbol + chữ CG (Continue Girder) tại girder liên tục qua girder; gộp điểm chuỗi trùng.</summary>
    public class PlanView7D17CSymbolTests
    {
        private static PvMember W(int line, double x0, double y0, double x1, double y1)
            => new PvMember { Line = line, Name = "g" + line, Kind = "Web", Dir = Math.Abs(x1 - x0) >= Math.Abs(y1 - y0) ? "L" : "T",
                              X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Thickness = 6, Height = 374 };

        /// <summary>2 girder L (y 0 và 937.5) chạy liên tục qua 1 girder T đứt — tờ 17C: CG trên L dưới, dưới L trên.</summary>
        [Fact] public void ContinueGirder_TowardNearestParallelGirder()
        {
            var i = new PlanViewInput { Panel = "P", TopPlate = (0, -5000, 10000, 5000) };
            i.Members.Add(W(1, 0, 0, 10000, 0));
            i.Members.Add(W(2, 0, 937.5, 10000, 937.5));
            i.Members.Add(W(3, 5000, -5000, 5000, -3)); i.Members.Add(W(3, 5000, 3, 5000, 934.5)); i.Members.Add(W(3, 5000, 940.5, 5000, 5000));
            var cs = SymbolRules.Continuous(i);
            Assert.Equal(2, cs.Count);
            Assert.All(cs, c => { Assert.Equal("CS_symbol", c.Block); Assert.Equal("Mechanical-AM_6", c.Layer); Assert.Equal(0, c.Rotation, 6); });
            var cg = SymbolRules.ContinueGirder(i, 75);
            Assert.Equal(2, cg.Count);
            Assert.All(cg, t => { Assert.Equal("CG", t.Text); Assert.Equal("Mechanical-AM_6", t.Layer); Assert.Equal(3, t.PaperHeight); });
            Assert.Contains(cg, t => Math.Abs(t.Y - PlanViewLayout.CgSide * 75) < 1e-6);            // L dưới: ghi phía trên
            Assert.Contains(cg, t => Math.Abs(t.Y - (937.5 - PlanViewLayout.CgSide * 75)) < 1e-6);  // L trên: ghi phía dưới
            Assert.All(cg, t => Assert.Equal(5000 + PlanViewLayout.CgAlong * 75, t.X, 6));
        }


        /// <summary>Tờ 17C: web girder L2 tách 2 đoạn ngay tại web T4 nhưng FLANGE L2 liền ⇒ vẫn liên tục (1 CS + 1 CG); T tách 2 đoạn lệch 7.5 mm ⇒ không nhân đôi.</summary>
        [Fact] public void ContinueGirder_SplitPiecesAtWeb_StillContinuous_NoDuplicate()
        {
            var i = new PlanViewInput { Panel = "P", TopPlate = (0, -5000, 10000, 5000) };
            i.Members.Add(W(1, 0, 0, 4997, 0)); i.Members.Add(W(1, 5003, 0, 10000, 0));                       // web L tách tại T
            i.Members.Add(new PvMember { Line = 1, Kind = "Flange", Dir = "L", X0 = 0, Y0 = 0, X1 = 10000, Y1 = 0, Thickness = 150, Height = 20 });   // flange L liền
            i.Members.Add(W(2, 0, 937.5, 10000, 937.5));
            i.Members.Add(W(3, 5000, -5000, 5000, -3)); i.Members.Add(W(3, 5000, 3, 5000, 934.5));
            i.Members.Add(W(3, 5007.5, 940.5, 5007.5, 5000)); i.Members.Add(W(5, 5000, 940.5, 5000, 3000));  // đoạn lệch 7.5 mm
            var cs = SymbolRules.Continuous(i);
            Assert.Equal(2, cs.Count(c => c.Rotation == 0));
            Assert.Single(cs, c => Math.Abs(c.Y) < 1e-6);
            Assert.Single(cs, c => Math.Abs(c.Y - 937.5) < 1e-6);
            Assert.Equal(2, SymbolRules.ContinueGirder(i, 75).Count);
        }

        /// <summary>Tờ 17C: 2 đoạn web cùng tuyến lệch 5 mm (vát) ⇒ trước đây sinh kích thước rác 5.0 trong chuỗi girder.</summary>
        [Fact] public void Section_ChainPointsWithinOneMillimetrePaper_Merged()
        {
            var i = new PlanViewInput { Panel = "P", TopPlate = (0, 0, 10000, 8000), PlateThickness = 6 };
            i.Members.Add(W(1, 0, 1000, 10000, 1000));
            i.Members.Add(W(2, 0, 4000, 5000, 4000)); i.Members.Add(W(3, 5000, 4005, 10000, 4005));   // 2 tuyến lệch 5 mm
            i.Members.Add(W(4, 0, 7000, 10000, 7000));
            var s = SectionRules.Build(i, 'Y', new SheetContext { Scale = 75, PlanMinX = 0, PlanMinY = 0, PlanMaxX = 10000, PlanMaxY = 8000,
                                                                   AssyTransform = new[] { 1.0, 0, 0, 1, 0, 0 } });
            Assert.DoesNotContain(s.Dims, d => Math.Abs(Math.Abs(d.Y2 - d.Y1) - 5) < 0.01);
        }
    }
}
