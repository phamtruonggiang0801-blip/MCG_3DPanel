using System;
using System.Windows;
using System.Windows.Threading;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace MCG_3DPanel.Commands
{
    /// <summary>
    /// Pick trên bản vẽ → dòng grid (spec 2026-11-05 cad-pick). Dùng chọn LỒNG (như NENTSEL) vì click thường chỉ chọn
    /// cả block panel, không biết solid/polyline nào bên trong.
    /// </summary>
    public static class CadPick
    {
        /// <summary>Đang trong vòng Pick (nút Pick bấm lại / hook Esc bỏ qua).</summary>
        public static bool IsPicking { get; private set; }

        /// <summary>Lặp chọn: mỗi lần trúng ⇒ <paramref name="onPick"/>(id chi tiết lồng); Enter/chuột phải ⇒ dừng, giữ
        /// vùng chọn; Esc ⇒ <paramref name="onClear"/> rồi dừng. Trả thông báo lỗi (null nếu bình thường) — không ném,
        /// không xoá vùng chọn khi prompt hỏng (review: chỉ Esc mới xoá).</summary>
        public static string PickLoop(Func<ObjectId, bool> onPick, Action onClear, out int hits)
        {
            hits = 0;
            if (IsPicking) return "Already picking — finish it in the drawing first (Enter = done, Esc = clear).";
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return "No active drawing.";
            if (!string.IsNullOrEmpty(doc.CommandInProgress))
                return $"Finish or cancel the current command ({doc.CommandInProgress}) first, then click Pick again.";
            IsPicking = true;
            try
            {
                try { Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView(); }
                catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine("[MCG_3DPanel] SetFocusToDwgView: " + ex.Message); }   // tiện lợi: thiếu focus thì người dùng click bản vẽ trước

                var opts = new PromptNestedEntityOptions("\nPick an object to find its row [Enter = done, Esc = clear]: ")
                {
                    AllowNone = true,
                };
                while (true)
                {
                    PromptNestedEntityResult r = doc.Editor.GetNestedEntity(opts);
                    if (r.Status == PromptStatus.OK)
                    {
                        if (onPick(r.ObjectId)) hits++;   // không có dòng ⇒ không báo gì (kỹ sư 2026-11-05)
                        continue;
                    }
                    if (r.Status == PromptStatus.None) return null;               // Enter / chuột phải: giữ vùng chọn
                    if (r.Status == PromptStatus.Cancel) { onClear?.Invoke(); return null; }   // Esc
                    return $"Pick stopped ({r.Status}).";
                }
            }
            finally { IsPicking = false; }
        }

        // ─── Esc trên bản vẽ (không có lệnh) ⇒ tắt highlight + bỏ chọn grid ─────────────────────
        private const int WM_KEYDOWN = 0x0100;
        private const int VK_ESCAPE = 0x1B;
        private static Action _onEsc;
        private static Dispatcher _dispatcher;
        private static Func<DependencyObject, bool> _inPalette;

        /// <summary>Gắn 1 lần (PaletteManager, từ UI thread). Bỏ qua: đang có lệnh, đang Pick (vòng Pick tự xử lý Esc),
        /// phím lặp, focus trong palette của mình (grid tự xử lý) hoặc trong ô nhập/dropdown. Việc xoá luôn hoãn ra
        /// <see cref="Dispatcher"/> của palette — không làm việc nặng trong vòng lặp thông điệp.</summary>
        public static void HookEscape(Action onEsc, Func<DependencyObject, bool> inPalette)
        {
            _onEsc = onEsc;
            _inPalette = inPalette;
            if (_dispatcher != null) return;
            _dispatcher = Dispatcher.CurrentDispatcher;
            AcadApp.PreTranslateMessage += (s, e) =>
            {
                if (e.Message.message != WM_KEYDOWN || (int)e.Message.wParam != VK_ESCAPE) return;
                if (((long)e.Message.lParam & (1L << 30)) != 0) return;   // phím giữ (auto-repeat)
                if (IsPicking) return;
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc == null || !string.IsNullOrEmpty(doc.CommandInProgress)) return;
                var focused = System.Windows.Input.Keyboard.FocusedElement as DependencyObject;
                if (focused is System.Windows.Controls.TextBox || focused is System.Windows.Controls.ComboBox
                    || focused is System.Windows.Controls.ComboBoxItem) return;
                if (focused != null && _inPalette != null && _inPalette(focused)) return;
                _dispatcher.BeginInvoke(new Action(() => _onEsc?.Invoke()));
            };
        }
    }
}
