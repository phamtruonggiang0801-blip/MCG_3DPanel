using MCG_3DPanel.Models;
using MCG_3DPanel.Services.Corrections;
using Xunit;

namespace MCG_3DPanel.Tests
{
    public class SourceMetaTests
    {
        [Fact]
        public void SourceMeta_RoundTrip()
        {
            var m = new SourceMeta
            {
                SourceBtrHandle = "1A2B",
                Transform = new double[] { 1, 0, 0, 1500.5, 0, 1, 0, -20, 0, 0, 1, 0, 0, 0, 0, 1 },
                Input = new PanelInput { TopPlateThickness = 8, WebHeight = 280, FlangeThickness = 12, FlatBarSpec = "100*10", HpSpec = "160*9" },
                Position = PanelPosition.Starboard,
            };
            var back = SourceMeta.FromJson(m.ToJson());
            Assert.Equal("1A2B", back.SourceBtrHandle);
            Assert.Equal(m.Transform, back.Transform);
            Assert.Equal(280, back.Input.WebHeight);
            Assert.Equal("160*9", back.Input.HpSpec);
            Assert.Equal(PanelPosition.Starboard, back.Position);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("{broken")]
        [InlineData("{\"SourceBtrHandle\":\"\",\"Transform\":[1,2]}")]
        public void FromJson_Invalid_ReturnsNull(string json) => Assert.Null(SourceMeta.FromJson(json));
    }
}
