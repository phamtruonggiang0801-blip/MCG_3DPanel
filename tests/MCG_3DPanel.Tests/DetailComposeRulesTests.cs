using System;
using System.Collections.Generic;
using System.Linq;
using MCG_3DPanel.Services.Detail;
using Xunit;

public class DetailComposeRulesTests
{
    static (P2, P2) S(double x0, double y0, double x1, double y1) => (new P2(x0, y0), new P2(x1, y1));
    static P2[] Rect(double x0, double y0, double x1, double y1) =>
        new[] { new P2(x0, y0), new P2(x1, y0), new P2(x1, y1), new P2(x0, y1) };

    static (double lo, double hi) XRange((P2 A, P2 B) s) => (Math.Min(s.A.X, s.B.X), Math.Max(s.A.X, s.B.X));

    // ─── Gộp đoạn thẳng trùng ───

    [Fact]
    public void Merge_OverlappingCollinear_BecomesOne()
    {
        var r = DetailComposeRules.MergeCollinear(new[] { S(0, 0, 100, 0), S(50, 0.03, 150, 0.03) }, null);
        Assert.Single(r);
        var (lo, hi) = XRange(r[0]);
        Assert.Equal(0, lo, 2); Assert.Equal(150, hi, 2);
    }

    [Fact]
    public void Merge_Duplicate_Reversed_BecomesOne()
    {
        var r = DetailComposeRules.MergeCollinear(new[] { S(0, 0, 100, 0), S(100, 0, 0, 0) }, null);
        Assert.Single(r);
    }

    [Fact]
    public void Merge_ParallelButApart_KeepsBoth()
    {
        var r = DetailComposeRules.MergeCollinear(new[] { S(0, 0, 100, 0), S(0, 0.2, 100, 0.2) }, null);
        Assert.Equal(2, r.Count);
    }

    [Fact]
    public void Merge_CollinearWithGap_KeepsBoth()
    {
        var r = DetailComposeRules.MergeCollinear(new[] { S(0, 0, 100, 0), S(101, 0, 200, 0) }, null);
        Assert.Equal(2, r.Count);
    }

    [Fact]
    public void Merge_CrossingLines_KeepsBoth()
    {
        var r = DetailComposeRules.MergeCollinear(new[] { S(0, 0, 100, 0), S(50, -50, 50, 50) }, null);
        Assert.Equal(2, r.Count);
    }

    [Fact]
    public void Merge_DiagonalOverlap_BecomesOne()
    {
        var r = DetailComposeRules.MergeCollinear(new[] { S(0, 0, 100, 100), S(50, 50, 120, 120) }, null);
        Assert.Single(r);
        Assert.Equal(120 * Math.Sqrt(2), (r[0].B - r[0].A).Length, 2);
    }

    [Fact]
    public void Merge_CoveredPart_IsRemoved()
    {
        // phần 0..60 trùng cạnh vết cắt ⇒ chỉ còn 60..100
        var r = DetailComposeRules.MergeCollinear(new[] { S(0, 0, 100, 0) }, new[] { S(-10, 0.02, 60, 0.02) });
        Assert.Single(r);
        var (lo, hi) = XRange(r[0]);
        Assert.Equal(60, lo, 2); Assert.Equal(100, hi, 2);
    }

    [Fact]
    public void Merge_FullyCovered_IsDropped()
    {
        var r = DetailComposeRules.MergeCollinear(new[] { S(10, 0, 90, 0) }, new[] { S(0, 0, 100, 0) });
        Assert.Empty(r);
    }

    [Fact]
    public void Merge_CoverInMiddle_SplitsInTwo()
    {
        var r = DetailComposeRules.MergeCollinear(new[] { S(0, 0, 100, 0) }, new[] { S(40, 0, 60, 0) });
        Assert.Equal(2, r.Count);
    }

    [Fact]
    public void Merge_DegenerateSegment_Dropped()
    {
        var r = DetailComposeRules.MergeCollinear(new[] { S(5, 5, 5.01, 5) }, null);
        Assert.Empty(r);
    }

    // ─── Cắt đường thẳng theo đa giác ───

    [Fact]
    public void ClipLine_Rectangle_OneSegment()
    {
        var r = DetailComposeRules.ClipLine(new P2(-50, 3), new P2(1, 0), Rect(0, 0, 300, 10));
        Assert.Single(r);
        var (lo, hi) = XRange(r[0]);
        Assert.Equal(0, lo, 6); Assert.Equal(300, hi, 6);
        Assert.Equal(3, r[0].A.Y, 6);
    }

    [Fact]
    public void ClipLine_ConcavePolygon_TwoSegments()
    {
        // chữ U: rãnh giữa x 100..200 từ y 5 lên
        var u = new[] { new P2(0, 0), new P2(300, 0), new P2(300, 10), new P2(200, 10), new P2(200, 5),
                        new P2(100, 5), new P2(100, 10), new P2(0, 10) };
        var r = DetailComposeRules.ClipLine(new P2(0, 7), new P2(1, 0), u);
        Assert.Equal(2, r.Count);
    }

    [Fact]
    public void ClipLine_Outside_Empty() =>
        Assert.Empty(DetailComposeRules.ClipLine(new P2(0, 20), new P2(1, 0), Rect(0, 0, 300, 10)));

    // ─── Vạch AM_8 ───

    [Fact]
    public void MarkLines_Plate10_TwoLinesAtThirds()
    {
        var r = DetailComposeRules.MarkLines(Rect(0, 0, 300, 10));
        Assert.Equal(2, r.Count);
        var ys = r.Select(s => s.A.Y).OrderBy(y => y).ToArray();
        Assert.Equal(10.0 / 3, ys[0], 4); Assert.Equal(20.0 / 3, ys[1], 4);
        Assert.All(r, s => { var (lo, hi) = XRange(s); Assert.Equal(0, lo, 4); Assert.Equal(300, hi, 4); });
    }

    [Fact]
    public void MarkLines_VerticalPlate6_OneLineAtMiddle()
    {
        var r = DetailComposeRules.MarkLines(Rect(100, 0, 106, 250));
        Assert.Single(r);
        Assert.Equal(103, r[0].A.X, 4); Assert.Equal(103, r[0].B.X, 4);
    }

    [Fact]
    public void MarkLines_RotatedPlate_LinesParallelToAxis()
    {
        double a = 30 * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        P2 R(double x, double y) => new P2(x * c - y * s, x * s + y * c);
        var r = DetailComposeRules.MarkLines(new[] { R(0, 0), R(300, 0), R(300, 20), R(0, 20) });
        Assert.Equal(3, r.Count);
        foreach (var seg in r)
        {
            var d = seg.B - seg.A;
            Assert.Equal(300, d.Length, 3);
            Assert.Equal(0, Math.Abs(d.X * s - d.Y * c), 6);
        }
    }

    [Fact]
    public void MarkLines_NotPlateLike_None()
    {
        // vuông 50×50 (dài < 2× rộng) và chữ L thưa
        Assert.Empty(DetailComposeRules.MarkLines(Rect(0, 0, 50, 50)));
        var l = new[] { new P2(0, 0), new P2(200, 0), new P2(200, 10), new P2(10, 10), new P2(10, 100), new P2(0, 100) };
        Assert.Empty(DetailComposeRules.MarkLines(l));
    }

    [Fact]
    public void Area_Rectangle() => Assert.Equal(3000, DetailComposeRules.Area(Rect(0, 0, 300, 10)), 6);

    // ─── CutMember từ vết cắt ───

    [Fact]
    public void ToCutMember_VerticalWeb()
    {
        var m = DetailComposeRules.ToCutMember("Web", Rect(100, -300, 110, 0), -50, 80);
        Assert.Equal("Web", m.Type);
        Assert.Equal(10, m.Thickness, 4);
        Assert.Equal(105, m.CutCenter.X, 4); Assert.Equal(-150, m.CutCenter.Y, 4);
        Assert.Equal(1, Math.Abs(m.CutAxis.Y), 6);
        Assert.Equal(-50, m.MinSigned); Assert.Equal(80, m.MaxSigned);
    }

    // ─── Mũi tên mặt cắt ───

    [Fact]
    public void SectionArrows_HorizontalCut_LookDown_MatchesSample()
    {
        // mẫu 16P 1B-1B: đường cắt ngang, nhìn −Y; chữ cách đầu mút 36.2 ra ngoài, 86.9 theo hướng nhìn
        var a = DetailComposeRules.SectionArrows(new P2(0, 0), new P2(1000, 0), new P2(0, -1));
        Assert.Equal(2, a.Length);
        var left = a[0]; var right = a[1];
        Assert.Equal(-24.947, left.BarA.X, 2); Assert.Equal(24.947, left.BarB.X, 2);
        Assert.Equal(0, left.BarA.Y, 6);
        Assert.Equal(0, left.Base.X, 6); Assert.Equal(-110.766, left.Tip.Y, 2); Assert.Equal(-53.368, left.Neck.Y, 2);
        Assert.Equal(-36.19, left.Text.X, 1); Assert.Equal(-86.9, left.Text.Y, 1);
        Assert.Equal(1036.19, right.Text.X, 1); Assert.Equal(-86.9, right.Text.Y, 1);
        Assert.Equal(1000, right.Base.X, 6);
    }

    [Fact]
    public void ThicknessTextAt_AlongSymbol()
    {
        var p = DetailComposeRules.ThicknessTextAt(new P2(10, 20), Math.PI);
        Assert.True(p.X < 10 - 50);
        Assert.Equal(20, p.Y, 6);
    }

    // ─── Ký hiệu Continuous: tấm nào nhận ───

    [Fact]
    public void ContinuousMembers_LargestCutPerPlate_SkipsHpAndOneSided()
    {
        var cuts = new List<(string, string, IList<P2>, double, double)>
        {
            ("A1", "Web", Rect(0, 0, 10, 100), -50, 50),     // lớn hơn ⇒ đại diện tấm A1
            ("A1", "Web", Rect(200, 0, 210, 20), -50, 50),
            ("B2", "HP", Rect(300, 0, 310, 100), -50, 50),   // HP ⇒ không
            ("C3", "FB", Rect(400, 0, 410, 100), 0, 50),     // chỉ 1 phía ⇒ không
            ("", "Bracket", Rect(500, 0, 510, 100), -5, 5),  // không handle ⇒ mỗi vết riêng
            (null, "Bracket", Rect(600, 0, 610, 100), -5, 5),
            ("D4", "Web", new[] { new P2(0, 0), new P2(1, 0) }, -5, 5),   // < 3 điểm ⇒ bỏ
        };
        var m = DetailComposeRules.ContinuousMembers(cuts);
        Assert.Equal(3, m.Count);
        Assert.Equal(5, m[0].CutCenter.X, 3);
        Assert.Equal(50, m[0].CutCenter.Y, 3);
        Assert.All(m, x => Assert.NotEqual("HP", x.Type));
        Assert.Empty(DetailComposeRules.ContinuousMembers(null));
    }

    // ─── Phụ kiện có thuộc tính (final review #8) ───

    [Fact]
    public void AttributesNote_NamesBlocksOnce_Sorted()
        => Assert.Equal("attribute values of block(s) M.D2, Socket are not shown in the detail (attributes are not copied)",
            DetailComposeRules.AttributesNote(new[] { "Socket", "M.D2", "Socket" }));

    [Fact]
    public void AttributesNote_None_Null()
    {
        Assert.Null(DetailComposeRules.AttributesNote(new string[0]));
        Assert.Null(DetailComposeRules.AttributesNote(null));
    }
}
