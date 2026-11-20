using MCG_3DPanel.Services.Detail;
using Xunit;

public class SectionCleanupRulesTests
{
    [Fact]
    public void CreatedThisRun_OnlyHandlesAtOrAboveSeed()
    {
        Assert.True(SectionCleanupRules.CreatedThisRun(0x200, 0x200));
        Assert.True(SectionCleanupRules.CreatedThisRun(0x200, 0x2A1));
        Assert.False(SectionCleanupRules.CreatedThisRun(0x200, 0x1FF));   // bản ghi có sẵn của bản vẽ
        Assert.False(SectionCleanupRules.CreatedThisRun(0x200, 0x10));
    }

    [Fact]
    public void FailureMessage_KeepsBothErrors()
    {
        string m = SectionCleanupRules.FailureMessage("eGeneralModelingFailure", "2 object(s) could not be erased");
        Assert.Contains("eGeneralModelingFailure", m);
        Assert.Contains("2 object(s) could not be erased", m);
    }

    [Fact]
    public void Count_Warning_OnlyWhenPositive()
    {
        Assert.Null(SectionCleanupRules.Count(0, "solid(s) could not be clipped"));
        Assert.Equal("3 solid(s) could not be clipped", SectionCleanupRules.Count(3, "solid(s) could not be clipped"));
    }
}

public class UnionPlausibleTests
{
    [Fact] public void Overlapping_Ok() => Assert.True(SectionCleanupRules.UnionPlausible(1000, 300, 1250));
    [Fact] public void Disjoint_Ok() => Assert.True(SectionCleanupRules.UnionPlausible(1000, 300, 1300));
    [Fact] public void PartSwallowed_Ok() => Assert.True(SectionCleanupRules.UnionPlausible(1000, 300, 1000));
    [Fact] public void LostEarlierParts_Rejected()   // DET.19: khối gộp mất flange + HP
        => Assert.False(SectionCleanupRules.UnionPlausible(5.0e7, 2.0e5, 4.0e7));
    [Fact] public void Grew_Rejected() => Assert.False(SectionCleanupRules.UnionPlausible(1000, 300, 1500));
    [Fact] public void NoAfter_Rejected() => Assert.False(SectionCleanupRules.UnionPlausible(1000, 300, null));
    [Fact] public void ZeroAfter_Rejected() => Assert.False(SectionCleanupRules.UnionPlausible(1000, 300, 0));
    [Fact] public void UnknownBefore_NotChecked() => Assert.True(SectionCleanupRules.UnionPlausible(null, 300, 50));
}
