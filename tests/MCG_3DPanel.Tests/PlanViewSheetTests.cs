using System.Collections.Generic;
using MCG_3DPanel.Services.PlanView;
using Xunit;

namespace MCG_3DPanel.Tests
{
    /// <summary>Plan View: tỉ lệ tờ, chọn khung A1 chứa panel, đổi toạ độ block → tờ (spec 2026-11-10).</summary>
    public class PlanViewSheetTests
    {
        [Theory] [InlineData(50, 50)] [InlineData(-75, 75)] [InlineData(49.9999, 50)]
        public void ScaleFromFrame_50_and_75(double sx, int expected) => Assert.Equal(expected, SheetGeometry.ScaleFromFrame(sx));

        private static readonly List<(double, double, double, double, double)> Four7D16P = new List<(double, double, double, double, double)>
        {
            (0, 0, 42050, 29700, 50), (42050, 0, 84100, 29700, 50), (84100, 0, 126150, 29700, 50), (126150, 0, 168200, 29700, 50),
        };

        [Fact] public void PickFrame_FirstOfFour_7D16P() => Assert.Equal(0, SheetGeometry.PickFrame(Four7D16P, 8071.26, 9832.41));
        [Fact] public void PickFrame_Third() => Assert.Equal(2, SheetGeometry.PickFrame(Four7D16P, 90000, 1000));
        [Fact] public void PickFrame_OutsideAll_Null() => Assert.Null(SheetGeometry.PickFrame(Four7D16P, -10, 5));

        [Fact] public void PickFrame_Nested_PicksSmallest()
        {
            var frames = new List<(double, double, double, double, double)> { (0, 0, 1000, 1000, 50), (100, 100, 500, 500, 10) };
            Assert.Equal(1, SheetGeometry.PickFrame(frames, 200, 200));
        }

        [Fact] public void ToSheet_FollowsMovedAndRotatedReference()
        {
            // xoay 90°, dời (1000, 2000): [a b c d tx ty] với x' = a·x + b·y + tx, y' = c·x + d·y + ty
            var t = new[] { 0.0, -1.0, 1.0, 0.0, 1000, 2000 };
            var p = SheetGeometry.ToSheet(10, 0, t);
            Assert.Equal(1000, p.X, 6);
            Assert.Equal(2010, p.Y, 6);
        }

        [Fact] public void IsUnrotated_MovedOnly_True() => Assert.True(SheetGeometry.IsUnrotated(new[] { 1.0, 0, 0, 1, 5000, 9000 }));
        [Fact] public void IsUnrotated_Rotated90_False() => Assert.False(SheetGeometry.IsUnrotated(new[] { 0.0, -1, 1, 0, 0, 0 }));
        [Fact] public void IsUnrotated_MirroredX_False() => Assert.False(SheetGeometry.IsUnrotated(new[] { -1.0, 0, 0, 1, 0, 0 }));
        [Fact] public void IsUnrotated_Rotated180_False() => Assert.False(SheetGeometry.IsUnrotated(new[] { -1.0, 0, 0, -1, 0, 0 }));

        [Fact] public void ModelHeight_5mm_at_75_is_375() => Assert.Equal(375, SheetGeometry.ModelHeight(5, 75));

        [Fact] public void Model_RequiredBlocks_Distinct()
        {
            var m = new PlanViewModel();
            m.Require("thk_sym"); m.Require("thk_sym"); m.Require("HP120x6");
            Assert.Equal(new[] { "thk_sym", "HP120x6" }, m.RequiredBlocks);
        }
    }
}
