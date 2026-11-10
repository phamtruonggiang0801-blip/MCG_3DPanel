using System;
using System.Collections.Generic;
using System.Linq;

namespace MCG_3DPanel.Services.PlanView
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>Kích thước tờ từ kích thước nháp AM_9 (kỹ sư 2026-11-10: chép nguyên, không theo xoá/gộp 3D) + kích thước bao
    /// Top Plate ±4 + đường kính lỗ lashing. Đo trên 6 tờ mẫu: AM_4 = 100% bản sao nháp Lashing Hole (cùng trục, cùng khoảng;
    /// điểm gốc dời ra mép tấm, đường kích thước ra ngoài thành chuỗi).</summary>
    public static class DraftDimRules
    {
        public const string LayerLashing = "Mechanical-AM_4";
        public const string LayerStructure = "Mechanical-AM_5";
        public const string OverallText = "<>{}{\\H0.71x;\\C3;%%p4}{{}}TOP PLATE";
        public const string LashingText = "<>-LASHING HOLES";
        private const double Touch = 1.0;   // mm: 2 khoảng chạm nhau ⇒ cùng chuỗi

        private sealed class Item
        {
            public PvDraft D;
            public string Side;     // top | bottom | left | right | "" (xiên)
            public double Lo, Hi;   // khoảng trên trục
        }

        /// <param name="shift">Dời toàn bộ (mặt bằng lỗ lashing chèn lần 2 bên phải).</param>
        public static List<PvDim> FromDrafts(PlanViewInput input, int scale, (double dx, double dy) shift = default)
        {
            var tp = input.TopPlate;
            var items = new List<Item>();
            foreach (var d in input.Drafts)
            {
                double s = Math.Abs(Math.Sin(d.Rotation)), c = Math.Abs(Math.Cos(d.Rotation));
                var it = new Item { D = d, Side = "" };
                if (s < 0.01)        // đo theo X
                {
                    double y = (d.Y1 + d.Y2) / 2;
                    it.Side = (y - tp.MinY) <= (tp.MaxY - y) ? "bottom" : "top";
                    it.Lo = Math.Min(d.X1, d.X2); it.Hi = Math.Max(d.X1, d.X2);
                }
                else if (c < 0.01)   // đo theo Y
                {
                    double x = (d.X1 + d.X2) / 2;
                    it.Side = (x - tp.MinX) <= (tp.MaxX - x) ? "left" : "right";
                    it.Lo = Math.Min(d.Y1, d.Y2); it.Hi = Math.Max(d.Y1, d.Y2);
                }
                items.Add(it);
            }

            var result = new List<PvDim>();
            foreach (var side in items.Where(i => i.Side != "").GroupBy(i => i.Side))
            {
                // Chuỗi = các khoảng nối đầu–cuối; chuỗi chồng nhau xếp ra các mức ngoài hơn (tham lam theo điểm đầu).
                var chains = BuildChains(side.ToList());
                var levelEnds = new List<double>();
                foreach (var ch in chains.OrderBy(ch => ch.Min(i => i.Lo)))
                {
                    double lo = ch.Min(i => i.Lo), hi = ch.Max(i => i.Hi);
                    int level = levelEnds.FindIndex(end => end <= lo + Touch);
                    if (level < 0) { levelEnds.Add(hi); level = levelEnds.Count - 1; } else levelEnds[level] = hi;
                    double off = (PlanViewLayout.ChainFirstOffset + level * PlanViewLayout.ChainSpacing) * scale;
                    foreach (var it in ch) result.Add(Place(it, side.Key, off, tp, shift));
                }
            }
            // Nháp xiên: chép nguyên điểm, đường kích thước lệch vuông góc 1 khoảng chuỗi đầu.
            foreach (var it in items.Where(i => i.Side == ""))
            {
                var d = it.D;
                double nx = -Math.Sin(d.Rotation), ny = Math.Cos(d.Rotation), off = PlanViewLayout.ChainFirstOffset * scale;
                result.Add(new PvDim
                {
                    Space = PvSpace.Block, X1 = d.X1 + shift.dx, Y1 = d.Y1 + shift.dy, X2 = d.X2 + shift.dx, Y2 = d.Y2 + shift.dy,
                    LineX = (d.X1 + d.X2) / 2 + nx * off + shift.dx, LineY = (d.Y1 + d.Y2) / 2 + ny * off + shift.dy,
                    Rotation = d.Rotation, Layer = LayerOf(d),
                });
            }
            return result;
        }

        /// <summary>Kích thước bao dài (đáy) + rộng (trái) với chữ ±4, ở mức ngoài chuỗi xa nhất phía đó; + Ø lỗ lashing nếu có.</summary>
        public static List<PvDim> Overall(PlanViewInput input, int scale, IReadOnlyList<PvDim> drafts)
        {
            var tp = input.TopPlate;
            var r = new List<PvDim>();
            double bottom = Next(OuterOffset(drafts, input, "bottom", scale)) * scale;
            double left = Next(OuterOffset(drafts, input, "left", scale)) * scale;
            r.Add(new PvDim
            {
                Space = PvSpace.Block, X1 = tp.MinX, Y1 = tp.MinY, X2 = tp.MaxX, Y2 = tp.MinY, LineX = (tp.MinX + tp.MaxX) / 2, LineY = tp.MinY - bottom,
                Rotation = 0, Layer = LayerStructure, TextOverride = OverallText,
            });
            r.Add(new PvDim
            {
                Space = PvSpace.Block, X1 = tp.MinX, Y1 = tp.MinY, X2 = tp.MinX, Y2 = tp.MaxY, LineX = tp.MinX - left, LineY = (tp.MinY + tp.MaxY) / 2,
                Rotation = Math.PI / 2, Layer = LayerStructure, TextOverride = OverallText,
            });
            if (input.Lashing != null)
            {
                var h = input.Lashing; double rad = h.Diameter / 2;
                r.Add(new PvDim
                {
                    Space = PvSpace.Block, Kind = "Diametric", X1 = h.X + rad, Y1 = h.Y, X2 = h.X - rad, Y2 = h.Y,
                    Layer = LayerStructure, DimStyle = "AM_ISO$3", TextOverride = LashingText,
                });
            }
            return r;
        }

        /// <summary>Khoảng cách (mm giấy) từ mép Top Plate tới đường kích thước xa nhất phía <paramref name="side"/>; 0 = phía đó trống.</summary>
        public static double OuterOffset(IReadOnlyList<PvDim> dims, PlanViewInput input, string side, int scale)
        {
            var tp = input.TopPlate;
            double best = 0;
            foreach (var d in dims)
            {
                if (d.Kind != "Rotated") continue;
                bool horiz = Math.Abs(Math.Sin(d.Rotation)) < 0.01, vert = Math.Abs(Math.Cos(d.Rotation)) < 0.01;
                double off =
                    side == "bottom" && horiz ? tp.MinY - d.LineY :
                    side == "top" && horiz ? d.LineY - tp.MaxY :
                    side == "left" && vert ? tp.MinX - d.LineX :
                    side == "right" && vert ? d.LineX - tp.MaxX : 0;
                if (off / scale > best) best = off / scale;
            }
            return best;
        }

        private static double Next(double outer) => outer <= 0 ? PlanViewLayout.ChainFirstOffset : outer + PlanViewLayout.ChainSpacing;

        private static List<List<Item>> BuildChains(List<Item> items)
        {
            var chains = new List<List<Item>>();
            foreach (var it in items.OrderBy(i => i.Lo).ThenBy(i => i.Hi))
            {
                var ch = chains.FirstOrDefault(c => Math.Abs(c[c.Count - 1].Hi - it.Lo) < Touch);
                if (ch != null) ch.Add(it); else chains.Add(new List<Item> { it });
            }
            return chains;
        }

        private static PvDim Place(Item it, string side, double off, (double MinX, double MinY, double MaxX, double MaxY) tp, (double dx, double dy) shift)
        {
            var dim = new PvDim { Space = PvSpace.Block, Layer = LayerOf(it.D) };
            switch (side)
            {
                case "bottom":
                case "top":
                    double ey = side == "bottom" ? tp.MinY : tp.MaxY;
                    dim.X1 = it.Lo; dim.X2 = it.Hi; dim.Y1 = dim.Y2 = ey;
                    dim.LineX = (it.Lo + it.Hi) / 2; dim.LineY = side == "bottom" ? ey - off : ey + off;
                    dim.Rotation = 0;
                    break;
                default:
                    double ex = side == "left" ? tp.MinX : tp.MaxX;
                    dim.Y1 = it.Lo; dim.Y2 = it.Hi; dim.X1 = dim.X2 = ex;
                    dim.LineY = (it.Lo + it.Hi) / 2; dim.LineX = side == "left" ? ex - off : ex + off;
                    dim.Rotation = Math.PI / 2;
                    break;
            }
            dim.X1 += shift.dx; dim.X2 += shift.dx; dim.LineX += shift.dx;
            dim.Y1 += shift.dy; dim.Y2 += shift.dy; dim.LineY += shift.dy;
            return dim;
        }

        private static string LayerOf(PvDraft d) => d.Source == "LashingHole" ? LayerLashing : LayerStructure;
    }
}
