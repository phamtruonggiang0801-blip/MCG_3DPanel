namespace MCG_3DPanel.Services.PlanView
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>Khoảng cách đặt nội dung tờ Plan View, đơn vị **mm GIẤY** (model = giấy × tỉ lệ tờ).
    /// Đo từ 6 tờ mẫu — xem docs/superpowers/specs/2026-11-10-plan-view-placement.md.</summary>
    public static class PlanViewLayout
    {
        // Nhãn tên
        public const double LabelTopStiffener = 24;   // HT/BS (phương T): baseline trên mép trên Top Plate, BottomCenter
        public const double LabelTopGirder = 44;      // T#: hàng ngoài hơn
        public const double LabelGirderBeyondStiffener = 20;
        public const double LabelLeft = 52;           // L#/HL#/BS (phương L): trái mép trái Top Plate, MiddleLeft
        public const double LabelBeyondChain = 8;     // hàng nhãn cách chuỗi kích thước ngoài cùng

        // Chuỗi kích thước
        public const double ChainFirstOffset = 22;
        public const double ChainSpacing = 8;

        // Hình chiếu
        public const double SectionGapY = 56;          // X-X trên mép trên Top Plate
        public const double SectionGapX = 77;          // Y-Y trái mép trái Top Plate
        public const double SectionBeyondLabels = 12;
        public const double LashingPlanGap = 97;       // mặt bằng lỗ lashing: mép phải plan → mép trái tấm
        public const double MdOffsetX = 65, MdOffsetY = -50, MdSpacing = 99;

        // Tiêu đề, member list, ghi chú dung sai
        public const double TitleBelow = 41;
        public const double TitleWidth = 182;
        public const double MemberListX = 176, MemberListY = 84, MemberListWidth = 176;
        public const double ToleranceNoteX = 32.031, ToleranceNoteY = 109.714;

        // Ký hiệu B / S / GE / GB / OB
        public const double MarkOffset = 4;

        // Cỡ chữ (mm giấy)
        public const double TextGirder = 5, TextStiffener = 3, TextMemberList = 3.5, TextTitle = 5;
    }
}
