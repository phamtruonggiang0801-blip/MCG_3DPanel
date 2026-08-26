using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
            {
                SetStatus("Classification cancelled.", error: true);
                return;
            }

            PopulateResultGrid(result);
            SetStatus(result.Summary());
            TabMain.SelectedIndex = 1;   // nhảy sang tab Result
        }

        // ─── Tab Result: grid + highlight + export ────────────────────────

        private void PopulateResultGrid(MCG_3DPanel.Services.ClassificationResult result)
        {
            var rows = ResultRow.From(result);
            GridResult.ItemsSource = rows;
            TxtGridInfo.Text = rows.Count == 0
                ? "No parts detected."
                : $"{rows.Count} objects  ·  {result.Summary()}";
        }

        private void GridResult_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GridResult.SelectedItem is ResultRow row)
                EntityHighlighter.Show(row.Path, row.Bounds);
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            var rows = GridResult.ItemsSource as IEnumerable<ResultRow>;
            if (rows == null || !rows.Any())
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
                sb.AppendLine("Handle,DetectedType,CorrectedType");
                foreach (var r in rows)
                    sb.AppendLine($"{Csv(r.Handle)},{Csv(r.DetectedType)},{Csv(r.CorrectedType)}");
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
            // Màu đậm, đọc được trên nền SÁNG (khác pastel cũ vốn thiết kế cho nền tối).
            TxtStatus.Text = message;
            TxtStatus.Foreground = error
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC6, 0x28, 0x28))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2E, 0x7D, 0x32));
        }
    }
}
