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
    }
}
