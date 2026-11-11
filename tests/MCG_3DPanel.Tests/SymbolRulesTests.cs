using System;
using System.Linq;
using MCG_3DPanel.Services.PlanView;
using Xunit;

namespace MCG_3DPanel.Tests
{
    /// <summary>Ký hiệu chữ (B, S, GE/GB, OB) + CS_horl_up_right tại mỗi chỗ thanh chạy liên tục xuyên web thanh khác (kỹ sư 2026-11-10).</summary>
    public class SymbolRulesTests
    {
        private const int Scale = 50;
        private static int _line;

        private static PvMember M(string kind, double x0, double y0, double x1, double y1, double t = 6, int? line = null)
            => new PvMember { Line = line ?? ++_line, Name = "x", Kind = kind, Dir = Math.Abs(x1 - x0) >= Math.Abs(y1 - y0) ? "L" : "T",
                              X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Thickness = t };

        private static PlanViewInput Input(int position = 0)
            => new PlanViewInput { Panel = "P", TopPlate = (0, 0, 16000, 13000), PlateThickness = 6, Position = position };

        /// <summary>7D-17P: 7 web L (y = 400 … 12600), 19 HT đứng chạy từ L1 tới L7 (dừng tại 2 web ngoài).</summary>
        private static PlanViewInput Panel7D17P()
        {
            var i = Input();
            double[] ys = { 400, 2450, 4500, 6500, 8600, 10600, 12600 };
            foreach (var y in ys) i.Members.Add(M("Web", 0, y, 16000, y));
            for (int k = 0; k < 19; k++) { double x = 1000 + 775 * k; i.Members.Add(M("HP", x, 403, x, 12597)); }
            return i;
        }

        [Fact] public void Continuous_7D17P_Gives95()
        {
            var cs = SymbolRules.Continuous(Panel7D17P());
            Assert.Equal(95, cs.Count);
            Assert.All(cs, c => Assert.Equal("CS_symbol", c.Block));          // chuẩn 7D-17C
            Assert.All(cs, c => Assert.Equal("Mechanical-AM_6", c.Layer));
            Assert.All(cs, c => Assert.Equal(Math.PI / 2, c.Rotation, 6));   // xoay theo thanh chạy liên tục (HT đứng)
            Assert.DoesNotContain(cs, c => Math.Abs(c.Y - 400) < 1 || Math.Abs(c.Y - 12600) < 1);
        }

        [Fact] public void Continuous_GirderThroughGirder_OneSymbol()
        {
            var i = Input();
            i.Members.Add(M("Web", 5000, 0, 5000, 13000));          // T liên tục
            i.Members.Add(M("Web", 0, 6000, 4997, 6000, line: 900));  // L bị chia tại T (cùng tuyến)
            i.Members.Add(M("Web", 5003, 6000, 16000, 6000, line: 900));
            var cs = SymbolRules.Continuous(i);
            Assert.Single(cs);
            Assert.Equal(5000, cs[0].X, 3);
            Assert.Equal(6000, cs[0].Y, 3);
        }

        [Fact] public void Continuous_EndingMember_None()
        {
            var i = Input();
            i.Members.Add(M("Web", 0, 6000, 16000, 6000));
            i.Members.Add(M("HP", 3000, 6003, 3000, 12000));   // HP dừng tại web
            Assert.Empty(SymbolRules.Continuous(i));
        }

        [Fact] public void Continuous_DeletedAndFlange_Ignored()
        {
            var i = Input();
            i.Members.Add(M("Web", 0, 6000, 16000, 6000));
            var hp = M("HP", 3000, 0, 3000, 13000); hp.Deleted = true; i.Members.Add(hp);
            i.Members.Add(M("Flange", 4000, 0, 4000, 13000));
            Assert.Empty(SymbolRules.Continuous(i));
        }

        /// <summary>Tờ 7D-17P: T1 (x 287) giao các web L ở đầu mút tuyến L ⇒ không ký hiệu (kỹ sư 2026-11-10).</summary>
        [Fact] public void GirderCrossing_AtGirderEnd_None()
        {
            var i = Input();
            i.Members.Add(M("Web", 287, 0, 287, 13000));
            i.Members.Add(M("Web", 0, 6000, 16000, 6000));
            Assert.Empty(SymbolRules.Continuous(i));
        }

        [Fact] public void GirderCrossing_NearOtherGirderEnd_None()
        {
            var i = Input();
            i.Members.Add(M("Web", 8000, 0, 8000, 13000));
            i.Members.Add(M("Web", 0, 400, 16000, 400));          // giao cách đầu mút tuyến T 400 < 1000
            Assert.Empty(SymbolRules.Continuous(i));
        }

        [Fact] public void Marks_BAtHp_SideAndAlong()
        {
            var i = Input();
            i.Marks.Add(new PvMark { Text = "B", X = 6587.7, Y = 20223.3, AtHp = true, DirX = 0, DirY = 1, PerpX = -1, PerpY = 0 });
            var t = SymbolRules.Marks(i, Scale).Single();
            Assert.Equal(6587.7 - PlanViewLayout.HpBSide * Scale, t.X, 3);
            Assert.Equal(20223.3 + PlanViewLayout.HpBAlong * Scale, t.Y, 3);
            Assert.Equal("CenterMiddleLeft", t.Justify);
        }

        /// <summary>6/6 tờ mẫu: mọi CS_horl_up_right có sx = 1, ở mọi vị trí panel.</summary>
        [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
        public void NeverMirrored_AnyPosition(int pos)
        {
            var i = Input(pos);
            i.Members.Add(M("Web", 0, 2000, 16000, 2000));
            i.Members.Add(M("Web", 0, 11000, 16000, 11000));
            i.Members.Add(M("HP", 3000, 0, 3000, 13000));
            Assert.All(SymbolRules.Continuous(i), c => Assert.False(c.MirrorX));
        }

        [Fact] public void Marks_TextLayerHeight_PushedAway()
        {
            var i = Input();
            i.Marks.Add(new PvMark { Text = "B", X = 1000, Y = 2000, DirX = 0, DirY = 1 });
            i.Marks.Add(new PvMark { Text = "GE", X = 3000, Y = 4000 });
            var t = SymbolRules.Marks(i, Scale);
            Assert.Equal(2, t.Count);
            Assert.All(t, x => { Assert.Equal("Mechanical-AM_6", x.Layer); Assert.Equal(3, x.PaperHeight); Assert.Equal("CenterMiddleLeft", x.Justify); Assert.Equal("Symbol", x.Category); });
            Assert.Equal(2000 + PlanViewLayout.MarkOffset * Scale, t[0].Y, 3);
            Assert.Equal(4000, t[1].Y, 3);
        }

        /// <summary>Tờ 7D-17P: OB ở T1 (bên trái) nằm trái bracket, ở T2 nằm phải, hàng dưới nằm dưới.</summary>
        [Fact] public void BracketCode_PushedTowardNearestEdge()
        {
            var i = Input();
            i.Marks.Add(new PvMark { Text = "OB", X = 300, Y = 6500, IsBracket = true });
            i.Marks.Add(new PvMark { Text = "OB", X = 15700, Y = 6500, IsBracket = true });
            i.Marks.Add(new PvMark { Text = "OB1", X = 8000, Y = 200, IsBracket = true });
            var t = SymbolRules.Marks(i, Scale);
            double off = PlanViewLayout.BracketTextOut * Scale;
            Assert.Equal(300 - off, t[0].X, 3); Assert.Equal(15700 + off, t[1].X, 3); Assert.Equal(200 - off, t[2].Y, 3);
        }
    }
}
