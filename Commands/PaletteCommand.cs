using System;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using SDS.UI.Views;

[assembly: CommandClass(typeof(SDS.UI.Commands.PaletteCommand))]

namespace SDS.UI.Commands
{
    public class PaletteCommand
    {
        private static PaletteSet _ps = null;
        private static XBoundaryView _view = null;

        [CommandMethod("CFS_VehicleScripts")]
        public void ShowPalette()
        {
            try
            {
                if (_ps == null)
                {
                    // 1. Khởi tạo cơ bản với GUID
                    Guid psGuid = new Guid("1A2B3C4D-5E6F-7A8B-9C0D-1E2F3A4B5C6D");
                    _ps = new PaletteSet("SDS Boundary Tool", psGuid);

                    // 2. Nạp nội dung (AddVisual)
                    _view = new XBoundaryView();
                    _ps.AddVisual("Boundary Panel", _view);

                    // 3. Thiết lập thông số
                    _ps.DockEnabled = DockSides.Right | DockSides.Left;
                    _ps.Size = new System.Drawing.Size(600, 600);
                    _ps.KeepFocus = true;
                }

                // 4. HIỂN THỊ TRƯỚC (Quan trọng)
                _ps.Visible = true;

                // 5. THIẾT LẬP NEO SAU KHI ĐÃ VISIBLE
                _ps.Dock = DockSides.Right;

                // 6. Kích hoạt tab
                if (_ps.Count > 0)
                {
                    _ps.Activate(0);
                }
            }
            catch (System.Exception ex)
            {
                var ed = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor;
                ed.WriteMessage("\nError Palette: " + ex.Message);
            }
        }
    }
}