using System.Linq;
using MCG_3DPanel.Naming;

namespace MCG_3DPanel.UI
{
    /// <summary>1 dòng tab Naming = 1 tuyến (Web / HP) hoặc 1 tuyến BS.</summary>
    public sealed class NamingRow : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        public NamingRow(NamingPanel panel, NamingLine line)
        {
            PanelData = panel;
            Line = line;
        }

        /// <summary>Tên đã đổi (Resolve lại tại chỗ) ⇒ báo grid, KHÔNG nạp lại bảng (giữ ô/vùng chọn người dùng vừa bấm).</summary>
        public void Refresh()
        {
            foreach (var n in new[] { nameof(Name), nameof(Label), nameof(IsUserName), nameof(NameTip), nameof(Pending) })
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(n));
        }

        public NamingPanel PanelData { get; }
        public NamingLine Line { get; }

        public string Panel => PanelData.PanelName;
        /// <summary>Phần tên (tag), vd "T1".</summary>
        public string Name => Line.Name;
        /// <summary>Tên đầy đủ hiển thị: "T1 WEB 374x6.0/FLANGE 100x20.0".</summary>
        public string Label => Line.Label;
        public string Spec => Line.Spec;
        public string AutoName => Line.AutoName;
        public bool IsUserName => Line.IsUserName;
        public string Kind => Line.Kind.ToString();
        /// <summary>Cột Kind: "Web" / "Web (merged)".</summary>
        public string KindText => Line.IsMerged ? $"{Kind} (merged)" : Kind;

        /// <summary>Tổng chiều dài các đoạn của tuyến (mm, hệ block 2D) — cột Length để sort (kỹ sư 2026-11-20).</summary>
        public double Length => Line.Pieces.Sum(p => System.Math.Sqrt((p.X1 - p.X0) * (p.X1 - p.X0) + (p.Y1 - p.Y0) * (p.Y1 - p.Y0)));

        /// <summary>Có sửa trên bản 2D đang chờ Update (tên / Del / Merge / Split của đoạn thuộc tuyến).</summary>
        public bool Pending
        {
            get
            {
                var keys = PanelData.Build?.Remote?.PendingKeys;
                return keys != null && keys.Count > 0 && Line.Pieces.Any(p => p.Key != null && keys.Contains(p.Key));
            }
        }

        public string NameTip => IsUserName
            ? $"Your name (auto: {AutoName}). Type a new one and press Enter; empty = back to {AutoName}. Applies to all selected rows."
            : "Auto name. Type a new one and press Enter (or leave the cell). Applies to all selected rows.";

        /// <summary>Khoá chọn lại sau khi nạp lại: (panel, khoá đoạn đầu tiên).</summary>
        public (string panel, string piece) Key => (PanelData.PanelKey, Line.Pieces.FirstOrDefault()?.Key);
    }
}
