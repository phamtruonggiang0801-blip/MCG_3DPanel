using MCG_3DPanel.Services.Classify;
using Xunit;

public class GirderRulesTests
{
    // Max span HP = 775 ở 7D-14P và 7D-16P (log Build 2026-11-20)
    [Theory]
    [InlineData(827, true)]    // 7D-16P 32CBA (2B7B3)
    [InlineData(800, true)]    // 7D-16P 32CBF (2FB20), 7D-14P 205DD
    [InlineData(840, true)]    // 7D-14P 205E5
    [InlineData(775, false)]   // bằng max span ⇒ vẫn OB/IB
    [InlineData(600, false)]
    public void Bracket_LongerThanMaxSpan_IsGb(double len, bool gb) =>
        Assert.Equal(gb, GirderRules.IsGirderBracket(len, 775));

    [Fact]
    public void NoMaxSpan_NeverGb() => Assert.False(GirderRules.IsGirderBracket(5000, 0));

    [Theory]
    [InlineData(true, true, "GB-HP")]
    [InlineData(true, false, "GB-FB")]
    [InlineData(false, true, "GE-HP")]
    [InlineData(false, false, "GE-FB")]
    public void Codes(bool gb, bool hp, string code) => Assert.Equal(code, GirderRules.Code(gb, hp));
}
