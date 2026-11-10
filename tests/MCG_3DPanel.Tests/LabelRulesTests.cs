using System;
using System.Linq;
using MCG_3DPanel.Services.PlanView;
using Xunit;

namespace MCG_3DPanel.Tests
{
    /// <summary>Nhãn tên theo tờ mẫu 7D-17P: MiddleLeft; T (21.0), HT (25.2), BS phương T (31.8) trên mép trên, căn giữa thanh;
    /// L (35.8), HL/BS phương L (42.6) trái mép trái. + member list MText.</summary>
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

        [Fact] public void Names_TDirection_Rows_7D17P()
        {
            var t = LabelRules.Names(Input(M(1, "T1", "Web", 300, 0, 300, 13000), M(2, "HT1", "HP", 1000, 500, 1000, 12500), M(3, "BS", "FB", 2000, 0, 2000, 13000)), Scale);
            var g = t.Single(x => x.Text == "T1");
            Assert.Equal("Mechanical-AM_1", g.Layer); Assert.Equal(5, g.PaperHeight); Assert.Equal("CenterMiddleLeft", g.Justify);
            Assert.Equal(300, g.X, 3);
            Assert.Equal(13000 + 21.0 * Scale, g.Y, 3);
            var s = t.Single(x => x.Text == "HT1");
            Assert.Equal("Mechanical-AM_6", s.Layer); Assert.Equal(3, s.PaperHeight);
            Assert.Equal(13000 + 25.2 * Scale, s.Y, 3);
            Assert.Equal(13000 + 31.8 * Scale, t.Single(x => x.Text == "BS").Y, 3);
            Assert.All(t, x => Assert.Equal("Name", x.Category));
        }

        [Fact] public void Names_LDirection_Columns_7D17P()
        {
            var t = LabelRules.Names(Input(M(1, "L3", "Web", 0, 4000, 16000, 4000), M(2, "HL1", "HP", 0, 200, 16000, 200)), Scale);
            var g = t.Single(x => x.Text == "L3");
            Assert.Equal("MiddleLeft", g.Justify);
            Assert.Equal(-35.8 * Scale, g.X, 3);
            Assert.Equal(4000, g.Y, 3);
            Assert.Equal(-42.6 * Scale, t.Single(x => x.Text == "HL1").X, 3);
        }

        [Fact] public void Names_MergedLinesShareOneLabel()
            => Assert.Single(LabelRules.Names(Input(M(1, "T3", "Web", 300, 0, 300, 6000), M(1, "T3", "Web", 300, 6100, 300, 13000)), Scale));

        [Fact] public void Names_EveryBsLineGetsItsLabel()
            => Assert.Equal(2, LabelRules.Names(Input(M(1, "BS", "FB", 0, 1000, 16000, 1000), M(2, "BS", "FB", 0, 2000, 16000, 2000)), Scale).Count);

        [Fact] public void Names_DeletedMemberHasNoLabel()
            => Assert.Empty(LabelRules.Names(Input(M(1, "T1", "Web", 300, 0, 300, 13000, deleted: true)), Scale));

        [Fact] public void Names_FlangeAndUnnamed_NoLabel()
            => Assert.Empty(LabelRules.Names(Input(M(1, "T1", "Flange", 300, 0, 300, 13000), M(2, "", "HP", 1000, 0, 1000, 13000)), Scale));

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
                "TRANSVERSAL GIRDERS T1,T2 : WEB 374x6.0/FLANGE 380x20.0\\P\\P" +
                "LONGITUDINAL GIRDERS L1,L7 : WEB 374x6.0/FLANGE 250x20.0\\P" +
                "LONGITUDINAL GIRDERS L2,L3,L5,L6 : WEB 374x6.0/FLANGE 170x20.0\\P" +
                "LONGITUDINAL GIRDERS L4 : WEB 374x6.0/FLANGE 150x20.0", t.Text);
        }

        /// <summary>Khung AM_8 theo số dòng (tờ 7D-17P: 9 dòng ⇒ trên 83.5, dưới 22.1; vạch ngăn ở 72.08 / 61.08 / 48.58 mm giấy).</summary>
        [Fact] public void MemberFrame_FollowsLines_7D17P()
        {
            var lines = new[] { "TOP PLATE THICKNESS: 6.0", "", "STIFFENERS", "", "TRANSVERSAL", "", "L1,L7", "L2..", "L4" };
            var (frames, rules) = SheetItemRules.MemberFrame(0, 0, Scale, lines);
            Assert.Equal(3, frames.Count);
            var outer = frames[0].Points;
            Assert.Equal(83.5, outer.Max(p => p.Y) / Scale, 1);
            Assert.Equal(22.1, outer.Min(p => p.Y) / Scale, 1);
            var ys = rules.Select(r => r.Y1 / Scale).OrderByDescending(y => y).ToList();
            var mids = Enumerable.Range(0, ys.Count / 3).Select(k => ys.Skip(k * 3).Take(3).Average()).ToList();   // 3 nét mỗi vạch
            Assert.Equal(3, mids.Count);
            Assert.InRange(mids[0], 72.08 - 0.8, 72.08 + 0.8);
            Assert.InRange(mids[1], 61.08 - 0.8, 61.08 + 0.8);
            Assert.InRange(mids[2], 48.58 - 0.8, 48.58 + 0.8);
            Assert.Equal(9, rules.Count);
        }

        [Fact] public void MemberFrame_GrowsWithMoreLines()
        {
            var a = SheetItemRules.MemberFrame(0, 0, Scale, new[] { "a", "", "b" }).Frames[0].Points.Min(p => p.Y);
            var b = SheetItemRules.MemberFrame(0, 0, Scale, new[] { "a", "", "b", "c", "d" }).Frames[0].Points.Min(p => p.Y);
            Assert.True(b < a);
        }

        [Fact] public void MemberList_NaturalOrder_T10AfterT2()
        {
            var input = Input(M(1, "T10", "Web", 9000, 0, 9000, 13000, "W"), M(2, "T2", "Web", 900, 0, 900, 13000, "W"));
            Assert.Contains("TRANSVERSAL GIRDERS T2,T10 : W", LabelRules.MemberList(input, 0, 0, Scale).Text);
        }
    }
}
