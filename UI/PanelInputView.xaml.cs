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
            CmbHpSpec.ItemsSource = MCG_3DPanel.Services.HpProfileService.StandardSpecs().ToList();
            CmbHpSpec.SelectedItem = "120*6";
            if (CmbHpSpec.SelectedItem == null) CmbHpSpec.SelectedIndex = 0;
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
            if (!double.TryParse(TxtTopPlateThk.Text, out double t) || t <= 0) { error = "Invalid Top Plate thickness."; return false; }
            if (!double.TryParse(TxtWebHeight.Text, out double h) || h <= 0) { error = "Invalid Web height."; return false; }
            if (!double.TryParse(TxtFlangeThk.Text, out double tf) || tf <= 0) { error = "Invalid Flange thickness."; return false; }
            if (!MCG_3DPanel.Models.PanelInput.TryParseSpec(TxtFlatBarSpec.Text, out _, out _)) { error = "Invalid Flat Bar spec (use height*thickness, e.g. 120*6)."; return false; }
            string hpSpec = CmbHpSpec.SelectedItem as string;
            if (!MCG_3DPanel.Models.PanelInput.TryParseSpec(hpSpec, out _, out _)) { error = "Choose an HP size."; return false; }

            input.TopPlateThickness = t;
            input.WebHeight = h;
            input.FlangeThickness = tf;
            input.FlatBarSpec = TxtFlatBarSpec.Text;
            input.HpSpec = hpSpec;
            return true;
        }

        private void SetStatus(string message, bool error = false) => StatusText.Set(TxtStatus, message, error);
    }
}
