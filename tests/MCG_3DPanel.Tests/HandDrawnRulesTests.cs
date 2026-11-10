using System;
using System.Collections.Generic;
using MCG_3DPanel.Services.PlanView;
using Xunit;

namespace MCG_3DPanel.Tests
{
    /// <summary>Replace chỉ xoá thứ tool sẽ vẽ lại; giữ DETAIL, 1A-1A, REF. DIM, (&lt;&gt;), kích thước tổng (spec 2026-11-10 quyết định 6).</summary>
    public class HandDrawnRulesTests
    {
        private static readonly List<(double, double, double)> Known = new List<(double, double, double)> { (0, 1000, 1640), (Math.PI / 2, 200, 2245) };
        private static readonly (double, double, double, double) Plate = (0, 0, 16000, 13000);

        private static bool R(PvExisting e) => HandDrawnRules.IsReplaceable(e, "7D-17P", Known, Plate);
        private static PvExisting T(string layer, string text) => new PvExisting { Type = "Text", Layer = layer, Text = text, InModelSpace = true };

        [Theory]
        [InlineData("Mechanical-AM_1", "T1")] [InlineData("Mechanical-AM_6", "HT12")] [InlineData("Mechanical-AM_1", "L6")]
        [InlineData("Mechanical-AM_1", "T6a")] [InlineData("Mechanical-AM_6", "BS")] [InlineData("Mechanical-AM_6", "B")]
        [InlineData("Mechanical-AM_6", "OB1")] [InlineData("Mechanical-AM_6", "GE1")] [InlineData("Mechanical-AM_6", "S")]
        [InlineData("Mechanical-AM_6", "GB")] [InlineData("Mechanical-AM_6", "HL2")]
        public void Replaceable_Labels(string layer, string text) => Assert.True(R(T(layer, text)));

        [Theory]
        [InlineData("Mechanical-AM_1", "DETAIL 1")] [InlineData("Mechanical-AM_1", "1A-1A")] [InlineData("Mechanical-AM_1", "FACE PLATE")]
        [InlineData("Mechanical-AM_5", "T1")] [InlineData("Mechanical-AM_6", "20.0")]
        public void NotReplaceable_OtherTexts(string layer, string text) => Assert.False(R(T(layer, text)));

        [Fact] public void Replaceable_PlanViewTitles_AndMemberList()
        {
            Assert.True(R(new PvExisting { Type = "MText", Layer = "Mechanical-AM_1", Text = "PLAN VIEW\\P(ALL FITTINGS SHOWN ARE FOR REFERENCE LINE)", InModelSpace = true }));
            Assert.True(R(new PvExisting { Type = "MText", Layer = "Mechanical-AM_6", Text = "TOP PLATE THICKNESS: 6.0\\P", InModelSpace = true }));
        }

        [Fact] public void Replaceable_DimMatchingDraft_True()
            => Assert.True(R(new PvExisting { Type = "Dim", Layer = "Mechanical-AM_4", X1 = 1000.4, Y1 = 50, X2 = 1640, Y2 = 50, Rotation = 0, Text = "", InModelSpace = true }));

        [Fact] public void Replaceable_OverallLength_True()
            => Assert.True(R(new PvExisting { Type = "Dim", Layer = "Mechanical-AM_5", X1 = 0, Y1 = 0, X2 = 16000, Y2 = 0, Rotation = 0, Text = "<>{}{\\H0.71x;\\C3;%%p4}{{}}TOP PLATE", InModelSpace = true }));

        [Fact] public void ChainTotal_NotMatching_False()
            => Assert.False(R(new PvExisting { Type = "Dim", Layer = "Mechanical-AM_5", X1 = 0, Y1 = 0, X2 = 6404, Y2 = 0, Rotation = 0, Text = "", InModelSpace = true }));

        [Fact] public void ReferenceDim_ParenText_False()
            => Assert.False(R(new PvExisting { Type = "Dim", Layer = "Mechanical-AM_4", X1 = 1000, Y1 = 50, X2 = 1640, Y2 = 50, Rotation = 0, Text = "(<>)", InModelSpace = true }));

        [Theory]
        [InlineData("7D-17P_X-X")] [InlineData("7D-17P_Y-Y")] [InlineData("T.7D-17P_X-X")] [InlineData("CS_horl_up_right")] [InlineData("CS_symbol")]
        [InlineData("thk_sym")] [InlineData("M.D1")] [InlineData("M.D2")] [InlineData("Rec. for tolerance")] [InlineData("Measurements_Tolerances vFuture")]
        [InlineData("7D-17P_Top Plate")] [InlineData("7D-17P_Lashing Hole")]
        public void Replaceable_Blocks(string name) => Assert.True(R(new PvExisting { Type = "BlockRef", BlockName = name, InModelSpace = true }));

        [Fact] public void OtherPanelsBlocks_False() => Assert.False(R(new PvExisting { Type = "BlockRef", BlockName = "7D-16P_X-X", InModelSpace = true }));

        [Fact] public void LashingPlanInsideAssy_False()
            => Assert.False(R(new PvExisting { Type = "BlockRef", BlockName = "7D-17P_Lashing Hole", InModelSpace = false }));

        [Fact] public void OwnMarked_IsOwn_AndNotHandDrawn()
        {
            var e = T("Mechanical-AM_1", "T1"); e.HasOwnMark = true;
            Assert.True(HandDrawnRules.IsOwn(e));
            Assert.False(R(e));
        }
    }
}
