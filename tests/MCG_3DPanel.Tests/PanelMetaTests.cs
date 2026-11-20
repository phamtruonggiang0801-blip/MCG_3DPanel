using System.Collections.Generic;
using System.Linq;
using MCG_3DPanel.Commands;
using MCG_3DPanel.Models;
using MCG_3DPanel.Services.Corrections;
using Xunit;

namespace MCG_3DPanel.Tests
{
    /// <summary>Dữ liệu lưu cùng panel 3D + quy tắc đường dẫn file &lt;panel&gt;_3D.dwg.</summary>
    public class PanelMetaTests
    {
        [Fact] public void Meta_RoundTrip()
        {
            var m = new PanelMeta { SourceRefHandle = "1B", SourceBtrHandle = "2C", Input = new PanelInput { WebHeight = 400, HpSpec = "150*8" }, Position = 2 };
            m.Solids.Add(new PanelMetaSolid { Handle = "A1", PartKey = "1B5#0", DeleteKey = "1B5#0", Code = "GE-HP", MergedCodes = { "GE-HP" } });
            var b = PanelMeta.FromJson(m.ToJson());
            Assert.Equal("1B", b.SourceRefHandle);
            Assert.Equal(400, b.Input.WebHeight);
            Assert.Equal("150*8", b.Input.HpSpec);
            Assert.Equal(2, b.Position);
            Assert.Equal("GE-HP", b.Solids[0].MergedCodes[0]);
        }

        [Fact] public void PanelMeta_OldJsonWithoutFingerprint_ReadsEmpty()
        {
            var b = PanelMeta.FromJson("{\"SourceRefHandle\":\"1B\",\"SourceBtrHandle\":\"2C\",\"Input\":{},\"Solids\":[]}");
            Assert.Equal("", b.SourceFingerprint);
        }

        [Fact] public void PanelMeta_Fingerprint_RoundTrips()
        {
            var m = new PanelMeta { SourceRefHandle = "1B", SourceBtrHandle = "2C", SourceFingerprint = "ab12" };
            Assert.Equal("ab12", PanelMeta.FromJson(m.ToJson()).SourceFingerprint);
        }

        [Fact] public void FromJson_OldJsonWithoutSourcePath_IsEmpty()
        {
            var b = PanelMeta.FromJson("{\"SourceRefHandle\":\"1B\",\"SourceBtrHandle\":\"2C\",\"Input\":{},\"Solids\":[]}");
            Assert.Equal("", b.SourcePath);
        }

        [Fact] public void FromJson_NullSourcePath_IsEmpty()
        {
            var b = PanelMeta.FromJson("{\"SourceRefHandle\":\"1B\",\"SourceBtrHandle\":\"2C\",\"Input\":{},\"Solids\":[],\"SourcePath\":null}");
            Assert.Equal("", b.SourcePath);
        }

        [Fact] public void ToJson_RoundTrips_SourcePath()
        {
            var m = new PanelMeta { SourceRefHandle = "1B", SourceBtrHandle = "2C", SourcePath = @"C:\a\7D-17P.dwg" };
            Assert.Equal(@"C:\a\7D-17P.dwg", PanelMeta.FromJson(m.ToJson()).SourcePath);
        }

        [Theory] [InlineData(null)] [InlineData("")] [InlineData("{bad")] [InlineData("{}")] [InlineData("[]")] [InlineData("{\"Input\":null}")] [InlineData("{\"Solids\":null}")] [InlineData("{\"Solids\":[null]}")]
        public void Meta_Invalid_Null(string json) => Assert.Null(PanelMeta.FromJson(json));

        [Fact] public void Meta_LiveSolids_DropsMissing()
        {
            var m = new PanelMeta();
            m.Solids.Add(new PanelMetaSolid { Handle = "A" });
            m.Solids.Add(new PanelMetaSolid { Handle = "B" });
            m.Solids.Add(new PanelMetaSolid { Handle = "C", Deleted = true });
            var live = m.LiveSolids(h => h == "A", out int dropped);
            Assert.Equal(new[] { "A", "C" }, live.Select(s => s.Handle));
            Assert.Equal(1, dropped);
        }

        [Fact] public void Meta_RoundTrip_KeepsSnippedEnds()
        {
            var m = new PanelMeta { SourceRefHandle = "1B", SourceBtrHandle = "2C" };
            m.Solids.Add(new PanelMetaSolid { Handle = "A", SnipLo = true });
            m.Solids.Add(new PanelMetaSolid { Handle = "B", SnipHi = true });
            var b = PanelMeta.FromJson(m.ToJson());
            Assert.True(b.Solids[0].SnipLo);
            Assert.False(b.Solids[0].SnipHi);
            Assert.True(b.Solids[1].SnipHi);
        }

        // Panel "3 part không dựng được" mở lại file vẫn phải báo như sau Build (tab Result: NotBuiltText).
        [Fact] public void Meta_RoundTrip_KeepsWarningsAndNotBuilt()
        {
            var m = new PanelMeta { SourceRefHandle = "1B", SourceBtrHandle = "2C" };
            m.Warnings.Add("Collar plate failed at HP 3.");
            m.NotBuilt.Add(new PanelMetaNotBuilt { Handle = "21913", Type = "Web", Code = "W7" });
            var b = PanelMeta.FromJson(m.ToJson());
            Assert.Equal(new[] { "Collar plate failed at HP 3." }, b.Warnings);
            Assert.Single(b.NotBuilt);
            Assert.Equal("21913", b.NotBuilt[0].Handle);
            Assert.Equal("Web", b.NotBuilt[0].Type);
            Assert.Equal("W7", b.NotBuilt[0].Code);
        }

        [Theory]
        [InlineData("{\"SourceRefHandle\":\"1B\",\"SourceBtrHandle\":\"2C\",\"Input\":{},\"Solids\":[]}")]   // file dựng trước khi có 2 trường này
        [InlineData("{\"SourceRefHandle\":\"1B\",\"SourceBtrHandle\":\"2C\",\"Input\":{},\"Solids\":[],\"Warnings\":null,\"NotBuilt\":null}")]
        [InlineData("{\"SourceRefHandle\":\"1B\",\"SourceBtrHandle\":\"2C\",\"Input\":{},\"Solids\":[],\"Warnings\":[null],\"NotBuilt\":[null]}")]
        public void Meta_WithoutWarningsOrNotBuilt_ReadsEmpty(string json)
        {
            var b = PanelMeta.FromJson(json);
            Assert.NotNull(b);
            Assert.Empty(b.Warnings);
            Assert.Empty(b.NotBuilt);
        }

        // GE/GB tách từ 1 tấm Web dùng CHUNG SourceKey với phần Web còn lại ⇒ phải khớp thêm theo loại.
        [Fact] public void PartMatcher_SharedKey_PicksByType()
        {
            var ge = new[] { "K#0", "GirderEnd" }; var rest = new[] { "K#0", "Web" };
            var match = new PartMatcher<string[]>(new[] { ge, rest }, p => p[0], p => p[1]);
            Assert.Same(rest, match.Take("K#0", "Web"));
        }

        [Fact] public void PartMatcher_SameKeyAndType_TakesInOrder()
        {
            var parts = new[] { "a", "b" };
            var match = new PartMatcher<string>(parts, p => "K#0", p => "GirderEnd");
            Assert.Equal("a", match.Take("K#0", "GirderEnd"));
            Assert.Equal("b", match.Take("K#0", "GirderEnd"));
        }

        [Fact] public void PartMatcher_TypeChanged_StillMatchesByKey()
        {
            var match = new PartMatcher<string>(new[] { "a" }, p => "K#0", p => "Web");
            Assert.Equal("a", match.Take("K#0", "ClosedPlate"));
        }

        [Fact] public void PartMatcher_AllTaken_ReusesFirst()
        {
            var match = new PartMatcher<string>(new[] { "a" }, p => "K#0", p => "Web");
            match.Take("K#0", "Web");
            Assert.Equal("a", match.Take("K#0", "Web"));
        }

        [Fact] public void PartMatcher_UnknownKey_Null()
        {
            var match = new PartMatcher<string>(new[] { "a", null }, p => p, p => "Web");
            Assert.Null(match.Take("gone#0", "Web"));
            Assert.Null(match.Take(null, "Web"));
        }

        // Solid dựng là ClosedPlate nhưng khớp vào part nay là Web ⇒ dòng và solid lệch nhau.
        [Fact] public void Solid_StoredKindDiffers_Counted() => Assert.True(new PanelMetaSolid { Type = "ClosedPlate" }.ClassifiedDifferently("Web"));
        [Fact] public void Solid_SameKind_NotCounted() => Assert.False(new PanelMetaSolid { Type = "Web" }.ClassifiedDifferently("Web"));
        [Fact] public void Solid_NoStoredKind_NotCounted() => Assert.False(new PanelMetaSolid { Type = "" }.ClassifiedDifferently("Web"));
        [Fact] public void Solid_Deleted_NotCounted() => Assert.False(new PanelMetaSolid { Type = "ClosedPlate", Deleted = true }.ClassifiedDifferently("Web"));

        [Fact] public void Target_Plain() => Assert.Equal(@"C:\p\7D-14P_Assy_3D.dwg", Panel3DPath.Target(@"C:\p", "7D-14P_Assy"));
        [Fact] public void Target_IllegalChars_Replaced() => Assert.Equal(@"C:\p\A_B_3D.dwg", Panel3DPath.Target(@"C:\p", "A/B"));
        [Fact] public void NextFree_FreeStaysSame() => Assert.Equal(@"C:\p\A_3D.dwg", Panel3DPath.NextFree(@"C:\p\A_3D.dwg", _ => false));
        [Fact] public void NextFree_SkipsTaken()
        {
            var taken = new HashSet<string> { @"C:\p\A_3D.dwg", @"C:\p\A_3D_2.dwg" };
            Assert.Equal(@"C:\p\A_3D_3.dwg", Panel3DPath.NextFree(@"C:\p\A_3D.dwg", taken.Contains));
        }
        [Fact] public void NextFree_ReservedInSameBatch()
        {
            var reserved = new HashSet<string>();
            var a = Panel3DPath.NextFree(@"C:\p\A_3D.dwg", reserved.Contains); reserved.Add(a);
            Assert.Equal(@"C:\p\A_3D_2.dwg", Panel3DPath.NextFree(@"C:\p\A_3D.dwg", reserved.Contains));
        }

        private static readonly System.DateTime T = new System.DateTime(2026, 11, 9, 14, 5, 6);

        [Fact] public void Backup_Name() => Assert.Equal(@"C:\p\A_3D.20261109_140506.bak", Panel3DPath.Backup(@"C:\p\A_3D.dwg", T, _ => false));
        [Fact] public void Backup_TakenSameSecond()
        {
            var taken = new HashSet<string> { @"C:\p\A_3D.20261109_140506.bak" };
            Assert.Equal(@"C:\p\A_3D.20261109_140506_2.bak", Panel3DPath.Backup(@"C:\p\A_3D.dwg", T, taken.Contains));
        }

        [Fact] public void Refusal_NoMeta() => Assert.Equal("A_3D.dwg has no panel data; use New copy.", Panel3DPath.UpdateRefusal("A_3D.dwg", null, "1B"));
        [Fact] public void Refusal_OtherReference()
            => Assert.Equal("A_3D.dwg was built from another 2D block; use New copy.", Panel3DPath.UpdateRefusal("A_3D.dwg", new PanelMeta { SourceRefHandle = "2C" }, "1B"));
        [Fact] public void Refusal_SameReference_Null() => Assert.Null(Panel3DPath.UpdateRefusal("A_3D.dwg", new PanelMeta { SourceRefHandle = "1b" }, "1B"));

        [Fact] public void Unmatched_CountsMissingParts() => Assert.Equal(1, Panel3DPath.CountUnmatched(new[] { "1B#0", "2C#1" }, new[] { "1B#0" }));
        [Fact] public void Unmatched_IgnoresGeneratedKeys() => Assert.Equal(0, Panel3DPath.CountUnmatched(new[] { "CollarPlate:x", "HpEndBracketB:y", "1B#0/GirderFlange#0" }, new string[0]));
    }
}
