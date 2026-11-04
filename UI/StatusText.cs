using System.Windows.Controls;
using System.Windows.Media;

namespace MCG_3DPanel.UI
{
    /// <summary>
    /// Dòng trạng thái dưới đáy mỗi tab — màu theo MCG_FittingManagement:
    /// thường = xanh #005E9E (StatusTextStyle), lỗi = đỏ #C00000.
    /// </summary>
    internal static class StatusText
    {
        private static readonly Brush Normal = Freeze(new SolidColorBrush(Color.FromRgb(0x00, 0x5E, 0x9E)));
        private static readonly Brush Error = Freeze(new SolidColorBrush(Color.FromRgb(0xC0, 0x00, 0x00)));

        public static void Set(TextBlock target, string message, bool error = false)
        {
            target.Text = message;
            target.Foreground = error ? Error : Normal;
        }

        private static Brush Freeze(Brush b) { b.Freeze(); return b; }
    }
}
