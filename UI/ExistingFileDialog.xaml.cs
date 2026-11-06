using System.Collections.Generic;
using System.Linq;
using System.Windows;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace MCG_3DPanel.UI
{
    /// <summary>Lựa chọn khi file 3D đích đã có: mở file cũ, dựng bản mới _2, hoặc dừng.</summary>
    public enum ExistingChoice { Open, NewCopy, Cancel }

    /// <summary>Hỏi 1 lần cho cả lô panel có file 3D sẵn. Đóng cửa sổ = Cancel.</summary>
    public partial class ExistingFileDialog : Window
    {
        private ExistingChoice _choice = ExistingChoice.Cancel;

        private ExistingFileDialog(IEnumerable<string> fileNames)
        {
            InitializeComponent();
            ListFiles.ItemsSource = fileNames.ToList();
        }

        public static ExistingChoice Ask(IEnumerable<string> fileNames)
        {
            var dialog = new ExistingFileDialog(fileNames);
            AcadApp.ShowModalWindow(dialog);
            return dialog._choice;
        }

        private void BtnOpen_Click(object sender, RoutedEventArgs e) => Choose(ExistingChoice.Open);

        private void BtnNewCopy_Click(object sender, RoutedEventArgs e) => Choose(ExistingChoice.NewCopy);

        private void Choose(ExistingChoice choice)
        {
            _choice = choice;
            Close();
        }
    }
}
