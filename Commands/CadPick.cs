using System;
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
        /// <summary>Lặp chọn: mỗi lần trúng ⇒ <paramref name="onPick"/>(id chi tiết lồng); Enter/chuột phải ⇒ dừng, giữ
        /// vùng chọn; Esc ⇒ <paramref name="onClear"/> rồi dừng. Trả số lần pick trúng dòng.</summary>
        public static int PickLoop(Func<ObjectId, bool> onPick, Action onClear)
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return 0;
            Editor ed = doc.Editor;
            try { Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView(); }
            catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine("[MCG_3DPanel] SetFocusToDwgView: " + ex.Message); }   // chỉ là tiện lợi: không có focus thì người dùng click vào bản vẽ trước

            int hits = 0;
            var opts = new PromptNestedEntityOptions("\nPick an object to find its row [Enter = done, Esc = clear]: ")
            {
                AllowNone = true,
            };
            while (true)
            {
                PromptNestedEntityResult r = ed.GetNestedEntity(opts);
                if (r.Status == PromptStatus.OK)
                {
                    // Không có dòng ⇒ không báo gì (kỹ sư 2026-11-05).
                    if (onPick(r.ObjectId)) hits++;
                    continue;
                }
                if (r.Status == PromptStatus.None) break;   // Enter / chuột phải: giữ vùng chọn
                onClear?.Invoke();                           // Esc
                break;
            }
            return hits;
        }

        // ─── Esc trên bản vẽ (không có lệnh) ⇒ tắt highlight + bỏ chọn grid ─────────────────────
        private const int WM_KEYDOWN = 0x0100;
        private const int VK_ESCAPE = 0x1B;
        private static Action _onEsc;

        /// <summary>Gắn 1 lần (PaletteManager). Bỏ qua khi đang có lệnh, hoặc khi focus đang ở ô nhập/dropdown của palette
        /// (Esc ở đó là huỷ gõ / đóng dropdown).</summary>
        public static void HookEscape(Action onEsc)
        {
            if (_onEsc != null) { _onEsc = onEsc; return; }
            _onEsc = onEsc;
            AcadApp.PreTranslateMessage += (s, e) =>
            {
                if (e.Message.message != WM_KEYDOWN || (int)e.Message.wParam != VK_ESCAPE) return;
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc == null || !string.IsNullOrEmpty(doc.CommandInProgress)) return;
                var focused = System.Windows.Input.Keyboard.FocusedElement as System.Windows.DependencyObject;
                if (focused is System.Windows.Controls.TextBox || focused is System.Windows.Controls.ComboBox
                    || focused is System.Windows.Controls.ComboBoxItem) return;
                // Không làm việc nặng trong vòng lặp thông điệp: hoãn ra dispatcher.
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() => _onEsc?.Invoke()));
                if (System.Windows.Application.Current == null) _onEsc?.Invoke();
            };
        }
    }
}
