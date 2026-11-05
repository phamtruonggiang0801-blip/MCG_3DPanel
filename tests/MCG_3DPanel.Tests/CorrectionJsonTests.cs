using System.Collections.Generic;
using MCG_3DPanel.Services.Corrections;
using Xunit;

namespace MCG_3DPanel.Tests
{
    public class CorrectionJsonTests
    {
        [Fact]
        public void RoundTrip_KindFlipSnipDeleted()
        {
            var parts = new Dictionary<string, PartCorrection>
            {
                ["393B9#0"] = new PartCorrection { Kind = "IB", FlipDir = true, OriginalKind = "OB", SnipLo = false, SnipHi = null },
                ["1AAB2#1"] = new PartCorrection { SnipHi = true },
            };
            var deleted = new[] { "CollarPlate:a~b", "38DDD#0/GirderFlange#0" };

            string json = CorrectionJson.Serialize(parts, deleted);
            CorrectionJson.Deserialize(json, out var p2, out var d2);

            Assert.Equal(2, p2.Count);
            var c = p2["393B9#0"];
            Assert.Equal("IB", c.Kind);
            Assert.True(c.FlipDir);
            Assert.Equal("OB", c.OriginalKind);
            Assert.False(c.SnipLo);
            Assert.Null(c.SnipHi);
            Assert.True(p2["1AAB2#1"].SnipHi);
            Assert.Null(p2["1AAB2#1"].Kind);
            Assert.Equal(new HashSet<string>(deleted), d2);
        }

        // Tripping bracket ép theo HP (kỹ sư 2026-11-05): null = Auto, true = On, false = Off.
        [Fact]
        public void RoundTrip_Tripping()
        {
            var parts = new Dictionary<string, PartCorrection>
            {
                ["218DA#0"] = new PartCorrection { Tripping = true },
                ["218E4#0"] = new PartCorrection { Tripping = false },
            };
            CorrectionJson.Deserialize(CorrectionJson.Serialize(parts, new string[0]), out var p2, out _);
            Assert.True(p2["218DA#0"].Tripping);
            Assert.False(p2["218E4#0"].Tripping);
            Assert.False(new PartCorrection { Tripping = false }.IsEmpty);
            Assert.True(new PartCorrection().IsEmpty);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not json")]
        public void Deserialize_EmptyOrBad_GivesEmpty(string json)
        {
            CorrectionJson.Deserialize(json, out var p, out var d);
            Assert.Empty(p);
            Assert.Empty(d);
        }

        [Fact]
        public void Chunks_SplitAndJoin_Lossless()
        {
            string s = new string('x', 601) + "ệ";
            var chunks = CorrectionJson.Chunk(s, 250);
            Assert.Equal(3, chunks.Count);
            Assert.All(chunks, ch => Assert.True(ch.Length <= 250));
            Assert.Equal(s, string.Concat(chunks));
        }
    }
}
