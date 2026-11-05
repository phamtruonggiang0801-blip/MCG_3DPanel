using System;
using System.Drawing;
using Autodesk.AutoCAD.Windows;

namespace MCG_3DPanel.UI
{
    /// <summary>
    /// Quản lý vòng đời PaletteSet (Singleton). Host WPF control MCG 3D Panel.
    /// </summary>
    public class PaletteManager
    {
        private static PaletteManager _instance;
        public static PaletteManager Instance => _instance ?? (_instance = new PaletteManager());

        // Guid cố định để AutoCAD nhớ vị trí/kích thước palette giữa các phiên.
        private static readonly Guid PaletteGuid = new Guid("B3D5A1F2-3C4D-4E5F-9A8B-1C2D3E4F5A6B");

        // Thứ tự tab — index dùng cho PaletteSet.Activate.
        private const int TabInput = 0;
        private const int TabResult = 1;

        private PaletteSet _ps;
        private PanelInputView _input;
        private PanelResultView _result;

        private PaletteManager() { }

        public void Show()
        {
            if (_ps == null)
            {
                _ps = new PaletteSet("MCG 3D Panel", PaletteGuid)
                {
                    // Tối giản: chỉ nút Close (bỏ AutoHide/PropertiesMenu) — xem skill ui-guidelines.
                    Style = PaletteSetStyles.ShowCloseButton,
                    MinimumSize = new Size(320, 480),
                };

                // Mỗi bước là 1 tab GỐC của PaletteSet (như MCG_FittingManagement), không phải TabControl
                // WPF lồng bên trong — thanh tab trông giống hệt plugin Fitting.
                _input = new PanelInputView();
                _result = new PanelResultView();
                _input.Built += r =>
                {
                    _result.Load(r);
                    if (r.Exists(b => b.Kept)) _ps.Activate(TabResult);   // giữ 3D → nhảy sang tab Result xem từng solid
                };
                // Rebuild 1 panel từ tab Result (sửa Kind / ⇄) → thay đúng panel đó trong danh sách, nạp lại bảng.
                MCG_3DPanel.Commands.PanelCommands.Rebuilt += _result.OnRebuilt;
                // Đóng bản vẽ → bỏ dòng / correction / yêu cầu rebuild của nó TRƯỚC khi Database bị giải phóng
                // (ObjectId cũ trỏ vào bộ nhớ đã free — chạm vào có thể làm AutoCAD crash).
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.DocumentToBeDestroyed += (s, e) =>
                {
                    var db = e.Document?.Database;
                    MCG_3DPanel.Commands.PanelCommands.ForgetDatabase(db);
                    MCG_3DPanel.Commands.EntityHighlighter.ForgetDatabase(db);
                    MCG_3DPanel.Services.Corrections.CorrectionStore.ClearDatabase(db);
                    _result.OnDocumentClosing(db);
                };
                // Đổi drawing: tắt highlight/marker KHI drawing cũ còn là current (Transient/Highlight gắn view của nó),
                // rồi báo trên tab Result nếu bảng đang hiện panel của drawing khác (ux-guardrails, lỗi 2026-11-05).
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.DocumentToBeDeactivated += (s, e) =>
                {
                    MCG_3DPanel.Commands.EntityHighlighter.ClearPoint();
                    MCG_3DPanel.Commands.EntityHighlighter.Clear();
                };
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.DocumentBecameCurrent += (s, e) =>
                    _result.OnDocumentActivated(e.Document?.Database);
                _ps.AddVisual("Input", _input);
                _ps.AddVisual("Result", _result);
                _ps.Activate(TabInput);

                // Cố định CẠNH TRÁI, chỉ cho dock trái (không dock phải/trên/dưới, hạn chế float tự do).
                _ps.DockEnabled = DockSides.Left;
                _ps.Dock = DockSides.Left;
            }
            _ps.Dock = DockSides.Left;   // đảm bảo luôn về trái mỗi lần mở
            _ps.Visible = true;
        }
    }
}
