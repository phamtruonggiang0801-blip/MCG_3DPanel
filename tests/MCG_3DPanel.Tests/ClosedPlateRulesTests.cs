using MCG_3DPanel.Services.Classify;
using Xunit;

namespace MCG_3DPanel.Tests
{
    /// <summary>Số thật 7D-17P (CAS.dump.json), kỹ sư 2026-11-06: 1B5FA là Closed Plate, 1B608 là GE 200.</summary>
    public class ClosedPlateRulesTests
    {
        // 1B5FA (L200): đầu web 1B5F9 cách đầu tấm 10.1, đầu web 1B5FB cách 170.
        [Fact] public void Bridges_1B5FA() => Assert.True(ClosedPlateRules.BridgesBothHalves(new[] { 10.1, 170.0 }, 200));
        [Fact] public void Bridges_OneWebOnly_False() => Assert.False(ClosedPlateRules.BridgesBothHalves(new[] { 10.1 }, 200));
        [Fact] public void Bridges_SameHalf_False() => Assert.False(ClosedPlateRules.BridgesBothHalves(new[] { 10.0, 60.0 }, 200));
        [Fact] public void Bridges_None_False() => Assert.False(ClosedPlateRules.BridgesBothHalves(new double[0], 200));

        // 1B608: không có Web vuông góc, Closed Plate 1B5F8 cách 200 ⇒ GE 200.
        [Fact] public void PerpEdge_NoWeb_UsesClosedPlate() => Assert.Equal(200, ClosedPlateRules.PerpEdge(-1, 200));
        // 1B5FF: Web 1B606 cách 469, Closed Plate 1B604 cách 250 ⇒ giữ 469.
        [Fact] public void PerpEdge_WebWins_EvenIfClosedPlateNearer() => Assert.Equal(469, ClosedPlateRules.PerpEdge(469, 250));
        [Fact] public void PerpEdge_Nothing_Minus1() => Assert.Equal(-1, ClosedPlateRules.PerpEdge(-1, -1));
    }
}
