using System.Linq;
using MCG_3DPanel.UI;
using Xunit;

namespace MCG_3DPanel.Tests
{
    public class ResultCatalogTests
    {
        private static readonly string[] None = new string[0];

        [Fact]
        public void Describe_Bracket_OB12_IsBracketOB_CanFlip()
        {
            var d = ResultCatalog.Describe("Bracket", "OB12", null, None, None, false, false);
            Assert.Equal(ResultFamily.Bracket, d.Family);
            Assert.Equal("OB", d.Kind);
            Assert.True(d.CanFlip);
            Assert.Equal(new[] { "OB", "IB", "BF", "Backing Plate" }, d.Options.Select(o => o.Text));
        }

        [Fact]
        public void Describe_WebMergedGeHp_ListsGeGbClosed()
        {
            var d = ResultCatalog.Describe("Web", "", null, new[] { "GE-HP" }, new[] { "HP" }, true, true);
            Assert.Equal(ResultFamily.Web, d.Family);
            Assert.Equal("Web + GE-HP", d.Kind);
            Assert.Equal(new[] { "Web", "Web + GE-HP", "Web + GB-HP", "Closed Plate" }, d.Options.Select(o => o.Text));
        }

        [Fact]
        public void Describe_WebNoGbAnchor_DisablesGb()
        {
            var d = ResultCatalog.Describe("Web", "", null, None, new[] { "HP" }, true, false);
            var gb = d.Options.Single(o => o.Text == "Web + GB-HP");
            Assert.False(gb.Enabled);
            Assert.Equal(ResultCatalog.NoGbAnchorTip, gb.Tooltip);
            Assert.True(d.Options.Single(o => o.Text == "Web + GE-HP").Enabled);
        }

        [Fact]
        public void Describe_WebNoGeZone_DisablesGe()
        {
            var d = ResultCatalog.Describe("Web", "", null, None, new[] { "FB" }, false, true);
            var ge = d.Options.Single(o => o.Text == "Web + GE-FB");
            Assert.False(ge.Enabled);
            Assert.Equal(ResultCatalog.NoGeZoneTip, ge.Tooltip);
        }

        [Fact]
        public void Describe_WebTwoEnds_MixedKindIsPresent()
        {
            var d = ResultCatalog.Describe("Web", "", null, new[] { "GE-HP", "GB-FB" }, new[] { "HP", "FB" }, true, true);
            Assert.Equal("Web + GE-HP + GB-FB", d.Kind);
            Assert.Equal(d.Kind, d.Options[0].Text);
            Assert.Contains(d.Options, o => o.Text == "Web + GE-HP + GE-FB");
            Assert.Contains(d.Options, o => o.Text == "Web + GB-HP + GB-FB");
        }

        [Fact]
        public void Describe_WebNoTouchingEnds_OnlyWebAndClosed()
        {
            var d = ResultCatalog.Describe("Web", "", null, None, None, false, false);
            Assert.Equal(new[] { "Web", "Closed Plate" }, d.Options.Select(o => o.Text));
        }

        [Fact]
        public void Describe_ClosedPlate_IsWebFamily()
        {
            var d = ResultCatalog.Describe("ClosedPlate", "", null, None, None, false, false);
            Assert.Equal(ResultFamily.Web, d.Family);
            Assert.Equal("Closed Plate", d.Kind);
        }

        [Fact]
        public void Describe_StandaloneGb_IsBracketFamily()
        {
            var d = ResultCatalog.Describe("GirderEnd", "GB-FB", null, None, None, true, true);
            Assert.Equal(ResultFamily.Bracket, d.Family);
            Assert.Equal("GB-FB", d.Kind);
            Assert.Equal(new[] { "GE-FB", "GB-FB", "Web" }, d.Options.Select(o => o.Text));
        }

        [Fact]
        public void Describe_StandaloneGe_IsWebFamily()
        {
            Assert.Equal(ResultFamily.Web, ResultCatalog.Describe("GirderEnd", "GE-HP", null, None, None, true, true).Family);
        }

        [Fact]
        public void Describe_WebCodeGbQuestion_ShowsPlainWeb()
        {
            Assert.Equal("Web", ResultCatalog.Describe("Web", "GB?", null, None, new[] { "HP" }, true, false).Kind);
        }

        [Fact]
        public void Describe_FlangeAndFaceplate()
        {
            var f = ResultCatalog.Describe("Flange", "", null, None, None, false, false);
            Assert.Equal(ResultFamily.Flange, f.Family);
            Assert.Equal("Main Flange", f.Kind);
            Assert.Equal(new[] { "Main Flange", "Faceplate" }, f.Options.Select(o => o.Text));
            Assert.Equal("Faceplate", ResultCatalog.Describe("FacePlate", "", null, None, None, false, false).Kind);
        }

        [Fact]
        public void Describe_BackingPlate_IsBracketFamily_NoFlip()
        {
            var d = ResultCatalog.Describe("BackingPlate", "", null, None, None, false, false);
            Assert.Equal(ResultFamily.Bracket, d.Family);
            Assert.Equal("Backing Plate", d.Kind);
            Assert.False(d.CanFlip);
        }

        [Fact]
        public void Describe_HpAndFb()
        {
            var hp = ResultCatalog.Describe("HP", "", null, None, None, false, false);
            Assert.Equal(ResultFamily.HP, hp.Family);
            Assert.True(hp.CanFlip);
            Assert.Empty(hp.Options);
            var fb = ResultCatalog.Describe("FlatBar", "", null, None, None, false, false);
            Assert.Equal(ResultFamily.FB, fb.Family);
            Assert.Equal("FB", fb.Kind);
            Assert.False(fb.CanFlip);
        }

        [Fact]
        public void Describe_Generated_AreReadOnly()
        {
            var collar = ResultCatalog.Describe(null, "", "CollarPlate", None, None, false, false);
            Assert.Equal(ResultFamily.Collar, collar.Family);
            Assert.Empty(collar.Options);
            var gf = ResultCatalog.Describe(null, "GE-HP", "GirderFlange", None, None, false, false);
            Assert.Equal(ResultFamily.Flange, gf.Family);
            Assert.Equal("Flange GE/GB-HP", gf.Kind);
            var b = ResultCatalog.Describe(null, "", "HpEndBracketB", None, None, false, false);
            Assert.Equal(ResultFamily.Bracket, b.Family);
            Assert.Equal("B", b.Kind);
            Assert.False(b.CanFlip);
        }

        [Fact]
        public void Describe_TopPlate()
        {
            var d = ResultCatalog.Describe("TopPlate", "", null, None, None, false, false);
            Assert.Equal(ResultFamily.Topplate, d.Family);
            Assert.Equal("Topplate", d.Kind);
        }

        [Theory]
        [InlineData("Web", CorrectionIntent.ForceWeb)]
        [InlineData("Web + GE-HP", CorrectionIntent.ForceGE)]
        [InlineData("GE-FB", CorrectionIntent.ForceGE)]
        [InlineData("Web + GB-HP + GB-FB", CorrectionIntent.ForceGB)]
        [InlineData("GB-FB", CorrectionIntent.ForceGB)]
        [InlineData("Closed Plate", CorrectionIntent.ClosedPlate)]
        [InlineData("Main Flange", CorrectionIntent.Flange)]
        [InlineData("Faceplate", CorrectionIntent.FacePlate)]
        [InlineData("OB", CorrectionIntent.BracketOB)]
        [InlineData("IB", CorrectionIntent.BracketIB)]
        [InlineData("BF", CorrectionIntent.BracketBF)]
        [InlineData("Backing Plate", CorrectionIntent.BackingPlate)]
        [InlineData("Topplate", CorrectionIntent.None)]
        [InlineData("", CorrectionIntent.None)]
        [InlineData(null, CorrectionIntent.None)]
        public void ParseIntent_Maps(string kind, CorrectionIntent expected)
            => Assert.Equal(expected, ResultCatalog.ParseIntent(kind));
    }
}
