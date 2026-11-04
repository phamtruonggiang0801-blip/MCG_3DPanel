using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using MCG_3DPanel.Models;
using MCG_3DPanel.Services;

namespace MCG_3DPanel.UI
{
    /// <summary>
    /// 1 dòng của grid "Result": Handle đối tượng · loại tool nhận diện · loại người dùng sửa lại.
    /// <see cref="Path"/>/<see cref="Bounds"/> giữ để highlight + zoom entity LỒNG trong block.
    /// </summary>
    public class ResultRow : INotifyPropertyChanged
    {
        /// <summary>Block 3D chứa solid (bỏ tiền tố "MCG3D_") — phân biệt khi Build nhiều panel 1 lúc.</summary>
        public string Panel { get; set; }
        public string Handle { get; set; }         // handle của solid trong block 3D
        /// <summary>Chủng loại chi tiết đầu web / bracket: GB · GE · OB · IB · BF · B (rỗng với tấm/thanh thường).</summary>
        public string Kind { get; set; }
        public string DetectedType { get; set; }    // loại tool nhận diện: web6 / OB12 / GE-HP / topplate …

        private string _correctedType;
        /// <summary>Người dùng điền lại (để trống = đồng ý). Báo thay đổi để dòng đổi màu ngay khi gõ.</summary>
        public string CorrectedType
        {
            get => _correctedType;
            set
            {
                if (_correctedType == value) return;
                _correctedType = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CorrectedType)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCorrected)));
            }
        }

        /// <summary>Có sửa loại → grid tô chữ xanh (ResultRowStyle).</summary>
        public bool IsCorrected => !string.IsNullOrWhiteSpace(_correctedType);

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>Từng chi tiết GB/GE/OB/IB/BF/B trong solid này (Web nuốt 2 GE → 2 phần tử) — để đếm.</summary>
        internal List<string> KindItems { get; set; } = new List<string>();

        // Không hiện lên grid — phục vụ highlight/zoom.
        internal ObjectId[] Path { get; set; }
        internal Extents3d Bounds { get; set; }
        /// <summary>Solid nguồn — report JSON đọc thêm thể tích / handle 2D gốc / code đã hợp nhất.</summary>
        internal BuiltSolid Solid { get; set; }

        /// <summary>Dựng danh sách dòng từ các SOLID của block 3D vừa build. Path = [BlockRef 3D, solid]
        /// để highlight đúng solid lồng trong block. Report JSON gọi với <paramref name="includeErased"/>
        /// = true để vẫn ghi được khi user chọn No (block 3D đã xoá) — CÙNG handle/thứ tự với CSV.</summary>
        public static List<ResultRow> From(BuildResult build, bool includeErased = false)
        {
            if (build == null || build.BlockRefId.IsNull) return new List<ResultRow>();
            return build.Solids
                .Where(s => !s.SolidId.IsNull && (includeErased || !s.SolidId.IsErased))
                .Select(s => new { s, label = Label(s), kind = KindOf(s), rank = s.Part != null ? Rank(s.Part.Type) : s.Type == "GirderFlange" ? Rank(PartType.Flange) : 7 })
                .OrderBy(x => x.rank).ThenBy(x => x.kind).ThenBy(x => x.label)
                .Select(x => new ResultRow
                {
                    Panel = PanelName(build),
                    Handle = x.s.SolidId.Handle.ToString(),
                    Kind = x.kind,
                    KindItems = x.s.MergedCodes.Count > 0
                        ? x.s.MergedCodes.Select(Prefix).ToList()
                        : (x.kind == "" ? new List<string>() : new List<string> { x.kind }),
                    DetectedType = x.label,
                    CorrectedType = "",
                    Path = new[] { build.BlockRefId, x.s.SolidId },
                    Bounds = x.s.Bounds,
                    Solid = x.s,
                })
                .ToList();
        }

        /// <summary>"MCG3D_P101_1" → "P101_1" (tên block 3D là duy nhất, kể cả 2 instance cùng block 2D).</summary>
        private static string PanelName(BuildResult build)
        {
            string n = build.BlockName ?? build.PanelBlockName ?? "";
            return n.StartsWith("MCG3D_") ? n.Substring(6) : n;
        }

        /// <summary>Nhãn của 1 solid: theo part nguồn, hoặc theo loại solid sinh thêm.</summary>
        private static string Label(BuiltSolid s)
        {
            // Web đã nuốt GE/GB (1 tấm liền) → ghi kèm code để không mất dấu GE/GB.
            if (s.Part != null && s.MergedCodes.Count > 0)
                return Label(s.Part) + " + " + string.Join(" + ", s.MergedCodes);
            if (s.Part != null) return Label(s.Part);
            switch (s.Type)
            {
                case "CollarPlate": return "collar plate";
                case "HpEndBracketB": return "bracket B";
                case "GirderFlange": return "flange " + (string.IsNullOrEmpty(s.Code) ? "GE/GB" : s.Code);   // flange 50×10 / 80×10 của GE/GB
                default: return s.Type ?? "unknown";
            }
        }

        /// <summary>
        /// Chủng loại rút từ code: Bracket "OB12"→OB, "IB…"→IB, "BF…"→BF; GirderEnd "GE-HP"→GE, "GB-FB"→GB;
        /// Web nuốt GE/GB → các tiền tố đã hợp nhất ("GE", "GE+GB"); Web "GB?" (thiếu HP định vị toe) → "GB?";
        /// Bracket đầu HP → "B". Còn lại rỗng.
        /// </summary>
        private static string KindOf(BuiltSolid s)
        {
            if (s.Part == null) return s.Type == "HpEndBracketB" ? "B" : "";
            string code = s.Part.Code ?? "";
            switch (s.Part.Type)
            {
                case PartType.Bracket:
                    foreach (var k in new[] { "OB", "IB", "BF" })
                        if (code.StartsWith(k)) return k;
                    return "";
                case PartType.GirderEnd:
                    return Prefix(code);
                case PartType.Web:
                    if (s.MergedCodes.Count > 0)
                        return string.Join("+", s.MergedCodes.Select(Prefix).Distinct());
                    return code == "GB?" ? "GB?" : "";
                default:
                    return "";
            }
        }

        /// <summary>"GE-HP" → "GE".</summary>
        private static string Prefix(string code)
        {
            if (string.IsNullOrEmpty(code)) return "";
            int i = code.IndexOf('-');
            return i > 0 ? code.Substring(0, i) : code;
        }

        /// <summary>Nhãn loại hiển thị: Web/Bracket/GE dùng dày hoặc mã, còn lại là tên loại.</summary>
        public static string Label(PanelPart p)
        {
            switch (p.Type)
            {
                case PartType.TopPlate: return "topplate";
                case PartType.Web: return "web" + Round(p.FootprintThickness);
                case PartType.Flange: return "flange";
                case PartType.FacePlate: return "faceplate";
                case PartType.FlatBar: return "flatbar";
                case PartType.HP: return "hp";
                case PartType.Bracket: return string.IsNullOrEmpty(p.Code) ? "bracket" : p.Code;
                case PartType.GirderEnd: return string.IsNullOrEmpty(p.Code) ? "GE" : p.Code;
                case PartType.CutOut: return "cutout";
                case PartType.ClosedPlate: return "closed plate";
                case PartType.BackingPlate: return "backing plate";
                default: return "unknown";
            }
        }

        private static int Round(double v) => (int)Math.Round(v);

        // Thứ tự nhóm cho dễ đọc: kết cấu chính → chi tiết → bracket/GE.
        private static int Rank(PartType t)
        {
            switch (t)
            {
                case PartType.TopPlate: return 0;
                case PartType.Web: return 1;
                case PartType.Flange: return 2;
                case PartType.FacePlate: return 3;
                case PartType.FlatBar: return 4;
                case PartType.HP: return 5;
                case PartType.GirderEnd: return 6;
                case PartType.Bracket: return 7;
                case PartType.CutOut: return 8;
                case PartType.ClosedPlate: return 8;
                case PartType.BackingPlate: return 8;
                default: return 9;
            }
        }
    }
}
