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
            l.Add(VY(NamingKind.Web, 4423, 452, 10057));
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
            return l;
        }

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
            // 12 thanh dọc + x=7803 phần trên (2 đoạn cách 6 → 1 dòng) + 1 thanh ngang (2 đoạn cách 66 → 2 dòng)
            Assert.Equal(12 + 1 + 2, fb.Count);
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
    }
}
