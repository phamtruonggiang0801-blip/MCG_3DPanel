using System.Collections.Generic;
using MCG_3DPanel.Models;
using MCG_3DPanel.Services.Corrections;
using MCG_3DPanel.UI;
using Xunit;

namespace MCG_3DPanel.Tests
{
    /// <summary>Kỹ sư 2026-11-05: FB chỉ nhập CHIỀU CAO (số), vẫn đọc spec cũ; sửa hàng loạt kiểu Excel.</summary>
    public class BulkEditAndSpecTests
    {
        [Theory]
        [InlineData("120", 120)]
        [InlineData(" 100.5 ", 100.5)]
        public void TryParseHeight_Number(string s, double h)
        {
            Assert.True(PanelInput.TryParseHeight(s, out double v));
            Assert.Equal(h, v, 6);
        }

        [Theory]
        [InlineData("120*6")]
        [InlineData("120x6")]
        [InlineData("120X6")]
        [InlineData("120×6")]
        [InlineData("120 × 6")]
        public void TryParseHeight_Legacy(string s)
        {
            Assert.True(PanelInput.TryParseHeight(s, out double v));
            Assert.Equal(120.0, v, 6);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("0")]
        [InlineData("-5")]
        [InlineData("abc")]
        [InlineData("120*")]
        public void TryParseHeight_Rejects(string s) => Assert.False(PanelInput.TryParseHeight(s, out _));

        [Fact]
        public void FlatBarHeight_AcceptsPlainNumber()
            => Assert.Equal(100.0, new PanelInput { FlatBarSpec = "100" }.FlatBarHeight, 6);

        [Fact]
        public void FormatSpec_UsesTimesSign() => Assert.Equal("120 × 6", PanelInput.FormatSpec("120*6"));

        private sealed class R { public string Name; public bool Can; public bool Here = true; }

        [Fact]
        public void Plan_ClickedOutsideSelection_OnlyClicked()
        {
            var a = new R { Name = "a", Can = true }; var b = new R { Name = "b", Can = true }; var c = new R { Name = "c", Can = true };
            var p = BulkEdit.Plan(c, new List<R> { a, b }, r => r.Can, r => r.Here);
            Assert.Equal(new[] { c }, p.Targets);
        }

        [Fact]
        public void Plan_SkipsUnsupported()
        {
            var a = new R { Can = true }; var b = new R { Can = false }; var c = new R { Can = true };
            var p = BulkEdit.Plan(a, new List<R> { a, b, c }, r => r.Can, r => r.Here);
            Assert.Equal(new[] { a, c }, p.Targets);
            Assert.Equal(1, p.Unsupported);
        }

        [Fact]
        public void Plan_SkipsOtherDrawing()
        {
            var a = new R { Can = true }; var b = new R { Can = true, Here = false };
            var p = BulkEdit.Plan(a, new List<R> { a, b }, r => r.Can, r => r.Here);
            Assert.Equal(new[] { a }, p.Targets);
            Assert.Equal(1, p.OtherDrawing);
        }

        [Fact]
        public void Describe_Counts()
        {
            var p = new BulkPlan<R> { Targets = new List<R> { new R(), new R() }, Unsupported = 3, OtherDrawing = 1 };
            Assert.Equal("Tripping = On on 2 rows · 3 skipped (not applicable) · 1 skipped (other drawing)",
                BulkEdit.Describe(p, "Tripping = On"));
            Assert.Equal("Tripping = On on 1 row", BulkEdit.Describe(new BulkPlan<R> { Targets = new List<R> { new R() } }, "Tripping = On"));
        }

        [Fact]
        public void RoundTrip_CollarFbHeight()
        {
            var parts = new Dictionary<string, PartCorrection>
            {
                ["218DA#0"] = new PartCorrection { Collar = true },
                ["21E00#0"] = new PartCorrection { FbHeight = 100 },
            };
            CorrectionJson.Deserialize(CorrectionJson.Serialize(parts, new string[0]), out var p2, out _);
            Assert.True(p2["218DA#0"].Collar);
            Assert.Equal(100.0, p2["21E00#0"].FbHeight);
            Assert.False(new PartCorrection { Collar = false }.IsEmpty);
            Assert.False(new PartCorrection { FbHeight = 90 }.IsEmpty);
        }
    }
}
