using System;
using MCG_3DPanel.Services.Detail;
using Xunit;

public class SectionMarkRulesTests
{
    [Theory]
    [InlineData(6, new[] { 3.0 })]
    [InlineData(10, new[] { 3.3333333, 6.6666667 })]
    [InlineData(12, new[] { 4.0, 8.0 })]
    [InlineData(15, new[] { 3.75, 7.5, 11.25 })]
    [InlineData(20, new[] { 5.0, 10.0, 15.0 })]
    public void Offsets_6_10_12_15_20(double t, double[] expected)
    {
        var o = SectionMarkRules.Offsets(t);
        Assert.Equal(expected.Length, o.Length);
        for (int i = 0; i < o.Length; i++) Assert.Equal(expected[i], o[i], 3);
    }

    [Fact]
    public void IsPlateLike_Rectangle_True() =>
        Assert.True(SectionMarkRules.IsPlateLike(300 * 10, 10, 300));

    [Fact]
    public void IsPlateLike_HpBulbSection_False() =>
        Assert.False(SectionMarkRules.IsPlateLike(120 * 6 + 22 * 15, 22, 120));

    [Fact]
    public void MinAreaRect_RotatedRectangle()
    {
        double a = 30 * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        P2 R(double x, double y) => new P2(x * c - y * s + 50, x * s + y * c - 20);
        var r = SectionMarkRules.MinAreaRect(new[] { R(0, 0), R(300, 0), R(300, 10), R(0, 10) });
        Assert.Equal(10, r.width, 4);
        Assert.Equal(300, r.length, 4);
        Assert.Equal(1.0, Math.Sqrt(r.axis.X * r.axis.X + r.axis.Y * r.axis.Y), 6);
        Assert.Equal(1.0, Math.Abs(r.axis.X * c + r.axis.Y * s), 6);
    }
}
