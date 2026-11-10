using System;
using System.Collections.Generic;
using System.Linq;

namespace MCG_3DPanel.Services.PlanView
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>Mặt cắt điển hình (kỹ sư 2026-11-10): X-X gom mọi thanh phương T, Y-Y mọi thanh phương L, tại toạ độ ngang của chúng.
    /// Thanh xoá trong 3D bị bỏ; tuyến gộp (cùng Line) hiện 1 lần. Hệ cục bộ theo block Y-Y mẫu: mặt trên Top Plate ở y = 0,
    /// tấm từ −t tới 0, web treo bên dưới, flange dưới web, profile HP/BS chèn tại (vị trí, −t).</summary>
    public static class SectionRules
    {
        public const string Layer = "Mechanical-AM_5";
        public const string ThkBlock = "thk_sym";

        /// <param name="planMinX">Bao mặt bằng (Top Plate) trên tờ, Model Space.</param>
        /// <param name="clearPaper">Nội dung xa nhất đã chiếm phía đặt mặt cắt (hàng nhãn), mm giấy; mặt cắt đặt ngoài nó.</param>
        public static PvSection Build(PlanViewInput input, char which, double planMinX, double planMinY, double planMaxX, double planMaxY,
            int scale, double clearPaper)
        {
            bool isX = which == 'X';
            var tp = input.TopPlate;
            double t = input.PlateThickness;
            string dir = isX ? "T" : "L";
            double origin = isX ? tp.MinX : tp.MinY;
            double length = isX ? tp.MaxX - tp.MinX : tp.MaxY - tp.MinY;

            var s = new PvSection { BlockName = input.Panel + (isX ? "_X-X_MCG" : "_Y-Y_MCG") };
            if (isX)
            {
                s.X = planMinX;
                s.Y = planMaxY + Math.Max(PlanViewLayout.SectionGapY, clearPaper > 0 ? clearPaper + PlanViewLayout.SectionBeyondLabels : 0) * scale;
            }
            else
            {
                s.X = planMinX - Math.Max(PlanViewLayout.SectionGapX, clearPaper > 0 ? clearPaper + PlanViewLayout.SectionBeyondLabels : 0) * scale;
                s.Y = planMinY;
                s.Rotation = Math.PI / 2;
            }

            s.Plates.Add(new PvRect { X0 = 0, Y0 = -t, X1 = length, Y1 = 0 });

            // 1 đại diện mỗi tuyến (đoạn dài nhất) cho từng loại.
            var lines = input.Members.Where(m => !m.Deleted && m.Dir == dir)
                .GroupBy(m => (m.Line, m.Kind))
                .Select(g => g.OrderByDescending(Len).First()).ToList();
            double Pos(PvMember m) => (isX ? (m.X0 + m.X1) / 2 : (m.Y0 + m.Y1) / 2) - origin;

            var webH = lines.Where(m => m.Kind == "Web").ToDictionary(m => m.Line, m => m.Height);
            double maxWeb = webH.Count > 0 ? webH.Values.Max() : 0;
            double maxDepth = maxWeb;

            foreach (var w in lines.Where(m => m.Kind == "Web"))
                s.Plates.Add(new PvRect { X0 = Pos(w) - w.Thickness / 2, X1 = Pos(w) + w.Thickness / 2, Y0 = -t - w.Height, Y1 = -t });
            foreach (var f in lines.Where(m => m.Kind == "Flange"))
            {
                double h = webH.TryGetValue(f.Line, out var wh) ? wh : maxWeb;   // flange dưới web của chính tuyến đó
                s.Plates.Add(new PvRect { X0 = Pos(f) - f.Thickness / 2, X1 = Pos(f) + f.Thickness / 2, Y0 = -t - h - f.Height, Y1 = -t - h });
                maxDepth = Math.Max(maxDepth, h + f.Height);
            }
            var profiles = lines.Where(m => (m.Kind == "HP" || m.Kind == "FB") && !string.IsNullOrEmpty(m.ProfileBlock)).OrderBy(Pos).ToList();
            foreach (var p in profiles)
            {
                s.Profiles.Add(new PvInsert { Space = PvSpace.Block, Block = p.ProfileBlock, X = Pos(p), Y = -t, Scale = 1, ScaleIsAbsolute = true, Layer = "0" });
                maxDepth = Math.Max(maxDepth, p.Height);
            }

            // thk_sym tại tâm mỗi tấm (Model Space, xoay theo mặt cắt).
            foreach (var r in s.Plates)
            {
                var (x, y) = ToSheet(s, (r.X0 + r.X1) / 2, (r.Y0 + r.Y1) / 2);
                s.ThkSymbols.Add(new PvInsert { Space = PvSpace.Sheet, Block = ThkBlock, X = x, Y = y, Rotation = s.Rotation, Scale = 1, Layer = "Mechanical-AM_0" });
            }

            // Kích thước bước giữa các profile liền kề, 1 chuỗi dưới mặt cắt.
            double lineLocal = -t - maxDepth - PlanViewLayout.ChainFirstOffset * scale;
            for (int k = 1; k < profiles.Count; k++)
            {
                double a = Pos(profiles[k - 1]), b = Pos(profiles[k]);
                if (b - a < 1e-6) continue;
                var p1 = ToSheet(s, a, -t); var p2 = ToSheet(s, b, -t); var ln = ToSheet(s, (a + b) / 2, lineLocal);
                s.Dims.Add(new PvDim
                {
                    Space = PvSpace.Sheet, X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y, LineX = ln.X, LineY = ln.Y,
                    Rotation = s.Rotation, Layer = Layer,
                });
            }
            return s;
        }

        /// <summary>Ghi các block mặt cắt cần có sẵn trong bản vẽ (profile, thk_sym) vào model.</summary>
        public static void Require(PvSection s, PlanViewModel model)
        {
            foreach (var p in s.Profiles) model.Require(p.Block);
            if (s.ThkSymbols.Count > 0) model.Require(ThkBlock);
        }

        private static (double X, double Y) ToSheet(PvSection s, double lx, double ly)
        {
            double c = Math.Cos(s.Rotation), sn = Math.Sin(s.Rotation);
            return (s.X + lx * c - ly * sn, s.Y + lx * sn + ly * c);
        }

        private static double Len(PvMember m) => Math.Sqrt((m.X1 - m.X0) * (m.X1 - m.X0) + (m.Y1 - m.Y0) * (m.Y1 - m.Y0));
    }
}
