using System.Linq;
using MCG_3DPanel.Services.PlanView;
using Xunit;

namespace MCG_3DPanel.Tests
{
    /// <summary>Nhãn tên trên tờ (T/L trên AM_1 5 mm, HT/HL/BS trên AM_6 3 mm) + member list MText.</summary>
    public class LabelRulesTests
    {
        private const int Scale = 50;

        private static PvMember M(int line, string name, string kind, double x0, double y0, double x1, double y1, string spec = "", bool deleted = false)
            => new PvMember { Line = line, Name = name, Kind = kind, Dir = System.Math.Abs(x1 - x0) >= System.Math.Abs(y1 - y0) ? "L" : "T",
                              X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Spec = spec, Deleted = deleted };

        private static PlanViewInput Input(params PvMember[] ms)
        {
            var i = new PlanViewInput { Panel = "P", TopPlate = (0, 0, 16000, 13000), PlateThickness = 6 };
            i.Members.AddRange(ms);
            return i;
        }

        [Fact] public void Names_GirderOnAM1_StiffenerOnAM6()
        {
            var t = LabelRules.Names(Input(M(1, "T1", "Web", 300, 0, 300, 13000), M(2, "HT1", "HP", 1000, 500, 1000, 12500)), Scale, 0, 0);
            var g = t.Single(x => x.Text == "T1");
            Assert.Equal("Mechanical-AM_1", g.Layer);
            Assert.Equal(5, g.PaperHeight);
            Assert.Equal("BottomCenter", g.Justify);
            Assert.Equal(300, g.X, 3);
            Assert.Equal(13000 + PlanViewLayout.LabelTopGirder * Scale, g.Y, 3);
            var s = t.Single(x => x.Text == "HT1");
            Assert.Equal("Mechanical-AM_6", s.Layer);
            Assert.Equal(3, s.PaperHeight);
            Assert.Equal(13000 + PlanViewLayout.LabelTopStiffener * Scale, s.Y, 3);
            Assert.All(t, x => Assert.Equal("Name", x.Category));
        }

        [Fact] public void Names_LDirection_LeftColumn_MiddleLeft()
        {
            var t = LabelRules.Names(Input(M(1, "L3", "Web", 0, 4000, 16000, 4000)), Scale, 0, 0).Single();
            Assert.Equal("MiddleLeft", t.Justify);
            Assert.Equal(-PlanViewLayout.LabelLeft * Scale, t.X, 3);
            Assert.Equal(4000, t.Y, 3);
        }

        [Fact] public void Names_StackBeyondDimensionChains()
        {
            // chuỗi kích thước trên cùng ở 38 mm ⇒ hàng HT ở 46, hàng T ở 66
            var t = LabelRules.Names(Input(M(1, "T1", "Web", 300, 0, 300, 13000), M(2, "HT1", "HP", 1000, 0, 1000, 13000)), Scale, 38, 0);
            Assert.Equal(13000 + 46 * Scale, t.Single(x => x.Text == "HT1").Y, 3);
            Assert.Equal(13000 + 66 * Scale, t.Single(x => x.Text == "T1").Y, 3);
        }

        [Fact] public void Names_MergedLinesShareOneLabel()
            => Assert.Single(LabelRules.Names(Input(M(1, "T3", "Web", 300, 0, 300, 6000), M(1, "T3", "Web", 300, 6100, 300, 13000)), Scale, 0, 0));

        [Fact] public void Names_EveryBsLineGetsItsLabel()
            => Assert.Equal(2, LabelRules.Names(Input(M(1, "BS", "FB", 0, 1000, 16000, 1000), M(2, "BS", "FB", 0, 2000, 16000, 2000)), Scale, 0, 0).Count);

        [Fact] public void Names_DeletedMemberHasNoLabel()
            => Assert.Empty(LabelRules.Names(Input(M(1, "T1", "Web", 300, 0, 300, 13000, deleted: true)), Scale, 0, 0));

        [Fact] public void Names_FlangeAndUnnamed_NoLabel()
            => Assert.Empty(LabelRules.Names(Input(M(1, "T1", "Flange", 300, 0, 300, 13000), M(2, "", "HP", 1000, 0, 1000, 13000)), Scale, 0, 0));

        [Fact] public void MemberList_7D17P()
        {
            const string g1 = "WEB 374x6.0/FLANGE 380x20.0", g2 = "WEB 374x6.0/FLANGE 250x20.0", g3 = "WEB 374x6.0/FLANGE 170x20.0", g4 = "WEB 374x6.0/FLANGE 150x20.0";
            var input = Input(
                M(1, "T1", "Web", 300, 0, 300, 13000, g1), M(2, "T2", "Web", 15400, 0, 15400, 13000, g1),
                M(3, "L1", "Web", 0, 400, 16000, 400, g2), M(9, "L7", "Web", 0, 12600, 16000, 12600, g2),
                M(4, "L2", "Web", 0, 2450, 16000, 2450, g3), M(5, "L3", "Web", 0, 4500, 16000, 4500, g3),
                M(7, "L5", "Web", 0, 8600, 16000, 8600, g3), M(8, "L6", "Web", 0, 10600, 16000, 10600, g3),
                M(6, "L4", "Web", 0, 6500, 16000, 6500, g4),
                M(10, "HT1", "HP", 1000, 0, 1000, 13000, "HP 120x6.0"));
            var t = LabelRules.MemberList(input, 0, 0, Scale);
            Assert.True(t.IsMText);
            Assert.Equal("Mechanical-AM_6", t.Layer);
            Assert.Equal(3.5, t.PaperHeight);
            Assert.Equal("TopLeft", t.Justify);
            Assert.Equal(PlanViewLayout.MemberListX * Scale, t.X, 3);
            Assert.Equal(PlanViewLayout.MemberListY * Scale, t.Y, 3);
            Assert.Equal(
                "TOP PLATE THICKNESS: 6.0\\P\\P" +
                "STIFFENERS (HL & HT): HP 120x6.0\\P\\P" +
                "TRANSVERSAL GIRDERS T1,T2 : WEB 374x6.0/FLANGE 380x20.0\\P" +
                "LONGITUDINAL GIRDERS L1,L7 : WEB 374x6.0/FLANGE 250x20.0\\P" +
                "LONGITUDINAL GIRDERS L2,L3,L5,L6 : WEB 374x6.0/FLANGE 170x20.0\\P" +
                "LONGITUDINAL GIRDERS L4 : WEB 374x6.0/FLANGE 150x20.0", t.Text);
        }

        [Fact] public void MemberList_NaturalOrder_T10AfterT2()
        {
            var input = Input(M(1, "T10", "Web", 9000, 0, 9000, 13000, "W"), M(2, "T2", "Web", 900, 0, 900, 13000, "W"));
            Assert.Contains("TRANSVERSAL GIRDERS T2,T10 : W", LabelRules.MemberList(input, 0, 0, Scale).Text);
        }
    }
}
