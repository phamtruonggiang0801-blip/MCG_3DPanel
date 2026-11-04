using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using MCG_3DPanel.Commands;

namespace MCG_3DPanel.UI
{
    /// <summary>
    /// Tab "Result" của PaletteSet: các SOLID của block 3D vừa build + highlight/zoom + sửa loại + Copy/Export CSV.
    /// Dữ liệu đẩy vào qua <see cref="Load"/> (PaletteManager gọi khi tab Input Build xong).
    /// </summary>
    public partial class PanelResultView : UserControl
    {
        public PanelResultView()
        {
            InitializeComponent();
        }

        /// <summary>Danh sách build đang hiển thị — rebuild thay đúng 1 phần tử.</summary>
        private List<MCG_3DPanel.Services.BuildResult> _builds = new List<MCG_3DPanel.Services.BuildResult>();

        /// <summary>PanelCommands.Rebuilt: thay bản cũ bằng bản mới (null = thất bại, giữ nguyên) rồi nạp lại.</summary>
        public void OnRebuilt(MCG_3DPanel.Services.BuildResult old, MCG_3DPanel.Services.BuildResult neu, string message)
        {
            if (neu != null)
            {
                int i = _builds.IndexOf(old);
                if (i >= 0) _builds[i] = neu; else _builds.Add(neu);
                Load(_builds);
            }
            SetStatus(message, error: neu == null);
        }

        /// <summary>Nạp solid của MỌI panel vừa build (giữ nguyên thứ tự chọn panel).</summary>
        public void Load(List<MCG_3DPanel.Services.BuildResult> builds)
        {
            _builds = builds ?? new List<MCG_3DPanel.Services.BuildResult>();
            var kept = _builds.Where(b => b.Kept).ToList();
            // Cột Panel chỉ cần khi có từ 2 panel trở lên — 1 panel thì thừa, lấn chỗ của Type.
            ColPanel.Visibility = kept.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

            // Bỏ (No) thì block 3D đã bị xoá — không còn solid nào để zoom.
            if (kept.Count == 0)
            {
                GridResult.ItemsSource = null;
                SetStatus("3D model discarded — nothing to list.");
                return;
            }

            var rows = kept.SelectMany(b => ResultRow.From(b)).ToList();
            GridResult.ItemsSource = rows;
            string report = builds.Select(b => b.ReportPath).FirstOrDefault(p => p != null);
            string where = kept.Count > 1 ? $"{kept.Count} panels" : $"block {kept[0].BlockName}";
            SetStatus(rows.Count == 0
                ? "No solids in the 3D block."
                : $"{rows.Count} solids  ·  {Summary(rows)}  ·  {where}"
                  + (report != null ? $"  ·  debug JSON: {System.IO.Path.GetFileName(report)}" : ""));
        }

        // Thứ tự cố định khi liệt kê chủng loại trên dòng trạng thái.
        private static readonly string[] KindOrder = { "GB", "GE", "OB", "IB", "BF", "B", "GB?" };

        /// <summary>Đếm chủng loại, vd "GB×2, GE×4, OB×6, IB×3, BF×5" (đếm cả GE/GB đã hợp nhất vào Web).</summary>
        private static string Summary(List<ResultRow> rows)
        {
            var counts = rows.SelectMany(r => r.KindItems).GroupBy(k => k).ToDictionary(g => g.Key, g => g.Count());
            var parts = KindOrder.Where(counts.ContainsKey).Select(k => $"{k}×{counts[k]}")
                .Concat(counts.Keys.Except(KindOrder).OrderBy(k => k).Select(k => $"{k}×{counts[k]}"));
            string s = string.Join(", ", parts);
            return s.Length == 0 ? "no GB/GE/OB/IB/BF" : s;
        }

        private List<ResultRow> Rows => (GridResult.ItemsSource as IEnumerable<ResultRow>)?.ToList() ?? new List<ResultRow>();

        // ─── Highlight / zoom ─────────────────────────────────────────────

        private void GridResult_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GridResult.SelectedItem is ResultRow row)
                EntityHighlighter.Show(row.Path, row.Bounds);
        }

        private void BtnZoomRow_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ResultRow row)
            {
                GridResult.SelectedItem = row;               // đồng bộ dòng chọn với object đang sáng
                EntityHighlighter.Show(row.Path, row.Bounds);
            }
        }

        private void MnuZoomTo_Click(object sender, RoutedEventArgs e)
        {
            if (GridResult.SelectedItem is ResultRow row)
                EntityHighlighter.Show(row.Path, row.Bounds);
        }

        private void MnuClearCorrection_Click(object sender, RoutedEventArgs e)
        {
            if (GridResult.SelectedItem is ResultRow row)
                row.CorrectedType = "";
        }

        // ─── Copy / Export ────────────────────────────────────────────────

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            var rows = Rows;
            if (rows.Count == 0) { SetStatus("Nothing to copy — classify a panel first.", error: true); return; }

            var sb = new StringBuilder();
            sb.AppendLine("Panel\tHandle\tKind\tDetectedType\tCorrectedType");
            foreach (var r in rows)
                sb.AppendLine($"{r.Panel}\t{r.Handle}\t{r.Kind}\t{r.DetectedType}\t{r.CorrectedType}");
            try
            {
                Clipboard.SetText(sb.ToString());
                SetStatus($"Copied {rows.Count} rows to the clipboard.");
            }
            catch (System.Exception ex)
            {
                SetStatus("Copy failed: " + ex.Message, error: true);
            }
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            var rows = Rows;
            if (rows.Count == 0)
            {
                SetStatus("Nothing to export — classify a panel first.", error: true);
                return;
            }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export result grid",
                Filter = "CSV file (*.csv)|*.csv",
                FileName = "MCG_PanelResult_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv",
                InitialDirectory = System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory),
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Panel,Handle,Kind,DetectedType,CorrectedType");
                foreach (var r in rows)
                    sb.AppendLine($"{Csv(r.Panel)},{Csv(r.Handle)},{Csv(r.Kind)},{Csv(r.DetectedType)},{Csv(r.CorrectedType)}");
                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                SetStatus("Exported: " + Path.GetFileName(dlg.FileName));
                ReportOutput.OpenFile(dlg.FileName);
            }
            catch (System.Exception ex)
            {
                SetStatus("Export failed: " + ex.Message, error: true);
            }
        }

        /// <summary>Escape 1 ô CSV (bọc "" nếu chứa , " hoặc xuống dòng).</summary>
        private static string Csv(string s)
        {
            s = s ?? "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        private void SetStatus(string message, bool error = false) => StatusText.Set(TxtStatus, message, error);
    }
}
