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
        private PanelNamingView _naming;
        private string _activeTab = "Input";

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
                _naming = new PanelNamingView();
                _naming.InputProvider = _input.TryGetInput;   // quy cách WEB/FLANGE/HP/FB trong tên theo tab Input
                _input.Built += r =>
                {
                    _result.Load(r);
                    _naming.Load(r);   // tab Naming: tên theo tuyến của panel vừa build (spec 2026-11-05)
                    if (r.Exists(b => b.Kept)) _ps.Activate(TabResult);   // giữ 3D → nhảy sang tab Result xem từng solid
                };
                // Rebuild 1 panel từ tab Result (sửa Kind / ⇄) → thay đúng panel đó trong danh sách, nạp lại bảng.
                MCG_3DPanel.Commands.PanelCommands.Rebuilt += _result.OnRebuilt;
                MCG_3DPanel.Commands.PanelCommands.Rebuilt += _naming.OnRebuilt;
                // Đóng bản vẽ → bỏ dòng / correction / yêu cầu rebuild của nó TRƯỚC khi Database bị giải phóng
                // (ObjectId cũ trỏ vào bộ nhớ đã free — chạm vào có thể làm AutoCAD crash).
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.DocumentToBeDestroyed += (s, e) =>
                {
                    var db = e.Document?.Database;
                    MCG_3DPanel.Commands.PanelCommands.ForgetDatabase(db);
                    MCG_3DPanel.Commands.EntityHighlighter.ForgetDatabase(db);
                    MCG_3DPanel.Services.Corrections.CorrectionStore.ClearDatabase(db);
                    MCG_3DPanel.Naming.NameStore.ClearDatabase(db);
                    _result.OnDocumentClosing(db);
                    _naming.OnDocumentClosing(db);
                };
                // Đổi drawing: tắt highlight/marker KHI drawing cũ còn là current (Transient/Highlight gắn view của nó),
                // rồi báo trên tab Result nếu bảng đang hiện panel của drawing khác (ux-guardrails, lỗi 2026-11-05).
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.DocumentToBeDeactivated += (s, e) =>
                {
                    MCG_3DPanel.Commands.EntityHighlighter.ClearPoint();
                    MCG_3DPanel.Commands.EntityHighlighter.Clear();
                };
                // Highlight dùng chung 1 trạng thái ⇒ tab ĐANG MỞ chạy SAU để vùng chọn của nó thắng.
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.DocumentBecameCurrent += (s, e) =>
                {
                    var db = e.Document?.Database;
                    if (_activeTab == "Naming") { _result.OnDocumentActivated(db); _naming.OnDocumentActivated(db); }
                    else { _naming.OnDocumentActivated(db); _result.OnDocumentActivated(db); }
                };
                _ps.PaletteActivated += (s, e) =>
                {
                    _activeTab = e.Activated?.Name ?? _activeTab;
                    if (_activeTab == "Naming") _naming.RefreshSizes();   // quy cách theo tab Input hiện tại
                };
                _ps.AddVisual("Input", _input);
                _ps.AddVisual("Result", _result);
                _ps.AddVisual("Naming", _naming);
                _ps.Activate(TabInput);

                // Cố định CẠNH TRÁI, chỉ cho dock trái (không dock phải/trên/dưới, hạn chế float tự do).
                _ps.DockEnabled = DockSides.Left;
                _ps.Dock = DockSides.Left;

                // Esc trên bản vẽ (không có lệnh) ⇒ tắt highlight + bỏ chọn grid cả 2 tab (kỹ sư 2026-11-05).
                MCG_3DPanel.Commands.CadPick.HookEscape(ClearAllSelections);
            }
            _ps.Dock = DockSides.Left;   // đảm bảo luôn về trái mỗi lần mở
            _ps.Visible = true;
        }

        /// <summary>Esc (bản vẽ hoặc grid): bỏ chọn trên tab Result + Naming và tắt highlight trên CAD.</summary>
        public void ClearAllSelections()
        {
            _result?.ClearSelection();
            _naming?.ClearSelection();
            MCG_3DPanel.Commands.EntityHighlighter.ClearPoint();
            MCG_3DPanel.Commands.EntityHighlighter.Clear();
        }
    }
}
