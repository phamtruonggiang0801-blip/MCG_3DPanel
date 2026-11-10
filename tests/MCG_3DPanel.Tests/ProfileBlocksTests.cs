using MCG_3DPanel.Services.PlanView;
using Xunit;

namespace MCG_3DPanel.Tests
{
    /// <summary>Tên block profile cho mặt cắt (kiểu template CAS.dwt): "HP120x6", "BS 80x6" — tool tự vẽ khi bản vẽ lẫn template đều không có.</summary>
    public class ProfileBlocksTests
    {
        [Theory]
        [InlineData("HP120x6", "HP", 120, 6)]
        [InlineData("HP140x7", "HP", 140, 7)]
        [InlineData("BS 80x6", "BS", 80, 6)]
        [InlineData("BS 100x8.5", "BS", 100, 8.5)]
        public void TryParse_Profiles(string name, string kind, double h, double t)
        {
            Assert.True(ProfileBlocks.TryParse(name, out var k, out var hh, out var tt));
            Assert.Equal(kind, k); Assert.Equal(h, hh); Assert.Equal(t, tt);
        }

        [Theory] [InlineData("M.D1")] [InlineData("thk_sym")] [InlineData("HP120")] [InlineData("BS80x6")] [InlineData("HPx6")] [InlineData("HP0x6")]
        public void TryParse_Others_False(string name) => Assert.False(ProfileBlocks.TryParse(name, out _, out _, out _));

        [Fact] public void HpSpec_ForProfileService() => Assert.Equal("140*7", ProfileBlocks.HpSpec(140, 7));

        /// <summary>Như block "BS 80x6" của CAS.dwt: gốc góc trên-trái, rộng t sang +X, cao h xuống −Y.</summary>
        [Fact] public void FlatBarOutline_MatchesTemplate()
        {
            var v = ProfileBlocks.FlatBarOutline(80, 6);
            Assert.Equal(new[] { (0.0, 0.0), (6.0, 0.0), (6.0, -80.0), (0.0, -80.0) }, v);
        }
    }
}
