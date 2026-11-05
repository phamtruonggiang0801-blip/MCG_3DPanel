using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCG_3DPanel.Commands;
using MCG_3DPanel.Models;
using MCG_3DPanel.Naming;
using MCG_3DPanel.Services;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace MCG_3DPanel.UI
{
    /// <summary>
    /// Tab "Naming" (spec 2026-11-05): tên kết cấu tự sinh theo tuyến (T/L/HT/HL/BS), người dùng sửa được. Tên người dùng
    /// lưu ở XRecord MCG3D_NAMES của block 2D (<see cref="NameStore"/>); đổi tên KHÔNG rebuild. ux-guardrails luật 1–3.
    /// </summary>
    public partial class PanelNamingView : UserControl
    {
        private List<NamingPanel> _panels = new List<NamingPanel>();
        private bool _suspendPosition;

        public delegate bool TryInput(out PanelInput input, out string error);
        /// <summary>Thông số tab Input (PaletteManager gán) — cho nút Select panel.</summary>
        public TryInput InputProvider { get; set; }

        public PanelNamingView()
        {
            InitializeComponent();
        }

        private PanelPosition Position => (PanelPosition)Math.Max(0, CmbPosition.SelectedIndex);

        private void SetPositionSilently(PanelPosition pos)
        {
            _suspendPosition = true;
            try { CmbPosition.SelectedIndex = (int)pos; }
            finally { _suspendPosition = false; }
        }

        // ─── Nạp panel ────────────────────────────────────────────────────

        private void BtnSelect_Click(object sender, RoutedEventArgs e)
        {
            if (AcadApp.DocumentManager.MdiActiveDocument == null) { SetStatus("No active drawing.", error: true); return; }
            // Quy cách WEB/FLANGE/HP/FB lấy theo tab Input (revision 1) — thông số sai thì báo, không đoán.
            PanelInput input = null;
            string inputErr = null;
            if (InputProvider == null || !InputProvider(out input, out inputErr))
            {
                SetStatus($"Check the Input tab first (sizes in the names come from it): {inputErr ?? "no input"}", error: true);
                return;
            }
            SetStatus("Select one or more 2D panel blocks to name…");
            var errors = new List<string>();
            var panels = PanelCommands.ClassifyOnly(Position, input, errors);
            if (panels == null) { SetStatus("Selection cancelled.", error: true); return; }
            SetPanels(panels, null);
            if (errors.Count > 0)
                SetStatus($"{errors.Count} panel(s) could not be classified: {string.Join(" · ", errors)}", error: true);
        }

        /// <summary>Sau Build 3D (tab Input): nạp các panel vừa build (thay cả bảng, giống tab Result).</summary>
        public void Load(List<BuildResult> builds)
        {
            var panels = new List<NamingPanel>();
            var errors = new List<string>();
            foreach (var b in builds ?? new List<BuildResult>())
            {
                if (b == null || b.SourceBtrId.IsNull || b.ClassifiedParts.Count == 0) continue;
                try
                {
                    var p = WithDb(b.SourceBtrId.Database, tr => NamingSource.FromBuild(tr, b));
                    if (p != null) panels.Add(p);
                }
                catch (System.Exception ex) { errors.Add($"{b.PanelBlockName}: {ex.Message}"); }
            }
            if (panels.Count > 0) SetPositionSilently(panels[0].Position);
            SetPanels(panels, null);
            if (errors.Count > 0) SetStatus($"{errors.Count} panel(s) could not be named: {string.Join(" · ", errors)}", error: true);
        }

        /// <summary>Rebuild 1 panel (tab Result / Load 3D) ⇒ thay đúng panel đó (hoặc thêm), giữ vùng chọn.</summary>
        public void OnRebuilt(BuildResult old, BuildResult neu, string message)
        {
            if (neu == null || neu.SourceBtrId.IsNull || neu.ClassifiedParts.Count == 0) return;
            NamingPanel p;
            try { p = WithDb(neu.SourceBtrId.Database, tr => NamingSource.FromBuild(tr, neu)); }
            catch (System.Exception ex) { SetStatus($"Naming: {neu.PanelBlockName} could not be refreshed ({ex.Message}).", error: true); return; }
            if (p == null) return;
            p.Position = Position;
            p.Recompute();
            var keep = Selected.Select(r => r.Key).ToList();
            int i = _panels.FindIndex(x => (old != null && ReferenceEquals(x.Build, old)) || x.PanelKey == p.PanelKey);
            var list = new List<NamingPanel>(_panels);
            if (i >= 0) list[i] = p; else list.Add(p);
            SetPanels(list, keep);
        }

        private void SetPanels(List<NamingPanel> panels, List<(string, string)> keep)
        {
            // 2 lần chèn cùng block 2D = cùng 1 bộ tên (lưu trên định nghĩa block) ⇒ chỉ 1 lần (review I2).
            _panels = BulkEdit.OnePer(panels ?? new List<NamingPanel>(), p => p.PanelKey, out int dup);
            Rebind(keep);
            RestoreSummary();
            if (dup > 0) SetStatus(Summary() + $"  ·  {dup} copy(ies) of the same 2D block listed once (they share their names).");
        }

        private void Rebind(List<(string panel, string piece)> keep)
        {
            ColPanel.Visibility = _panels.Count > 1 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            var rows = _panels.SelectMany(p => p.Lines.Select(l => new NamingRow(p, l))).ToList();
            GridNaming.ItemsSource = rows;
            if (keep == null || keep.Count == 0) return;
            var want = new HashSet<(string, string)>(keep);
            var hit = rows.Where(r => want.Contains(r.Key)).ToList();
            if (hit.Count == 0) return;
            _suspendShow = true;
            try
            {
                GridNaming.SelectedItems.Clear();
                foreach (var r in hit) GridNaming.SelectedItems.Add(r);
            }
            finally { _suspendShow = false; }
            Show(Selected, zoom: false);
            GridNaming.ScrollIntoView(hit[0]);
        }

        private string Summary()
        {
            var rows = Rows;
            if (_panels.Count == 0) return "No panel yet. Click Select panel… or run Build 3D.";
            int user = rows.Count(r => r.IsUserName);
            string where = _panels.Count > 1 ? $"{_panels.Count} panels" : _panels[0].PanelName;
            var by = rows.GroupBy(r => r.Kind).Select(g => $"{g.Key}×{g.Count()}");
            return $"{rows.Count} lines ({string.Join(", ", by)})  ·  {user} renamed  ·  {where}";
        }

        private void RestoreSummary() => SetStatus(Summary());

        private void CmbPosition_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suspendPosition || _panels == null || _panels.Count == 0 || TxtStatus == null) return;
            var keep = Selected.Select(r => r.Key).ToList();
            foreach (var p in _panels) { p.Position = Position; p.Recompute(); }
            Rebind(keep);
            SetStatus($"Renumbered for {Position}.  ·  " + Summary());
        }

        // ─── Đổi tên (Enter / rời ô) ──────────────────────────────────────

        private void TxtName_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (!(sender is TextBox tb) || !(tb.DataContext is NamingRow row)) return;
            if (e.Key == System.Windows.Input.Key.Enter) { Commit(tb, row, bulk: true); e.Handled = true; }
            else if (e.Key == System.Windows.Input.Key.Escape) { ResetBox(tb); e.Handled = true; }
        }

        /// <summary>Rời ô (như Excel) ⇒ ghi CHỈ dòng đó: lúc rời ô vùng chọn có thể đã đổi sang dòng vừa bấm — đổi tên hàng
        /// loạt chỉ qua Enter. Dùng LostKeyboardFocus (bấm ra vùng vẽ AutoCAD cũng ghi — LostFocus logic thì không, review m3).</summary>
        private void TxtName_LostFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox tb && tb.DataContext is NamingRow row && (tb.Text ?? "").Trim() != row.Label) Commit(tb, row, bulk: false);
        }

        /// <summary>Trả ô về tên đang dùng qua binding (không gán Text — giữ binding khi grid tái dùng ô).</summary>
        private static void ResetBox(TextBox tb) => tb.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();

        private void Commit(TextBox tb, NamingRow row, bool bulk)
        {
            // Người dùng chỉ sửa phần tên; quy cách luôn tự ghép (revision 1) ⇒ bỏ quy cách khỏi chữ gõ.
            string text = StructureNaming.StripSpec(tb.Text, row.Spec);
            if (text == row.Name || (text.Length == 0 && !row.IsUserName)) { ResetBox(tb); return; }   // không đổi
            ResetBox(tb);
            // Trống ⇒ về tên tự sinh.
            Rename(row, text.Length == 0 ? null : text, bulk);
        }

        /// <summary>Đặt tên (null = về tự sinh) cho dòng được sửa — hoặc CẢ vùng chọn nếu <paramref name="bulk"/> và dòng
        /// thuộc vùng chọn.</summary>
        private void Rename(NamingRow clicked, string name, bool bulk)
        {
            var sel = !bulk ? new List<NamingRow> { clicked }
                : _clickSnapshot != null && _clickSnapshot.Contains(clicked) ? _clickSnapshot : Selected;
            _clickSnapshot = null;
            var plan = BulkEdit.Plan(clicked, sel, r => true, InActiveDrawing);
            string what = name == null ? "Name = auto" : $"Name = {name}";
            if (plan.Targets.Count == 0)
            {
                // Ô đã được trả về tên cũ (ResetBox) — chỉ báo.
                SetStatus(plan.OtherDrawing > 0 ? OtherDrawingText(clicked) : "Nothing to rename.", error: plan.OtherDrawing > 0);
                return;
            }
            Apply(plan.Targets, name, BulkEdit.Describe(plan, what));
        }

        private void MnuReset_Click(object sender, RoutedEventArgs e)
        {
            var sel = Selected;
            if (sel.Count == 0) return;
            var plan = BulkEdit.Plan(sel[0], sel, r => r.IsUserName, InActiveDrawing);
            if (plan.Targets.Count == 0)
            {
                if (plan.OtherDrawing > 0) SetStatus(OtherDrawingText(sel.First(r => !InActiveDrawing(r))), error: true);
                else SetStatus(sel.Count == 1 ? "This row already has its auto name." : "The selected rows already have their auto names.");
                return;
            }
            Apply(plan.Targets, null, BulkEdit.Describe(plan, "Reset to auto"));
        }

        /// <summary>Ghi tên vào cache + XRecord (1 lần mỗi panel), gắn lại tên, cập nhật dòng tại chỗ.</summary>
        private void Apply(List<NamingRow> targets, string name, string note)
        {
            foreach (var r in targets) NameStore.SetLine(r.PanelData.PanelKey, r.Line, name);
            var touched = targets.Select(r => r.PanelData).Distinct().ToList();
            var errors = SaveAll(touched);
            foreach (var p in touched) p.ResolveNames();
            // Cập nhật TẠI CHỖ (không thay ItemsSource): ô / dòng người dùng vừa bấm sang không bị huỷ (review m1).
            foreach (var r in Rows) if (touched.Contains(r.PanelData)) r.Refresh();
            Report(note, errors);
        }

        private List<string> SaveAll(IEnumerable<NamingPanel> panels)
        {
            var errors = new List<string>();
            foreach (var p in panels)
            {
                try
                {
                    string err = WithDb(p.Db, tr => NameStore.Save(tr, p.SourceBtrId));
                    if (err != null) errors.Add($"{p.PanelName}: {err}");
                }
                catch (System.Exception ex) { errors.Add($"{p.PanelName}: {ex.Message}"); }
            }
            return errors;
        }

        private void Report(string note, List<string> errors)
        {
            if (errors.Count > 0) SetStatus(note + "  ·  " + string.Join(" · ", errors), error: true);
            else SetStatus(note + "  ·  " + Summary());
        }

        // ─── Del Name (kỹ sư revision 1): ẩn tuyến, không đánh số, các tuyến khác đánh số lại ─────────

        private void MnuDelName_Click(object sender, RoutedEventArgs e)
        {
            var sel = Selected;
            if (sel.Count == 0) return;
            var plan = BulkEdit.Plan(sel[0], sel, r => true, InActiveDrawing);
            if (plan.Targets.Count == 0) { SetStatus(OtherDrawingText(sel[0]), error: true); return; }
            foreach (var r in plan.Targets) NameStore.DeleteLine(r.PanelData.PanelKey, r.Line);
            var touched = plan.Targets.Select(r => r.PanelData).Distinct().ToList();
            var errors = SaveAll(touched);
            foreach (var p in touched) p.Recompute();   // đánh số lại
            EntityHighlighter.Clear();
            Rebind(null);
            Report(BulkEdit.Describe(plan, "Name deleted") + " — renumbered (right-click → Restore deleted names to undo)", errors);
        }

        private void GridMenu_Opened(object sender, RoutedEventArgs e)
        {
            var adoc = AcadApp.DocumentManager.MdiActiveDocument;
            int n = _panels.Where(p => adoc != null && p.Db == adoc.Database).Sum(p => NameStore.Deleted(p.PanelKey).Count);
            MnuRestoreDeleted.Header = n > 0 ? $"Restore deleted names ({n})" : "Restore deleted names";
            MnuRestoreDeleted.IsEnabled = n > 0;
        }

        private void MnuRestoreDeleted_Click(object sender, RoutedEventArgs e)
        {
            var adoc = AcadApp.DocumentManager.MdiActiveDocument;
            var panels = _panels.Where(p => adoc != null && p.Db == adoc.Database).ToList();
            int n = panels.Sum(p => NameStore.RestoreAll(p.PanelKey));
            if (n == 0) { SetStatus("Nothing to restore in this drawing."); return; }
            var errors = SaveAll(panels);
            foreach (var p in panels) p.Recompute();
            Rebind(Selected.Select(r => r.Key).ToList());
            Report($"Restored {n} deleted piece name(s) — renumbered", errors);
        }

        // ─── Vùng chọn / drawing ──────────────────────────────────────────

        private List<NamingRow> Rows => (GridNaming.ItemsSource as IEnumerable<NamingRow>)?.ToList() ?? new List<NamingRow>();

        private List<NamingRow> Selected
        {
            get
            {
                var rows = GridNaming.ItemsSource as IList<NamingRow>;
                var sel = GridNaming.SelectedItems.OfType<NamingRow>().ToList();
                return rows == null ? sel : sel.OrderBy(r => rows.IndexOf(r)).ToList();
            }
        }

        private static bool InActiveDrawing(NamingRow r)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            return doc != null && r?.PanelData?.Db != null && r.PanelData.Db == doc.Database;
        }

        private static string OtherDrawingText(NamingRow r)
            => $"Panel {r?.Panel} is in drawing {DrawingName(r?.PanelData?.Db)} — switch to it to rename.";

        private static string DrawingName(Database db)
        {
            foreach (Document d in AcadApp.DocumentManager) if (d.Database == db) return System.IO.Path.GetFileName(d.Name);
            return "(closed)";
        }

        /// <summary>Chạy <paramref name="f"/> trong transaction của drawing chứa <paramref name="db"/> (khoá document).
        /// Drawing đã đóng ⇒ ném (caller báo lỗi — không im lặng).</summary>
        private static T WithDb<T>(Database db, Func<Transaction, T> f)
        {
            Document doc = null;
            foreach (Document d in AcadApp.DocumentManager) if (d.Database == db) { doc = d; break; }
            if (doc == null) throw new InvalidOperationException("the drawing is closed");
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                T r = f(tr);
                tr.Commit();
                return r;
            }
        }

        public void OnDocumentClosing(Database db)
        {
            if (db == null || _panels.Count == 0) return;
            var left = _panels.Where(p => p.Db != db).ToList();
            if (left.Count == _panels.Count) return;
            SetPanels(left, null);
            if (left.Count == 0) SetStatus("Drawing closed — select a panel again.");
        }

        public void OnDocumentActivated(Database db)
        {
            if (db == null || _panels.Count == 0) return;
            if (_panels.Any(p => p.Db == db))
            {
                RestoreSummary();
                var selRows = Selected.Where(r => r.PanelData.Db == db).ToList();
                if (selRows.Count > 0) Show(selRows, zoom: false);
                return;
            }
            SetStatus($"The rows below belong to drawing {DrawingName(_panels[0].Db)} — switch back to it to highlight or rename.", error: true);
        }

        private List<NamingRow> _clickSnapshot;

        /// <summary>Nhấn chuột vào ô Name của 1 dòng thuộc vùng chọn (≥ 2 dòng) ⇒ chụp vùng chọn trước khi WPF thu về 1 dòng.</summary>
        private void GridNaming_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var src = e.OriginalSource as DependencyObject;
            var row = Up<DataGridRow>(src);
            if (row == null) return;
            bool onControl = Up<TextBox>(src) != null || Up<System.Windows.Controls.Primitives.ButtonBase>(src) != null;
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

        // ─── Highlight / zoom ─────────────────────────────────────────────

        private bool _suspendShow;
        private bool _showQueued;

        private void GridNaming_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suspendShow || _showQueued) return;
            _showQueued = true;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
            {
                _showQueued = false;
                Show(Selected, zoom: false);
            }));
        }

        private void Show(IReadOnlyList<NamingRow> rows, bool zoom)
        {
            if (rows == null || rows.Count == 0) { EntityHighlighter.Clear(); return; }
            var adoc = AcadApp.DocumentManager.MdiActiveDocument;
            var paths = new List<ObjectId[]>();
            Extents3d? box = null;
            int missing = 0, other = 0;
            foreach (var r in rows)
            {
                if (adoc == null || r.PanelData.Db != adoc.Database) { other++; continue; }
                bool any = false;
                foreach (var key in r.Line.Pieces.Select(p => p.Key).Distinct())
                {
                    if (!r.PanelData.Paths.TryGetValue(key, out var list)) continue;
                    foreach (var path in list)
                    {
                        if (path.Any(id => id.IsNull || id.IsErased)) continue;
                        paths.Add(path);
                        any = true;
                    }
                    if (r.PanelData.Bounds.TryGetValue(key, out var b))
                    {
                        if (box is Extents3d bb) { bb.AddExtents(b); box = bb; } else box = b;
                    }
                }
                if (!any) missing++;
            }
            if (paths.Count == 0)
            {
                EntityHighlighter.Clear();
                if (other > 0) SetStatus(OtherDrawingText(rows.First(r => adoc == null || r.PanelData.Db != adoc.Database)).Replace("rename", "highlight"), error: true);
                else SetStatus("These objects no longer exist (erased or undone) — select the panel again.", error: true);
                return;
            }
            string err = EntityHighlighter.ShowMany(paths, box ?? new Extents3d(), zoom && box != null);
            if (err == null && missing > 0) err = $"{missing} selected line{(missing > 1 ? "s have" : " has")} no object left to highlight.";
            if (err == null && other > 0) err = $"{other} selected row{(other > 1 ? "s are" : " is")} in another drawing — not highlighted.";
            if (err != null) SetStatus(err, error: true);
            else if (_statusIsError) RestoreSummary();
        }

        /// <summary>Chuột phải → Zoom to Object (kỹ sư revision 1: bỏ cột ⌖).</summary>
        private void MnuZoomTo_Click(object sender, RoutedEventArgs e) => Show(Selected, zoom: true);

        // ─── Copy ─────────────────────────────────────────────────────────

        private void GridCopy_Executed(object sender, System.Windows.Input.ExecutedRoutedEventArgs e)
        {
            e.Handled = true;
            CopyRows(Selected.Count > 0 ? Selected : Rows);
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e) => CopyRows(Selected.Count > 1 ? Selected : Rows);

        private void CopyRows(List<NamingRow> rows)
        {
            if (rows.Count == 0) { SetStatus("Nothing to copy — select a panel first.", error: true); return; }
            var sb = new StringBuilder();
            sb.AppendLine("Panel\tName\tTag\tSize\tKind");
            foreach (var r in rows)
                sb.AppendLine(string.Join("\t", r.Panel, r.Label, r.Name, r.Spec, r.Kind));
            try
            {
                Clipboard.SetText(sb.ToString());
                SetStatus($"Copied {rows.Count} rows to the clipboard.");
            }
            catch (System.Exception ex) { SetStatus("Copy failed: " + ex.Message, error: true); }
        }

        private bool _statusIsError;
        private void SetStatus(string message, bool error = false)
        {
            _statusIsError = error;
            StatusText.Set(TxtStatus, message, error);
        }
    }
}
