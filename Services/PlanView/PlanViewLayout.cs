using System;

namespace MCG_3DPanel.Services.PlanView
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>Khoảng cách đặt nội dung tờ Plan View, đơn vị **mm GIẤY** (model = giấy × tỉ lệ tờ).
    /// Chuẩn công ty = tờ 7D-17C (CAS-0068762, 1:75 — kỹ sư 2026-11-10); bố cục XẾP LỚP tính từ khung bao Top Plate:
    /// trên: [kích thước dài] → nhãn T/HT/BS → mặt cắt X-X → chuỗi bước → chuỗi girder; trái: [kích thước rộng] → nhãn L/HL → mặt cắt Y-Y…
    /// Xem docs/superpowers/specs/2026-11-10-plan-view-vs-manual-7D-17C.md.</summary>
    public static class PlanViewLayout
    {
        /// <summary>Bộ khoảng cách đang dùng (builder đặt qua <see cref="Use"/>; mặc định = Chuẩn 7D-17C).</summary>
        [ThreadStatic] private static LayoutProfile _p;
        public static LayoutProfile P => _p ?? LayoutProfile.Standard;

        /// <summary>Dùng <paramref name="p"/> trong phạm vi using (builder dựng thử Chuẩn rồi Gọn).</summary>
        public static IDisposable Use(LayoutProfile p) { var old = _p; _p = p; return new Restore(() => _p = old); }
        private sealed class Restore : IDisposable { private readonly Action _a; public Restore(Action a) { _a = a; } public void Dispose() => _a(); }

        // ─── Nhãn tên (MiddleLeft; phương T: chữ căn giữa đường tâm thanh, Y = giữa chữ) ───
        public static double LabelTopGirder => P.LabelTopGirder;     // T#: trên mép trên Top Plate (tờ 17C: kích thước dài 37.4 + 8.8)
        public static double LabelTopHp => P.LabelTopHp;         // HT#
        public static double LabelTopFb => P.LabelTopFb;         // BS phương T
        public static double LabelLeftGirderFromSection => P.LabelLeftGirderFromSection;    // L#: điểm đầu chữ, tính từ mặt cắt Y-Y về phía tấm
        public static double LabelLeftStiffenerFromSection => P.LabelLeftStiffenerFromSection;  // HL# / BS phương L

        // ─── Mặt bằng chính: chỉ 2 kích thước bao; phía = cạnh thẳng dài (mặc định dưới / trái, cạnh gãy < 50 % ⇒ trên / phải) ───
        public static double OverallGapH => P.OverallGapH;        // dài: cách mép trên/dưới khung bao
        public static double OverallGapV => P.OverallGapV;        // rộng: cách mép phải/trái khung bao (tờ 17C: 422.7 − 376.95)
        public const double BrokenEdge = 0.5;          // cạnh thẳng dài nhất < 50 % kích thước ⇒ đặt kích thước phía đối diện
        public static double LeftDimShift => P.LeftDimShift;         // kích thước rộng ở bên TRÁI ⇒ cụm Y-Y + nhãn L/HL dời thêm ra trái

        // ─── Mặt cắt (tấm = giữa 2 cạnh thẳng chính, gốc = cạnh chính thấp) ───
        public static double SectionXAbove => P.SectionXAbove;      // X-X: mép dưới tấm trên mặt cắt cách mép trên Top Plate
        public static double SectionYLeft => P.SectionYLeft;       // Y-Y: mặt trên tấm trên mặt cắt cách mép trái Top Plate
        public static double SecXEnds => P.SecXEnds; public static double SecXPitch => P.SecXPitch; public static double SecXGirder => P.SecXGirder; public static double SecXHeightLeft => P.SecXHeightLeft;
        public static double SecYEnds => P.SecYEnds; public static double SecYPitch => P.SecYPitch; public static double SecYGirder => P.SecYGirder; public static double SecYHeightAbove => P.SecYHeightAbove;

        // ─── Mặt bằng lỗ lashing ───
        public static double LashGapAfterPlan => P.LashGapAfterPlan;   // mép trái tấm lashing cách thứ ngoài cùng bên phải của mặt bằng chính
        public const double LashRightMargin = 45.6;    // tấm rộng ⇒ dời trái để chừa lề phải
        public static double LashChainSide => P.LashChainSide;      // chuỗi dọc: cách mép trái/phải khung bao tấm (phía cạnh thẳng)
        public static double LashChainTop => P.LashChainTop;       // chuỗi ngang: trên mép trên tấm
        public static double LashCogSide => P.LashCogSide; public static double LashCogSideNear => P.LashCogSideNear; public static double LashCogTop => P.LashCogTop;   // COG: dọc bên phải (ngoài chuỗi phải / sát khi chuỗi ở trái), ngang trên
        public const double DiaPickX = 0.026, DiaPickY = 0.167;      // Ø lỗ: lỗ gần (tâm + 2.6% bề rộng, đáy + 16.7% chiều cao) — tờ 7D-17P
        public const double DiaAngleDeg = 28;                         // hướng chữ Ø (tờ tay)
        public const double MdBelowTop = 50.3;         // M.D1/M.D2 dưới mép trên tấm
        public const double MdFirstFromCentre = 0.2141; // × bề rộng tấm (M.D1 trái tâm)
        public const double MdSpacing = 0.4067;        // × bề rộng tấm (M.D1 → M.D2)
        public const double MdLeaderDx1 = -9.6, MdLeaderDx2 = 4.8; // điểm mũi tên trên đường chéo, theo X từ góc block M.D

        // ─── Tiêu đề, member list, ghi chú dung sai, khung AM_8 ───
        public static double TitleBelowPlan => P.TitleBelowPlan;     // dưới mép dưới tấm (không có kích thước dài phía dưới)
        public static double TitleBelowDim => P.TitleBelowDim;      // dưới đường kích thước dài khi nó nằm phía dưới
        public static double TitleBelowLashing => P.TitleBelowLashing;
        public const double TitleWidthPlan = 182.2, TitleWidthLashing = 136.1;
        public const double MemberListX = 175.8, MemberListY = 80.2, MemberListWidth = 175.9;
        public const double ToleranceNoteX = 32.031, ToleranceNoteY = 109.714;

        // ─── Ký hiệu chữ B / S / GE / GB / OB / CG ───
        public const double MarkOffset = 4;
        public const double BracketTextOut = 8.5;      // OB/OB1: chữ đẩy ra ngoài bracket (phía mép tấm)
        public const double HpBAlong = 4.25, HpBSide = 3.1; // chữ B: dọc HP về phía bracket / sang trái HP
        public const double CgAlong = 0.9, CgSide = 3.2;    // chữ CG: dọc girder liên tục, lệch về phía girder song song gần nhất (tờ 17C)
        public const double GirderCrossMin = 1000;     // mm thật: girder × girder chỉ ký hiệu liên tục khi cách đầu mút cả 2 tuyến ≥ giá trị này
        public const double FrameTop = 3.3, FrameBottom = 7.9, FrameInset = 0.26, FrameRuleGap = 0.25; // khung AM_8 quanh member list
        public const double LineSpacingFactor = 5.0 / 3.0; // khoảng dòng MText mặc định = 5/3 × cao chữ

        // ─── Cỡ chữ (mm giấy) ───
        public const double TextGirder = 5, TextStiffener = 3, TextMemberList = 3.5, TextTitle = 5;
    }
}
