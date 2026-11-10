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
            var d = DraftDimRules.Lashing(Input(V(139.9, -12354.1, -11904.1)), Scale, (0, 0)).Single();
            Assert.Equal("Mechanical-AM_4", d.Layer);
            Assert.Equal(139.9, d.X1, 3); Assert.Equal(-12354.1, d.Y1, 3); Assert.Equal(-11904.1, d.Y2, 3);
            Assert.Equal(-359.06 - PlanViewLayout.LashChainLeft * Scale, d.LineX, 3);
            Assert.Equal(Math.PI / 2, d.Rotation, 6);
        }

        [Fact] public void Lashing_Horizontal_TopChain()
        {
            var h = new PvDraft { Source = "LashingHole", X1 = 709.9, Y1 = -79, X2 = 1349.9, Y2 = -79, Rotation = 0 };
            var d = DraftDimRules.Lashing(Input(h), Scale, (0, 0)).Single();
            Assert.Equal(500.84 + PlanViewLayout.LashChainTop * Scale, d.LineY, 3);
            Assert.Equal(-79, d.Y1, 3);
        }

        [Fact] public void Lashing_EndOnPlateEdge_GoesToAM5()
        {
            var h = new PvDraft { Source = "LashingHole", X1 = 0, Y1 = -6000, X2 = 139.9, Y2 = -6000, Rotation = 0 };   // x = 0 là cạnh tấm
            Assert.Equal("Mechanical-AM_5", DraftDimRules.Lashing(Input(h), Scale, (0, 0)).Single().Layer);
        }

        /// <summary>Tờ 7D-17P: 480 (chồng lên 695.5 cùng trục) giữ đường của chính nó trong tấm, AM_5, "&lt;&gt;".</summary>
        [Fact] public void Lashing_OverlappingShorterDraft_IsLocal_AM5_OwnLine()
        {
            var longD = V(139.9, -774.4, -79); var shortD = V(139.9, -774.4, -294.4);
            shortD.LineX = 498.8; shortD.LineY = -774.4;
            var dims = DraftDimRules.Lashing(Input(longD, shortD), Scale, (0, 0));
            var local = dims.Single(d => Math.Abs(Math.Abs(d.Y2 - d.Y1) - 480) < 0.01);
            Assert.Equal("Mechanical-AM_5", local.Layer);
            Assert.Equal(498.8, local.LineX, 3);
            Assert.Equal("<>", local.TextOverride);
            Assert.False(local.Tmove);
            var chain = dims.Single(d => Math.Abs(Math.Abs(d.Y2 - d.Y1) - 695.4) < 0.01);
            Assert.Equal(-359.06 - PlanViewLayout.LashChainLeft * Scale, chain.LineX, 3);
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
            var dims = DraftDimRules.Lashing(i, Scale, (18811.1, 0));
            Assert.Equal(2, dims.Count);
            Assert.Equal(139.9 + 18811.1, dims[0].X1, 3);
            var dia = dims.Single(d => d.Kind == "Diametric");
            Assert.Equal("AM_ISO$3", dia.DimStyle); Assert.Equal(DraftDimRules.LashingText, dia.TextOverride);
            Assert.Equal(55, Math.Abs(dia.X2 - dia.X1), 3);
        }
    }
}
