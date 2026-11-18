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
    public void LabelSpots_CentreFirst_StayInsideFace()
    {
        // mặt vách 10A-10A: x -611.5…215.9, y -180…194; chữ "(10.0)" rộng 144, cao 30
        var min = new P2(-611.5, -180); var max = new P2(215.9, 194);
        var spots = DetailComposeRules.LabelSpots(min, max, 144, 30);
        Assert.Equal(-197.8, spots[0].X, 6);
        Assert.Equal(7, spots[0].Y, 6);
        Assert.All(spots, c =>
        {
            Assert.True(c.X - 76 >= min.X - 1e-9 && c.X + 76 <= max.X + 1e-9);
            Assert.True(c.Y - 19 >= min.Y - 1e-9 && c.Y + 19 <= max.Y + 1e-9);
        });
        // giữ ngang giữa chiều cao: dời ngang 1 bước (36) xếp trước dời đứng 1 bước (15)
        Assert.Equal(7, spots[1].Y, 6);
    }

    [Fact]
    public void LabelSpots_FaceTooSmall_OnlyCentre()
    {
        var spots = DetailComposeRules.LabelSpots(new P2(0, 0), new P2(100, 20), 144, 30);
        Assert.Single(spots);
        Assert.Equal(50, spots[0].X, 6);
    }

    [Fact]
    public void FreeSpot_SkipsLinesAndTaken_FallsBackToFirst()
    {
        var spots = new List<P2> { new P2(0, 0), new P2(200, 0), new P2(400, 0) };
        var segs = new List<(P2, P2)> { (new P2(0, -100), new P2(0, 100)) };          // nét đứng qua chỗ 1
        var taken = new List<(P2, P2)> { (new P2(150, -10), new P2(250, 10)) };      // "$" ở chỗ 2
        Assert.Equal(400, DetailComposeRules.FreeSpot(spots, 144, 30, segs, taken)!.Value.X, 6);
        var all = new List<(P2, P2)> { (new P2(-1000, 0), new P2(1000, 0)) };
        Assert.Equal(0, DetailComposeRules.FreeSpot(spots, 144, 30, all, null)!.Value.X, 6);
        Assert.Null(DetailComposeRules.FreeSpot(new List<P2>(), 144, 30, null, null));
    }

    [Fact]
    public void ExtendOutside_PushesBothEndsBeyondContentPlusGap()
    {
        var (a, b) = DetailComposeRules.ExtendOutside(new P2(0, 50), new P2(1000, 50), new P2(100, 0), new P2(900, 100), 250);
        Assert.Equal(-150, a.X, 6); Assert.Equal(1150, b.X, 6);
        Assert.Equal(50, a.Y, 6); Assert.Equal(50, b.Y, 6);
    }

    [Fact]
    public void ExtendOutside_NeverShortens_MissKeeps()
    {
        var (a, b) = DetailComposeRules.ExtendOutside(new P2(-1000, 50), new P2(2000, 50), new P2(100, 0), new P2(900, 100), 250);
        Assert.Equal(-1000, a.X, 6); Assert.Equal(2000, b.X, 6);
        var (c, d) = DetailComposeRules.ExtendOutside(new P2(0, 900), new P2(1000, 900), new P2(100, 0), new P2(900, 100), 250);
        Assert.Equal(0, c.X, 6); Assert.Equal(1000, d.X, 6);
    }

    [Fact]
    public void ContinuousMembers_SkipsHorizontalPlates()
    {
        var cuts = new List<(string, string, IList<P2>, double, double)>
        {
            ("W", "Web", Rect(0, 0, 10, 374), -50, 50),
            ("F", "FacePlate", Rect(0, -20, 500, 0), -50, 50),
            ("T", "TopPlate", Rect(-700, 374, 700, 380), -50, 50),
        };
        var m = Assert.Single(DetailComposeRules.ContinuousMembers(cuts));
        Assert.Equal("Web", m.Type);
        Assert.Equal(374, m.CutLength, 6);
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

    // ─── Viền Top Plate: cạnh trên biên vùng ⇒ break, còn lại AM_7 (kỹ sư 2026-11-18) ───

    // Vùng = hình chữ nhật x ∈ [0,100], y ∈ [0,100]
    static double DistRect(P2 p) => Math.Min(Math.Min(Math.Abs(p.X), Math.Abs(p.X - 100)), Math.Min(Math.Abs(p.Y), Math.Abs(p.Y - 100)));

    [Fact]
    public void TopPlateRuns_EdgesOnBoundary_BecomeBreaks()
    {
        // Tấm phủ nửa trái vùng: mép thật x = 50, 3 cạnh còn lại nằm trên biên vùng
        var loop = new List<P2> { new P2(0, 0), new P2(50, 0), new P2(50, 100), new P2(0, 100) };
        var (breaks, edges) = DetailComposeRules.TopPlateRuns(loop, DistRect, 0.5);
        Assert.Equal(3, breaks.Count);
        var e = Assert.Single(edges);
        Assert.False(e.Closed);
        Assert.Equal(2, e.Pts.Count);
        Assert.All(e.Pts, p => Assert.Equal(50, p.X, 6));
    }

    [Fact]
    public void TopPlateRuns_HoleInside_ClosedEdgeNoBreak()
    {
        var hole = new List<P2> { new P2(40, 40), new P2(60, 40), new P2(60, 60), new P2(40, 60) };
        var (breaks, edges) = DetailComposeRules.TopPlateRuns(hole, DistRect, 0.5);
        Assert.Empty(breaks);
        var e = Assert.Single(edges);
        Assert.True(e.Closed);
        Assert.Equal(4, e.Pts.Count);
    }

    [Fact]
    public void TopPlateRuns_WholeRegion_AllBreak()
    {
        var loop = new List<P2> { new P2(0, 0), new P2(100, 0), new P2(100, 100), new P2(0, 100) };
        var (breaks, edges) = DetailComposeRules.TopPlateRuns(loop, DistRect, 0.5);
        Assert.Equal(4, breaks.Count);
        Assert.Empty(edges);
    }
}
