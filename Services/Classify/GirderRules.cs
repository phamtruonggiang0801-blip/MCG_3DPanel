namespace MCG_3DPanel.Services.Classify
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (test link trực tiếp).

    /// <summary>Định nghĩa OB/IB, GB, GE (kỹ sư 2026-11-20):
    /// <list type="bullet">
    /// <item>OB/IB: bracket 1 đầu end tại HP/FB, đầu kia end tại Web.</item>
    /// <item>GB (Girder Bracket): như OB nhưng dài HƠN max span HP (Max Stiff).</item>
    /// <item>GE (Girder End): hình thức end của Web tại HP/FB — đoạn đầu web dài chạm HP/FB luôn là GE, không còn tách GB.</item>
    /// </list></summary>
    public static class GirderRules
    {
        /// <summary>Bracket (1 đầu HP/FB, 1 đầu Web) dài hơn max span HP ⇒ GB; không thì giữ OB/IB/BF.</summary>
        public static bool IsGirderBracket(double memberLength, double maxStiff) =>
            maxStiff > 0 && memberLength > maxStiff;

        /// <summary>Mã cho bracket đã thành GB / đầu web đã thành GE: "GB-HP", "GE-FB"…</summary>
        public static string Code(bool girderBracket, bool touchesHp) =>
            (girderBracket ? "GB" : "GE") + (touchesHp ? "-HP" : "-FB");
    }
}
