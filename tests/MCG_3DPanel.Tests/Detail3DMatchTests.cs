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
        /// <summary>Build 3D đặt tên file theo TÊN BLOCK _Assy (Panel3DPath.Target) ⇒ mẫu tìm phải khớp đúng file đó.</summary>
        [Fact] public void CandidatePattern_MatchesBuildTarget()
        {
            string built = System.IO.Path.GetFileName(MCG_3DPanel.Commands.Panel3DPath.Target(@"C:\p", "7D-16P_Assy"));
            string pattern = Detail3DMatch.CandidatePattern("7D-16P_Assy");
            Assert.Equal("7D-16P_Assy_3D.dwg", built);
            Assert.True(System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, built));
        }

        [Fact] public void CandidatePattern_Sanitizes() => Assert.Equal("7D_16P_3D*.dwg", Detail3DMatch.CandidatePattern("7D/16P"));

        // ─── Tên block trong record vân tay (final review #6): tên hiệu dụng, bỏ tên ẩn danh '*' ───

        [Fact] public void FingerprintName_Plain() => Assert.Equal("P_Assy", Detail3DMatch.FingerprintName("P_Assy", null));
        [Fact] public void FingerprintName_DynamicUsesEffective() => Assert.Equal("Flat bar", Detail3DMatch.FingerprintName("*U12", "Flat bar"));
        [Fact] public void FingerprintName_AnonymousLeftOut() => Assert.Equal("", Detail3DMatch.FingerprintName("*U12", null));
        [Fact] public void FingerprintName_AnonymousEffectiveLeftOut() => Assert.Equal("", Detail3DMatch.FingerprintName("*U12", "*U3"));
        [Fact] public void FingerprintName_StableAcrossAnonymousRename()
            => Assert.Equal(Detail3DMatch.FingerprintName("*U12", "FB"), Detail3DMatch.FingerprintName("*U40", "FB"));

        // ─── Lý do không có file 3D dùng được (final review #4) ───

        static Candidate3D F(string file, string src, string h) =>
            new Candidate3D { Path = @"C:\x\" + file, SourcePath = src, SourceRefHandle = h };

        [Fact] public void NoMatch_NothingFound_BuildFirst()
            => Assert.Equal("No 3D file of P next to this drawing. Build 3D first.",
                Detail3DMatch.NoMatchReason("P", new Candidate3D[0], new (string, string)[0], @"C:\x\p.dwg", "1A"));

        [Fact] public void NoMatch_BuiltFromOtherDrawing_NamesFileAndPath()
        {
            string s = Detail3DMatch.NoMatchReason("P", new[] { F("P_3D.dwg", @"C:\old\p.dwg", "1A") }, new (string, string)[0], @"C:\x\p.dwg", "1A");
            Assert.Contains(@"Found P_3D.dwg but it was built from C:\old\p.dwg", s);
            Assert.DoesNotContain("No 3D file", s);
        }

        [Fact] public void NoMatch_NoSourceRecorded()
            => Assert.Contains("Found P_3D.dwg but it does not record which 2D drawing it was built from",
                Detail3DMatch.NoMatchReason("P", new[] { F("P_3D.dwg", null, "1A") }, new (string, string)[0], @"C:\x\p.dwg", "1A"));

        [Fact] public void NoMatch_SameDrawingOtherReference()
            => Assert.Contains("Found P_3D.dwg but it was built from another P_Assy block reference in this drawing",
                Detail3DMatch.NoMatchReason("P", new[] { F("P_3D.dwg", @"C:\x\p.dwg", "2B") }, new (string, string)[0], @"C:\x\p.dwg", "1A"));

        [Fact] public void NoMatch_ReadFailure_NamesFileAndError()
        {
            string s = Detail3DMatch.NoMatchReason("P", new Candidate3D[0], new[] { ("P_3D.dwg", "eFileSharingViolation") }, @"C:\x\p.dwg", "1A");
            Assert.Contains("Could not read P_3D.dwg (eFileSharingViolation)", s);
            Assert.DoesNotContain("No 3D file", s);
        }

        [Fact] public void NoMatch_MixedReasons_AllListed()
        {
            string s = Detail3DMatch.NoMatchReason("P", new[] { F("P_3D.dwg", @"C:\old\p.dwg", "1A") },
                new[] { ("P_3D_2.dwg", "bad") }, @"C:\x\p.dwg", "1A");
            Assert.Contains("Found P_3D.dwg", s); Assert.Contains("Could not read P_3D_2.dwg (bad)", s);
            Assert.EndsWith("Build 3D again from this drawing.", s);
        }
    }
}
