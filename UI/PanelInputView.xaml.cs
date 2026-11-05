using System;
using System.Collections.Generic;
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
        /// <summary>Build xong (kể cả bị bỏ) — PaletteManager đẩy danh sách solid sang tab Result.</summary>
        public event Action<List<MCG_3DPanel.Services.BuildResult>> Built;

        public PanelInputView()
        {
            InitializeComponent();

            // Danh sách HP tiêu chuẩn (18 cỡ) → ComboBox, thay vì nhập tay. Xem HpProfileService.StandardSpecs.
            // Hiển thị "120 × 6" (kỹ sư 2026-11-05), giá trị vẫn là spec chuẩn "120*6" của HP.csv.
            var items = MCG_3DPanel.Services.HpProfileService.StandardSpecs().Select(s => new HpItem(s)).ToList();
            CmbHpSpec.ItemsSource = items;
            CmbHpSpec.SelectedItem = items.FirstOrDefault(i => i.Spec == "120*6") ?? items.FirstOrDefault();
        }

        /// <summary>1 cỡ HP trên dropdown: <see cref="Spec"/> = "120*6" (HP.csv), hiển thị "120 × 6".</summary>
        private sealed class HpItem
        {
            public HpItem(string spec) { Spec = spec; }
            public string Spec { get; }
            public override string ToString() => MCG_3DPanel.Models.PanelInput.FormatSpec(Spec);
        }

        private void BtnBuild_Click(object sender, RoutedEventArgs e)
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
            var builds = PanelCommands.RunBuild(input, pos);
            if (builds == null) { SetStatus("Build cancelled.", error: true); return; }

            int solids = builds.Sum(b => b.SolidCount);
            int ok = builds.Count(b => b.SolidCount > 0);
            if (solids == 0) SetStatus("No solids were built (check layers/inputs).", error: true);
            else SetStatus($"{(builds.Any(b => b.Kept) ? "Built & kept" : "Built (discarded)")}: " +
                           $"{solids} solids in {ok} panel(s)" +
                           (ok < builds.Count ? $" · {builds.Count - ok} panel(s) built nothing" : "") + ".");
            Built?.Invoke(builds);
        }

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
