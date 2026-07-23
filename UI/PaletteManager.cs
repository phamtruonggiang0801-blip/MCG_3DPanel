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

        private PaletteSet _ps;
        private PanelBuilderControl _control;

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
                _control = new PanelBuilderControl();
                _ps.AddVisual("Builder", _control);

                // Cố định CẠNH TRÁI, chỉ cho dock trái (không dock phải/trên/dưới, hạn chế float tự do).
                _ps.DockEnabled = DockSides.Left;
                _ps.Dock = DockSides.Left;
            }
            _ps.Dock = DockSides.Left;   // đảm bảo luôn về trái mỗi lần mở
            _ps.Visible = true;
        }
    }
}
