using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using MCG_3DPanel.Commands;
using MCG_3DPanel.Services;
using MCG_3DPanel.Services.Corrections;

namespace MCG_3DPanel.UI
{
    /// <summary>
    /// Tab "Result" của PaletteSet: các SOLID của block 3D vừa build theo họ kết cấu + highlight/zoom.
    /// Sửa Kind (dropdown) hoặc ⇄ ⇒ lưu correction (<see cref="CorrectionStore"/>) rồi TỰ rebuild đúng panel đó
    /// (<see cref="PanelCommands.RequestRebuild"/>), giữ dòng đang chọn. Dữ liệu đẩy vào qua <see cref="Load"/>.
    /// </summary>
    public partial class PanelResultView : UserControl
    {
        public PanelResultView()
        {
            InitializeComponent();
        }

        /// <summary>Danh sách build đang hiển thị — rebuild thay đúng 1 phần tử.</summary>
        private List<BuildResult> _builds = new List<BuildResult>();

        /// <summary>Dòng cần chọn lại sau rebuild (PanelKey, SourceKey).</summary>
        private (string panel, string source) _reselect;

        /// <summary>PanelCommands.Rebuilt: thay bản cũ bằng bản mới (null = thất bại, giữ nguyên) rồi nạp lại.</summary>
        public void OnRebuilt(BuildResult old, BuildResult neu, string message)
        {
            var sel = _reselect;
            _reselect = default;
            if (neu != null)
            {
                int i = _builds.IndexOf(old);
                if (i >= 0) _builds[i] = neu; else _builds.Add(neu);
            }
            // Nạp lại cả khi thất bại: correction vẫn còn trong kho → dòng hiện đúng trạng thái đã sửa.
            Load(_builds, sel.panel, sel.source);
            SetStatus(message, error: neu == null);
        }

        /// <summary>Bản vẽ sắp đóng: bỏ build của nó khỏi bảng (ObjectId sắp không còn hợp lệ).</summary>
        public void OnDocumentClosing(Autodesk.AutoCAD.DatabaseServices.Database db)
        {
            if (db == null || _builds.Count == 0) return;
            int before = _builds.Count;
            var left = _builds.Where(b => b.BlockRefId.IsNull || b.BlockRefId.Database != db).ToList();
            if (left.Count == before) return;
            if (left.Count == 0)
            {
                _builds = left;
                GridResult.ItemsSource = null;
                SetStatus("Drawing closed — Build 3D again to list solids.");
                return;
            }
            Load(left);
        }

        /// <summary>Nạp solid của MỌI panel vừa build (giữ nguyên thứ tự chọn panel). Có
        /// <paramref name="keepPanelKey"/>/<paramref name="keepSourceKey"/> ⇒ chọn lại + cuộn tới + highlight dòng đó.</summary>
        public void Load(List<BuildResult> builds, string keepPanelKey = null, string keepSourceKey = null)
        {
            _builds = builds ?? new List<BuildResult>();
            var kept = _builds.Where(b => b.Kept).ToList();
            // Cột Panel chỉ cần khi có từ 2 panel trở lên — 1 panel thì thừa, lấn chỗ của Kind.
            ColPanel.Visibility = kept.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

            // Bỏ (No) thì block 3D đã bị xoá — không còn solid nào để zoom.
            if (kept.Count == 0)
            {
                GridResult.ItemsSource = null;
                SetStatus("3D model discarded — nothing to list.");
                return;
            }

            var rows = kept.SelectMany(b => ResultRow.From(b)).ToList();
            foreach (var r in rows) r.KindChanged += OnKindChanged;
            GridResult.ItemsSource = rows;

            string report = _builds.Select(b => b.ReportPath).FirstOrDefault(p => p != null);
            string where = kept.Count > 1 ? $"{kept.Count} panels" : $"block {kept[0].BlockName}";
            SetStatus(rows.Count == 0
                ? "No solids in the 3D block."
                : $"{rows.Count} solids  ·  {Summary(rows)}  ·  {where}"
                  + (report != null ? $"  ·  debug JSON: {System.IO.Path.GetFileName(report)}" : ""));

            if (keepSourceKey != null)
            {
                var row = rows.FirstOrDefault(r => r.PanelKey == keepPanelKey && (r.SourceKey ?? r.DeleteKey) == keepSourceKey);
                if (row != null)
                {
                    GridResult.SelectedItem = row;      // SelectionChanged → highlight
                    GridResult.ScrollIntoView(row);
                }
            }
        }

        /// <summary>Đếm theo Kind, vd "Web×6, OB×12, IB×3, HP×20" (theo thứ tự họ).</summary>
        private static string Summary(List<ResultRow> rows)
        {
            var parts = rows.GroupBy(r => (r.Family, r.Kind))
                .OrderBy(g => g.Key.Family).ThenBy(g => g.Key.Kind)
                .Select(g => $"{g.Key.Kind}×{g.Count()}");
            return string.Join(", ", parts);
        }

        private List<ResultRow> Rows => (GridResult.ItemsSource as IEnumerable<ResultRow>)?.ToList() ?? new List<ResultRow>();

        // ─── Correction → rebuild ─────────────────────────────────────────

        private void OnKindChanged(ResultRow row)
        {
            if (!row.KindEditable || string.IsNullOrEmpty(row.Kind)) return;
            var c = CorrectionStore.Get(row.PanelKey, row.SourceKey) ?? new PartCorrection();
            if (c.OriginalKind == null) c.OriginalKind = row.DetectedKind;
            c.Kind = row.Kind == c.OriginalKind ? null : row.Kind;   // chọn lại Kind gốc ⇒ huỷ correction Kind
            CorrectionStore.Set(row.PanelKey, row.SourceKey, c);
            Rebuild(row);
        }

        private void BtnFlip_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is ResultRow row) || !row.CanFlip) return;
            var c = CorrectionStore.Get(row.PanelKey, row.SourceKey) ?? new PartCorrection { OriginalKind = row.DetectedKind };
            c.FlipDir = !c.FlipDir;
            CorrectionStore.Set(row.PanelKey, row.SourceKey, c);
            Rebuild(row);
        }

        // ─── Sniped End từng đầu ──────────────────────────────────────────

        private void BtnSnip_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement fe) || !(fe.DataContext is ResultRow row) || !row.CanSnip) return;
            bool lo = (fe.Tag as string) == "lo";
            var c = CorrectionStore.Get(row.PanelKey, row.SourceKey) ?? new PartCorrection { OriginalKind = row.DetectedKind };
            // Ép NGƯỢC trạng thái thực tế; giữ override tới khi Clear Correction (plan Part 2, Task 8).
            if (lo) c.SnipLo = !row.SnipLoOn; else c.SnipHi = !row.SnipHiOn;
            CorrectionStore.Set(row.PanelKey, row.SourceKey, c);
            Rebuild(row);
        }

        private void BtnSnip_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is ResultRow row && row.CanSnip)
                EntityHighlighter.ShowPoint((fe.Tag as string) == "lo" ? row.EndLo : row.EndHi);
        }

        private void BtnSnip_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => EntityHighlighter.ClearPoint();

        // ─── Delete / Restore solid ───────────────────────────────────────

        private void GridMenu_Opened(object sender, RoutedEventArgs e)
        {
            var row = GridResult.SelectedItem as ResultRow;
            bool can = row != null && !string.IsNullOrEmpty(row.DeleteKey);
            MnuDelete.Visibility = can && !row.Deleted ? Visibility.Visible : Visibility.Collapsed;
            MnuRestore.Visibility = can && row.Deleted ? Visibility.Visible : Visibility.Collapsed;
        }

        private void MnuDelete_Click(object sender, RoutedEventArgs e) => SetDeleted(true);
        private void MnuRestore_Click(object sender, RoutedEventArgs e) => SetDeleted(false);

        private void SetDeleted(bool deleted)
        {
            if (!(GridResult.SelectedItem is ResultRow row) || string.IsNullOrEmpty(row.DeleteKey)) return;
            CorrectionStore.SetDeleted(row.PanelKey, row.DeleteKey, deleted);
            EntityHighlighter.Clear();
            Rebuild(row);
        }

        private void MnuClearCorrection_Click(object sender, RoutedEventArgs e)
        {
            if (!(GridResult.SelectedItem is ResultRow row)) return;
            if (CorrectionStore.Get(row.PanelKey, row.SourceKey) == null) { SetStatus("This row has no correction."); return; }
            CorrectionStore.Clear(row.PanelKey, row.SourceKey);
            Rebuild(row);
        }

        private void Rebuild(ResultRow row)
        {
            _reselect = (row.PanelKey, row.SourceKey ?? row.DeleteKey);
            SetStatus($"Rebuilding {row.Panel}…");
            PanelCommands.RequestRebuild(row.Build);
        }

        // ─── Highlight / zoom ─────────────────────────────────────────────

        private void Show(ResultRow row)
        {
            if (row.Deleted) { SetStatus("Deleted — right-click Restore to bring it back."); return; }
            // Sau Ctrl+Z (undo rebuild) solid của bảng có thể đã bị xoá — không highlight, chỉ báo.
            if (row.Solid == null || row.Solid.SolidId.IsNull || row.Solid.SolidId.IsErased)
            {
                SetStatus("Solid no longer exists — rebuild or re-run Build 3D.", error: true);
                return;
            }
            EntityHighlighter.Show(row.Path, row.Bounds);
        }

        private void GridResult_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GridResult.SelectedItem is ResultRow row) Show(row);
        }

        private void BtnZoomRow_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ResultRow row)
            {
                GridResult.SelectedItem = row;               // đồng bộ dòng chọn với object đang sáng
                Show(row);
            }
        }

        private void MnuZoomTo_Click(object sender, RoutedEventArgs e)
        {
            if (GridResult.SelectedItem is ResultRow row) Show(row);
        }

        // ─── Copy / Export ────────────────────────────────────────────────

        private const string Header = "Panel,Handle,Family,DetectedKind,Kind,Size,Flags,DirFlipped";

        private static string[] Cells(ResultRow r) => new[]
        {
            r.Panel, r.Handle, r.Family.ToString(), r.DetectedKind, r.Kind, r.Size, r.Flags, r.Flipped ? "yes" : "",
        };

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            var rows = Rows;
            if (rows.Count == 0) { SetStatus("Nothing to copy — classify a panel first.", error: true); return; }

            var sb = new StringBuilder();
            sb.AppendLine(Header.Replace(',', '\t'));
            foreach (var r in rows) sb.AppendLine(string.Join("\t", Cells(r)));
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
                sb.AppendLine(Header);
                foreach (var r in rows) sb.AppendLine(string.Join(",", Cells(r).Select(Csv)));
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
