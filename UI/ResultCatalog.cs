using System;
using System.Collections.Generic;
using System.Linq;

namespace MCG_3DPanel.UI
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (được link sang tests/MCG_3DPanel.Tests để unit test).

    /// <summary>Họ kết cấu hiển thị trên tab Result (kỹ sư chốt 2026-11-04).</summary>
    public enum ResultFamily { Topplate, Web, Flange, Bracket, HP, FB, Collar }

    /// <summary>Ý nghĩa 1 lựa chọn Kind khi người dùng sửa — ClassificationService áp theo intent này.</summary>
    public enum CorrectionIntent
    {
        None, ForceWeb, ForceGE, ForceGB, ClosedPlate, Flange, FacePlate,
        BracketOB, BracketIB, BracketBF, BackingPlate,
    }

    /// <summary>1 mục dropdown Kind. <see cref="Enabled"/>=false → mờ, kèm <see cref="Tooltip"/> giải thích.</summary>
    public sealed class KindOption
    {
        public string Text { get; set; }
        public bool Enabled { get; set; } = true;
        public string Tooltip { get; set; }
        public override string ToString() => Text;
    }

    /// <summary>Mô tả 1 dòng: họ, Kind hiện tại, các Kind được phép đổi (rỗng ⇒ chỉ đọc), có nút đảo chiều không.</summary>
    public sealed class RowDescriptor
    {
        public ResultFamily Family { get; set; }
        public string Kind { get; set; }
        public List<KindOption> Options { get; set; } = new List<KindOption>();
        public bool CanFlip { get; set; }
    }

    /// <summary>Danh mục họ → Kind (spec 2026-11-04 mục 3). Dropdown chỉ đổi trong cùng họ.</summary>
    public static class ResultCatalog
    {
        public const string NoGbAnchorTip = "No crossing or nearby HP/FB to locate the GB toe";
        public const string NoGeZoneTip = "No perpendicular web to end the GE";

        private static readonly string[] BracketKinds = { "OB", "IB", "BF", "Backing Plate" };
        private static readonly string[] FlangeKinds = { "Main Flange", "Faceplate" };

        /// <param name="partType">PartType.ToString() khi có part 2D; null với solid sinh thêm.</param>
        /// <param name="solidType">Loại solid sinh thêm: "CollarPlate" / "HpEndBracketB" / "GirderFlange".</param>
        /// <param name="mergedCodes">Code GE/GB đã hợp nhất vào solid Web này (vd "GE-HP").</param>
        /// <param name="endKinds">Loại member ở các đầu web chạm HP/FB ("HP"/"FB").</param>
        /// <param name="geAvailable">Có Web vuông góc để kết thúc GE.</param>
        /// <param name="gbAvailable">Có mốc toe GB (HP/FB xuyên hoặc dừng ≤ 50mm).</param>
        public static RowDescriptor Describe(string partType, string code, string solidType,
            IReadOnlyList<string> mergedCodes, IReadOnlyList<string> endKinds, bool geAvailable, bool gbAvailable)
        {
            code = code ?? "";
            mergedCodes = mergedCodes ?? Array.Empty<string>();
            endKinds = endKinds ?? Array.Empty<string>();

            if (partType == null)
            {
                switch (solidType)
                {
                    case "CollarPlate": return ReadOnly(ResultFamily.Collar, "Collar Plate");
                    case "HpEndBracketB": return ReadOnly(ResultFamily.Bracket, "B");
                    case "GirderFlange": return ReadOnly(ResultFamily.Flange, "Flange GE/GB-" + Suffix(code, "HP"));
                    default: return ReadOnly(ResultFamily.Web, solidType ?? "unknown");
                }
            }

            switch (partType)
            {
                case "TopPlate": return ReadOnly(ResultFamily.Topplate, "Topplate");
                case "HP": return new RowDescriptor { Family = ResultFamily.HP, Kind = "HP", CanFlip = true };
                case "FlatBar": return ReadOnly(ResultFamily.FB, "FB");

                case "Web":
                {
                    string kind = mergedCodes.Count == 0 ? "Web" : "Web + " + string.Join(" + ", mergedCodes);
                    return WebRow(kind, endKinds, geAvailable, gbAvailable);
                }
                case "ClosedPlate": return WebRow("Closed Plate", endKinds, geAvailable, gbAvailable);

                case "GirderEnd":
                {
                    string k = Suffix(code, "HP");
                    bool isGb = code.StartsWith("GB");
                    var d = new RowDescriptor { Family = isGb ? ResultFamily.Bracket : ResultFamily.Web, Kind = isGb ? "GB-" + k : "GE-" + k };
                    // Lựa chọn KHÁC loại hiện tại mờ khi không áp được (cùng quy tắc với WebRow) — spec §9.
                    d.Options.Add(new KindOption
                    {
                        Text = "GE-" + k, Enabled = !isGb || geAvailable, Tooltip = !isGb || geAvailable ? null : NoGeZoneTip,
                    });
                    d.Options.Add(new KindOption
                    {
                        Text = "GB-" + k, Enabled = isGb || gbAvailable, Tooltip = isGb || gbAvailable ? null : NoGbAnchorTip,
                    });
                    d.Options.Add(new KindOption { Text = "Web" });
                    return d;
                }

                case "Flange": return Choice(ResultFamily.Flange, "Main Flange", FlangeKinds, false);
                case "FacePlate": return Choice(ResultFamily.Flange, "Faceplate", FlangeKinds, false);

                case "Bracket":
                {
                    string kind = BracketKinds.Take(3).FirstOrDefault(k => code.StartsWith(k)) ?? "OB";
                    return Choice(ResultFamily.Bracket, kind, BracketKinds, true);
                }
                case "BackingPlate": return Choice(ResultFamily.Bracket, "Backing Plate", BracketKinds, false);

                default: return ReadOnly(ResultFamily.Web, partType);
            }
        }

        /// <summary>Nhãn 2 đầu thanh theo hướng trục (dx, dy): ngang/chéo ≤45° ⇒ ("L","R"), đứng ⇒ ("B","T").
        /// "lo" = đầu X nhỏ (ngang) / Y nhỏ (đứng) — correction Sniped End lưu theo lo/hi, không theo thứ tự đỉnh.</summary>
        public static (string lo, string hi) EndLabels(double dx, double dy) =>
            Math.Abs(dx) >= Math.Abs(dy) ? ("L", "R") : ("B", "T");

        /// <summary>Kind người dùng chọn → việc ClassificationService phải làm.</summary>
        public static CorrectionIntent ParseIntent(string kind)
        {
            if (string.IsNullOrWhiteSpace(kind)) return CorrectionIntent.None;
            if (kind == "Web") return CorrectionIntent.ForceWeb;
            if (kind.StartsWith("Web + GE") || kind.StartsWith("GE-")) return CorrectionIntent.ForceGE;
            if (kind.StartsWith("Web + GB") || kind.StartsWith("GB-")) return CorrectionIntent.ForceGB;
            switch (kind)
            {
                case "Closed Plate": return CorrectionIntent.ClosedPlate;
                case "Main Flange": return CorrectionIntent.Flange;
                case "Faceplate": return CorrectionIntent.FacePlate;
                case "OB": return CorrectionIntent.BracketOB;
                case "IB": return CorrectionIntent.BracketIB;
                case "BF": return CorrectionIntent.BracketBF;
                case "Backing Plate": return CorrectionIntent.BackingPlate;
                default: return CorrectionIntent.None;
            }
        }

        // ─── Nội bộ ───────────────────────────────────────────────────────

        /// <summary>Web / Closed Plate: Web · Web + GE-{k} · Web + GB-{k} (khi có đầu chạm HP/FB) · Closed Plate.</summary>
        private static RowDescriptor WebRow(string kind, IReadOnlyList<string> endKinds, bool geAvailable, bool gbAvailable)
        {
            var d = new RowDescriptor { Family = ResultFamily.Web, Kind = kind };
            d.Options.Add(new KindOption { Text = "Web" });
            var ks = endKinds.Distinct().ToList();
            if (ks.Count > 0)
            {
                d.Options.Add(new KindOption
                {
                    Text = "Web + " + string.Join(" + ", ks.Select(k => "GE-" + k)),
                    Enabled = geAvailable, Tooltip = geAvailable ? null : NoGeZoneTip,
                });
                d.Options.Add(new KindOption
                {
                    Text = "Web + " + string.Join(" + ", ks.Select(k => "GB-" + k)),
                    Enabled = gbAvailable, Tooltip = gbAvailable ? null : NoGbAnchorTip,
                });
            }
            d.Options.Add(new KindOption { Text = "Closed Plate" });
            EnsureCurrent(d);
            return d;
        }

        private static RowDescriptor Choice(ResultFamily f, string kind, IEnumerable<string> options, bool canFlip)
        {
            var d = new RowDescriptor { Family = f, Kind = kind, CanFlip = canFlip };
            d.Options.AddRange(options.Select(o => new KindOption { Text = o }));
            EnsureCurrent(d);
            return d;
        }

        private static RowDescriptor ReadOnly(ResultFamily f, string kind) => new RowDescriptor { Family = f, Kind = kind };

        /// <summary>Kind hiện tại luôn có trong dropdown (vd Web 2 đầu GE + GB) — chèn lên đầu nếu thiếu.</summary>
        private static void EnsureCurrent(RowDescriptor d)
        {
            if (!d.Options.Any(o => o.Text == d.Kind))
                d.Options.Insert(0, new KindOption { Text = d.Kind });
        }

        /// <summary>"GE-HP" → "HP"; không có '-' → <paramref name="fallback"/>.</summary>
        private static string Suffix(string code, string fallback)
        {
            int i = (code ?? "").IndexOf('-');
            return i >= 0 && i < code.Length - 1 ? code.Substring(i + 1) : fallback;
        }
    }
}
