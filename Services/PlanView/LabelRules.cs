using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace MCG_3DPanel.Services.PlanView
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>Nhãn tên (1 cái mỗi tuyến tab Naming) + member list MText. Theo chuẩn 7D-17C: mọi nhãn MiddleLeft; phương T chữ căn giữa
    /// đường tâm thanh (writer đo bề rộng chữ — Justify "CenterMiddleLeft"), phương L chữ bắt đầu tại cột tính từ mặt cắt Y-Y.</summary>
    public static class LabelRules
    {
        public const string LayerGirder = "Mechanical-AM_1";
        public const string LayerStiffener = "Mechanical-AM_6";

        public static List<PvText> Names(PlanViewInput input, int scale)
        {
            var tp = input.TopPlate;
            var r = new List<PvText>();
            foreach (var line in input.Members
                         .Where(m => !m.Deleted && !string.IsNullOrEmpty(m.Name) && m.Kind != "Flange")
                         .GroupBy(m => m.Line))
            {
                var first = line.First();
                bool girder = first.Kind == "Web";
                bool isT = first.Dir == "T";
                // Đoạn chạm mép (T: lên cao nhất; L: sang trái nhất) — nhãn trên đường tâm của nó.
                var piece = isT ? line.OrderByDescending(m => System.Math.Max(m.Y0, m.Y1)).First()
                                : line.OrderBy(m => System.Math.Min(m.X0, m.X1)).First();
                var t = new PvText
                {
                    Space = PvSpace.Block, Text = first.Name, Category = "Name",
                    Layer = girder ? LayerGirder : LayerStiffener,
                    PaperHeight = girder ? PlanViewLayout.TextGirder : PlanViewLayout.TextStiffener,
                };
                if (isT)
                {
                    double row = girder ? PlanViewLayout.LabelTopGirder : first.Kind == "HP" ? PlanViewLayout.LabelTopHp : PlanViewLayout.LabelTopFb;
                    t.Justify = "CenterMiddleLeft";
                    t.X = (piece.X0 + piece.X1) / 2;
                    t.Y = tp.MaxY + row * scale;
                }
                else
                {
                    t.Justify = "MiddleLeft";
                    t.X = tp.MinX - (SectionRules.YLeft(input) - (girder ? PlanViewLayout.LabelLeftGirderFromSection : PlanViewLayout.LabelLeftStiffenerFromSection)) * scale;
                    t.Y = (piece.Y0 + piece.Y1) / 2;
                }
                r.Add(t);
            }
            return r;
        }

        /// <summary>MText quy cách: chiều dày tấm, quy cách nẹp HP, rồi mỗi quy cách girder T / L một dòng (tên theo thứ tự tự nhiên).</summary>
        /// <summary>Dòng của member list: nhóm chiều dày tấm / nẹp / girder T / girder L, giữa các nhóm 1 dòng trống
        /// (tờ tay 7D-17P: vạch ngăn 3 nét của khung AM_8 nằm giữa các dòng trống này).</summary>
        public static List<string> MemberListLines(PlanViewInput input)
        {
            var inv = CultureInfo.InvariantCulture;
            var groups = new List<List<string>> { new List<string> { "TOP PLATE THICKNESS: " + input.PlateThickness.ToString("0.0", inv) } };
            var live = input.Members.Where(m => !m.Deleted && !string.IsNullOrEmpty(m.Name)).ToList();
            var hp = live.Where(m => m.Kind == "HP" && !string.IsNullOrEmpty(m.Spec)).Select(m => m.Spec).Distinct().ToList();
            if (hp.Count > 0) groups.Add(new List<string> { "STIFFENERS (HL & HT): " + string.Join(", ", hp) });
            foreach (var (dir, title) in new[] { ("T", "TRANSVERSAL GIRDERS"), ("L", "LONGITUDINAL GIRDERS") })
            {
                var webs = live.Where(m => m.Kind == "Web" && m.Dir == dir && !string.IsNullOrEmpty(m.Spec))
                               .GroupBy(m => m.Line).Select(g => g.First()).ToList();
                var g = new List<string>();
                foreach (var spec in webs.GroupBy(m => m.Spec).OrderBy(x => x.Select(m => m.Name).OrderBy(n => n, Natural).First(), Natural))
                {
                    var names = spec.Select(m => m.Name).Distinct().OrderBy(n => n, Natural);
                    g.Add($"{title} {string.Join(",", names)} : {spec.Key}");
                }
                if (g.Count > 0) groups.Add(g);
            }
            var lines = new List<string>();
            foreach (var g in groups) { if (lines.Count > 0) lines.Add(""); lines.AddRange(g); }
            return lines;
        }

        public static PvText MemberList(PlanViewInput input, double frameX, double frameY, int scale)
        {
            var lines = MemberListLines(input);
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
