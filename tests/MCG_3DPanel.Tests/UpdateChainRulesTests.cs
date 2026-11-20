using MCG_3DPanel.Services.Update;
using Xunit;

public class UpdateChainRulesTests
{
    [Fact] public void AllOk() => Assert.Equal("3D, Plan View and Details updated.", UpdateChainRules.Summary(true, null, true, null, 0));
    [Fact] public void NoDrawings() => Assert.Equal("3D updated.", UpdateChainRules.Summary(false, null, false, null, 0));
    [Fact] public void PlanViewOnly() => Assert.Equal("3D and Plan View updated.", UpdateChainRules.Summary(true, null, false, null, 0));
    [Fact] public void DetailsOnly() => Assert.Equal("3D and Details updated.", UpdateChainRules.Summary(false, null, true, null, 0));
    [Fact] public void PlanViewFailed() =>
        Assert.Equal("3D updated; Plan View failed: X. Details were not updated.", UpdateChainRules.Summary(true, "X", true, null, 0));
    [Fact] public void DetailFailed() =>
        Assert.Equal("3D and Plan View updated; Details failed: Y.", UpdateChainRules.Summary(true, null, true, "Y", 0));
    [Fact] public void Applied3() =>
        Assert.Equal("3D updated. 3 edits from the 2D drawing applied.", UpdateChainRules.Summary(false, null, false, null, 3));
    [Fact] public void Applied1() =>
        Assert.Equal("3D updated. 1 edit from the 2D drawing applied.", UpdateChainRules.Summary(false, null, false, null, 1));
}
