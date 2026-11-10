using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace MCG_3DPanel.Services.PlanView
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>Nhãn tên (1 cái mỗi tuyến tab Naming) + member list MText. Vị trí: docs/.../2026-11-10-plan-view-placement.md.</summary>
    public static class LabelRules
    {
        public const string LayerGirder = "Mechanical-AM_1";
        public const string LayerStiffener = "Mechanical-AM_6";

        /// <param name="topOuter">Chuỗi kích thước xa nhất phía trên (mm giấy, 0 = trống) — hàng nhãn đẩy ra ngoài nó.</param>
        /// <param name="leftOuter">Như trên, phía trái.</param>
        public static List<PvText> Names(PlanViewInput input, int scale, double topOuter, double leftOuter)
        {
            var tp = input.TopPlate;
            double rowStiff = Math.Max(PlanViewLayout.LabelTopStiffener, topOuter > 0 ? topOuter + PlanViewLayout.LabelBeyondChain : 0);
            double rowGirder = Math.Max(PlanViewLayout.LabelTopGirder, rowStiff + PlanViewLayout.LabelGirderBeyondStiffener);
            double colLeft = Math.Max(PlanViewLayout.LabelLeft, leftOuter > 0 ? leftOuter + PlanViewLayout.LabelBeyondChain : 0);

            var r = new List<PvText>();
            foreach (var line in input.Members
                         .Where(m => !m.Deleted && !string.IsNullOrEmpty(m.Name) && m.Kind != "Flange")
                         .GroupBy(m => m.Line))
            {
                var first = line.First();
                bool girder = first.Kind == "Web";
                bool isT = first.Dir == "T";
                // Đoạn chạm mép (T: lên cao nhất; L: sang trái nhất) — đặt nhãn trên đường tâm của nó.
                var piece = isT ? line.OrderByDescending(m => Math.Max(m.Y0, m.Y1)).First()
                                : line.OrderBy(m => Math.Min(m.X0, m.X1)).First();
                var t = new PvText
                {
                    Space = PvSpace.Block, Text = first.Name, Category = "Name",
                    Layer = girder ? LayerGirder : LayerStiffener,
                    PaperHeight = girder ? PlanViewLayout.TextGirder : PlanViewLayout.TextStiffener,
                };
                if (isT)
                {
                    t.Justify = "BottomCenter";
                    t.X = (piece.X0 + piece.X1) / 2;
                    t.Y = tp.MaxY + (girder ? rowGirder : rowStiff) * scale;
                }
                else
                {
                    t.Justify = "MiddleLeft";
                    t.X = tp.MinX - colLeft * scale;
                    t.Y = (piece.Y0 + piece.Y1) / 2;
                }
                r.Add(t);
            }
            return r;
        }

        /// <summary>MText quy cách: chiều dày tấm, quy cách nẹp HP, rồi mỗi quy cách girder T / L một dòng (tên theo thứ tự tự nhiên).</summary>
        public static PvText MemberList(PlanViewInput input, double frameX, double frameY, int scale)
        {
            var inv = CultureInfo.InvariantCulture;
            var lines = new List<string> { "TOP PLATE THICKNESS: " + input.PlateThickness.ToString("0.0", inv), "" };
            var live = input.Members.Where(m => !m.Deleted && !string.IsNullOrEmpty(m.Name)).ToList();
            var hp = live.Where(m => m.Kind == "HP" && !string.IsNullOrEmpty(m.Spec)).Select(m => m.Spec).Distinct().ToList();
            if (hp.Count > 0) { lines.Add("STIFFENERS (HL & HT): " + string.Join(", ", hp)); lines.Add(""); }
            foreach (var (dir, title) in new[] { ("T", "TRANSVERSAL GIRDERS"), ("L", "LONGITUDINAL GIRDERS") })
            {
                var webs = live.Where(m => m.Kind == "Web" && m.Dir == dir && !string.IsNullOrEmpty(m.Spec))
                               .GroupBy(m => m.Line).Select(g => g.First()).ToList();
                foreach (var spec in webs.GroupBy(m => m.Spec).OrderBy(g => g.Select(m => m.Name).OrderBy(n => n, Natural).First(), Natural))
                {
                    var names = spec.Select(m => m.Name).Distinct().OrderBy(n => n, Natural);
                    lines.Add($"{title} {string.Join(",", names)} : {spec.Key}");
                }
            }
            while (lines.Count > 0 && lines[lines.Count - 1] == "") lines.RemoveAt(lines.Count - 1);
            return new PvText
            {
                Space = PvSpace.Sheet, IsMText = true, Category = "MemberList", Justify = "TopLeft",
                X = frameX + PlanViewLayout.MemberListX * scale, Y = frameY + PlanViewLayout.MemberListY * scale,
                PaperWidth = PlanViewLayout.MemberListWidth, PaperHeight = PlanViewLayout.TextMemberList,
                Layer = LayerStiffener, Text = string.Join("\\P", lines),
            };
        }

        private static readonly IComparer<string> Natural = Comparer<string>.Create(CompareNatural);

        /// <summary>T2 &lt; T10; L1 &lt; L7.</summary>
        private static int CompareNatural(string a, string b)
        {
            var ma = Regex.Match(a ?? "", @"^(\D*)(\d*)(.*)$"); var mb = Regex.Match(b ?? "", @"^(\D*)(\d*)(.*)$");
            int c = string.CompareOrdinal(ma.Groups[1].Value, mb.Groups[1].Value);
            if (c != 0) return c;
            int na = ma.Groups[2].Value.Length > 0 ? int.Parse(ma.Groups[2].Value) : -1;
            int nb = mb.Groups[2].Value.Length > 0 ? int.Parse(mb.Groups[2].Value) : -1;
            if (na != nb) return na.CompareTo(nb);
            return string.CompareOrdinal(ma.Groups[3].Value, mb.Groups[3].Value);
        }
    }
}
