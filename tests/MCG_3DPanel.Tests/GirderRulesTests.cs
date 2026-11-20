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

public class GirderRunTests
{
    [Fact]   // 7D-16P girder y≈12737: t10 2B7B3 (60..887.5) + t6 2B53B (887.5..11909.5) + t10 2FB20 (11909.5..12709.5)
    public void RunSpan_WholeGirderLine() =>
        Assert.Equal(12649.5, GirderRules.RunSpan(new[] { (60.0, 887.5), (887.5, 11909.5), (11909.5, 12709.5) }), 6);

    [Fact]
    public void RunSpan_Empty_Zero() => Assert.Equal(0, GirderRules.RunSpan(new (double, double)[0]));
}
