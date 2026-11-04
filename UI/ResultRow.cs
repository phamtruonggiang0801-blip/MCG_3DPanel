using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using MCG_3DPanel.Models;
using MCG_3DPanel.Services;
using MCG_3DPanel.Services.Corrections;

namespace MCG_3DPanel.UI
{
    /// <summary>
    /// 1 dòng của grid "Result" = 1 SOLID trong block 3D: Family · Kind (dropdown sửa được trong cùng họ) · Size ·
    /// Dir (⇄ cho HP/Bracket) · Flags. Danh mục họ/Kind: <see cref="ResultCatalog"/> (spec 2026-11-04).
    /// <see cref="Path"/>/<see cref="Bounds"/> giữ để highlight + zoom entity LỒNG trong block.
    /// </summary>
    public class ResultRow : INotifyPropertyChanged
    {
        /// <summary>Block 3D chứa solid (bỏ tiền tố "MCG3D_") — phân biệt khi Build nhiều panel 1 lúc.</summary>
        public string Panel { get; set; }
        public string Handle { get; set; }         // handle của solid trong block 3D
        public ResultFamily Family { get; set; }
        /// <summary>Kind tool nhận diện ra (đã áp correction cũ nếu có) — chỉ hiển thị.</summary>
        public string DetectedKind { get; set; }
        public List<KindOption> Options { get; set; } = new List<KindOption>();
        /// <summary>Có part 2D + có lựa chọn ⇒ hiện dropdown; còn lại (solid sinh thêm, HP, FB…) chỉ đọc.</summary>
        public bool KindEditable => !string.IsNullOrEmpty(SourceKey) && Options.Count > 1;
        public string Size { get; set; }
        public bool CanFlip { get; set; }
        public string Flags { get; set; }

        private string _kind;
        /// <summary>Kind đang chọn. Đổi giá trị ⇒ <see cref="KindChanged"/> (view lưu correction + rebuild).</summary>
        public string Kind
        {
            get => _kind;
            set
            {
                // ComboBox ẩn / chưa khớp item có thể đẩy null về — bỏ qua, không coi là người dùng sửa.
                if (_kind == value || (!_loading && string.IsNullOrEmpty(value))) return;
                _kind = value;
                Raise(nameof(Kind));
                if (!_loading) KindChanged?.Invoke(this);
            }
        }

        private bool _flipped;
        public bool Flipped
        {
            get => _flipped;
            set { _flipped = value; Raise(nameof(Flipped)); Raise(nameof(DirText)); }
        }
        public string DirText => CanFlip ? (Flipped ? "flipped" : "") : "";

        // ─── Sniped End từng đầu (HP/FB) — nút bật/tắt trên grid ───
        public bool CanSnip { get; set; }
        public string SnipLoLabel { get; set; }
        public string SnipHiLabel { get; set; }
        /// <summary>Đầu lo/hi THỰC SỰ đã vát ở lần build này.</summary>
        public bool SnipLoOn { get; set; }
        public bool SnipHiOn { get; set; }
        internal Autodesk.AutoCAD.Geometry.Point3d EndLo { get; set; }
        internal Autodesk.AutoCAD.Geometry.Point3d EndHi { get; set; }

        /// <summary>Có correction cho part này ⇒ grid tô chữ xanh (ResultRowStyle).</summary>
        public bool IsCorrected { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        internal event Action<ResultRow> KindChanged;
        private bool _loading = true;
        internal void EndLoad() => _loading = false;
        private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        // Không hiện lên grid — highlight/zoom, correction, report JSON.
        internal ObjectId[] Path { get; set; }
        internal Extents3d Bounds { get; set; }
        internal BuiltSolid Solid { get; set; }
        internal BuildResult Build { get; set; }
        internal string PanelKey { get; set; }
        internal string SourceKey { get; set; }

        /// <summary>Dựng danh sách dòng từ các SOLID của block 3D vừa build. Path = [BlockRef 3D, solid]
        /// để highlight đúng solid lồng trong block. Report JSON gọi với <paramref name="includeErased"/>
        /// = true để vẫn ghi được khi user chọn No (block 3D đã xoá) — CÙNG handle/thứ tự với CSV.</summary>
        public static List<ResultRow> From(BuildResult build, bool includeErased = false)
        {
            if (build == null || build.BlockRefId.IsNull) return new List<ResultRow>();
            string panelKey = CorrectionStore.PanelKey(build.SourceRefId);
            return build.Solids
                .Where(s => !s.SolidId.IsNull && (includeErased || !s.SolidId.IsErased))
                .Select(s => Make(build, s, panelKey))
                .OrderBy(r => r.Family).ThenBy(r => r.Kind).ThenBy(r => r.Size).ThenBy(r => r.Handle)
                .ToList();
        }

        private static ResultRow Make(BuildResult build, BuiltSolid s, string panelKey)
        {
            PanelPart p = s.Part;
            var d = ResultCatalog.Describe(p?.Type.ToString(), p != null ? p.Code : s.Code, p == null ? s.Type : null,
                s.MergedCodes, p?.EndTouchKinds, p?.GeAvailable ?? false, p?.GbAvailable ?? false);
            var corr = CorrectionStore.Get(panelKey, p?.SourceKey);
            var row = new ResultRow
            {
                Panel = PanelName(build),
                Handle = s.SolidId.Handle.ToString(),
                Family = d.Family,
                DetectedKind = corr?.OriginalKind ?? d.Kind,
                Options = d.Options,
                CanFlip = d.CanFlip && p != null,
                Size = SizeOf(build.Input, p, d.Family),
                Flags = SnipFlags(p),
                IsCorrected = corr != null,
                Path = new[] { build.BlockRefId, s.SolidId },
                Bounds = s.Bounds,
                Solid = s,
                Build = build,
                PanelKey = panelKey,
                SourceKey = p?.SourceKey,
            };
            row.Kind = d.Kind;
            row.Flipped = corr?.FlipDir == true;
            if (p != null && (p.Type == PartType.HP || p.Type == PartType.FlatBar))
            {
                var (a, b) = Services.Classify.ClassifyUtil.Endpoints(p);
                var (lo, hi) = ResultCatalog.EndLabels(b.X - a.X, b.Y - a.Y);
                bool horiz = lo == "L";
                bool aIsLo = horiz ? a.X <= b.X : a.Y <= b.Y;
                row.CanSnip = true;
                row.SnipLoLabel = horiz ? "◀ L" : "▼ B";
                row.SnipHiLabel = horiz ? "R ▶" : "T ▲";
                row.SnipLoOn = p.SnipLo;
                row.SnipHiOn = p.SnipHi;
                row.EndLo = aIsLo ? a : b;
                row.EndHi = aIsLo ? b : a;
            }
            row.EndLoad();
            return row;
        }

        /// <summary>"MCG3D_P101_1" → "P101_1" (tên block 3D là duy nhất, kể cả 2 instance cùng block 2D).</summary>
        private static string PanelName(BuildResult build)
        {
            string n = build.BlockName ?? build.PanelBlockName ?? "";
            return n.StartsWith("MCG3D_") ? n.Substring(6) : n;
        }

        /// <summary>Size theo họ: tấm "t{dày}", flange "{rộng}*{dày}", HP/FB theo quy cách nhập; solid sinh thêm rỗng.</summary>
        private static string SizeOf(PanelInput input, PanelPart p, ResultFamily f)
        {
            if (p == null) return "";
            switch (p.Type)
            {
                case PartType.TopPlate: return input != null ? "t" + Fmt(input.TopPlateThickness) : "";
                case PartType.HP: return input?.HpSpec ?? "";
                case PartType.FlatBar: return input?.FlatBarSpec ?? "";
                case PartType.Flange:
                case PartType.FacePlate:
                    return Fmt(p.FootprintThickness) + (input != null ? "*" + Fmt(input.FlangeThickness) : "");
                default: return "t" + Fmt(p.FootprintThickness);
            }
        }

        private static string Fmt(double v) => Math.Round(v).ToString("0");

        /// <summary>"sniped L" / "sniped L+R" / "sniped B+T" … cho Export/Copy; rỗng nếu không vát đầu nào.</summary>
        private static string SnipFlags(PanelPart p)
        {
            if (p == null || !(p.SnipLo || p.SnipHi)) return "";
            var (a, b) = Services.Classify.ClassifyUtil.Endpoints(p);
            var (lo, hi) = ResultCatalog.EndLabels(b.X - a.X, b.Y - a.Y);
            return "sniped " + (p.SnipLo && p.SnipHi ? lo + "+" + hi : p.SnipLo ? lo : hi);
        }
    }
}
