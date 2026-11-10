using System.Linq;
using MCG_3DPanel.Services.PlanView;
using Xunit;

namespace MCG_3DPanel.Tests
{
    /// <summary>Block ký hiệu tờ Plan View tool tự vẽ khi bản vẽ 2D lẫn CAS.dwt không có (kỹ sư 2026-11-10).
    /// Hình học chép từ dump CAS-0062242 (7D-17P).</summary>
    public class SymbolBlockShapesTests
    {
        [Theory]
        [InlineData("CS_symbol")] [InlineData("CS_horl_up_right")] [InlineData("thk_sym")] [InlineData("Rec. for tolerance")]
        [InlineData("M.D1")] [InlineData("M.D2")] [InlineData("Measurements_Tolerances vFuture")]
        public void Catalog_HasSheetSymbols(string name) => Assert.True(SymbolBlockShapes.Has(name));

        [Fact] public void Catalog_NotProfilesOrOthers() { Assert.False(SymbolBlockShapes.Has("HP120x6")); Assert.False(SymbolBlockShapes.Has("A1")); }

        [Fact] public void CsHorl_IsCsSymbolRotated180_AndDependsOnIt()
        {
            var s = SymbolBlockShapes.Get("CS_horl_up_right").Single();
            Assert.Equal("BlockRef", s.Kind);
            Assert.Equal("CS_symbol", s.Block);
            Assert.Equal(System.Math.PI, s.Rotation, 3);
            Assert.Equal(new[] { "CS_symbol" }, SymbolBlockShapes.Dependencies("CS_horl_up_right"));
        }

        [Fact] public void CsSymbol_FourLinesTwoSolids_AM3_Color32()
        {
            var s = SymbolBlockShapes.Get("CS_symbol");
            Assert.Equal(4, s.Count(x => x.Kind == "Line"));
            Assert.Equal(2, s.Count(x => x.Kind == "Solid"));
            Assert.All(s, x => { Assert.Equal("Mechanical-AM_3", x.Layer); Assert.Equal(32, x.Color); });
        }

        [Fact] public void ThkSym_FiveLines_AM4() => Assert.Equal(5, SymbolBlockShapes.Get("thk_sym").Count(x => x.Kind == "Line" && x.Layer == "Mechanical-AM_4"));

        [Fact] public void RecForTolerance_Box40x8_AM8()
        {
            var p = SymbolBlockShapes.Get("Rec. for tolerance").Single();
            Assert.Equal("Polyline", p.Kind);
            Assert.Equal("Mechanical-AM_8", p.Layer);
            Assert.Equal(40, p.Points.Max(q => q.Item1) - p.Points.Min(q => q.Item1), 3);
            Assert.Equal(8, p.Points.Max(q => q.Item2) - p.Points.Min(q => q.Item2), 3);
        }

        [Theory] [InlineData("M.D1", "D1")] [InlineData("M.D2", "D2")]
        public void MeasureDiagonal_ThreeRowsAndTexts(string name, string label)
        {
            var s = SymbolBlockShapes.Get(name);
            Assert.Equal(3, s.Count(x => x.Kind == "Polyline"));
            Assert.Contains(s, x => x.Kind == "MText" && x.Text == label);
            Assert.Contains(s, x => x.Kind == "MText" && x.Text == "D1-D2<10");
        }

        [Fact] public void ToleranceNote_TextAndThreeSignLines()
        {
            var s = SymbolBlockShapes.Get("Measurements_Tolerances vFuture");
            var t = s.Single(x => x.Kind == "MText");
            Assert.StartsWith("\\pxi-3,l4,sm1,t4;U.N.O.", t.Text);
            Assert.Contains("COPY OF MEASURMENT PROTOCOL TO BE RETURNED TO MacGREGOR", t.Text);
            Assert.Equal(150, t.Height);
            Assert.Equal(3, s.Count(x => x.Kind == "Line"));
        }

        [Fact] public void Layers_CoverEveryShapeLayer()
        {
            foreach (var name in new[] { "CS_symbol", "thk_sym", "Rec. for tolerance", "M.D1", "Measurements_Tolerances vFuture" })
                foreach (var shape in SymbolBlockShapes.Get(name))
                    Assert.NotNull(SymbolBlockShapes.Layer(shape.Layer));
            var am3 = SymbolBlockShapes.Layer("Mechanical-AM_3");
            Assert.Equal(6, am3.Color); Assert.Equal("AM_ISO02W050", am3.Linetype); Assert.Equal(35, am3.LineweightHundredths);
        }
    }
}
