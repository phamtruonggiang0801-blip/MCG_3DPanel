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

public class UpdateDrawingRulesTests
{
    [Fact] public void PlanAndDetails_NamesApplied_GeometryWaits() =>
        Assert.Equal("Plan View and Details updated with 2 waiting names. 3 edits still wait for Update 3D Model.",
            UpdateChainRules.DrawingSummary(true, null, true, null, 2, 3));
    [Fact] public void PlanOnly_NothingWaiting() =>
        Assert.Equal("Plan View updated.", UpdateChainRules.DrawingSummary(true, null, false, null, 0, 0));
    [Fact] public void OneName() =>
        Assert.Equal("Plan View updated with 1 waiting name.", UpdateChainRules.DrawingSummary(true, null, false, null, 1, 0));
    [Fact] public void PlanFailed() =>
        Assert.Equal("Plan View failed: X. Details were not updated.", UpdateChainRules.DrawingSummary(true, "X", true, null, 0, 0));
    [Fact] public void DetailsFailed() =>
        Assert.Equal("Plan View updated; Details failed: Y. 1 edit still waits for Update 3D Model.",
            UpdateChainRules.DrawingSummary(true, null, true, "Y", 0, 1));
    [Fact] public void NoDrawing() =>
        Assert.Equal("This panel has no Plan View or details yet — use Create Drawing.", UpdateChainRules.DrawingSummary(false, null, false, null, 0, 0));
}
