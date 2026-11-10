using System.Collections.Generic;
using System.Linq;

namespace MCG_3DPanel.Services.PlanView
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>Bối cảnh tờ: tỉ lệ, gốc khung A1, bao Top Plate của mặt bằng (Model Space), transform của reference _Assy [a b c d tx ty].</summary>
    public sealed class SheetContext
    {
        public int Scale { get; set; }
        public double FrameX { get; set; }
        public double FrameY { get; set; }
        public double PlanMinX { get; set; }
        public double PlanMinY { get; set; }
        public double PlanMaxX { get; set; }
        public double PlanMaxY { get; set; }
        public double[] AssyTransform { get; set; }
    }

    /// <summary>Mặt bằng lỗ lashing, M.D1/M.D2, ghi chú dung sai, tiêu đề, khung "Rec. for tolerance" (placement rules 2026-11-10).</summary>
    public static class SheetItemRules
    {
        public const string TitlePlan = "PLAN VIEW\\P(ALL FITTINGS SHOWN ARE FOR REFERENCE LINE)";
        public const string TitleLashing = "LASHING HOLES PLAN";
        public const string ToleranceNote = "Measurements_Tolerances vFuture";
        public const string ToleranceBox = "Rec. for tolerance";

        /// <summary>Điểm chèn (Model Space) của lần chèn thứ 2 các block Top Plate + Lashing Hole: tấm bắt đầu cách mép phải mặt bằng
        /// LashingPlanGap, cùng cao độ với reference _Assy (đo: lệch Y = 0).</summary>
        public static (double X, double Y) LashingInsertion(PlanViewInput input, SheetContext s)
            => (s.PlanMaxX + PlanViewLayout.LashingPlanGap * s.Scale - input.TopPlate.MinX, s.AssyTransform[5]);

        public static List<PvInsert> LashingPlan(PlanViewInput input, SheetContext s)
        {
            var (x, y) = LashingInsertion(input, s);
            double plateMinX = x + input.TopPlate.MinX, plateMaxY = y + input.TopPlate.MaxY;
            double mdX = plateMinX + PlanViewLayout.MdOffsetX * s.Scale, mdY = plateMaxY + PlanViewLayout.MdOffsetY * s.Scale;
            return new List<PvInsert>
            {
                new PvInsert { Space = PvSpace.Sheet, Block = input.Panel + "_Top Plate", X = x, Y = y, Scale = 1, ScaleIsAbsolute = true, Layer = "Mechanical-AM_0" },
                new PvInsert { Space = PvSpace.Sheet, Block = input.Panel + "_Lashing Hole", X = x, Y = y, Scale = 1, ScaleIsAbsolute = true, Layer = "Mechanical-AM_0" },
                new PvInsert { Space = PvSpace.Sheet, Block = "M.D1", X = mdX, Y = mdY, Scale = 1, Layer = "Mechanical-AM_0" },
                new PvInsert { Space = PvSpace.Sheet, Block = "M.D2", X = mdX + PlanViewLayout.MdSpacing * s.Scale, Y = mdY, Scale = 1, Layer = "Mechanical-AM_0" },
            };
        }

        public static List<PvInsert> Fixed(double frameX, double frameY, int scale) => new List<PvInsert>
        {
            new PvInsert { Space = PvSpace.Sheet, Block = ToleranceNote, Layer = "Mechanical-AM_6", Scale = 1,
                           X = frameX + PlanViewLayout.ToleranceNoteX * scale, Y = frameY + PlanViewLayout.ToleranceNoteY * scale },
        };

        /// <summary>Tiêu đề MText TopCenter, căn giữa dưới tấm của hình chiếu, ngoài chuỗi kích thước đáy xa nhất.</summary>
        public static PvText Title(string text, double plateMinX, double plateMaxX, double plateMinY, int scale, double bottomOuter)
        {
            double below = System.Math.Max(PlanViewLayout.TitleBelow, bottomOuter > 0 ? bottomOuter + PlanViewLayout.LabelBeyondChain : 0);
            return new PvText
            {
                Space = PvSpace.Sheet, IsMText = true, Category = "Title", Justify = "TopCenter", Text = text,
                Layer = "Mechanical-AM_1", PaperHeight = PlanViewLayout.TextTitle, PaperWidth = PlanViewLayout.TitleWidth,
                X = (plateMinX + plateMaxX) / 2, Y = plateMinY - below * scale,
            };
        }

        /// <summary>1 khung "Rec. for tolerance" tại chữ của mỗi kích thước bao (kích thước phải đo kiểm sau chế tạo), xoay theo kích thước.</summary>
        public static List<PvInsert> ToleranceBoxes(IReadOnlyList<PvDim> overall, int scale)
            => overall.Where(d => d.Kind == "Rotated").Select(d => new PvInsert
            {
                Space = d.Space, Block = ToleranceBox, X = d.LineX, Y = d.LineY, Rotation = d.Rotation,
                Scale = scale, ScaleIsAbsolute = true, Layer = "Mechanical-AM_0",
            }).ToList();
    }
}
