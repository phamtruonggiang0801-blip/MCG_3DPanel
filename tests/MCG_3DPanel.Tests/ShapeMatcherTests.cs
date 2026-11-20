using System.Collections.Generic;
using System.Linq;
using MCG_3DPanel.Services.Detail;
using MCG_3DPanel.Services.Update;
using Xunit;

public class ShapeMatcherTests
{
    const string S = "7D-16P_Structure";

    static ShapeRecord R(string h, string block, string layer, bool closed, params (double x, double y)[] pts) => new ShapeRecord
    {
        Handle = h, Block = block, Type = "Polyline", Layer = layer, Closed = closed,
        Points = pts.Select(p => new P2(p.x, p.y)).ToList(),
        Bulges = pts.Select(_ => 0.0).ToList(),
    };

    static ShapeRecord Rect(string h, double x, double y, double w = 827, double t = 10, string layer = "Mechanical-AM_0", string block = S)
        => R(h, block, layer, true, (x, y), (x + w, y), (x + w, y + t), (x, y + t));

    static ShapeRecord Shift(ShapeRecord r, string h, double dx, double dy) => new ShapeRecord
    {
        Handle = h, Block = r.Block, Type = r.Type, Layer = r.Layer, Closed = r.Closed, Name = r.Name,
        Points = r.Points.Select(p => new P2(p.X + dx, p.Y + dy)).ToList(),
        Bulges = r.Bulges.ToList(), Scalars = r.Scalars.ToList(),
    };

    [Fact]
    public void SameShapes_NewHandles_AllMapped()
    {
        var o = new[] { Rect("2B7B3", 60, 12734), Rect("2B7B5", 324, 12744, 455, 10) };
        var n = new[] { Shift(o[0], "A1", 0, 0), Shift(o[1], "A2", 0, 0) };
        var m = ShapeMatcher.Match(o, n);
        Assert.Equal("A1", m.Map["2B7B3"]);
        Assert.Equal("A2", m.Map["2B7B5"]);
        Assert.Empty(m.Shifts);
    }

    [Fact]
    public void MovedVertex_NotMapped()
    {
        var o = Rect("2B7B3", 60, 12734);
        var n = Shift(o, "A1", 0, 0); n.Points[1] = new P2(n.Points[1].X + 0.6, n.Points[1].Y);
        Assert.False(ShapeMatcher.Match(new[] { o }, new[] { n }).Map.ContainsKey("2B7B3"));
    }

    [Fact]
    public void WithinTolerance_Mapped()
    {
        var o = Rect("2B7B3", 60, 12734);
        var n = Shift(o, "A1", 0, 0); n.Points[1] = new P2(n.Points[1].X + 0.4, n.Points[1].Y);
        Assert.Equal("A1", ShapeMatcher.Match(new[] { o }, new[] { n }).Map["2B7B3"]);
    }

    [Fact]
    public void ClosedRedrawnFromOtherStartAndReversed_Mapped()
    {
        var o = R("1", S, "L", true, (0, 0), (100, 0), (100, 10), (0, 10));
        var n = R("2", S, "L", true, (100, 10), (100, 0), (0, 0), (0, 10));
        Assert.Equal("2", ShapeMatcher.Match(new[] { o }, new[] { n }).Map["1"]);
    }

    [Fact]
    public void ReversedWithBulge_Mapped()
    {
        var o = R("1", S, "L", false, (0, 0), (100, 0), (100, 50)); o.Bulges[0] = 0.5;
        var n = R("2", S, "L", false, (100, 50), (100, 0), (0, 0)); n.Bulges[1] = -0.5;
        Assert.Equal("2", ShapeMatcher.Match(new[] { o }, new[] { n }).Map["1"]);
    }

    [Fact]
    public void ReversedWithWrongBulgeSign_NotMapped()
    {
        var o = R("1", S, "L", false, (0, 0), (100, 0), (100, 50)); o.Bulges[0] = 0.5;
        var n = R("2", S, "L", false, (100, 50), (100, 0), (0, 0)); n.Bulges[1] = 0.5;
        Assert.False(ShapeMatcher.Match(new[] { o }, new[] { n }).Map.ContainsKey("1"));
    }

    [Fact]
    public void OtherLayer_NotMapped()
    {
        var o = Rect("1", 0, 0);
        var n = Rect("2", 0, 0, layer: "Mechanical-AM_3");
        Assert.False(ShapeMatcher.Match(new[] { o }, new[] { n }).Map.ContainsKey("1"));
    }

    [Fact]
    public void UnchangedHandle_MapsToItself()
    {
        var a = Rect("1", 0, 0); var b = Rect("2", 0, 0);   // trùng hình: handle giống nhau phải thắng
        var m = ShapeMatcher.Match(new[] { a, b }, new[] { Shift(b, "2", 0, 0), Shift(a, "1", 0, 0) });
        Assert.Equal("1", m.Map["1"]);
        Assert.Equal("2", m.Map["2"]);
    }

    [Fact]
    public void IdenticalDuplicates_PairedInOrder()
    {
        var o = Enumerable.Range(0, 20).Select(i => Rect("O" + i, i * 1000, 0, 80, 6)).ToList();
        var n = o.Select((r, i) => Shift(r, "N" + (19 - i), 0, 0)).Reverse().ToList();
        var m = ShapeMatcher.Match(o, n);
        Assert.Equal(20, m.Map.Count);
        Assert.Equal(20, m.Map.Values.Distinct().Count());
        Assert.Equal("N19", m.Map["O0"]);
    }

    [Fact]
    public void ManyIdenticalShapes_BasePointMoved_ShiftFound()
    {
        var o = Enumerable.Range(0, 20).Select(i => Rect("O" + i, i * 1000, 0, 80, 6)).ToList();
        var n = o.Select((r, i) => Shift(r, "N" + i, 250, 0)).ToList();
        var m = ShapeMatcher.Match(o, n);
        Assert.Equal(20, m.Map.Count);
        Assert.Equal("N3", m.Map["O3"]);
        Assert.Single(m.Shifts);
    }

    [Fact]
    public void BasePointMoved_AllMappedAndShiftReported()
    {
        var o = new[] { Rect("1", 60, 12734), Rect("2", 324, 12744, 455), Rect("3", 530, 12134, 10, 600) };
        var n = o.Select((r, i) => Shift(r, "N" + i, 100, -50.2)).ToList();
        var m = ShapeMatcher.Match(o, n);
        Assert.Equal(3, m.Map.Count);
        var s = Assert.Single(m.Shifts);
        Assert.Equal(S, s.Block);
        Assert.Equal(100, s.Offset.X, 0);
        Assert.InRange(s.Offset.Y, -50.5, -50.0);
    }

    [Fact]
    public void BasePointMovedAndTwoEdited_OthersMapped()
    {
        var o = Enumerable.Range(0, 10).Select(i => Rect("O" + i, i * 900, i * 37, 300 + i * 10)).ToList();
        var n = o.Select((r, i) => Shift(r, "N" + i, 100, 0)).ToList();
        n[2].Points[1] = new P2(n[2].Points[1].X + 50, n[2].Points[1].Y);
        n[7].Points[2] = new P2(n[7].Points[2].X, n[7].Points[2].Y + 5);
        var m = ShapeMatcher.Match(o, n);
        Assert.Equal(8, m.Map.Count);
        Assert.False(m.Map.ContainsKey("O2"));
        Assert.False(m.Map.ContainsKey("O7"));
        Assert.Single(m.Shifts);
    }

    [Fact]
    public void NoMajority_NoShift()
    {
        var o = Enumerable.Range(0, 4).Select(i => Rect("O" + i, i * 900, 0, 300 + i * 10)).ToList();
        var n = new List<ShapeRecord> { Shift(o[0], "N0", 100, 0), Shift(o[1], "N1", 100, 0), Shift(o[2], "N2", 0, 100), Shift(o[3], "N3", 0, 100) };
        var m = ShapeMatcher.Match(o, n);
        Assert.Empty(m.Shifts);
        Assert.Empty(m.Map);
    }

    [Fact]
    public void BlockOnlyInOld_Dropped()
    {
        var o = new[] { Rect("1", 0, 0, block: "7D-16P_Old") };
        var n = new[] { Rect("2", 0, 0) };
        Assert.Empty(ShapeMatcher.Match(o, n).Map);
    }

    [Fact]
    public void AnonymousBlock_Skipped()
    {
        var o = new[] { Rect("1", 0, 0, block: "") };
        var n = new[] { Rect("2", 0, 0, block: "") };
        Assert.Empty(ShapeMatcher.Match(o, n).Map);
    }

    [Fact]
    public void BlockReference_MovedWithBasePoint_Mapped()
    {
        ShapeRecord Ref(string h, double x) => new ShapeRecord
        {
            Handle = h, Block = "7D-16P_Assy", Type = "BlockReference", Layer = "0", Name = "7D-16P_Structure",
            Points = new List<P2> { new P2(x, 0) }, Scalars = new List<double> { 0, 1, 1 },
        };
        var o = new[] { Ref("1", 0), Rect("2", 50, 50, block: "7D-16P_Assy") };
        var n = new[] { Ref("3", 30), Shift(o[1], "4", 30, 0) };
        var m = ShapeMatcher.Match(o, n);
        Assert.Equal("3", m.Map["1"]);
        Assert.Equal("4", m.Map["2"]);
    }

    [Theory]   // review: độ lệch rơi đúng giữa 2 ô lưới 0.5 + nhiễu số thực ⇒ phiếu chia đôi
    [InlineData(100.25)]
    [InlineData(1234.75)]
    public void MidpointShiftWithNoise_ShiftFound(double dx)
    {
        var o = Enumerable.Range(0, 20).Select(i => Rect("O" + i, i * 1000, 0, 80, 6)).ToList();
        var n = o.Select((r, i) => Shift(r, "N" + i, dx + (i % 2 == 0 ? 1e-9 : -1e-9), 0)).ToList();
        var m = ShapeMatcher.Match(o, n);
        Assert.Equal(20, m.Map.Count);
        Assert.Equal("N5", m.Map["O5"]);
        Assert.Equal(dx, Assert.Single(m.Shifts).Offset.X, 3);
    }

    [Fact]   // review: F1 kéo dài tại chỗ (handle còn), F3 mới chép đè lên hình cũ của F1 ⇒ F1 KHÔNG được chuyển sang F3
    public void HandleStillPresent_NotRekeyedToAnotherPart()
    {
        var f1 = Rect("F1", 0, 0, 80, 6);
        var f1Stretched = Rect("F1", 0, 0, 120, 6);
        var f3 = Rect("F3", 0, 0, 80, 6);
        var m = ShapeMatcher.Match(new[] { f1 }, new[] { f1Stretched, f3 });
        Assert.False(m.Map.ContainsKey("F1"));
    }

    // Kỹ sư 2026-11-20: DC lại block không dời gì nhưng nhiễu số thực (−0.1) bị báo "base point moved" ⇒ độ lệch dưới dung sai = 0.
    [Fact]
    public void SubToleranceShift_NotReported()
    {
        var o = new[] { Rect("1", 60, 12734), Rect("2", 324, 12744, 455, 10), Rect("3", 900, 100, 300, 12) };
        var n = new[] { Shift(o[0], "1", -0.04, -0.1), Shift(o[1], "2", -0.04, -0.1), Shift(o[2], "3", -0.04, -0.1) };
        var m = ShapeMatcher.Match(o, n);
        Assert.Empty(m.Shifts);
        Assert.Equal("1", m.Map["1"]);
    }

    [Fact]
    public void RealShift_StillReported()
    {
        var o = new[] { Rect("1", 60, 12734), Rect("2", 324, 12744, 455, 10), Rect("3", 900, 100, 300, 12) };
        var n = new[] { Shift(o[0], "A", 50, 0), Shift(o[1], "B", 50, 0), Shift(o[2], "C", 50, 0) };
        var m = ShapeMatcher.Match(o, n);
        Assert.Single(m.Shifts);
        Assert.Equal("A", m.Map["1"]);
    }
}
