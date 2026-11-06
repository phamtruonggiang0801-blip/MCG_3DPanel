using System;
using System.Collections.Generic;
using System.Linq;
using MCG_3DPanel.Models;
using MCG_3DPanel.Naming;
using Xunit;

namespace MCG_3DPanel.Tests
{
    /// <summary>Naming tab (spec 2026-11-05): tuyến + tên tự sinh, toạ độ thật panel 7D-16C (CAS.dump.json, hệ block).</summary>
    public class StructureNamingTests
    {
        private static int _n;
        private static NamingPiece P(NamingKind k, double x0, double y0, double x1, double y1, double t = 6, string key = null)
            => new NamingPiece { Key = key ?? ("k" + (++_n)), Kind = k, X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Thickness = t };
        private static NamingPiece VY(NamingKind k, double x, double y0, double y1, double t = 6, string key = null) => P(k, x, y0, x, y1, t, key);
        private static NamingPiece HX(NamingKind k, double y, double x0, double x1, double t = 6, string key = null) => P(k, x0, y, x1, y, t, key);

        internal static List<NamingPiece> Cas7D16C()
        {
            var l = new List<NamingPiece>();
            // Web theo Y (T)
            l.Add(VY(NamingKind.Web, 537, 1800, 8722));
            l.Add(VY(NamingKind.Web, 530, 200, 1400, 20));
            l.Add(VY(NamingKind.Web, 2483, 452, 10057));
            l.Add(VY(NamingKind.Web, 4423, 90, 446));
            l.Add(VY(NamingKind.Web, 4423, 452, 10057, key: "T3a"));
            l.Add(VY(NamingKind.Web, 4423, 10063, 10908));
            l.Add(VY(NamingKind.Web, 4423, 10917, 11296));
            l.Add(VY(NamingKind.Web, 6363, 452, 10057));
            l.Add(VY(NamingKind.Web, 8303, 452, 10057));
            l.Add(VY(NamingKind.Web, 10243, 452, 10057));
            l.Add(VY(NamingKind.Web, 10967, 11725, 14177, key: "T6a"));
            l.Add(VY(NamingKind.Web, 12233, 603, 13627));
            // Web theo X (L)
            l.Add(HX(NamingKind.Web, 449, 840, 11880));
            l.Add(HX(NamingKind.Web, 10060, 840, 12230));
            l.Add(HX(NamingKind.Web, 12497, 10246, 12230));
            l.Add(HX(NamingKind.Web, 12497, 12236, 12710));
            l.Add(HX(NamingKind.Web, 13768, 10970, 11880));
            // HP
            l.Add(VY(NamingKind.HP, 57, 39, 10483));
            l.Add(VY(NamingKind.HP, 12713, 427, 13790));
            foreach (var y in new double[] { 87, 903, 1678, 2453, 3228, 4003, 4778, 5553, 6322, 7097, 7847, 8622, 9397, 10465, 10947, 11722, 13262, 14180 })
                l.Add(HX(NamingKind.HP, y, 570, 12200));
            l.Add(HX(NamingKind.HP, 12497, 8336, 10210, key: "1B2E9"));
            // FB (BS)
            foreach (var x in new double[] { 1043, 1983, 2983, 3923, 4923, 5863, 6863, 7803, 8803, 9743, 10743, 11733 })
                l.Add(VY(NamingKind.FB, x, 906, 9394));
            l.Add(VY(NamingKind.FB, 7803, 10468, 11719));
            l.Add(VY(NamingKind.FB, 7803, 11725, 12351));
            l.Add(HX(NamingKind.FB, 9710, 570, 8270));
            l.Add(HX(NamingKind.FB, 9710, 8336, 8800));
            // Flange T1 (AM_11 487..587 ⇒ rộng 100, tâm 537), dày theo Input 20
            l.Add(new NamingPiece { Key = "fT1", Kind = NamingKind.Flange, X0 = 537, Y0 = 1040, X1 = 537, Y1 = 9520, Thickness = 100, Height = 20 });
            foreach (var p in l) Spec(p);
            return l;
        }

        /// <summary>Thông số adapter gán: Web cao 374 (Input), HP 120×6 (Input), FB cao 80 (BS 80×6).</summary>
        private static NamingPiece Spec(NamingPiece p)
        {
            if (p.Kind == NamingKind.Web) p.Height = 374;
            else if (p.Kind == NamingKind.HP) { p.Height = 120; p.Thickness = 6; }
            else if (p.Kind == NamingKind.FB) p.Height = 80;
            return p;
        }
        private static NamingPiece Web(NamingPiece p) => Spec(p);

        private static string[] Names(IEnumerable<NamingLine> l, NamingKind k, NamingDir d)
            => l.Where(x => x.Kind == k && x.Dir == d).Select(x => x.Name).ToArray();
        private static NamingLine Line(IEnumerable<NamingLine> l, string name) => l.Single(x => x.Name == name);

        [Fact]
        public void Lines_Cas7D16C_Webs()
        {
            var l = StructureNaming.Lines(Cas7D16C(), PanelPosition.Center);
            Assert.Equal(new[] { "T1", "T2", "T3", "T4", "T5", "T6", "T7", "T8" }, Names(l, NamingKind.Web, NamingDir.T));
            Assert.Equal(new[] { "L1", "L2", "L3", "L4" }, Names(l, NamingKind.Web, NamingDir.L));
            Assert.Equal(4, Line(l, "T3").Pieces.Count);
            Assert.Equal(2, Line(l, "T1").Pieces.Count);
            Assert.Contains(Line(l, "T7").Pieces, p => p.Key == "T6a");
        }

        [Fact]
        public void Lines_Hp1B2E9_BelongsToL3()
        {
            var l = StructureNaming.Lines(Cas7D16C(), PanelPosition.Center);
            Assert.Contains(Line(l, "L3").Pieces, p => p.Key == "1B2E9");
            Assert.Equal(NamingKind.Web, Line(l, "L3").Kind);
        }

        [Fact]
        public void Lines_Cas7D16C_Hp()
        {
            var l = StructureNaming.Lines(Cas7D16C(), PanelPosition.Center);
            Assert.Equal(new[] { "HT1", "HT2" }, Names(l, NamingKind.HP, NamingDir.T));
            var hl = Names(l, NamingKind.HP, NamingDir.L);
            Assert.Equal(Enumerable.Range(1, 18).Select(i => "HL" + i).ToArray(), hl);
            Assert.Equal(87, Line(l, "HL1").Center, 3);
            Assert.Equal(14180, Line(l, "HL18").Center, 3);
            Assert.Equal(57, Line(l, "HT1").Center, 3);
        }

        [Fact]
        public void Lines_Fb_AllBs()
        {
            var l = StructureNaming.Lines(Cas7D16C(), PanelPosition.Center);
            var fb = l.Where(x => x.Kind == NamingKind.FB).ToList();
            Assert.All(fb, x => Assert.Equal("BS", x.Name));
            // Revision 1: BS thẳng hàng = 1 dòng dù cách xa ⇒ 12 tuyến dọc + 1 tuyến ngang
            Assert.Equal(12 + 1, fb.Count);
        }

        [Fact]
        public void Lines_FbFarApart_OneLine()   // BS 947 + BS 8428 cùng phương ⇒ 1 dòng (kỹ sư revision 1)
        {
            var l = StructureNaming.Lines(Cas7D16C(), PanelPosition.Center);
            var x7803 = l.Single(x => x.Kind == NamingKind.FB && x.Dir == NamingDir.T && System.Math.Abs(x.Center - 7803) < 1);
            Assert.Equal(3, x7803.Pieces.Count);
        }

        [Fact]
        public void Spec_WebWithFlange()
        {
            var t1 = Line(StructureNaming.Lines(Cas7D16C(), PanelPosition.Center), "T1");
            Assert.Equal("WEB 374x6.0/FLANGE 100x20.0", t1.Spec);
            Assert.Equal("T1 WEB 374x6.0/FLANGE 100x20.0", t1.Label);
        }

        [Fact]
        public void Spec_WebNoFlange_HpFb()
        {
            var l = StructureNaming.Lines(Cas7D16C(), PanelPosition.Center);
            Assert.Equal("WEB 374x6.0", Line(l, "T2").Spec);
            Assert.Equal("WEB 374x6.0", Line(l, "L3").Spec);   // HP 1B2E9 không đổi quy cách tuyến web
            Assert.Equal("HL1 HP 120x6.0", Line(l, "HL1").Label);
            Assert.Equal("BS FB 80x6.0", l.First(x => x.Kind == NamingKind.FB).Label);
        }

        [Fact]
        public void Spec_FlangeNotOnLine_Ignored()
        {
            var l = StructureNaming.Lines(new[]
            {
                Web(VY(NamingKind.Web, 1000, 0, 5000)),
                new NamingPiece { Key = "f", Kind = NamingKind.Flange, X0 = 1300, Y0 = 0, X1 = 1300, Y1 = 5000, Thickness = 100, Height = 20 },
            }, PanelPosition.Center);
            Assert.Equal("WEB 374x6.0", l.Single().Spec);
        }

        [Fact]
        public void Deleted_HiddenAndRenumbered()
        {
            // Del Name xoá MỌI đoạn của tuyến (NameStore.DeleteLine)
            var pieces = Cas7D16C();
            var t3 = Line(StructureNaming.Lines(pieces, PanelPosition.Center), "T3").Pieces.Select(p => p.Key).ToList();
            var l = StructureNaming.Lines(pieces, PanelPosition.Center, new HashSet<string>(t3));
            Assert.Equal(new[] { "T1", "T2", "T3", "T4", "T5", "T6", "T7" }, Names(l, NamingKind.Web, NamingDir.T));
            Assert.Equal(6363, Line(l, "T3").Center, 0);   // T4 cũ → T3
            Assert.DoesNotContain(l, x => x.Pieces.Any(p => p.Key == "T3a"));
        }

        [Fact]
        public void Deleted_PieceJoiningAnotherLine_DoesNotHideIt()   // review R1 #3
        {
            var l = StructureNaming.Lines(new[]
            {
                Web(VY(NamingKind.Web, 1000, 0, 5000, key: "keep")), Web(VY(NamingKind.Web, 1005, 6000, 6300, key: "stub")),
            }, PanelPosition.Center, new HashSet<string> { "stub" });
            Assert.Equal("keep", l.Single().Pieces.Single().Key);
        }

        [Theory]
        [InlineData("T6a WEB 374x6.0", "T6a")]
        [InlineData("T6a", "T6a")]
        [InlineData("  T6a  WEB 374x6.0 ", "T6a")]
        [InlineData("WEB 374x6.0", "")]
        [InlineData("T6a WEB 400x8.0", "T6a")]                 // review R1 #1: gõ quy cách khác ⇒ vẫn chỉ giữ tên
        [InlineData("T6a WEB 374x6.0/FLANGE", "T6a")]          // xoá dở phần flange
        [InlineData("T6a web 374x6", "T6a")]
        [InlineData("HL3 HP 120x6", "HL3")]
        [InlineData("BS FB 80x6.0", "BS")]
        [InlineData("WEBX1", "WEBX1")]                          // không phải token quy cách
        public void StripSpec_KeepsTagOnly(string typed, string tag)
            => Assert.Equal(tag, StructureNaming.StripSpec(typed, "WEB 374x6.0"));

        // ─── Revision 2: flange đi theo web khi highlight, Merge ───

        [Fact]
        public void Flanges_AttachedToWebLine()
        {
            var l = StructureNaming.Lines(Cas7D16C(), PanelPosition.Center);
            Assert.Equal("fT1", Line(l, "T1").Flanges.Single().Key);
            Assert.Empty(Line(l, "T2").Flanges);
            Assert.Empty(Line(l, "HL1").Flanges);
        }

        private static List<string> KeysOf(IEnumerable<NamingLine> l, params string[] names)
            => names.SelectMany(n => Line(l, n).Pieces.Select(p => p.Key)).ToList();

        [Fact]
        public void Merge_TwoWebLines_OneRowOneNumber()
        {
            var pieces = Cas7D16C();
            var plain = StructureNaming.Lines(pieces, PanelPosition.Center);
            var group = KeysOf(plain, "T3", "T4");
            var l = StructureNaming.Lines(pieces, PanelPosition.Center, null, new[] { group });
            Assert.Equal(new[] { "T1", "T2", "T3", "T4", "T5", "T6", "T7" }, Names(l, NamingKind.Web, NamingDir.T));
            var m = Line(l, "T3");
            Assert.True(m.IsMerged);
            Assert.Equal(4 + 1, m.Pieces.Count);
            Assert.Equal(8303, Line(l, "T4").Center, 0);   // T5 cũ → T4
            Assert.False(Line(l, "T1").IsMerged);
        }

        [Fact]
        public void Merge_DifferentKindOrDir_Ignored()
        {
            var pieces = Cas7D16C();
            var plain = StructureNaming.Lines(pieces, PanelPosition.Center);
            var l = StructureNaming.Lines(pieces, PanelPosition.Center, null, new[] { KeysOf(plain, "T2", "L1"), KeysOf(plain, "T5", "HT1") });
            Assert.Equal(plain.Count, l.Count);
            Assert.DoesNotContain(l, x => x.IsMerged);
        }

        [Fact]
        public void Merge_ThreeBs_OneRow()
        {
            var pieces = Cas7D16C();
            var plain = StructureNaming.Lines(pieces, PanelPosition.Center);
            var bs = plain.Where(x => x.Kind == NamingKind.FB && x.Dir == NamingDir.T).Take(3).SelectMany(x => x.Pieces.Select(p => p.Key)).ToList();
            var l = StructureNaming.Lines(pieces, PanelPosition.Center, null, new[] { bs });
            Assert.Equal(plain.Count(x => x.Kind == NamingKind.FB) - 2, l.Count(x => x.Kind == NamingKind.FB));
        }

        [Fact]
        public void NamesJson_MergesRoundTrip()
        {
            var json = NamesJson.Serialize(new Dictionary<string, string>(), null, new[] { new[] { "a", "b" }, new[] { "c", "d" } });
            NamesJson.Deserialize(json, out _, out var merges);
            Assert.Equal(2, merges.Count);
            Assert.Equal(new[] { "a", "b" }, merges[0].OrderBy(s => s).ToArray());
        }

        [Fact]
        public void NamesJson_DeletedRoundTrip()
        {
            var json = NamesJson.Serialize(new Dictionary<string, string> { ["a"] = "T1" }, new[] { "x", "y" });
            var names = NamesJson.Deserialize(json, out var deleted);
            Assert.Equal("T1", names["a"]);
            Assert.Equal(new[] { "x", "y" }, deleted.OrderBy(s => s).ToArray());
            Assert.Empty(NamesJson.Deserialize("garbage", out var none).Keys);
            Assert.Empty(none);
        }

        [Fact]
        public void Lines_FbSplitAtHp_OneLine()
        {
            var l = StructureNaming.Lines(new[] { VY(NamingKind.FB, 8803, 906, 9394), VY(NamingKind.FB, 8803, 9400, 10027) }, PanelPosition.Center);
            Assert.Single(l);
            Assert.Equal(2, l[0].Pieces.Count);
            Assert.Equal(8488 + 627, l[0].Length, 3);
        }

        [Fact]
        public void Lines_Starboard_ReversesL()
        {
            var l = StructureNaming.Lines(Cas7D16C(), PanelPosition.Starboard);
            Assert.Equal(14180, Line(l, "HL1").Center, 3);
            Assert.Equal(13768, Line(l, "L1").Center, 3);
            Assert.Equal(57, Line(l, "HT1").Center, 3);
            Assert.Equal(537, Line(l, "T1").Center, 0);
        }

        [Fact]
        public void Lines_Port_SameAsCenter()
        {
            var c = StructureNaming.Lines(Cas7D16C(), PanelPosition.Center).Select(x => x.Name + "@" + x.Center.ToString("0")).ToArray();
            var p = StructureNaming.Lines(Cas7D16C(), PanelPosition.Port).Select(x => x.Name + "@" + x.Center.ToString("0")).ToArray();
            Assert.Equal(c, p);
        }

        [Fact]
        public void Lines_DiagonalGoesToNearestAxis()
        {
            var l = StructureNaming.Lines(new[] { P(NamingKind.HP, 0, 0, 1000, 300) }, PanelPosition.Center);
            Assert.Equal(NamingDir.L, l.Single().Dir);
            Assert.Equal("HL1", l.Single().Name);
        }

        [Fact]
        public void Lines_ShortPieceMakesOwnLine()
        {
            var pieces = Cas7D16C();
            pieces.Add(HX(NamingKind.HP, 57, 60, 262));
            var l = StructureNaming.Lines(pieces, PanelPosition.Center);
            Assert.Equal(57, Line(l, "HL1").Center, 3);
            Assert.Equal(87, Line(l, "HL2").Center, 3);
        }

        [Fact]
        public void Lines_EmptyInput_Empty() => Assert.Empty(StructureNaming.Lines(new NamingPiece[0], PanelPosition.Center));

        [Fact]
        public void Resolve_UserNameOverrides()
        {
            var l = StructureNaming.Lines(Cas7D16C(), PanelPosition.Center);
            StructureNaming.Resolve(l, new Dictionary<string, string> { ["T6a"] = "T6a" });
            var t = l.Single(x => x.Pieces.Any(p => p.Key == "T6a"));
            Assert.Equal("T6a", t.Name);
            Assert.True(t.IsUserName);
            Assert.Equal("T7", t.AutoName);
            Assert.False(Line(l, "T8").IsUserName);
        }

        [Fact]
        public void Resolve_MostCommonName()
        {
            var l = StructureNaming.Lines(new[]
            {
                VY(NamingKind.Web, 100, 0, 100, key: "a"), VY(NamingKind.Web, 100, 200, 300, key: "b"), VY(NamingKind.Web, 100, 400, 500, key: "c"),
            }, PanelPosition.Center);
            StructureNaming.Resolve(l, new Dictionary<string, string> { ["a"] = "B", ["b"] = "A", ["c"] = "A" });
            Assert.Equal("A", l.Single().Name);
        }

        [Fact]
        public void Resolve_StaleKeysIgnored()
        {
            var l = StructureNaming.Lines(Cas7D16C(), PanelPosition.Center);
            StructureNaming.Resolve(l, new Dictionary<string, string> { ["gone"] = "X" });
            Assert.DoesNotContain(l, x => x.IsUserName);
            Assert.Equal("T1", l[0].Name);
        }

        [Fact]
        public void Resolve_ClearedStoreGoesBackToAuto()
        {
            var l = StructureNaming.Lines(Cas7D16C(), PanelPosition.Center);
            StructureNaming.Resolve(l, new Dictionary<string, string> { ["T6a"] = "T6a" });
            StructureNaming.Resolve(l, new Dictionary<string, string>());
            Assert.Equal("T7", l.Single(x => x.Pieces.Any(p => p.Key == "T6a")).Name);
        }

        [Fact]
        public void NamesJson_RoundTrip()
        {
            var d = NamesJson.Deserialize(NamesJson.Serialize(new Dictionary<string, string> { ["1B2C0#0"] = "T3", ["x"] = "BS" }));
            Assert.Equal("T3", d["1B2C0#0"]);
            Assert.Equal(2, d.Count);
        }

        [Fact]
        public void NamesJson_GarbageIsEmpty()
        {
            Assert.Empty(NamesJson.Deserialize(null));
            Assert.Empty(NamesJson.Deserialize(""));
            Assert.Empty(NamesJson.Deserialize("{not json"));
        }

        [Fact]
        public void OnePerPanel_SameBlockTwice_KeepsFirst()   // review I2: 2 lần chèn cùng block 2D = 1 bộ tên
        {
            var kept = MCG_3DPanel.UI.BulkEdit.OnePer(new[] { "A:1", "B:2", "A:1", "C:3" }, s => s, out int dropped);
            Assert.Equal(new[] { "A:1", "B:2", "C:3" }, kept);
            Assert.Equal(1, dropped);
        }

        [Fact]
        public void NamesJson_MergeLoaded_KeepsEditMadeBeforeLoad()   // review I3
        {
            var cache = new Dictionary<string, string> { ["a"] = "NEW" };
            NamesJson.MergeLoaded(cache, new Dictionary<string, string> { ["a"] = "OLD", ["b"] = "T6a" });
            Assert.Equal("NEW", cache["a"]);
            Assert.Equal("T6a", cache["b"]);
        }

        internal static List<NamingPiece> Cas7D17C()
        {
            var l = new List<NamingPiece>();
            Action<NamingKind, double, double, double, double, double, string> a =
                (k, x0, y0, x1, y1, t, key) => l.Add(Spec(P(k, x0, y0, x1, y1, t, key)));
            // Web xiên (4 đoạn)
            a(NamingKind.Web, 281.6, -895.7, 502.7, -1329.3, 10, "1ADF9");
            a(NamingKind.Web, 4541.6, -9250.8, 504.5, -1328.4, 6, "1AE23");
            a(NamingKind.Web, 6380.7, -12864.3, 4539.8, -9251.7, 10, "1AE5C");
            a(NamingKind.Web, 6382.5, -12863.4, 10492.1, -20928.1, 6, "1AE5D");
            // HP xiên (3 đoạn)
            a(NamingKind.HP, 83.8, -921.5, 4696.6, -9974.0, 6, "1AE8A");
            a(NamingKind.HP, 4801.0, -10178.9, 5012.3, -10593.5, 6, "1AE8B");
            a(NamingKind.HP, 5116.7, -10798.5, 10407.7, -21181.9, 6, "1AE8C");
            // Web thẳng
            a(NamingKind.Web, 2438.1, -5110.4, 2438.1, -469.0, 6, "1AE47");
            a(NamingKind.Web, 8397.4, -10074.2, 8397.4, -469.0, 6, "1AE07");
            a(NamingKind.Web, 8397.4, -16804.8, 8397.4, -11017.7, 6, "1AE3C");
            return l;
        }

        [Fact] public void Cas7D17C_SkewWebIsOneLine()
        {
            var l = StructureNaming.Lines(Cas7D17C(), PanelPosition.Center);
            Assert.Equal(new[] { "T1", "T2", "T3" }, Names(l, NamingKind.Web, NamingDir.T));
            var t2 = Line(l, "T2");
            Assert.Equal(new[] { "1ADF9", "1AE23", "1AE5C", "1AE5D" }, t2.Pieces.Select(p => p.Key).OrderBy(k => k).ToArray());
            Assert.Equal(5386.85, t2.Center, 1);   // giữa X 281.6..10492.1
            Assert.Equal(2438.1, Line(l, "T1").Center, 1);
            Assert.Equal(8397.4, Line(l, "T3").Center, 1);
        }

        [Fact] public void Cas7D17C_HpIsSeparateLine()
        {
            var l = StructureNaming.Lines(Cas7D17C(), PanelPosition.Center);
            Assert.Equal(new[] { "HT1" }, Names(l, NamingKind.HP, NamingDir.T));
            Assert.Equal(3, Line(l, "HT1").Pieces.Count);
            Assert.Equal(5245.75, Line(l, "HT1").Center, 1);
        }

        [Fact] public void Skew_SameCenterAsStraight_NotMerged()
        {
            var l = StructureNaming.Lines(new[] { P(NamingKind.Web, 1000, 0, 1000, 5000), P(NamingKind.Web, 0, 5000, 2000, 1000) }, PanelPosition.Center);
            Assert.Equal(2, l.Count);
        }

        [Fact] public void Skew_Near45_StaysOneLine()
        {
            // Đoạn đầu nghiêng về L, đoạn sau về T, cùng 1 đường trong 1°
            var l = StructureNaming.Lines(new[] { P(NamingKind.Web, 0, 0, 1000, 999), P(NamingKind.Web, 1100, 1099, 2100, 2100) }, PanelPosition.Center);
            Assert.Single(l);
        }

        [Fact] public void Skew_ParallelFarApart_TwoLines()
        {
            var l = StructureNaming.Lines(new[] { P(NamingKind.Web, 0, 0, 1000, 500), P(NamingKind.Web, 0, 100, 1000, 600) }, PanelPosition.Center);
            Assert.Equal(2, l.Count);
        }

        [Fact] public void Skew_DeletedLongest_RestStayOneLine()
        {
            var l = StructureNaming.Lines(Cas7D17C(), PanelPosition.Center, new[] { "1AE5D" });
            var skew = l.Single(x => x.Kind == NamingKind.Web && x.Pieces.Any(p => p.Key == "1AE23"));
            Assert.Equal(3, skew.Pieces.Count);
        }

        [Fact] public void Skew_NearAxis_KeepsAxisPath()
        {
            // Lệch 0.14° như flange thật ⇒ không phải xiên, tính như đoạn thẳng trục
            var l = StructureNaming.Lines(new[] { P(NamingKind.Web, 0, 0, 1000, 2.4), P(NamingKind.Web, 2000, 3, 3000, 5) }, PanelPosition.Center);
            Assert.Single(l);
        }

        private static List<NamingPiece> Cas7D17CWithFlanges()
        {
            var l = Cas7D17C();
            Action<double, double, double, double, double, string> f = (x0, y0, x1, y1, w, key) =>
            { var p = P(NamingKind.Flange, x0, y0, x1, y1, w, key); p.Height = 20; l.Add(p); };
            f(436.5, -1195.0, 2106.7, -4472.7, 150, "1AE46");
            f(2477.5, -5200.4, 4217.9, -8615.7, 150, "1AE49");
            f(5653.7, -11433.2, 5852.5, -11823.4, 150, "1AE4D");
            f(6438.3, -12973.0, 8056.0, -16147.5, 150, "1AE5E");
            f(10156.5, -20269.4, 8472.2, -16964.3, 150, "1AE75");
            f(2239.0, -4405.3, 2609.8, -5133.0, 447, "1AE45");   // tấm nối
            f(8608.5, -16894.9, 8192.2, -16078.1, 456, "1AE60"); // tấm nối
            f(2438.1, -4382.3, 2438.1, -1076.0, 80, "1AE39");
            f(8397.4, -16057.2, 8397.4, -11212.8, 80, "1AE27");
            f(8397.4, -9862.5, 8397.4, -656.0, 80, "1AE33");
            return l;
        }

        [Fact] public void Cas7D17C_SkewWebSize()
        {
            var l = StructureNaming.Lines(Cas7D17CWithFlanges(), PanelPosition.Center);
            Assert.Equal("WEB 374x6.0/FLANGE 150x20.0", Line(l, "T2").Spec);
        }

        [Fact] public void Cas7D17C_FlangesNotCrossed()
        {
            var l = StructureNaming.Lines(Cas7D17CWithFlanges(), PanelPosition.Center);
            Assert.Equal(new[] { "1AE45", "1AE46", "1AE49", "1AE4D", "1AE5E", "1AE60", "1AE75" }, Line(l, "T2").Flanges.Select(f => f.Key).OrderBy(k => k).ToArray());
            Assert.Equal(new[] { "1AE39" }, Line(l, "T1").Flanges.Select(f => f.Key).ToArray());
            Assert.Equal(new[] { "1AE27", "1AE33" }, Line(l, "T3").Flanges.Select(f => f.Key).OrderBy(k => k).ToArray());
        }

        [Fact] public void Merge_SkewWithStraight_KeepsBothFlanges()
        {
            var merges = new[] { new[] { "1AE5D", "1AE47" } };
            var l = StructureNaming.Lines(Cas7D17CWithFlanges(), PanelPosition.Center, null, merges);
            var m = l.Single(x => x.IsMerged);
            Assert.Contains(m.Flanges, f => f.Key == "1AE75");
            Assert.Contains(m.Flanges, f => f.Key == "1AE39");
            Assert.Equal(8437.3, m.Center, 1);   // tuyến trộn xiên + thẳng ⇒ tâm đoạn dài nhất (1AE5D)
        }

        // Web xiên (0,0)-(1000,500) + flange rộng 150 song song, lệch vuông góc d mm, dời dọc trục `along` mm.
        private static NamingPiece SkewFlange(double d, double along, string key)
        {
            double len = Math.Sqrt(1000 * 1000 + 500 * 500), ux = 1000 / len, uy = 500 / len;
            double sx = along * ux - d * uy, sy = along * uy + d * ux;
            var f = P(NamingKind.Flange, sx, sy, sx + 1000, sy + 500, 150, key);
            f.Height = 20;
            return f;
        }

        [Fact] public void SkewFlange_PerpDistCutoff()
        {
            var near = new[] { Web(P(NamingKind.Web, 0, 0, 1000, 500, 6, "w")), SkewFlange(75 + 14, 0, "f") };
            Assert.Single(StructureNaming.Lines(near, PanelPosition.Center).Single().Flanges);
            var far = new[] { Web(P(NamingKind.Web, 0, 0, 1000, 500, 6, "w")), SkewFlange(75 + 16, 0, "f") };
            Assert.Empty(StructureNaming.Lines(far, PanelPosition.Center).Single().Flanges);
        }

        [Fact] public void SkewFlange_PastWebEnd_NotAttached()
        {
            var l = new[] { Web(P(NamingKind.Web, 0, 0, 1000, 500, 6, "w")), SkewFlange(0, 2000, "f") };
            Assert.Empty(StructureNaming.Lines(l, PanelPosition.Center).Single().Flanges);
        }

        private static List<NamingPiece> MirroredV() => new List<NamingPiece>
        {
            Web(P(NamingKind.Web, 0, 0, 1000, 500, 6, "A")),
            Web(P(NamingKind.Web, 1000, 500, 2000, 0, 6, "B")),
        };

        [Fact] public void Skew_CenterTie_NumberingIndependentOfInputOrder()
        {
            var fwd = StructureNaming.Lines(MirroredV(), PanelPosition.Center);
            var rev = StructureNaming.Lines(Enumerable.Reverse(MirroredV()).ToList(), PanelPosition.Center);
            Assert.Equal(2, fwd.Count);
            foreach (var key in new[] { "A", "B" })
                Assert.Equal(fwd.Single(x => x.Pieces.Any(p => p.Key == key)).Name, rev.Single(x => x.Pieces.Any(p => p.Key == key)).Name);
        }

        [Fact] public void Merge_InsideOneSkewLine_NoOp()
        {
            var l = StructureNaming.Lines(Cas7D17C(), PanelPosition.Center, null, new[] { new[] { "1AE23", "1AE5D" } });
            Assert.DoesNotContain(l, x => x.IsMerged);
        }

        [Fact] public void Skew_FlatBars_OneBsLine()
        {
            var l = StructureNaming.Lines(new[] { P(NamingKind.FB, 0, 0, 1000, 509.5), P(NamingKind.FB, 1100, 560.5, 2000, 1019) }, PanelPosition.Center);
            Assert.Single(l);
            Assert.Equal("BS", l[0].Name);
        }

        [Fact] public void Skew_LDir_CenterUsesY()
        {
            var l = StructureNaming.Lines(new[] { P(NamingKind.Web, 0, 0, 1000, 509.5), P(NamingKind.Web, 1100, 560.5, 2000, 1019) }, PanelPosition.Center);
            Assert.Single(l);
            Assert.Equal(NamingDir.L, l[0].Dir);
            Assert.Equal("L1", l[0].Name);
            Assert.Equal(509.5, l[0].Center, 1);
        }

        private static NamingPiece[] Three() => new[]
        {
            Spec(VY(NamingKind.Web, 100, 0, 1000, key: "a")),
            Spec(VY(NamingKind.Web, 100, 1100, 2000, key: "b")),
            Spec(VY(NamingKind.Web, 100, 2100, 3000, key: "c")),
        };

        [Fact] public void CanSplit_Rules()
        {
            var l = StructureNaming.Lines(Three(), PanelPosition.Center);
            Assert.True(StructureNaming.CanSplit(l.Single()));
            var one = StructureNaming.Lines(new[] { Spec(VY(NamingKind.Web, 100, 0, 1000, key: "a")) }, PanelPosition.Center);
            Assert.False(StructureNaming.CanSplit(one.Single()));
        }

        [Fact] public void Split_EachKeyOwnLine()
        {
            var l = StructureNaming.Lines(Three(), PanelPosition.Center, null, null, new[] { "a", "b", "c" });
            Assert.Equal(new[] { "T1", "T2", "T3" }, Names(l, NamingKind.Web, NamingDir.T));
            Assert.Equal("a", Line(l, "T1").Pieces.Single().Key);
            Assert.Equal("c", Line(l, "T3").Pieces.Single().Key);
        }

        [Fact] public void Split_OnlyOneKey_RestStayTogether()
        {
            var l = StructureNaming.Lines(Three(), PanelPosition.Center, null, null, new[] { "a" });
            Assert.Equal(2, l.Count);
            Assert.Contains(l, x => x.Pieces.Count == 2);
        }

        [Fact] public void Split_SameKeyPartsStayTogether()
        {
            var p = new[] { Spec(VY(NamingKind.Web, 100, 0, 1000, key: "a")), Spec(VY(NamingKind.Web, 100, 1100, 2000, key: "a")), Spec(VY(NamingKind.Web, 100, 2100, 3000, key: "b")) };
            var l = StructureNaming.Lines(p, PanelPosition.Center, null, null, new[] { "a", "b" });
            Assert.Equal(2, l.Count);
            Assert.Equal(2, l.Single(x => x.Pieces.Any(q => q.Key == "a")).Pieces.Count);
        }

        [Fact] public void Split_HpOnWebLine_BecomesHp()
        {
            var p = new[] { Spec(HX(NamingKind.Web, 100, 0, 5000, key: "w")), Spec(HX(NamingKind.HP, 100, 5100, 6000, key: "h")) };
            Assert.Single(StructureNaming.Lines(p, PanelPosition.Center));
            var l = StructureNaming.Lines(p, PanelPosition.Center, null, null, new[] { "h" });
            Assert.Equal("HL1", l.Single(x => x.Pieces.Any(q => q.Key == "h")).Name);
        }

        [Fact] public void Split_Skew_EachKeyOwnLine()
        {
            var l = StructureNaming.Lines(Cas7D17C(), PanelPosition.Center, null, null, new[] { "1AE23", "1AE5D" });
            Assert.Single(l.Single(x => x.Pieces.Any(q => q.Key == "1AE23")).Pieces);
            Assert.Single(l.Single(x => x.Pieces.Any(q => q.Key == "1AE5D")).Pieces);
        }

        [Fact] public void Split_MergeRecombines()
        {
            var l = StructureNaming.Lines(Three(), PanelPosition.Center, null, new[] { new[] { "a", "b" } }, new[] { "a", "b", "c" });
            Assert.Equal(2, l.Count);
            Assert.True(l.Single(x => x.Pieces.Count == 2).IsMerged);
        }

        [Fact] public void Split_MergeThenUnmerge_BackToSplit()
        {
            var split = new[] { "a", "b", "c" };
            var merged = StructureNaming.Lines(Three(), PanelPosition.Center, null, new[] { new[] { "a", "b" } }, split);
            Assert.Equal(2, merged.Count);
            var back = StructureNaming.Lines(Three(), PanelPosition.Center, null, null, split);
            Assert.Equal(3, back.Count);
        }

        [Fact] public void Split_EqualCenter_StableOrder_LDir()
        {
            NamingPiece[] H() => new[]
            {
                Spec(HX(NamingKind.Web, 100, 0, 1000, key: "a")),
                Spec(HX(NamingKind.Web, 100, 1100, 2000, key: "b")),
                Spec(HX(NamingKind.Web, 100, 2100, 3000, key: "c")),
            };
            var all = new[] { "a", "b", "c" };
            var a = StructureNaming.Lines(H(), PanelPosition.Starboard, null, null, all);
            var b = StructureNaming.Lines(Enumerable.Reverse(H()), PanelPosition.Starboard, null, null, all);
            Assert.Equal(3, a.Count);
            Assert.All(a, x => Assert.Equal(NamingDir.L, x.Dir));
            Assert.Equal(a.Select(x => x.Pieces[0].Key + x.Name).OrderBy(s => s), b.Select(x => x.Pieces[0].Key + x.Name).OrderBy(s => s));
        }

        [Fact] public void Split_FlatBar_OwnLines()
        {
            var fb = new[]
            {
                Spec(VY(NamingKind.FB, 100, 0, 1000, key: "a")),
                Spec(VY(NamingKind.FB, 100, 1100, 2000, key: "b")),
            };
            var l = StructureNaming.Lines(fb, PanelPosition.Center, null, null, new[] { "a", "b" });
            Assert.Equal(2, l.Count);
            Assert.All(l, x => Assert.Equal(NamingKind.FB, x.Kind));
            Assert.All(l, x => Assert.Equal(StructureNaming.BS, x.Name));
            Assert.All(l, x => Assert.Single(x.Pieces));
        }

        [Fact] public void AutoLineKeys_ReturnsWholeLine()
        {
            var keys = StructureNaming.AutoLineKeys(Three(), PanelPosition.Center, null, new[] { "a" });
            Assert.Equal(new[] { "a", "b", "c" }, keys.OrderBy(k => k).ToArray());
            Assert.Empty(StructureNaming.AutoLineKeys(Three(), PanelPosition.Center, null, new[] { "zzz" }));
        }

        [Fact] public void Split_UnknownKey_Ignored()
        {
            var l = StructureNaming.Lines(Three(), PanelPosition.Center, null, null, new[] { "gone" });
            Assert.Single(l);
        }

        [Fact] public void Split_DeletedPiece_NoRow()
        {
            var l = StructureNaming.Lines(Three(), PanelPosition.Center, new[] { "b" }, null, new[] { "a", "b", "c" });
            Assert.Equal(new[] { "T1", "T2" }, Names(l, NamingKind.Web, NamingDir.T));
            Assert.DoesNotContain(l, x => x.Pieces.Any(q => q.Key == "b"));
        }

        [Fact] public void Split_EqualCenter_StableOrder()
        {
            var a = StructureNaming.Lines(Three(), PanelPosition.Starboard, null, null, new[] { "a", "b", "c" });
            var b = StructureNaming.Lines(Enumerable.Reverse(Three()), PanelPosition.Starboard, null, null, new[] { "a", "b", "c" });
            Assert.Equal(a.Select(x => x.Pieces[0].Key + x.Name), b.Select(x => x.Pieces[0].Key + x.Name));
        }

        [Fact] public void NamesJson_Splits_RoundTrip()
        {
            var json = NamesJson.Serialize(new Dictionary<string, string>(), null, null, new[] { "a", "b" });
            NamesJson.Deserialize(json, out _, out _, out HashSet<string> s);
            Assert.Equal(new[] { "a", "b" }, s.OrderBy(k => k).ToArray());
        }

        [Fact] public void NamesJson_NoSplits_Empty()
        {
            NamesJson.Deserialize("{\"Version\":1,\"Names\":{\"a\":\"T1\"}}", out _, out _, out HashSet<string> s);
            Assert.Empty(s);
        }
    }
}
