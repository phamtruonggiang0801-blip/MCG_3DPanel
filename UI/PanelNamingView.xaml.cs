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

        /// <summary>Position của tab Input (PaletteManager gán) — kỹ sư 2026-11-05: tab Naming không có ô Position riêng.</summary>
        public Func<PanelPosition> PositionProvider { get; set; }

        public PanelNamingView()
        {
            InitializeComponent();
        }

        private PanelPosition Position => PositionProvider?.Invoke() ?? PanelPosition.Center;

        // ─── Nạp panel (sau Build 3D / rebuild / Load 3D) ─────────────────

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
                    if (p == null) continue;
                    if (p.Position != Position) { p.Position = Position; p.Recompute(); }
                    panels.Add(p);
                }
                catch (System.Exception ex) { errors.Add($"{b.PanelBlockName}: {ex.Message}"); }
            }
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
            if (_panels.Count == 0) return "No panel yet. Run Build 3D (Input tab) or Load 3D (Result tab).";
            int user = rows.Count(r => r.IsUserName);
            string where = _panels.Count > 1 ? $"{_panels.Count} panels" : _panels[0].PanelName;
            var by = rows.GroupBy(r => r.Kind).Select(g => $"{g.Key}×{g.Count()}");
            return $"{rows.Count} lines ({string.Join(", ", by)})  ·  {user} renamed  ·  {where}  ·  {Position}";
        }

        private void RestoreSummary() => SetStatus(Summary());

        /// <summary>Mở tab Naming: Position tab Input đã đổi ⇒ đánh số lại (tên tự đặt giữ nguyên).</summary>
        public void OnActivated()
        {
            var pos = Position;
            var changed = _panels.Where(p => p.Position != pos).ToList();
            if (changed.Count == 0) return;
            var keep = Selected.Select(r => r.Key).ToList();
            foreach (var p in changed) { p.Position = pos; p.Recompute(); }
            Rebind(keep);
            SetStatus($"Renumbered for {pos} (Input tab).  ·  " + Summary());
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
            if (sender is TextBox tb && tb.DataContext is NamingRow row && (tb.Text ?? "").Trim() != row.Name) Commit(tb, row, bulk: false);
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
            // Dòng chỉ bị Split (không có tên người dùng) cũng phải về gom tự động.
            bool IsSplit(NamingRow r) => r.Line.Pieces.Any(p => p.Key != null && NameStore.Splits(r.PanelData.PanelKey).Contains(p.Key));
            var plan = BulkEdit.Plan(sel[0], sel, r => r.IsUserName || IsSplit(r), InActiveDrawing);
            if (plan.Targets.Count == 0)
            {
                if (plan.OtherDrawing > 0) SetStatus(OtherDrawingText(sel.First(r => !InActiveDrawing(r))), error: true);
                else SetStatus(sel.Count == 1 ? "This row already has its auto name." : "The selected rows already have their auto names.");
                return;
            }
            bool rejoined = false;
            foreach (var r in plan.Targets) rejoined |= NameStore.Unsplit(r.PanelData.PanelKey, r.Line);
            if (!rejoined) { Apply(plan.Targets, null, BulkEdit.Describe(plan, "Reset to auto")); return; }
            // Có đoạn rejoin ⇒ tuyến đổi: xoá tên người dùng theo SourceKey rồi tính lại + đánh số lại.
            foreach (var r in plan.Targets) NameStore.SetLine(r.PanelData.PanelKey, r.Line, null);
            var touched = plan.Targets.Select(r => r.PanelData).Distinct().ToList();
            foreach (var p in touched) p.Recompute();
            var errors = SaveAll(touched);
            Rebind(plan.Targets.Select(r => r.Key).ToList());
            Report(BulkEdit.Describe(plan, "Reset to auto") + " — split pieces rejoined their automatic lines, renumbered", errors);
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

        // ─── Merge / Unmerge (revision 2): nhiều tuyến cùng loại + cùng phương ⇒ 1 tuyến, 1 tên, 1 số ──────

        private void MnuMerge_Click(object sender, RoutedEventArgs e)
        {
            var sel = Selected;
            if (sel.Count < 2) { SetStatus("Select two or more rows of the same kind to merge."); return; }
            if (sel.Any(r => !InActiveDrawing(r))) { SetStatus(OtherDrawingText(sel.First(r => !InActiveDrawing(r))), error: true); return; }
            if (sel.Select(r => r.PanelData).Distinct().Count() > 1) { SetStatus("Merge works inside one panel — select rows of a single panel.", error: true); return; }
            if (sel.Select(r => (r.Line.Kind, r.Line.Dir)).Distinct().Count() > 1)
            {
                SetStatus("Only lines of the same kind and direction can be merged (e.g. Web T with Web T, BS with BS).", error: true);
                return;
            }
            var p = sel[0].PanelData;
            // Nhiều tên tự đặt khác nhau ⇒ giữ tên của dòng đầu tiên (thứ tự bảng) cho CẢ tuyến gộp, báo tên bị bỏ (review R2 #2).
            var userNames = sel.Where(r => r.IsUserName).Select(r => r.Name).Distinct().ToList();
            string firstKey = sel[0].Line.Pieces.First().Key;
            NameStore.Merge(p.PanelKey, sel.Select(r => r.Line));
            p.Recompute();
            string note = $"Merged {sel.Count} rows into one — renumbered (right-click → Unmerge to split)";
            if (userNames.Count > 0)
            {
                var merged = p.Lines.FirstOrDefault(l => l.Pieces.Any(x => x.Key == firstKey));
                if (merged != null) NameStore.SetLine(p.PanelKey, merged, userNames[0]);
                p.ResolveNames();
                if (userNames.Count > 1) note += $" · kept name {userNames[0]} ({string.Join(", ", userNames.Skip(1))} dropped)";
            }
            var errors = SaveAll(new[] { p });
            Rebind(sel.Select(r => r.Key).ToList());   // dòng gộp có đoạn đầu = đoạn đầu của 1 trong các dòng cũ
            Report(note, errors);
        }

        private void MnuUnmerge_Click(object sender, RoutedEventArgs e)
        {
            var sel = Selected.Where(r => r.Line.IsMerged || StructureNaming.CanSplit(r.Line)).ToList();
            if (sel.Count == 0) { SetStatus("Select a merged line, or a line of two or more pieces."); return; }
            var plan = BulkEdit.Plan(sel[0], sel, r => true, InActiveDrawing);
            if (plan.Targets.Count == 0) { SetStatus(OtherDrawingText(sel[0]), error: true); return; }
            // Tên tự đặt của tuyến gộp chỉ ở lại trên tuyến con chứa đoạn DÀI NHẤT; các tuyến con khác về tên tự sinh
            // (review R2 #1 — tránh nhiều tuyến cùng 1 tên sau khi tách).
            var keepOn = plan.Targets.Where(r => r.IsUserName)
                .Select(r => (r.PanelData, keys: r.Line.Pieces.Select(x => x.Key).ToList(),
                              longest: r.Line.Pieces.OrderByDescending(x => x.Length).First().Key)).ToList();
            foreach (var r in plan.Targets)
            {
                if (r.Line.IsMerged) NameStore.Unmerge(r.PanelData.PanelKey, r.Line);
                else NameStore.Split(r.PanelData.PanelKey, r.Line);
            }
            var touched = plan.Targets.Select(r => r.PanelData).Distinct().ToList();
            foreach (var p in touched) p.Recompute();
            foreach (var (p, keys, longest) in keepOn)
                foreach (var l in p.Lines.Where(l => l.Pieces.Any(x => keys.Contains(x.Key)) && l.Pieces.All(x => x.Key != longest)))
                    NameStore.SetLine(p.PanelKey, l, null);
            foreach (var p in touched) p.ResolveNames();
            var errors = SaveAll(touched);
            Rebind(plan.Targets.Select(r => r.Key).ToList());
            Report(BulkEdit.Describe(plan, "Unmerged") + " — renumbered" + (keepOn.Count > 0 ? " · your name stays on the longest part" : ""), errors);
        }

        private void GridMenu_Opened(object sender, RoutedEventArgs e)
        {
            var sel = Selected;
            // Không gộp được ⇒ mờ + lý do trên tooltip (ux-guardrails), thay vì chỉ báo lỗi khi bấm.
            string why = sel.Count < 2 ? "Select two or more rows to merge."
                : sel.Select(r => r.PanelData).Distinct().Count() > 1 ? "The selected rows belong to different panels."
                : sel.Select(r => (r.Line.Kind, r.Line.Dir)).Distinct().Count() > 1 ? "Only lines of the same kind and direction can be merged."
                : sel.Any(r => !InActiveDrawing(r)) ? "These rows are in another drawing."
                : null;
            MnuMerge.IsEnabled = why == null;
            MnuMerge.ToolTip = why ?? "Combine the selected lines into one line with one name and one number; the others renumber";
            ToolTipService.SetShowOnDisabled(MnuMerge, true);
            bool canUnmerge = sel.Any(r => r.Line.IsMerged || StructureNaming.CanSplit(r.Line));
            MnuUnmerge.IsEnabled = canUnmerge;
            MnuUnmerge.ToolTip = canUnmerge ? "Split the selected lines into their pieces, then renumber. Use Merge to put pieces back together."
                : "This line has one piece — nothing to split.";
            ToolTipService.SetShowOnDisabled(MnuUnmerge, true);
            var adoc = AcadApp.DocumentManager.MdiActiveDocument;
            int n = _panels.Where(p => adoc != null && p.Db == adoc.Database).Sum(p => NameStore.Deleted(p.PanelKey).Count);
            MnuRestoreDeleted.Header = n > 0 ? $"Restore deleted names ({n})" : "Restore deleted names";
            MnuRestoreDeleted.IsEnabled = n > 0;
        }

        private void MnuRestoreDeleted_Click(object sender, RoutedEventArgs e)
        {
            var adoc = AcadApp.DocumentManager.MdiActiveDocument;
            var panels = _panels.Where(p => adoc != null && p.Db == adoc.Database).ToList();
            int before = panels.Sum(p => p.Lines.Count);
            int n = panels.Sum(p => NameStore.RestoreAll(p.PanelKey));
            if (n == 0) { SetStatus("Nothing to restore in this drawing."); return; }
            var errors = SaveAll(panels);
            foreach (var p in panels) p.Recompute();
            Rebind(Selected.Select(r => r.Key).ToList());
            int back = panels.Sum(p => p.Lines.Count) - before;   // đếm TUYẾN hiện lại, không đếm đoạn (review R1)
            Report(back > 0 ? $"Restored {back} line(s) — renumbered" : "Restored — the deleted pieces no longer exist in this panel", errors);
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
                // Web sáng kèm flange của tuyến (revision 2).
                foreach (var key in r.Line.Pieces.Concat(r.Line.Flanges).Select(p => p.Key).Distinct())
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
            if (_statusIsError) return;   // giữ lỗi highlight trong lúc pick
            if (Selected.Count > 0) SetStatus($"{Selected.Count} line(s) selected" + (hits > 0 ? $" · {hits} picked" : "") + ".");
            else RestoreSummary();
        }

        /// <summary>Chọn THÊM dòng chứa chi tiết vừa pick (solid 3D hoặc polyline 2D, kể cả flange ⇒ dòng web) trong panel của
        /// drawing đang mở. Không có dòng ⇒ false (im lặng — kỹ sư 2026-11-05).</summary>
        public bool SelectFromCad(ObjectId picked, bool remove)
        {
            var adoc = AcadApp.DocumentManager.MdiActiveDocument;
            bool Hit(NamingRow r) => r.Line.Pieces.Concat(r.Line.Flanges).Any(p =>
                r.PanelData.Paths.TryGetValue(p.Key, out var paths) && paths.Any(path => path.Length > 0 && path[path.Length - 1] == picked));
            var row = Rows.FirstOrDefault(r => adoc != null && r.PanelData.Db == adoc.Database && Hit(r));
            if (row == null) return false;
            _suspendShow = true;
            try { if (remove) GridNaming.SelectedItems.Remove(row); else if (!GridNaming.SelectedItems.Contains(row)) GridNaming.SelectedItems.Add(row); }
            finally { _suspendShow = false; }
            if (!remove) GridNaming.ScrollIntoView(row);
            Show(Selected, zoom: false);
            return true;
        }

        /// <summary>Quét chọn trong Pick: THÊM mọi tuyến (drawing đang mở) có ÍT NHẤT 1 đoạn/flange khớp khung. Trả số dòng thêm.</summary>
        public int SelectInWindow(Func<Extents3d, bool> hit, bool remove)
        {
            var adoc = AcadApp.DocumentManager.MdiActiveDocument;
            if (adoc == null) return 0;
            var rows = Rows.Where(r => r.PanelData.Db == adoc.Database
                && r.Line.Pieces.Concat(r.Line.Flanges).Any(p => r.PanelData.Bounds.TryGetValue(p.Key, out var b) && hit(b))).ToList();
            int added = 0;
            _suspendShow = true;
            try { foreach (var r in rows) { if (remove) { if (GridNaming.SelectedItems.Contains(r)) { GridNaming.SelectedItems.Remove(r); added++; } } else if (!GridNaming.SelectedItems.Contains(r)) { GridNaming.SelectedItems.Add(r); added++; } } }
            finally { _suspendShow = false; }
            if (rows.Count > 0) { if (!remove) GridNaming.ScrollIntoView(rows[0]); Show(Selected, zoom: false); }
            return added;
        }

        /// <summary>Esc (bản vẽ hoặc grid): bỏ chọn + tắt highlight.</summary>
        public void ClearSelection()
        {
            _clickSnapshot = null;
            _suspendShow = true;
            try { GridNaming.SelectedItems.Clear(); }
            finally { _suspendShow = false; }
            EntityHighlighter.Clear();
        }

        /// <summary>Esc trong grid (không đang gõ tên) ⇒ bỏ chọn ở CẢ 2 tab + tắt highlight.</summary>
        private void Grid_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.Escape) return;
            if (Up<TextBox>(e.OriginalSource as DependencyObject) != null) return;   // Esc trong ô Name = huỷ gõ
            PaletteManager.Instance.ClearAllSelections();
            e.Handled = true;
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
            sb.AppendLine("Panel\tName\tSize\tKind");
            foreach (var r in rows)
                sb.AppendLine(string.Join("\t", r.Panel, r.Name, r.Spec, r.KindText));
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
