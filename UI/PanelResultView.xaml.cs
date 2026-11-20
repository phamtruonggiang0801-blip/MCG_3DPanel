using System;
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
            PlanViewCommand.Finished += (msg, err) => SetStatus(msg, err);
            Commands.Detail.DetailCommands.Finished += (msg, err) => SetStatus(msg, err);
            // Update từ bản 2D xong: nạp lại (dòng hết dấu chờ) rồi hiện câu báo của chuỗi.
            Commands.UpdateChain.Finished += (msg, err) =>
            {
                try { PaletteManager.Instance.Refresh(); } catch (System.Exception ex) { msg += " The tabs could not be updated: " + ex.Message; err = true; }
                SetStatus(msg, err);
            };
            Loaded += (s, e) =>
            {
                // STEPOUT chỉ có khi chạy AutoCAD Mechanical — không có thì nút mờ + tooltip giải thích.
                bool ok = StepExport.IsAvailable();
                MnuExportStep.IsEnabled = ok;
                if (!ok) MnuExportStep.ToolTip = "Requires AutoCAD Mechanical (STEPOUT)";
            };
        }

        /// <summary>Danh sách build đang hiển thị — rebuild thay đúng 1 phần tử.</summary>
        private List<BuildResult> _builds = new List<BuildResult>();

        /// <summary>Các dòng cần chọn lại sau rebuild (PanelKey, SourceKey) — cả vùng chọn khi sửa hàng loạt.</summary>
        private List<(string panel, string source)> _reselect;
        /// <summary>Số panel còn chờ rebuild của lần sửa gần nhất (sửa hàng loạt 2 panel ⇒ 2 lần OnRebuilt).</summary>
        private readonly HashSet<string> _pendingPanels = new HashSet<string>();   // BlockName các panel đang chờ rebuild của lần sửa gần nhất

        /// <summary>PanelCommands.Rebuilt: thay bản cũ bằng bản mới (null = thất bại, giữ nguyên) rồi nạp lại.</summary>
        public void OnRebuilt(BuildResult old, BuildResult neu, string message)
        {
            // Chỉ lần rebuild THUỘC lần sửa gần nhất mới dùng vùng chọn + ghi chú của nó (panel khác KHÔNG bị
            // chọn lại dòng cũ hay nối ghi chú cũ — review).
            bool mine = old?.BlockName != null && _pendingPanels.Remove(old.BlockName);
            var sel = mine ? _reselect : null;
            string note = mine ? _bulkNote : null;
            if (_pendingPanels.Count == 0) { _reselect = null; _bulkNote = null; }
            if (neu != null)
            {
                int i = _builds.IndexOf(old);
                // Rebuild panel đã có trên bảng ⇒ thay đúng dòng cũ theo tên block (không nhân đôi).
                if (i < 0) i = _builds.FindIndex(b => b.BlockName == neu.BlockName && b.BlockRefId.Database == neu.BlockRefId.Database);
                if (i >= 0) _builds[i] = neu; else _builds.Add(neu);
            }
            // Nạp lại cả khi thất bại: correction vẫn còn trong kho → dòng hiện đúng trạng thái đã sửa.
            Load(_builds, sel);
            if (note != null) message = note + "  ·  " + message;
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
            // Rebuild ghi report riêng khi có lỗi; panel không lỗi thì có thể chưa có file nào — review I1.
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

        /// <summary>Hiện panel của drawing đang active (đổi drawing, mở palette, Build xong). Bảng đã đúng các build này
        /// (AutoCAD báo lại cùng document) ⇒ giữ dòng + vùng chọn; khác ⇒ thay cả bảng. <paramref name="message"/> ⇒ báo lỗi.</summary>
        public void ShowFile(List<BuildResult> builds, string message)
        {
            EntityHighlighter.ClearPoint();
            EntityHighlighter.Clear();
            if (builds.Count > 0 && _builds.SequenceEqual(builds))
            {
                RestoreSummary();
                // Highlight đã tắt khi rời drawing; bấm lại đúng dòng đang chọn KHÔNG phát SelectionChanged ⇒ tự sáng lại
                // (review I4).
                // Cả vùng chọn (không chỉ 1 dòng) — checklist A3.
                var selRows = Selected;
                if (selRows.Count > 0) Show(selRows, zoom: false);
            }
            else Load(builds);
            if (message != null) SetStatus(message + "  ·  " + TxtStatus.Text, error: true);
        }

        /// <summary>Báo lỗi trên thanh trạng thái (handler của palette không được để lọt exception).</summary>
        public void ShowError(string message) => SetStatus(message, error: true);

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
            // Panel đọc nền từ file 3D thuộc bản 2D (AssyRef2D) — database phụ của nó bị giải phóng cùng lúc (review 2026-11-20).
            // …và panel đọc từ file 3D đang mở như document: đóng file 3D đó ⇒ bỏ (so tham chiếu SideDb, không chạm ObjectId).
            var left = _builds.Where(b => b.Remote != null ? b.Remote.AssyRef2D.Database != db && !ReferenceEquals(b.Remote.SideDb, db)
                                                           : b.BlockRefId.IsNull || b.BlockRefId.Database != db).ToList();
            if (left.Count == before) return;
            if (left.Count == 0)
            {
                _builds = left;
                GridResult.ItemsSource = null;
                BtnUpdate2D.Visibility = Visibility.Collapsed;
                SetStatus("Drawing closed — open a saved 2D drawing to Build 3D, or a 3D panel file to work on it.");
                return;
            }
            Load(left);
        }

        /// <summary>Nạp solid của MỌI panel vừa build (giữ nguyên thứ tự chọn panel). Có
        /// <paramref name="keepPanelKey"/>/<paramref name="keepSourceKey"/> ⇒ chọn lại + cuộn tới + highlight dòng đó.</summary>
        public void Load(List<BuildResult> builds, string keepPanelKey = null, string keepSourceKey = null)
        {
            // Build 3D mới (tab Input) ⇒ bỏ mọi việc sửa đang chờ của bảng cũ (không chọn lại / nối ghi chú cũ).
            if (keepSourceKey == null) { _pendingPanels.Clear(); _reselect = null; _bulkNote = null; }
            Load(builds, keepSourceKey == null ? null : new List<(string, string)> { (keepPanelKey, keepSourceKey) });
        }

        /// <summary>Như trên, chọn lại CẢ vùng chọn <paramref name="keep"/> (sau sửa hàng loạt).</summary>
        public void Load(List<BuildResult> builds, List<(string panel, string source)> keep)
        {
            _builds = builds ?? new List<BuildResult>();
            var kept = _builds.Where(b => b.Kept).ToList();
            // Cột Panel chỉ cần khi có từ 2 panel trở lên — 1 panel thì thừa, lấn chỗ của Kind.
            ColPanel.Visibility = kept.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

            // Nút Update chỉ khi panel đọc nền từ file 3D (đang ở bản 2D); nhãn kèm số sửa chờ.
            var remoteBuilds = kept.Where(b => b.Remote != null).ToList();
            BtnUpdate2D.Visibility = remoteBuilds.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            int waiting = remoteBuilds.GroupBy(b => b.Remote.AssyBtr2D).Sum(g => g.First().Remote.PendingKeys.Count);
            BtnUpdate2D.Content = waiting > 0 ? $"Update ({waiting}) ▾" : "Update ▾";

            // Drawing không có panel (vd bản 2D) ⇒ bảng trống + gợi ý.
            if (kept.Count == 0)
            {
                GridResult.ItemsSource = null;
                _notBuilt = null;
                _summary = "No panel in this drawing. Run Build 3D on a 2D block.";
                SetStatus(_summary);
                return;
            }

            var rows = kept.SelectMany(b => ResultRow.From(b)).ToList();
            foreach (var r in rows) { r.KindChanged += OnKindChanged; r.TripChanged += OnTripChanged; r.CollarChanged += OnCollarChanged; }
            GridResult.ItemsSource = rows;

            string report = _builds.Select(b => b.ReportPath).FirstOrDefault(p => p != null);
            string where = kept.Count > 1 ? $"{kept.Count} panels" : $"block {kept[0].BlockName}";
            _notBuilt = NotBuiltText(kept);
            _summary = rows.Count == 0
                ? "No solids in the 3D block."
                : $"{rows.Count} solids  ·  {Summary(rows)}  ·  {where}"
                  + (report != null ? $"  ·  debug JSON: {System.IO.Path.GetFileName(report)}" : "");
            if (kept.Any(b => b.Remote != null))
            {
                // Bản 2D đọc nền file 3D: báo số sửa chờ; bị khoá ⇒ lý do thay thông báo (ux: không để nút sửa im lặng).
                string locked = kept.Select(b => b.Remote).FirstOrDefault(x => x != null && x.Locked)?.LockReason;
                _summary = (locked ?? PendingText(kept)) + "  ·  " + _summary;
            }
            if (_notBuilt != null) SetStatus(_notBuilt + "  ·  " + _summary, error: true);
            else SetStatus(_summary);

            if (keep != null && keep.Count > 0)
            {
                var want = new HashSet<(string, string)>(keep);
                var hit = rows.Where(r => want.Contains((r.PanelKey, r.SourceKey ?? r.DeleteKey))).ToList();
                if (hit.Count > 0)
                {
                    // Thêm từng dòng ⇒ SelectionChanged bắn N lần (highlight lại cả vùng chọn mỗi lần — chậm với vài
                    // trăm dòng): tạm chặn, highlight 1 lần ở cuối.
                    _suspendShow = true;
                    try
                    {
                        GridResult.SelectedItems.Clear();
                        foreach (var r in hit) GridResult.SelectedItems.Add(r);
                    }
                    finally { _suspendShow = false; }
                    Show(Selected, zoom: false);
                    GridResult.ScrollIntoView(hit[0]);
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

        // ─── Correction → rebuild (sửa hàng loạt kiểu Excel — kỹ sư 2026-11-05) ─────────────

        /// <summary>Các dòng đang chọn (thứ tự hiển thị).</summary>
        private List<ResultRow> Selected
        {
            get
            {
                // Theo THỨ TỰ BẢNG (SelectedItems theo thứ tự click) — Copy / thông báo đúng thứ tự (review).
                var rows = GridResult.ItemsSource as IList<ResultRow>;
                var sel = GridResult.SelectedItems.OfType<ResultRow>().ToList();
                return rows == null ? sel : sel.OrderBy(r => rows.IndexOf(r)).ToList();
            }
        }

        /// <summary>Panel của dòng thuộc drawing ĐANG MỞ (ux-guardrails luật 1).</summary>
        private static bool InActiveDrawing(ResultRow r)
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var b = r?.Build;
            if (doc != null && b?.Remote != null) return b.Remote.AssyRef2D.Database == doc.Database;   // đọc nền từ file 3D: thuộc bản 2D
            return doc != null && b != null && !b.SourceRefId.IsNull && b.SourceRefId.Database == doc.Database;
        }

        /// <summary>Lý do không sửa được trên bản 2D (block lệch file 3D / file 3D đang mở) của dòng đầu tiên bị khoá; null = sửa được.</summary>
        private static string LockedReason(IEnumerable<ResultRow> rows) =>
            rows?.Select(r => r?.Build?.Remote).FirstOrDefault(x => x != null && x.Locked)?.LockReason;

        /// <summary>Trả control về trạng thái thật (dropdown/toggle/ô đã đổi trên màn hình nhưng KHÔNG lưu) + báo.
        /// Hoãn: đang ở giữa cập nhật binding của chính control đó.</summary>
        private void DeferRevert(string message, List<ResultRow> keepRows = null)
        {
            var keep = Keys(keepRows ?? Selected);
            Dispatcher.BeginInvoke(new System.Action(() =>
            {
                Load(_builds, keep);
                SetStatus(message, error: true);
            }));
        }

        private static List<(string panel, string source)> Keys(IEnumerable<ResultRow> rows)
            => rows.Select(r => (r.PanelKey, r.SourceKey ?? r.DeleteKey)).ToList();

        /// <summary>Đổi 1 control trên <paramref name="clicked"/>: thuộc vùng chọn ⇒ áp CÙNG giá trị cho mọi dòng đang chọn
        /// hỗ trợ (<paramref name="supports"/>); ngoài vùng chọn ⇒ chỉ dòng đó. Dòng drawing khác bị bỏ qua, KHÔNG lưu.
        /// Mỗi panel rebuild đúng 1 lần.</summary>
        private void ApplyBulk(ResultRow clicked, Func<ResultRow, bool> supports, Action<ResultRow, PartCorrection> apply, string what)
        {
            // Vùng chọn LÚC NHẤN CHUỘT (WPF có thể đã thu về 1 dòng khi mở dropdown — review), không thì vùng chọn hiện tại.
            var sel = _clickSnapshot != null && _clickSnapshot.Contains(clicked) ? _clickSnapshot : Selected;
            _clickSnapshot = null;
            string locked = LockedReason(sel.Contains(clicked) ? sel : new List<ResultRow> { clicked });
            if (locked != null) { DeferRevert(locked, sel); return; }
            var plan = BulkEdit.Plan(clicked, sel, supports, InActiveDrawing);
            if (plan.Targets.Count == 0)
            {
                string msg = plan.OtherDrawing > 0
                    ? $"This panel is in drawing {DrawingName(clicked?.Build?.SourceRefId.Database)} — switch to it to change {clicked?.Panel}."
                    : $"{what}: nothing to change.";
                DeferRevert(msg, sel);
                return;
            }
            foreach (var r in plan.Targets)
            {
                var c = CorrectionStore.Get(r.PanelKey, r.SourceKey) ?? new PartCorrection { OriginalKind = r.DetectedKind };
                apply(r, c);
                CorrectionStore.Set(r.PanelKey, r.SourceKey, c);
            }
            RebuildMany(clicked, plan.Targets, sel, plan.Targets.Count > 1 || plan.Unsupported + plan.OtherDrawing > 0
                ? BulkEdit.Describe(plan, what) : null);
        }

        private void OnKindChanged(ResultRow row)
        {
            // Chỉ Web ↔ Closed Plate đổi được (kỹ sư 2026-11-09): lưu correction rồi rebuild như các cột khác.
            if (!row.KindEditable || string.IsNullOrEmpty(row.Kind)) return;
            string kind = row.Kind;
            ApplyBulk(row, r => r.KindEditable && r.Options.Any(o => o.Text == kind), (r, c) =>
            {
                if (c.OriginalKind == null) c.OriginalKind = r.DetectedKind;
                c.Kind = ResultCatalog.CorrectionKindFor(kind, r.DetectedKind);
            }, $"Kind = {kind}");
        }

        private void BtnFlip_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is ResultRow row) || !row.CanFlip) return;
            bool target = !(CorrectionStore.Get(row.PanelKey, row.SourceKey)?.FlipDir ?? false);   // CÙNG giá trị cho cả vùng chọn
            ApplyBulk(row, r => r.CanFlip, (r, c) => c.FlipDir = target, target ? "Direction flipped" : "Direction reset");
        }

        // ─── Tripping / Collar theo HP: dropdown Auto / On / Off ─────────────

        /// <summary>ComboBox ĐÓNG mà nhận phím mũi tên / Home / End / PageUp-Down ⇒ WPF đổi lựa chọn ngay ⇒ lưu + rebuild
        /// ngoài ý muốn (vd Esc rồi ↓ để xuống dòng). Chỉ đổi khi người dùng MỞ dropdown chọn — review I2.</summary>
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
            string v = row.TripChoice;
            ApplyBulk(row, r => r.CanTrip, (r, c) => c.Tripping = ResultRow.ToForce(v), $"Tripping = {v}");
        }

        private void OnCollarChanged(ResultRow row)
        {
            if (!row.CanTrip) return;
            string v = row.CollarChoice;
            ApplyBulk(row, r => r.CanTrip, (r, c) => c.Collar = ResultRow.ToForce(v), $"Collar = {v}");
        }

        // ─── Chiều cao riêng từng FB (kỹ sư 2026-11-05: chỉ chiều cao, chiều dày theo 2D) ─────────

        /// <summary>Enter = ghi; Esc = huỷ (trả về giá trị đang dùng).</summary>
        private void TxtFbHeight_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (!(sender is TextBox tb) || !(tb.DataContext is ResultRow row)) return;
            if (e.Key == System.Windows.Input.Key.Enter) { CommitFbHeight(tb, row); e.Handled = true; }
            else if (e.Key == System.Windows.Input.Key.Escape) { ResetBox(tb); e.Handled = true; }
        }

        /// <summary>Rời ô mà chưa Enter ⇒ không ghi, trả về giá trị đang dùng (tránh rebuild ngoài ý muốn).</summary>
        private void TxtFbHeight_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && tb.DataContext is ResultRow row && tb.Text != row.FbHeightText) ResetBox(tb);
        }

        /// <summary>Trả ô về giá trị ĐANG DÙNG qua binding (KHÔNG gán Text — gán đè làm mất binding, ô tái dùng khi cuộn grid
        /// hiện chiều cao của FB khác rồi Enter ghi nhầm — review).</summary>
        private static void ResetBox(TextBox tb) => tb.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();

        private void CommitFbHeight(TextBox tb, ResultRow row)
        {
            if (!row.CanFbHeight) return;
            string text = (tb.Text ?? "").Trim();
            double? h = null;   // trống ⇒ về chiều cao bên tab Input
            if (text.Length > 0)
            {
                if (!MCG_3DPanel.Models.PanelInput.TryParseHeight(text, out double v))
                {
                    SetStatus($"Invalid FB height \"{text}\" — enter a positive number in mm (e.g. 100), or leave it empty to use the Input tab value.", error: true);
                    ResetBox(tb);
                    return;
                }
                h = v;
            }
            if (text == row.FbHeightText && (h != null) == row.FbHeightCorrected) return;   // không đổi
            string what = h == null ? "FB height = Input value" : $"FB height = {MCG_3DPanel.Models.PanelInput.Fmt(h.Value)}";
            ApplyBulk(row, r => r.CanFbHeight, (r, c) => c.FbHeight = h, what);
        }

        // ─── Sniped End từng đầu ──────────────────────────────────────────

        private void BtnSnip_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement fe) || !(fe.DataContext is ResultRow row) || !row.CanSnip) return;
            bool lo = (fe.Tag as string) == "lo";
            // Ép NGƯỢC trạng thái thực tế của dòng được bấm; CÙNG giá trị cho cả vùng chọn.
            bool target = lo ? !row.SnipLoOn : !row.SnipHiOn;
            ApplyBulk(row, r => r.CanSnip, (r, c) => { if (lo) c.SnipLo = target; else c.SnipHi = target; },
                $"Sniped End {(lo ? "L/B" : "R/T")} = {(target ? "on" : "off")}");
        }

        private void BtnSnip_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is ResultRow row && row.CanSnip)
                EntityHighlighter.ShowPoint((fe.Tag as string) == "lo" ? row.EndLo : row.EndHi);
        }

        private void BtnSnip_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => EntityHighlighter.ClearPoint();

        // ─── Delete / Restore / Clear Correction (cả vùng chọn) ─────────────

        private void GridMenu_Opened(object sender, RoutedEventArgs e)
        {
            var sel = Selected.Where(r => !string.IsNullOrEmpty(r.DeleteKey)).ToList();
            MnuDelete.Visibility = sel.Any(r => !r.Deleted) ? Visibility.Visible : Visibility.Collapsed;
            MnuRestore.Visibility = sel.Any(r => r.Deleted) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void MnuDelete_Click(object sender, RoutedEventArgs e) => SetDeleted(true);
        private void MnuRestore_Click(object sender, RoutedEventArgs e) => SetDeleted(false);

        private void SetDeleted(bool deleted)
        {
            var sel = Selected;
            if (sel.Count == 0) return;
            string locked = LockedReason(sel);
            if (locked != null) { SetStatus(locked, error: true); return; }
            var plan = BulkEdit.Plan(sel[0], sel, r => !string.IsNullOrEmpty(r.DeleteKey) && r.Deleted != deleted, InActiveDrawing);
            string what = deleted ? "Deleted" : "Restored";
            if (plan.Targets.Count == 0) { DeferRevert(plan.OtherDrawing > 0 ? OtherDrawingText(sel) : $"{what}: nothing to change.", sel); return; }
            foreach (var r in plan.Targets) CorrectionStore.SetDeleted(r.PanelKey, r.DeleteKey, deleted);
            EntityHighlighter.Clear();
            RebuildMany(sel[0], plan.Targets, sel, BulkEdit.Describe(plan, what));
        }

        private void MnuClearCorrection_Click(object sender, RoutedEventArgs e)
        {
            var sel = Selected;
            if (sel.Count == 0) return;
            string locked = LockedReason(sel);
            if (locked != null) { SetStatus(locked, error: true); return; }
            var plan = BulkEdit.Plan(sel[0], sel, r => CorrectionStore.Get(r.PanelKey, r.SourceKey) != null, InActiveDrawing);
            if (plan.Targets.Count == 0)
            {
                if (plan.OtherDrawing > 0) DeferRevert(OtherDrawingText(sel), sel);
                else SetStatus(sel.Count == 1 ? "This row has no correction." : "The selected rows have no correction.");
                return;
            }
            foreach (var r in plan.Targets) CorrectionStore.Clear(r.PanelKey, r.SourceKey);
            RebuildMany(sel[0], plan.Targets, sel, BulkEdit.Describe(plan, "Correction cleared"));
        }

        /// <summary>"These rows are in drawing X — switch to it to change them." (ux-guardrails luật 1, ghi rõ drawing).</summary>
        private string OtherDrawingText(List<ResultRow> rows)
        {
            var r = rows.FirstOrDefault(x => !InActiveDrawing(x));
            string name = DrawingName(r?.Build?.SourceRefId.Database);
            return (rows.Count > 1 ? "These rows are" : "This row is") + $" in drawing {name} — switch to it to change {(rows.Count > 1 ? "them" : "it")}.";
        }

        /// <summary>Vùng chọn chụp lúc NHẤN CHUỘT lên control trong 1 dòng đang chọn (xem GridResult_PreviewMouseDown).</summary>
        private List<ResultRow> _clickSnapshot;

        /// <summary>Double Click vào dòng ⇒ Zoom (kỹ sư 2026-11-09). Bấm trên ô nhập / nút / dropdown giữ nguyên thao tác của control đó.</summary>
        private void GridResult_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var src = e.OriginalSource as DependencyObject;
            if (!(Up<DataGridRow>(src)?.Item is ResultRow row)) return;
            if (Up<TextBox>(src) != null || Up<ComboBox>(src) != null || Up<System.Windows.Controls.Primitives.ButtonBase>(src) != null) return;
            try
            {
                var sel = Selected;
                Show(sel.Contains(row) ? sel : new List<ResultRow> { row }, zoom: true);
            }
            catch (System.Exception ex) { SetStatus("Could not zoom: " + ex.Message, error: true); }
        }

        /// <summary>Nhấn chuột lên ComboBox / nút / ô nhập trong 1 dòng THUỘC vùng chọn (≥ 2 dòng) ⇒ chụp vùng chọn TRƯỚC khi WPF
        /// có thể thu nó về 1 dòng (ComboBox không nhận focus khi mở) — sửa hàng loạt vẫn áp đúng cả vùng chọn (review).
        /// Nhấn vào ô thường ⇒ bỏ ảnh chụp. Nhấn trong danh sách dropdown (Popup, ngoài cây visual grid) ⇒ giữ.</summary>
        private void GridResult_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var src = e.OriginalSource as DependencyObject;
            var row = Up<DataGridRow>(src);
            if (row == null) return;
            bool onControl = Up<ComboBox>(src) != null || Up<System.Windows.Controls.Primitives.ButtonBase>(src) != null || Up<TextBox>(src) != null;
            var sel = Selected;
            _clickSnapshot = onControl && row.IsSelected && sel.Count > 1 ? sel : null;
        }

        private static T Up<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null && !(d is T))
                d = d is System.Windows.Media.Visual || d is System.Windows.Media.Media3D.Visual3D
                    ? System.Windows.Media.VisualTreeHelper.GetParent(d)
                    : LogicalTreeHelper.GetParent(d);
            return d as T;
        }

        /// <summary>Ghi chú sửa hàng loạt ("Tripping = On on 5 rows · 2 skipped…") — nối vào thông báo rebuild.</summary>
        private string _bulkNote;

        /// <summary>Rebuild MỖI panel có dòng bị sửa đúng 1 lần; giữ lại vùng chọn hiện tại (nếu dòng được bấm thuộc nó).</summary>
        private void RebuildMany(ResultRow clicked, List<ResultRow> targets, List<ResultRow> sel, string note)
        {
            EntityHighlighter.ClearPoint();   // nút dưới chuột bị dựng lại ⇒ MouseLeave có thể không tới
            _reselect = Keys(sel.Contains(clicked) ? sel : new List<ResultRow> { clicked });
            _bulkNote = note;
            var builds = targets.Select(r => r.Build).Where(b => b != null).Distinct().ToList();
            var remote = builds.Where(b => b.Remote != null).ToList();
            if (remote.Count > 0)
            {
                // Bản 2D: không rebuild — ghi phần sửa chờ Update (spec 2026-11-20 3d-controls-in-2d), đọc lại dòng.
                var errors = remote.Select(Commands.Remote3DSession.SavePending).Where(x => x != null).ToList();
                var keep = _reselect;
                _reselect = null;
                _bulkNote = null;
                PaletteManager.Instance.Refresh();
                Load(_builds, keep);
                string msg = (note != null ? note + "  ·  " : "") + PendingText(_builds);
                if (errors.Count > 0) SetStatus(string.Join(" · ", errors), error: true); else SetStatus(msg);
                builds = builds.Where(b => b.Remote == null).ToList();
                if (builds.Count == 0) return;
            }
            SetStatus(CadPick.IsPicking
                ? "Change saved — the 3D rebuilds as soon as you finish picking (Enter in the drawing)."
                : builds.Count == 1 ? $"Rebuilding {targets[0].Panel}…" : $"Rebuilding {builds.Count} panels…");
            _pendingPanels.Clear();
            foreach (var b in builds) if (b.BlockName != null) _pendingPanels.Add(b.BlockName);
            foreach (var b in builds) PanelCommands.RequestRebuild(b);
        }

        /// <summary>"{n} edits waiting — press Update…" theo các panel đọc nền từ file 3D; không có sửa chờ ⇒ rỗng.</summary>
        internal static string PendingText(IEnumerable<BuildResult> builds)
        {
            int n = builds.Where(b => b?.Remote != null).GroupBy(b => b.Remote.AssyBtr2D).Sum(g => g.First().Remote.PendingKeys.Count);
            return n == 0 ? "No edits waiting for Update." : $"{n} edit{(n > 1 ? "s" : "")} waiting — press Update to apply them to the 3D and the drawings.";
        }

        // ─── Highlight / zoom (cả vùng chọn) ────────────────────────────────

        /// <summary>Highlight các dòng <paramref name="rows"/>; <paramref name="zoom"/> ⇒ zoom bao trọn.</summary>
        private void Show(IReadOnlyList<ResultRow> rows, bool zoom)
        {
            if (rows == null || rows.Count == 0) { EntityHighlighter.Clear(); return; }
            var live = rows.Where(r => !r.Deleted && r.Solid != null && !r.Solid.SolidId.IsNull && !r.Solid.SolidId.IsErased && r.Path != null).ToList();
            if (live.Count == 0)
            {
                EntityHighlighter.Clear();
                if (rows.All(r => r.Deleted)) SetStatus("Deleted — right-click Restore to bring it back.");
                else if (rows.Any(r => r.Build?.Remote != null && r.Path == null))
                {
                    var r0 = rows.First(r => r.Build?.Remote != null && r.Path == null);
                    // Đúng lý do (kỹ sư 2026-11-20): block 2D đã đổi ⇒ chờ Update 3D Model; part sinh thêm ⇒ không có hình 2D riêng.
                    if (r0.Build.Remote.Stale2D)
                        SetStatus("The 2D panel changed since the last Update — run Update 3D Model, then the rows find their objects again.", error: true);
                    else if (r0.Solid?.Part == null)
                        SetStatus("This part has no object of its own in the 2D drawing — it is made by the tool.");
                    else
                        SetStatus("The 2D object of this row was not found in the drawing — run Update 3D Model.", error: true);
                }
                // Sau Ctrl+Z (undo rebuild) solid của bảng có thể đã bị xoá — không highlight, chỉ báo.
                else SetStatus("Solid no longer exists — rebuild or re-run Build 3D.", error: true);
                return;
            }
            // Khung zoom chỉ gồm dòng của drawing ĐANG MỞ (dòng drawing khác không được highlight — review).
            var adoc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var inDoc = live.Where(r => adoc != null && r.Path != null && r.Path.Length > 0 && r.Path[0].Database == adoc.Database).ToList();
            var box = (inDoc.Count > 0 ? inDoc : live)[0].Bounds;
            foreach (var r in (inDoc.Count > 0 ? inDoc : live).Skip(1)) box.AddExtents(r.Bounds);
            string err = EntityHighlighter.ShowMany(live.Select(r => r.Path).ToList(), box, zoom);
            int gone = rows.Count - live.Count;
            if (err == null && gone > 0) err = $"{gone} selected row{(gone > 1 ? "s are" : " is")} deleted or no longer exist — not highlighted.";
            if (err != null) SetStatus(err, error: true);
            else if (_statusIsError && _summary != null) RestoreSummary();   // lỗi cũ không treo mãi — review M1
        }

        private void Show(ResultRow row, bool zoom) => Show(new List<ResultRow> { row }, zoom);

        private bool _suspendShow;

        private void GridResult_SelectionChanged(object sender, SelectionChangedEventArgs e)
            {
                // Kéo chuột qua N dòng ⇒ N lần SelectionChanged: gom về 1 lần highlight khi grid rảnh (review — hiệu năng).
                if (_suspendShow || _showQueued) return;
                _showQueued = true;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new System.Action(() =>
                {
                    _showQueued = false;
                    Show(Selected, zoom: false);   // chỉ highlight — zoom bằng nút ⌖
                }));
            }
        private bool _showQueued;

        private void BtnZoomRow_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is ResultRow row)) return;
            var sel = Selected;
            if (sel.Count > 1 && sel.Contains(row)) { Show(sel, zoom: true); return; }   // zoom bao trọn vùng chọn
            GridResult.SelectedItem = row;               // đồng bộ dòng chọn với object đang sáng
            Show(row, zoom: true);
        }

        private void MnuZoomTo_Click(object sender, RoutedEventArgs e) => Show(Selected, zoom: true);

        // ─── Pick trên bản vẽ → dòng; Esc ⇒ bỏ chọn (kỹ sư 2026-11-05) ─────────────────

        private void BtnPick_Click(object sender, RoutedEventArgs e)
        {
            if (Rows.Count == 0) { SetStatus("Nothing listed yet — run Build 3D first.", error: true); return; }
            SetStatus("Pick objects or drag a window in the drawing… Enter = done, Esc = clear.");
            string err;
            int hits;
            try { err = CadPick.PickLoop(SelectFromCad, SelectInWindow, PaletteManager.Instance.ClearAllSelections, out hits); }
            catch (System.Exception ex) { SetStatus("Pick failed: " + ex.Message, error: true); return; }
            if (err != null) { SetStatus(err, error: true); return; }
            if (_statusIsError) return;   // giữ lỗi highlight trong lúc pick (vd không highlight được)
            if (_pendingPanels.Count > 0) SetStatus("Rebuilding the edited panel(s)…");   // sửa trong lúc Pick ⇒ rebuild chạy ngay sau
            else if (Selected.Count > 0) SetStatus($"{Selected.Count} row(s) selected" + (hits > 0 ? $" · {hits} picked" : "") + ".");
            else RestoreSummary();
        }

        /// <summary>Chọn THÊM dòng của solid vừa pick (drawing đang mở), cuộn tới, highlight. Không có dòng ⇒ false (im lặng).</summary>
        public bool SelectFromCad(Autodesk.AutoCAD.DatabaseServices.ObjectId picked, bool remove)
        {
            // Bản 2D: dòng có entity 2D cuối đường dẫn = đối tượng vừa pick.
            var row = Rows.FirstOrDefault(r => r.Solid != null && (r.Build?.Remote != null
                ? r.Path != null && r.Path[r.Path.Length - 1] == picked
                : r.Solid.SolidId == picked));
            if (row == null) return false;
            _suspendShow = true;
            try { if (remove) GridResult.SelectedItems.Remove(row); else if (!GridResult.SelectedItems.Contains(row)) GridResult.SelectedItems.Add(row); }
            finally { _suspendShow = false; }
            if (!remove) GridResult.ScrollIntoView(row);
            Show(Selected, zoom: false);
            return true;
        }

        /// <summary>Quét chọn trong Pick: THÊM mọi dòng (drawing đang mở, solid còn) có solid khớp khung. Trả số dòng thêm.</summary>
        public int SelectInWindow(Func<Autodesk.AutoCAD.DatabaseServices.ObjectId[], Autodesk.AutoCAD.DatabaseServices.Extents3d, bool> hit, bool remove)
        {
            var adoc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (adoc == null) return 0;
            var rows = Rows.Where(r => !r.Deleted && r.Solid != null && !r.Solid.SolidId.IsNull && !r.Solid.SolidId.IsErased
                                       && r.Path != null && r.Path[0].Database == adoc.Database && hit(r.Path, r.Bounds)).ToList();
            int added = 0;
            _suspendShow = true;
            try { foreach (var r in rows) { if (remove) { if (GridResult.SelectedItems.Contains(r)) { GridResult.SelectedItems.Remove(r); added++; } } else if (!GridResult.SelectedItems.Contains(r)) { GridResult.SelectedItems.Add(r); added++; } } }
            finally { _suspendShow = false; }
            if (rows.Count > 0) { if (!remove) GridResult.ScrollIntoView(rows[0]); Show(Selected, zoom: false); }
            return added;
        }

        /// <summary>Esc (bản vẽ hoặc grid): bỏ chọn + tắt highlight.</summary>
        public void ClearSelection()
        {
            _clickSnapshot = null;
            _suspendShow = true;
            try { GridResult.SelectedItems.Clear(); }
            finally { _suspendShow = false; }
            EntityHighlighter.ClearPoint();
            EntityHighlighter.Clear();
        }

        /// <summary>Esc trong grid (không đang gõ trong ô / mở dropdown) ⇒ bỏ chọn ở CẢ 2 tab + tắt highlight.</summary>
        private void Grid_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.Escape) return;
            var src = e.OriginalSource as DependencyObject;
            if (Up<TextBox>(src) != null || Up<ComboBox>(src) != null) return;
            PaletteManager.Instance.ClearAllSelections();
            e.Handled = true;
        }

        /// <summary>Ctrl+C trong grid: copy các dòng ĐANG CHỌN (kèm header) để dán sang Excel.</summary>
        private void GridCopy_Executed(object sender, System.Windows.Input.ExecutedRoutedEventArgs e)
        {
            e.Handled = true;
            CopyRows(Selected.Count > 0 ? Selected : Rows);
        }

        /// <summary>Mở menu Export ngay dưới nút (menu CSV / STEP).</summary>
        private void BtnExportMenu_Click(object sender, RoutedEventArgs e)
        {
            var cm = BtnExportMenu.ContextMenu;
            cm.PlacementTarget = BtnExportMenu;
            cm.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            cm.IsOpen = true;
        }

        // ─── Create Drawing ▸ Plan View (spec 2026-11-10) ─────────────────

        /// <summary>Mở menu Create Drawing dưới nút. Plan View chỉ bật trong file 3D có panel (đọc lại mỗi lần mở menu).</summary>
        private void BtnCreateDrawing_Click(object sender, RoutedEventArgs e)
        {
            var db = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Database;
            bool has3D = db != null && PanelSession.For(db, out _).Any(b => b.Kept && !b.BlockRefId.IsNull);
            // Bản 2D đọc nền file 3D: Plan View chạy được từ đây (mở file 3D ngầm) — kỹ sư 2026-11-20.
            bool remote = !has3D && _builds.Any(b => b.Kept && b.Remote != null && b.Remote.AssyRef2D.Database == db);
            MnuPlanView.IsEnabled = has3D || remote;
            MnuPlanView.ToolTip = has3D || remote ? "Write the Plan View sheet of this panel into the 2D drawing it was built from"
                                                  : "Build 3D first, or open a 3D panel file";
            UpdateDetailMenus();
            var cm = BtnCreateDrawing.ContextMenu;
            cm.PlacementTarget = BtnCreateDrawing;
            cm.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            cm.IsOpen = true;
        }

        /// <summary>Mở menu Update (Drawing / 3D Model) dưới nút — kỹ sư 2026-11-20.</summary>
        private void BtnUpdate2D_Click(object sender, RoutedEventArgs e)
        {
            var cm = BtnUpdate2D.ContextMenu;
            cm.PlacementTarget = BtnUpdate2D;
            cm.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            cm.IsOpen = true;
        }

        private void MnuUpdateDrawing_Click(object sender, RoutedEventArgs e) => RunUpdate(Commands.UpdateChain.RequestDrawing, "Updating the drawing…");
        private void MnuUpdateModel_Click(object sender, RoutedEventArgs e) => RunUpdate(Commands.UpdateChain.RequestModel, "Updating the 3D model…");

        /// <summary>Panel của dòng đang chọn (không chọn ⇒ panel đầu tiên của bản 2D). Nhiều panel ⇒ mỗi lần 1 panel.</summary>
        private void RunUpdate(Action<BuildResult> request, string status)
        {
            try
            {
                var b = Selected.Select(r => r.Build).FirstOrDefault(x => x?.Remote != null)
                        ?? _builds.FirstOrDefault(x => x.Kept && x.Remote != null);
                if (b == null) { SetStatus("Nothing to update — open the 2D drawing of a panel that has a 3D file.", error: true); return; }
                SetStatus(status);
                request(b);
            }
            catch (System.Exception ex) { SetStatus("Update failed: " + ex.Message, error: true); }
        }

        private void MnuPlanView_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var db = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Database;
                var b = Selected.Select(r => r.Build).FirstOrDefault(x => x?.Remote != null && x.Remote.AssyRef2D.Database == db)
                        ?? _builds.FirstOrDefault(x => x.Kept && x.Remote != null && x.Remote.AssyRef2D.Database == db);
                if (b == null) { PlanViewCommand.Request(); return; }   // đang ở file 3D: như cũ
                SetStatus("Writing the Plan View…");
                Commands.UpdateChain.RequestPlanView(b);
            }
            catch (System.Exception ex) { SetStatus("Plan View failed: " + ex.Message, error: true); }
        }

        // ─── Create Drawing ▸ Details (spec 2026-11-16) ───────────────────

        const string DetailDisabledTip = "Open the 2D drawing of a panel that has a 3D file";

        /// <summary>Bật 4 mục Detail theo bản vẽ ACTIVE (đọc lại mỗi lần mở menu): bản vẽ 2D (không phải file 3D) có _Assy mà
        /// thư mục có file &lt;panel&gt;_3D*.dwg. Update Details cần thêm block Detail do tool tạo. Lỗi đọc ⇒ báo status, không bật.</summary>
        private void UpdateDetailMenus()
        {
            bool can = false, hasDetail = false;
            try
            {
                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc != null && !Panel3DFile.HoldsSourceReference(doc.Database))
                    can = Commands.Detail.DetailCommands.HasPanelWith3DFile(doc, out hasDetail);
            }
            catch (System.Exception ex)
            {
                can = false; hasDetail = false;
                SetStatus("Could not check the drawing for details: " + ex.Message, error: true);
            }
            MnuDetailPlan.IsEnabled = MnuSectionDetail.IsEnabled = MnuDetailView.IsEnabled = can;
            MnuDetailPlan.ToolTip = can ? "Cut a detail (inserted at scale 1) from an AM_7 rectangle on the Plan View" : DetailDisabledTip;
            MnuSectionDetail.ToolTip = can ? "Cut a section through a Detail Plan" : DetailDisabledTip;
            MnuDetailView.ToolTip = can ? "Cut a detail at any scale from an AM_7 circle or rectangle" : DetailDisabledTip;
            MnuUpdateDetails.IsEnabled = can && hasDetail;
            MnuUpdateDetails.ToolTip = !can ? DetailDisabledTip
                : !hasDetail ? "This drawing has no detail made by the tool yet" : "Redraw every detail from the current 3D file";
        }

        private void MnuDetailPlan_Click(object sender, RoutedEventArgs e) => Commands.Detail.DetailCommands.Request("MCG_DetailPlan ");
        private void MnuSectionDetail_Click(object sender, RoutedEventArgs e) => Commands.Detail.DetailCommands.Request("MCG_DetailSection ");
        private void MnuDetailView_Click(object sender, RoutedEventArgs e) => Commands.Detail.DetailCommands.Request("MCG_DetailView ");
        private void MnuUpdateDetails_Click(object sender, RoutedEventArgs e) => Commands.Detail.DetailCommands.Request("MCG_DetailUpdate ");

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

        private const string Header = "Panel,Handle,Family,DetectedKind,Kind,Size,Flags,DirFlipped,Tripping,Collar";

        private static string[] Cells(ResultRow r) => new[]
        {
            r.Panel, r.Handle, r.Family.ToString(), r.DetectedKind, r.Kind, r.Size, r.Flags, r.Flipped ? "yes" : "",
            r.CanTrip ? r.TripChoice : "", r.CanTrip ? r.CollarChoice : "",
        };

        /// <summary>Nút Copy / menu Copy Table: chọn nhiều dòng ⇒ chỉ các dòng đó; còn lại ⇒ cả bảng.</summary>
        private void BtnCopy_Click(object sender, RoutedEventArgs e) => CopyRows(Selected.Count > 1 ? Selected : Rows);

        private void CopyRows(List<ResultRow> rows)
        {
            if (rows.Count == 0) { SetStatus("Nothing to copy — run Build 3D first.", error: true); return; }

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
                SetStatus("Nothing to export — run Build 3D first.", error: true);
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
