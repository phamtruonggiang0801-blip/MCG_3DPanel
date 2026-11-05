using System.Linq;
using MCG_3DPanel.Naming;

namespace MCG_3DPanel.UI
{
    /// <summary>1 dòng tab Naming = 1 tuyến (Web / HP) hoặc 1 thanh FB.</summary>
    public sealed class NamingRow
    {
        public NamingRow(NamingPanel panel, NamingLine line)
        {
            PanelData = panel;
            Line = line;
        }

        public NamingPanel PanelData { get; }
        public NamingLine Line { get; }

        public string Panel => PanelData.PanelName;
        public string Name => Line.Name;
        public string AutoName => Line.AutoName;
        public bool IsUserName => Line.IsUserName;
        public string Kind => Line.Kind.ToString();
        public string Dir => Line.Dir.ToString();
        public int Pieces => Line.Pieces.Count;
        public string Length => Line.Length.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        /// <summary>Các chiều dày khác nhau trong tuyến, vd "6/20".</summary>
        public string Thk => string.Join("/", Line.Pieces.Select(p => System.Math.Round(p.Thickness, 1))
            .Distinct().OrderBy(t => t).Select(t => t.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)));

        public string NameTip => IsUserName
            ? $"Your name (auto name: {AutoName}). Type a new one and press Enter; empty = back to {AutoName}. Applies to all selected rows."
            : "Auto name. Type a new one and press Enter (or leave the cell). Applies to all selected rows.";

        /// <summary>Khoá chọn lại sau khi nạp lại: (panel, khoá đoạn đầu tiên).</summary>
        public (string panel, string piece) Key => (PanelData.PanelKey, Line.Pieces.FirstOrDefault()?.Key);
    }
}
