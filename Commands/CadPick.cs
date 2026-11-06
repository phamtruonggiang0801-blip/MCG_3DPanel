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

        /// <summary>
        /// Lặp chọn như AutoCAD (kỹ sư 2026-11-05): click trúng chi tiết ⇒ chọn lồng (như NENTSEL); click chỗ TRỐNG ⇒ kéo
        /// khung (trái→phải Window = trọn trong, phải→trái Crossing = chạm). **Bỏ chọn**: giữ Shift khi click/kéo, hoặc gõ
        /// R (Remove) — A (Add) để quay lại thêm. Enter/chuột phải ⇒ dừng, giữ vùng chọn; Esc ⇒ <paramref name="onClear"/>.
        /// <paramref name="onPick"/>(id, remove) / <paramref name="onWindow"/>(phép thử (đường dẫn entity, hộp bao), remove) trả số dòng đổi.
        /// Trả thông báo lỗi (null nếu bình thường) — không ném, prompt hỏng không xoá vùng chọn (chỉ Esc mới xoá).
        /// </summary>
        public static string PickLoop(Func<ObjectId, bool, bool> onPick,
            Func<Func<ObjectId[], Autodesk.AutoCAD.DatabaseServices.Extents3d, bool>, bool, int> onWindow,
            Action onClear, out int hits)
        {
            hits = 0;
            if (IsPicking) return "Already picking — finish it in the drawing first (Enter = done, Esc = clear).";
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return "No active drawing.";
            if (!string.IsNullOrEmpty(doc.CommandInProgress))
                return $"Finish or cancel the current command ({doc.CommandInProgress}) first, then click Pick again.";
            IsPicking = true;
            // Prompt LẤY ĐIỂM: Shift = tạm bật Ortho (TEMPOVERRIDES) ⇒ không có điểm gốc thì khoá theo LASTPOINT (thường 0,0)
            // ⇒ góc khung nhảy về gốc (lỗi 2026-11-05). Tạm tắt Shift-override, Ortho, OSnap như lệnh chọn của AutoCAD; trả lại
            // đúng giá trị cũ ở finally.
            System.Collections.Generic.Dictionary<string, object> saved;
            using (doc.LockDocument()) saved = SuspendPointAids();   // ORTHOMODE là biến của bản vẽ ⇒ cần khoá khi gọi từ palette
            try
            {
                try { Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView(); }
                catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine("[MCG_3DPanel] SetFocusToDwgView: " + ex.Message); }   // tiện lợi: thiếu focus thì người dùng click bản vẽ trước

                Editor ed = doc.Editor;
                bool removeMode = false;
                var edgeCache = new System.Collections.Generic.Dictionary<ObjectId, Autodesk.AutoCAD.Geometry.Point3d[][]>();   // cạnh solid, sống trong 1 lần Pick
                while (true)
                {
                    var po = new PromptPointOptions(removeMode
                        ? "\nRemove: pick an object or drag a window [Add] <Enter = done, Esc = clear>: "
                        : "\nPick an object or drag a window, Shift = unselect [Remove] <Enter = done, Esc = clear>: ")
                    {
                        AllowNone = true,
                    };
                    po.Keywords.Add(removeMode ? "Add" : "Remove");
                    PromptPointResult p = ed.GetPoint(po);
                    if (p.Status == PromptStatus.Keyword) { removeMode = p.StringResult == "Remove"; continue; }
                    if (p.Status == PromptStatus.None) return null;
                    if (p.Status == PromptStatus.Cancel) { onClear?.Invoke(); return null; }
                    if (p.Status != PromptStatus.OK) return $"Pick stopped ({p.Status}).";
                    bool remove = removeMode || ShiftDown();   // Shift + click = bỏ chọn, như AutoCAD

                    // Trúng chi tiết tại điểm click ⇒ chọn lồng; không trúng ⇒ góc 1 của khung.
                    var at = ed.GetNestedEntity(new PromptNestedEntityOptions("")
                    {
                        NonInteractivePickPoint = p.Value,
                        UseNonInteractivePickPoint = true,
                    });
                    if (at.Status == PromptStatus.OK)
                    {
                        if (onPick(at.ObjectId, remove)) hits++;   // không có dòng ⇒ không báo gì (kỹ sư 2026-11-05)
                        continue;
                    }
                    PromptPointResult c = ed.GetCorner(new PromptCornerOptions("\nOpposite corner: ", p.Value));
                    if (c.Status == PromptStatus.Cancel) { onClear?.Invoke(); return null; }
                    if (c.Status != PromptStatus.OK) continue;
                    hits += onWindow(WindowTest(ed, doc.Database, p.Value, c.Value, edgeCache), remove || ShiftDown());
                }
            }
            finally
            {
                try { using (doc.LockDocument()) RestorePointAids(saved); }
                catch (System.Exception ex) { doc.Editor.WriteMessage($"\n[MCG_3DPanel] Could not restore ORTHOMODE / OSMODE / TEMPOVERRIDES after Pick ({ex.Message}) — check them.\n"); }
                IsPicking = false;
            }
        }

        private static readonly string[] AidVars = { "TEMPOVERRIDES", "ORTHOMODE", "OSMODE" };

        /// <summary>Tắt tạm TEMPOVERRIDES=0, ORTHOMODE=0, OSMODE bật bit 16384 (OSnap off). Trả giá trị cũ (biến không đọc được ⇒ bỏ qua).</summary>
        private static System.Collections.Generic.Dictionary<string, object> SuspendPointAids()
        {
            var old = new System.Collections.Generic.Dictionary<string, object>();
            foreach (var v in AidVars)
            {
                try
                {
                    object cur = AcadApp.GetSystemVariable(v);
                    old[v] = cur;
                    object off = v == "OSMODE" ? (object)(short)(System.Convert.ToInt32(cur) | 16384) : (short)0;
                    AcadApp.SetSystemVariable(v, off);
                }
                catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine($"[MCG_3DPanel] {v}: {ex.Message}"); }   // biến không có trên bản AutoCAD này ⇒ giữ nguyên
            }
            return old;
        }

        private static void RestorePointAids(System.Collections.Generic.Dictionary<string, object> old)
        {
            foreach (var kv in old)
            {
                try { AcadApp.SetSystemVariable(kv.Key, kv.Value); }
                catch (System.Exception ex) { AcadApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage($"\n[MCG_3DPanel] Could not restore {kv.Key} to {kv.Value} after Pick ({ex.Message}).\n"); }
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetKeyState(int vKey);
        private const int VK_SHIFT = 0x10;

        /// <summary>Shift đang giữ (đọc trạng thái phím Win32 — focus ở cửa sổ vẽ, không phải WPF).</summary>
        private static bool ShiftDown() => (GetKeyState(VK_SHIFT) & 0x8000) != 0;

        /// <summary>Phép thử khung 2 góc (UCS) trên mặt phẳng nhìn hiện tại (đúng cả góc nhìn 3D). Solid so theo CẠNH THẬT: hộp bao top plate
        /// phủ cả panel, flange xiên phồng to ⇒ quét đâu cũng dính (lỗi 2026-11-06). Thực thể khác (polyline 2D) so theo hộp bao.</summary>
        private static Func<ObjectId[], Autodesk.AutoCAD.DatabaseServices.Extents3d, bool> WindowTest(Editor ed, Database db,
            Autodesk.AutoCAD.Geometry.Point3d c1Ucs, Autodesk.AutoCAD.Geometry.Point3d c2Ucs,
            System.Collections.Generic.Dictionary<ObjectId, Autodesk.AutoCAD.Geometry.Point3d[][]> cache)
        {
            var ucs = ed.CurrentUserCoordinateSystem;
            Autodesk.AutoCAD.Geometry.Matrix3d toEye;
            using (ViewTableRecord v = ed.GetCurrentView())
            {
                toEye = (Autodesk.AutoCAD.Geometry.Matrix3d.Rotation(-v.ViewTwist, v.ViewDirection, v.Target)
                         * Autodesk.AutoCAD.Geometry.Matrix3d.Displacement(v.Target - Autodesk.AutoCAD.Geometry.Point3d.Origin)
                         * Autodesk.AutoCAD.Geometry.Matrix3d.PlaneToWorld(v.ViewDirection)).Inverse();
            }
            var a = c1Ucs.TransformBy(ucs).TransformBy(toEye);
            var b = c2Ucs.TransformBy(ucs).TransformBy(toEye);
            bool crossing = PickWindow.IsCrossing(a.X, b.X);
            bool BoxHit(Autodesk.AutoCAD.DatabaseServices.Extents3d box, Autodesk.AutoCAD.Geometry.Matrix3d m)
            {
                double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
                var lo = box.MinPoint; var hi = box.MaxPoint;
                for (int i = 0; i < 8; i++)
                {
                    var q = new Autodesk.AutoCAD.Geometry.Point3d((i & 1) == 0 ? lo.X : hi.X, (i & 2) == 0 ? lo.Y : hi.Y, (i & 4) == 0 ? lo.Z : hi.Z)
                        .TransformBy(m);
                    x0 = Math.Min(x0, q.X); y0 = Math.Min(y0, q.Y); x1 = Math.Max(x1, q.X); y1 = Math.Max(y1, q.Y);
                }
                return PickWindow.Hit(a.X, a.Y, b.X, b.Y, crossing, x0, y0, x1, y1);
            }
            return (path, box) =>
            {
                if (path == null || path.Length == 0) return BoxHit(box, toEye);
                using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    var id = path[path.Length - 1];
                    if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Solid3d solid)) return BoxHit(box, toEye);
                    var m = toEye;
                    for (int i = 0; i < path.Length - 1; i++)
                        if (tr.GetObject(path[i], OpenMode.ForRead) is BlockReference br) m = m * br.BlockTransform;
                    if (crossing && !BoxHit(box, m)) return false;   // hộp bao không chạm ⇒ chắc chắn không chạm, khỏi đọc cạnh
                    var edges = SolidEdges(solid, cache, ed);
                    if (edges == null) return BoxHit(box, m);
                    var flat = new System.Collections.Generic.List<(double X0, double Y0, double X1, double Y1)>();
                    foreach (var poly in edges)
                        for (int i = 0; i + 1 < poly.Length; i++)
                        {
                            var p0 = poly[i].TransformBy(m); var p1 = poly[i + 1].TransformBy(m);
                            flat.Add((p0.X, p0.Y, p1.X, p1.Y));
                        }
                    return PickWindow.HitEdges(a.X, a.Y, b.X, b.Y, crossing, flat);
                }
            };
        }

        private const int EDGE_SAMPLES = 9;   // điểm lấy trên mỗi cạnh (cạnh cong: scallop, lỗ CO)

        /// <summary>Cạnh solid (hệ block), mỗi cạnh là 1 dãy điểm. Lỗi đọc Brep ⇒ null, báo lên command line, caller dùng hộp bao.</summary>
        private static Autodesk.AutoCAD.Geometry.Point3d[][] SolidEdges(Solid3d solid,
            System.Collections.Generic.Dictionary<ObjectId, Autodesk.AutoCAD.Geometry.Point3d[][]> cache, Editor ed)
        {
            if (cache.TryGetValue(solid.ObjectId, out var known)) return known;
            try
            {
                var list = new System.Collections.Generic.List<Autodesk.AutoCAD.Geometry.Point3d[]>();
                using (var brep = new Autodesk.AutoCAD.BoundaryRepresentation.Brep(solid))
                    foreach (var e in brep.Edges)
                        using (var c = e.Curve)
                        {
                            var pts = c.GetSamplePoints(EDGE_SAMPLES);
                            var arr = new Autodesk.AutoCAD.Geometry.Point3d[pts.Length];
                            for (int i = 0; i < pts.Length; i++) arr[i] = pts[i].Point;
                            list.Add(arr);
                        }
                return cache[solid.ObjectId] = list.ToArray();
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[MCG_3DPanel] Could not read the edges of solid {solid.Handle}, using its bounding box: {ex.Message}\n");
                return cache[solid.ObjectId] = null;
            }
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
