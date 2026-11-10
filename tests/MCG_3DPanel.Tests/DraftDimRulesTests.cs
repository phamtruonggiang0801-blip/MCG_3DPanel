using System;
using System.Collections.Generic;
using System.Linq;
using MCG_3DPanel.Services.PlanView;
using Xunit;

namespace MCG_3DPanel.Tests
{
    /// <summary>Kích thước theo tờ mẫu 7D-17P: mặt bằng chính chỉ 2 kích thước bao; nháp lashing chỉ trên mặt bằng lỗ lashing, giữ điểm đo,
    /// đường ra chuỗi trái (8.3) / trên (9.6); 1 đầu trên đường bao tấm ⇒ AM_5.</summary>
    public class DraftDimRulesTests
    {
        private const int Scale = 50;

        /// <summary>Đường bao kiểu 7D-17P (hệ block) — xem PlateGeometryTests.</summary>
        private static PlanViewInput Input(params PvDraft[] drafts)
        {
            var i = new PlanViewInput { Panel = "7D-17P", TopPlate = (-359.06, -12891.26, 15939.84, 500.84), PlateThickness = 6 };
            i.PlateOutline.AddRange(new List<(double, double)>
            {
                (-359.06, -12891.26), (15939.84, -12891.26), (15939.84, 61), (15939.84, 500.84), (14000, 500.84), (14000, 61),
                (264.8, 61), (264.8, 464.9), (-359.06, 464.9), (-359.06, 61), (0, 61), (0, -12500), (-359.06, -12500),
            });
            i.Drafts.AddRange(drafts);
            return i;
        }

        private static PvDraft V(double x, double y1, double y2, string src = "LashingHole")
            => new PvDraft { Source = src, X1 = x, Y1 = y1, X2 = x, Y2 = y2, Rotation = Math.PI / 2 };

        [Fact] public void Overall_7D17P_LengthAndWidthToOutlineVertices()
        {
            var o = DraftDimRules.Overall(Input(), Scale);
            var len = o.Single(d => d.Rotation == 0);
            Assert.Equal(16298.9, len.X2 - len.X1, 1);
            Assert.Equal(-12891.26 - PlanViewLayout.OverallBelow * Scale, len.LineY, 3);
            var wid = o.Single(d => d.Rotation != 0);
            Assert.Equal(13356.16, wid.Y2 - wid.Y1, 2);                 // tờ tay: 13356.2 (không phải khung bao 13392.1)
            Assert.Equal(-359.06 - PlanViewLayout.OverallLeft * Scale, wid.LineX, 3);
            Assert.All(o, d => { Assert.Equal(DraftDimRules.OverallText, d.TextOverride); Assert.Equal("Mechanical-AM_5", d.Layer); Assert.False(d.Tmove); });
        }

        /// <summary>Nháp dọc 450 của 7D-17P (lỗ x = 139.9): giữ điểm tại lỗ, đường cách mép trái tấm 8.3 mm giấy.</summary>
        [Fact] public void Lashing_KeepsDraftPoints_LeftChain_AM4()
        {
            var d = DraftDimRules.Lashing(Input(V(139.9, -12354.1, -11904.1)), Scale, (0, 0)).Single(x => x.TextOverride != "(<>)");   // + đóng chuỗi tới mép
            Assert.Equal("Mechanical-AM_4", d.Layer);
            Assert.Equal(139.9, d.X1, 3); Assert.Equal(-12354.1, d.Y1, 3); Assert.Equal(-11904.1, d.Y2, 3);
            Assert.Equal(-359.06 - PlanViewLayout.LashChainLeft * Scale, d.LineX, 3);
            Assert.Equal(Math.PI / 2, d.Rotation, 6);
        }

        [Fact] public void Lashing_Horizontal_TopChain()
        {
            var h = new PvDraft { Source = "LashingHole", X1 = 709.9, Y1 = -79, X2 = 1349.9, Y2 = -79, Rotation = 0 };
            var d = DraftDimRules.Lashing(Input(h), Scale, (0, 0)).Single(x => x.TextOverride != "(<>)");
            Assert.Equal(500.84 + PlanViewLayout.LashChainTop * Scale, d.LineY, 3);
            Assert.Equal(-79, d.Y1, 3);
        }

        [Fact] public void Lashing_EndOnPlateEdge_GoesToAM5()
        {
            var h = new PvDraft { Source = "LashingHole", X1 = 0, Y1 = -6000, X2 = 139.9, Y2 = -6000, Rotation = 0 };   // x = 0 là cạnh tấm
            Assert.Equal("Mechanical-AM_5", DraftDimRules.Lashing(Input(h), Scale, (0, 0)).Single(x => x.TextOverride != "(<>)").Layer);
        }

        /// <summary>Tờ 7D-17P: 395.5 (chồng lên nháp dài hơn cùng trục) nằm trong tấm, đường GIỮA cột lỗ của nó (15649.9) và cột lỗ kế
        /// tiếp phía trong (15009.9) = 15329.9 (tờ tay 15330.6), AM_5, "&lt;&gt;".</summary>
        [Fact] public void Lashing_LocalDraft_LineMidwayToNextHoleInward()
        {
            var longD = V(15649.9, -774.5, -79); var shortD = V(15649.9, -774.5, -379);
            var i = Input(longD, shortD);
            i.Lashing = new PvLashing { Diameter = 55 };
            foreach (var x in new[] { 139.9, 709.9, 15009.9, 15649.9 }) i.Lashing.Holes.Add((x, -774.5));
            var dims = DraftDimRules.Lashing(i, Scale, (0, 0));
            var local = dims.Single(d => d.Kind == "Rotated" && Math.Abs(Math.Abs(d.Y2 - d.Y1) - 395.5) < 0.01);
            Assert.Equal("Mechanical-AM_5", local.Layer);
            Assert.Equal(15329.9, local.LineX, 1);
            Assert.Equal("<>", local.TextOverride);
            Assert.False(local.Tmove);
            var chain = dims.Single(d => d.Kind == "Rotated" && Math.Abs(Math.Abs(d.Y2 - d.Y1) - 695.5) < 0.01);
            Assert.Equal(-359.06 - PlanViewLayout.LashChainLeft * Scale, chain.LineX, 3);
        }

        /// <summary>Tờ 7D-17P: chuỗi ngang dừng ở lỗ 15649.9 ⇒ thêm 290 "(&lt;&gt;)" tới mép phải tấm (15939.8), cùng chuỗi trên.</summary>
        [Fact] public void Lashing_ChainShortOfEdge_AddsReferenceClosingDim()
        {
            var h1 = new PvDraft { Source = "LashingHole", X1 = 139.9, Y1 = -6974.5, X2 = 709.9, Y2 = -6974.5, Rotation = 0 };
            var h2 = new PvDraft { Source = "LashingHole", X1 = 15009.9, Y1 = -6974.5, X2 = 15649.9, Y2 = -6974.5, Rotation = 0 };
            var edge = new PvDraft { Source = "LashingHole", X1 = 139.9, Y1 = -6974.5, X2 = -0.1, Y2 = -6974.5, Rotation = 0 };
            var dims = DraftDimRules.Lashing(Input(h1, h2, edge), Scale, (0, 0));
            var close = dims.Single(d => Math.Abs(Math.Abs(d.X2 - d.X1) - 289.94) < 0.1);
            Assert.Equal("(<>)", close.TextOverride);
            Assert.Equal("Mechanical-AM_5", close.Layer);
            Assert.Equal(500.84 + PlanViewLayout.LashChainTop * Scale, close.LineY, 3);
            Assert.DoesNotContain(dims, d => Math.Abs(Math.Abs(d.X2 - d.X1) - 140) < 0.1 && d.TextOverride == "(<>)");   // đầu trái đã có nháp tới mép
        }

        /// <summary>Tờ 7D-17P: COG (7973.8, −6403.9, r 250) đo từ góc (mép phải, y 0): 6404 dọc bên phải +10.4 mm, 7966.1 ngang +20.4 mm.</summary>
        [Fact] public void Lashing_CogDims_FromRightReferenceCorner()
        {
            var i = Input();
            i.Cog = (7973.8, -6403.9, 250);
            var dims = DraftDimRules.Lashing(i, Scale, (0, 0));
            var v = dims.Single(d => Math.Abs(Math.Abs(d.Y2 - d.Y1) - 6403.9) < 0.1);
            Assert.Equal(15939.84 + PlanViewLayout.LashCogRight * Scale, v.LineX, 3);
            Assert.Equal(7973.8 + 250, v.X2, 3);
            var h = dims.Single(d => Math.Abs(Math.Abs(d.X2 - d.X1) - 7966.04) < 0.1);
            Assert.Equal(500.84 + PlanViewLayout.LashCogTop * Scale, h.LineY, 3);
            Assert.All(new[] { v, h }, d => Assert.Equal("Mechanical-AM_5", d.Layer));
        }

        /// <summary>Tờ 7D-17P: Ø55 ghi ở lỗ giữa-dưới (8189.9, −10728.7), không phải lỗ đầu tiên.</summary>
        [Fact] public void Lashing_Diameter_AtMiddleLowerHole()
        {
            var i = Input();
            i.Lashing = new PvLashing { Diameter = 55 };
            foreach (var p in new[] { (139.9, -79.0), (7549.9, -10728.7), (8189.9, -10728.7), (8189.9, -6974.5) }) i.Lashing.Holes.Add(p);
            var dia = DraftDimRules.Lashing(i, Scale, (0, 0)).Single(d => d.Kind == "Diametric");
            Assert.Equal(8189.9, (dia.X1 + dia.X2) / 2, 1);
            Assert.Equal(-10728.7, (dia.Y1 + dia.Y2) / 2, 1);
        }

        [Fact] public void Lashing_KeepsTextTmoveOblique()
        {
            var v = V(139.9, -12891.26, -12751.4); v.Text = "(<>)"; v.Tmove = false;
            var d = DraftDimRules.Lashing(Input(v), Scale, (0, 0)).Single();
            Assert.Equal("(<>)", d.TextOverride);
            Assert.False(d.Tmove);
        }

        [Fact] public void Lashing_IgnoresStructureDrafts_AddsDiameter_AndShifts()
        {
            var i = Input(V(139.9, -1000, -500), V(500, -9000, -8500, "Structure"));
            i.Lashing = new PvLashing { Diameter = 55, X = 139.9, Y = -79 };
            var dims = DraftDimRules.Lashing(i, Scale, (18811.1, 0)).Where(d => d.TextOverride != "(<>)").ToList();
            Assert.Equal(2, dims.Count);
            Assert.Equal(139.9 + 18811.1, dims[0].X1, 3);
            var dia = dims.Single(d => d.Kind == "Diametric");
            Assert.Equal("AM_ISO$3", dia.DimStyle); Assert.Equal(DraftDimRules.LashingText, dia.TextOverride);
            Assert.Equal(55, Math.Sqrt((dia.X2 - dia.X1) * (dia.X2 - dia.X1) + (dia.Y2 - dia.Y1) * (dia.Y2 - dia.Y1)), 3);   // xiên 28° như tờ tay
        }
    }
}
