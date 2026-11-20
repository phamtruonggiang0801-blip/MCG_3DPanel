using System.Windows;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace MCG_3DPanel.UI
{
    /// <summary>Update gặp file 3D đang mở có thay đổi chưa lưu: true = lưu rồi Update, false = bỏ qua panel này. Đóng cửa sổ = bỏ qua.</summary>
    public partial class UnsavedFileDialog : Window
    {
        private bool _save;

        private UnsavedFileDialog(string fileName)
        {
            InitializeComponent();
            TxtMessage.Text = $"{fileName} has unsaved changes.";
        }

        public static bool AskSave(string fileName)
        {
            var dialog = new UnsavedFileDialog(fileName);
            AcadApp.ShowModalWindow(dialog);
            return dialog._save;
        }

        /// <summary>Build 3D trên bản vẽ 2D chưa lưu (kỹ sư 2026-11-20: gộp Save vào Build): true = lưu rồi Build.</summary>
        public static bool AskSaveBeforeBuild(string fileName)
        {
            var dialog = new UnsavedFileDialog(fileName);
            dialog.TxtMessage.Text = $"{fileName} has unsaved changes. Build 3D reads the saved drawing.";
            dialog.BtnSave.Content = "Save and Build";
            dialog.BtnSave.ToolTip = "Save this drawing (same DWG version, normal .bak), then build the 3D.";
            dialog.BtnSkip.Content = "Cancel";
            dialog.BtnSkip.ToolTip = "Do not save and do not build.";
            AcadApp.ShowModalWindow(dialog);
            return dialog._save;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            _save = true;
            Close();
        }
    }
}
