using System.Windows;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace MCG_3DPanel.UI
{
    public enum HandDrawnChoice { Cancel, Replace, KeepBoth }

    /// <summary>Create Drawing gặp phần vẽ tay trên tờ của panel: Replace / Keep both / Cancel. Đóng cửa sổ = Cancel.</summary>
    public partial class HandDrawnDialog : Window
    {
        private HandDrawnChoice _choice = HandDrawnChoice.Cancel;

        private HandDrawnDialog(int count, string panel)
        {
            InitializeComponent();
            TxtMessage.Text = $"{count} hand-drawn items found on the {panel} sheet.";
        }

        public static HandDrawnChoice Ask(int count, string panel)
        {
            var dialog = new HandDrawnDialog(count, panel);
            AcadApp.ShowModalWindow(dialog);
            return dialog._choice;
        }

        private void BtnReplace_Click(object sender, RoutedEventArgs e) { _choice = HandDrawnChoice.Replace; Close(); }
        private void BtnKeep_Click(object sender, RoutedEventArgs e) { _choice = HandDrawnChoice.KeepBoth; Close(); }
    }
}
