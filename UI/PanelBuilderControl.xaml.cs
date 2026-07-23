using System.IO;
using System.Windows;
using System.Windows.Controls;
using Autodesk.AutoCAD.ApplicationServices;
using MCG_3DPanel.Commands;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace MCG_3DPanel.UI
{
    /// <summary>
    /// WPF control host trong PaletteSet. Điều khiển Inspect (Phase 0) và Build (Phase sau).
    /// Gọi trực tiếp logic AutoCAD từ event handler (không qua CommandMethod) — xem skill ui-guidelines.
    /// </summary>
    public partial class PanelBuilderControl : UserControl
    {
        public PanelBuilderControl()
        {
            InitializeComponent();
        }

        private void BtnInspect_Click(object sender, RoutedEventArgs e)
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                SetStatus("No active drawing.", error: true);
                return;
            }

            SetStatus("Select a panel block in the drawing…");
            string outPath = PanelCommands.RunInspect();
            if (string.IsNullOrEmpty(outPath))
                SetStatus("Inspection cancelled.", error: true);
            else
                SetStatus("Exported: " + Path.GetFileName(outPath));
        }

        private void BtnClassify_Click(object sender, RoutedEventArgs e)
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                SetStatus("No active drawing.", error: true);
                return;
            }

            var pos = (MCG_3DPanel.Models.PanelPosition)CmbPanelPos.SelectedIndex;
            SetStatus("Select a panel block to classify…");
            var result = PanelCommands.RunClassify(pos);
            if (result == null)
                SetStatus("Classification cancelled.", error: true);
            else
                SetStatus(result.Summary());
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
            SetStatus("Select a panel block to build…");
            var res = MCG_3DPanel.Commands.PanelCommands.RunBuild(input, pos);
            if (res == null) SetStatus("Build cancelled.", error: true);
            else if (res.SolidCount == 0) SetStatus("No solids were built (check layers/inputs).", error: true);
            else SetStatus($"{(res.Kept ? "Built & kept" : "Built (discarded)")}: {res.SolidCount} solids.");
        }

        private bool TryReadInput(out MCG_3DPanel.Models.PanelInput input, out string error)
        {
            input = new MCG_3DPanel.Models.PanelInput();
            error = null;
            if (!double.TryParse(TxtTopPlateThk.Text, out double t) || t <= 0) { error = "Invalid Top Plate thickness."; return false; }
            if (!double.TryParse(TxtWebHeight.Text, out double h) || h <= 0) { error = "Invalid Web height."; return false; }
            if (!double.TryParse(TxtFlangeThk.Text, out double tf) || tf <= 0) { error = "Invalid Flange thickness."; return false; }
            if (!MCG_3DPanel.Models.PanelInput.TryParseSpec(TxtFlatBarSpec.Text, out _, out _)) { error = "Invalid Flat Bar spec (use height*thickness, e.g. 120*6)."; return false; }
            if (!MCG_3DPanel.Models.PanelInput.TryParseSpec(TxtHpSpec.Text, out _, out _)) { error = "Invalid HP spec (use height*thickness, e.g. 120*6)."; return false; }

            input.TopPlateThickness = t;
            input.WebHeight = h;
            input.FlangeThickness = tf;
            input.FlatBarSpec = TxtFlatBarSpec.Text;
            input.HpSpec = TxtHpSpec.Text;
            return true;
        }

        private void SetStatus(string message, bool error = false)
        {
            TxtStatus.Text = message;
            TxtStatus.Foreground = error
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE0, 0x8A, 0x8A))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8F, 0xD1, 0x8F));
        }
    }
}
