using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.AutoCAD.ApplicationServices;
using MCG_3DPanel.Commands;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace MCG_3DPanel.UI
{
    /// <summary>
    /// Tab "Input" của PaletteSet: thông số + Build 3D.
    /// Gọi trực tiếp logic AutoCAD từ event handler (không qua CommandMethod) — xem skill ui-guidelines.
    /// </summary>
    public partial class PanelInputView : UserControl
    {
        /// <summary>Build xong, có file 3D — PaletteManager chuyển sang tab Result (các tab đã nạp ở PanelCommands.BuildFinished).</summary>
        public event Action<List<MCG_3DPanel.Services.BuildResult>> Built;

        public PanelInputView()
        {
            InitializeComponent();

            // Danh sách HP tiêu chuẩn (18 cỡ) → ComboBox, thay vì nhập tay. Xem HpProfileService.StandardSpecs.
            // Hiển thị "120 × 6" (kỹ sư 2026-11-05), giá trị vẫn là spec chuẩn "120*6" của HP.csv.
            var items = MCG_3DPanel.Services.HpProfileService.StandardSpecs().Select(s => new HpItem(s)).ToList();
            CmbHpSpec.ItemsSource = items;
            CmbHpSpec.SelectedItem = items.FirstOrDefault(i => i.Spec == "120*6") ?? items.FirstOrDefault();
            PanelCommands.Rebuilt += OnRebuilt;
        }

        /// <summary>1 cỡ HP trên dropdown: <see cref="Spec"/> = "120*6" (HP.csv), hiển thị "120 × 6".</summary>
        private sealed class HpItem
        {
            public HpItem(string spec) { Spec = spec; }
            public string Spec { get; }
            public override string ToString() => MCG_3DPanel.Models.PanelInput.FormatSpec(Spec);
        }

        /// <summary>Drawing đang active có panel 3D ⇒ ô nhập hiện giá trị đã lưu của panel và nút thành "Rebuild 3D";
        /// null (drawing 2D) ⇒ nút về "Build 3D", ô giữ nguyên người dùng đã gõ. Gán ô qua code không phải người dùng sửa
        /// (không có handler theo dõi sửa). Vẫn đúng panel + đúng giá trị đã lưu (alt-tab, AutoCAD báo lại cùng document) ⇒ KHÔNG
        /// đụng ô, giữ số người dùng đang gõ; rebuild đổi giá trị đã lưu thì hiện giá trị mới. Không ném.</summary>
        public void ShowPanel(MCG_3DPanel.Services.BuildResult panel)
        {
            try
            {
                if (panel?.Input == null)
                {
                    ForgetShown();   // quay lại panel sau đó phải hiện giá trị của nó: ô giờ là số cho Build 2D
                    _rebuildMode = false;
                    BtnBuild.Content = "Build 3D";
                    BtnBuild.ToolTip = "Pick the panel block and build its 3D solid model";
                    return;
                }
                _rebuildMode = true;
                BtnBuild.Content = "Rebuild 3D";
                BtnBuild.ToolTip = "Rebuild this panel's 3D in this file with the values above";

                var inp = panel.Input;
                // Khoá theo tên block + Database, không theo BuildResult: rebuild thay BuildResult nhưng giữ tên block.
                // Lưu con trỏ, không giữ wrapper Database (ux-guardrails luật 1).
                IntPtr db = panel.BtrId.IsNull ? IntPtr.Zero : panel.BtrId.Database.UnmanagedObject;
                string values = string.Join("|", panel.BlockName, (int)panel.Position, MCG_3DPanel.Models.PanelInput.Fmt(inp.TopPlateThickness),
                    MCG_3DPanel.Models.PanelInput.Fmt(inp.WebHeight), MCG_3DPanel.Models.PanelInput.Fmt(inp.FlangeThickness), inp.FlatBarSpec, inp.HpSpec);
                if (db == _shownDb && values == _shownValues) return;
                _shownDb = db;
                _shownValues = values;

                TxtTopPlateThk.Text = MCG_3DPanel.Models.PanelInput.Fmt(inp.TopPlateThickness);
                TxtWebHeight.Text = MCG_3DPanel.Models.PanelInput.Fmt(inp.WebHeight);
                TxtFlangeThk.Text = MCG_3DPanel.Models.PanelInput.Fmt(inp.FlangeThickness);
                // FB lưu dạng số "120" hoặc spec cũ "120*6" — ô chỉ hiện chiều cao.
                TxtFbHeight.Text = MCG_3DPanel.Models.PanelInput.TryParseHeight(inp.FlatBarSpec, out double fb)
                    ? MCG_3DPanel.Models.PanelInput.Fmt(fb) : inp.FlatBarSpec ?? "";
                CmbPanelPos.SelectedIndex = (int)panel.Position;
                var hp = CmbHpSpec.Items.OfType<HpItem>().FirstOrDefault(i => i.Spec == inp.HpSpec);
                if (hp != null) CmbHpSpec.SelectedItem = hp;
                else { CmbHpSpec.SelectedItem = null; SetStatus($"HP size {MCG_3DPanel.Models.PanelInput.FormatSpec(inp.HpSpec)} of this panel is not in the standard list — choose one before rebuilding.", error: true); }
            }
            catch (System.Exception ex) { Debug.WriteLine("[MCG_3DPanel] ShowPanel: " + ex.Message); }
        }

        // Panel + giá trị đã lưu đang hiện trên các ô (xem ShowPanel).
        private IntPtr _shownDb;
        private string _shownValues;

        private void ForgetShown()
        {
            _shownDb = IntPtr.Zero;
            _shownValues = null;
        }

        private void BtnBuild_Click(object sender, RoutedEventArgs e)
        {
            // Build đổi document giữa chừng: không để exception nào thoát khỏi handler (AutoCAD sẽ chết).
            try
            {
                if (IsRebuildMode) RunRebuildFromInput(); else RunBuildFromInput();
            }
            catch (System.Exception ex)
            {
                try { SetStatus((IsRebuildMode ? "Rebuild failed: " : "Build failed: ") + ex.Message, error: true); }
                catch (System.Exception sx) { Debug.WriteLine("[MCG_3DPanel] SetStatus: " + sx.Message); }
            }
        }

        private bool _rebuildMode;
        private bool IsRebuildMode => _rebuildMode;

        /// <summary>Trong file 3D: áp ô nhập lên panel đầu tiên của file rồi rebuild thường (cùng file, không chọn, không file mới).</summary>
        private void RunRebuildFromInput()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) { SetStatus("No active drawing.", error: true); return; }
            if (PanelCommands.IsBuilding) { SetStatus("A build is running — try again when it finishes.", error: true); return; }

            if (!TryReadInput(out var input, out string err)) { SetStatus(err, error: true); return; }

            // Lấy lại từ session (không giữ BuildResult cũ: sau rebuild session đã thay bằng bản mới).
            var panel = PanelSession.For(doc.Database, out _).FirstOrDefault();
            if (panel == null)
            {
                _rebuildMode = false;
                BtnBuild.Content = "Build 3D";
                SetStatus("This drawing has no 3D panel any more — the button is back to Build 3D.", error: true);
                return;
            }
            RevertPending();   // yêu cầu trước chưa xong (kẹt) ⇒ trả giá trị cũ của nó trước
            _pending = panel;
            _oldInput = panel.Input;
            _oldPosition = panel.Position;
            panel.Input = input;
            panel.Position = (MCG_3DPanel.Models.PanelPosition)Math.Max(0, CmbPanelPos.SelectedIndex);
            SetStatus("Rebuilding… (save the file afterwards)");
            PanelCommands.RequestRebuild(panel);
        }

        // Panel đang chờ rebuild do tab này gửi + giá trị đang lưu trong file (để trả lại khi rebuild hỏng).
        private MCG_3DPanel.Services.BuildResult _pending;
        private MCG_3DPanel.Models.PanelInput _oldInput;
        private MCG_3DPanel.Models.PanelPosition _oldPosition;

        private void RevertPending()
        {
            if (_pending == null) return;
            _pending.Input = _oldInput;
            _pending.Position = _oldPosition;
            _pending = null;
        }

        /// <summary>Rebuild do tab này gửi xong (hoặc hỏng): hiện kết quả; hỏng ⇒ trả Input/Position cũ cho BuildResult (cache
        /// session dùng chung) và hiện lại giá trị thật trong file. Rebuild của tab khác bị bỏ qua. Không ném.</summary>
        private void OnRebuilt(MCG_3DPanel.Services.BuildResult old, MCG_3DPanel.Services.BuildResult neu, string message)
        {
            try
            {
                if (_pending == null || !ReferenceEquals(old, _pending)) return;
                if (neu != null)
                {
                    _pending = null;
                    ShowPanel(neu);   // nhớ giá trị mới ngay, để lần refresh sau không đè số người dùng gõ tiếp
                    SetStatus(message ?? "Rebuilt.", error: false);
                    return;
                }
                var panel = _pending;
                RevertPending();
                ForgetShown();   // ô còn số vừa gõ, giá trị đã lưu không đổi ⇒ buộc hiện lại giá trị thật
                ShowPanel(panel);
                SetStatus(message ?? "Rebuild failed — previous 3D kept.", error: true);
            }
            catch (System.Exception ex) { Debug.WriteLine("[MCG_3DPanel] Input OnRebuilt: " + ex.Message); }
        }

        /// <summary>Drawing sắp đóng: yêu cầu rebuild treo của nó bị bỏ (không có sự kiện Rebuilt) ⇒ trả giá trị cũ. Không ném.</summary>
        public void OnDocumentClosing(Autodesk.AutoCAD.DatabaseServices.Database db)
        {
            try
            {
                if (_pending != null && !_pending.SourceRefId.IsNull && _pending.SourceRefId.Database == db) RevertPending();
                if (db != null && db.UnmanagedObject == _shownDb) ForgetShown();   // con trỏ có thể bị Database mới dùng lại
            }
            catch (System.Exception ex) { Debug.WriteLine("[MCG_3DPanel] Input OnDocumentClosing: " + ex.Message); }
        }

        private void RunBuildFromInput()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) { SetStatus("No active drawing.", error: true); return; }

            if (!TryReadInput(out var input, out string err))
            {
                SetStatus(err, error: true);
                return;
            }

            var pos = (MCG_3DPanel.Models.PanelPosition)CmbPanelPos.SelectedIndex;
            SetStatus("Select one or more panel blocks to build…");
            var builds = PanelCommands.RunBuild(input, pos, out BuildRun run);
            if (builds == null) { SetStatus(run.Stop ?? "Build cancelled.", error: true); return; }

            var notes = new List<string>();
            if (run.Empty > 0) notes.Add($"{run.Empty} panel(s) built nothing");
            if (run.Failed > 0) notes.Add($"{run.Failed} panel(s) failed, see the command line");
            if (run.PendingApplied > 0)
                notes.Add($"{run.PendingApplied} edit{(run.PendingApplied > 1 ? "s" : "")} from the 2D drawing applied");
            notes.AddRange(run.Notes);      // thông tin — không làm câu báo đỏ
            notes.AddRange(run.Problems);
            string tail = string.Concat(notes.Select(n => " · " + n));
            bool error = run.Failed > 0 || run.Problems.Count > 0;

            string summary;
            if (run.Files > 0) summary = $"Built {run.Solids} solids in {run.Files} 3D file(s){tail}";
            else if (run.Opened > 0) summary = $"Opened {run.Opened} existing 3D file(s){tail}";
            else if (run.Failed > 0) { summary = $"No 3D file was built{tail}"; error = true; }
            else { summary = $"No solids were built (check layers/inputs){tail}"; error = true; }
            if (!summary.EndsWith(".")) summary += ".";   // note cuối có thể đã tự có dấu chấm
            SetStatus(summary, error);

            // Chỉ Open / không dựng được gì: không có build nào để đẩy vào tab. Tab tự theo document đang active (file vừa chuyển sang tự nạp dòng của nó), nên không đụng.
            if (builds.Count == 0) return;
            try { Built?.Invoke(builds); }
            catch (System.Exception ex)
            {
                // File đã dựng và lưu xong: giữ kết quả Build, chỉ báo thêm lỗi cập nhật tab.
                SetStatus($"{summary} The tabs could not be updated: {ex.Message}", error: true);
            }
        }

        /// <summary>Vị trí panel đang chọn — tab Naming đánh số theo đây (kỹ sư 2026-11-05).</summary>
        public MCG_3DPanel.Models.PanelPosition Position =>
            (MCG_3DPanel.Models.PanelPosition)System.Math.Max(0, CmbPanelPos.SelectedIndex);

        private bool TryReadInput(out MCG_3DPanel.Models.PanelInput input, out string error)
        {
            input = new MCG_3DPanel.Models.PanelInput();
            error = null;
            // Số dương, dấu thập phân chấm hoặc phẩy (ux-guardrails: báo rõ phải nhập gì).
            if (!MCG_3DPanel.Models.PanelInput.TryParsePositive(TxtTopPlateThk.Text, out double t)) { error = "Top Plate thickness must be a positive number in mm (e.g. 6)."; return false; }
            if (!MCG_3DPanel.Models.PanelInput.TryParsePositive(TxtWebHeight.Text, out double h)) { error = "Web height must be a positive number in mm (e.g. 374)."; return false; }
            if (!MCG_3DPanel.Models.PanelInput.TryParsePositive(TxtFlangeThk.Text, out double tf)) { error = "Flange thickness must be a positive number in mm (e.g. 20)."; return false; }
            // FB: CHỈ chiều cao (kỹ sư 2026-11-05) — chiều dày theo 2D.
            if (!MCG_3DPanel.Models.PanelInput.TryParsePositive(TxtFbHeight.Text, out double hfb)) { error = "FB height must be a positive number in mm (e.g. 120)."; return false; }
            string hpSpec = (CmbHpSpec.SelectedItem as HpItem)?.Spec;
            if (!MCG_3DPanel.Models.PanelInput.TryParseSpec(hpSpec, out _, out _)) { error = "Choose an HP size."; return false; }

            input.TopPlateThickness = t;
            input.WebHeight = h;
            input.FlangeThickness = tf;
            input.FlatBarSpec = MCG_3DPanel.Models.PanelInput.Fmt(hfb);
            input.HpSpec = hpSpec;
            return true;
        }

        private void SetStatus(string message, bool error = false) => StatusText.Set(TxtStatus, message, error);
    }
}
