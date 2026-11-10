using System.Collections.Generic;
using System.Linq;
using MCG_3DPanel.Services.PlanView;
using Xunit;

namespace MCG_3DPanel.Tests
{
    /// <summary>Kích thước tờ = bản sao kích thước nháp AM_9: cùng trục + khoảng, điểm gốc dời ra mép Top Plate, đường kích thước ra ngoài theo chuỗi.</summary>
    public class DraftDimRulesTests
    {
        private const int Scale = 50;

        // Top Plate (toạ độ block) 16000 × 13000 — giá trị nháp 640 lấy từ 7D-17P (CAS-0062242, block Lashing Hole).
        private static PlanViewInput Input(params PvDraft[] drafts)
        {
            var i = new PlanViewInput { Panel = "7D-17P", TopPlate = (0, 0, 16000, 13000), PlateThickness = 6 };
            i.Drafts.AddRange(drafts);
            return i;
        }

        private static PvDraft H(string src, double x1, double x2, double y) => new PvDraft { Source = src, X1 = x1, Y1 = y, X2 = x2, Y2 = y, Rotation = 0 };

        [Fact] public void LashingDraft_KeepsInterval_MovesToEdge_AM4()
        {
            var d = DraftDimRules.FromDrafts(Input(H("LashingHole", 2629.891, 3269.891, 4000)), Scale).Single();
            Assert.Equal("Mechanical-AM_4", d.Layer);
            Assert.Equal("AM_ISO$0", d.DimStyle);
            Assert.Equal(PvSpace.Block, d.Space);
            Assert.Equal(2629.891, d.X1, 3);
            Assert.Equal(3269.891, d.X2, 3);
            Assert.Equal(0, d.Y1, 3);                                    // mép dưới (gần hơn mép trên)
            Assert.Equal(0, d.Y2, 3);
            Assert.Equal(-PlanViewLayout.ChainFirstOffset * Scale, d.LineY, 3);
            Assert.Equal(0, d.Rotation, 6);
        }

        [Fact] public void StructureDraft_GoesToAM5_TopEdge()
        {
            var d = DraftDimRules.FromDrafts(Input(H("Structure", 1000, 1775, 11000)), Scale).Single();
            Assert.Equal("Mechanical-AM_5", d.Layer);
            Assert.Equal(13000, d.Y1, 3);
            Assert.Equal(13000 + PlanViewLayout.ChainFirstOffset * Scale, d.LineY, 3);
        }

        [Fact] public void VerticalDraft_LeftEdge()
        {
            var v = new PvDraft { Source = "Structure", X1 = 3000, Y1 = 1000, X2 = 3000, Y2 = 3045, Rotation = System.Math.PI / 2 };
            var d = DraftDimRules.FromDrafts(Input(v), Scale).Single();
            Assert.Equal(0, d.X1, 3);
            Assert.Equal(1000, System.Math.Min(d.Y1, d.Y2), 3);
            Assert.Equal(3045, System.Math.Max(d.Y1, d.Y2), 3);
            Assert.Equal(-PlanViewLayout.ChainFirstOffset * Scale, d.LineX, 3);
        }

        [Fact] public void TouchingDrafts_FormOneChain_SameLineY()
        {
            var dims = DraftDimRules.FromDrafts(Input(
                H("LashingHole", 709.891, 1349.891, 100), H("LashingHole", 1349.891, 1989.891, 100), H("LashingHole", 1989.891, 2629.891, 100)), Scale);
            Assert.Equal(3, dims.Count);
            Assert.Single(dims.Select(d => d.LineY).Distinct());
        }

        [Fact] public void OverlappingChains_StackOutward()
        {
            var dims = DraftDimRules.FromDrafts(Input(H("Structure", 0, 2000, 100), H("Structure", 500, 1500, 100)), Scale);
            var lines = dims.Select(d => d.LineY).OrderByDescending(y => y).ToList();
            Assert.Equal(-PlanViewLayout.ChainFirstOffset * Scale, lines[0], 3);
            Assert.Equal(-(PlanViewLayout.ChainFirstOffset + PlanViewLayout.ChainSpacing) * Scale, lines[1], 3);
        }

        [Fact] public void Shift_MovesEverything()
        {
            var d = DraftDimRules.FromDrafts(Input(H("LashingHole", 100, 740, 50)), Scale, (20000, 5)).Single();
            Assert.Equal(20100, d.X1, 3);
            Assert.Equal(5, d.Y1, 3);
        }

        [Fact] public void Overall_TwoDims_WithToleranceText_BeyondOuterChain()
        {
            var input = Input(H("Structure", 0, 2000, 100), H("Structure", 500, 1500, 100));
            var drafts = DraftDimRules.FromDrafts(input, Scale);
            var o = DraftDimRules.Overall(input, Scale, drafts);
            Assert.Equal(2, o.Count);
            Assert.All(o, d => Assert.Equal("<>{}{\\H0.71x;\\C3;%%p4}{{}}TOP PLATE", d.TextOverride));
            Assert.All(o, d => Assert.Equal("Mechanical-AM_5", d.Layer));
            var len = o.Single(d => d.Rotation == 0);
            Assert.Equal(16000, len.X2 - len.X1, 3);
            // 2 chuỗi đáy đã dùng ⇒ kích thước bao ở mức thứ 3
            Assert.Equal(-(PlanViewLayout.ChainFirstOffset + 2 * PlanViewLayout.ChainSpacing) * Scale, len.LineY, 3);
            var wid = o.Single(d => d.Rotation != 0);
            Assert.Equal(13000, System.Math.Abs(wid.Y2 - wid.Y1), 3);
            Assert.Equal(-PlanViewLayout.ChainFirstOffset * Scale, wid.LineX, 3);
        }

        [Fact] public void NoDrafts_GivesOnlyOverallAndDiameter()
        {
            var input = Input();
            input.Lashing = new PvLashing { Diameter = 55, X = 500, Y = 600 };
            Assert.Empty(DraftDimRules.FromDrafts(input, Scale));
            var o = DraftDimRules.Overall(input, Scale, new List<PvDim>());
            Assert.Equal(3, o.Count);
            var dia = o.Single(d => d.Kind == "Diametric");
            Assert.Equal("AM_ISO$3", dia.DimStyle);
            Assert.Equal("<>-LASHING HOLES", dia.TextOverride);
            Assert.Equal(55, System.Math.Abs(dia.X2 - dia.X1), 3);
        }

        [Fact] public void OuterOffset_TopSide()
        {
            var input = Input(H("Structure", 0, 2000, 12000), H("Structure", 500, 1500, 12000));
            var dims = DraftDimRules.FromDrafts(input, Scale);
            Assert.Equal(PlanViewLayout.ChainFirstOffset + PlanViewLayout.ChainSpacing, DraftDimRules.OuterOffset(dims, input, "top", Scale), 3);
            Assert.Equal(0, DraftDimRules.OuterOffset(dims, input, "left", Scale), 3);
        }
    }
}
