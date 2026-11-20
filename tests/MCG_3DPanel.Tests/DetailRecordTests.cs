using System.Collections.Generic;
using MCG_3DPanel.Services.Detail;
using Xunit;

namespace MCG_3DPanel.Tests
{
    public class DetailRecordTests
    {
        static DetailRecord Section() => new DetailRecord
        {
            Kind = DetailKind.Section, Panel = "7D-16C", Number = 3, Letter = "A",
            AssyRefHandle = "1A2", BoundaryHandle = "3F",
            Region = new List<double[]> { new[] { 0.0, 0.0 }, new[] { 100.5, 200.25 } },
            CutA = new[] { 1.0, 2.0 }, CutB = new[] { 3.0, 4.0 }, Side = new[] { 0.0, 1.0 },
            Scale = 2, Title = "3A-3A", BaseX = 10.5, BaseY = -20
        };

        [Fact]
        public void RoundTrip_Section()
        {
            var json = Section().ToJson();
            Assert.Contains("\"Section\"", json);
            var r = DetailRecord.FromJson(json);
            Assert.NotNull(r);
            Assert.Equal(DetailKind.Section, r.Kind);
            Assert.Equal("7D-16C", r.Panel);
            Assert.Equal(3, r.Number);
            Assert.Equal("A", r.Letter);
            Assert.Equal("1A2", r.AssyRefHandle);
            Assert.Equal("3F", r.BoundaryHandle);
            Assert.Equal(2, r.Region.Count);
            Assert.Equal(new[] { 100.5, 200.25 }, r.Region[1]);
            Assert.Equal(new[] { 1.0, 2.0 }, r.CutA);
            Assert.Equal(new[] { 3.0, 4.0 }, r.CutB);
            Assert.Equal(new[] { 0.0, 1.0 }, r.Side);
            Assert.Equal(2, r.Scale);
            Assert.Equal("3A-3A", r.Title);
            Assert.Equal(10.5, r.BaseX);
            Assert.Equal(-20, r.BaseY);
        }

        [Fact]
        public void RoundTrip_CircleView()
        {
            var v = new DetailRecord
            {
                Kind = DetailKind.View, Panel = "P", Number = 7, Circle = true,
                Region = new List<double[]> { new[] { 5.0, 6.0, 40.0 } }, Title = "DETAIL 7"
            };
            var r = DetailRecord.FromJson(v.ToJson());
            Assert.NotNull(r);
            Assert.Equal(DetailKind.View, r.Kind);
            Assert.True(r.Circle);
            Assert.Equal(new[] { 5.0, 6.0, 40.0 }, r.Region[0]);
            Assert.Null(r.CutA);
            Assert.Equal(1, r.Scale);
        }

        [Fact]
        public void Depth_RoundTripsOldRecordIsZero()
        {
            var s = Section();
            s.Depth = 750;
            Assert.Equal(750, DetailRecord.FromJson(s.ToJson()).Depth);
            var old = s.ToJson().Replace(",\"Depth\":750", "");
            Assert.DoesNotContain("Depth", old);
            Assert.Equal(0, DetailRecord.FromJson(old).Depth);
        }

        [Fact] public void FromJson_Garbage_Null() => Assert.Null(DetailRecord.FromJson("{not json"));
        [Fact] public void FromJson_Empty_Null()
        {
            Assert.Null(DetailRecord.FromJson(""));
            Assert.Null(DetailRecord.FromJson("   "));
            Assert.Null(DetailRecord.FromJson(null));
        }

        [Fact] public void FromJson_MissingKind_Null() => Assert.Null(DetailRecord.FromJson("{\"Panel\":\"P\"}"));
        [Fact] public void FromJson_EmptyPanel_Null() => Assert.Null(DetailRecord.FromJson("{\"Kind\":\"Plan\",\"Panel\":\"\"}"));
        [Fact] public void FromJson_BadKind_Null() => Assert.Null(DetailRecord.FromJson("{\"Kind\":\"Zzz\",\"Panel\":\"P\"}"));

        [Fact]
        public void FromJson_SectionWithoutCut_Null()
        {
            Assert.Null(DetailRecord.FromJson("{\"Kind\":\"Section\",\"Panel\":\"P\"}"));
            Assert.Null(DetailRecord.FromJson("{\"Kind\":\"Section\",\"Panel\":\"P\",\"CutA\":[1,2],\"CutB\":[3],\"Side\":[0,1]}"));
            Assert.Null(DetailRecord.FromJson("{\"Kind\":\"Section\",\"Panel\":\"P\",\"CutA\":[1,2],\"CutB\":[3,4],\"Side\":null}"));
        }

        [Fact]
        public void FromJson_NullCollections_Defaults()
        {
            var r = DetailRecord.FromJson("{\"Kind\":\"Plan\",\"Panel\":\"P\",\"Region\":null,\"Letter\":null,\"Title\":null,\"AssyRefHandle\":null,\"BoundaryHandle\":null}");
            Assert.NotNull(r);
            Assert.NotNull(r.Region);
            Assert.Empty(r.Region);
            Assert.Equal("", r.Letter);
            Assert.Equal("", r.Title);
            Assert.Equal("", r.AssyRefHandle);
            Assert.Equal("", r.BoundaryHandle);
        }

        [Fact]
        public void FromJson_NullRegionEntry_Null() =>
            Assert.Null(DetailRecord.FromJson("{\"Kind\":\"Plan\",\"Panel\":\"P\",\"Region\":[null]}"));
    }
}
