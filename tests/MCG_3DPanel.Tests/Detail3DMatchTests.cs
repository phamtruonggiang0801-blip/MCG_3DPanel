using MCG_3DPanel.Services.Detail;
using Xunit;

namespace MCG_3DPanel.Tests
{
    public class Detail3DMatchTests
    {
        [Fact] public void Fingerprint_OrderIndependent() => Assert.Equal(Detail3DMatch.Fingerprint(new[] { "a", "b" }), Detail3DMatch.Fingerprint(new[] { "b", "a" }));
        [Fact] public void Fingerprint_ChangesWithContent() => Assert.NotEqual(Detail3DMatch.Fingerprint(new[] { "a" }), Detail3DMatch.Fingerprint(new[] { "a", "b" }));

        static Candidate3D C(string src, string h, string fp = "x", bool unsaved = false) =>
            new Candidate3D { Path = "a_3D.dwg", SourcePath = src, SourceRefHandle = h, Fingerprint = fp, OpenUnsaved = unsaved };

        [Fact] public void Matching_FiltersByPathAndHandle()
        {
            var a = C(@"C:\x\p.dwg", "1A"); var b = C(@"C:\x\p.dwg", "2B"); var c = C(@"C:\x\q.dwg", "1A"); var d = C(null, "1A");
            var r = Detail3DMatch.Matching(new[] { a, b, c, d }, @"C:\x\p.dwg", "1A");
            Assert.Single(r); Assert.Same(a, r[0]);
        }
        [Fact] public void Matching_PathCaseInsensitive()
        {
            var a = C(@"c:\X\P.DWG", "1a");
            Assert.Single(Detail3DMatch.Matching(new[] { a }, @"C:\x\p.dwg", "1A"));
        }
        [Fact] public void Stale_Unsaved3D_Warns() => Assert.Equal(StaleState.Unsaved3D, Detail3DMatch.Stale(C("s", "h", "f", true), "f"));
        [Fact] public void Stale_EmptyFingerprint_Unknown() => Assert.Equal(StaleState.Unknown, Detail3DMatch.Stale(C("s", "h", ""), "f"));
        [Fact] public void Stale_Differs_Changed2D() => Assert.Equal(StaleState.Changed2D, Detail3DMatch.Stale(C("s", "h", "f1"), "f2"));
        [Fact] public void Stale_Same_Fresh() => Assert.Equal(StaleState.Fresh, Detail3DMatch.Stale(C("s", "h", "f"), "f"));
        [Fact] public void CandidatePattern_Sanitizes() => Assert.Equal("7D_16P_3D*.dwg", Detail3DMatch.CandidatePattern("7D/16P"));
    }
}
