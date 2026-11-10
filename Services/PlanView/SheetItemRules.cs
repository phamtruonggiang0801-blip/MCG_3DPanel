using System;
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

    /// <summary>Mặt bằng lỗ lashing, M.D1/M.D2 + đường chéo + leader, ghi chú dung sai, tiêu đề, khung AM_8 của member list,
    /// khung "Rec. for tolerance" — theo tờ mẫu 7D-17P.</summary>
    public static class SheetItemRules
    {
        public const string TitlePlan = "\\pxqc;{\\L\\C161;PLAN VIEW\\P\\H0.7x;\\l\\C2;(ALL FITTINGS SHOWN ARE FOR REFERENCE LINE)}";
        public const string TitleLashing = "\\pxqc;{\\L\\C161;LASHING HOLES PLAN}";
        public const string ToleranceNote = "Measurements_Tolerances vFuture";
        public const string ToleranceBox = "Rec. for tolerance";
        private const double MdWidth = 1125, MdHeight = 750;   // block M.D1/M.D2 ở tỉ lệ 1:50

        /// <summary>Điểm chèn lần 2 của Top Plate + Lashing Hole: mép trái tấm cách gốc khung 513 mm giấy, hoặc dời trái để chừa lề
        /// phải 45.6 mm (tấm rộng — 7D-17P); cùng cao độ với reference _Assy.</summary>
        public static (double X, double Y) LashingInsertion(PlanViewInput input, SheetContext s)
        {
            double w = (input.TopPlate.MaxX - input.TopPlate.MinX) / s.Scale;
            double left = Math.Min(PlanViewLayout.LashLeftFromFrame, 841 - PlanViewLayout.LashRightMargin - w);
            return (s.FrameX + left * s.Scale - input.TopPlate.MinX, s.AssyTransform[5]);
        }

        private static (double X0, double X1, double Top, double Bottom) LashPlate(PlanViewInput input, SheetContext s)
        {
            var (x, y) = LashingInsertion(input, s);
            var tp = input.TopPlate;
            return (x + tp.MinX, x + tp.MaxX, y + tp.MaxY, y + tp.MinY);
        }

        public static List<PvInsert> LashingPlan(PlanViewInput input, SheetContext s)
        {
            var (x, y) = LashingInsertion(input, s);
            var (md1, md2, mdY) = Md(input, s);
            return new List<PvInsert>
            {
                new PvInsert { Space = PvSpace.Sheet, Block = input.Panel + "_Top Plate", X = x, Y = y, Scale = 1, ScaleIsAbsolute = true, Layer = "Mechanical-AM_0" },
                new PvInsert { Space = PvSpace.Sheet, Block = input.Panel + "_Lashing Hole", X = x, Y = y, Scale = 1, ScaleIsAbsolute = true, Layer = "Mechanical-AM_0" },
                new PvInsert { Space = PvSpace.Sheet, Block = "M.D1", X = md1, Y = mdY, Scale = 1, Layer = "Mechanical-AM_0" },
                new PvInsert { Space = PvSpace.Sheet, Block = "M.D2", X = md2, Y = mdY, Scale = 1, Layer = "Mechanical-AM_0" },
            };
        }

        /// <summary>M.D1/M.D2: cặp đối xứng quanh tâm mặt bằng lỗ lashing (6/6 tờ), dưới mép trên tấm.</summary>
        private static (double Md1, double Md2, double Y) Md(PlanViewInput input, SheetContext s)
        {
            var (x0, x1, top, _) = LashPlate(input, s);
            double w = x1 - x0, c = (x0 + x1) / 2, md1 = c - PlanViewLayout.MdFirstFromCentre * w;
            return (md1, md1 + PlanViewLayout.MdSpacing * w, top - PlanViewLayout.MdBelowTop * s.Scale);
        }

        /// <summary>2 đường chéo đo kiểm (AM_7) qua các góc tấm trên 2 đường cắt giữa panel + 2 leader (posb) từ M.D1/M.D2 tới chúng.</summary>
        public static (List<PvLine> Lines, List<PvLeader> Leaders) Diagonals(PlanViewInput input, SheetContext s)
        {
            var (ix, iy) = LashingInsertion(input, s);
            var tp = input.TopPlate;
            var o = DraftDimRules.Outline(input);
            var (xl, xh) = PlateGeometry.Cut(o, 'X', (tp.MinY + tp.MaxY) / 2, tp);
            var (yl, yh) = PlateGeometry.Cut(o, 'Y', (tp.MinX + tp.MaxX) / 2, tp);
            var d1 = new PvLine { Space = PvSpace.Sheet, X1 = ix + xl, Y1 = iy + yh, X2 = ix + xh, Y2 = iy + yl, Layer = "Mechanical-AM_7" };
            var d2 = new PvLine { Space = PvSpace.Sheet, X1 = ix + xl, Y1 = iy + yl, X2 = ix + xh, Y2 = iy + yh, Layer = "Mechanical-AM_7" };

            var (md1, md2, mdY) = Md(input, s);
            double f = s.Scale / 50.0, cornerY = mdY - MdHeight * f;
            PvLeader Leader(PvLine d, double cornerX, double dxPaper)
            {
                double px = cornerX + dxPaper * s.Scale, py = d.Y1 + (d.Y2 - d.Y1) * (px - d.X1) / (d.X2 - d.X1);
                var l = new PvLeader { Space = PvSpace.Sheet };
                l.Points.Add((px, py)); l.Points.Add((cornerX, cornerY));
                return l;
            }
            return (new List<PvLine> { d1, d2 },
                    new List<PvLeader> { Leader(d1, md1, PlanViewLayout.MdLeaderDx1), Leader(d2, md2 + MdWidth * f, PlanViewLayout.MdLeaderDx2) });
        }

        public static List<PvInsert> Fixed(double frameX, double frameY, int scale) => new List<PvInsert>
        {
            new PvInsert { Space = PvSpace.Sheet, Block = ToleranceNote, Layer = "Mechanical-AM_6", Scale = 1,
                           X = frameX + PlanViewLayout.ToleranceNoteX * scale, Y = frameY + PlanViewLayout.ToleranceNoteY * scale },
        };

        /// <summary>Tiêu đề MText TopCenter (định dạng y như tờ tay), căn giữa dưới tấm của hình chiếu.</summary>
        public static PvText Title(string text, double plateMinX, double plateMaxX, double plateMinY, int scale, double paperWidth)
            => new PvText
            {
                Space = PvSpace.Sheet, IsMText = true, Category = "Title", Justify = "TopCenter", Text = text,
                Layer = "Mechanical-AM_1", PaperHeight = PlanViewLayout.TextTitle, PaperWidth = paperWidth,
                X = (plateMinX + plateMaxX) / 2, Y = plateMinY - PlanViewLayout.TitleBelow * scale,
            };

        public static PvText LashingTitle(PlanViewInput input, SheetContext s)
        {
            var (x0, x1, _, bottom) = LashPlate(input, s);
            return Title(TitleLashing, x0, x1, bottom, s.Scale, PlanViewLayout.TitleWidthLashing);
        }

        /// <summary>Khung 3 lớp AM_8 chứa member list + 3 vạch ngăn 3 nét (toạ độ tờ 7D-17P, mm giấy từ gốc khung).</summary>
        public static List<PvPolyline> MemberFramePolylines(double frameX, double frameY, int scale)
        {
            var r = new List<PvPolyline>();
            foreach (var (x0, y0, x1, y1) in new[] { (172.738, 22.082, 328.400, 83.508), (172.998, 22.342, 328.140, 83.248), (173.258, 22.602, 327.880, 82.988) })
            {
                var p = new PvPolyline { Space = PvSpace.Sheet, Layer = "Mechanical-AM_8" };
                foreach (var (x, y) in new[] { (x0, y0), (x1, y0), (x1, y1), (x0, y1) }) p.Points.Add((frameX + x * scale, frameY + y * scale));
                r.Add(p);
            }
            return r;
        }

        public static List<PvLine> MemberFrameLines(double frameX, double frameY, int scale)
            => new[] { 71.832, 72.082, 72.332, 60.832, 61.082, 61.332, 48.334, 48.584, 48.834 }
                .Select(y => new PvLine { Space = PvSpace.Sheet, Layer = "Mechanical-AM_8",
                                          X1 = frameX + 173.238 * scale, X2 = frameX + 327.900 * scale, Y1 = frameY + y * scale, Y2 = frameY + y * scale })
                .ToList();

        /// <summary>1 khung "Rec. for tolerance" giữa đường mỗi kích thước bao (kích thước phải đo kiểm sau chế tạo), xoay theo kích thước.</summary>
        public static List<PvInsert> ToleranceBoxes(IReadOnlyList<PvDim> overall, int scale)
            => overall.Where(d => d.Kind == "Rotated").Select(d =>
            {
                bool horiz = Math.Abs(Math.Sin(d.Rotation)) < 0.01;
                return new PvInsert
                {
                    Space = d.Space, Block = ToleranceBox, Rotation = d.Rotation, Scale = scale, ScaleIsAbsolute = true, Layer = "Mechanical-AM_0",
                    X = horiz ? (d.X1 + d.X2) / 2 : d.LineX, Y = horiz ? d.LineY : (d.Y1 + d.Y2) / 2,
                };
            }).ToList();
    }
}
