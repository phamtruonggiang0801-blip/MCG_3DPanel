namespace MCG_3DPanel.Services.PlanView
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>Khoảng cách đặt nội dung tờ Plan View, đơn vị **mm GIẤY** (model = giấy × tỉ lệ tờ).
    /// Hiệu chỉnh theo tờ mẫu 7D-17P (CAS-0062242) — xem docs/superpowers/specs/2026-11-10-plan-view-vs-manual-7D-17P.md.</summary>
    public static class PlanViewLayout
    {
        // ─── Nhãn tên (MiddleLeft; phương T: chữ căn giữa đường tâm thanh, Y = giữa chữ) ───
        public const double LabelTopGirder = 21.0;     // T#: trên mép trên Top Plate
        public const double LabelTopHp = 25.2;         // HT#
        public const double LabelTopFb = 31.8;         // BS phương T
        public const double LabelLeftGirder = 35.8;    // L#: điểm đầu chữ, trái mép trái Top Plate
        public const double LabelLeftStiffener = 42.6; // HL# / BS phương L

        // ─── Mặt bằng chính: chỉ 2 kích thước bao ───
        public const double OverallBelow = 24.7;       // dài: dưới mép dưới
        public const double OverallLeft = 13.2;        // rộng: trái mép trái
        public const double OverallEdgeBand = 0.05;    // đỉnh đo lấy trong 5% bề rộng/cao sát góc

        // ─── Mặt cắt (gốc = đầu tấm trên đường cắt giữa panel) ───
        public const double SectionXAbove = 36.25;     // X-X: mép dưới tấm trên mặt cắt cách mép trên Top Plate
        public const double SectionYLeft = 45.4;       // Y-Y: mặt trên tấm trên mặt cắt cách mép trái Top Plate
        public const double SecXEnds = 8.9, SecXPitch = 15.8, SecXGirder = 25.3, SecXHeightLeft = 13.7;
        public const double SecYEnds = 12.0, SecYPitch = 12.0, SecYGirder = 21.9, SecYHeightAbove = 12.0;

        // ─── Mặt bằng lỗ lashing ───
        public const double LashLeftFromFrame = 513;   // mép trái tấm, tính từ gốc khung
        public const double LashRightMargin = 45.6;    // tấm rộng ⇒ dời trái để chừa lề phải
        public const double LashChainLeft = 8.3;       // chuỗi kích thước dọc: trái mép trái tấm
        public const double LashChainTop = 9.6;        // chuỗi kích thước ngang: trên mép trên tấm
        public const double LashCogRight = 10.4, LashCogTop = 20.4;   // kích thước COG từ góc (mép phải, y 0): dọc bên phải, ngang chuỗi ngoài
        public const double DiaPickX = 0.026, DiaPickY = 0.167;      // Ø lỗ: lỗ gần (tâm + 2.6% bề rộng, đáy + 16.7% chiều cao) — tờ 7D-17P
        public const double DiaAngleDeg = 28;                         // hướng chữ Ø (tờ tay)
        public const double MdBelowTop = 50.3;         // M.D1/M.D2 dưới mép trên tấm
        public const double MdFirstFromCentre = 0.2141; // × bề rộng tấm (M.D1 trái tâm)
        public const double MdSpacing = 0.4067;        // × bề rộng tấm (M.D1 → M.D2)
        public const double MdLeaderDx1 = -9.6, MdLeaderDx2 = 4.8; // điểm mũi tên trên đường chéo, theo X từ góc block M.D

        // ─── Tiêu đề, member list, ghi chú dung sai, khung AM_8 ───
        public const double TitleBelow = 50.7;
        public const double TitleWidthPlan = 182.2, TitleWidthLashing = 136.1;
        public const double MemberListX = 175.8, MemberListY = 80.2, MemberListWidth = 175.9;
        public const double ToleranceNoteX = 32.031, ToleranceNoteY = 109.714;

        // ─── Ký hiệu chữ B / S / GE / GB / OB ───
        public const double MarkOffset = 4;
        public const double BracketTextOut = 8.5;      // OB/OB1: chữ đẩy ra ngoài bracket (phía mép tấm)
        public const double HpBAlong = 4.25, HpBSide = 3.1; // chữ B: dọc HP về phía bracket / sang trái HP
        public const double GirderCrossMin = 1000;     // mm thật: girder × girder chỉ ký hiệu liên tục khi cách đầu mút cả 2 tuyến ≥ giá trị này
        public const double FrameTop = 3.3, FrameBottom = 7.9, FrameInset = 0.26, FrameRuleGap = 0.25; // khung AM_8 quanh member list
        public const double LineSpacingFactor = 5.0 / 3.0; // khoảng dòng MText mặc định = 5/3 × cao chữ

        // ─── Cỡ chữ (mm giấy) ───
        public const double TextGirder = 5, TextStiffener = 3, TextMemberList = 3.5, TextTitle = 5;
    }
}
