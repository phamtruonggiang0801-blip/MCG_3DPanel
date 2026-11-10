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

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            _save = true;
            Close();
        }
    }
}
