using MCG_3DPanel.Services.Detail;
using Xunit;

namespace MCG_3DPanel.Tests
{
    public class DetailNamingTests
    {
        [Fact]
        public void NextDetailNumber_CountsHandMade()
        {
            var names = new[] { "7D-16C_DET.1", "7D-16C_DET4_4A-4A", "7D-16C_DET.5", "NEW_7D-17C_DET.10_10A-10A" };
            Assert.Equal(6, DetailNaming.NextDetailNumber("7D-16C", names));
        }

        [Fact]
        public void NextDetailNumber_OtherPanelIgnored()
        {
            var names = new[] { "7D-16CX_DET.9", "7D-17C_DET.3", "7D-16C_DET.2_2A-2A" };
            Assert.Equal(3, DetailNaming.NextDetailNumber("7D-16C", names));
            Assert.Equal(1, DetailNaming.NextDetailNumber("7D-99", names));
        }

        [Fact]
        public void NextDetailNumber_NewPrefixSamePanel()
        {
            Assert.Equal(8, DetailNaming.NextDetailNumber("7D-17C", new[] { "NEW_7D-17C_DET.7_7A-7A" }));
        }

        [Fact]
        public void NextSectionLetter_SkipsUsed()
        {
            Assert.Equal("C", DetailNaming.NextSectionLetter(new[] { "A", "B" }));
            Assert.Equal("A", DetailNaming.NextSectionLetter(new string[0]));
        }

        [Fact]
        public void NextSectionLetter_AfterZ()
        {
            var all = new System.Collections.Generic.List<string>();
            for (char c = 'A'; c <= 'Z'; c++) all.Add(c.ToString());
            Assert.Equal("AA", DetailNaming.NextSectionLetter(all));
        }

        [Fact]
        public void Names_And_Titles()
        {
            Assert.Equal("7D-16C_DET.2", DetailNaming.DetailBlock("7D-16C", 2));
            Assert.Equal("7D-16C_DET.2_2B-2B", DetailNaming.SectionBlock("7D-16C", 2, "B"));
            Assert.Equal("2B", DetailNaming.SectionMark(2, "B"));
            Assert.Equal("%%UDETAIL 2", DetailNaming.DetailTitle(2));
            Assert.Equal("DET.2", DetailNaming.PlanLabel(2));
            Assert.Equal("1A-1A", DetailNaming.SectionTitle(1, "A", false));
        }

        [Fact]
        public void SectionTitle_Vertical()
        {
            Assert.Equal("1D-1D  90°ROTATED", DetailNaming.SectionTitle(1, "D", true));
        }
    }
}
