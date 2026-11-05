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
            StepExport.Finished += (msg, err) => SetStatus(msg, err);
            Loaded += (s, e) =>
            {
                // STEPOUT chỉ có khi chạy AutoCAD Mechanical — không có thì nút mờ + tooltip giải thích.
                bool ok = StepExport.IsAvailable();
                BtnStep.IsEnabled = ok;
                if (!ok) BtnStep.ToolTip = "Requires AutoCAD Mechanical (STEPOUT)";
            };
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
                // Load 3D… nạp lại panel đã có trên bảng ⇒ thay đúng dòng cũ theo tên block (không nhân đôi).
                if (i < 0) i = _builds.FindIndex(b => b.BlockName == neu.BlockName && b.BlockRefId.Database == neu.BlockRefId.Database);
                if (i >= 0) _builds[i] = neu; else _builds.Add(neu);
            }
            // Nạp lại cả khi thất bại: correction vẫn còn trong kho → dòng hiện đúng trạng thái đã sửa.
            Load(_builds, sel.panel, sel.source);
            // Rebuild xong vẫn nhắc part 2D không dựng được (không để lỗi im lặng — ux-guardrails).
            if (neu != null && _notBuilt != null) SetStatus(message + "  ·  " + _notBuilt, error: true);
            else SetStatus(message, error: neu == null);
        }

        /// <summary>Trạng thái bình thường gần nhất (để khôi phục khi quay lại đúng drawing).</summary>
        private string _summary;
        /// <summary>"⚠ N 2D part(s) not built (…) · M build warning(s)" hoặc null.</summary>
        private string _notBuilt;

        /// <summary>Part 2D không ra solid + cảnh báo builder (BuildContext.Warn) — trước đây CHỈ nằm trong JSON report,
        /// giờ báo thẳng trên thanh trạng thái (ux-guardrails).</summary>
        private static string NotBuiltText(List<BuildResult> kept)
        {
            // "Unknown" = nét không phải kết cấu (vd marker layer 0 ngoài boong) — không phải lỗi dựng (review M3).
            var problem = kept.Where(b => b.Warnings.Count > 0 || b.Parts.Any(p => !p.Built && p.Type != "Unknown")).ToList();
            var bad = problem.SelectMany(b => b.Parts.Where(p => !p.Built && p.Type != "Unknown")
                .Select(p => $"{p.Type} {p.Handle}")).ToList();
            int warn = problem.Sum(b => b.Warnings.Count);
            if (bad.Count == 0 && warn == 0) return null;
            // Rebuild ghi report riêng khi có lỗi; Load 3D (mở lại bản vẽ) thì có thể chưa có file nào — review I1.
            string report = problem.Select(b => b.ReportPath).FirstOrDefault(p => p != null);
            string where = report != null
                ? $" — see {System.IO.Path.GetFileName(report)}"
                : " — run Build 3D again to write a debug report";
            var msg = new List<string>();
            if (bad.Count > 0)
            {
                string list = string.Join(", ", bad.Take(3)) + (bad.Count > 3 ? $", +{bad.Count - 3} more" : "");
                msg.Add($"{bad.Count} 2D part{(bad.Count > 1 ? "s" : "")} not built ({list})");
            }
            if (warn > 0) msg.Add($"{warn} build warning{(warn > 1 ? "s" : "")}");
            return "⚠ " + string.Join(" · ", msg) + where;
        }

        /// <summary>Đổi drawing đang mở (DocumentBecameCurrent): tắt highlight/marker của drawing cũ; bảng đang hiện
        /// panel của drawing KHÁC ⇒ báo rõ thay vì để highlight/zoom/rebuild hỏng im lặng (lỗi 2026-11-05).</summary>
        public void OnDocumentActivated(Autodesk.AutoCAD.DatabaseServices.Database db)
        {
            EntityHighlighter.ClearPoint();
            EntityHighlighter.Clear();
            if (db == null || _builds.Count == 0) return;
            var kept = _builds.Where(b => b.Kept && !b.BlockRefId.IsNull).ToList();
            if (kept.Count == 0) return;
            if (kept.Any(b => b.BlockRefId.Database == db))
            {
                RestoreSummary();
                // Highlight đã tắt khi rời drawing; bấm lại đúng dòng đang chọn KHÔNG phát SelectionChanged ⇒ tự sáng lại
                // (review I4).
                if (GridResult.SelectedItem is ResultRow sel && sel.Build?.BlockRefId.Database == db) Show(sel, zoom: false);
                return;
            }
            string name = DrawingName(kept[0].BlockRefId.Database);
            SetStatus($"The rows below belong to drawing {name} — switch back to it to highlight, zoom or rebuild, or run Build 3D / Load 3D here.", error: true);
        }

        /// <summary>Thanh trạng thái về tóm tắt bình thường (kèm ⚠ nếu có part hỏng / cảnh báo).</summary>
        private void RestoreSummary()
        {
            if (_summary == null) return;
            SetStatus(_notBuilt != null ? _notBuilt + "  ·  " + _summary : _summary, error: _notBuilt != null);
        }

        private static string DrawingName(Autodesk.AutoCAD.DatabaseServices.Database db)
        {
            foreach (Autodesk.AutoCAD.ApplicationServices.Document d in Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager)
                if (d.Database == db) return System.IO.Path.GetFileName(d.Name);
            return "(closed)";
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
            foreach (var r in rows) { r.KindChanged += OnKindChanged; r.TripChanged += OnTripChanged; }
            GridResult.ItemsSource = rows;

            string report = _builds.Select(b => b.ReportPath).FirstOrDefault(p => p != null);
            string where = kept.Count > 1 ? $"{kept.Count} panels" : $"block {kept[0].BlockName}";
            _notBuilt = NotBuiltText(kept);
            _summary = rows.Count == 0
                ? "No solids in the 3D block."
                : $"{rows.Count} solids  ·  {Summary(rows)}  ·  {where}"
                  + (report != null ? $"  ·  debug JSON: {System.IO.Path.GetFileName(report)}" : "");
            if (_notBuilt != null) SetStatus(_notBuilt + "  ·  " + _summary, error: true);
            else SetStatus(_summary);

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

        /// <summary>Chỉ sửa correction khi panel thuộc drawing ĐANG MỞ (ux-guardrails luật 1). Không ⇒ KHÔNG lưu gì,
        /// trả control về trạng thái thật (dropdown/toggle đã đổi trên màn hình) và báo người dùng.</summary>
        private bool CanEdit(ResultRow row)
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var db = row?.Build?.SourceRefId.Database;
            if (doc != null && db != null && !row.Build.SourceRefId.IsNull && db == doc.Database) return true;
            string msg = $"This panel is in drawing {(db != null ? DrawingName(db) : "(unknown)")} — switch to it to change {row?.Panel}.";
            // Hoãn: đang ở giữa cập nhật binding của chính control đó.
            Dispatcher.BeginInvoke(new System.Action(() =>
            {
                Load(_builds, row?.PanelKey, row?.SourceKey ?? row?.DeleteKey);
                SetStatus(msg, error: true);
            }));
            return false;
        }

        private void OnKindChanged(ResultRow row)
        {
            if (!row.KindEditable || string.IsNullOrEmpty(row.Kind)) return;
            if (!CanEdit(row)) return;
            var c = CorrectionStore.Get(row.PanelKey, row.SourceKey) ?? new PartCorrection();
            if (c.OriginalKind == null) c.OriginalKind = row.DetectedKind;
            c.Kind = row.Kind == c.OriginalKind ? null : row.Kind;   // chọn lại Kind gốc ⇒ huỷ correction Kind
            CorrectionStore.Set(row.PanelKey, row.SourceKey, c);
            Rebuild(row);
        }

        private void BtnFlip_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is ResultRow row) || !row.CanFlip) return;
            if (!CanEdit(row)) return;
            var c = CorrectionStore.Get(row.PanelKey, row.SourceKey) ?? new PartCorrection { OriginalKind = row.DetectedKind };
            c.FlipDir = !c.FlipDir;
            CorrectionStore.Set(row.PanelKey, row.SourceKey, c);
            Rebuild(row);
        }

        // ─── Tripping bracket theo HP: dropdown Auto / On / Off ─────────────

        /// <summary>ComboBox ĐÓNG mà nhận phím mũi tên / Home / End / PageUp-Down ⇒ WPF đổi lựa chọn ngay ⇒ lưu + rebuild
        /// ngoài ý muốn (vd Esc rồi ↓ để xuống dòng). Chỉ đổi Tripping khi người dùng MỞ dropdown chọn — review I2.</summary>
        private void CmbTrip_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (!(sender is ComboBox cb) || cb.IsDropDownOpen) return;
            switch (e.Key)
            {
                case System.Windows.Input.Key.Up: case System.Windows.Input.Key.Down:
                case System.Windows.Input.Key.Home: case System.Windows.Input.Key.End:
                case System.Windows.Input.Key.PageUp: case System.Windows.Input.Key.PageDown:
                    e.Handled = true;
                    break;
            }
        }

        /// <summary>Lăn chuột trên ComboBox đóng: không đổi lựa chọn — để grid cuộn.</summary>
        private void CmbTrip_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (!(sender is ComboBox cb) || cb.IsDropDownOpen) return;
            e.Handled = true;
            // Bắn lại từ phần tử CHA để sự kiện nổi lên ScrollViewer của DataGrid (bắn trên DataGrid thì không tới).
            var parent = System.Windows.Media.VisualTreeHelper.GetParent(cb) as UIElement;
            parent?.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                { RoutedEvent = UIElement.MouseWheelEvent, Source = parent });
        }

        private void OnTripChanged(ResultRow row)
        {
            if (!row.CanTrip) return;
            if (!CanEdit(row)) return;
            var c = CorrectionStore.Get(row.PanelKey, row.SourceKey) ?? new PartCorrection { OriginalKind = row.DetectedKind };
            c.Tripping = row.TripChoice == "On" ? true : row.TripChoice == "Off" ? false : (bool?)null;
            CorrectionStore.Set(row.PanelKey, row.SourceKey, c);
            Rebuild(row);
        }

        // ─── Sniped End từng đầu ──────────────────────────────────────────

        private void BtnSnip_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement fe) || !(fe.DataContext is ResultRow row) || !row.CanSnip) return;
            if (!CanEdit(row)) return;
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
            if (!CanEdit(row)) return;
            CorrectionStore.SetDeleted(row.PanelKey, row.DeleteKey, deleted);
            EntityHighlighter.Clear();
            Rebuild(row);
        }

        private void MnuClearCorrection_Click(object sender, RoutedEventArgs e)
        {
            if (!(GridResult.SelectedItem is ResultRow row)) return;
            if (!CanEdit(row)) return;
            if (CorrectionStore.Get(row.PanelKey, row.SourceKey) == null) { SetStatus("This row has no correction."); return; }
            CorrectionStore.Clear(row.PanelKey, row.SourceKey);
            Rebuild(row);
        }

        private void Rebuild(ResultRow row)
        {
            EntityHighlighter.ClearPoint();   // nút dưới chuột bị dựng lại ⇒ MouseLeave có thể không tới
            _reselect = (row.PanelKey, row.SourceKey ?? row.DeleteKey);
            SetStatus($"Rebuilding {row.Panel}…");
            PanelCommands.RequestRebuild(row.Build);
        }

        // ─── Highlight / zoom ─────────────────────────────────────────────

        private void Show(ResultRow row, bool zoom)
        {
            if (row.Deleted) { SetStatus("Deleted — right-click Restore to bring it back."); return; }
            // Sau Ctrl+Z (undo rebuild) solid của bảng có thể đã bị xoá — không highlight, chỉ báo.
            if (row.Solid == null || row.Solid.SolidId.IsNull || row.Solid.SolidId.IsErased)
            {
                SetStatus("Solid no longer exists — rebuild or re-run Build 3D.", error: true);
                return;
            }
            string err = EntityHighlighter.Show(row.Path, row.Bounds, zoom);
            if (err != null) SetStatus(err, error: true);
            else if (_statusIsError && _summary != null) RestoreSummary();   // lỗi cũ (vd "Solid no longer exists") không treo mãi — review M1
        }

        private void GridResult_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GridResult.SelectedItem is ResultRow row) Show(row, zoom: false);   // chỉ highlight — zoom bằng nút ⌖
        }

        private void BtnZoomRow_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ResultRow row)
            {
                GridResult.SelectedItem = row;               // đồng bộ dòng chọn với object đang sáng
                Show(row, zoom: true);
            }
        }

        private void MnuZoomTo_Click(object sender, RoutedEventArgs e)
        {
            if (GridResult.SelectedItem is ResultRow row) Show(row, zoom: true);
        }

        // ─── Load 3D… (sau khi mở lại bản vẽ) ─────────────────────────────

        private void BtnLoad3D_Click(object sender, RoutedEventArgs e)
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            SetStatus("Select the MCG3D_* blocks to load…");
            doc.SendStringToExecute("MCG_Load3DPanel ", true, false, false);
        }

        // ─── Export STEP (mỗi panel 1 file) ───────────────────────────────

        private void BtnStep_Click(object sender, RoutedEventArgs e)
        {
            if (!StepExport.IsAvailable())
            {
                SetStatus("Requires AutoCAD Mechanical (STEPOUT).", error: true);
                return;
            }
            var kept = _builds.Where(b => b.Kept).ToList();
            if (kept.Count == 0) { SetStatus("Nothing to export — build a panel first.", error: true); return; }

            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            string dwgDir = null;
            try { dwgDir = Path.GetDirectoryName(doc?.Name); } catch { }
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Folder for the STEP files (one file per panel)",
                InitialDirectory = Directory.Exists(dwgDir) ? dwgDir
                    : System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory),
            };
            if (dlg.ShowDialog() != true) return;
            SetStatus($"Exporting {kept.Count} panel(s) to STEP…");
            StepExport.Request(kept, dlg.FolderName);
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
                string openErr = ReportOutput.OpenFile(dlg.FileName);
                if (openErr == null) SetStatus("Exported: " + Path.GetFileName(dlg.FileName));
                else SetStatus($"Exported {Path.GetFileName(dlg.FileName)}, but could not open it: {openErr}", error: true);
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

        private bool _statusIsError;
        private void SetStatus(string message, bool error = false)
        {
            _statusIsError = error;
            StatusText.Set(TxtStatus, message, error);
        }
    }
}
