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
                _naming.PositionProvider = () => _input.Position;   // đánh số theo Position của tab Input (kỹ sư 2026-11-05)
                _input.Built += r =>
                {
                    // Các tab đã nạp ở BuildFinished (RefreshTabs) — ở đây chỉ chuyển tab.
                    if (r.Exists(b => b.Kept)) _ps.Activate(TabResult);   // giữ 3D → nhảy sang tab Result xem từng solid
                };
                // Rebuild 1 panel từ tab Result (sửa Kind / ⇄) → thay đúng panel đó trong danh sách, nạp lại bảng.
                MCG_3DPanel.Commands.PanelCommands.Rebuilt += _result.OnRebuilt;
                MCG_3DPanel.Commands.PanelCommands.Rebuilt += _naming.OnRebuilt;
                // Mỗi file dựng xong: nhớ build của nó (lô nhiều panel — chuyển sang file nào cũng thấy đúng dòng).
                MCG_3DPanel.Commands.PanelCommands.FileBuilt += MCG_3DPanel.Commands.PanelSession.Set;
                MCG_3DPanel.Commands.PanelCommands.BuildFinished += () => RefreshTabs();
                // Đóng bản vẽ → bỏ dòng / correction / yêu cầu rebuild của nó TRƯỚC khi Database bị giải phóng
                // (ObjectId cũ trỏ vào bộ nhớ đã free — chạm vào có thể làm AutoCAD crash). Gác TỪNG bước: bước này lỗi
                // bước sau vẫn phải chạy, không thì còn id của Database đã free.
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.DocumentToBeDestroyed += (s, e) =>
                {
                    var db = e.Document?.Database;
                    Guarded(() => MCG_3DPanel.Commands.PanelSession.Forget(db));
                    Guarded(() => MCG_3DPanel.Commands.PanelCommands.ForgetDatabase(db));
                    Guarded(() => MCG_3DPanel.Commands.EntityHighlighter.ForgetDatabase(db));
                    Guarded(() => MCG_3DPanel.Services.Corrections.CorrectionStore.ClearDatabase(db));
                    Guarded(() => MCG_3DPanel.Naming.NameStore.ClearDatabase(db));
                    Guarded(() => _result.OnDocumentClosing(db));
                    Guarded(() => _naming.OnDocumentClosing(db));
                    Guarded(() => _input.OnDocumentClosing(db));
                    // SAU các tab: dòng đọc nền phải rời bảng trước khi database phụ / database file 3D bị giải phóng.
                    Guarded(() => MCG_3DPanel.Commands.Remote3DSession.Forget(db));
                };
                // Đổi drawing: tắt highlight/marker KHI drawing cũ còn là current (Transient/Highlight gắn view của nó).
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.DocumentToBeDeactivated += (s, e) => Guarded(() =>
                {
                    MCG_3DPanel.Commands.EntityHighlighter.ClearPoint();
                    MCG_3DPanel.Commands.EntityHighlighter.Clear();
                });
                // Các tab luôn hiện panel của drawing đang active (mở file 3D ⇒ tự điền, không rebuild, không ghi).
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.DocumentBecameCurrent += (s, e) => RefreshTabs(e.Document?.Database);
                _ps.PaletteActivated += (s, e) => Guarded(() =>
                {
                    _activeTab = e.Activated?.Name ?? _activeTab;
                    if (_activeTab == "Naming") _naming.OnActivated();   // Position tab Input đổi ⇒ đánh số lại
                });
                _ps.AddVisual("Input", _input);
                _ps.AddVisual("Result", _result);
                _ps.AddVisual("Naming", _naming);
                _ps.Activate(TabInput);

                // Cố định CẠNH TRÁI, chỉ cho dock trái (không dock phải/trên/dưới, hạn chế float tự do).
                _ps.DockEnabled = DockSides.Left;
                _ps.Dock = DockSides.Left;

                // Esc trên bản vẽ (không có lệnh) ⇒ tắt highlight + bỏ chọn grid cả 2 tab (kỹ sư 2026-11-05).
                MCG_3DPanel.Commands.CadPick.HookEscape(ClearAllSelections, InPalette);
            }
            _ps.Dock = DockSides.Left;   // đảm bảo luôn về trái mỗi lần mở
            _ps.Visible = true;
            RefreshTabs();   // palette có thể mở sau khi drawing đã mở sẵn
        }

        /// <summary>Nạp Result + Naming theo drawing đang active: đổi drawing, mở palette, Build xong. Đang Build thì bỏ qua
        /// (file dở chưa có dữ liệu panel), cuối Build gọi lại 1 lần. Không dùng Editor; không để lọt exception (AutoCAD chết).</summary>
        private void RefreshTabs(Autodesk.AutoCAD.DatabaseServices.Database eventDb = null)
        {
            if (MCG_3DPanel.Commands.PanelCommands.IsBuilding) return;
            // Update / Plan View từ bản 2D đang mở-đóng file 3D: không nạp giữa chừng, cuối lệnh nạp 1 lần (lỗi 0xc0000005 2026-11-20).
            if (MCG_3DPanel.Commands.UpdateChain.Running) return;
            // Chốt chặn nạp lồng: đang nạp mà sự kiện document bắn lại ⇒ bỏ lần lồng (chẩn đoán tràn stack 2026-11-20).
            if (_refreshing) { MCG_3DPanel.Commands.TraceLog.Write("RefreshTabs: re-entered, skipped"); return; }
            _refreshing = true;
            try { RefreshTabsCore(eventDb); }
            finally { _refreshing = false; }
        }

        private bool _refreshing;

        private void RefreshTabsCore(Autodesk.AutoCAD.DatabaseServices.Database eventDb)
        {
            Guarded(() =>
            {
                // Sự kiện document truyền Database của chính document đó (không lấy MdiActiveDocument: document đang đóng
                // sẽ bị nạp lại vào cache sau Forget).
                var db = eventDb ?? Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Database;
                MCG_3DPanel.Commands.TraceLog.Write("RefreshTabs: " + (FindDoc(db)?.Name ?? "(none)"));
                if (db == null) { _input.ShowPanel(null); return; }   // đóng hết drawing: OnDocumentClosing đã dọn bảng
                var builds = MCG_3DPanel.Commands.PanelSession.For(db, out string message);
                // Bản vẽ 2D (không có panel 3D): đọc nền file <panel>_3D*.dwg cạnh nó — sửa chờ Update (spec 2026-11-20 3d-controls-in-2d).
                if (builds.Count == 0 && !MCG_3DPanel.Commands.Panel3DFile.HoldsSourceReference(db))
                {
                    var doc = FindDoc(db);
                    if (doc != null) builds = MCG_3DPanel.Commands.Remote3DSession.For2D(doc, out message);
                }
                // Input TRƯỚC các tab: Naming đọc Position từ ô Input qua PositionProvider.
                // Panel đọc nền từ file 3D (bản 2D) KHÔNG đưa vào tab Input: tab Input trên bản 2D giữ Build / Update như cũ.
                _input.ShowPanel(System.Linq.Enumerable.FirstOrDefault(builds, b => b.Remote == null));
                // Highlight dùng chung 1 trạng thái ⇒ tab ĐANG MỞ chạy SAU để vùng chọn của nó thắng.
                if (_activeTab == "Naming") { _result.ShowFile(builds, message); _naming.ShowFile(builds, message); }
                else { _naming.ShowFile(builds, message); _result.ShowFile(builds, message); }
                MCG_3DPanel.Commands.TraceLog.Write($"RefreshTabs: done, {builds.Count} panel(s)");
            });
        }

        /// <summary>Nạp lại các tab theo drawing đang active (sau khi ghi sửa chờ trên bản 2D).</summary>
        internal void Refresh() => RefreshTabs();

        private static Autodesk.AutoCAD.ApplicationServices.Document FindDoc(Autodesk.AutoCAD.DatabaseServices.Database db)
        {
            foreach (Autodesk.AutoCAD.ApplicationServices.Document d in Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager)
                if (d.Database == db) return d;
            return null;
        }

        /// <summary>Handler của document/palette: exception lọt ra ngoài có thể làm AutoCAD crash ⇒ bắt, báo trên thanh trạng thái.</summary>
        private void Guarded(Action action)
        {
            try { action(); }
            catch (System.Exception ex)
            {
                try { _result?.ShowError("The tabs could not be updated: " + ex.Message); }
                catch (System.Exception sx) { System.Diagnostics.Debug.WriteLine("[MCG_3DPanel] ShowError: " + sx.Message); }
            }
        }

        /// <summary>Esc (bản vẽ hoặc grid): bỏ chọn trên tab Result + Naming và tắt highlight trên CAD.</summary>
        public void ClearAllSelections()
        {
            _result?.ClearSelection();
            _naming?.ClearSelection();
        }

        /// <summary>Phần tử thuộc palette của mình (Esc ở đó do grid tự xử lý — hook Esc bản vẽ bỏ qua).</summary>
        private bool InPalette(System.Windows.DependencyObject d)
        {
            while (d != null)
            {
                if (ReferenceEquals(d, _input) || ReferenceEquals(d, _result) || ReferenceEquals(d, _naming)) return true;
                d = d is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(d)
                                                     : System.Windows.LogicalTreeHelper.GetParent(d);
            }
            return false;
        }
    }
}
