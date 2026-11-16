using MCG_3DPanel.Services.Detail;
using Xunit;

namespace MCG_3DPanel.Tests
{
    public class Detail3DMatchTests
    {
        [Fact] public void Fingerprint_OrderIndependent() => Assert.Equal(Detail3DMatch.Fingerprint(new[] { "a", "b" }), Detail3DMatch.Fingerprint(new[] { "b", "a" }));
        [Fact] public void Fingerprint_ChangesWithContent() => Assert.NotEqual(Detail3DMatch.Fingerprint(new[] { "a" }), Detail3DMatch.Fingerprint(new[] { "a", "b" }));
    }
}
