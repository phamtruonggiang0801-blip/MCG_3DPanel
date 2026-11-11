namespace MCG_3DPanel.Services.PlanView
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>Bộ khoảng cách (mm GIẤY) của tờ Plan View. Hai bộ hiệu chỉnh từ tờ tay:
    /// • <see cref="Standard"/> = tờ 7D-17C (1:75, tấm nhỏ trên tờ — chuẩn công ty, rộng rãi);
    /// • <see cref="Compact"/> = tờ 7D-17P (1:50, tấm gần kín tờ — lề trái chỉ 77 mm).
    /// Builder dựng theo Chuẩn; nội dung lọt ra ngoài khung trong của A1 ⇒ dựng lại theo Gọn (lỗi 2026-11-10: 17P dùng bộ Chuẩn ⇒
    /// mặt cắt Y-Y ở x 4.4, chuỗi kích thước ở x −7.7 / −22, ngoài khung x 16).</summary>
    public sealed class LayoutProfile
    {
        public string Name { get; private set; }

        public double LabelTopGirder, LabelTopHp, LabelTopFb;                       // trên mép trên Top Plate
        public double LabelLeftGirderFromSection, LabelLeftStiffenerFromSection;    // từ mặt cắt Y-Y về phía tấm
        public double OverallGapH, OverallGapV, LeftDimShift;
        public double SectionXAbove, SectionYLeft;
        public double SecXEnds, SecXPitch, SecXGirder, SecXHeightLeft;
        public double SecYEnds, SecYPitch, SecYGirder, SecYHeightAbove;
        public double LashGapAfterPlan, LashChainSide, LashChainTop, LashCogSide, LashCogSideNear, LashCogTop;
        public double TitleBelowPlan, TitleBelowDim, TitleBelowLashing;

        public static readonly LayoutProfile Standard = new LayoutProfile
        {
            Name = "Standard (7D-17C)",
            LabelTopGirder = 46.2, LabelTopHp = 47.6, LabelTopFb = 52.3,
            LabelLeftGirderFromSection = 16.8, LabelLeftStiffenerFromSection = 5.9,
            OverallGapH = 37.4, OverallGapV = 45.8, LeftDimShift = 20,
            SectionXAbove = 56.0, SectionYLeft = 68.8,
            SecXEnds = 9.7, SecXPitch = 9.7, SecXGirder = 24.6, SecXHeightLeft = 12.1,
            SecYEnds = 12.1, SecYPitch = 12.1, SecYGirder = 26.4, SecYHeightAbove = 12.0,
            LashGapAfterPlan = 40.2, LashChainSide = 19.4, LashChainTop = 13.0, LashCogSide = 29.4, LashCogSideNear = 10.4, LashCogTop = 25.1,
            TitleBelowPlan = 36.1, TitleBelowDim = 26.0, TitleBelowLashing = 52.5,
        };

        /// <summary>Tờ 7D-17P (CAS-0062242, dump 2026-11-10 16:29): Y-Y 45.3, X-X 36.2, kích thước rộng 13.2, dài 24.7, tiêu đề 50.7,
        /// tấm lashing 469.4 (= mép phải mặt bằng 419.2 + 50.2).</summary>
        public static readonly LayoutProfile Compact = new LayoutProfile
        {
            Name = "Compact (7D-17P)",
            LabelTopGirder = 21.0, LabelTopHp = 25.2, LabelTopFb = 31.8,
            LabelLeftGirderFromSection = 9.6, LabelLeftStiffenerFromSection = 2.8,
            OverallGapH = 24.7, OverallGapV = 13.2, LeftDimShift = 0,
            SectionXAbove = 36.25, SectionYLeft = 45.4,
            SecXEnds = 8.9, SecXPitch = 15.8, SecXGirder = 25.3, SecXHeightLeft = 13.7,
            SecYEnds = 12.0, SecYPitch = 12.0, SecYGirder = 21.9, SecYHeightAbove = 12.0,
            LashGapAfterPlan = 50.2, LashChainSide = 8.3, LashChainTop = 9.6, LashCogSide = 18.3, LashCogSideNear = 10.4, LashCogTop = 20.4,
            TitleBelowPlan = 36.1, TitleBelowDim = 26.0, TitleBelowLashing = 50.7,
        };
    }
}
